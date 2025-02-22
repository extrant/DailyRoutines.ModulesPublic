using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;


namespace DailyRoutines.Modules;

public class AutoUseManual : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoUseManual"),               //"自动使用生存学/工程学指南"
        Description = GetLoc("AutoUseManualDescribe"), //"当前职业为非满级生产，采集职业时，自动按从最高级优先吃指南获得经验加成buff"
        Category = ModuleCategories.Action,
    };

    private static Config ModuleConfig = null!;
    private static DateTime LastTime = DateTime.MinValue;
    private const int CooldownSeconds = 10;
    

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        TaskHelper ??= new TaskHelper { TimeLimitMS = 60000 };
        
        FrameworkManager.Register(true, OnUpdate);
    }
    
    private static readonly HashSet<uint> GatherJobs = [16, 17, 18];
    
    private static readonly HashSet<uint> ArtJobs = [8, 9, 10, 11, 12, 13, 14, 15];
    
    public static bool IsGatherJob()
    {
        var id = DService.ClientState.LocalPlayer.ClassJob.RowId;
        return GatherJobs.Contains(id);
    }
    
    public static bool IsArtJob()
    {
        var id = DService.ClientState.LocalPlayer.ClassJob.RowId;
        return ArtJobs.Contains(id);
    }

    public static unsafe bool IsMaxLevel()
    {
        var level = DService.ClientState.LocalPlayer.Level;
        return level == UIState.Instance()->PlayerState.MaxLevel;
    }
    
    private static bool IsCooldownElapsed() => (DateTime.Now - LastTime).TotalSeconds >= CooldownSeconds;
    private void OnUpdate(IFramework framework)
    {
        var id = DService.ClientState.LocalPlayer.ClassJob.RowId;
        
        if (IsValidState() && IsCooldownElapsed())
        {
            if (IsGatherJob() && !IsMaxLevel() && !HasGather())
            {
                GatherCheckAndUseItems();
            }

            if (IsArtJob() && !IsMaxLevel() && !HasArt())
            {
                ArtCheckAndUseItems();
            }
        }
    }
    
    private void GatherCheckAndUseItems()
    {
        uint[] itemIds = { 26553, 12668, 4635, 4633 };

        foreach (uint itemId in itemIds)
        {
            CheckAndUse(itemId);
        }
    }
    

    private void ArtCheckAndUseItems()
    {
        uint[] itemIds = { 26554, 12667, 4634, 4632 };

        foreach (uint itemId in itemIds)
        {
            CheckAndUse(itemId);
        }
    }
    
    private bool CheckAndUse(uint itemid)
    {
        if (GetItemCount(itemid, false) == 0)
            return false;
        UseItem(itemid);
        return true;
    }

    private bool UseItem(uint itemId)
    {
        TaskHelper.Abort();
        UseActionManager.UseActionLocation(ActionType.Item, itemId, 0xE0000000, default, 0xFFFF);
        LastTime = DateTime.Now; // 更新最后使用时间
        TaskHelper.DelayNext(3_000);
        return true;
    }
    

    public static unsafe uint GetItemCount(uint itemId, bool isHq = false)
    {
        IntPtr inventoryManagerPtr = (IntPtr)InventoryManager.Instance();

        if (inventoryManagerPtr == IntPtr.Zero)
        {
            return 0;
        }

        InventoryManager* inventoryManager = (InventoryManager*)inventoryManagerPtr;

        return (uint)inventoryManager->GetInventoryItemCount(itemId, isHq, true, true, (short)0);
    }
    

    public override void ConfigUI()
    {
        ImGui.Text(GetLoc("SendNotification"));//"发送通知:"
        ImGui.SameLine();
        ImGui.Checkbox("##AutoCheckgysahl_greensUsageSendNotice", ref ModuleConfig.SendNotice);
        SaveConfig(ModuleConfig);
    }
    

    private static unsafe bool IsValidState() =>
        !BetweenAreas &&
        !OccupiedInEvent &&
        !IsCasting &&
        DService.ClientState.LocalPlayer != null &&
        IsScreenReady() &&
        ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 2) == 0;

    private IPlayerCharacter? me = DService.ClientState.LocalPlayer;
    private bool HasGather()
    {
        if (DService.ClientState.LocalPlayer is null) return false;
        if (HasAura(46))
            return true;
        return false;
    }
    
    private bool HasArt()
    {
        if (DService.ClientState.LocalPlayer is null) return false;
        if (HasAura(45))
            return true;
        return false;
    }
    
    public unsafe bool HasAura(uint id)
    {
        var statusManager = me.ToBCStruct()->StatusManager;
        if (statusManager.HasStatus(id))
            return true;
        return false;
    }
    
    private class Config : ModuleConfiguration
    {
        
        public bool SendNotice = true;
    }
    
    public override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        base.Uninit();
    }
}
