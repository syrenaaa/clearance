using System;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

[CommandHandler(typeof(ClearanceCommand))]
public class RevokeCommand : ICommand
{
    public string Command => "revoke";
    public string[] Aliases => [];
    public string Description => "end a guest's sponsorship";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.LongTermBanning, out response)) return false;
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (!Cmd.TryTarget(arguments, out var guest, out _)) { response = "usage: clearance revoke <guest user id>"; return false; }

        if (db.GetActiveSponsorship(guest) is not { } s || !db.RevokeSponsorship(s.SponsorId, guest))
        {
            response = $"{guest} has no sponsor.";
            return false;
        }
        response = $"revoked {s.SponsorId}'s sponsorship of {guest}.";
        return true;
    }
}
