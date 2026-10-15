using System;

namespace Clearance;

public static class UserIds
{
    private const long DiscordEpochMs = 1420070400000;

    public static bool IsSteam(string id) => id.EndsWith("@steam", StringComparison.OrdinalIgnoreCase);
    
    // since discord ids are snowflakes we can get the creation date
    public static long? DiscordCreated(string id)
    {
        const string suffix = "@discord";
        if (!id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        if (!ulong.TryParse(id.Substring(0, id.Length - suffix.Length), out var snowflake)) return null;

        var created = (long)((snowflake >> 22) / 1000) + DiscordEpochMs / 1000;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return created > DiscordEpochMs / 1000 && created <= now ? created : null;
    }
}
