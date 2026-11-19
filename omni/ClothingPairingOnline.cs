using System.Collections.Concurrent;
using System.Diagnostics;
using ClothingPairing.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using TreeTools.Lifecycle;

namespace ClothingPairing.Online;

[ModuleApiVersion(1)]
public sealed class ClothingPairingOnline : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily,
        Author = "Displaced",
        Commands =
        [
            new("CommandPause", OmniCommandCatalog.FormatModule(nameof(ClothingPairingOnline), "pause")),
            new("CommandRestore", OmniCommandCatalog.FormatModule(nameof(ClothingPairingOnline), "restore")),
        ],
        SupportUrls = ["https://github.com/Displaced5/OmniToolbox.Displaced"],
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.Create(OnlineTexts.Source);
    public override bool HasSettings => true;

    // Omni replaces this member with the host-bound configuration before OnStart.
    private Settings config = new();
    private sealed class Settings : SettingsData { }

    private readonly ConcurrentQueue<Action> actions = new();
    private readonly Stopwatch clock = new();
    private readonly RestoreController?[] controllers = new RestoreController?[2];
    private readonly GameSnapshot[] snapshots = new GameSnapshot[2];
    private readonly bool[] failed = new bool[2];
    private SubmissionArbiter arbiter = new();
    private GameAdapter? adapter;
    private ModuleServices? services;
    private string memoryPath = "";
    private double nextSample;
    private double nextSave;
    private bool active;
    private bool dirty;
    private bool persistenceFault;
    private bool standalonePresent;
    private int? clearTarget;
    private ulong? clearCharacter;

    protected override void OnStart(FeatureLifetime activation)
    {
        // Each activation gets new observation state; remembered IDs never arm a restore on login.
        config.Normalize();
        ResetObservation();
        arbiter = new();
        nextSample = nextSave = 0;
        dirty = persistenceFault = standalonePresent = false;
        clearTarget = null;
        memoryPath = Path.Combine(DalamudServices.PluginInterface.GetPluginConfigDirectory(),
            "ClothingPairingOnline", "memories.json");
        MemoryArchive.Load(memoryPath, config);
        services = DalamudServices.PluginInterface.Create<ModuleServices>()
            ?? throw new InvalidOperationException("Accessory services are unavailable.");
        adapter = new GameAdapter(services.Client, services.Player, services.Objects,
            services.Conditions, services.Unlocks, services.Data);
        clock.Restart();
        active = true;
        activation.OnStopping(() =>
        {
            active = false;
            DalamudServices.Framework.Update -= OnUpdate;
            services.Client.TerritoryChanged -= OnTerritoryChanged;
            services.Client.Logout -= OnLogout;
        });
        DalamudServices.Framework.Update += OnUpdate;
        services.Client.TerritoryChanged += OnTerritoryChanged;
        services.Client.Logout += OnLogout;
    }

    protected override void OnStop()
    {
        active = false;
        FlushMemories(true);
        ResetObservation();
        adapter = null;
        services = null;
        clearTarget = null;
        clearCharacter = null;
        clock.Stop();
    }

    private static AccessoryKind Kind(int index) => index == 0 ? AccessoryKind.Fashion : AccessoryKind.Facewear;

    private void ResetObservation()
    {
        Array.Clear(controllers);
        Array.Clear(snapshots);
        Array.Clear(failed);
        while (actions.TryDequeue(out _)) { }
        adapter?.ResetSession();
    }

    private void OnTerritoryChanged(uint territory) =>
        actions.Enqueue(() => adapter?.NotifyZoneChange(clock.Elapsed.TotalSeconds));

    private void OnLogout(int type, int code) => actions.Enqueue(() =>
    {
        FlushMemories(true);
        ResetObservation();
    });

    private void OnUpdate(IFramework framework)
    {
        if (!active || adapter is null) return;
        try
        {
            var conflict = DalamudServices.PluginInterface.InstalledPlugins.Any(p =>
                p.InternalName == "ClothingPairing" && p.IsLoaded);
            if (conflict)
            {
                if (!standalonePresent) FlushMemories(true);
                ResetObservation();
                standalonePresent = true;
                return;
            }
            standalonePresent = false;
            var processed = 0;
            while (processed++ < 100 && actions.TryDequeue(out var action)) action();
            var now = clock.Elapsed.TotalSeconds;
            FlushMemories();
            if (now < nextSample) return;
            nextSample = now + 0.1;
            var requests = new RestoreRequest?[2];
            for (var i = 0; i < controllers.Length; i++)
            {
                if (failed[i]) continue;
                try
                {
                    var controller = controllers[i];
                    var snapshot = adapter.Capture(now, controller?.Memory.LastAccessoryId ?? 0,
                        controller?.HasPendingRequest == true, config.RespectAutoUmbrella, Kind(i));
                    if (snapshot.CharacterId != 0 && controller?.CharacterId != snapshot.CharacterId)
                    {
                        controller = controllers[i] = new RestoreController(snapshot.CharacterId,
                            config.GetMemory(snapshot.CharacterId, Kind(i)));
                        snapshot = adapter.Capture(now, controller.Memory.LastAccessoryId, false,
                            config.RespectAutoUmbrella, Kind(i));
                        dirty = true;
                    }
                    snapshots[i] = snapshot;
                    requests[i] = controller?.Update(snapshot, config.ToOptions(Kind(i)), now);
                }
                catch (Exception ex) { StopChannel(i, ex); }
            }
            if (arbiter.Select(requests.Select(r => r.HasValue).ToArray(), now) is { } selected)
            {
                try
                {
                    var result = adapter.TrySubmit(now, requests[selected]!.Value,
                        config.RespectAutoUmbrella, Kind(selected));
                    if (result.Attempted)
                    {
                        arbiter.Submitted(now);
                        controllers[selected]!.Submitted(now, result.Accepted);
                    }
                    else controllers[selected]!.Defer(result.Message);
                }
                catch (Exception ex) { StopChannel(selected, ex); }
            }
            FlushMemories();
        }
        catch (Exception ex)
        {
            for (var i = 0; i < failed.Length; i++) StopChannel(i, ex);
        }
    }

    private void StopChannel(int index, Exception exception)
    {
        failed[index] = true;
        controllers[index]?.StopForError("此类配饰接口发生异常，已暂停；请重载插件后检查 /xllog");
        DalamudServices.PluginLog.Error(exception, "Clothing pairing online channel {Channel} stopped", index);
    }

    private void FlushMemories(bool force = false)
    {
        dirty |= controllers.Any(c => c?.ConfigurationChanged == true);
        if (!dirty || (!force && clock.Elapsed.TotalSeconds < nextSave)) return;
        try
        {
            MemoryArchive.Save(memoryPath, config);
            foreach (var controller in controllers) controller?.AcknowledgeConfiguration();
            dirty = persistenceFault = false;
            nextSave = clock.Elapsed.TotalSeconds + 1;
        }
        catch (Exception ex)
        {
            if (!persistenceFault) DalamudServices.PluginLog.Error(ex, "Clothing pairing online memory save failed");
            persistenceFault = true;
            nextSave = clock.Elapsed.TotalSeconds + 5;
        }
    }

    public override bool TryHandleCommand(string arguments)
    {
        if (!active || standalonePresent) return false;
        var parts = arguments.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var category = parts.FirstOrDefault();
        var command = category is "face" or "fashion" ? parts.ElementAtOrDefault(1) : category;
        if (command is not ("pause" or "resume" or "restore" or "clear")) return false;
        var targets = Enumerable.Range(0, 2).Where(i => category is not ("face" or "fashion") ||
            Kind(i) == (category == "face" ? AccessoryKind.Facewear : AccessoryKind.Fashion)).ToArray();
        actions.Enqueue(() =>
        {
            foreach (var i in targets)
            {
                if (command == "pause") controllers[i]?.Pause();
                else if (command == "clear") controllers[i]?.ClearMemory();
                else if (config.ToOptions(Kind(i)).Enabled) controllers[i]?.RequestRestore();
            }
        });
        return true;
    }

    private string L(string key) => OmniLoc.Get(this, key);
    private string State(string text) => text.Length == 0 ? "" : L(OnlineTexts.StateKey(text));

    public override bool DrawSettings()
    {
        var changed = false;
        bool Check(string key, bool value, Action<bool> update)
        {
            if (!OmniControls.Checkbox(L(key), ref value)) return false;
            update(value);
            return true;
        }
        ImGui.TextWrapped(L("Behaviour"));
        changed |= Check("Enabled", config.Enabled, v => config.Enabled = v);
        changed |= Check("FashionEnabled", config.FashionEnabled, v => config.FashionEnabled = v);
        changed |= Check("FacewearEnabled", config.FacewearEnabled, v => config.FacewearEnabled = v);
        changed |= Check("Duty", config.RestoreAfterDuty, v => config.RestoreAfterDuty = v);
        changed |= Check("Mount", config.RestoreAfterMount, v => config.RestoreAfterMount = v);
        changed |= Check("Combat", config.RestoreAfterCombat, v => config.RestoreAfterCombat = v);
        changed |= Check("Zone", config.RestoreAfterZone, v => config.RestoreAfterZone = v);
        changed |= Check("ClassJob", config.RestoreFacewearAfterClassJob, v => config.RestoreFacewearAfterClassJob = v);
        changed |= Check("Gearset", config.RestoreFacewearAfterGearset, v => config.RestoreFacewearAfterGearset = v);
        changed |= Check("Umbrella", config.RespectAutoUmbrella, v => config.RespectAutoUmbrella = v);
        if (standalonePresent) ImGui.TextWrapped(L("StandaloneConflict"));
        if (persistenceFault) ImGui.TextWrapped(L("SaveError"));
        if (!active) ImGui.TextWrapped(L("DisabledModule"));
        for (var i = 0; i < controllers.Length; i++) DrawChannel(i);
        return changed;
    }

    private void DrawChannel(int index)
    {
        ImGui.PushID(index);
        ImGui.Separator();
        ImGui.TextUnformatted(L(index == 0 ? "Fashion" : "Facewear"));
        string Display(uint? id) => id is null ? L("Synchronizing") : id == 0 ? L("None") :
            adapter?.Find(id.Value, Kind(index)) is { } info ? $"{info.Name} ({id.Value})" :
            string.Format(L("UnknownAccessory"), id.Value);
        ImGui.TextUnformatted(string.Format(L("Current"), Display(snapshots[index].AccessoryId)));
        ImGui.TextUnformatted(string.Format(L("Remembered"), Display(controllers[index]?.Memory.LastAccessoryId ?? 0)));
        ImGui.TextWrapped(string.Format(L("Status"), failed[index] ? L("ChannelError") :
            controllers[index] is { } controller ? State(controller.Status) : L("WaitingForPlayer")));
        if (controllers[index] is { } current)
            ImGui.TextUnformatted(string.Format(L("Attempts"), current.Attempts, RestoreController.MaximumAttempts));
        ImGui.BeginDisabled(!active || standalonePresent || failed[index] || controllers[index] is null);
        if (OmniControls.SmallButton(L("Pause"), false)) Queue(index, c => c.Pause());
        ImGui.SameLine();
        ImGui.BeginDisabled(!config.ToOptions(Kind(index)).Enabled);
        if (OmniControls.SmallButton(L("Restore"), false)) Queue(index, c => c.RequestRestore());
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (OmniControls.SmallButton(L("Clear"), false))
        {
            clearTarget = index;
            clearCharacter = controllers[index]?.CharacterId;
        }
        if (clearTarget == index && clearCharacter == controllers[index]?.CharacterId)
        {
            ImGui.TextWrapped(L("ConfirmClear"));
            if (OmniControls.SmallButton(L("Confirm"), false))
            {
                Queue(index, c => c.ClearMemory());
                clearTarget = null;
            }
            ImGui.SameLine();
            if (OmniControls.SmallButton(L("Cancel"), false)) clearTarget = null;
        }
        else if (clearTarget == index) clearTarget = null;
        ImGui.EndDisabled();
        ImGui.PopID();
    }

    private void Queue(int index, Action<RestoreController> action)
    {
        var character = controllers[index]?.CharacterId;
        actions.Enqueue(() =>
        {
            if (controllers[index] is { } controller && controller.CharacterId == character) action(controller);
        });
    }

    // Settings reset is intentionally unavailable: clearing memories requires explicit per-item confirmation.
    public override bool ResetSettings() => false;
}
