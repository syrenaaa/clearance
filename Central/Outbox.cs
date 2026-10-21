using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clearance.Persist;
using LiteDB;

namespace Clearance.Central;

// sends bans, pardons and daily stats to the central server from a background loop. the queue lives
// in the database, so a restart or an outage only delays things. oldest first, one at a time, so a
// pardon never goes out before its ban
public sealed class Outbox : IDisposable
{
    private const long MaxBanSeconds = 3_155_760_000; // spec max, 100 years
    private const int StatsBackfillDays = 7;

    private enum Outcome { Done, Drop, Retry, Unauthorized }

    private readonly Database _db;
    private readonly ClearanceConfig _cfg;
    private readonly CentralClient _client;
    private readonly string _version;
    private readonly Action<string> _log; // must be safe to call from any thread
    private readonly string _install;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private bool _failing;

    public Outbox(Database db, ClearanceConfig cfg, CentralClient client, string version, Action<string> log)
    {
        (_db, _cfg, _client, _version, _log) = (db, cfg, client, version, log);
        _install = db.InstallId();
        Task.Run(Loop);
    }

    // ---- called from the game ----

    public void Banned(Punishment p)
    {
        if (!_cfg.ShareBans || p.Duration < _cfg.ShareMinBanHours * 3600L) return;
        _db.Enqueue("ban", p.Id.ToString());
        _signal.Release();
    }

    // only bans that went out (or are still waiting to) need a pardon
    public void Pardoned(int punishmentId)
    {
        var id = punishmentId.ToString();
        if (_db.GetPunishment(punishmentId)?.CentralReportId is null && !_db.IsQueued("ban", id)) return;
        _db.Enqueue("pardon", id);
        _signal.Release();
    }

    // ---- loop ----

    private async Task Loop()
    {
        var ct = _stop.Token;
        try
        {
            while (true)
            {
                try
                {
                    QueueStatsDays();
                    await Drain(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log($"Clearance: central outbox error: {ex.GetType().Name}: {ex.Message}");
                }
                await _signal.WaitAsync(TimeSpan.FromMinutes(1), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // every finished utc day since the last one, a week back at most
    private void QueueStatsDays()
    {
        if (!_cfg.ShareStats) return;
        var today = DailyStats.DayStart(DailyStats.Day(Now()));
        var first = today - StatsBackfillDays * 86400L;
        if (_db.GetMeta("statsDay") is { } last) first = Math.Max(first, DailyStats.DayStart(last) + 86400);
        for (var t = first; t < today; t += 86400)
        {
            var day = DailyStats.Day(t);
            if (!_db.IsQueued("stats", day)) _db.Enqueue("stats", day);
            _db.SetMeta("statsDay", day);
        }
    }

    private async Task Drain(CancellationToken ct)
    {
        while (_db.PeekOutbox() is { } item && item.NextAt <= Now())
        {
            var (outcome, why) = await Send(item, ct);
            Report(outcome, item, why);
            if (outcome is Outcome.Done or Outcome.Drop)
            {
                _db.Done(item);
                continue;
            }
            // unauthorized = wrong or revoked key, nothing changes until the config does
            var wait = outcome == Outcome.Unauthorized ? 3600 : Math.Min(3600, 30L << Math.Min(item.Attempts, 7));
            _db.Retry(item, Now() + wait);
            return;
        }
    }

    private async Task<(Outcome, string?)> Send(OutboxItem item, CancellationToken ct)
    {
        try
        {
            switch (item.Kind)
            {
                case "ban":
                {
                    if (_db.GetPunishment(int.Parse(item.Ref)) is not { } p) return (Outcome.Drop, null);
                    var (status, body) = await _client.SendAsync(HttpMethod.Post, "v1/reports", BanBody(p), ct);
                    if (Ok(status) && Str(body, "reportId") is { } rid) _db.SetCentralReportId(p.Id, rid);
                    return Classify(status, body);
                }
                case "pardon":
                {
                    // its ban was dropped, so there's nothing to pardon over there
                    if (_db.GetPunishment(int.Parse(item.Ref)) is not { CentralReportId: { } rid } p) return (Outcome.Drop, null);
                    var body = new BsonDocument
                    {
                        ["kind"] = "pardon", ["clientRef"] = Ref(p), ["reportId"] = rid,
                        ["at"] = CentralClient.Num(p.PardonedAt > 0 ? p.PardonedAt : Now()),
                    };
                    var (status, res) = await _client.SendAsync(HttpMethod.Post, "v1/reports", body, ct);
                    return Classify(status, res);
                }
                case "stats":
                {
                    var body = DailyStats.Build(_db, _cfg, _version, item.Ref);
                    var (status, res) = await _client.SendAsync(HttpMethod.Put, $"v1/stats/{item.Ref}", body, ct);
                    return Classify(status, res);
                }
                default:
                    return (Outcome.Drop, $"unknown kind {item.Kind}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (Outcome.Retry, ex.GetType().Name);
        }
    }

    private BsonDocument BanBody(Punishment p) => new()
    {
        ["kind"] = "ban",
        ["clientRef"] = Ref(p),
        ["userId"] = p.UserId,
        ["at"] = CentralClient.Num(p.At),
        ["duration"] = p.Duration >= MaxBanSeconds ? BsonValue.Null : CentralClient.Num(p.Duration),
        ["severity"] = Math.Min(5, Math.Max(1, p.Severity)),
        ["category"] = "other", // game bans only come with free text
    };

    // the install id keeps refs unique even if clearance.db gets reset and ids start at 1 again
    private string Ref(Punishment p) => $"{_install}:{p.Id}";

    private static (Outcome, string?) Classify(HttpStatusCode status, BsonValue? body)
    {
        var code = (int)status;
        if (Ok(status)) return (Outcome.Done, null);
        var why = Str(body, "error") is { } e ? $"HTTP {code} {e}" : $"HTTP {code}";
        if (code == 401) return (Outcome.Unauthorized, why);
        if (code == 429 || code >= 500) return (Outcome.Retry, why);
        return (Outcome.Drop, why); // the server won't take it, resending won't change that
    }

    private static bool Ok(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string? Str(BsonValue? body, string key) =>
        body is BsonDocument d && d.TryGetValue(key, out var v) && v.IsString ? v.AsString : null;

    // one line when it breaks and one when it recovers, plus one per dropped item
    private void Report(Outcome outcome, OutboxItem item, string? why)
    {
        if (outcome == Outcome.Drop && why is not null) _log($"Clearance: central server refused {item.Kind} {item.Ref} ({why}), dropped");
        var failing = outcome is Outcome.Retry or Outcome.Unauthorized;
        if (failing && !_failing)
            _log(outcome == Outcome.Unauthorized
                ? "Clearance: central server rejected CentralKey, check it. retrying hourly"
                : $"Clearance: central server unreachable ({why}), will retry");
        if (!failing && _failing) _log("Clearance: central server works again");
        _failing = failing;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public void Dispose() => _stop.Cancel();
}
