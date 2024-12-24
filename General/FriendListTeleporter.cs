using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using System.Collections.Generic;
using System.Linq;

namespace DailyRoutines.Modules;

public unsafe class FriendListTeleporter : DailyModuleBase
{
    private static readonly TeleportMenuItem TeleportItem = new();

    private static readonly Dictionary<uint, uint> SpecialLocation = new()
    {
        { 128, 129 },
        { 133, 132 },
        { 131, 130 },
        { 399, 478 }
    };

    public override ModuleInfo Info => new()
    {
        Title = "右键好友列表传送", //  GetLoc("ExpandfriendTeleporter"),
        Description = "右键好友列表传送", //GetLoc("ExpandfriendTeleporter"),
        Category = ModuleCategories.General,
        Author = ["Xww"]
    };

    public override void Init()
    {
        DService.ContextMenu.OnMenuOpened += OnMenuOpen;
    }

    public override void Uninit()
    {
        DService.ContextMenu.OnMenuOpened -= OnMenuOpen;
        base.Uninit();
    }

    private static void OnMenuOpen(IMenuOpenedArgs args)
    {
        if (!TeleportItem.IsDisplay(args)) return;
        args.AddMenuItem(TeleportItem.Get());
    }

    private class TeleportMenuItem : MenuItemBase
    {
        private uint _aetheryteId;

        public override string Name { get; protected set; } = "传送到好友地图";

        protected override void OnClicked(IMenuItemClickedArgs args) => Telepo.Instance()->Teleport(_aetheryteId, 0);

        public override bool IsDisplay(IMenuOpenedArgs args)
        {
            if (args.AddonName == "FriendList")
            {
                var friendListAddonAgent = (AgentFriendlist*)DService.Gui.FindAgentInterface(args.AddonName);
                var friendListAddon = (AddonFriendList*)args.AddonPtr;
                if (friendListAddonAgent->InfoProxy->CharData[friendListAddon->FriendList->HeldItemIndex].Location < 1) return false;

                _aetheryteId = GetAetheryteId(friendListAddonAgent->InfoProxy->CharData[friendListAddon->FriendList->HeldItemIndex].Location);
                return _aetheryteId > 0;
            }

            return false;
        }

        private static uint GetAetheryteId(uint Location)
        {
            if (SpecialLocation.TryGetValue(Location, out var tempLocation)) Location = tempLocation;
            var result = DService.AetheryteList
                                 .Where(aetheryte => aetheryte.TerritoryId == Location)
                                 .Select(aetheryte => aetheryte.AetheryteId)
                                 .FirstOrDefault();

            return result;
        }
    }
}
