using System;
using System.IO;
using Clearance.Discord;
using Clearance.Persist;
using LabApi.Features;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;

namespace Clearance;

public class ClearancePlugin : Plugin<ClearanceConfig>
{
    public override string Name => "Clearance";
    public override string Description => "Steam trust score screening with sponsors.";
    public override string Author => "syrena";
    public override Version Version => new(1, 0, 0);
    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    // commands are created by the game, so they reach the db through here
    internal static ClearancePlugin? Instance { get; private set; }
    internal Database? Db => _db;
    internal Webhook? Discord { get; private set; }

    private Database? _db;
    private ClearanceEvents? _events;

    public override void Enable()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SCP Secret Laboratory", "LabAPI", "configs", Server.Port.ToString(), "Clearance");
        _db = new Database(Path.Combine(dir, "clearance.db"));

        if (Config!.DiscordWebhookUrl.Length > 0)
            Discord = new Webhook(Config.DiscordWebhookUrl, text => _events?.Note(text));

        _events = new ClearanceEvents(_db, Config, Discord);
        _events.Register();
        Instance = this;
        
        Logger.Info("Clearance: enabled.");

        if (Config!.SteamApiKey.Length == 0) Logger.Warn("Clearance: no SteamApiKey set, steam players can't be checked.");
        if (!Config.Enforce) Logger.Info("Clearance: log-only mode, nobody will be kicked.");
    }

    public override void Disable()
    {
        Instance = null;
        _events?.Unregister(); // saves play time as well
        _events = null;

        Discord?.Dispose();
        Discord = null;

        _db?.Dispose();
        _db = null;
        
        Logger.Info("Clearance: disabled.");
    }
}
