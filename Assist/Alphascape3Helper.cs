using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace DailyRoutines.ModulesPublic;

public unsafe class Alphascape3Helper : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("Alphascape3HelperTitle"),
        Description = GetLoc("Alphascape3HelperDescription"),
        Category    = ModuleCategories.Assist
    };

    public override void Init()
    {
        DService.ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(DService.ClientState.TerritoryType);
    }

    private void OnZoneChanged(ushort zoneID)
    {
        FrameworkManager.Unregister(OnUpdate);
        if (zoneID != 800) return;
        
        FrameworkManager.Register(false, OnUpdate);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (!Throttler.Throttle("Alphascape3Helper-OnUpdate", 1_000)) return;
        if (DService.ClientState.TerritoryType != 800)
        {
            FrameworkManager.Unregister(OnUpdate);
            return;
        }

        if (Control.GetLocalPlayer() == null) return;
        
        var obj = DService.ObjectTable.FirstOrDefault(x => x.ObjectKind == ObjectKind.BattleNpc && x.DataId == 9638);
        if (obj == null || !obj.IsTargetable) return;

        new UseActionPacket(ActionType.Action, 12911, obj.EntityId, Control.GetLocalPlayer()->Rotation).Send();
    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        FrameworkManager.Unregister(OnUpdate);
    }
}
