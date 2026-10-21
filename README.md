# clearance

a steam-based trust scoring plugin for scp:sl servers (labapi).

the plugin defaults in **log-only mode**: every join is scored and logged, but
nobody is kicked until you turn `Enforce` on.

## how it works

when someone joins, clearance lets them in if they're northwood staff, staff
allowlisted them, or a player is sponsoring them. otherwise it scores their
steam account:

- **points** for scp:sl hours, account age, steam level, library size, and time
  played on your server
- **penalties** for private profiles, brand new accounts and vac / game bans
  (old bans fade, recent ones are an instant deny)

a high score gets in, a middle score gets in but staff get a broadcast, and a
low score is rejected. if steam is down, the last lookup is used, or the player
is let in.

**sponsors:** players with some time on the server can sponsor a guest with
`.sponsor <id>`. if the guest gets banned within the sponsorship window, the
sponsor gets a strike, and too many strikes means no more sponsoring.

**central server:** with a `CentralKey`, servers share bans and anonymous daily
stats. players with running bans on other servers lose points (see
[central server](DETAILS.md#central-server)).

**staff** manage the allowlist, sponsorships and pardons with
`clearance <subcommand>` in RA, and can see how the current config would score
known players with `clearance stats`.

set `DiscordWebhookUrl` and denies, flags, strikes and staff actions get posted
to a **discord** channel.

players with **do not track** on are still checked, but their tracking data is
deleted when they leave. they get a hint that `.trackme` lets their play time
count, if they want.

you can read more details in [DETAILS.md](DETAILS.md), and all the config option
is specified in [CONFIG.md](CONFIG.md).

## install

1. build the project
2. put `Clearance.dll` in `LabAPI/plugins/<port>/`
3. put `LiteDB.dll` in `LabAPI/dependencies/<port>/`
4. start the server once so it makes `config.yml`
5. set `SteamApiKey` and restart
6. leave `Enforce` off for a week or two, tune with `clearance stats` (see
   [tuning](DETAILS.md#tuning)), then turn `Enforce` on

## todos

- [x] discord webhook
- [x] central server: shared bans + daily stats
- [ ] discord slash commands (check a score, sponsor, ...)
