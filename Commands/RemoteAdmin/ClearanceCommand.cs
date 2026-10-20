using System;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

// staff commands: "clearance <sub>" in RA or the server console.
// looking needs sensitive data access, changing things needs long term banning
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public class ClearanceCommand : ParentCommand
{
    public override string Command => "clearance";
    public override string[] Aliases => ["cl"];
    public override string Description => "clearance staff commands";

    // attach with [CommandHandler(typeof(ClearanceCommand))] instead
    public override void LoadGeneratedCommands() { }

    protected override bool ExecuteParent(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "usage: clearance <check|allow|unallow|allowlist|revoke|pardon|stats|export>";
        return false;
    }
}
