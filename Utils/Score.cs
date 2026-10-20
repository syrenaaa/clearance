using System;
using System.Collections.Generic;
using Clearance.Persist;

namespace Clearance;

public enum Verdict { Allow, Flag, Deny }

public static class Score
{
    private const double SecondsPerYear = 365.25 * 86400;

    // steam = null (lookup failed, or not a steam account) is treated like a private profile.
    // discordCreated: for @discord ids, account creation time from the snowflake (see UserIds).
    public static (int Score, bool InstantDeny, string Breakdown) Compute(
        ClearanceConfig cfg, SteamSnapshot? steam, long serverSeconds, long now, long? discordCreated = null)
    {
        List<string> parts = [];
        double total = 0;
        var instantDeny = false;

        void Add(string name, double points)
        {
            total += points;
            if (Math.Abs(points) >= 0.5) parts.Add($"{name} {points:+0;-0}");
        }

        void Age(long? created)
        {
            if (created is not { } c) return;
            var years = Math.Max(0, now - c) / SecondsPerYear;
            Add("age", Math.Min(cfg.AgeCap, cfg.PointsPerYear * years));
            if (years * 365.25 < cfg.NewAccountDays) Add("new account", -cfg.NewAccountPenalty);
        }

        if (steam is { IsPublic: true })
        {
            var hours = (steam.ScpslMinutes ?? 0) / 60.0;
            Add("hours", Math.Min(cfg.HoursCap, cfg.HoursWeight * Math.Sqrt(hours)));
            Age(steam.AccountCreated);

            Add("level", Math.Min(cfg.LevelCap, steam.SteamLevel ?? 0));
            Add("library", Math.Min(cfg.LibraryCap, (steam.GamesOwned ?? 0) / (double)Math.Max(1, cfg.GamesPerPoint)));
        }
        else if (discordCreated is not null)
        {
            // only the account age is known, no hours / level / library
            Add("discord", -cfg.DiscordPenalty);
            Age(discordCreated);
        }
        else
        {
            Add("private profile", -cfg.PrivateProfilePenalty);
        }

        Add("server", Math.Min(cfg.ServerHoursCap, serverSeconds / 3600.0));

        // ban info comes from a different steam endpoint that works for private profiles too
        if (steam is { HasBan: true })
        {
            if (steam.DaysSinceLastBan is not { } days || days <= cfg.RecentBanDays)
            {
                Add("ban", -cfg.BanPenalty);
                instantDeny = true;
            }
            else
            {
                // people change: past the recent window the penalty halves every BanHalfLifeDays
                var halvings = (days - cfg.RecentBanDays) / (double)Math.Max(1, cfg.BanHalfLifeDays);
                Add("ban", -cfg.BanPenalty * Math.Pow(0.5, halvings));
            }
        }

        var score = Math.Max(0, (int)Math.Round(total));
        return (score, instantDeny, parts.Count == 0 ? "no points" : string.Join(", ", parts));
    }

    public static Verdict Decide(ClearanceConfig cfg, int score, bool instantDeny) =>
        instantDeny ? Verdict.Deny
        : score >= cfg.AllowScore ? Verdict.Allow
        : score >= cfg.FlagScore ? Verdict.Flag
        : Verdict.Deny;

    public static string DenyMessage(ClearanceConfig cfg, int score, bool instantDeny, string breakdown) =>
        instantDeny
            ? cfg.BanKickMessage
            : cfg.KickMessage
                .Replace("{score}", score.ToString())
                .Replace("{needed}", cfg.FlagScore.ToString())
                .Replace("{breakdown}", breakdown);
}