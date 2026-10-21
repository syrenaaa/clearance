using System;
using System.Linq;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

public class AllowlistCommand : ICommand
{
    public string Command => "allowlist";
    public string[] Aliases => [];
    public string Description => "list allowlisted players";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.PlayerSensitiveDataAccess, out response)) return false;
        if (!Cmd.Ready(out var db, out _, out response)) return false;

        var list = db.GetAllowlist();
        response = list.Count == 0
            ? "the allowlist is empty."
            : string.Join("\n", list.Select(a => $"{a.UserId} by {a.AddedBy} {Cmd.Date(a.AddedAt)}{(a.Reason is null ? "" : ": " + a.Reason)}"));
        return true;
    }
}
