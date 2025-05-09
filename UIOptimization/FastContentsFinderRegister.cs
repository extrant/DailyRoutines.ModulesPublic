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

public unsafe class FastContentsFinderRegister : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("FastContentsFinderRegisterTitle"),
        Description = GetLoc("FastContentsFinderRegisterDescription"),
        Category    = ModuleCategories.UIOptimization,
        ModulesPrerequisite = ["ContentFinderCommand"]
    };

    public override void Init()
    {
        Overlay       ??= new(this);
        Overlay.Flags |=  ImGuiWindowFlags.NoBackground;
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "ContentsFinder", OnAddon);
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

        var listComponent = (AtkComponentNode*)ContentsFinder->GetNodeById(52);
        if (listComponent == null) return;
        
        var treelistComponent = (AtkComponentTreeList*)listComponent->Component;
        if (treelistComponent == null) return;
        
        var otherPFNode = (AtkTextNode*)ContentsFinder->GetNodeById(57);
        if (otherPFNode == null) return;

        var listLength = treelistComponent->ListLength;
        if (listLength == 0) return;
        
        var lineHeight = ImGui.GetTextLineHeight() - ImGui.GetStyle().FramePadding.Y;
        
        for (var i = 0; i < Math.Min(listLength, 45); i++)
        {
            var offset = 3 + i;
            if (offset >= listComponent->Component->UldManager.NodeListCount) break;
            
            var listItemComponent = (AtkComponentNode*)listComponent->Component->UldManager.NodeList[offset];
            if (listItemComponent == null || 
                listItemComponent->Y >= 300 ||
                listItemComponent->ScreenY < listComponent->ScreenY ||
                listItemComponent->ScreenY + 20 > otherPFNode->ScreenY) continue;
            
            var nameNode = (AtkTextNode*)listItemComponent->Component->UldManager.SearchNodeById(5);
            if (nameNode == null) continue;
            
            var name = string.Empty;
            try { name = nameNode->NodeText.ExtractText(); }
            catch { name = string.Empty; }
            if (string.IsNullOrWhiteSpace(name)) continue;
            
            var lockNode = (AtkImageNode*)listItemComponent->Component->UldManager.SearchNodeById(3);
            if (lockNode == null) continue;
            
            var levelNode = (AtkTextNode*)listItemComponent->Component->UldManager.SearchNodeById(18);
            if (levelNode == null) continue;
            
            if (levelNode->IsVisible())
                levelNode->ToggleVisibility(false);

            var position = new Vector2(levelNode->ScreenX + (currentTab == 0 ? 24f : 0f), levelNode->ScreenY - 8f);
            ImGui.SetNextWindowPos(position);
            if (ImGui.Begin($"FastContentsFinderRouletteOverlay-{listItemComponent->NodeId}",
                            ImGuiWindowFlags.NoDecoration    | ImGuiWindowFlags.AlwaysAutoResize   |
                            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove             |
                            ImGuiWindowFlags.NoDocking       | ImGuiWindowFlags.NoFocusOnAppearing |
                            ImGuiWindowFlags.NoNav           | ImGuiWindowFlags.NoBackground))
            {
                if (DService.Condition[ConditionFlag.InDutyQueue])
                {
                    if (DService.Texture.TryGetFromGameIcon(new(61502), out var explorerTexture))
                    {
                        if (ImGui.ImageButton(explorerTexture.GetWrapOrEmpty().ImGuiHandle, new(lineHeight)))
                            ContentsFinderHelper.CancelDutyApply();
                        ImGuiOm.TooltipHover($"{GetLoc("Cancel")}");
                    }
                }
                else
                {
                    var sharedPrefix = $"{levelNode->NodeText.ExtractText()} {name}";

                    name = name.Replace(" ", string.Empty);
                    using (ImRaii.Group())
                    {
                        using (ImRaii.Disabled(lockNode->IsVisible()))
                        {
                            if (DService.Texture.TryGetFromGameIcon(new(60081), out var joinTexture))
                            {
                                if (ImGui.ImageButton(joinTexture.GetWrapOrEmpty().ImGuiHandle, new(lineHeight)))
                                {
                                    ChatHelper.SendMessage($"/pdrduty {(currentTab == 0 ? "r" : "n")} {name}");
                                    ChatHelper.SendMessage($"/pdrduty {(currentTab != 0 ? "r" : "n")} {name}");
                                }                                
                                ImGuiOm.TooltipHover($"{sharedPrefix}");
                            }
                            
                            if (currentTab != 0)
                            {
                                if (IsConflictKeyPressed())
                                {
                                    if (DService.Texture.TryGetFromGameIcon(new(60648), out var explorerTexture))
                                    {
                                        ImGui.SameLine();
                                        if (ImGui.ImageButton(explorerTexture.GetWrapOrEmpty().ImGuiHandle, new(lineHeight)))
                                            ChatHelper.SendMessage($"/pdrduty n {name} explorer");
                                        ImGuiOm.TooltipHover($"{sharedPrefix} ({LuminaGetter.GetRow<Addon>(13038)!.Value.Text.ExtractText()})");
                                    }
                                }
                                else
                                {
                                    if (DService.Texture.TryGetFromGameIcon(new(60641), out var unrestTexture))
                                    {
                                        ImGui.SameLine();
                                        if (ImGui.ImageButton(unrestTexture.GetWrapOrEmpty().ImGuiHandle, new(lineHeight)))
                                            ChatHelper.SendMessage($"/pdrduty n {name} unrest");
                                        ImGuiOm.TooltipHover($"{sharedPrefix} ({LuminaGetter.GetRow<Addon>(10008)!.Value.Text.ExtractText()})\n" +
                                                             $"[{GetLoc("FastContentsFinderRegister-HoldConflictKeyToToggle")}]");
                                    }
                                }
                            }
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
