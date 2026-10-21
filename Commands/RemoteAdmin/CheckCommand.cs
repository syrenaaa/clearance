using System;
using System.Linq;
using System.Text;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

public class CheckCommand : ICommand
{
    public string Command => "check";
    public string[] Aliases => [];
    public string Description => "show what clearance knows about a player";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.PlayerSensitiveDataAccess, out response)) return false;
        if (!Cmd.Ready(out var db, out var cfg, out response)) return false;
        if (!Cmd.TryTarget(arguments, out var id, out _)) { response = "usage: clearance check <user id>"; return false; }

        var sb = new StringBuilder(id).Append('\n');
        sb.Append("allowlisted: ").Append(db.IsAllowlisted(id) ? "yes" : "no").Append('\n');
        if (db.GetActiveSponsorship(id) is { } s)
            sb.Append($"sponsored by {s.SponsorId} until {Cmd.Date(s.ExpiresAt)}\n");

        var guests = db.GetActiveGuests(id);
        var strikes = db.GetActiveStrikeWeight(id, TimeSpan.FromDays(cfg.StrikeDecayDays));
        sb.Append($"sponsoring {guests.Count}: {string.Join(", ", guests.Select(g => g.GuestId))}\n");
        sb.Append($"strikes: {strikes}/{cfg.MaxStrikeWeight}\n");

        foreach (var p in db.GetPunishments(id))
            sb.Append($"ban #{p.Id} {Cmd.Date(p.At)} by {p.IssuedBy}: {p.Reason}{(p.PardonedAt != 0 ? " (pardoned)" : "")}\n");

        foreach (var d in db.GetRecentDecisions(5, id))
            sb.Append($"{Cmd.Date(d.At)} {d.Outcome} score {d.Score}{(d.Enforced ? " [rejected]" : "")} {d.Breakdown}\n");

        response = sb.ToString().TrimEnd();
        return true;
    }
}
