﻿using System.Collections.Generic;
using DailyRoutines.Abstracts;
using Dalamud.Game.Gui.ContextMenu;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoInventoryTransfer : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoInventoryTransferTitle"),
        Description = GetLoc("AutoInventoryTransferDescription"),
        Category    = ModuleCategories.UIOperation,
        Author      = ["Yangdoubao"]
    };

    private static readonly List<string> MenuTexts =
    [
        LuminaWrapper.GetAddonText(97),
        LuminaWrapper.GetAddonText(98),
        LuminaWrapper.GetAddonText(881),
        LuminaWrapper.GetAddonText(887)
    ];

    public override void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 2_000 };

        DService.ContextMenu.OnMenuOpened += OnContextMenuOpened;
    }

    public override void ConfigUI() => ConflictKeyText();

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (!IsConflictKeyPressed() || !IsInventoryOpen()) return;
        
        TaskHelper.Enqueue(() => IsAddonAndNodesReady(ContextMenu));
        TaskHelper.Enqueue(() => { ClickContextMenu(MenuTexts); });
        
        return;

        bool IsInventoryOpen()
            => IsAddonAndNodesReady(Inventory)          ||
               IsAddonAndNodesReady(InventoryLarge)     ||
               IsAddonAndNodesReady(InventoryExpansion) ||
               IsAddonAndNodesReady(InventoryRetainer)  ||
               IsAddonAndNodesReady(InventoryRetainerLarge);
    }

    public override void Uninit()
    {
        DService.ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        
        base.Uninit();
    }
}
