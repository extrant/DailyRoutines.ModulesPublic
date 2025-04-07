using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace DailyRoutines.Modules;

public class AutoConfirmPortraitUpdate : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoConfirmPortraitUpdateTitle"),
        Description = GetLoc("AutoConfirmPortraitUpdateDescription"),
        Category    = ModuleCategories.UIOperation
    };

    private static bool SendNotification = true;
    
    public override unsafe void Init()
    {
        AddConfig(nameof(SendNotification), true);
        SendNotification = GetConfig<bool>(nameof(SendNotification));
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "BannerPreview", OnAddon);
        if (BannerPreview != null) OnAddon(AddonEvent.PostSetup, null);
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("SendNotification"), ref SendNotification))
            UpdateConfig(nameof(SendNotification), SendNotification);
    }

    private unsafe void OnAddon(AddonEvent type, AddonArgs? args)
    {
        Callback(BannerPreview, true, 0);
        
        if (SendNotification) NotificationSuccess(GetLoc("AutoConfirmPortraitUpdate-Notification"));
    }
    
    public override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);
    }
}
