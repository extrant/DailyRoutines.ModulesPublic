using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;


namespace DailyRoutines.Modules;

public class AutoCheckgysahl_greensUsage : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoCheckgysahl_greensUsageTitle"),               //"自动召唤搭档"
        Description = GetLoc("AutoCheckgysahl_greensUsageDescription"), //"在野外，自动使用基萨尔野菜召唤陆行鸟搭档"
        Category = ModuleCategories.Action,
    };

    private static Config ModuleConfig = null!;

    private static uint SelectedItem = 4868;
    private static bool SelectItemIsHQ = false;

    private static DateTime Lastgysahl_greensUsageTime = DateTime.MinValue;
    private const int gysahl_greensUsageCooldownSeconds = 10;
    

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        TaskHelper ??= new TaskHelper { TimeLimitMS = 60000 };
        
        DService.Framework.Update += OnFrameworkUpdate;
    }
    
    private void OnFrameworkUpdate(IFramework iFramework)
    {
        if (IsValidState() && IsCooldownElapsed() && PatonaTimeLeft()< ModuleConfig.RefreshThreshold)
        {
            EnqueueUsegysahl_greens();
        }
    }

    public static unsafe uint GetItemCount(uint itemId, bool isHq)
    {
        // 获取 InventoryManager 的实例指针
        IntPtr inventoryManagerPtr = (IntPtr)InventoryManager.Instance();

        // 将指针转换为 InventoryManager 结构体
        InventoryManager inventoryManager = Marshal.PtrToStructure<InventoryManager>(inventoryManagerPtr);

        // 调用 GetInventoryItemCount 方法
        return (uint)inventoryManager.GetInventoryItemCount(itemId, isHq, true, true, (short)0);
    }

    private bool? TakeItem()
    {
        TaskHelper.Abort();

        if (GetItemCount(SelectedItem, SelectItemIsHQ) == 0)
        {
            return false;
        }

        UseActionManager.UseActionLocation(ActionType.Item, SelectedItem, 0xE0000000, default, 0xFFFF);

        Lastgysahl_greensUsageTime = DateTime.Now; // 更新最后使用时间
        TaskHelper.DelayNext(3_000);
        return true;
    }
    

    private void EnqueueUsegysahl_greens()
    {
        if (IsValidState() && IsCooldownElapsed() && PatonaTimeLeft()< ModuleConfig.RefreshThreshold)
        {
            TaskHelper.Enqueue(TakeItem);
        }
    }
    


    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.ParsedOrange, GetLoc("gysahl_greensSetting"));//"自动使用陆行鸟粮设置"

        ImGui.Text(GetLoc("gysahl_greensTime"));//"搭档剩余时间少于多少秒自动吃菜:"
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##AutoCheckgysahl_greensUsageRefreshThreshold", ref ModuleConfig.RefreshThreshold);

        ImGui.Text(GetLoc("gysahl_greensTime"));//"发送通知:"
        ImGui.SameLine();
        ImGui.Checkbox("##AutoCheckgysahl_greensUsageSendNotice", ref ModuleConfig.SendNotice);
        

        SaveConfig(ModuleConfig);
    }


private static bool IsInMainCity()
{
    HashSet<uint> MainCityMapID = [
        129,                                    //海都1
        128,                                    //海都2
        132,                                    //森都1
        133,                                    //森都2
        130,                                    //沙都1
        131,                                    //沙都1
        418,                                    //伊修加德1
        419,                                    //伊修加德2
        478,                                    //田园郡
        635,                                    //神拳痕
        628,                                    //黄金港
        963,                                    //拉扎罕
        819,                                    //水晶都
        820,                                    //游末邦
        156,                                    //摩杜纳
        962,                                    //旧萨雷安
        1185,                                   //图莱尤拉
        1186,                                   //九号方案
        134,                                    //盛夏农庄
        136, //海雾村
        282, //海雾村私人小屋
        283, //海雾村私人公馆
        284, //海雾村私人别墅
        339, //海雾村
        384, //海雾村个人房间
        423, //海雾村部队工房
        340, //黄衣草苗国
        342, //黄衣草苗国私人小屋
        343, //薰衣草苗回私人公馆
        344, //黄衣草苗国私人别墅
        385, //薰衣草苗国个人房间
        425, //薰衣草苗国部队工房
        341, //高脚孤丘
        345, //高脚孤丘私人小屋
        346, //高脚孤丘私人公馆
        347, //高脚孤丘私人别墅
        386, //高脚孤丘个人房间
        424, //高脚孤丘部队工房
        641, //白银乡
        649, //白银乡私人小屋
        650, //白银乡私人公馆
        651, //白银乡私人别墅
        652, //白银乡个人房间
        653, //白银乡部队工房
        979, //穹顶皓天
        980, //穹顶皓天私人小屋
        981, //穹顶皓天私人公馆
        982, //穹顶皓天私人别墅
        983, //穹顶皓天个人房间
        984, //穹顶皓天部队工房
        573, // 海雾村公寓大厅
        608, // 海雾村公寓
        574, // 薰衣草苗圃公寓大厅
        609, // 薰衣草苗圃公寓
        575, // 高脚孤丘公寓大厅
        610, // 高脚孤丘公寓
        654, // 白银乡公寓大厅
        655, // 白银乡公寓
        985, // 穹顶皓天公寓大厅
        999, // 穹顶皓天公寓
        915,  //干戈斯
    ];

    uint currentMapId = DService.ClientState.MapId;

    return MainCityMapID.Contains(currentMapId);
}
    
    
    
    private static unsafe CompanionInfo GetPatona() => UIState.Instance()->Buddy.CompanionInfo;

    public static float PatonaTimeLeft() => GetPatona().TimeLeft;

    private static unsafe bool IsValidState() =>
        !BetweenAreas &&
        !OccupiedInEvent &&
        !IsCasting &&
        DService.ClientState.LocalPlayer != null &&
        IsScreenReady() &&
        !IsInMainCity() &&
        !DService.DutyState.IsDutyStarted;
    
    private static bool IsCooldownElapsed() => (DateTime.Now - Lastgysahl_greensUsageTime).TotalSeconds >= gysahl_greensUsageCooldownSeconds;
    
    private class Config : ModuleConfiguration
    {
        public int RefreshThreshold = 600; // 秒
        public bool SendNotice = true;
    }
    
    public override void Uninit()
    {
        // 取消订阅 Framework.Update 事件
        DService.Framework.Update -= OnFrameworkUpdate;

        base.Uninit();
    }
}
