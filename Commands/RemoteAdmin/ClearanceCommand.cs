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
    public override string[] Aliases => ["clr"]; // not "cl": vanilla clearbroadcast has it, and the clash leaves us without subcommands
    public override string Description => "clearance staff commands";

    // LabAPI attaches [CommandHandler(typeof(ClearanceCommand))] children itself; nothing calls this
    public override void LoadGeneratedCommands() { }

    protected override bool ExecuteParent(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "usage: clearance <check|allow|unallow|allowlist|revoke|pardon|stats|export>";
        return false;
    }
}
