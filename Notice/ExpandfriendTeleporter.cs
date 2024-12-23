using DailyRoutines.Abstracts;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;

namespace ExpandfriendTeleporter;

public unsafe class ExpandfriendTeleporter : DailyModuleBase
{
    private static Dictionary<uint, uint> other; //处理三大主城的

    public override ModuleInfo Info => new()
    {
        Title = "右键好友列表传送", //  GetLoc("ExpandfriendTeleporter"),
        Description = "右键好友列表传送", //GetLoc("ExpandfriendTeleporter"),
        Category = ModuleCategories.Notice,
        Author = ["Xww"]
    };

    public override void Init()
    {
        DService.ContextMenu.OnMenuOpened += add;
        other ??= new Dictionary<uint, uint>
        {
            { 128, 129 },
            { 133, 132 },
            { 131, 130 }
        };
    }

    public override void Uninit()
    {
        DService.ContextMenu.OnMenuOpened -= add;
    }

    private void add(IMenuOpenedArgs args)
    {
        if (args.AddonName == "FriendList")
        {
            var a = (AgentFriendlist*)DService.Gui.FindAgentInterface(args.AddonName);
            var b = (AddonFriendList*)args.AddonPtr;
            if (a->InfoProxy->CharData[b->FriendList->HeldItemIndex].Location < 1) return;

            var aetid = getAetheryteId(a->InfoProxy->CharData[b->FriendList->HeldItemIndex].Location);
            if (aetid < 1) return;
            var n = new MenuItem();
            n.Name = "传送到好友地图";
            n.OnClicked += clickedArgs => tp(aetid);
            args.AddMenuItem(n);
        }
    }

    private uint getAetheryteId(uint Location)
    {
        if (other.ContainsKey(Location)) Location = other[Location];
        foreach (var aa in DService.AetheryteList)
            if (aa.TerritoryId == Location)
                return aa.AetheryteId;

        return 0;
    }

    private void tp(uint aetid)
    {
        Telepo.Instance()->Teleport(aetid, 0);
    }
}
