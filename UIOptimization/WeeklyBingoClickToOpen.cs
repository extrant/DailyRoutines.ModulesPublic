using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyRoutines.Modules;

public class WeeklyBingoClickToOpen : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("WeeklyBingoClickToOpenTitle"),
        Description = GetLoc("WeeklyBingoClickToOpenDescription"),
        Category = ModuleCategories.UIOptimization,
        Author = ["Due"]
    };

    private readonly IAddonEventHandle?[] eventHandles = new IAddonEventHandle?[16];

    public override void Init()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnAddonSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "WeeklyBingo", OnAddonFinalize);
    }

    public override unsafe void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddonSetup, OnAddonFinalize);

        var addon = (AddonWeeklyBingo*)DService.Gui.GetAddonByName("WeeklyBingo");
        if (addon is not null)
        {
            ResetEventHandles();
        }
    }

    private unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        var addonWeeklyBingo = (AddonWeeklyBingo*)args.Addon;

        ResetEventHandles();
        foreach (var index in Enumerable.Range(0, 16))
        {
            var dutySlot = addonWeeklyBingo->DutySlotList[index];
            eventHandles[index] = DService.AddonEvent.AddEvent((nint)addonWeeklyBingo, (nint)dutySlot.DutyButton->OwnerNode, AddonEventType.ButtonClick, OnDutySlotClick);
        }
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args)
    {
        ResetEventHandles();
    }

    private unsafe void OnDutySlotClick(AddonEventType atkEventType, IntPtr atkUnitBase, IntPtr atkResNode)
    {
        var dutyButtonNode = (AtkResNode*)atkResNode;
        var tileIndex = (int)dutyButtonNode->NodeId - 12;

        var selectedTask = PlayerState.Instance()->GetWeeklyBingoTaskStatus(tileIndex);
        var bingoData = PlayerState.Instance()->WeeklyBingoOrderData[tileIndex];

        if (selectedTask is PlayerState.WeeklyBingoTaskStatus.Open)
        {
            var dutiesForTask = OrderDataToTerritory(bingoData);
            var territoryType = dutiesForTask.FirstOrDefault();
            var cfc = LuminaCache.Get<ContentFinderCondition>().FirstOrDefault(cfc => cfc.TerritoryType.RowId == territoryType);
            if (cfc.RowId is 0) return;

            AgentContentsFinder.Instance()->OpenRegularDuty(cfc.RowId);
        }
    }

    private void ResetEventHandles()
    {
        foreach (var index in Enumerable.Range(0, 16))
        {
            if (eventHandles[index] is { } handle)
            {
                DService.AddonEvent.RemoveEvent(handle);
                eventHandles[index] = null;
            }
        }
    }

    public static List<uint> OrderDataToTerritory(uint orderDataId)
    {
        var bingoOrderData = LuminaCache.Get<WeeklyBingoOrderData>().GetRow(orderDataId);

        switch (bingoOrderData.Type)
        {
            case 0: // Specific Duty
                return [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(c => c.Content.RowId == bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(c => c.TerritoryType.RowId)];

            case 1: // Dungeon at specific level
                return [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired == bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];

            case 2: // Dungeon at level range
                return [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= bingoOrderData.Data.RowId - (bingoOrderData.Data.RowId > 50 ? 9 : 49) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId - 1)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];

            case 3: // Various special categories
                return bingoOrderData.Unknown1 switch
                {
                    1 => [], // Treasure Map
                    2 => [], // PvP
                    3 => [.. LuminaCache.Get<ContentFinderCondition>()
                        .Where(m => m.ContentType.RowId is 21)
                        .OrderBy(row => row.SortKey)
                        .Select(m => m.TerritoryType.RowId)], // Deep Dungeon
                    _ => [],
                };

            case 4: // NRs / ARs
                return bingoOrderData.Data.RowId switch
                {
                    // Bahamut Binding Coil, Second Coil, Final Coil
                    2 => [241, 242, 243, 244, 245],
                    3 => [355, 356, 357, 358],
                    4 => [193, 194, 195, 196],

                    // Alexander Gordias, Midas, The Creator
                    5 => [442, 443, 444, 445],
                    6 => [520, 521, 522, 523],
                    7 => [580, 581, 582, 583],

                    // Omega Deltascape, Sigmascape, Alphascape
                    8 => [691, 692, 693, 694],
                    9 => [748, 749, 750, 751],
                    10 => [798, 799, 800, 801],

                    // Eden's Gate: Resurrection or Descent
                    11 => [849, 850],
                    // Eden's Gate: Inundation or Sepulture
                    12 => [851, 852],
                    // Eden's Verse: Fulmination or Furor
                    13 => [902, 903],
                    // Eden's Verse: Iconoclasm or Refulgence
                    14 => [904, 905],
                    // Eden's Promise: Umbra or Litany
                    15 => [942, 943],
                    // Eden's Promise: Anamorphosis or Eternity
                    16 => [944, 945],

                    // Asphodelos: First or Second Circles
                    17 => [1002, 1004],
                    // Asphodelos: Third or Fourth Circles
                    18 => [1006, 1008],
                    // Abyssos: Fifth or Sixth Circles
                    19 => [1081, 1083],
                    // Abyssos: Seventh or Eight Circles
                    20 => [1085, 1087],
                    // Anabaseios: Ninth or Tenth Circles
                    21 => [1147, 1149],
                    // Anabaseios: Eleventh or Twelwth Circles
                    22 => [1151, 1153],

                    // Eden's Gate
                    23 => [849, 850, 851, 852],
                    // Eden's Verse
                    24 => [902, 903, 904, 905],
                    // Eden's Promise
                    25 => [942, 943, 944, 945],

                    // Alliance Raids (A Realm Reborn)
                    26 => [174, 372, 151],
                    // Alliance Raids (Heavensward)
                    27 => [508, 556, 627],
                    // Alliance Raids (Stormblood)
                    28 => [734, 776, 826],
                    // Alliance Raids (Shadowbringers)
                    29 => [882, 917, 966],
                    // Alliance Raids (Endwalker)
                    30 => [1054, 1118, 1178],

                    // Asphodelos
                    31 => [1002, 1004, 1006, 1008],
                    // Abyssos
                    32 => [1081, 1083, 1085, 1087],
                    // Anabaseios
                    33 => [1147, 1149, 1151, 1153],

                    // AAC Light-heavyweight M1/2
                    34 => [1225, 1227],
                    // AAC Light-heavyweight M3/4
                    35 => [1229, 1231],

                    _ => [],
                };

            case 5: // Larger level range
                return bingoOrderData.Data.RowId switch
                {
                    49 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= 1 && m.ClassJobLevelRequired <= 49)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    79 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= 51 && m.ClassJobLevelRequired <= 79)
                    .Where(m => m.ClassJobLevelRequired % 10 != 0)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    99 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= 81 && m.ClassJobLevelRequired <= 99)
                    .Where(m => m.ClassJobLevelRequired % 10 != 0)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    _ => [],
                };

            case 6:
                return bingoOrderData.Data.RowId switch
                {
                    60 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired == 50 || m.ClassJobLevelRequired == 60)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    80 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired == 70 || m.ClassJobLevelRequired == 80)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    90 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired == 90)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    _ => [],
                };

            case 7:
                return bingoOrderData.Data.RowId switch
                {
                    60 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 4)
                    .Where(m => m.ClassJobLevelRequired >= 50 && m.ClassJobLevelRequired <= 60)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    100 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 4)
                    .Where(m => m.ClassJobLevelRequired >= 70 && m.ClassJobLevelRequired <= 100)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    _ => [],
                };

            case 8:
                return bingoOrderData.Data.RowId switch
                {
                    60 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 5)
                    .Where(m => m.ClassJobLevelRequired >= 50 && m.ClassJobLevelRequired <= 60)
                    .Where(m => m.AllianceRoulette == true)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    90 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 5)
                    .Where(m => m.ClassJobLevelRequired >= 70 && m.ClassJobLevelRequired <= 90)
                    .Where(m => m.AllianceRoulette == true)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    _ => [],
                };

            case 9:
                return bingoOrderData.Data.RowId switch
                {
                    60 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 5)
                    .Where(m => m.NormalRaidRoulette == true)
                    .Where(m => m.ClassJobLevelRequired >= 50 && m.ClassJobLevelRequired <= 60)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    100 => [.. LuminaCache.Get<ContentFinderCondition>()
                    .Where(m => m.ContentType.RowId is 5)
                    .Where(m => m.NormalRaidRoulette == true)
                    .Where(m => m.ClassJobLevelRequired >= 70 && m.ClassJobLevelRequired <= 100)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)],
                    _ => [],
                };

        }
        return [];
    }
}
