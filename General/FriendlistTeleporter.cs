using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
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

        public override bool IsDisplay(IMenuOpenedArgs args) => args.Target is MenuTargetDefault target
                                                                && target.TargetCharacter?.Location.GameData is not null
                                                                && args.AddonName == "FriendList"
                                                                && GetAetheryteId(
                                                                    target.TargetCharacter.Location.GameData.RowId,
                                                                    out aetheryteID);

        private static bool GetAetheryteId(uint zoneID, out uint aetheryteID)
        {
            aetheryteID = 0;
            if (zoneID == 0) return false;
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
