using System;
using System.Linq;
using Clearance.Discord;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

[CommandHandler(typeof(ClearanceCommand))]
public class PardonCommand : ICommand
{
    public string Command => "pardon";
    public string[] Aliases => [];
    public string Description => "pardon a ban by its number, which removes the sponsor's strike";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.LongTermBanning, out response)) return false;
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (arguments.Count != 1 || !int.TryParse(arguments.First().TrimStart('#'), out var punishmentId))
        {
            response = "usage: clearance pardon <ban number from clearance check>";
            return false;
        }

        var strikes = db.PardonPunishment(punishmentId);
        response = strikes < 0 ? $"there's no ban #{punishmentId}." : $"pardoned ban #{punishmentId}, removed {strikes} strike(s).";
        if (strikes >= 0) Cmd.Discord?.Post("ban pardoned", $"ban #{punishmentId} by {Cmd.Who(sender)}, {strikes} strike(s) removed", Webhook.Green);
        return strikes >= 0;
    }
}
