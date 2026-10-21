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

    // LabAPI's loader doesn't attach [CommandHandler(typeof(ClearanceCommand))] children (it tries to
    // register "clearance" again instead), so they're listed here. add new subcommands to this list
    public override void LoadGeneratedCommands()
    {
        RegisterCommand(new CheckCommand());
        RegisterCommand(new AllowCommand());
        RegisterCommand(new UnallowCommand());
        RegisterCommand(new AllowlistCommand());
        RegisterCommand(new RevokeCommand());
        RegisterCommand(new PardonCommand());
        RegisterCommand(new StatsCommand());
        RegisterCommand(new ExportCommand());
    }

    protected override bool ExecuteParent(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "usage: clearance <check|allow|unallow|allowlist|revoke|pardon|stats|export>";
        return false;
    }
}
