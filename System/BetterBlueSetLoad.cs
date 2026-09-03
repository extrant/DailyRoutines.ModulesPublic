using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class BetterBlueSetLoad : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("BetterBlueSetLoadTitle"),
        Description = Lang.Get("BetterBlueSetLoadDescription", COMMAND),
        Category    = ModuleCategory.System
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };
    
    private static readonly CompSig CanAssignBlueMageActionSig =
        new("48 89 5C 24 ?? 57 48 83 EC ?? 8B DA 48 8B F9 85 D2 0F 84 ?? ?? ?? ?? 8B CA E8 ?? ?? ?? ?? 44 8B C3");
    private delegate bool CanAssignBlueMageActionDelegate
    (
        ActionManager* actionManager,
        uint           actionID
    );
    private Hook<CanAssignBlueMageActionDelegate>? CanAssignBlueMageActionHook;

    protected override void Init()
    {
        CanAssignBlueMageActionHook =
            IGameInteropProvider.Instance().HookFromSignature<CanAssignBlueMageActionDelegate>
            (
                CanAssignBlueMageActionSig.Get(),
                CanAssignBlueMageActionDetour
            );
        CanAssignBlueMageActionHook.Enable();

        CommandManager.Instance().AddSubCommand(COMMAND, new(OnCommand) { HelpMessage = Lang.Get("BetterBlueSetLoad-CommandHelp") });
    }

    protected override void Uninit() =>
        CommandManager.Instance().RemoveSubCommand(COMMAND);

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("Command")}");

        using (ImRaii.PushIndent())
            ImGui.TextUnformatted($"/pdr {COMMAND} → {Lang.Get("BetterBlueSetLoad-CommandHelp")}");
    }

    private static bool CanAssignBlueMageActionDetour
    (
        ActionManager* actionManager,
        uint           actionID
    ) => true;

    private static void OnCommand
    (
        string command,
        string args
    )
    {
        args = args.Trim();
        if (string.IsNullOrEmpty(args)) return;

        if (uint.TryParse(args, out var setIndex) && setIndex < 5)
            ApplyByIndex(setIndex);
        else
        {
            var names = AozNoteModule.Instance()->ActiveSets
                        .ToArray()
                        .Where(x => !string.IsNullOrWhiteSpace(x.CustomNameString))
                        .Select((value, index) => (Index: (uint)index, Name: value.CustomNameString))
                        .DistinctBy(x => x.Name)
                        .ToDictionary(x => x.Name, x => x.Index);
            if (!names.TryGetValue(args, out setIndex)) return;
            
            ApplyByIndex(setIndex);
        }
    }

    private static void ApplyByIndex
    (
        uint index
    )
    {
        if (index > 4) return;

        var set = AozNoteModule.Instance()->ActiveSets[(int)index];
        var setName = string.IsNullOrWhiteSpace(set.CustomNameString) ?
                          LuminaWrapper.GetAddonText(12271 + index) :
                          set.CustomNameString;

        var actionArray = stackalloc uint[24];
        for (var i = 0; i < 24; i++)
            actionArray[i] = set.ActiveActions[i];
        ActionManager.Instance()->SetBlueMageActions(actionArray);

        using var utf8String = new Utf8String(setName);
        RaptureLogModule.Instance()->ShowLogMessageString(9472, &utf8String);
    }

    #region 常量

    private const string COMMAND = "blueset";

    #endregion
}
