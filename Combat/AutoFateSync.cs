using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace DailyRoutines.Modules;

public class AutoFateSync : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoFateSyncTitle"),
        Description = GetLoc("AutoFateSyncDescription"),
        Category = ModuleCategories.Combat,
    };

    private static readonly Throttler<string> Throttler = new();
    private static Config ModuleConfig = null!;

    private static readonly CompSig FateDirectorSetupSig = new("E8 ?? ?? ?? ?? 48 39 37");
    private delegate nint FateDirectorSetupDelegate(uint rowID, nint a2, nint a3);
    private static Hook<FateDirectorSetupDelegate>? FateDirectorSetupHook;

    private static CancellationTokenSource? CancelSource;

    private static readonly uint[] TankStanceStatuses = [79, 91, 743, 1833];
    private static readonly Dictionary<uint, uint> TankStanceActions = new()
    {
        // 剑术师 / 骑士
        { 1, 28 },
        { 19, 28 },
        // 斧术师 / 战士
        { 3, 48 },
        { 21, 48 },
        // 暗黑骑士
        { 32, 3629 },
        // 绝枪战士
        { 37, 16142 }
    };
    
    public override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };
        ModuleConfig = LoadConfig<Config>() ?? new();
        CancelSource ??= new();
        
        FateDirectorSetupHook ??= 
            DService.Hook.HookFromSignature<FateDirectorSetupDelegate>(FateDirectorSetupSig.Get(), FateDirectorSetupDetour);
        FateDirectorSetupHook.Enable();
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(50f * GlobalFontScale);
        if (ImGui.InputFloat(Lang.Get("AutoFateSync-Delay"), ref ModuleConfig.Delay, 0, 0, "%.1f"))
            ModuleConfig.Delay = Math.Max(0, ModuleConfig.Delay);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            SaveConfig(ModuleConfig);
            CancelSource.Cancel();
        }
        ImGuiOm.HelpMarker(Lang.Get("AutoFateSync-DelayHelp"));

        if (ImGui.Checkbox(Lang.Get("AutoFateSync-IgnoreMounting"), ref ModuleConfig.IgnoreMounting))
            SaveConfig(ModuleConfig);
        ImGuiOm.HelpMarker(Lang.Get("AutoFateSync-IgnoreMountingHelp"));
        if (ImGui.Checkbox(Lang.Get("AutoFateSync-AutoTankStance"), ref ModuleConfig.AutoTankStance))
            SaveConfig(ModuleConfig);
    }

    private unsafe nint FateDirectorSetupDetour(uint rowID, nint a2, nint a3)
    {
        var original = FateDirectorSetupHook.Original(rowID, a2, a3);
        if (rowID == 102401 && FateManager.Instance()->CurrentFate != null) HandleFateEnter();
        return original;
    }

    private unsafe void HandleFateEnter()
    {
        if (ModuleConfig.IgnoreMounting && (DService.Condition[ConditionFlag.InFlight] || IsOnMount))
        {
            FrameworkManager.Register(false, OnFlying);
            return;
        }

        if (ModuleConfig.Delay > 0)
        {
            DService.Framework.RunOnTick(() =>
            {
                var manager = FateManager.Instance();
                if (manager->CurrentFate == null || DService.ClientState.LocalPlayer == null) return;

                ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.FateLevelSync, manager->CurrentFate->FateId, 1);
                if (ModuleConfig.AutoTankStance) EnqueueStanceTask();
            }, TimeSpan.FromSeconds(ModuleConfig.Delay), 0, CancelSource.Token);
            return;
        }
    
        if (ModuleConfig.AutoTankStance) EnqueueStanceTask();
        ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.FateLevelSync, FateManager.Instance()->CurrentFate->FateId, 1);
    }

    private unsafe void EnqueueStanceTask()
    {
        TaskHelper.Abort();
        TaskHelper.DelayNext(500);
        TaskHelper.Enqueue(() => CancelSource.Token.IsCancellationRequested || 
                                 FateManager.Instance()->CurrentFate == null ||
                                 (!IsOnMount && !OccupiedInEvent));
        TaskHelper.Enqueue(() =>
        {
            if (CancelSource.Token.IsCancellationRequested || FateManager.Instance()->CurrentFate == null || IsOnMount || OccupiedInEvent) return;
            if (DService.ClientState.LocalPlayer.ClassJob.RowId != 0 && DService.ClientState.LocalPlayer.IsTargetable)
            {
                if (TankStanceActions.TryGetValue(DService.ClientState.LocalPlayer.ClassJob.RowId, out var actionId))
                {
                    var battlePlayer = (BattleChara*)DService.ClientState.LocalPlayer.Address;
                    if (!TankStanceStatuses.Any(status => battlePlayer->StatusManager.HasStatus(status)))
                    {
                        UseActionManager.UseAction(ActionType.Action, actionId);
                    }
                }
            }
        });
    }
    private unsafe void OnFlying(IFramework _)
    {
        if (!Throttler.Throttle("OnFlying")) return;

        var currentFate = FateManager.Instance()->CurrentFate;
        if (currentFate == null || DService.ClientState.LocalPlayer == null)
        {
            FrameworkManager.Unregister(OnFlying);
            return;
        }

        if (DService.Condition[ConditionFlag.InFlight] || IsOnMount) return;

        ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.FateLevelSync, currentFate->FateId, 1);
        if (ModuleConfig.AutoTankStance) EnqueueStanceTask();
        FrameworkManager.Unregister(OnFlying);
    }

    public override void Uninit()
    {
        CancelSource?.Cancel();
        CancelSource?.Dispose();
        CancelSource = null;
        
        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public bool IgnoreMounting = true;
        public float Delay = 3f;
        public bool AutoTankStance;
    }
}
