# clearance config

every option in `config.yml`. how they fit together is in [DETAILS.md](DETAILS.md).

## general

| option                    | default  | what it does                                            |
| ------------------------- | -------- | ------------------------------------------------------- |
| `Enforce`                 | `false`  | `false` = log only, nobody is kicked                    |
| `SteamApiKey`             | `""`     | get one at https://steamcommunity.com/dev/apikey        |
| `ScpslAppId`              | `700330` | steam app id used to find scp:sl hours                  |
| `SteamCacheHours`         | `12`     | how long to reuse a steam lookup                        |
| `SteamTimeoutSeconds`     | `5`      | how long a join waits for steam. keep this low          |
| `AllowWhenSteamIsDown`    | `true`   | let players in if the steam lookup fails                |
| `AllowNewWhenSteamIsDown` | `true`   | `false` = reject first-time players while steam is down |

## discord

| option              | default | what it does                                                                |
| ------------------- | ------- | --------------------------------------------------------------------------- |
| `DiscordWebhookUrl` | `""`    | channel webhook to post to (see [discord](DETAILS.md#discord)). empty = off |

## score

| option                  | default | what it does                             |
| ----------------------- | ------- | ---------------------------------------- |
| `HoursWeight`           | `4`     | multiplier on `sqrt(hours)`              |
| `HoursCap`              | `40`    | max points from hours                    |
| `PointsPerYear`         | `5`     | account age points per year              |
| `AgeCap`                | `25`    | max points from age                      |
| `LevelCap`              | `20`    | max points from steam level              |
| `GamesPerPoint`         | `4`     | games owned per library point            |
| `LibraryCap`            | `15`    | max points from library                  |
| `ServerHoursCap`        | `30`    | max points from time on your server      |
| `PrivateProfilePenalty` | `5`     | private profile                          |
| `DiscordPenalty`        | `10`    | discord accounts                         |
| `NewAccountDays`        | `30`    | accounts younger than this count as new  |
| `NewAccountPenalty`     | `10`    | new account                              |
| `BanPenalty`            | `30`    | any vac / game ban                       |
| `RecentBanDays`         | `365`   | bans newer than this are an instant deny |
| `BanHalfLifeDays`       | `365`   | older bans: penalty halves this often    |
| `AllowScore`            | `50`    | this score or more = allow               |
| `FlagScore`             | `20`    | this score or more = flag, below = deny  |

## sponsors

| option              | default | what it does                                  |
| ------------------- | ------- | --------------------------------------------- |
| `SponsorWindowDays` | `30`    | how long a sponsorship lasts                  |
| `SponsorMinHours`   | `5`     | hours played here before you can sponsor      |
| `MaxActiveGuests`   | `3`     | guests one sponsor can have at once           |
| `MaxStrikeWeight`   | `3`     | sponsors at or over this can't sponsor anyone |
| `StrikeDecayDays`   | `30`    | strikes stop counting after this long         |

## messages

| option                 | default                                                                              |
| ---------------------- | ------------------------------------------------------------------------------------ |
| `KickMessage`          | `clearance denied (score {score}, need {needed}). {breakdown}`                       |
| `BanKickMessage`       | `clearance denied: your steam account has a recent VAC or game ban.`                 |
| `SteamDownKickMessage` | `steam is not responding and we can't check new players right now. try again later.` |

`{score}`, `{needed}` and `{breakdown}` are templates and get filled in.
