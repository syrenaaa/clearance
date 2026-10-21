using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace Clearance.Persist;

public class Database : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly object _gate = new(); // only for the read-modify-write bits

    private ILiteCollection<PlayerRecord> Players => _db.GetCollection<PlayerRecord>("players");
    private ILiteCollection<SteamSnapshot> Steam => _db.GetCollection<SteamSnapshot>("steam");
    private ILiteCollection<AllowlistEntry> Allowlist => _db.GetCollection<AllowlistEntry>("allowlist");
    private ILiteCollection<DecisionEntry> Decisions => _db.GetCollection<DecisionEntry>("decisions", BsonAutoId.Int32);
    private ILiteCollection<Sponsorship> Sponsorships => _db.GetCollection<Sponsorship>("sponsorships", BsonAutoId.Int32);
    private ILiteCollection<Punishment> Punishments => _db.GetCollection<Punishment>("punishments", BsonAutoId.Int32);
    private ILiteCollection<Strike> Strikes => _db.GetCollection<Strike>("strikes", BsonAutoId.Int32);
    private ILiteCollection<TrackingConsent> Consents => _db.GetCollection<TrackingConsent>("consents");
    private ILiteCollection<OutboxItem> Outbox => _db.GetCollection<OutboxItem>("outbox", BsonAutoId.Int32);
    private ILiteCollection<Meta> Metas => _db.GetCollection<Meta>("meta");

    public string Folder { get; }

    public Database(string path)
    {
        Folder = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(Folder);

        // LiteDB caches a type's mapper before filling it in, so two threads touching a type for the first
        // time can see it half built ("Member X not found"). build them all now, while only one thread runs
        foreach (var t in new[]
                 {
                     typeof(PlayerRecord), typeof(SteamSnapshot), typeof(AllowlistEntry), typeof(DecisionEntry),
                     typeof(Sponsorship), typeof(Punishment), typeof(Strike), typeof(TrackingConsent),
                     typeof(OutboxItem), typeof(Meta),
                 })
            BsonMapper.Global.ToDocument(t, Activator.CreateInstance(t));

        _db = new LiteDatabase(path);

        Decisions.EnsureIndex(x => x.UserId);
        Sponsorships.EnsureIndex(x => x.SponsorId);
        Sponsorships.EnsureIndex(x => x.GuestId);
        Punishments.EnsureIndex(x => x.UserId);
        Strikes.EnsureIndex(x => x.SponsorId);
        Strikes.EnsureIndex(x => x.PunishmentId);
        Decisions.EnsureIndex(x => x.At);
        Punishments.EnsureIndex(x => x.At);
    }

    // ---- players ----

    public void RecordJoin(string userId)
    {
        var id = Norm(userId);
        lock (_gate)
        {
            var now = Now();
            var p = Players.FindById(id) ?? new PlayerRecord { UserId = id, FirstSeen = now };
            p.LastSeen = now;
            p.Joins++;
            Players.Upsert(p);
        }
    }

    public void AddServerTime(string userId, long seconds)
    {
        if (seconds <= 0) return;
        var id = Norm(userId);
        lock (_gate)
        {
            var now = Now();
            var p = Players.FindById(id) ?? new PlayerRecord { UserId = id, FirstSeen = now };
            p.ServerSeconds += seconds;
            p.LastSeen = now;
            Players.Upsert(p);
        }
    }

    public long GetServerSeconds(string userId) =>
        Players.FindById(Norm(userId))?.ServerSeconds ?? 0;

    public bool HasJoinedBefore(string userId) => Players.FindById(Norm(userId)) is { Joins: > 0 };

    // do not track: drop everything that isn't moderation. bans, strikes, sponsorships and
    // the allowlist stay, those are staff actions
    public void Forget(string userId)
    {
        var id = Norm(userId);
        Players.Delete(id);
        Steam.Delete(id);
        Decisions.DeleteMany(x => x.UserId == id);
    }

    public void GiveConsent(string userId) =>
        Consents.Upsert(new TrackingConsent { UserId = Norm(userId), GivenAt = Now() });

    public bool HasConsent(string userId) => Consents.FindById(Norm(userId)) is not null;

    // withdrawing also deletes what was tracked under the consent
    public bool WithdrawConsent(string userId)
    {
        if (!Consents.Delete(Norm(userId))) return false;
        Forget(userId);
        return true;
    }

    // ---- steam cache ----

    // null = not cached, or older than maxAge
    public SteamSnapshot? GetCachedSteam(string userId, TimeSpan maxAge) =>
        GetLastSteam(userId) is { } s && s.FetchedAt >= Now() - (long)maxAge.TotalSeconds ? s : null;

    // any age, for when steam is down
    public SteamSnapshot? GetLastSteam(string userId) => Steam.FindById(Norm(userId));

    public void SaveSteam(string userId, SteamSnapshot snapshot)
    {
        snapshot.UserId = Norm(userId);
        snapshot.FetchedAt = Now();
        Steam.Upsert(snapshot);
    }

    // ---- allowlist (staff overrides) ----

    public void AddToAllowlist(string userId, string addedBy, string? reason) =>
        Allowlist.Upsert(new AllowlistEntry { UserId = Norm(userId), AddedBy = addedBy, Reason = reason, AddedAt = Now() });

    public bool RemoveFromAllowlist(string userId) => Allowlist.Delete(Norm(userId));

    public bool IsAllowlisted(string userId) => Allowlist.FindById(Norm(userId)) is not null;

    public List<AllowlistEntry> GetAllowlist() =>
        Allowlist.Query().OrderByDescending(x => x.AddedAt).ToList();

    // ---- decision log ----

    public void LogDecision(string userId, int score, string outcome, string? breakdown, bool enforced) =>
        Decisions.Insert(new DecisionEntry
        {
            UserId = Norm(userId), At = Now(), Score = score,
            Outcome = outcome, Breakdown = breakdown, Enforced = enforced
        });

    // newest first; pass a userId to see one player
    public List<DecisionEntry> GetRecentDecisions(int limit = 50, string? userId = null)
    {
        var q = Decisions.Query();
        if (userId is not null)
        {
            var id = Norm(userId);
            q = q.Where(x => x.UserId == id);
        }
        return q.OrderByDescending(x => x.Id).Limit(limit).ToList();
    }

    public Dictionary<string, int> CountOutcomesSince(long since) =>
        Decisions.Find(x => x.At >= since).GroupBy(x => x.Outcome).ToDictionary(g => g.Key, g => g.Count());

    // ---- tuning ----

    // what scoring needs, per player with a steam lookup: the lookup, time here, and unpardoned bans here
    public List<(SteamSnapshot Steam, long ServerSeconds, int Bans)> GetScoringData()
    {
        var seconds = Players.FindAll().ToDictionary(x => x.UserId, x => x.ServerSeconds);
        var bans = Punishments.Find(x => x.PardonedAt == 0).GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Count());
        return Steam.FindAll()
            .Select(s => (s, seconds.TryGetValue(s.UserId, out var t) ? t : 0, bans.TryGetValue(s.UserId, out var b) ? b : 0))
            .ToList();
    }

    // ---- sponsorships ----

    // window / caps / strike limit come from the plugin config. Error text is safe to show the player.
    public (bool Ok, string? Error) TrySponsor(string sponsorId, string guestId, TimeSpan window,
        int maxActive, int maxStrikeWeight, TimeSpan strikeDecay, TimeSpan minPlayTime)
    {
        var sponsor = Norm(sponsorId);
        var guest = Norm(guestId);

        lock (_gate)
        {
            var now = Now();

            if (sponsor == guest)
                return (false, "You can't sponsor yourself.");
            // keeps fresh accounts from vouching for their own alts
            if ((Players.FindById(sponsor)?.ServerSeconds ?? 0) < (long)minPlayTime.TotalSeconds)
                return (false, $"You need {minPlayTime.TotalHours:0.#} hours played here before you can sponsor anyone.");
            if (ActiveSponsorship(sponsor, now) is not null)
                return (false, "You're still inside someone else's sponsorship window, so you can't sponsor anyone yet.");
            if (StrikeWeight(sponsor, strikeDecay, now) >= maxStrikeWeight)
                return (false, "You have too many recent strikes to sponsor anyone right now.");
            if (ActiveGuests(sponsor, now).Count() >= maxActive)
                return (false, "All of your sponsor slots are in use.");
            if (ActiveSponsorship(guest, now) is not null)
                return (false, "That player already has a sponsor.");

            Sponsorships.Insert(new Sponsorship
            {
                SponsorId = sponsor, GuestId = guest,
                CreatedAt = now, ExpiresAt = now + (long)window.TotalSeconds
            });
            return (true, null);
        }
    }

    // punishments after a revoke don't strike the sponsor
    public bool RevokeSponsorship(string sponsorId, string guestId)
    {
        var sponsor = Norm(sponsorId);
        var guest = Norm(guestId);
        lock (_gate)
        {
            var now = Now();
            var s = Sponsorships.Query()
                .Where(x => x.SponsorId == sponsor && x.GuestId == guest && x.RevokedAt == 0 && x.ExpiresAt > now)
                .FirstOrDefault();
            if (s is null) return false;

            s.RevokedAt = now;
            Sponsorships.Update(s);
            return true;
        }
    }

    // null = this guest has no active sponsor
    public Sponsorship? GetActiveSponsorship(string guestId) => ActiveSponsorship(Norm(guestId), Now());

    public List<Sponsorship> GetActiveGuests(string sponsorId) => ActiveGuests(Norm(sponsorId), Now()).ToList();

    // ---- punishments + strikes ----

    public List<Punishment> GetPunishments(string userId)
    {
        var id = Norm(userId);
        return Punishments.Query().Where(x => x.UserId == id).OrderByDescending(x => x.Id).ToList();
    }

    // Record a confirmed ban (not a kick). If the player has an active sponsor, the sponsor gets a strike
    // with weight = severity. Keep punishment.Id if you want to pardon it later.
    public (Punishment punishment, Strike? strike) RecordPunishment(string userId, int severity, string? reason, string issuedBy,
        long duration = 0)
    {
        var id = Norm(userId);
        lock (_gate)
        {
            using var tx = new Tx(_db);
            var now = Now();
            var punishment = new Punishment
            {
                UserId = id, At = now, Severity = Math.Max(1, severity),
                Reason = reason, IssuedBy = issuedBy, Duration = Math.Max(0, duration)
            };
            punishment.Id = Punishments.Insert(punishment).AsInt32;

            if (ActiveSponsorship(id, now) is not { } sponsorship)
            {
                tx.Commit();
                return (punishment, null);
            }

            var strike = new Strike
            {
                SponsorId = sponsorship.SponsorId, GuestId = id,
                SponsorshipId = sponsorship.Id, PunishmentId = punishment.Id,
                Weight = punishment.Severity, CreatedAt = now
            };
            strike.Id = Strikes.Insert(strike).AsInt32;
            tx.Commit();
            return (punishment, strike);
        }
    }

    // reverses the strikes the punishment caused; returns how many, or -1 if it doesn't exist
    public int PardonPunishment(int punishmentId)
    {
        lock (_gate)
        {
            using var tx = new Tx(_db);
            if (Punishments.FindById(punishmentId) is not { } p) return -1;

            var now = Now();
            if (p.PardonedAt == 0)
            {
                p.PardonedAt = now;
                Punishments.Update(p);
            }

            var open = Strikes.Query().Where(x => x.PunishmentId == punishmentId && x.PardonedAt == 0).ToList();
            foreach (var s in open)
            {
                s.PardonedAt = now;
                Strikes.Update(s);
            }
            tx.Commit();
            return open.Count;
        }
    }

    // strikes that still count: not pardoned, not older than the decay
    public int GetActiveStrikeWeight(string sponsorId, TimeSpan decay) =>
        StrikeWeight(Norm(sponsorId), decay, Now());

    public Punishment? GetPunishment(int punishmentId) => Punishments.FindById(punishmentId);

    public void SetCentralReportId(int punishmentId, string reportId)
    {
        lock (_gate)
        {
            if (Punishments.FindById(punishmentId) is not { } p) return;
            p.CentralReportId = reportId;
            Punishments.Update(p);
        }
    }

    // ---- daily stats (central server) ----

    public List<DecisionEntry> GetDecisionsBetween(long from, long to) =>
        Decisions.Find(x => x.At >= from && x.At < to).ToList();

    public List<Punishment> GetPunishmentsBetween(long from, long to) =>
        Punishments.Find(x => x.At >= from && x.At < to).ToList();

    // the score at their last scored check before `before`, null if there's none (or it was forgotten)
    public int? GetLastScore(string userId, long before, IReadOnlyCollection<string> scoredOutcomes)
    {
        var id = Norm(userId);
        return Decisions.Find(x => x.UserId == id && x.At <= before)
            .Where(x => scoredOutcomes.Contains(x.Outcome))
            .OrderByDescending(x => x.Id)
            .Select(x => (int?)x.Score)
            .FirstOrDefault();
    }

    // ---- outbox (central server) ----

    public void Enqueue(string kind, string reference) =>
        Outbox.Insert(new OutboxItem { Kind = kind, Ref = reference });

    public bool IsQueued(string kind, string reference) => Outbox.Exists(x => x.Kind == kind && x.Ref == reference);

    // oldest first. the queue keeps order, so a pardon never goes out before its ban
    public OutboxItem? PeekOutbox() => Outbox.Query().OrderBy(x => x.Id).FirstOrDefault();

    public void Retry(OutboxItem item, long nextAt)
    {
        item.Attempts++;
        item.NextAt = nextAt;
        Outbox.Update(item);
    }

    public void Done(OutboxItem item) => Outbox.Delete(item.Id);

    // ---- meta ----

    public string? GetMeta(string key) => Metas.FindById(key)?.Value;

    public void SetMeta(string key, string value) => Metas.Upsert(new Meta { Key = key, Value = value });

    // random, made once per database. keeps central client refs unique even after the db is reset
    public string InstallId()
    {
        lock (_gate)
        {
            if (GetMeta("install") is { } id) return id;
            id = Guid.NewGuid().ToString("N");
            SetMeta("install", id);
            return id;
        }
    }

    // ---- helpers ----

    private Sponsorship? ActiveSponsorship(string guest, long now) =>
        Sponsorships.Query()
            .Where(x => x.GuestId == guest && x.RevokedAt == 0 && x.ExpiresAt > now)
            .FirstOrDefault();

    private ILiteQueryable<Sponsorship> ActiveGuests(string sponsor, long now) =>
        Sponsorships.Query()
            .Where(x => x.SponsorId == sponsor && x.RevokedAt == 0 && x.ExpiresAt > now);

    private int StrikeWeight(string sponsor, TimeSpan decay, long now)
    {
        var cutoff = now - (long)decay.TotalSeconds;
        return Strikes.Query()
            .Where(x => x.SponsorId == sponsor && x.PardonedAt == 0 && x.CreatedAt >= cutoff)
            .ToList()
            .Sum(x => x.Weight);
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // "76561198439176472@steam": trim + lowercase so "@Steam" can't make duplicates
    internal static string Norm(string id) =>
        string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("empty user id") : id.Trim().ToLowerInvariant();

    public void Dispose() => _db.Dispose();

    // rolls back on Dispose unless Commit() was called, so exceptions and early returns undo partial writes
    private sealed class Tx(LiteDatabase db) : IDisposable
    {
        private readonly bool _owns = db.BeginTrans();
        private bool _done;

        public void Commit()
        {
            if (_owns && !_done) db.Commit();
            _done = true;
        }

        public void Dispose()
        {
            if (_owns && !_done) db.Rollback();
        }
    }
}