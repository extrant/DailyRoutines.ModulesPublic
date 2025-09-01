using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DailyRoutines.ModulesPublic;

public class QuickChangeAdjoiningArea : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = "快速切换相邻区域",
        Description = "在区域内任意坐标直接切换至相邻区域, 而无需抵达过图线",
        Category    = ModuleCategories.General,
        Author      = ["Sbago"]
    };
    
    private static ModuleConfig Config = null!;

    protected override void Init()
    {
        TaskHelper ??= new();
        Config     =   LoadConfig<ModuleConfig>() ?? new();
    }

    protected override unsafe void ConfigUI()
    {
        if (!LayoutWorld.Instance()->ActiveLayout->InstancesByType.TryGetValuePointer(InstanceType.ExitRange, out var exitRanges)) return;
        
        foreach (var exitRange in exitRanges->Value->Values)
        {
            var pExitRange = (ExitRangeLayoutInstance*)exitRange.Value;
            var pPopRange  = pExitRange->PopRangeLayoutInstance;
            if (pPopRange == null) continue;
            
            if (ImGui.Button($"{LuminaWrapper.GetZonePlaceName(pExitRange->TerritoryType)} {*pPopRange->Base.GetTranslationImpl()}"))
                AgentMap.Instance()->SetFlagMapMarker(GameState.TerritoryType, GameState.Map, *pPopRange->Base.GetTranslationImpl());
                        
            ImGui.SameLine();
            if (ImGui.Button($"Change###{(long)pExitRange:X}"))
            {
                TaskHelper.Enqueue(() => PopRangeManager.Instance()->PopRange((ILayoutInstance*)pExitRange));
                /*var agentMap = AgentMap.Instance();
                if (agentMap->IsFlagMarkerSet && agentMap->FlagMapMarker.TerritoryId == GameState.TerritoryType)
                {
                    TaskHelper.Enqueue(() =>
                    {
                        if (!BetweenAreas) return true;

                        if (LocalPlayerState.DistanceTo2D(new(agentMap->FlagMapMarker.XFloat, agentMap->FlagMapMarker.YFloat)) > 9)
                        {
                            var pos = new Vector3(agentMap->FlagMapMarker.XFloat, LocalPlayerState.Object.Position.Y, agentMap->FlagMapMarker.YFloat);
                            MovementManager.TPPlayerAddress(pos);
                        }
                        else
                            MovementManager.TPGround();
                                
                        return false;
                    });
                }*/
                        
                TaskHelper.Enqueue(() => BetweenAreas);
                TaskHelper.Enqueue(() => PopRangeManager.Instance()->PopRange((ILayoutInstance*)pExitRange));
            }
        }
    }
}

public class ModuleConfig : ModuleConfiguration
{
    public Dictionary<uint, List<uint>> Record = new();
}
