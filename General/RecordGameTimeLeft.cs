using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using Dalamud.Plugin.Services;
using DailyRoutines.Infos;
using OmenTools;
using DailyRoutines.Managers;
using Dalamud.Game.Gui.Dtr;
using ImGuiNET;
using DailyRoutines.Helpers;

namespace DailyRoutines.Modules;

public class RecordGameTimeLeft : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        /*
        Title = GetLoc("RecordGameTimeLeft"),
        Description = GetLoc("RecordGameTimeLeftDesc"),
        */ // Placeholder until localization is added
        Title = "记录剩余点卡时间",
        Description = "登陆时自动记录剩余点卡，可选在服务器信息栏显示到期时间。月卡请勿启用。",
        Category = ModuleCategories.General,
        Author = ["Due"]
    };

    private const string Command = "/timeleft";
    private static Config ModuleConfig = null!;
    private static IDtrBarEntry? Entry = null;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        Entry = DService.DtrBar.Get("DailyRoutines-GameTimeLeft");
        Entry.Shown = false;

        CommandManager.AddCommand(Command, new(OnCommand)
        {
            HelpMessage = "开启/关闭 服务器栏显示",
        });

        FrameworkManager.Register(false, OnUpdate);
    }

    public override void ConfigUI()
    {

        ImGui.Text("仅供参考，实际时间会有分钟级误差。");

        if (ModuleConfig.timeTill != null)
        {
            ImGui.Text($"上次记录时间： {ModuleConfig.lastSuccessRecord}。 时间至： {ModuleConfig.timeTill}");
        }
        else
        {
            ImGui.Text("暂无数据。请重新登录游戏。");
        }

        ImGui.Spacing();

        var showOnDTR = ModuleConfig.showOnDTR;
        if (ImGui.Checkbox("在信息栏显示", ref showOnDTR))
        {
            ModuleConfig.showOnDTR = showOnDTR;
            ModuleConfig.Save(this);
        }

        ImGui.Spacing();

        if (ImGui.Button("重置记录##TL_Reset"))
        {
            ResetConfig();
        }
    }

    public override void Uninit()
    {
        Entry?.Remove();

        FrameworkManager.Unregister(OnUpdate);

        base.Uninit();
    }

    private void OnCommand(string command, string args)
    {
        if (command is Command)
        {
            ModuleConfig.showOnDTR = !ModuleConfig.showOnDTR;
            ModuleConfig.Save(this);
            if (ModuleConfig.showOnDTR)
            {
                NotifyHelper.Chat("已开启信息栏显示");
            }
            else
            {
                NotifyHelper.Chat("已关闭信息栏显示");
            }
        }
    }

    private unsafe void OnUpdate(IFramework framework)
    {
        if (DService.ClientState.IsLoggedIn == false)
        {
            if (AgentLobby.Instance() != null)
            {
                if (ModuleConfig.lastSuccessRecord != null)
                {
                    if (DateTime.Now.Subtract(DateTime.Parse(ModuleConfig.lastSuccessRecord)).TotalMinutes < 1)
                    {
                        return;
                    }
                }
                try
                {
                    ModuleConfig.timeLeft = getTime(*(AgentLobby.Instance()->LobbyData.LobbyUIClient.SubscriptionInfo));
                    ModuleConfig.timeTill = DateTime.Now.AddSeconds(int.Parse(ModuleConfig.timeLeft)).ToString();
                    ModuleConfig.lastSuccessRecord = DateTime.Now.ToString();
                    ModuleConfig.Save(this);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        if (ModuleConfig.showOnDTR)
        {
            if (ModuleConfig.timeTill != null)
            {
                Entry.Text = $"点卡到: {DateTime.Parse(ModuleConfig.timeTill):MM-dd HH:mm}";
                Entry.Shown = true;
            }
            else
            {
                Entry.Shown = false;
            }
        }
        else
        {
            Entry.Shown = false;
        }

    }

    private static string getTime(LobbySubscriptionInfo str)
    {
        var size = Marshal.SizeOf(str);
        var arr = new byte[size];
        var ptr = IntPtr.Zero;

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

        var ret = "";
        // 理论最多到 FFFFFF, 没有人冲到这个数吧
        // 什么神奇程序员反过来存的
        ret = string.Join("", arr.Skip(24).Take(3).Reverse().Select(x => x.ToString("X2")));
        ret = Convert.ToInt32(ret, 16).ToString();
        return ret;
    }

    private class Config : ModuleConfiguration
    {
        public string? lastSuccessRecord { get; set; } = null;
        public bool showOnDTR { get; set; } = false;
        public string? timeLeft { get; set; } = null;
        public string? timeTill { get; set; } = null;
    }

    private void ResetConfig()
    {
        ModuleConfig.lastSuccessRecord = null;
        ModuleConfig.showOnDTR = false;
        ModuleConfig.timeLeft = null;
        ModuleConfig.timeTill = null;
        ModuleConfig.Save(this);
    }
}
