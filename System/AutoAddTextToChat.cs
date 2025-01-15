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
using DailyRoutines.Abstracts;
using Dalamud.Hooking;
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

    private static Config ModuleConfig = null!;

    public override void Init()
    {
        ModuleConfig = new Config().Load(this);
        ProcessSendedChatHook ??= DService.Hook.HookFromSignature<ProcessSendedChatDelegate>(ProcessSendedChatSig.Get(), ProcessSendedChatDetour);
        ProcessSendedChatHook.Enable();
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("Prefix"), ref ModuleConfig.IsAddPrefix)) ModuleConfig.Save(this);
        if (ImGui.Checkbox(GetLoc("Suffix"), ref ModuleConfig.IsAddSuffix)) ModuleConfig.Save(this);

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();

        ImGui.Text(GetLoc("Prefix"));
        ImGui.SameLine();
        ImGui.InputText("###Prefix", ref ModuleConfig.PrefixString, 48);
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("Suffix"));
        ImGui.SameLine();
        ImGui.InputText("###Suffix", ref ModuleConfig.SuffixString, 48);
        ImGui.Spacing();
        
        ImGui.AlignTextToFramePadding();
        if (ImGui.Button(GetLoc("Save"))) ModuleConfig.Save(this);
    }

    private static void ProcessSendedChatDetour(ShellCommandModule* module, Utf8String* message, UIModule* uiModule)
    {
        var messageText = message->ExtractText();
        var isCommand = messageText.StartsWith('/') || messageText.StartsWith('／');
        var isTellCommand = messageText.StartsWith("/tell ");

        if ((!string.IsNullOrWhiteSpace(messageText) && !isCommand) || isTellCommand)
        {
            if (HandleAddPrefixAndSuffix(messageText, out var modifiedMessage))
            {
                var finalMessage = Utf8String.FromString(modifiedMessage);
                ProcessSendedChatHook.Original(module, finalMessage, uiModule);
                finalMessage->Dtor(true);
            }
            return;
        }
        ProcessSendedChatHook.Original(module, message, uiModule);
    }
    
    private static bool HandleAddPrefixAndSuffix(string message, out string handledMessage)
    {
        handledMessage = message;
        if (ModuleConfig.IsAddPrefix) handledMessage = $"{ModuleConfig.PrefixString}{handledMessage}";
        if (ModuleConfig.IsAddSuffix) handledMessage = $"{handledMessage}{ModuleConfig.SuffixString}";
        return true;
    }
    
    public class Config : ModuleConfiguration
    {
        public bool IsAddPrefix = true;
        public bool IsAddSuffix = true;
        public string PrefixString = "";
        public string SuffixString = "";
    }
}
