using System.Collections.Generic;

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

using Lumina.Excel.GeneratedSheets;
using OmenTools;
using OmenTools.Helpers;
using OmenTools.Infos;

using DailyRoutines.Managers;
using DailyRoutines.Abstracts;
using ImGuiNET;
using static DailyRoutines.AutoPeloton.AutoPeloton;
using DailyRoutines.Helpers;
using System.Linq;
using Lumina.Excel.GeneratedSheets2;

namespace DailyRoutines.AutoPeloton;

public class AutoPeloton : DailyModuleBase
{
    private readonly uint s_PelotoningActionId = 7557;

    // 诗人 机工 舞者
    private readonly HashSet<uint> s_ClassJobArr = [23, 31, 38];

    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoPelotonTitle"), // "自动释放速行"
        Description = GetLoc("AutoPelotonDescription"), // "使用远敏职业时，自动尝试释放速行"
        Category = ModuleCategories.Action,
        Author = ["N/A"],
    };

    public class Configs : ModuleConfiguration
    {
        public bool OnlyInDuty = true;
        public bool DisableInWalk = true;
    };

    public Configs Config { get; private set; }
    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoPeloton-OnlyInDuty"), ref Config.OnlyInDuty)) // "只在副本中使用"
        {
            SaveConfig(Config);
            TaskHelper.Abort();
            TaskHelper.Enqueue(OneTimeConditionCheck);
        }
        if (ImGui.Checkbox(GetLoc("AutoPeloton-DisableInWalk"), ref Config.DisableInWalk)) // "走路模式时禁用"
        {
            SaveConfig(Config);
        }
    }

    public override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };
        Config = LoadConfig<Configs>() ?? new Configs();

        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
        DService.DutyState.DutyRecommenced += OnDutyRecommenced;
        DService.Condition.ConditionChange += OnConditionChanged;
        DService.ClientState.LevelChanged += OnLevelChanged;
        DService.ClientState.ClassJobChanged += OnClassJobChanged;

        if (!TaskHelper.IsBusy) TaskHelper.Enqueue(OneTimeConditionCheck);

    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnTerritoryChanged;
        DService.DutyState.DutyRecommenced -= OnDutyRecommenced;
        DService.Condition.ConditionChange -= OnConditionChanged;
        DService.ClientState.LevelChanged -= OnLevelChanged;
        DService.ClientState.ClassJobChanged -= OnClassJobChanged;

        if (Config != null) SaveConfig(Config);

        base.Uninit();
    }

    // 重新挑战
    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    // 地图变更
    private unsafe void OnTerritoryChanged(ushort zone)
    {
        TaskHelper.Abort();

        if (Config.OnlyInDuty && (GameMain.Instance()->CurrentContentFinderConditionId == 0)) return;

        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    // 等级变更
    private void OnLevelChanged(uint classJobId, uint level)
    {
        TaskHelper.Abort();

        if (!HelpersOm.IsActionUnlocked(s_PelotoningActionId)) return;

        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    // 职业变更
    private void OnClassJobChanged(uint classJobId)
    {
        TaskHelper.Abort();

        if (!s_ClassJobArr.Contains(classJobId)) return;

        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    // 战斗状态
    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag is not ConditionFlag.InCombat) return;
        TaskHelper.Abort();
        if (!value) TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    private unsafe bool? OneTimeConditionCheck()
    {
        // re-entry by OnTerritoryChanged()
        if (Config.OnlyInDuty && (GameMain.Instance()->CurrentContentFinderConditionId == 0)) return true;
        if (GameMain.IsInPvPArea() || GameMain.IsInPvPInstance()) return true;
        // re-entry by OnConditionChanged()
        if (DService.Condition[ConditionFlag.InCombat]) return true;

        TaskHelper.Enqueue(MainProcess);
        return true;
    }

    private bool Cycle(int delayMs = 0)
    {
        if (delayMs > 0)
        {
            TaskHelper.DelayNext(delayMs);
        }
        TaskHelper.Enqueue(MainProcess);
        return true;
    }

    private unsafe bool? MainProcess()
    {
        if (InfosOm.BetweenAreas || !HelpersOm.IsScreenReady() || InfosOm.OccupiedInEvent) return Cycle(1_000);
        if (DService.ClientState.LocalPlayer is not { } localPlayer) return Cycle(1_000);
        if (!s_ClassJobArr.Contains(localPlayer.ClassJob.Id)) return true;
        if (!HelpersOm.IsActionUnlocked(s_PelotoningActionId)) return true;

        if (Config.DisableInWalk && (Control.Instance()->IsWalking)) return Cycle(1_000);

        TaskHelper.Enqueue(UsePeloton, "UsePeloton", 5_000, true, 1);

        return Cycle(1_000);
    }

    private unsafe bool? UsePeloton()
    {
        if (DService.ClientState.LocalPlayer is not { } localPlayer) return false;
        ActionManager* am = ActionManager.Instance();
        var statusManager = localPlayer.ToBCStruct()->StatusManager;

        // PeletonNotReady
        if (am->GetActionStatus(ActionType.Action, s_PelotoningActionId) != 0) return true;
        // AlreadyHasPeletonBuff
        if (statusManager.HasStatus(1199) || statusManager.HasStatus(50)) return true;
        // NotMoving
        if (AgentMap.Instance()->IsPlayerMoving != 1) return true;

        TaskHelper.Enqueue(() => UseActionManager.UseAction(
            ActionType.Action,
            s_PelotoningActionId),
            $"UseAction_{s_PelotoningActionId}",
            5_000,
            true,
            1
        );
        return true;
    }
}
