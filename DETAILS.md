# clearance details

everything about how clearance decides. the short version is in
[README.md](README.md), every option is in [CONFIG.md](CONFIG.md).

## the check

when a player connects, clearance checks these in order:

1. **bypass**: `@northwood` ids, and anyone whose auth token has the northwood
   staff, ignore bans or ignore whitelist flag, always get in. the community
   server guidelines (7.1.1, 7.2.1) require plugins to respect these
2. **allowlist**: staff override, always let in
3. **active sponsor**: they have a sponsor, let in
4. **score**: ask steam (or use the cached answer), calculate a score, decide

the score decides one of three things:

| score                       | verdict | what happens                                           |
| --------------------------- | ------- | ------------------------------------------------------ |
| `AllowScore` or more        | allow   | you can join the server normally.                      |
| `FlagScore` to `AllowScore` | flag    | you can join, but staff with RA access get a broadcast |
| below `FlagScore`           | deny    | rejected with a message (only if `Enforce` is on)      |

every decision gets saved in the database with a breakdown of the points so you
can see why someone was denied.

if steam is down or times out:

1. if the player was looked up before, the old cached data is used, no matter
   how old
2. if not, and they never joined before, `AllowNewWhenSteamIsDown` decides
   (`false` = reject them with `SteamDownKickMessage`)
3. otherwise they're let in by default (`AllowWhenSteamIsDown`)

## score calculation

every part gives points up to a cap. sum, round, and floor at 0.

| part                | formula                       | default cap |
| ------------------- | ----------------------------- | ----------- |
| scp:sl hours        | `HoursWeight * sqrt(hours)`   | 40          |
| account age         | `PointsPerYear * years`       | 25          |
| steam level         | 1 point per level             | 20          |
| library size        | `games owned / GamesPerPoint` | 15          |
| time on this server | 1 point per hour              | 30          |

**penalties:**

- **private profile**: `-PrivateProfilePenalty` (5). hours, level and library
  are all hidden, so only the server-time part can score
- **new account**: `-NewAccountPenalty` (10) if the account is younger than
  `NewAccountDays` (30)
- **vac / game ban**: `-BanPenalty` (30), fading for old bans (see below)

**instant deny:** a vac or game ban newer than `RecentBanDays` (365) is an
instant deny, no matter the score. a ban with an unknown date counts as recent
too.

older bans don't deny, and their penalty fades: it halves every
`BanHalfLifeDays` (365) after `RecentBanDays`. with the defaults a ban from 2
years ago is -15, 3 years -7.5, 4 years -3.75.

### discord and other ids

- **`@steam`** ids get the full score
- **`@discord`** ids have no steam data, but discord ids are snowflakes and the
  account creation time is inside the id. so they get account age points and the
  new-account penalty, like a steam account with everything else hidden. there's
  no extra penalty for using discord: the community server guidelines (8.6)
  forbid treating discord logins differently from steam
- **`@northwood`** ids are northwood staff and always get in, no score
- **anything else** is scored like a private profile. put them on the allowlist
  if u need them in

## sponsors

a trusted player can sponsor a guest. while the sponsorship is active
(`SponsorWindowDays`), the guest skips scoring and gets in.

if the guest gets **banned** during that window, the sponsor gets a strike. the
strike weight is the severity of the punishment (bans are 2). once a sponsor's
active strikes add up to `MaxStrikeWeight`, they can't sponsor anyone until the
old strikes decay or get pardoned.

rules for sponsoring:

