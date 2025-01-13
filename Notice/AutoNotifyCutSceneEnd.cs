using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Diagnostics;
using System.Linq;

namespace DailyRoutines.Modules;

public class AutoNotifyCutSceneEnd : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoNotifyCutSceneEndTitle"),
        Description = GetLoc("AutoNotifyCutSceneEndDescription"),
        Category = ModuleCategories.Notice,
    };

    private static bool IsDutyEnd;
    private static bool IsSomeoneInCutscene;

    private static Stopwatch? Stopwatch;

    public override void Init()
    {
        Stopwatch ??= new Stopwatch();

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_PartyList", OnPartyList);
        FrameworkManager.Register(false, OnUpdate);
        DService.DutyState.DutyCompleted += OnDutyComplete;
        DService.ClientState.TerritoryChanged += OnZoneChanged;
    }

    private static unsafe void OnPartyList(AddonEvent type, AddonArgs args)
    {
        if (IsSomeoneInCutscene || IsDutyEnd || DService.ClientState.IsPvP || !BoundByDuty) return;

        var isSBInCutScene = DService.PartyList.Any(member => member.GameObject != null &&
                                                             ((Character*)member.GameObject.Address)->CharacterData
                                                             .OnlineStatus == 15);

        if (isSBInCutScene)
        {
            Stopwatch.Restart();
            IsSomeoneInCutscene = true;
        }
    }

    private static unsafe void OnUpdate(IFramework framework)
    {
        if (!IsSomeoneInCutscene) return;
        if (!Throttler.Throttle("AutoNotifyCutSceneEnd")) return;
        if (!IsScreenReady()) return;

        var isSBInCutScene = DService.PartyList.Any(member => member.GameObject != null &&
                                                             ((Character*)member.GameObject.Address)->CharacterData
                                                             .OnlineStatus == 15);

        if (isSBInCutScene) return;

        IsSomeoneInCutscene = false;

        if (Stopwatch.Elapsed < TimeSpan.FromSeconds(4))
            Stopwatch.Reset();
        else
        {
            var message = Lang.Get("AutoNotifyCutSceneEnd-NotificationMessage");
            Chat(message);
            NotificationInfo(message);
            Speak(message);
        }
    }

    private static void OnZoneChanged(ushort zone)
    {
        Stopwatch.Reset();
        IsDutyEnd = false;
    }

    private static void OnDutyComplete(object? sender, ushort duty)
    {
        Stopwatch.Reset();
        IsDutyEnd = true;
    }

    public override void Uninit()
    {
        Stopwatch?.Reset();
        Stopwatch = null;

        DService.DutyState.DutyCompleted -= OnDutyComplete;
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        DService.AddonLifecycle.UnregisterListener(OnPartyList);
    }
}
