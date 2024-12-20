using Dalamud.Hooking;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;

namespace DailyRoutines.Modules;

public class RealQueuePosition : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("RealQueuePositionTitle"),
        Description = GetLoc("RealQueuePositionDescription"),
        Category = ModuleCategories.UIOptimization,
        Author = ["逆光"]
    };

    private static readonly CompSig WorldTravelQueuePositionDataSig = new("83 F8 ?? 73 ?? 44 8B C0 1B D2");
    private static readonly MemoryPatch WorldTravelQueuePositionDataPatch =
        new(WorldTravelQueuePositionDataSig.Get(), [0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly CompSig WorldTravelQueuePositionAddonSig =
        new("81 C2 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D0 48 8D 8C 24");
    private static readonly MemoryPatch WorldTravelQueuePositionAddonPatch =
        new(WorldTravelQueuePositionAddonSig.ScanText() + 2, [0xF4, 0x30]);

    private static readonly CompSig ContentFinderQueuePositionDataSig = new("40 ?? 57 41 ?? 48 ?? ?? ?? 0f ?? ?? ?? 49");
    private delegate byte ContentFinderQueuePositionDataDelegate(nint a1, uint a2, nint a3);
    private static Hook<ContentFinderQueuePositionDataDelegate>? ContentFinderQueuePositionDataHook;

    public override void Init()
    {
        WorldTravelQueuePositionDataPatch.Enable();
        WorldTravelQueuePositionAddonPatch.Enable();

        ContentFinderQueuePositionDataHook ??=
            DService.Hook.HookFromSignature<ContentFinderQueuePositionDataDelegate>(
            ContentFinderQueuePositionDataSig.Get(), ContentFinderQueuePositionDataDetour);
        ContentFinderQueuePositionDataHook.Enable();
    }

    private static byte ContentFinderQueuePositionDataDetour(nint a1, uint a2, nint a3)
    {
        uint v9 = Marshal.ReadByte(new nint(a3 + 4));
        //var v12 = Marshal.ReadByte(new nint(a1 + 91));
        //var v14 = Marshal.ReadByte(new nint(a1 + 92));
        //DService.Log.Debug($"v9:{v9}, v12:{v12}, v14:{v14}");

        if (v9 != 0)
        {
            Marshal.WriteByte(new nint(a1 + 91), (byte)v9);
            Marshal.WriteByte(new nint(a1 + 92), (byte)v9);
        }

        return ContentFinderQueuePositionDataHook.Original(a1, a2, a3);
    }

    public override void Uninit()
    {
        WorldTravelQueuePositionDataPatch.Disable();
        WorldTravelQueuePositionAddonPatch.Disable();
        base.Uninit();
    }
}
