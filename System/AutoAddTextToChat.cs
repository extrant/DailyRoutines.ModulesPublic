global using static DailyRoutines.Infos.Widgets;
global using static OmenTools.Helpers.HelpersOm;
global using static DailyRoutines.Infos.Extensions;
global using static OmenTools.Infos.InfosOm;
global using static OmenTools.Helpers.ThrottlerHelper;
global using static DailyRoutines.Managers.Configuration;
global using static DailyRoutines.Managers.LanguageManagerExtensions;
global using static DailyRoutines.Helpers.NotifyHelper;
global using OmenTools.Infos;
global using OmenTools.ImGuiOm;
global using OmenTools.Helpers;
global using OmenTools;
global using ImGuiNET;
global using ImPlotNET;
global using Lang = DailyRoutines.Managers.LanguageManager;
global using Dalamud.Game;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.Shell;

namespace DailyRoutines.Modules;

public unsafe class AutoAddTextToChat : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoAddTextToChatTitle"),
        Description = GetLoc("AutoAddTextToChatDescription"),
        Author = ["那年雪落"],
        Category = ModuleCategories.System,
    };

    private static readonly CompSig ProcessSendedChatSig = new("E8 ?? ?? ?? ?? FE 86 ?? ?? ?? ?? C7 86 ?? ?? ?? ?? ?? ?? ?? ??");

    private delegate void ProcessSendedChatDelegate(ShellCommandModule* module, Utf8String* message, UIModule* uiModule);

    private static Hook<ProcessSendedChatDelegate>? ProcessSendedChatHook;

    private static Config? ModuleConfig;

    public override void Init()
    {
        var config = LoadConfig<Config>();
        if (config == null)
        {
            config = new Config();
            if (LanguageManager.CurrentLanguage == ClientLanguage.ChineseSimplified.ToString())
            {
                config.BlackList.Add(".", "。", "？", "?", "！", "!", "吗", "吧", "呢", "啊", "呗", "呀", "阿", "哦", "嘛", "咯", "哎", "啦", "哇", "呵", "哈", "奥", "嗷");
            }
            SaveConfig(config);
        }
        else
        {
            ModuleConfig = config;
        }

        ProcessSendedChatHook ??= ProcessSendedChatSig.GetHook<ProcessSendedChatDelegate>(ProcessSendedChatDetour);
        ProcessSendedChatHook.Enable();
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("Prefix"), ref ModuleConfig.IsAddPrefix)) SaveConfig(ModuleConfig);
        if (ImGui.Checkbox(GetLoc("Suffix"), ref ModuleConfig.IsAddSuffix)) SaveConfig(ModuleConfig);

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();

        ImGui.Text(GetLoc("Prefix"));
        ImGui.SameLine();
        ImGui.InputText("###Prefix", ref ModuleConfig.PrefixString, 48);
        if (ImGui.IsItemDeactivatedAfterEdit()) SaveConfig(ModuleConfig);
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("Suffix"));
        ImGui.SameLine();
        ImGui.InputText("###Suffix", ref ModuleConfig.SuffixString, 48);
        if (ImGui.IsItemDeactivatedAfterEdit()) SaveConfig(ModuleConfig);
        ImGui.Spacing();
        
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(LightSkyBlue, GetLoc("Blacklist"));
        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Plus, GetLoc("Add")))
        {
            ModuleConfig.BlackList.Add(string.Empty);
            SaveConfig(ModuleConfig);
        }

        ImGui.Spacing();
        
        if (ModuleConfig.BlackList.Count == 0) return;

        var blackListItems = ModuleConfig.BlackList.ToList();
        var tableSize = (ImGui.GetContentRegionAvail() * 0.85f) with { Y = 0 };
        using var table = ImRaii.Table(GetLoc("Blacklist"), 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, tableSize);
        if (!table) return;
        
        ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch);
        
        for (var i = 0; i < blackListItems.Count; i++)
        {
            if (i % 5 == 0) ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(i % 5);

            var inputRef = blackListItems[i];
            using var id = ImRaii.PushId($"{inputRef}_{i}_Command");
            ImGui.InputText($"##Item{i}", ref inputRef, 48);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.BlackList.Remove(blackListItems[i]);
                ModuleConfig.BlackList.Add(inputRef);
                SaveConfig(ModuleConfig);
                blackListItems[i] = inputRef;
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("Delete", FontAwesomeIcon.TrashAlt, GetLoc("Delete")))
            {
                ModuleConfig.BlackList.Remove(blackListItems[i]);
                SaveConfig(ModuleConfig);
                blackListItems.RemoveAt(i);
                i--;
            }
        }
    }

    private static void ProcessSendedChatDetour(ShellCommandModule* module, Utf8String* message, UIModule* uiModule)
    {
        var messageText = message->ExtractText();
        var isCommand = messageText.StartsWith('/') || messageText.StartsWith('／');
        var isTellCommand = messageText.StartsWith("/tell ");

        if ((!string.IsNullOrWhiteSpace(messageText) && !isCommand) || isTellCommand)
        {
            if (IsWhiteListChat(messageText))
            {
                ProcessSendedChatHook.Original(module, message, uiModule);
                return;
            }

            if (AddPrefixAndSuffixIfNeeded(messageText, out var modifiedMessage, isTellCommand))
            {
                var finalMessage = Utf8String.FromString(modifiedMessage);
                ProcessSendedChatHook.Original(module, finalMessage, uiModule);
                finalMessage->Dtor(true);
                return;
            }
        }
        ProcessSendedChatHook.Original(module, message, uiModule);
    }

    private static bool IsWhiteListChat(string message)
    {
        return ModuleConfig?.BlackList.Any(whiteListChat => !string.IsNullOrEmpty(whiteListChat) && message.EndsWith(whiteListChat)) ?? false;
    }
    
    private static bool AddPrefixAndSuffixIfNeeded(string original, out string handledMessage, bool isTellCommand = false)
    {
        handledMessage = original;
        if (ModuleConfig.IsAddPrefix)
        {
            if (isTellCommand)
            {
                var firstSpaceIndex = original.IndexOf(' ');
                if (firstSpaceIndex == -1) return false;
                var secondSpaceIndex = original.IndexOf(' ', firstSpaceIndex + 1);
                if (secondSpaceIndex == -1) return false;
                handledMessage = $"{original[..secondSpaceIndex]} {ModuleConfig.PrefixString}{original[secondSpaceIndex..].TrimStart()}";
            }
            else 
            { 
                handledMessage = $"{ModuleConfig.PrefixString}{handledMessage}";
            }
        }
        if (ModuleConfig.IsAddSuffix) handledMessage = $"{handledMessage}{ModuleConfig.SuffixString}";
        return true;
    }
    
    public class Config : ModuleConfiguration
    {
        public bool IsAddPrefix;
        public bool IsAddSuffix;
        public string PrefixString = "";
        public string SuffixString = "";
        public readonly HashSet<string> BlackList = [];
    }
}
