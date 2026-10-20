using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clearance.Discord;
using Clearance.Persist;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;

namespace Clearance;

internal sealed class ClearanceEvents(Database db, ClearanceConfig cfg, Webhook? discord)
{
    private const int BanSeverity = 2;

    private readonly Dictionary<Player, long> _joinedAt = [];
    private readonly ConcurrentQueue<(string Text, bool Staff)> _notes = new();
    private readonly ConcurrentDictionary<string, SteamSnapshot> _dntStore = new();

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // for other threads (the discord loop): printed on the next join/leave
    public void Note(string text) => _notes.Enqueue((text, false));

    public void Register()
    {
        PlayerEvents.PreAuthenticating += OnPreAuthenticating;
        PlayerEvents.Joined += OnJoined;
        PlayerEvents.Left += OnLeft;
        PlayerEvents.Banned += OnBanned;
    }

    public void Unregister()
    {
        PlayerEvents.PreAuthenticating -= OnPreAuthenticating;
        PlayerEvents.Joined -= OnJoined;
        PlayerEvents.Left -= OnLeft;
        PlayerEvents.Banned -= OnBanned;

        foreach (var player in _joinedAt.Keys.ToList()) SaveTime(player);
        FlushNotes();
    }

    // ---- network thread --------------------------------
    // PreAuthenticating runs on a different thread with no apis. it queues notes instead
    // and gets printed from main thread
    private void OnPreAuthenticating(PlayerPreAuthenticatingEventArgs ev)
    {
        var id = ev.UserId;

        try
        {
            // CSG 7.1.1 / 7.2.1: plugins must respect bypass flags and northwood staff get past any
            // restriction. both the id and the flags come from central auth, so they can't be faked
            const CentralAuthPreauthFlags bypass =
                CentralAuthPreauthFlags.NorthwoodStaff | CentralAuthPreauthFlags.IgnoreBans | CentralAuthPreauthFlags.IgnoreWhitelist;
            if (UserIds.IsNorthwood(id) || (ev.Flags & bypass) != 0)
            {
                db.LogDecision(id, 0, "Bypass", (ev.Flags & bypass).ToString(), false);
                db.RecordJoin(id);
                return;
            }

            if (db.IsAllowlisted(id))
            {
                db.LogDecision(id, 0, "Allowlisted", null, false);
                db.RecordJoin(id);
                return;
            }

            if (db.GetActiveSponsorship(id) is { } sponsorship)
            {
                db.LogDecision(id, 0, "Sponsored", $"by {sponsorship.SponsorId}", false);
                db.RecordJoin(id);
                return;
            }

            SteamSnapshot? steam = null;
            if (UserIds.IsSteam(id)) // discord and other ids have nothing to look up
            {
                steam = db.GetCachedSteam(id, TimeSpan.FromHours(cfg.SteamCacheHours)) ?? DntSteam(id);
                if (steam is null)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.SteamTimeoutSeconds));
                        steam = Task.Run(() => SteamClient.FetchAsync(id, cfg.SteamApiKey, cfg.ScpslAppId, cts.Token), cts.Token)
                                    .GetAwaiter().GetResult();
                        db.SaveSteam(id, steam);
                    }
                    catch (Exception ex)
                    {
                        db.LogDecision(id, 0, "steam-error", ex.GetType().Name, false);
                        _notes.Enqueue(($"Clearance: steam lookup failed for {id} ({ex.GetType().Name})", false));

                        // an old lookup beats no lookup, so score with it
                        steam = db.GetLastSteam(id);
                        if (steam is null)
                        {
                            if (!cfg.AllowNewWhenSteamIsDown && !db.HasJoinedBefore(id))
                            {
                                db.LogDecision(id, 0, "SteamDownNew", null, cfg.Enforce);
                                _notes.Enqueue(($"Clearance: {id} is new and steam is down{(cfg.Enforce ? " [rejected]" : "")}", false));
                                discord?.Post(cfg.Enforce ? "rejected: steam is down" : "would be rejected: steam is down",
                                    $"{Webhook.Link(id)} is new and couldn't be checked", cfg.Enforce ? Webhook.Red : Webhook.Orange);
                                if (cfg.Enforce)
                                {
                                    ev.RejectCustom(cfg.SteamDownKickMessage);
                                    return;
                                }
                            }

                            if (cfg.AllowWhenSteamIsDown)
                            {
                                db.RecordJoin(id);
                                return;
                            }
                            // otherwise fall through and score them like a private profile
                        }
                        else
                        {
                            var age = TimeSpan.FromSeconds(Now() - steam.FetchedAt);
                            _notes.Enqueue(($"Clearance: using {age.TotalHours:0}h old steam data for {id}", false));
                        }
                    }
                }
            }

            var (score, instantDeny, breakdown) =
                Score.Compute(cfg, steam, db.GetServerSeconds(id), Now(), UserIds.DiscordCreated(id));
            var verdict = Score.Decide(cfg, score, instantDeny);
            var enforced = cfg.Enforce && verdict == Verdict.Deny;
            db.LogDecision(id, score, verdict.ToString(), breakdown, enforced);

            if (verdict != Verdict.Allow)
            {
                _notes.Enqueue(($"Clearance: {id} {verdict} (score {score}: {breakdown}){(enforced ? " [rejected]" : "")}",
                                verdict == Verdict.Flag));
                discord?.Post(
                    verdict == Verdict.Flag ? "flagged" : enforced ? "rejected" : "would be rejected (log-only)",
                    $"{Webhook.Link(id)}\nscore {score}: {breakdown}",
                    verdict == Verdict.Flag || !enforced ? Webhook.Orange : Webhook.Red);
            }

            if (enforced)
            {
                ev.RejectCustom(Score.DenyMessage(cfg, score, instantDeny, breakdown));
                return;
            }
            db.RecordJoin(id);
        }
        catch (Exception ex)
        {
            // never lock everybody out because of a bug: let them in, leave a note
            _notes.Enqueue(($"Clearance: error while checking {id}: {ex.GetType().Name}: {ex.Message}", false));
        }
    }

    // ---- main thread -----------------------------------------------------

    private void OnJoined(PlayerJoinedEventArgs ev)
    {
        if (ev.Player.IsPlayer) _joinedAt[ev.Player] = Now();
        FlushNotes();
    }

    private void OnLeft(PlayerLeftEventArgs ev)
    {
        SaveTime(ev.Player);
        FlushNotes();
    }

    private void OnBanned(PlayerBannedEventArgs ev)
    {
        if (string.IsNullOrWhiteSpace(ev.PlayerId)) return;
        var issuer = ev.Issuer?.UserId is { Length: > 0 } who ? who : "console";
        var (_, strike) = db.RecordPunishment(ev.PlayerId, BanSeverity, ev.Reason, issuer);
        if (strike is null) return;
        Logger.Info($"Clearance: {ev.PlayerId} was banned, sponsor {strike.SponsorId} got a strike.");
        discord?.Post("sponsor strike",
            $"{Webhook.Link(strike.SponsorId)} got a strike because their guest {Webhook.Link(ev.PlayerId)} was banned by {issuer}",
            Webhook.Red);
    }

    private void SaveTime(Player player)
    {
        if (!_joinedAt.TryGetValue(player, out var since)) return;
        _joinedAt.Remove(player);
        // UserId can still be empty right at join, so it's read here instead
        var id = player.UserId;
        if (string.IsNullOrWhiteSpace(id)) return;

        // do not track only arrives with authentication, after pre-auth already stored things.
        // so the session is checked like everyone else's and wiped once they leave
        if (player.DoNotTrack)
        {
            if (db.GetLastSteam(id) is { } steam) _dntStore[steam.UserId] = steam;
            db.Forget(id);
        }
        else db.AddServerTime(id, Now() - since);
    }

    // called from pre-auth too, so only touches the concurrent dictionary
    private SteamSnapshot? DntSteam(string id)
    {
        var oldest = Now() - cfg.SteamCacheHours * 3600L;
        foreach (var old in _dntStore.Where(x => x.Value.FetchedAt < oldest).ToList())
            _dntStore.TryRemove(old.Key, out _);
        return _dntStore.TryGetValue(Database.Norm(id), out var s) ? s : null;
    }

    private void FlushNotes()
    {
        while (_notes.TryDequeue(out var note))
        {
            Logger.Info(note.Text);
            if (!note.Staff) continue;
            foreach (var staff in Player.ReadyList.Where(p => p.RemoteAdminAccess))
                staff.SendBroadcast(note.Text, 8);
        }
    }
}
