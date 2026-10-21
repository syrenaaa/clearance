using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clearance.Persist;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

// for tuning: re-scores everyone clearance has steam data for with the *current* config,
// and shows how many of each verdict later got banned here
[CommandHandler(typeof(ClearanceCommand))]
public class StatsCommand : ICommand
{
    public string Command => "stats";
    public string[] Aliases => [];
    public string Description => "how the current config would score known players";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.PlayerSensitiveDataAccess, out response)) return false;
        if (!Cmd.Ready(out var db, out var cfg, out response)) return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sb = new StringBuilder();

        var recent = db.CountOutcomesSince(now - 7 * 86400);
        sb.Append($"checks in the last 7 days: {recent.Values.Sum()}\n");
        foreach (var x in recent.OrderByDescending(x => x.Value))
            sb.Append($"  {x.Key}: {x.Value}\n");

        var players = Rescore(db, cfg, now);
        sb.Append($"\nthe current config on {players.Count} known steam players:\n");
        foreach (var verdict in new[] { Verdict.Allow, Verdict.Flag, Verdict.Deny })
        {
            var group = players.Where(p => p.Verdict == verdict).ToList();
            sb.Append($"  {verdict}: {group.Count}, {group.Count(p => p.Bans > 0)} of them banned here\n");
        }

        response = sb.ToString().TrimEnd();
        return true;
    }

    internal static List<(SteamSnapshot Steam, long ServerSeconds, int Bans, int Score, Verdict Verdict, string Breakdown)>
        Rescore(Database db, ClearanceConfig cfg, long now) =>
        db.GetScoringData().Select(d =>
        {
            var (score, instantDeny, breakdown) = Score.Compute(cfg, d.Steam, d.ServerSeconds, now);
            return (d.Steam, d.ServerSeconds, d.Bans, score, Score.Decide(cfg, score, instantDeny), breakdown);
        }).ToList();
}
