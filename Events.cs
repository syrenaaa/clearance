using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clearance.Persist;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;

namespace Clearance;

internal sealed class ClearanceEvents(Database db, ClearanceConfig cfg)
{
    private const int BanSeverity = 2;

    private readonly Dictionary<Player, long> _joinedAt = [];
    private readonly ConcurrentQueue<(string Text, bool Staff)> _notes = new();

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

    // ---- the actual gate (network thread) --------------------------------
    // PreAuthenticating runs on a different thread with no apis. it queues notes instead
    // and gets printed from main thread
    private void OnPreAuthenticating(PlayerPreAuthenticatingEventArgs ev)
    {
        var id = ev.UserId;

        try
        {
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
                steam = db.GetCachedSteam(id, TimeSpan.FromHours(cfg.SteamCacheHours));
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
                _notes.Enqueue(($"Clearance: {id} {verdict} (score {score}: {breakdown}){(enforced ? " [rejected]" : "")}",
                                verdict == Verdict.Flag));

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
        if (strike is not null)
            Logger.Info($"Clearance: {ev.PlayerId} was banned, sponsor {strike.SponsorId} got a strike.");
    }

    private void SaveTime(Player player)
    {
        if (!_joinedAt.TryGetValue(player, out var since)) return;
        _joinedAt.Remove(player);
        // UserId can still be empty right at join, so it's read here instead
        var id = player.UserId;
        if (!string.IsNullOrWhiteSpace(id)) db.AddServerTime(id, Now() - since);
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
