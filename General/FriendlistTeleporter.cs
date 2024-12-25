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

public class FriendlistTeleporter : DailyModuleBase
{
    private static readonly TeleportMenuItem TeleportItem = new();

    public override ModuleInfo Info => new()
    {
        Title = GetLoc("FriendlistTeleporterTitle"),
        Description = GetLoc("FriendlistTeleporterDescription"),
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
        private uint aetheryteID;

        public override string Name { get; protected set; } = GetLoc("FriendlistTeleporter-MenuItemTeleport");

        protected override unsafe void OnClicked(IMenuItemClickedArgs args) => Telepo.Instance()->Teleport(aetheryteID, 0);

        public override unsafe bool IsDisplay(IMenuOpenedArgs args)
        {
            if (args.AddonName == "FriendList")
            {
                var agentFriendlist = AgentFriendlist.Instance();
                var zoneID =
                    agentFriendlist->InfoProxy->CharData[((AddonFriendList*)FriendList)->FriendList->HeldItemIndex]
                        .Location;
                return zoneID > 0 && GetAetheryteId(zoneID, out aetheryteID);
            }

            return false;
        }

        private static bool GetAetheryteId(uint zoneID, out uint aetheryteID)
        {
            zoneID = zoneID switch
            {
                128 => 129,
                133 => 132,
                131 => 130,
                399 => 478,
                _ => zoneID
            };
            aetheryteID = DService.AetheryteList
                                  .Where(aetheryte => aetheryte.TerritoryId == zoneID)
                                  .Select(aetheryte => aetheryte.AetheryteId)
                                  .FirstOrDefault();

            return aetheryteID > 0;
        }
    }
}
