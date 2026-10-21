namespace Clearance;

public class ClearanceConfig
{
    // false = scores and decisions only get logged
    public bool Enforce { get; set; } = false;

    // steam web api key (https://steamcommunity.com/dev/apikey)
    public string SteamApiKey { get; set; } = "";
    public int ScpslAppId { get; set; } = 700330;
    public int SteamCacheHours { get; set; } = 12;
    public int SteamTimeoutSeconds { get; set; } = 5;     // pre-auth waits this long for steam
    public bool AllowWhenSteamIsDown { get; set; } = true; // steam failed/timed out: true = pass (logged as "steam-error")
    public bool AllowNewWhenSteamIsDown { get; set; } = true; // false = players who never joined before get rejected

    // score: each thing gives points, up to a cap
    public double HoursWeight { get; set; } = 4;    // points = weight * sqrt(game hours)
    public int HoursCap { get; set; } = 40;
    public double PointsPerYear { get; set; } = 5;  // account age
    public int AgeCap { get; set; } = 25;
    public int LevelCap { get; set; } = 20;         // 1 point per steam level
    public int GamesPerPoint { get; set; } = 4;     // games owned
    public int LibraryCap { get; set; } = 15;
    public int ServerHoursCap { get; set; } = 30;   // 1 point per hour played on this server

    // penalties
    public int PrivateProfilePenalty { get; set; } = 5;
    public int NewAccountDays { get; set; } = 30;
    public int NewAccountPenalty { get; set; } = 10;
    public int BanPenalty { get; set; } = 30;
    public int RecentBanDays { get; set; } = 365;   // vac/game ban newer than this = deny
    public int BanHalfLifeDays { get; set; } = 365; // older bans: penalty halves every this many days

    // tiers: >=AllowScore => pass, FlagScore..AllowScore => flag staff, <FlagScore => deny
    public int AllowScore { get; set; } = 50;
    public int FlagScore { get; set; } = 20;

    // sponsors
    public int SponsorWindowDays { get; set; } = 30;
    public double SponsorMinHours { get; set; } = 5;  // hours played here before you can sponsor
    public int MaxActiveGuests { get; set; } = 3;
    public int MaxStrikeWeight { get; set; } = 3;
    public int StrikeDecayDays { get; set; } = 30;

    // do not track players get a hint after joining that .trackme lets their play time count
    public bool DntConsentHint { get; set; } = true;
    public string DntConsentHintMessage { get; set; } =
        "this server respects your Do Not Track setting.\nif you want your play time here to count towards your trust score, type .trackme in the console (~).";

    // discord: webhook url from the channel's integrations settings. empty = off
    public string DiscordWebhookUrl { get; set; } = "";

    // central server: shared ban facts + anonymous daily stats. empty key = off
    public string CentralUrl { get; set; } = "https://sl-clearance.val.run";
    public string CentralKey { get; set; } = "";
    public int CentralTimeoutSeconds { get; set; } = 3; // runs next to the steam lookup, not after it
    public bool ShareBans { get; set; } = true;
    public int ShareMinBanHours { get; set; } = 24;     // shorter bans stay local
    public bool ShareStats { get; set; } = true;
    public int NetworkBanPenalty { get; set; } = 10;    // per server with a running ban for them
    public int NetworkBanCap { get; set; } = 30;

    // kick messages. {score} {needed} {breakdown} are templates
    public string KickMessage { get; set; } = "clearance denied (score {score}, need {needed}). {breakdown}";
    public string BanKickMessage { get; set; } = "clearance denied: your steam account has a recent VAC or game ban.";
    public string SteamDownKickMessage { get; set; } = "steam is not responding and we can't check new players right now. try again later.";
}