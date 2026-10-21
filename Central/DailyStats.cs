using System;
using System.Collections.Generic;
using System.Linq;
using Clearance.Persist;
using LiteDB;

namespace Clearance.Central;

// one utc day of anonymous aggregates for PUT /v1/stats/{day}: counts only, no user ids leave the server
public static class DailyStats
{
    // decision log outcome -> spec CheckCounts key
    private static readonly Dictionary<string, string> Checks = new()
    {
        ["Allow"] = "allow", ["Flag"] = "flag", ["Deny"] = "deny",
        ["Bypass"] = "bypass", ["Allowlisted"] = "allowlisted", ["Sponsored"] = "sponsored",
        ["steam-error"] = "steamError", ["SteamDownNew"] = "steamDownNew",
    };

    public static readonly string[] Scored = ["Allow", "Flag", "Deny"];

    public static string Day(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd");

    public static long DayStart(string day) =>
        new DateTimeOffset(DateTime.ParseExact(day, "yyyy-MM-dd", null), TimeSpan.Zero).ToUnixTimeSeconds();

    // lower bound in steps of 10. 130 is open ended, so higher caps still fit
    public static int Bucket(int score) => Math.Min(130, Math.Max(0, score) / 10 * 10);

    public static BsonDocument Build(Database db, ClearanceConfig cfg, string pluginVersion, string day)
    {
        var from = DayStart(day);
        var to = from + 86400;
        var decisions = db.GetDecisionsBetween(from, to);

        var checks = new BsonDocument();
        foreach (var g in decisions.Where(d => Checks.ContainsKey(d.Outcome)).GroupBy(d => Checks[d.Outcome]))
            checks[g.Key] = g.Count();

        var scores = Buckets(decisions.Where(d => Scored.Contains(d.Outcome)).Select(d => d.Score));
        var bans = Buckets(db.GetPunishmentsBetween(from, to)
            .Select(p => db.GetLastScore(p.UserId, p.At, Scored))
            .Where(s => s is not null)
            .Select(s => s!.Value));

        return new BsonDocument
        {
            ["pluginVersion"] = pluginVersion,
            ["enforce"] = cfg.Enforce,
            ["thresholds"] = new BsonDocument { ["allowScore"] = cfg.AllowScore, ["flagScore"] = cfg.FlagScore },
            ["checks"] = checks,
            ["scores"] = scores,
            ["bansByJoinScore"] = bans,
        };
    }

    private static BsonDocument Buckets(IEnumerable<int> scores)
    {
        var doc = new BsonDocument();
        foreach (var g in scores.GroupBy(Bucket).OrderBy(g => g.Key)) doc[g.Key.ToString()] = g.Count();
        return doc;
    }
}
