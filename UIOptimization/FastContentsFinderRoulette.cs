using System;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Helpers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.Modules;

public unsafe class FastContentsFinderRoulette : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("FastContentsFinderRouletteTitle"),
        Description = GetLoc("FastContentsFinderRouletteDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    public override void Init()
    {
        Overlay       ??= new(this);
        Overlay.Flags |=  ImGuiWindowFlags.NoBackground;
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ContentsFinder", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ContentsFinder", OnAddon);
        if (ContentsFinder != null) OnAddon(AddonEvent.PostSetup, null);
    }

    public override void OverlayUI()
    {
        if (ContentsFinder == null)
        {
            Overlay.IsOpen = false;
            return;
        }

        var currentTab = ContentsFinder->AtkValues[26].UInt;
        if (currentTab != 0) return;

        var listComponent = (AtkComponentNode*)ContentsFinder->GetNodeById(52);
        if (listComponent == null) return;

        for (var i = 0; i < 10; i++)
        {
            var listItemComponent = (AtkComponentNode*)listComponent->Component->UldManager.NodeList[3 + i];
            if (listItemComponent == null) continue;
            
            var nameNode = (AtkTextNode*)listItemComponent->Component->UldManager.SearchNodeById(5);
            if (nameNode == null) continue;
            
            var name = SanitizeSeIcon(SeString.Parse(nameNode->NodeText).TextValue);
            if (string.IsNullOrWhiteSpace(name)) continue;
            
            var lockNode = (AtkImageNode*)listItemComponent->Component->UldManager.SearchNodeById(3);
            if (lockNode == null) continue;
            
            var levelNode = (AtkTextNode*)listItemComponent->Component->UldManager.SearchNodeById(18);
            if (levelNode == null) continue;
            
            if (levelNode->IsVisible())
                levelNode->ToggleVisibility(false);

            var position = new Vector2(levelNode->ScreenX + 6f, levelNode->ScreenY - 8f);
            ImGui.SetNextWindowPos(position);
            if (ImGui.Begin($"FastContentsFinderRouletteOverlay-{name}",
                            ImGuiWindowFlags.NoDecoration    | ImGuiWindowFlags.AlwaysAutoResize   |
                            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove             |
                            ImGuiWindowFlags.NoDocking       | ImGuiWindowFlags.NoFocusOnAppearing |
                            ImGuiWindowFlags.NoNav           | ImGuiWindowFlags.NoBackground))
            {
                if (DService.Condition[ConditionFlag.InDutyQueue])
                {
                    if (ImGui.SmallButton($"{GetLoc("Cancel")}###{name}"))
                        ContentsFinderHelper.CancelDutyApply();
                }
                else
                {
                    using (ImRaii.Disabled(lockNode->IsVisible()))
                    {
                        if (ImGui.SmallButton($"{LuminaCache.GetRow<Addon>(2504)!.Value.Text.ExtractText()}###{name}"))
                        {
                            var content = LuminaCache.Get<ContentRoulette>()
                                                     .FirstOrDefault(x => x.Name.ExtractText().Contains(name, StringComparison.OrdinalIgnoreCase));
                            if (content.RowId != 0)
                                ContentsFinderHelper.RequestDutyRoulette((ushort)content.RowId, ContentsFinderHelper.DefaultOption);
                        }
                    }
                }
                ImGui.End();
            }
        }
    }
    
    public override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);
        
        base.Uninit();
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup   => true,
            AddonEvent.PreFinalize => false,
            _                      => Overlay.IsOpen
        };
    }
}
