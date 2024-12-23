using DailyRoutines.Abstracts;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Infos;

namespace ExpandfriendTeleporter;

public unsafe class ExpandfriendTeleporter : DailyModuleBase
{
    private static readonly CompSig Teleportsig =
        new(
            "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 8B E9 41 8B D9 48 8B 0D ?? ?? ?? ?? 41 8B F8 8B F2");

    private static Hook<Teleport> _s;
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
        _s ??= DService.Hook.HookFromSignature<Teleport>(Teleportsig.Get(), te);
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

    private int te(int a1, int a2, int a3, int a4, int a5)
    {
        return _s.Original(a1, a2, a3, a4, a5);
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
        _s.Original(202, (int)aetid, 0, 0, 0);
    }

    private delegate int Teleport(int a1, int a2, int a3, int a4, int a5);
}
