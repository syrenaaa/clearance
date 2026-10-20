using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;

namespace Clearance.Discord;

// posts embeds to a discord webhook from a background loop, so neither pre-auth nor the main
// thread ever waits on discord. if discord is slow or down, new posts are dropped instead of piling up
public sealed class Webhook : IDisposable
{
    public const int Red = 0xE74C3C, Orange = 0xE67E22, Green = 0x2ECC71, Blue = 0x3498DB, Grey = 0x95A5A6;

    private const int MaxQueued = 50;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _url;
    private readonly Action<string> _log; // must be safe to call from any thread
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private bool _failing;

    public Webhook(string url, Action<string> log)
    {
        (_url, _log) = (url, log);
        Task.Run(Loop);
    }

    // safe from any thread
    public void Post(string title, string text, int color)
    {
        if (_queue.Count >= MaxQueued) return;

        var embed = new BsonDocument
        {
            ["title"] = Cut(title, 256),
            ["description"] = Cut(text, 4096),
            ["color"] = color,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
        };
        var payload = new BsonDocument
        {
            ["embeds"] = new BsonArray { embed },
            ["allowed_mentions"] = new BsonDocument { ["parse"] = new BsonArray() }, // staff text can't ping anyone
        };
        _queue.Enqueue(JsonSerializer.Serialize(payload));
        _signal.Release();
    }

    // steam ids link to the profile, everything else is shown as is
    public static string Link(string userId) =>
        UserIds.IsSteam(userId)
            ? $"[{userId}](https://steamcommunity.com/profiles/{userId.Substring(0, userId.IndexOf('@'))})"
            : $"`{userId}`";

    private async Task Loop()
    {
        var ct = _stop.Token;
        try
        {
            while (true)
            {
                if (!_queue.TryPeek(out var json))
                {
                    await _signal.WaitAsync(ct);
                    continue;
                }

                if (await Send(json, ct) is { } retryAfter)
                {
                    await Task.Delay(retryAfter, ct); // rate limited, try the same post again
                    continue;
                }
                _queue.TryDequeue(out _);
                await Task.Delay(500, ct); // webhooks allow about 5 posts per 2 seconds
            }
        }
        catch (OperationCanceledException) { }
    }

    // null = done with this post (sent, or given up on). otherwise: wait this long and retry
    private async Task<TimeSpan?> Send(string json, CancellationToken ct)
    {
        string? error;
        try
        {
            using var res = await Http.PostAsync(_url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
            if ((int)res.StatusCode == 429) return res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
            error = res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            error = ex.GetType().Name;
        }

        // one line when it breaks and one when it recovers, not one per post
        if (error is not null && !_failing) _log($"Clearance: discord webhook failed ({error}), dropping posts until it works again");
        if (error is null && _failing) _log("Clearance: discord webhook works again");
        _failing = error is not null;
        return null;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    public void Dispose() => _stop.Cancel();
}
