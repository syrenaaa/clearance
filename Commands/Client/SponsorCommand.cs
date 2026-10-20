using System;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Clearance.Commands.Client;

// the guest is usually not online, so sponsoring is by user id
[CommandHandler(typeof(ClientCommandHandler))]
public class SponsorCommand : ICommand
{
    public string Command => "sponsor";
    public string[] Aliases => [];
    public string Description => "vouch for a player so they skip the check. you get a strike if they get banned.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Ready(out var db, out var cfg, out response)) return false;
        if (!Player.TryGet(sender, out var me)) { response = "only players can sponsor."; return false; }
        if (arguments.Count != 1 || !Cmd.TryTarget(arguments, out var guest, out _))
        {
            response = "usage: .sponsor <steam id or user id>";
            return false;
        }

        var (ok, error) = db.TrySponsor(me.UserId, guest, TimeSpan.FromDays(cfg.SponsorWindowDays),
            cfg.MaxActiveGuests, cfg.MaxStrikeWeight, TimeSpan.FromDays(cfg.StrikeDecayDays));
        response = ok
            ? $"you're sponsoring {guest} for {cfg.SponsorWindowDays} days. if they get banned in that time, you get a strike."
            : error!;
        return ok;
    }
}
