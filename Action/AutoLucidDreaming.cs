using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace DailyRoutines.Modules;

public class AutoLucidDreaming : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("AutoLucidDreamingTitle"),       // "自动释放醒梦"
        Description = GetLoc("AutoLucidDreamingDescription"), // "当MP低于7000时，自动尝试释放醒梦"
        Category    = ModuleCategories.Action,
        Author      = ["qingsiweisan"]
    };
    
    // 法系和治疗职业ID
    private readonly HashSet<uint> s_ClassJobArr = [6, 7, 15, 19, 20, 21, 23, 24, 25, 26, 27, 28, 33, 35, 36, 40];
    
    private readonly uint s_LucidDreamingActionId = 7562;
    
    private Configs Config = null!;

    public override void Init()
    {   
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };
        Config     =   LoadConfig<Configs>() ?? new();

        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
        DService.DutyState.DutyRecommenced    += OnDutyRecommenced;
        DService.Condition.ConditionChange    += OnConditionChanged;
        DService.ClientState.LevelChanged     += OnLevelChanged;
        DService.ClientState.ClassJobChanged  += OnClassJobChanged;

        TaskHelper.Enqueue(OneTimeConditionCheck);
    }
    
    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoLucidDreaming-OnlyInDuty"), ref Config.OnlyInDuty)) // "只在副本中使用"
        {
            SaveConfig(Config);
            
            TaskHelper.Abort();
            TaskHelper.Enqueue(OneTimeConditionCheck);
        }

        if (ImGui.DragInt("##MpThresholdSlider", ref Config.MpThreshold, 100f, 3000, 9000, $"{GetLoc("AutoLucidDreaming-MpThreshold")}: %d")) // "MP阈值"
            SaveConfig(Config);
    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnTerritoryChanged;
        DService.DutyState.DutyRecommenced    -= OnDutyRecommenced;
        DService.Condition.ConditionChange    -= OnConditionChanged;
        DService.ClientState.LevelChanged     -= OnLevelChanged;
        DService.ClientState.ClassJobChanged  -= OnClassJobChanged;

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

        if (Config.OnlyInDuty && GameMain.Instance()->CurrentContentFinderConditionId == 0) return;
        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    // 等级变更
    private void OnLevelChanged(uint classJobId, uint level)
    {
        TaskHelper.Abort();
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
        if (value) TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    private unsafe bool? OneTimeConditionCheck()
    {
        // re-entry by OnTerritoryChanged()
        if (Config.OnlyInDuty && GameMain.Instance()->CurrentContentFinderConditionId == 0) return true;
        if (GameMain.IsInPvPArea() || GameMain.IsInPvPInstance()) return true;
        // re-entry by OnConditionChanged()
        if (!DService.Condition[ConditionFlag.InCombat]) return true;

        TaskHelper.Enqueue(MainProcess);
        return true;
    }

    private bool Cycle(int delayMs = 0)
    {
        if (delayMs > 0) TaskHelper.DelayNext(delayMs);
        TaskHelper.Enqueue(MainProcess);
        return true;
    }

    private unsafe bool? MainProcess()
    {
        if (BetweenAreas || !IsScreenReady() || OccupiedInEvent) return Cycle(1_000);
        if (DService.ClientState.LocalPlayer is not { } localPlayer) return Cycle(1_000);
        if (!s_ClassJobArr.Contains(localPlayer.ClassJob.RowId)) return true;
        if (!IsActionUnlocked(s_LucidDreamingActionId)) return true;
        if (!DService.Condition[ConditionFlag.InCombat]) return Cycle(1_000);

        TaskHelper.Enqueue(UseLucidDreaming, "UseLucidDreaming", 5_000, true, 1);
        return Cycle(1_000);
    }

    private unsafe bool? UseLucidDreaming()
    {
        if (DService.ClientState.LocalPlayer is not { } localPlayer) return false;
        var actionManager = ActionManager.Instance();
        var character = localPlayer.ToBCStruct();
        var statusManager = character->StatusManager;

        // 使用 Dalamud API 获取当前 MP
        var player = DService.ClientState.LocalPlayer;
        var currentMp = player.CurrentMp;
        
        // MP高于阈值，不需要使用醒梦
        if (currentMp >= Config.MpThreshold) return true;
        // 醒梦技能不可用
        if (actionManager->GetActionStatus(ActionType.Action, s_LucidDreamingActionId) != 0) return true;
        // 已有醒梦状态
        if (statusManager.HasStatus(1204)) return true;

        TaskHelper.Enqueue(() => UseActionManager.UseAction(ActionType.Action, s_LucidDreamingActionId),
                           $"UseAction_{s_LucidDreamingActionId}", 5_000, true, 1);
        return true;
    }
    
    private class Configs : ModuleConfiguration
    {
        public bool OnlyInDuty = true;
        public int MpThreshold = 7000;
    }
}