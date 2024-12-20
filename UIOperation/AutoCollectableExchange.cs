using DailyRoutines.Abstracts;
using DailyRoutines.Infos.Clicks;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

public unsafe class AutoCollectableExchange : DailyModuleBase
{
    private static readonly CompSig HandInCollectablesSig = new("48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B F1 48 8B 49"); 
    private delegate nint HandInCollectablesDelegate(AgentInterface* agentCollectablesShop);
    private static HandInCollectablesDelegate? HandInCollectables;
    
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoCollectableExchangeTitle"),
        Description = GetLoc("AutoCollectableExchangeDescription"),
        Category = ModuleCategories.UIOperation,
    };

    public override void Init()
    {
        TaskHelper ??= new();
        Overlay ??= new(this);

        HandInCollectables ??= HandInCollectablesSig.GetDelegate<HandInCollectablesDelegate>();

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CollectablesShop", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "CollectablesShop", OnAddon);
        if (CollectablesShop != null) OnAddon(AddonEvent.PostSetup, null);
    }

    public override void OverlayUI()
    {
        if (CollectablesShop == null)
        {
            Overlay.IsOpen = false;
            return;
        }
        var buttonNode = CollectablesShop->GetNodeById(51);
        if (buttonNode == null) return;

        ImGui.SetWindowPos(new(buttonNode->ScreenX - ImGui.GetWindowSize().X, buttonNode->ScreenY + 4f));

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, GetLoc("AutoCollectableExchangeTitle"));

        ImGui.SameLine();
        using (ImRaii.Disabled(!buttonNode->NodeFlags.HasFlag(NodeFlags.Enabled) || TaskHelper.IsBusy))
        {
            if (ImGui.Button(GetLoc("Start")))
                EnqueueExchange();
        }

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("Stop"))) TaskHelper.Abort();
    }

    private void EnqueueExchange()
    {
        TaskHelper.Enqueue(() =>
        {
            if (CollectablesShop == null || IsAddonAndNodesReady(SelectYesno))
            {
                TaskHelper.Abort();
                return true;
            }

            var list = CollectablesShop->GetComponentNodeById(31)->GetAsAtkComponentList();
            if (list == null) return false;

            if (list->ListLength <= 0)
            {
                TaskHelper.Abort();
                return true;
            }

            HandInCollectables(AgentModule.Instance()->GetAgentByInternalId(AgentId.CollectablesShop));
            return true;
        }, "ClickExchange");

        TaskHelper.Enqueue(EnqueueExchange, "EnqueueNewRound");
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };
    }

    public override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
