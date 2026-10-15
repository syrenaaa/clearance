using System;
using System.IO;
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
    public override string Author => "~furry";
    public override Version Version => new(1, 0, 0);
    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    private Database? _db;
    private ClearanceEvents? _events;

    public override void Enable()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SCP Secret Laboratory", "LabAPI", "configs", Server.Port.ToString(), "Clearance");
        _db = new Database(Path.Combine(dir, "clearance.db"));

        _events = new ClearanceEvents(_db, Config!);
        _events.Register();
        
        Logger.Info("Clearance: enabled.");

        if (Config!.SteamApiKey.Length == 0) Logger.Warn("Clearance: no SteamApiKey set, steam players can't be checked.");
        if (!Config.Enforce) Logger.Info("Clearance: log-only mode, nobody will be kicked.");
    }

    public override void Disable()
    {
        _events?.Unregister(); // saves play time as well
        _events = null;

        _db?.Dispose();
        _db = null;
        
        Logger.Info("Clearance: disabled.");
    }
}
