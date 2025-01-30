using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyRoutines.Modules;

public class AutoNotifyBonusFate : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoNotifyBonusFateTitle"),
        Description = GetLoc("AutoNotifyBonusFateDescription"),
        Category = ModuleCategories.Notice,
        Author = ["Due"]
    };

    private static List<IFate> LastFates = [];
    private static readonly List<ushort> ValidTerritory = [1187, 1188, 1189, 1190, 1191, 1192]; // All Dawntrail maps

    public override void Init()
    {
        FrameworkManager.Register(false, OnUpdate);
        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    private static void OnTerritoryChanged(ushort _) => LastFates.Clear();

    private static void OnUpdate(IFramework _)
    {
        if (!Throttler.Throttle("AutoNotifyBonusFateCheck", 5_000) ||
            DService.ClientState.LocalPlayer == null) return;
        if (!ValidTerritory.Contains(DService.ClientState.TerritoryType)) return;

        if (DService.Fate is not { Length: > 0 } fateTable) return;
        if (LastFates.Count != 0 && fateTable.SequenceEqual(LastFates)) return;
        var newFates = LastFates.Count == 0 ? fateTable : fateTable.Except(LastFates);
        
        foreach (var item in newFates)
        {
            if (item == null || !item.HasBonus) continue;

            var message = GetLoc("AutoNotifyBonusFate-Notification");
            NotificationInfo(message);
            Speak(message);
            break;
        }
        LastFates = [.. fateTable];
    }

    public override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        DService.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

}
