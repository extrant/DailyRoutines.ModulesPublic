using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace DailyRoutines.Modules;

public class AutoHideNeedlessPopups : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoHideNeedlessPopupsTitle"),
        Description = GetLoc("AutoHideNeedlessPopupsDescription"),
        Category = ModuleCategories.UIOptimization,
    };

    private static readonly string[] AddonNames = 
    [
        "_NotificationCircleBook", "AchievementInfo", "RecommendList", "PlayGuide", "HowTo", "WebLauncher",
        "LicenseViewer"
    ];

    public override void Init()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonNames, OnAddon);
    }

    private static unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = args.Addon.ToAtkUnitBase();
        if (addon == null) return;

        addon->Close(false);
        addon->FireCloseCallback();
    }

    public override void Uninit()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonNames, OnAddon);
    }
}
