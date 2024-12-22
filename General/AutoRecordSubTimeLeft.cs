using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Plugin.Services;
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

        if (ModuleConfig.TimeLeft != TimeSpan.MinValue && ModuleConfig.LastSuccessRecord != DateTime.MinValue)
            Entry.Text = $"{GetLoc("AutoRecordSubTimeLeft-ExpireTime")}: {DateTime.Now + ModuleConfig.TimeLeft}";

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CharaSelect", OnLobby);
    }
    
    public override void ConfigUI()
    {
        if (ImGui.Button($"{GetLoc("Reset")}##TL_Reset")) 
            ResetConfig();
        
        ImGui.Spacing();
        
        if (ModuleConfig.TimeLeft == TimeSpan.MinValue || ModuleConfig.LastSuccessRecord == DateTime.MinValue)
        {
            ImGui.TextColored(Orange, GetLoc("AutoRecordSubTimeLeft-NoData"));
            return;
        }

        if (ModuleConfig.HasMonthlySub)
        {
            ImGui.TextColored(Orange, GetLoc("AutoRecordSubTimeLeft-MonthSubscribe"));
            return;
        }
        
        ImGui.TextColored(LightSkyBlue, $"{GetLoc("AutoRecordSubTimeLeft-LastRecordTime")}:");
        
        ImGui.SameLine();
        ImGui.Text($"{ModuleConfig.LastSuccessRecord}");
        
        ImGui.TextColored(LightSkyBlue, $"{GetLoc("AutoRecordSubTimeLeft-TimeTill")}:");
        
        ImGui.SameLine();
        ImGui.Text($"{ModuleConfig.TimeLeft:hh\\:mm\\:ss}");
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

            if (info->DaysRemaining != 0)
            {
                ModuleConfig.HasMonthlySub = true;
                ModuleConfig.Save(this);
                return;
            }

            ModuleConfig.TimeLeft          = TimeSpan.FromSeconds(GetLeftTimeSecond(*info));
            ModuleConfig.LastSuccessRecord = DateTime.Now;
            ModuleConfig.Save(this);

            Entry.Text = $"{GetLoc("AutoRecordSubTimeLeft-ExpireTime")}: {DateTime.Now + ModuleConfig.TimeLeft}";
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private static int GetLeftTimeSecond(LobbySubscriptionInfo str)
    {
        var size = Marshal.SizeOf(str);
        var arr  = new byte[size];
        var ptr  = IntPtr.Zero;

        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(str, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
        } 
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        var ret = string.Join(string.Empty, arr.Skip(24).Take(3).Reverse().Select(x => x.ToString("X2")));
        return Convert.ToInt32(ret, 16);
    }
    
    private void ResetConfig()
    {
        ModuleConfig.LastSuccessRecord = DateTime.MinValue;
        ModuleConfig.TimeLeft          = TimeSpan.MinValue;
        ModuleConfig.HasMonthlySub     = false;
        
        ModuleConfig.Save(this);
    }

    private class Config : ModuleConfiguration
    {
        public DateTime LastSuccessRecord = DateTime.MinValue;
        public TimeSpan TimeLeft          = TimeSpan.MinValue;
        public bool     HasMonthlySub;
    }
}
