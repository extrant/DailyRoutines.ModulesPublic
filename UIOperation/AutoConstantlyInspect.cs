using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

public class AutoConstantlyInspect : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoConstantlyInspectTitle"),
        Description = GetLoc("AutoConstantlyInspectDescription"),
        Category = ModuleCategories.UIOperation,
    };

    public override void Init()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemInspectionResult", OnAddon);
    }

    public override void ConfigUI() { ConflictKeyText(); }

    private static unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (IsConflictKeyPressed())
        {
            NotificationSuccess(Lang.Get("ConflictKey-InterruptMessage"));
            return;
        }

        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null) return;

        var nextButton = addon->GetButtonNodeById(74);
        if (nextButton == null || !nextButton->IsEnabled) return;
        SendEvent(AgentId.ItemInspection, 3, 0);
        addon->Close(true);
    }

    public override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
