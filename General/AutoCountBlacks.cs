using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DailyRoutines.Abstracts;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using OmenTools;
using OmenTools.Helpers;
using OmenTools.Infos;

namespace DailyRoutines.Modules;

public class AutoCountBlacks : DailyModuleBase
{
    private int BlackNum;
    private StringBuilder Tooltip = new();
    private static Config ModuleConfig = null!;
    private static IDtrBarEntry DtrEntry;

    private Dictionary<uint, World> Worlds;
    private Hook<InfoProxyInterface.Delegates.EndRequest>? InfoProxyBlackListEndRequestHook;

    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoCountBlacksTitle"),
        Description = GetLoc("AutoCountBlacksTitleDesc"),
        Category = ModuleCategories.General,
        Author = ["ToxicStar"],
    };

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        InitWorlds();
        InitHookBlackList();

        DtrEntry = DService.DtrBar.Get("DailyRoutines-AutoCountBlacksTitle");
        DtrEntry.Shown = true;

        FrameworkManager.Register(false, OnUpdate);
    }

    private void InitWorlds()
    {
        var worldSheet = DService.Data.GetExcelSheet<World>();
        var luminaWorlds = worldSheet.Where(world =>
                    !string.IsNullOrEmpty(world.Name) &&
                     world.RowId is not 0);

        Worlds = luminaWorlds.ToDictionary(luminaWorld => luminaWorld.RowId, luminaWorld => luminaWorld);
    }

    private unsafe void InitHookBlackList()
    {
        var infoProxyBlackListEndRequestAddress = (nint)InfoModule.Instance()->GetInfoProxyById(InfoProxyId.Blacklist)->VirtualTable->EndRequest;
        InfoProxyBlackListEndRequestHook ??= DService.Hook.HookFromAddress<InfoProxyInterface.Delegates.EndRequest>(infoProxyBlackListEndRequestAddress, InfoProxyBlackListEndRequestDetour);
        InfoProxyBlackListEndRequestHook.Enable();
    }

    private unsafe void InfoProxyBlackListEndRequestDetour(InfoProxyInterface* thisPtr)
    {
        //每次加载黑名单时，统计一次
        var infoProxyBlacklist = InfoProxyBlacklist.Instance();
        ModuleConfig.BlackList.Clear();
        foreach (var blockCharacter in infoProxyBlacklist->BlockedCharacters)
        {
            //blockCharacter.Id = accountId for new, contentId for old
            ModuleConfig.BlackList.Add(blockCharacter.Id);
        }
        SaveConfig(ModuleConfig);
    }

    private unsafe void OnUpdate(IFramework _)
    {
        if (DService.ClientState.LocalPlayer is null) return;
        if (DtrEntry is null) return;

        Tooltip.Clear();
        BlackNum = 0;
        var myPos = DService.ClientState.LocalPlayer.Position;
        var length = DService.ObjectTable.Length >= 200 ? 200 : DService.ObjectTable.Length;
        for (int i = 0; i < length; i++)
        {
            var obj = DService.ObjectTable[i];
            if (obj is not null && obj.ObjectKind is ObjectKind.Player)
            {
                var needCheckPos = obj.Position;
                if(Vector3.Distance(myPos, needCheckPos) <= ModuleConfig.CheckRange)
                {
                    var chara = (BattleChara*)obj.Address;
                    if (!Worlds.TryGetValue(chara->HomeWorld, out var world))
                    {
                        continue;
                    }

                    //Character.Id = accountId for new, contentId for old
                    if (ModuleConfig.BlackList.Contains(chara->Character.ContentId) || ModuleConfig.BlackList.Contains(chara->Character.AccountId))
                    {
                        Tooltip.AppendLine($"{obj.Name}@{world.Name.ToString()}");
                        BlackNum++;
                    }
                }
            }
        }

        DtrEntry.Text = string.Format(GetLoc("AutoCountBlacksTitle-Text"), BlackNum.ToString());
        DtrEntry.Tooltip = Tooltip.ToString().Trim();
    }

    public override void ConfigUI()
    {
        if (ImGui.InputInt(GetLoc("AutoCountBlacksTitle-Range"), ref ModuleConfig.CheckRange))
        {
            ModuleConfig.CheckRange = Math.Max(1, ModuleConfig.CheckRange);
        }
    }

    public override void Uninit()
    {
        DtrEntry?.Remove();
        DtrEntry = null;

        FrameworkManager.Unregister(OnUpdate);

        InfoProxyBlackListEndRequestHook?.Dispose();
        InfoProxyBlackListEndRequestHook = null;

        base.Uninit();
    }

    public class Config : ModuleConfiguration
    {
        public int CheckRange = 2;
        public HashSet<ulong> BlackList = new();
    }
}
