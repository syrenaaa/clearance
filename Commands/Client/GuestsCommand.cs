using System;
using System.Linq;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Clearance.Commands.Client;

[CommandHandler(typeof(ClientCommandHandler))]
public class GuestsCommand : ICommand
{
    public string Command => "guests";
    public string[] Aliases => [];
    public string Description => "show who you're sponsoring and your strikes.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Ready(out var db, out var cfg, out response)) return false;
        if (!Player.TryGet(sender, out var me)) { response = "only players can sponsor."; return false; }

        var guests = db.GetActiveGuests(me.UserId);
        var strikes = db.GetActiveStrikeWeight(me.UserId, TimeSpan.FromDays(cfg.StrikeDecayDays));
        var lines = guests.Select(g => $"  {g.GuestId} until {Cmd.Date(g.ExpiresAt)}");
        response = $"guests: {guests.Count}/{cfg.MaxActiveGuests}, strikes: {strikes}/{cfg.MaxStrikeWeight}\n"
                   + string.Join("\n", lines);
        return true;
    }
}
