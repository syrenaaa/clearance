using System;
using Clearance.Discord;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

[CommandHandler(typeof(ClearanceCommand))]
public class UnallowCommand : ICommand
{
    public string Command => "unallow";
    public string[] Aliases => [];
    public string Description => "remove a player from the allowlist";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.LongTermBanning, out response)) return false;
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (!Cmd.TryTarget(arguments, out var id, out _)) { response = "usage: clearance unallow <user id>"; return false; }

        var ok = db.RemoveFromAllowlist(id);
        response = ok ? $"{id} is off the allowlist." : $"{id} wasn't allowlisted.";
        if (ok) Cmd.Discord?.Post("removed from allowlist", $"{Webhook.Link(id)} by {Cmd.Who(sender)}", Webhook.Grey);
        return ok;
    }
}
