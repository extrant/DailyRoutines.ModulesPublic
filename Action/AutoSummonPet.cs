using System.Collections.Frozen;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.DutyState;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;

namespace DailyRoutines.ModulesPublic;

public class AutoSummonPet : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoSummonPetTitle"),
        Description = Lang.Get("AutoSummonPetDescription"),
        Category    = ModuleCategory.Action
    };

    protected override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeoutMS = 30_000 };

        IClientState.Instance().TerritoryChanged += OnZoneChanged;
        IDutyState.Instance().DutyRecommenced    += OnDutyRecommenced;
    }

    protected override void Uninit()
    {
        IDutyState.Instance().DutyRecommenced    -= OnDutyRecommenced;
        IClientState.Instance().TerritoryChanged -= OnZoneChanged;
    }

    // 重新挑战
    private void OnDutyRecommenced
    (
        IDutyStateEventArgs args
    )
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    // 进入副本
    private void OnZoneChanged
    (
        uint u
    )
    {
        TaskHelper.Abort();

        if (!GameState.IsInPVEActonZone) return;

        TaskHelper.DelayNext(1_000);
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private unsafe bool CheckCurrentJob()
    {
        if (ICondition.Instance().IsBetweenAreas         ||
            !UIModule.IsScreenReady()                            ||
            ICondition.Instance()[ConditionFlag.Casting] ||
            IObjectTable.Instance().LocalPlayer is not { IsTargetable: true } localPlayer) return false;

        if (!SummonActions.TryGetValue(LocalPlayerState.ClassJob, out var actionID))
        {
            TaskHelper.Abort();
            return true;
        }

        var state = CharacterManager.Instance()->LookupPetByOwnerObject(localPlayer.ToStruct()) != null;

        if (state)
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Enqueue(() => UseActionManager.Instance().UseAction(ActionType.Action, actionID));
        TaskHelper.DelayNext(1_000);
        TaskHelper.Enqueue(CheckCurrentJob);
        return true;
    }

    #region 常量

    private static readonly FrozenDictionary<uint, uint> SummonActions = new Dictionary<uint, uint>
    {
        [28] = 17215, // 学者
        [26] = 25798, // 秘术师 / 召唤师
        [27] = 25798
    }.ToFrozenDictionary();

    #endregion
}
