using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoClaimPVPRewards : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoClaimPVPRewardsTitle"),
        Description = GetLoc("AutoClaimPVPRewardsDescription"),
        Category    = ModuleCategories.UIOperation,
    };

    private static TextButtonNode? ClaimButton;

    protected override void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 5_000 };
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,   "PvpReward", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "PvpReward", OnAddon);
        if (PvpReward != null) 
            OnAddon(AddonEvent.PostSetup, null);
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);
        OnAddon(AddonEvent.PreFinalize, null);
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {

        switch (type)
        {
            case AddonEvent.PostDraw:
                if (PvpReward == null) return;

                var closeButton = PvpReward->GetComponentButtonById(124);
                if (closeButton != null && closeButton->OwnerNode->IsVisible())
                    closeButton->OwnerNode->ToggleVisibility(false);
                
                if (ClaimButton == null)
                {
                    var resNode = PvpReward->RootNode;
                    if (resNode == null) return;
                    
                    ClaimButton = new()
                    {
                        Size      = new(280, 28),
                        Position  = new(370, 500),
                        IsVisible = true,
                        SeString  = GetLoc("AutoClaimPVPRewards-Button"),
                        OnClick = () =>
                        {
                            var currentRank = PvpReward->AtkValues[7].UInt;
                            if (currentRank <= 1 || IsTrophyCrystalAboutToReachLimit()) return;

                            for (var i = 0; i < currentRank; i++)
                            {
                                TaskHelper.Enqueue(() =>
                                                   {
                                                       if (!IsTrophyCrystalAboutToReachLimit()) return;
                                                       TaskHelper.Abort();
                                                   }, $"CheckTCAmount_Rank{i}");

                                TaskHelper.Enqueue(
                                    () =>
                                    {
                                        ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.CollectTrophyCrystal);
                                        ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.CollectTrophyCrystal, 1);
                                    },
                                    $"ClaimTC_Rank{i}");

                                TaskHelper.DelayNext(10, $"Delay_Rank{i}");
                            }
                        },
                        NodeId = 10001
                    };
                    
                    ClaimButton.AttachNode(resNode);
                }

                ClaimButton.IsEnabled = !TaskHelper.IsBusy;
                
                break;
            case AddonEvent.PreFinalize:
                ClaimButton?.DetachNode();
                ClaimButton = null;
                break;
        }
    }
    
    private static bool IsTrophyCrystalAboutToReachLimit() 
        => InventoryManager.Instance()->GetInventoryItemCount(36656) > 1_9000;
}
