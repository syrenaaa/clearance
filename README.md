# clearance

a steam-based trust score for scp:sl servers

it starts in **log-only mode**, so it won't do anything until you turn `Enforce` on.

## how it works

when a player connects, clearance checks these things:

1. **allowlist**: staff override, always let in
2. **active sponsor**: they have a sponsor, let in
3. **score**: ask steam (or use the cached answer), calculate a score, decide

the score decides one of three things:

| score | verdict | what happens |
|---|---|---|
| `AllowScore` or more | allow | you can join the server normally. |
| `FlagScore` to `AllowScore` | flag | you can join, but staff with RA access get a broadcast |
| below `FlagScore` | deny | rejected with a message (only if `Enforce` is on) |

every decision gets saved in the database with a breakdown of the points so you can see why someone was denied.

if steam is down or times out, the player is let in by default (`AllowWhenSteamIsDown`). same if the plugin hits a bug while checking someone. a broken plugin shouldnt lock out the whole server.

## score calculation

every part gives points up to a cap. add them up, round, and floor at 0.

| part | formula | default cap |
|---|---|---|
| scp:sl hours | `HoursWeight * sqrt(hours)` | 40 |
| account age | `PointsPerYear * years` | 25 |
| steam level | 1 point per level | 20 |
| library size | `games owned / GamesPerPoint` | 15 |
| time on this server | 1 point per hour | 30 |


**penalties:**

- **private profile**: `-PrivateProfilePenalty` (15). hours, level and library are all hidden, so only the server-time part can score
- **new account**: `-NewAccountPenalty` (10) if the account is younger than `NewAccountDays` (30)
- **vac / game ban**: `-BanPenalty` (30)
- **discord account**: `-DiscordPenalty` (10), see below

**instant deny:** a vac or game ban newer than `RecentBanDays` (365) is an instant deny, no matter the score. a ban with an unknown date counts as recent too. older bans only get the -30.

### discord and other ids

- **`@steam`** ids get the full score
- **`@discord`** ids have no steam data, but discord ids are snowflakes and the account creation time is inside the id. so they get account age points, the new-account penalty, and `-DiscordPenalty`. no hours, level or library
- **anything else** (like `@northwood`) is scored like a private profile. put them on the allowlist if u need them in

## config

**general**

| option | default | what it does |
|---|---|---|
| `Enforce` | `false` | `false` = log only, nobody is kicked |
| `SteamApiKey` | `""` | get one at https://steamcommunity.com/dev/apikey |
| `ScpslAppId` | `700330` | steam app id used to find scp:sl hours |
| `SteamCacheHours` | `12` | how long to reuse a steam lookup |
| `SteamTimeoutSeconds` | `5` | how long a join waits for steam. keep this low |
| `AllowWhenSteamIsDown` | `true` | let players in if the steam lookup fails |

**score**

| option | default | what it does |
|---|---|---|
| `HoursWeight` | `4` | multiplier on `sqrt(hours)` |
| `HoursCap` | `40` | max points from hours |
| `PointsPerYear` | `5` | account age points per year |
| `AgeCap` | `25` | max points from age |
| `LevelCap` | `20` | max points from steam level |
| `GamesPerPoint` | `4` | games owned per library point |
| `LibraryCap` | `15` | max points from library |
| `ServerHoursCap` | `30` | max points from time on your server |
| `PrivateProfilePenalty` | `15` | private profile |
| `DiscordPenalty` | `10` | discord accounts |
| `NewAccountDays` | `30` | accounts younger than this count as new |
| `NewAccountPenalty` | `10` | new account |
| `BanPenalty` | `30` | any vac / game ban |
| `RecentBanDays` | `365` | bans newer than this are an instant deny |
| `AllowScore` | `50` | this score or more = allow |
| `FlagScore` | `30` | this score or more = flag, below = deny |

**sponsors**

| option | default | what it does |
|---|---|---|
| `SponsorWindowDays` | `30` | how long a sponsorship lasts |
| `MaxActiveGuests` | `3` | guests one sponsor can have at once |
| `MaxStrikeWeight` | `3` | sponsors at or over this can't sponsor anyone |
| `StrikeDecayDays` | `30` | strikes stop counting after this long |

**messages**

| option | default |
|---|---|
| `KickMessage` | `clearance denied (score {score}, need {needed}). {breakdown}` |
| `BanKickMessage` | `clearance denied: your steam account has a recent VAC or game ban.` |

`{score}`, `{needed}` and `{breakdown}` are templates and get filled in.

## sponsors

a trusted player can sponsor a guest. while the sponsorship is active (`SponsorWindowDays`), the guest skips scoring and gets in.

if the guest gets **banned** during that window, the sponsor gets a strike. the strike weight is the severity of the punishment (bans are 2). once a sponsor's active strikes add up to `MaxStrikeWeight`, they can't sponsor anyone until the old strikes decay or get pardoned.

rules for sponsoring:

- not yourself
- you can't sponsor anyone while u are someone else's guest
- you need free slots (`MaxActiveGuests`)
- you need to be under the strike limit
- the guest can't already have a sponsor

revoking a sponsorship stops future strikes. punishments after a revoke don't count.

## database

clearance uses [LiteDB](https://www.litedb.org/).

the file is `clearance.db` in `<appdata>/SCP Secret Laboratory/LabAPI/configs/<port>/Clearance/`. user ids are trimmed and lowercased before they're stored. times are unix seconds, and `0` means "not set" for `RevokedAt` / `PardonedAt`.

| collection | contents |
|---|---|
| `players` | first seen, last seen, join count, total seconds played on this server |
| `steam` | cached steam lookup: public or not, scp:sl minutes, level, games owned, account created, ban info, fetched time |
| `allowlist` | staff overrides: user id, who added it, reason |
| `decisions` | log of every check: user, score, outcome, breakdown, and whether it was actually enforced |
| `sponsorships` | sponsor, guest, created, expires, revoked |
| `punishments` | bans: user, severity, reason, issued by, pardoned |
| `strikes` | sponsor, guest, which sponsorship and punishment caused it, weight, pardoned |

## install

1. build the project
2. put `Clearance.dll` in `LabAPI/plugins/<port>/`
3. put `LiteDB.dll` in `LabAPI/dependencies/<port>/`
4. start the server once so it makes `config.yml`
5. set `SteamApiKey`, restart, recommended to set `Enforce` false for a while
6. check the logs and the `decisions` collection, tune the numbers, then turn `Enforce` on

## todos

- [ ] commands: sponsor, revoke, allowlist add/remove, pardon, show a player's score
- [ ] discord integration