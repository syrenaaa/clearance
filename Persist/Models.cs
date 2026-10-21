using LiteDB;

namespace Clearance.Persist;

// times are unix seconds. for RevokedAt / PardonedAt, 0 means "not set".
public class PlayerRecord
{
    [BsonId] public string UserId { get; set; } = "";
    public long FirstSeen { get; set; }
    public long LastSeen { get; set; }
    public int Joins { get; set; }
    public long ServerSeconds { get; set; }
}

// data fetched from steam
public class SteamSnapshot
{
    [BsonId] public string UserId { get; set; } = "";
    public long FetchedAt { get; set; }
    public bool IsPublic { get; set; }
    public int? ScpslMinutes { get; set; }
    public int? SteamLevel { get; set; }
    public int? GamesOwned { get; set; }
    public long? AccountCreated { get; set; }
    public bool HasBan { get; set; }
    public int? DaysSinceLastBan { get; set; }
}

public class AllowlistEntry
{
    [BsonId] public string UserId { get; set; } = "";
    public string AddedBy { get; set; } = "";
    public string? Reason { get; set; }
    public long AddedAt { get; set; }
}

public class DecisionEntry
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public long At { get; set; }
    public int Score { get; set; }
    public string Outcome { get; set; } = "";   // "allowed", "flagged", "kicked", ...
    public string? Breakdown { get; set; }      // human-readable score breakdown
    public bool Enforced { get; set; }          // false in log-only mode
}

public class Sponsorship
{
    public int Id { get; set; }
    public string SponsorId { get; set; } = "";
    public string GuestId { get; set; } = "";
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }         // end of the accountability window
    public long RevokedAt { get; set; }
}

public class Punishment
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public long At { get; set; }
    public int Severity { get; set; }           // becomes the strike weight
    public string? Reason { get; set; }
    public string IssuedBy { get; set; } = "";
    public long PardonedAt { get; set; }
    public long Duration { get; set; }          // seconds. 0 = unknown (bans from before this was saved)
    public string? CentralReportId { get; set; } // set once the central server has it
}

// writes waiting for the central server, sent oldest first. the body is built at send time
public class OutboxItem
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";      // "ban" / "pardon": Ref = punishment id. "stats": Ref = utc day
    public string Ref { get; set; } = "";
    public int Attempts { get; set; }
    public long NextAt { get; set; }
}

// small key/value settings that live with the data, like the install id
public class Meta
{
    [BsonId] public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

// a do not track player's explicit opt-in (CSG 8.3.2). GivenAt is the proof
public class TrackingConsent
{
    [BsonId] public string UserId { get; set; } = "";
    public long GivenAt { get; set; }
}

public class Strike
{
    public int Id { get; set; }
    public string SponsorId { get; set; } = "";  // who got struck
    public string GuestId { get; set; } = "";    // whose punishment caused it
    public int SponsorshipId { get; set; }
    public int PunishmentId { get; set; }
    public int Weight { get; set; }
    public long CreatedAt { get; set; }
    public long PardonedAt { get; set; }
}