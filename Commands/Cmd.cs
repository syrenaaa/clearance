using System;
using System.Linq;
using Clearance.Persist;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Clearance.Commands;

internal static class Cmd
{
    public static bool Ready(out Database db, out ClearanceConfig cfg, out string response)
    {
        if (ClearancePlugin.Instance is { Db: { } d, Config: { } c })
        {
            (db, cfg, response) = (d, c, "");
            return true;
        }
        (db, cfg, response) = (null!, null!, "clearance isn't running.");
        return false;
    }

    public static bool Allowed(ICommandSender sender, PlayerPermissions perm, out string response)
    {
        var ok = sender.CheckPermission(perm);
        response = ok ? "" : $"you need the {perm} permission for this.";
        return ok;
    }

    // a full user id, a bare steam64 id, or the number of a player on the server
    public static string? ResolveId(string arg)
    {
        arg = arg.Trim();
        if (arg.IndexOf('@') > 0) return arg;
        if (!ulong.TryParse(arg, out var n)) return null;
        if (n < 100_000) return Player.Get((int)n) is { UserId.Length: > 0 } p ? p.UserId : null;
        return arg.Length == 17 ? arg + "@steam" : null;
    }

    // first argument as a user id, the rest joined (for reasons)
    public static bool TryTarget(ArraySegment<string> args, out string id, out string? rest)
    {
        id = args.Count > 0 ? ResolveId(args.First()) ?? "" : "";
        rest = args.Count > 1 ? string.Join(" ", args.Skip(1)) : null;
        return id.Length > 0;
    }

    public static string Who(ICommandSender sender) =>
        Player.TryGet(sender, out var p) && p.UserId is { Length: > 0 } id ? id : "console";

    public static string Date(long unix) =>
        DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm");
}
