using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCancelCast : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCancelCastTitle"),
        Description = GetLoc("AutoCancelCastDescription"),
        Category    = ModuleCategories.Action,
    };

    private static readonly HashSet<ObjectKind> ValidObjectKinds = [ObjectKind.Pc, ObjectKind.BattleNpc];

    private static readonly HashSet<ConditionFlag> ValidConditions = [ConditionFlag.Casting, ConditionFlag.Casting87];

    private static HashSet<uint> TargetAreaActions { get; } =
        LuminaGetter.Get<LuminaAction>()
                    .Where(x => x.TargetArea)
                    .Select(x => x.RowId).ToHashSet();
    
    private static bool IsOnCasting;

    protected override void Init()
    {
        DService.Condition.ConditionChange += OnConditionChanged;
        FrameworkManager.Register(OnUpdate);
    }

    private static void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (!ValidConditions.Contains(flag)) return;
        
        IsOnCasting = value;
    }

    private static void OnUpdate(IFramework _)
    {
        if (!IsOnCasting) return;
        if (!IsCasting)
        {
            IsOnCasting = false;
            return;
        }

        var player = DService.ObjectTable.LocalPlayer;
        if (player.CastActionType != ActionType.Action || TargetAreaActions.Contains(player.CastActionId))
        {
            IsOnCasting = false;
            return;
        }

        var obj = CharacterManager.Instance()->LookupBattleCharaByEntityId((uint)player.CastTargetObjectId);
        if (obj == null                                 ||
            !ValidObjectKinds.Contains(obj->ObjectKind) ||
            obj->Health == 0                            ||
            ActionManager.CanUseActionOnTarget(player.CastActionId, (GameObject*)obj))
            return;

        if (Throttler.Throttle("AutoCancelCast-CancelCast")) 
            ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.CancelCast);
    }

    protected override void Uninit()
    {
        DService.Condition.ConditionChange -= OnConditionChanged;
        FrameworkManager.Unregister(OnUpdate);
    }
}
