using System;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DailyRoutines.Modules;

public unsafe class ScrollableTabs : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("ScrollableTabsTitle"),
        Description = GetLoc("ScrollableTabsDescription"),
        Category    = ModuleCategories.UIOperation,
        Author      = ["Cyf5119"],
    };
    
    private const int NumArmouryBoardTabs           = 12;
    private const int NumInventoryTabs              = 5;
    private const int NumInventoryLargeTabs         = 4;
    private const int NumInventoryExpansionTabs     = 2;
    private const int NumInventoryRetainerTabs      = 6;
    private const int NumInventoryRetainerLargeTabs = 3;
    private const int NumBuddyTabs                  = 3;

    private static Config ModuleConfig = null!;
    private static int    WheelState;

    private static AtkCollisionNode* IntersectingCollisionNode =>
        RaptureAtkModule.Instance()->AtkCollisionManager.IntersectingCollisionNode;

    private static bool IsNext =>
        WheelState == (!ModuleConfig.Invert ? 1 : -1);

    private static bool IsPrev =>
        WheelState == (!ModuleConfig.Invert ? -1 : 1);

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();

        FrameworkManager.Register(true, OnUpdate);
    }

    public override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        base.Uninit();
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("ScrollableTabs-Invert"), ref ModuleConfig.Invert))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(1370), ref ModuleConfig.HandleArmouryBoard))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(12250), ref ModuleConfig.HandleAOZNotebook))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(230), ref ModuleConfig.HandleCharacter))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(230) + "->" + LuminaWarpper.GetAddonText(760), ref ModuleConfig.HandleCharacterClass))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(230) + "->" + LuminaWarpper.GetAddonText(102512), ref ModuleConfig.HandleCharacterRepute))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(882), ref ModuleConfig.HandleInventoryBuddy))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(3511), ref ModuleConfig.HandleBuddy))
            SaveConfig(ModuleConfig);
        
        // if(ImGui.Checkbox(LuminaWarpper.GetAddonText(3660), ref ModuleConfig.HandleCurrency))
        // SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(13671), ref ModuleConfig.HandleOrnamentNoteBook))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(13802), ref ModuleConfig.HandleFieldRecord))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(3804), ref ModuleConfig.HandleFishGuide))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(3735), ref ModuleConfig.HandleMiragePrismPrismBox))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(9335) + "->" + LuminaWarpper.GetAddonText(9339), ref ModuleConfig.HandleGoldSaucerCardList))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(9335) + "->" + LuminaWarpper.GetAddonText(9340) + "->" + LuminaWarpper.GetAddonText(9425),
                           ref ModuleConfig.HandleGoldSaucerCardDeckEdit))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(9335) + "->" + LuminaWarpper.GetAddonText(9550) + "->" + LuminaWarpper.GetAddonText(9594),
                           ref ModuleConfig.HandleLovmPaletteEdit))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(520), ref ModuleConfig.HandleInventory))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(15321), ref ModuleConfig.HandleMJIMinionNoteBook))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(7595), ref ModuleConfig.HandleMinionNoteBook))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(4964), ref ModuleConfig.HandleMountNoteBook))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(6941), ref ModuleConfig.HandleRetainer))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(3933), ref ModuleConfig.HandleFateProgress))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(LuminaWarpper.GetAddonText(8140), ref ModuleConfig.HandleAdventureNoteBook))
            SaveConfig(ModuleConfig);
    }

    private static void OnUpdate(IFramework _)
    {
        if (!DService.ClientState.IsLoggedIn)
            return;

        WheelState = Math.Clamp(UIInputData.Instance()->CursorInputs.MouseWheel, -1, 1);
        if (WheelState == 0)
            return;

        if (ModuleConfig.Invert)
            WheelState *= -1;

        var hoveredUnitBase = RaptureAtkModule.Instance()->AtkCollisionManager.IntersectingAddon;
        if (hoveredUnitBase == null)
        {
            WheelState = 0;
            return;
        }

        var name = hoveredUnitBase->NameString;
        if (string.IsNullOrEmpty(name))
        {
            WheelState = 0;
            return;
        }

        // parent lookup
        switch (name)
        {
            // use these directly
            case "AetherCurrent":          // Aether Currents
            case "ArmouryBoard":           // Armoury Chest
            case "AOZNotebook":            // Blue Magic Spellbook
            case "OrnamentNoteBook":       // Fashion Accessories
            case "MYCWarResultNotebook":   // Field Records
            case "FishGuide2":             // Fish Guide
            case "GSInfoCardList":         // Gold Saucer -> Card List
            case "GSInfoEditDeck":         // Gold Saucer -> Decks -> Edit Deck
            case "LovmPaletteEdit":        // Gold Saucer -> Lord of Verminion -> Minion Hotbar
            case "Inventory":              // Inventory
            case "InventoryLarge":         // Inventory
            case "InventoryExpansion":     // Inventory
            case "MinionNoteBook":         // Minions
            case "MountNoteBook":          // Mounts
            case "InventoryRetainer":      // Retainer Inventory
            case "InventoryRetainerLarge": // Retainer Inventory
            case "FateProgress":           // Shared FATE
            case "AdventureNoteBook":      // Sightseeing Log
            case "MJIMinionNoteBook":      // Island Minion Guide
            // case "Currency":               // Currency
            case "InventoryBuddy":      // Chocobo Saddlebag
            case "InventoryBuddy2":     // Chocobo Saddlebag (when in Retainer Inventory)
            case "Character":           // Character
            case "CharacterClass":      // Character -> Classes/Jobs
            case "CharacterRepute":     // Character -> Reputation
            case "Buddy":               // Companion
            case "MiragePrismPrismBox": // Glamours
                break;

            // used by Inventory
            case "InventoryGrid":
            case "InventoryGridCrystal":
                name = "Inventory";
                break;

            // Key Items (part of Inventory)
            case "InventoryEvent":
            case "InventoryEventGrid":
                name = "InventoryEvent";
                break;

            // used by InventoryLarge or InventoryExpansion
            case "InventoryCrystalGrid":
                name = "InventoryLarge";
                if (DService.GameConfig.UiConfig.TryGet("ItemInventryWindowSizeType", out uint itemInventryWindowSizeType) && itemInventryWindowSizeType == 2)
                    name = "InventoryExpansion";
                break;

            // used by InventoryLarge
            case "InventoryEventGrid0":
            case "InventoryEventGrid1":
            case "InventoryEventGrid2":
            case "InventoryGrid0":
            case "InventoryGrid1":
                name = "InventoryLarge";
                break;

            // used by InventoryExpansion
            case "InventoryEventGrid0E":
            case "InventoryEventGrid1E":
            case "InventoryEventGrid2E":
            case "InventoryGrid0E":
            case "InventoryGrid1E":
            case "InventoryGrid2E":
            case "InventoryGrid3E":
                name = "InventoryExpansion";
                break;

            // used by InventoryRetainer
            case "RetainerGridCrystal":
            case "RetainerGrid":
                name = "InventoryRetainer";
                break;

            // used by InventoryRetainerLarge
            case "RetainerCrystalGrid":
            case "RetainerGrid0":
            case "RetainerGrid1":
            case "RetainerGrid2":
            case "RetainerGrid3":
            case "RetainerGrid4":
                name = "InventoryRetainerLarge";
                break;

            // embedded addons of Character
            case "CharacterStatus":  // Character -> Attributes
            case "CharacterProfile": // Character -> Profile
                name = "Character";
                break;

            // embedded addons of Buddy
            case "BuddyAction":     // Companion -> Actions
            case "BuddySkill":      // Companion -> Skills
            case "BuddyAppearance": // Companion -> Appearance
                name = "Buddy";
                break;

            default:
                WheelState = 0;
                return;
        }

        // if (!TryGetAddon<AtkUnitBase>(name, out var unitBase)) // 使用 Omen牌 方法替代
        if (!TryGetAddonByName<AtkUnitBase>(name, out var unitBase))
        {
            WheelState = 0;
            return;
        }

        if (ModuleConfig.HandleArmouryBoard && name == "ArmouryBoard")
            UpdateArmouryBoard((AddonArmouryBoard*)unitBase);
        else if (ModuleConfig.HandleInventory && name is "Inventory" or "InventoryEvent" or "InventoryLarge" or "InventoryExpansion")
        {
            switch (name)
            {
                case "Inventory":
                    UpdateInventory((AddonInventory*)unitBase);
                    break;
                case "InventoryEvent":
                    UpdateInventoryEvent((AddonInventoryEvent*)unitBase);
                    break;
                case "InventoryLarge":
                    UpdateInventoryLarge((AddonInventoryLarge*)unitBase);
                    break;
                case "InventoryExpansion":
                    UpdateInventoryExpansion((AddonInventoryExpansion*)unitBase);
                    break;
            }
        }
        else if (ModuleConfig.HandleRetainer && name is "InventoryRetainer" or "InventoryRetainerLarge")
        {
            switch (name)
            {
                case "InventoryRetainer":
                    UpdateInventoryRetainer((AddonInventoryRetainer*)unitBase);
                    break;
                case "InventoryRetainerLarge":
                    UpdateInventoryRetainerLarge((AddonInventoryRetainerLarge*)unitBase);
                    break;
            }
        }
        else if ((ModuleConfig.HandleMinionNoteBook && name == "MinionNoteBook") || (ModuleConfig.HandleMountNoteBook && name == "MountNoteBook"))
            UpdateMountMinion((AddonMinionMountBase*)unitBase);
        else if (ModuleConfig.HandleFishGuide && name == "FishGuide2")
            UpdateTabController(unitBase, &((AddonFishGuide2*)unitBase)->TabController);
        else if (ModuleConfig.HandleAdventureNoteBook && name == "AdventureNoteBook")
            UpdateTabController(unitBase, &((AddonAdventureNoteBook*)unitBase)->TabController);
        else if (ModuleConfig.HandleOrnamentNoteBook && name == "OrnamentNoteBook")
            UpdateTabController(unitBase, &((AddonOrnamentNoteBook*)unitBase)->TabController);
        else if (ModuleConfig.HandleGoldSaucerCardList && name == "GSInfoCardList")
            UpdateTabController(unitBase, &((AddonGSInfoCardList*)unitBase)->TabController);
        else if (ModuleConfig.HandleGoldSaucerCardDeckEdit && name == "GSInfoEditDeck")
            UpdateTabController(unitBase, &((AddonGSInfoEditDeck*)unitBase)->TabController);
        else if (ModuleConfig.HandleLovmPaletteEdit && name == "LovmPaletteEdit")
            UpdateTabController(unitBase, &((AddonLovmPaletteEdit*)unitBase)->TabController);
        else if (ModuleConfig.HandleAOZNotebook && name == "AOZNotebook")
            UpdateAOZNotebook((AddonAOZNotebook*)unitBase);
        else if (ModuleConfig.HandleAetherCurrent && name == "AetherCurrent")
            UpdateAetherCurrent((AddonAetherCurrent*)unitBase);
        else if (ModuleConfig.HandleFateProgress && name == "FateProgress")
            UpdateFateProgress((AddonFateProgress*)unitBase);
        else if (ModuleConfig.HandleFieldRecord && name == "MYCWarResultNotebook")
            UpdateFieldNotes((AddonMYCWarResultNotebook*)unitBase);
        else if (ModuleConfig.HandleMJIMinionNoteBook && name == "MJIMinionNoteBook")
            UpdateMJIMinionNoteBook((AddonMJIMinionNoteBook*)unitBase);
        // else if (ModuleConfig.HandleCurrency && name == "Currency")
        // {
        // UpdateCurrency((AddonCurrency*)unitBase);
        // }
        else if (ModuleConfig.HandleInventoryBuddy && name is "InventoryBuddy" or "InventoryBuddy2")
            UpdateInventoryBuddy((AddonInventoryBuddy*)unitBase);
        else if (ModuleConfig.HandleBuddy && name == "Buddy")
            UpdateBuddy((AddonBuddy*)unitBase);
        else if (ModuleConfig.HandleMiragePrismPrismBox && name == "MiragePrismPrismBox")
            UpdateMiragePrismPrismBox((AddonMiragePrismPrismBox*)unitBase);
        else if (name is "Character" or "CharacterClass" or "CharacterRepute")
        {
            var addonCharacter = name == "Character" ? (AddonCharacter*)unitBase : GetAddonByName<AddonCharacter>("Character");

            if (addonCharacter            == null || !addonCharacter->AddonControl.IsChildSetupComplete ||
                IntersectingCollisionNode == addonCharacter->CharacterPreviewCollisionNode)
            {
                WheelState = 0;
                return;
            }

            switch (name)
            {
                case "Character" when ModuleConfig.HandleCharacter:
                    UpdateCharacter(addonCharacter);
                    break;
                case "CharacterClass" when ModuleConfig.HandleCharacter && !ModuleConfig.HandleCharacterClass:
                    UpdateCharacter(addonCharacter);
                    break;
                case "CharacterClass" when ModuleConfig.HandleCharacterClass:
                    UpdateCharacterClass(addonCharacter, (AddonCharacterClass*)unitBase);
                    break;
                case "CharacterRepute" when ModuleConfig.HandleCharacter && !ModuleConfig.HandleCharacterRepute:
                    UpdateCharacter(addonCharacter);
                    break;
                case "CharacterRepute" when ModuleConfig.HandleCharacterRepute:
                    UpdateCharacterRepute(addonCharacter, (AddonCharacterRepute*)unitBase);
                    break;
            }
        }

        WheelState = 0;
    }

    private static int GetTabIndex(int currentTabIndex, int numTabs) => Math.Clamp(currentTabIndex + WheelState, 0, numTabs - 1);

    private static void UpdateArmouryBoard(AddonArmouryBoard* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, NumArmouryBoardTabs);

        if (addon->TabIndex < tabIndex)
            addon->NextTab(0);
        else if (addon->TabIndex > tabIndex)
            addon->PreviousTab(0);
    }

    private static void UpdateInventory(AddonInventory* addon)
    {
        if (addon->TabIndex == NumInventoryTabs - 1 && WheelState > 0)
        {
            var values = stackalloc AtkValue[3];

            values[0].Ctor();
            values[0].Type = ValueType.Int;
            values[0].Int  = 22;

            values[1].Ctor();
            values[1].Type = ValueType.Int;
            values[1].Int  = *(int*)((nint)addon + 0x228);

            values[2].Ctor();
            values[2].Type = ValueType.UInt;
            values[2].UInt = 0;

            addon->AtkUnitBase.FireCallback(3, values);
        }
        else
        {
            var tabIndex = GetTabIndex(addon->TabIndex, NumInventoryTabs);

            if (addon->TabIndex == tabIndex)
                return;

            addon->SetTab(tabIndex);
        }
    }

    private static void UpdateInventoryEvent(AddonInventoryEvent* addon)
    {
        if (addon->TabIndex == 0 && WheelState < 0)
        {
            // inside Vf68, fn call before return with a2 being 2
            var values = stackalloc AtkValue[3];

            values[0].Ctor();
            values[0].Type = ValueType.Int;
            values[0].Int  = 22;

            values[1].Ctor();
            values[1].Type = ValueType.Int;
            values[1].Int  = *(int*)((nint)addon + 0x280);

            values[2].Ctor();
            values[2].Type = ValueType.UInt;
            values[2].UInt = 2;

            addon->AtkUnitBase.FireCallback(3, values);
        }
        else
        {
            var numEnabledButtons = 0;
            foreach (ref var button in addon->Buttons)
                if ((button.Value->AtkComponentButton.Flags & 0x40000) != 0)
                    numEnabledButtons++;

            var tabIndex = GetTabIndex(addon->TabIndex, numEnabledButtons);

            if (addon->TabIndex == tabIndex)
                return;

            addon->SetTab(tabIndex);
        }
    }

    private static void UpdateInventoryLarge(AddonInventoryLarge* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, NumInventoryLargeTabs);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex);
    }

    private static void UpdateInventoryExpansion(AddonInventoryExpansion* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, NumInventoryExpansionTabs);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex, false);
    }

    private static void UpdateInventoryRetainer(AddonInventoryRetainer* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, NumInventoryRetainerTabs);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex);
    }

    private static void UpdateInventoryRetainerLarge(AddonInventoryRetainerLarge* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, NumInventoryRetainerLargeTabs);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex);
    }

    private static void UpdateTabController(AtkUnitBase* addon, TabController* tabController)
    {
        var tabIndex = GetTabIndex(tabController->TabIndex, tabController->TabCount);

        if (tabController->TabIndex == tabIndex)
            return;

        tabController->TabIndex = tabIndex;
        tabController->CallbackFunction(tabIndex, addon);
    }

    private static void UpdateAOZNotebook(AddonAOZNotebook* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, addon->TabCount);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex, true);
    }

    private static void UpdateAetherCurrent(AddonAetherCurrent* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, addon->TabCount);
        if (addon->TabIndex == tabIndex) return;

        addon->SetTab(tabIndex);

        for (var i = 0; i < addon->Tabs.Length; i++) addon->Tabs[i].Value->IsSelected = i == tabIndex;
    }

    private static void UpdateFateProgress(AddonFateProgress* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, addon->TabCount);
        if (!addon->IsLoaded || addon->TabIndex == tabIndex)
            return;

        // fake event, so it can call SetEventIsHandled
        var atkEvent = new AtkEvent();
        addon->SetTab(tabIndex, &atkEvent);
    }

    private static void UpdateFieldNotes(AddonMYCWarResultNotebook* addon)
    {
        if (IntersectingCollisionNode == addon->DescriptionCollisionNode)
            return;

        var atkEvent   = new AtkEvent();
        var eventParam = Math.Clamp((addon->CurrentNoteIndex % 10) + WheelState, -1, addon->MaxNoteIndex - 1);

        if (eventParam == -1)
        {
            if (addon->CurrentPageIndex > 0)
            {
                var page = addon->CurrentPageIndex                             - 1;
                addon->AtkUnitBase.ReceiveEvent(AtkEventType.ButtonClick, page + 10, &atkEvent);
                addon->AtkUnitBase.ReceiveEvent(AtkEventType.ButtonClick, 9,         &atkEvent);
            }
        }
        else if (eventParam == 10)
        {
            if (addon->CurrentPageIndex < 4)
            {
                var page = addon->CurrentPageIndex                             + 1;
                addon->AtkUnitBase.ReceiveEvent(AtkEventType.ButtonClick, page + 10, &atkEvent);
            }
        }
        else
            addon->AtkUnitBase.ReceiveEvent(AtkEventType.ButtonClick, eventParam, &atkEvent);
    }

    private static void UpdateMountMinion(AddonMinionMountBase* addon)
    {
        if (addon->CurrentView == AddonMinionMountBase.ViewType.Normal)
        {
            if (addon->TabController.TabIndex == 0 && WheelState < 0)
                addon->SwitchToFavorites();
            else
                UpdateTabController((AtkUnitBase*)addon, &addon->TabController);
        }
        else if (addon->CurrentView == AddonMinionMountBase.ViewType.Favorites && WheelState > 0) addon->TabController.CallbackFunction(0, (AtkUnitBase*)addon);
    }

    private static void UpdateMJIMinionNoteBook(AddonMJIMinionNoteBook* addon)
    {
        var agent = AgentMJIMinionNoteBook.Instance();

        if (agent->CurrentView == AgentMJIMinionNoteBook.ViewType.Normal)
        {
            if (addon->TabController.TabIndex == 0 && WheelState < 0)
            {
                agent->CurrentView                      = AgentMJIMinionNoteBook.ViewType.Favorites;
                agent->SelectedFavoriteMinion.TabIndex  = 0;
                agent->SelectedFavoriteMinion.SlotIndex = agent->SelectedNormalMinion.SlotIndex;
                agent->SelectedFavoriteMinion.MinionId  = agent->GetSelectedMinionId();
                agent->SelectedMinion                   = &agent->SelectedFavoriteMinion;
                agent->HandleCommand(0x407);
            }
            else
            {
                UpdateTabController((AtkUnitBase*)addon, &addon->TabController);
                agent->HandleCommand(0x40B);
            }
        }
        else if (agent->CurrentView == AgentMJIMinionNoteBook.ViewType.Favorites && WheelState > 0)
        {
            agent->CurrentView                    = AgentMJIMinionNoteBook.ViewType.Normal;
            agent->SelectedNormalMinion.TabIndex  = 0;
            agent->SelectedNormalMinion.SlotIndex = agent->SelectedFavoriteMinion.SlotIndex;
            agent->SelectedNormalMinion.MinionId  = agent->GetSelectedMinionId();
            agent->SelectedMinion                 = &agent->SelectedNormalMinion;

            addon->TabController.TabIndex = 0;
            addon->TabController.CallbackFunction(0, (AtkUnitBase*)addon);

            agent->HandleCommand(0x40B);
        }
    }

    // private static void UpdateCurrency(AddonCurrency* addon)
    // {
    //     var atkStage = AtkStage.Instance();
    //     var numberArray = atkStage->GetNumberArrayData(NumberArrayType.Currency);
    //     var currentTab = numberArray->IntArray[0];
    //     var newTab = currentTab;
    //
    //     var enableStates = new bool[addon->Tabs.Length];
    //     for (var i = 0; i < addon->Tabs.Length; i++)
    //     {
    //         enableStates[i] = addon->Tabs[i].Value != null && addon->Tabs[i].Value->IsEnabled;
    //     }
    //
    //     if (_wheelState > 0 && currentTab < enableStates.Length)
    //     {
    //         for (var i = currentTab + 1; i < enableStates.Length; i++)
    //         {
    //             if (enableStates[i])
    //             {
    //                 newTab = i;
    //                 break;
    //             }
    //         }
    //     }
    //     else if (currentTab > 0)
    //     {
    //         for (var i = currentTab - 1; i >= 0; i--)
    //         {
    //             if (enableStates[i])
    //             {
    //                 newTab = i;
    //                 break;
    //             }
    //         }
    //     }
    //
    //     if (currentTab == newTab)
    //         return;
    //
    //     numberArray->SetValue(0, newTab);
    //     addon->AtkUnitBase.OnRequestedUpdate(atkStage->GetNumberArrayData(), atkStage->GetStringArrayData());
    // }

    private static void UpdateInventoryBuddy(AddonInventoryBuddy* addon)
    {
        if (!PlayerState.Instance()->HasPremiumSaddlebag)
            return;

        var tabIndex = GetTabIndex(addon->TabIndex, 2);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab((byte)tabIndex);
    }

    private static void UpdateBuddy(AddonBuddy* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, NumBuddyTabs);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex);

        for (var i = 0; i < NumBuddyTabs; i++)
        {
            var button                                           = addon->RadioButtons.GetPointer(i);
            if (button->Value != null) button->Value->IsSelected = i == addon->TabIndex;
        }
    }

    private static void UpdateMiragePrismPrismBox(AddonMiragePrismPrismBox* addon)
    {
        if (addon->JobDropdown                                   == null ||
            addon->JobDropdown->List                             == null ||
            addon->JobDropdown->List->AtkComponentBase.OwnerNode == null ||
            addon->JobDropdown->List->AtkComponentBase.OwnerNode->AtkResNode.IsVisible())
            return;

        if (addon->OrderDropdown                                   == null ||
            addon->OrderDropdown->List                             == null ||
            addon->OrderDropdown->List->AtkComponentBase.OwnerNode == null ||
            addon->OrderDropdown->List->AtkComponentBase.OwnerNode->AtkResNode.IsVisible())
            return;

        var prevButton = !ModuleConfig.Invert ? addon->PrevButton : addon->NextButton;
        var nextButton = !ModuleConfig.Invert ? addon->NextButton : addon->PrevButton;

        if (prevButton == null || (IsPrev && !prevButton->IsEnabled))
            return;

        if (nextButton == null || (IsNext && !nextButton->IsEnabled))
            return;

        // if (IsAddonOpen("MiragePrismPrismBoxFilter"))
        // return;
        // TODO 先这样写着，但可能有BUG
        if (IsAddonAndNodesReady(GetAddonByName("MiragePrismPrismBoxFilter")))
            return;

        var agent = AgentMiragePrismPrismBox.Instance();
        agent->PageIndex += (byte)WheelState;
        agent->UpdateItems(false, false);
    }

    private static void UpdateCharacter(AddonCharacter* addon)
    {
        var tabIndex = GetTabIndex(addon->TabIndex, addon->TabCount);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex);

        for (var i = 0; i < addon->TabCount; i++)
        {
            var button                                           = addon->Tabs.GetPointer(i);
            if (button->Value != null) button->Value->IsSelected = i == addon->TabIndex;
        }
    }

    private static void UpdateCharacterClass(AddonCharacter* addonCharacter, AddonCharacterClass* addon)
    {
        // prev or next embedded addon
        if (ModuleConfig.HandleCharacter && (addon->TabIndex + WheelState < 0 || addon->TabIndex + WheelState > 1))
        {
            UpdateCharacter(addonCharacter);
            return;
        }

        var tabIndex = GetTabIndex(addon->TabIndex, 2);

        if (addon->TabIndex == tabIndex)
            return;

        addon->SetTab(tabIndex);
    }

    private static void UpdateCharacterRepute(AddonCharacter* addonCharacter, AddonCharacterRepute* addon)
    {
        // prev embedded addon
        if (ModuleConfig.HandleCharacter && addon->SelectedExpansion + WheelState < 0)
        {
            UpdateCharacter(addonCharacter);
            return;
        }

        var tabIndex = GetTabIndex(addon->SelectedExpansion, addon->ExpansionsCount);

        if (addon->SelectedExpansion == tabIndex)
            return;

        var atkEvent = new AtkEvent();
        var data     = new AtkEventData();
        data.ListItemData.SelectedIndex = tabIndex; // technically the index of an id array, but it's literally the same value
        addon->AtkUnitBase.ReceiveEvent((AtkEventType)37, 0, &atkEvent, &data);
    }
    
    private class Config : ModuleConfiguration
    {
        public          bool Invert                = true;
        public readonly bool HandleAetherCurrent   = true;
        public          bool HandleArmouryBoard    = true;
        public          bool HandleAOZNotebook     = true;
        public          bool HandleCharacter       = true;
        public          bool HandleCharacterClass  = true;
        public          bool HandleCharacterRepute = true;
        public          bool HandleInventoryBuddy  = true;

        public bool HandleBuddy = true;

        // public bool HandleCurrency = true;
        // TODO 国服FFCS还没更这个
        public bool HandleOrnamentNoteBook       = true;
        public bool HandleFieldRecord            = true;
        public bool HandleFishGuide              = true;
        public bool HandleMiragePrismPrismBox    = true;
        public bool HandleGoldSaucerCardList     = true;
        public bool HandleGoldSaucerCardDeckEdit = true;
        public bool HandleLovmPaletteEdit        = true;
        public bool HandleInventory              = true;
        public bool HandleMJIMinionNoteBook      = true;
        public bool HandleMinionNoteBook         = true;
        public bool HandleMountNoteBook          = true;
        public bool HandleRetainer               = true;
        public bool HandleFateProgress           = true;
        public bool HandleAdventureNoteBook      = true;
    }
}
