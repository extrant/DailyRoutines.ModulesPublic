using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.Dtr;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DailyRoutines.Modules;

public class AutoRecordSubTimeLeft : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("AutoRecordSubTimeLeftTitle"),
        Description = GetLoc("AutoRecordSubTimeLeftDescription"),
        Category    = ModuleCategories.General,
        Author      = ["Due"]
    };

    public override ModulePermission Permission => new() { CNOnly = true };

    private static Config        ModuleConfig = null!;
    private static IDtrBarEntry? Entry;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Entry         ??= DService.DtrBar.Get("DailyRoutines-GameTimeLeft");
        Entry.OnClick =   () => ChatHelper.Instance.SendMessage($"/pdr search {GetType().Name}");
        Entry.Shown   =   true;

        RefreshEntry();

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CharaSelect", OnLobby);
    }

    public override void ConfigUI()
    {
        var contentID = DService.ClientState.LocalContentId;
        if (contentID == 0) return;
        
        if (!ModuleConfig.Infos.TryGetValue(contentID, out var info) || info.Record == DateTime.MinValue ||
            (info.LeftMonth == TimeSpan.MinValue && info.LeftTime == TimeSpan.MinValue))
        {
            ImGui.TextColored(Orange, GetLoc("AutoRecordSubTimeLeft-NoData"));
            return;
        }

        ImGui.TextColored(LightSkyBlue, $"{GetLoc("AutoRecordSubTimeLeft-LastRecordTime")}:");

        ImGui.SameLine();
        ImGui.Text($"{info.Record}");

        ImGui.TextColored(LightSkyBlue, $"{GetLoc("AutoRecordSubTimeLeft-MonthSub")} {GetLoc("AutoRecordSubTimeLeft-TimeTill")}:");

        ImGui.SameLine();
        ImGui.Text(FormatTimeSpan(info.LeftMonth, CultureInfo.CurrentCulture));
        
        ImGui.TextColored(LightSkyBlue, $"{GetLoc("AutoRecordSubTimeLeft-TimeSub")} {GetLoc("AutoRecordSubTimeLeft-TimeTill")}:");

        ImGui.SameLine();
        ImGui.Text(FormatTimeSpan(info.LeftTime, CultureInfo.CurrentCulture));
    }

    public override void Uninit()
    {
        Entry?.Remove();
        Entry = null;

        DService.AddonLifecycle.UnregisterListener(OnLobby);
        base.Uninit();
    }

    private unsafe void OnLobby(AddonEvent eventType, AddonArgs? args)
    {
        if (DService.ClientState.IsLoggedIn) return;

        var agent = AgentLobby.Instance();
        if (agent == null) return;

        try
        {
            var info = agent->LobbyData.LobbyUIClient.SubscriptionInfo;
            if (info == null) return;

            var contentID = agent->LobbyData.ContentId;
            if (contentID == 0) return;
            
            var timeInfo = GetLeftTimeSecond(*info);
            ModuleConfig.Infos[contentID]
                = new(DateTime.Now,
                      timeInfo.MonthTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.MonthTime),
                      timeInfo.PointTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.PointTime));
            ModuleConfig.Save(this);

            RefreshEntry(contentID);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private static (int MonthTime, int PointTime) GetLeftTimeSecond(LobbySubscriptionInfo info)
    {
        var size = Marshal.SizeOf(info);
        var arr = new byte[size];
        var ptr = IntPtr.Zero;

        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(info, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        var month = string.Join(string.Empty, arr.Skip(16).Take(3).Reverse().Select(x => x.ToString("X2")));
        var point = string.Join(string.Empty, arr.Skip(24).Take(3).Reverse().Select(x => x.ToString("X2")));
        return (Convert.ToInt32(month, 16), Convert.ToInt32(point, 16));
    }

    private static void RefreshEntry(ulong contentID = 0)
    {
        if (contentID == 0) contentID = DService.ClientState.LocalContentId;
        if (contentID == 0) return;
        
        if (!ModuleConfig.Infos.TryGetValue(contentID, out var info) || info.Record == DateTime.MinValue ||
            (info.LeftMonth == TimeSpan.MinValue && info.LeftTime == TimeSpan.MinValue))
            return;
        
        var isMonth = info.LeftMonth != TimeSpan.MinValue;
        var expireTime = DateTime.Now + (isMonth ? info.LeftMonth : info.LeftTime);
        Entry.Text =
            $"{GetLoc($"AutoRecordSubTimeLeft-{(isMonth ? "Month" : "Time")}Sub")}: {expireTime:MM/dd HH:mm}";
        Entry.Tooltip = $"{GetLoc("AutoRecordSubTimeLeft-ExpireTime")}:\n{expireTime}\n" +
                        $"{GetLoc("AutoRecordSubTimeLeft-TimeTill")}:\n{FormatTimeSpan(isMonth ? info.LeftMonth : info.LeftTime, CultureInfo.CurrentCulture)}";
    }
    
    public static string FormatTimeSpan(TimeSpan timeSpan, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName switch
        {
            "zh" => $"{timeSpan.Days} 天 {timeSpan.Hours} 小时 {timeSpan.Minutes} 分 {timeSpan.Seconds} 秒",
            _    => $"{timeSpan.Days} d {timeSpan.Hours} h {timeSpan.Minutes} m {timeSpan.Seconds} s"
        };

    private class Config : ModuleConfiguration
    {
        public Dictionary<ulong, (DateTime Record, TimeSpan LeftMonth, TimeSpan LeftTime)> Infos = [];
    }
}
