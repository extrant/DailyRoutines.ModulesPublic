using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCancelStarContributor : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCancelStarContributorTitle"),
        Description = GetLoc("AutoCancelStarContributorDescription"),
        Category    = ModuleCategories.General,
        Author      = ["Shiyuvi"]
    };
    
    private const uint StarContributorBuffID = 4409;
    
    public override void Init() => 
        DService.ClientState.TerritoryChanged += OnZoneChanged;

    private static void OnZoneChanged(ushort zone)
    {
        FrameworkManager.Unregister(OnUpdate);
        if (GameState.TerritoryIntendedUse != 60) return;
        
        FrameworkManager.Register(OnUpdate, throttleMS: 10_000);
    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        FrameworkManager.Unregister(OnUpdate);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (GameState.TerritoryIntendedUse != 60)
        {
            FrameworkManager.Unregister(OnUpdate);
            return;
        }
        
        if (BetweenAreas || DService.ObjectTable.LocalPlayer is not { } localPlayer) return;
        
        var statusManager = localPlayer.ToStruct()->StatusManager;
        if (!statusManager.HasStatus(StarContributorBuffID)) return;
        
        StatusManager.ExecuteStatusOff(StarContributorBuffID);
    }
}
