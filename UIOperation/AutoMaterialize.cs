using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoMaterialize : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoMaterializeTitle"),
        Description = GetLoc("AutoMaterializeDescription"),
        Category    = ModuleCategories.UIOperation,
    };

    // 0 - 成功; 3 - 获取 InventoryType 或 InventorySlot 失败; 4 - 物品为空或不符合条件; 34 - 当前状态无法使用; 
    private static readonly CompSig ExtractMateriaSig = new("E8 ?? ?? ?? ?? 83 7E 20 00 75 5A");
    private delegate int ExtractMateriaDelegate(nint a1, InventoryType type, uint slot);
    private static Hook<ExtractMateriaDelegate>? ExtractMateriaHook;

    private static readonly CompSig MaterializeController = new("48 8D 0D ?? ?? ?? ?? 8B D0 E8 ?? ?? ?? ?? 83 7E");

    private static readonly InventoryType[] ArmoryInventories =
    [
        InventoryType.EquippedItems, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryWaist, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
        InventoryType.ArmoryMainHand, InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3,
        InventoryType.Inventory4
    ];

    private const string Command = "materialize";

    private static bool AutoExtractAll;

    public override void Init()
    {
        ExtractMateriaHook ??= ExtractMateriaSig.GetHook<ExtractMateriaDelegate>(ExtractMateriaDetour);
        ExtractMateriaHook.Enable();

        AddConfig(nameof(AutoExtractAll), true);
        AutoExtractAll = GetConfig<bool>(nameof(AutoExtractAll));

        TaskHelper ??= new TaskHelper { TimeLimitMS = 5_000 };
        Overlay ??= new Overlay(this);
        Overlay.Flags |= ImGuiWindowFlags.NoMove;

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "Materialize",       OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Materialize",       OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "MaterializeDialog", OnDialogAddon);

        if (Materialize != null) 
            OnAddon(AddonEvent.PostSetup, null);
        if (MaterializeDialog != null) 
            OnDialogAddon(AddonEvent.PostSetup, null);

        CommandManager.AddSubCommand(Command, new((_, _) => StartARoundAll()) { HelpMessage = GetLoc("AutoMaterialize-AutoExtractAll") });
    }

    public override void ConfigUI() => ConflictKeyText();

    public override void OverlayUI()
    {
        var addon = Materialize;
        if (addon == null) return;

        var pos = new Vector2(addon->GetX() + 6, addon->GetY() - ImGui.GetWindowSize().Y + 6);
        ImGui.SetWindowPos(pos);

        using (FontManager.UIFont80.Push())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.DalamudYellow, GetLoc("AutoMaterializeTitle"));

            ImGui.SameLine();
            using (ImRaii.Disabled(TaskHelper.IsBusy))
            {
                if (ImGui.Button(GetLoc("AutoMaterialize-ExtractAll")))
                    StartARoundAll();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            if (ImGui.Button(GetLoc("Stop")))
                TaskHelper.Abort();

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            if (ImGui.Checkbox(GetLoc("AutoMaterialize-AutoExtractAll"), ref AutoExtractAll))
                UpdateConfig(nameof(AutoExtractAll), AutoExtractAll);
            ImGuiOm.HelpMarker(GetLoc("AutoMaterialize-AutoExtractAllHelp"));
        }
    }

    private void StartARoundAll() 
        => TaskHelper.Enqueue(() => StartARound(ArmoryInventories), "开始精炼全部装备");

    private bool? StartARound(IReadOnlyList<InventoryType> types)
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        if (!Throttler.Throttle("AutoMaterialize")) return false;

        if (!IsEnvironmentValid()) return false;

        var manager = InventoryManager.Instance();
        foreach (var type in types)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) continue;

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;
                if (slot->SpiritbondOrCollectability != 10_000) continue;

                if (!LuminaGetter.TryGetRow<Item>(slot->ItemId, out var itemData)) continue;

                var itemName = itemData.Name.ExtractText();
                TaskHelper.Enqueue(() => ExtractMateria(type, (uint)i) == 0,
                                   $"开始精炼单件装备 {itemName}({slot->ItemId})");
                TaskHelper.Enqueue(() => Chat(GetLoc("AutoMaterialize-Notice-ExtractNow", itemData.Name.ExtractText())), 
                                   $"通知精制进度 {itemName}({slot->ItemId})");
                TaskHelper.DelayNext(1_000, 
                                     $"等待精制完成 {itemName}({slot->ItemId})");
                TaskHelper.Enqueue(() => StartARound(types), $"开始下一轮精制 本轮: {itemName}({slot->ItemId})");
                return true;
            }
        }

        NotificationInfo(GetLoc("AutoMaterialize-Notice-ExtractFinish"));
        TaskHelper.Abort();
        return true;
    }

    private bool IsEnvironmentValid()
    {
        if (IsInventoryFull([InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4]))
        {
            TaskHelper.Abort();
            return false;
        }

        if (DService.Condition[ConditionFlag.Mounted])
        {
            TaskHelper.Abort();
            NotificationError(GetLoc("AutoMaterialize-Notice-OnMount"));
            return false;
        }

        if (OccupiedInEvent) return false;
        if (DService.Condition[ConditionFlag.InCombat]) return false;

        return true;
    }

    private static int ExtractMateria(InventoryType type, uint slot) =>
        ExtractMateriaHook.Original(MaterializeController.GetStatic(), type, slot);

    private int ExtractMateriaDetour(nint a1, InventoryType type, uint slot)
    {
        var original = ExtractMateriaHook.Original(a1, type, slot);
        if (AutoExtractAll && !TaskHelper.IsBusy) 
            StartARoundAll();
        return original;
    }

    private void OnAddon(AddonEvent type, AddonArgs args) =>
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup   => true,
            AddonEvent.PreFinalize => false,
            _                      => Overlay.IsOpen,
        };

    private static void OnDialogAddon(AddonEvent type, AddonArgs args)
    {
        var addon = MaterializeDialog;
        if (addon == null) return;

        Callback(addon, true, 0);
    }

    public override void Uninit()
    {
        CommandManager.RemoveSubCommand(Command);
        
        DService.AddonLifecycle.UnregisterListener(OnAddon);
        DService.AddonLifecycle.UnregisterListener(OnDialogAddon);

        base.Uninit();
    }
}
