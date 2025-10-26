using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace DailyRoutines.ModulesPublic;

public unsafe class LargerColorantColoringPreviewComponent : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("LargerColorantColoringPreviewComponentTitle"),
        Description = GetLoc("LargerColorantColoringPreviewComponentDescription"),
        Category    = ModuleCategories.UIOptimization,
    };
    
    // 懒得恢复了, 就这样
    protected override void Init()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ColorantColoring", OnAddon);
        if (IsAddonAndNodesReady(ColorantColoring))
            OnAddon(AddonEvent.PostSetup, null);
    }

    protected override void Uninit() => 
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ColorantColoring", OnAddon);

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = ColorantColoring;
        if (addon == null) return;
        
        for (var i = 0; i < addon->WindowNode->Component->UldManager.NodeListCount; i++)
        {
            var node = addon->WindowNode->Component->UldManager.NodeList[i];
            if (node == null) continue;

            if (node->Width == 654)
                node->SetWidth(674);
                        
            if (node->Width == 640)
                node->SetWidth(660);
            
            if (node->Width == 649)
                node->SetWidth(669);
            
            if (node->Width == 646)
                node->SetWidth(666);
            
            if (node->X == 621)
                node->SetXFloat(641);
        }

        var previewComponent = addon->GetComponentNodeById(71);
        if (previewComponent != null)
        {
            previewComponent->SetWidth(252);
            previewComponent->SetHeight(420);
            previewComponent->SetXFloat(-10);
            previewComponent->SetYFloat(1);

            var borderNode = previewComponent->Component->UldManager.SearchNodeById(3);
            if (borderNode != null)
            {
                borderNode->SetWidth(252);
                borderNode->SetHeight(420);
            }
            
            var collisionNode = previewComponent->Component->UldManager.SearchNodeById(5);
            if (collisionNode != null)
            {
                collisionNode->SetWidth(252);
                collisionNode->SetHeight(420);
            }
            
            var imageNode = previewComponent->Component->UldManager.SearchNodeById(4);
            if (imageNode != null)
            {
                imageNode->SetWidth(244);
                imageNode->SetHeight(412);
            }
        }

        var gearContainerNode = addon->GetNodeById(17);
        if (gearContainerNode != null)
            gearContainerNode->SetWidth(646);

        for (var i = 72U; i < 81; i++)
        {
            var checkBoxNode = addon->GetComponentNodeById(i);
            if (checkBoxNode == null) continue;
            
            checkBoxNode->SetYFloat(424);
        }
    }
}
