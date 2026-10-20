using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clearance.Persist;
using LiteDB;

namespace Clearance;

public static class SteamClient
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.steampowered.com/") };

    public static async Task<SteamSnapshot> FetchAsync(string userId, string apiKey, int appId, CancellationToken ct)
    {
        var steamId = userId.Split('@')[0];
        if (!ulong.TryParse(steamId, out _)) throw new FormatException("not a steam id");
        if (apiKey.Length == 0) throw new InvalidOperationException("no steam api key set");

        var key = Uri.EscapeDataString(apiKey);

        // profile + bans first. bans work even when the profile is private
        var summaryTask = Call("ISteamUser/GetPlayerSummaries/v2/", $"steamids={steamId}");
        var bansTask = Call("ISteamUser/GetPlayerBans/v1/", $"steamids={steamId}");
        await Task.WhenAll(summaryTask, bansTask);

        var profile = First(Get(Get(summaryTask.Result, "response"), "players"));
        if (profile.IsNull) throw new InvalidOperationException("steam doesn't know this id");
        var bans = First(Get(bansTask.Result, "players"));

        var snap = new SteamSnapshot
        {
            IsPublic = Num(Get(profile, "communityvisibilitystate")) == 3,
            AccountCreated = Num(Get(profile, "timecreated")),
            HasBan = Flag(Get(bans, "VACBanned")) || Num(Get(bans, "NumberOfGameBans")) > 0,
            DaysSinceLastBan = (int?)Num(Get(bans, "DaysSinceLastBan")),
        };
        if (!snap.IsPublic) return snap; // level and library are hidden anyway, skip the calls

        var levelTask = Call("IPlayerService/GetSteamLevel/v1/", $"steamid={steamId}");
        var gamesTask = Call("IPlayerService/GetOwnedGames/v1/", $"steamid={steamId}&include_played_free_games=1");
        await Task.WhenAll(levelTask, gamesTask);

        snap.SteamLevel = (int?)Num(Get(Get(levelTask.Result, "response"), "player_level"));

        var games = Get(gamesTask.Result, "response");
        snap.GamesOwned = (int?)Num(Get(games, "game_count"));
        if (Get(games, "games") is not BsonArray list) return snap;
        foreach (var game in list)
        {
            if (Num(Get(game, "appid")) != appId) continue;
            snap.ScpslMinutes = (int?)Num(Get(game, "playtime_forever"));
            break;
        }
        
        // "game details" are private; scp:sl hours stay null and score as 0.
        return snap;

        async Task<BsonValue> Call(string path, string query)
        {
            using var res = await Http.GetAsync($"{path}?key={key}&{query}", ct);
            res.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize(await res.Content.ReadAsStringAsync());
        }
    }

    private static BsonValue Get(BsonValue v, string key) =>
        v is BsonDocument d && d.TryGetValue(key, out var x) ? x : BsonValue.Null;

    private static BsonValue First(BsonValue v) => v is BsonArray { Count: > 0 } a ? a[0] : BsonValue.Null;

    private static long? Num(BsonValue v) => v.IsNumber ? v.AsInt64 : null;

    private static bool Flag(BsonValue v) => v is { IsBoolean: true, AsBoolean: true };
}