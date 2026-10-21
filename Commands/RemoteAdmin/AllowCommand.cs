using System;
using Clearance.Discord;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

[CommandHandler(typeof(ClearanceCommand))]
public class AllowCommand : ICommand
{
    public string Command => "allow";
    public string[] Aliases => [];
    public string Description => "let a player skip the check";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.LongTermBanning, out response)) return false;
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (!Cmd.TryTarget(arguments, out var id, out var reason))
        {
            response = "usage: clearance allow <user id> [reason]";
            return false;
        }

        db.AddToAllowlist(id, Cmd.Who(sender), reason);
        response = $"{id} is allowlisted.";
        Cmd.Discord?.Post("allowlisted", $"{Webhook.Link(id)} by {Cmd.Who(sender)}{(reason is null ? "" : ": " + reason)}", Webhook.Green);
        return true;
    }
}
