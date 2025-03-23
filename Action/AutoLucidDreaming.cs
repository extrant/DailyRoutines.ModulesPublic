using System;
using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace DailyRoutines.Modules;

public unsafe class AutoLucidDreaming : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoLucidDreamingTitle"),
        Description = GetLoc("AutoLucidDreamingDescription"),
        Category = ModuleCategories.Action,
        Author = ["qingsiweisan"]
    };

    // 字段定义
    private static readonly HashSet<uint> ClassJobArr = [6, 7, 15, 19, 20, 21, 23, 24, 26, 27, 28, 33, 35, 36, 40];
    private static readonly uint LucidDreamingActionId = 7562;
    private static DateTime LastLucidDreamingUseTime = DateTime.MinValue;
    private static DateTime LastPlayerActionTime = DateTime.MinValue;
    private Configs Config = null!;
    private static bool IsAbilityLocked = false;
    
    // 常量定义
    private const int AbilityLockTimeMs = 800;
    private const int PlayerInputIgnoreTimeMs = 300;
    private const int GcdStartThresholdMs = 200;
    private const int GcdEndThresholdMs = 500;
    private const float UseInGcdWindowStart = 60;
    private const float UseInGcdWindowEnd = 95;

    // 辅助工具方法
    private static void SetAbilityLock(bool locked) => IsAbilityLocked = locked;
    
    // 配置类定义
    private class Configs : ModuleConfiguration
    {
        public bool OnlyInDuty = true;
        public int MpThreshold = 7000;
    }
    
    public override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };
        Config = LoadConfig<Configs>() ?? new();

        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
        DService.DutyState.DutyRecommenced += OnDutyRecommenced;
        DService.Condition.ConditionChange += OnConditionChanged;
        DService.ClientState.LevelChanged += OnLevelChanged;
        DService.ClientState.ClassJobChanged += OnClassJobChanged;

        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoLucidDreaming-OnlyInDuty"), ref Config.OnlyInDuty))
        {
            SaveConfig(Config);
            TaskHelper.Abort();
            TaskHelper.Enqueue(OneTimeConditionCheck);
        }

        if (ImGui.DragInt("##MpThresholdSlider", ref Config.MpThreshold, 100f, 3000, 9000, $"{GetLoc("AutoLucidDreaming-MpThreshold")}: %d"))
            SaveConfig(Config);
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

    // 事件处理方法
    private void OnDutyRecommenced(object? sender, ushort e) => ResetTaskHelperAndCheck();
    private void OnLevelChanged(uint classJobId, uint level) => ResetTaskHelperAndCheck();
    
    private void OnTerritoryChanged(ushort zone)
    {
        TaskHelper.Abort();
        if (Config.OnlyInDuty && GameMain.Instance()->CurrentContentFinderConditionId == 0) return;
        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    private void OnClassJobChanged(uint classJobId)
    {
        TaskHelper.Abort();
        if (!ClassJobArr.Contains(classJobId)) return;
        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag is not ConditionFlag.InCombat) return;
        TaskHelper.Abort();
        if (value) TaskHelper.Enqueue(OneTimeConditionCheck);
    }
    
    private void ResetTaskHelperAndCheck()
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(OneTimeConditionCheck);
    }

    // 主要处理逻辑方法
    private bool? OneTimeConditionCheck()
    {
        // 快速返回条件检查
        if ((Config.OnlyInDuty && GameMain.Instance()->CurrentContentFinderConditionId == 0) ||
            GameMain.IsInPvPArea() || GameMain.IsInPvPInstance() ||
            !DService.Condition[ConditionFlag.InCombat])
            return true;

        TaskHelper.Enqueue(MainProcess);
        return true;
    }

    private bool Cycle(int delayMs = 0)
    {
        if (delayMs > 0) TaskHelper.DelayNext(delayMs);
        TaskHelper.Enqueue(MainProcess);
        return true;
    }

    private bool? MainProcess()
    {
        // 基本状态检查
        if (BetweenAreas || !IsScreenReady() || OccupiedInEvent ||
            DService.ClientState.LocalPlayer is not { } localPlayer ||
            !DService.Condition[ConditionFlag.InCombat])
            return Cycle(1_000);
            
        // 职业和技能检查
        if (!ClassJobArr.Contains(localPlayer.ClassJob.RowId) ||
            !IsActionUnlocked(LucidDreamingActionId))
            return true;

        TaskHelper.Enqueue(PreventAbilityUse, "PreventAbilityUse", 5_000, true, 1);
        TaskHelper.Enqueue(UseLucidDreaming, "UseLucidDreaming", 5_000, true, 1);
        return Cycle(1_000);
    }

    private bool? PreventAbilityUse()
    {
        var timeSinceLastUse = (DateTime.Now - LastLucidDreamingUseTime).TotalMilliseconds;
        var shouldLock = timeSinceLastUse < AbilityLockTimeMs;
        
        SetAbilityLock(shouldLock);
        
        if (shouldLock)
        {
            var remainingLockTime = AbilityLockTimeMs - (int)timeSinceLastUse;
            DService.Chat?.PrintError($"能力技已锁定 ({remainingLockTime}ms)");
            TaskHelper.DelayNext(Math.Min(remainingLockTime, 100));
        }
        
        return true;
    }

    private bool? UseLucidDreaming()
    {
        // 基础状态检查
        if (DService.ClientState.LocalPlayer is not { } localPlayer) return false;
        var character = localPlayer.ToBCStruct();
        if (character == null) return true;
        
        var statusManager = character->StatusManager;
        var currentMp = localPlayer.CurrentMp;
        var timeSinceLastAction = (DateTime.Now - LastPlayerActionTime).TotalMilliseconds;
        var timeSinceLastUse = (DateTime.Now - LastLucidDreamingUseTime).TotalMilliseconds;
        
        // 快速返回条件检查
        if (timeSinceLastAction < PlayerInputIgnoreTimeMs ||
            timeSinceLastUse < AbilityLockTimeMs ||
            currentMp >= Config.MpThreshold)
            return true;
            
        // 检查技能状态
        var actionManager = ActionManager.Instance();
        if (actionManager->GetActionStatus(ActionType.Action, LucidDreamingActionId) != 0 ||
            statusManager.HasStatus(1204) ||
            character->Mode == CharacterModes.AnimLock ||
            character->IsCasting ||
            actionManager->AnimationLock > 0)
            return true;

        // GCD检测逻辑 - 使用内置的优化百分比窗口模式
        var gcdRecast = actionManager->GetRecastGroupDetail(58);
        if (gcdRecast->IsActive != 0)
        {
            var gcdTotal = actionManager->GetRecastTimeForGroup(58);
            var gcdElapsed = gcdRecast->Elapsed;
            
            // 使用百分比窗口模式
            var gcdProgressPercent = gcdElapsed / gcdTotal * 100;
            if (gcdProgressPercent < UseInGcdWindowStart || gcdProgressPercent > UseInGcdWindowEnd)
                return true;
        }

        // 使用醒梦
        var capturedTime = DateTime.Now;
        TaskHelper.Enqueue(() =>
        {
            // 更新LastPlayerActionTime（原UseAction方法中的逻辑）
            if (LucidDreamingActionId != 7562) LastPlayerActionTime = DateTime.Now;
            
            // 检查技能锁定
            if (IsAbilityLocked)
                return false;
            
            // 使用UseActionManager
            var result = UseActionManager.UseAction(ActionType.Action, LucidDreamingActionId);
            if (result) LastLucidDreamingUseTime = capturedTime;
            return result;
        }, $"UseAction_{LucidDreamingActionId}", 5_000, true, 1);
        return true;
    }
    
}
