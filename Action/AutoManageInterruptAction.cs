using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

public unsafe class AutoManageInterruptAction : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoManageInterruptActionTitle"),
        Description = GetLoc("AutoManageInterruptActionDescription"),
        Category = ModuleCategories.Action,
    };

    private static readonly HashSet<uint> InterruptActions = [7538, 7551];
    
    public override void Init()
    {
        UseActionManager.Register(OnPreUseAction);
    }

    private static void OnPreUseAction(
        ref bool                        isPrevented,
        ref ActionType                  actionType, ref uint actionID, ref ulong targetID, ref uint extraParam,
        ref ActionManager.UseActionMode queueState, ref uint comboRouteID, ref bool* outOptAreaTargeted)
    {
        if (actionType != ActionType.Action || !InterruptActions.Contains(actionID)) return;
        if (DService.Targets.Target is IBattleChara { IsCasting: true, IsCastInterruptible: true }) return;
        
        isPrevented = true;
    }

    public override void Uninit()
    {
        UseActionManager.Unregister(OnPreUseAction);
    }
}
