using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace ClothingPairing.Online;

internal sealed class ModuleServices
{
    [PluginService] public IClientState Client { get; private set; } = null!;
    [PluginService] public IPlayerState Player { get; private set; } = null!;
    [PluginService] public IObjectTable Objects { get; private set; } = null!;
    [PluginService] public ICondition Conditions { get; private set; } = null!;
    [PluginService] public IUnlockState Unlocks { get; private set; } = null!;
    [PluginService] public IDataManager Data { get; private set; } = null!;
}