- not yourself
- you can't sponsor anyone while u are someone else's guest
- you need `SponsorMinHours` played on this server (counted when you leave, so
  the current session doesn't count yet)
- you need free slots (`MaxActiveGuests`)
- you need to be under the strike limit
- the guest can't already have a sponsor

revoking a sponsorship stops future strikes. punishments after a revoke don't
count.

## commands

ids can be a full user id (`76561198000000000@steam`), a bare steam64 id, or the
number of a player who is on the server.

**players** (in the `~` console)

| command           | what it does                                       |
| ----------------- | -------------------------------------------------- |
| `.sponsor <id>`   | sponsor a guest                                    |
| `.unsponsor <id>` | stop sponsoring them                               |
| `.guests`         | who you're sponsoring, and your strikes            |
| `.trackme`        | do not track players: save my play time anyway     |
| `.untrackme`      | take back `.trackme` and delete my saved play time |

**staff** (in RA or the server console, `clr` works as a short form)

| command                         | permission                  | what it does                                                    |
| ------------------------------- | --------------------------- | --------------------------------------------------------------- |
| `clearance check <id>`          | `PlayerSensitiveDataAccess` | allowlist, sponsor, guests, strikes, bans, recent checks        |
| `clearance allowlist`           | `PlayerSensitiveDataAccess` | list the allowlist                                              |
| `clearance allow <id> [reason]` | `LongTermBanning`           | let them skip the check                                         |
| `clearance unallow <id>`        | `LongTermBanning`           | remove them from the allowlist                                  |
| `clearance revoke <guest id>`   | `LongTermBanning`           | end a guest's sponsorship                                       |
| `clearance pardon <ban number>` | `LongTermBanning`           | pardon a ban (number from `check`), undoes the sponsor's strike |
| `clearance stats`               | `PlayerSensitiveDataAccess` | how the current config scores known players, see tuning         |
| `clearance export`              | `PlayerSensitiveDataAccess` | same as stats, one row per player in a csv next to the database |

## tuning

the defaults are a starting point. to fit them to your players:

1. run with `Enforce` off for a week or two. every join is still scored and
   logged
2. `clearance stats` re-scores everyone clearance has steam data for with the
   current config, and shows how many would be allowed, flagged and denied, and
   how many of each were banned on your server later
3. change the config, reload, run `clearance stats` again and compare. good
   rules put the banned players in deny and keep everyone else out of it
4. `clearance export` writes the same data to a csv if you want a spreadsheet

players without a steam lookup (discord ids, do not track players) aren't in
stats or export.

## discord

set `DiscordWebhookUrl` to a channel webhook (channel settings, integrations,
webhooks) and clearance posts there:

| post                         | when                                                     |
| ---------------------------- | -------------------------------------------------------- |
| rejected / would be rejected | a player is denied (or would be, in log-only mode)       |
| flagged                      | a player gets in with a middle score                     |
| rejected: steam is down      | a new player is turned away by `AllowNewWhenSteamIsDown` |
| sponsor strike               | a guest was banned and their sponsor got a strike        |
| sponsorships                 | `.sponsor`, `.unsponsor`, `clearance revoke`             |
| allowlist / pardons          | `clearance allow`, `unallow`, `pardon`                   |

steam ids link to the steam profile. posts never ping anyone, even if a reason
contains `@everyone`.

posting happens in the background, so joins never wait on discord. if discord is
down or rate limits, posts are retried a bit and then dropped, and the server
log says so once.

do not track is only known after the check, so a denied or flagged player's id
is posted even if they have do not track on.

## central server

set `CentralKey` and clearance talks to a shared central server
([clearance-central](https://github.com/syrenaaa/clearance-central)). it does
two things:

**ban sharing.** bans of at least `ShareMinBanHours` are reported with the user
id, time, length, severity and category (always `other` for now, game bans only
come with free text). pardoning one with `clearance pardon` reports the pardon.
the reason, the issuer and the player's name are never sent.

on join, the network lookup runs next to the steam lookup, so it doesn't add to
the wait. it returns facts, not verdicts: how many servers have a running ban
for the player. once at least `NetworkMinServers` agree, each server is a
`-NetworkBanPenalty` in the score (up to `NetworkBanCap`), shown as
`network bans` in the breakdown, so one server alone can't push anyone out. it's
never an instant deny on its own. if the central server is slow or down, the
player is scored without it. answers are cached for 10 minutes.

**daily stats** (opt in with `ShareStats`). once a utc day is over, its counts
go up: how many checks ended in each outcome, scores in buckets of 10, how many
of that day's bans had which score at their last check, plus `Enforce`,
`AllowScore`, `FlagScore` and the plugin version. no user ids. the server drops
days with fewer than 20 checks and only shows sums over days with at least 5
servers, so no single server can be picked out. missed days are caught up, up to
a week back.

reports and stats wait in the `outbox` collection and are sent one at a time,
oldest first, from the background. joins never wait on them. if the central
server is down they're retried with backoff (up to an hour apart), and a wrong
`CentralKey` is retried hourly. the server log says once when it breaks and once
when it works again.

bans count as server security (CSG D.8), so they're shared for do not track
players too. do not track players' decisions are deleted when they leave, so
they mostly don't show up in the daily counts.

## database

clearance uses [LiteDB](https://www.litedb.org/).

the file is `clearance.db` in
`<appdata>/SCP Secret Laboratory/LabAPI/configs/<port>/Clearance/`. user ids are
trimmed and lowercased before they're stored. times are unix seconds, and `0`
means "not set" for `RevokedAt` / `PardonedAt`.

| collection     | contents                                                                                                        |
| -------------- | --------------------------------------------------------------------------------------------------------------- |
| `players`      | first seen, last seen, join count, total seconds played on this server                                          |
| `steam`        | cached steam lookup: public or not, scp:sl minutes, level, games owned, account created, ban info, fetched time |
| `allowlist`    | staff overrides: user id, who added it, reason                                                                  |
| `decisions`    | log of every check: user, score, outcome, breakdown, and whether it was actually enforced                       |
| `sponsorships` | sponsor, guest, created, expires, revoked                                                                       |
| `punishments`  | bans: user, severity, reason, issued by, pardoned, length, central report id                                    |
| `strikes`      | sponsor, guest, which sponsorship and punishment caused it, weight, pardoned                                    |
| `consents`     | do not track players who used `.trackme`: user id, when                                                         |
| `outbox`       | reports + stats days waiting for the central server                                                             |
| `meta`         | the install id (keeps central report ids unique if the db is reset), last stats day queued                      |

### do not track

players with do not track on are still checked when they join, but when they
leave their `players`, `steam` and `decisions` entries are deleted. the game
only tells the server about do not track after pre-auth, so the data exists
while they're online. `allowlist`, `sponsorships`, `punishments` and `strikes`
are kept, those are moderation.

their steam lookup is kept in memory only (for `SteamCacheHours`, gone on
restart), so rejoining over and over can't use up the steam api key.

without their play time, do not track players can't earn server points or
sponsor. the community server guidelines (8.3.1) forbid punishing players for
using do not track, so they can opt in instead (8.3.2):

- about 5 seconds after joining, do not track players get a hint
  (`DntConsentHintMessage`) that `.trackme` lets their play time count. it's
  optional, nothing happens if they ignore it
- `.trackme` saves a consent record (user id + time, in `consents`) as proof.
  from then on they're tracked like everyone else
- `.untrackme` deletes the consent record and their tracked data
