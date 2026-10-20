using System;
using Clearance.Discord;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Clearance.Commands.Client;

[CommandHandler(typeof(ClientCommandHandler))]
public class UnsponsorCommand : ICommand
{
    public string Command => "unsponsor";
    public string[] Aliases => [];
    public string Description => "stop sponsoring a player. bans after this don't strike you.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Ready(out var db, out _, out response)) return false;
        if (!Player.TryGet(sender, out var me)) { response = "only players can sponsor."; return false; }
        if (arguments.Count != 1 || !Cmd.TryTarget(arguments, out var guest, out _))
        {
            response = "usage: .unsponsor <steam id or user id>";
            return false;
        }

        var ok = db.RevokeSponsorship(me.UserId, guest);
        response = ok ? $"you're no longer sponsoring {guest}." : $"you aren't sponsoring {guest}.";
        if (ok) Cmd.Discord?.Post("sponsorship ended", $"{Webhook.Link(me.UserId)} stopped sponsoring {Webhook.Link(guest)}", Webhook.Grey);
        return ok;
    }
}
