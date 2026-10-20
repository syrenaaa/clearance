using System;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Clearance.Commands.Client;

// explicit opt-in for do not track players (CSG 8.3.2)
[CommandHandler(typeof(ClientCommandHandler))]
public class TrackMeCommand : ICommand
{
    public string Command => "trackme";
    public string[] Aliases => [];
    public string Description => "let this server save your play time even with do not track on";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (!Player.TryGet(sender, out var me)) { response = "only players can do this."; return false; }
        if (!me.DoNotTrack) { response = "you don't have do not track on, so your play time is already saved."; return false; }

        db.GiveConsent(me.UserId);
        response = "thanks! your play time on this server is saved from now on and counts towards your trust score. "
                   + "you can take it back any time with .untrackme";
        return true;
    }
}
