using System;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Clearance.Commands.Client;

[CommandHandler(typeof(ClientCommandHandler))]
public class UntrackMeCommand : ICommand
{
    public string Command => "untrackme";
    public string[] Aliases => [];
    public string Description => "take back .trackme and delete your saved play time";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (!Player.TryGet(sender, out var me)) { response = "only players can do this."; return false; }

        var ok = db.WithdrawConsent(me.UserId);
        response = ok
            ? "done. your saved play time is deleted and nothing new is saved while do not track is on."
            : "you haven't used .trackme, so there's nothing to take back.";
        return ok;
    }
}
