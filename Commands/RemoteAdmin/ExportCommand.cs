using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CommandSystem;

namespace Clearance.Commands.RemoteAdmin;

// same data as stats, one row per player, for a spreadsheet
[CommandHandler(typeof(ClearanceCommand))]
public class ExportCommand : ICommand
{
    public string Command => "export";
    public string[] Aliases => [];
    public string Description => "write known players and their current score to a csv";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!Cmd.Allowed(sender, PlayerPermissions.PlayerSensitiveDataAccess, out response)) return false;
        if (!Cmd.Ready(out var db, out var cfg, out response)) return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rows = StatsCommand.Rescore(db, cfg, now).Select(p =>
        {
            var s = p.Steam;
            string N(double? v) => v?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
            return string.Join(",",
                s.UserId, s.IsPublic, N(s.AccountCreated is { } c ? (now - c) / (365.25 * 86400) : null),
                N(s.SteamLevel), N(s.GamesOwned), N(s.ScpslMinutes / 60.0), s.HasBan, N(s.DaysSinceLastBan),
                N(p.ServerSeconds / 3600.0), p.Bans, p.Score, p.Verdict, $"\"{p.Breakdown}\"");
        });

        var path = Path.Combine(db.Folder, $"clearance-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.csv");
        File.WriteAllLines(path, new[]
        {
            "user_id,public,account_years,steam_level,games,scpsl_hours,steam_ban,days_since_ban,server_hours,bans_here,score,verdict,breakdown"
        }.Concat(rows));

        response = $"wrote {path}";
        return true;
    }
}
