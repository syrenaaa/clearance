using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;

namespace Clearance.Central;

// what the central server knows about a player: running bans on other servers. facts, not verdicts
public sealed class NetworkFacts(int distinctServers, int activeBans)
{
    public int DistinctServers { get; } = distinctServers;
    public int ActiveBans { get; } = activeBans;
}

// http to the central server (openapi.yaml in clearance-central). bodies are LiteDB json, but
// LiteDB writes longs as {"$numberLong": ..}, so every number goes in as int or double (see Num)
public sealed class CentralClient
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, (NetworkFacts Facts, DateTime Until)> _cache = new();

    public CentralClient(string url, string key, TimeSpan timeout)
    {
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _timeout = timeout;
    }

    // pre-auth waits on this, so it never throws and gives up after the timeout. null = unknown, fail open
    public async Task<NetworkFacts?> LookupAsync(string userId)
    {
        var id = userId.Trim().ToLowerInvariant();
        if (_cache.TryGetValue(id, out var hit) && hit.Until > DateTime.UtcNow) return hit.Facts;
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var (status, body) = await SendAsync(HttpMethod.Get, $"v1/players/{Uri.EscapeDataString(id)}", null, cts.Token);
            if (status != HttpStatusCode.OK || body is not BsonDocument d) return null;
            var facts = new NetworkFacts(Int(d, "distinctServers"), Int(d, "activeBans"));
            if (_cache.Count > 10_000) _cache.Clear();
            _cache[id] = (facts, DateTime.UtcNow + CacheFor);
            return facts;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // throws on network errors / timeouts; callers decide what to retry
    public async Task<(HttpStatusCode Status, BsonValue? Body)> SendAsync(HttpMethod method, string path, BsonDocument? body,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync();
        BsonValue? parsed = null;
        try { parsed = text.Length > 0 ? JsonSerializer.Deserialize(text) : null; }
        catch (Exception) { /* not json, e.g. a proxy error page */ }
        return (res.StatusCode, parsed);
    }

    // exact up to 2^53, which unix seconds won't reach
    public static BsonValue Num(long n) => n is >= int.MinValue and <= int.MaxValue ? new BsonValue((int)n) : new BsonValue((double)n);

    private static int Int(BsonDocument d, string key) => d.TryGetValue(key, out var v) && v.IsNumber ? v.AsInt32 : 0;
}
