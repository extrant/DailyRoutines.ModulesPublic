using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DailyRoutines.Modules;

public class RealQueuePosition : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("RealQueuePositionTitle"),
        Description = GetLoc("RealQueuePositionDescription"),
        Category = ModuleCategories.UIOptimization,
        Author = ["逆光 / Nukoooo"]
    };

    private DateTime _eta = DateTime.Now;

    private nint GetAgentDataAddress;
    private readonly CompSig GetAgentDataAddressSig = new("E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 0F 84 ?? ?? ?? ?? 80 B8 ?? ?? ?? ?? ?? 0F 84");

    private nint SetStringAddress;
    private readonly CompSig SetStringAddressSig = new("48 83 EC ?? 0F B6 44 24 ?? C6 44 24");

    private readonly CompSig AgentWorldTravelUpdaterSig = new("40 53 56 57 41 54 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 4C 8B FA");
    private delegate bool AgentWorldTravelUpdateDelegate(nint a1, nint a2, nint a3, bool a4);
    private Hook<AgentWorldTravelUpdateDelegate> AgentWorldTravelUpdateHook;

    private readonly CompSig UpdateWorldTravelDataSig = new("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B D9 48 8B FA 0F B6 4A");
    private delegate void UpdateWorldTravelDataDelegate(nint a1, nint a2);
    private Hook<UpdateWorldTravelDataDelegate> UpdateWorldTravelDataHook;

    private readonly CompSig ContentFinderQueuePositionDataSig = new("40 ?? 57 41 ?? 48 ?? ?? ?? 0f ?? ?? ?? 49");
    private delegate byte ContentFinderQueuePositionDataDelegate(nint a1, uint a2, nint a3);
    private Hook<ContentFinderQueuePositionDataDelegate>? ContentFinderQueuePositionDataHook;

    public override void Init()
    {
        GetAgentDataAddress = GetAgentDataAddressSig.ScanText();
        SetStringAddress = SetStringAddressSig.ScanText();

        AgentWorldTravelUpdateHook ??= DService.Hook.HookFromSignature<AgentWorldTravelUpdateDelegate>(AgentWorldTravelUpdaterSig.Get(), AgentWorldTravelUpdaterDetour);
        AgentWorldTravelUpdateHook.Enable();

        UpdateWorldTravelDataHook ??= DService.Hook.HookFromSignature<UpdateWorldTravelDataDelegate>(UpdateWorldTravelDataSig.Get(), UpdateWorldTravelDataDetour);
        UpdateWorldTravelDataHook.Enable();

        ContentFinderQueuePositionDataHook ??=
            DService.Hook.HookFromSignature<ContentFinderQueuePositionDataDelegate>(
            ContentFinderQueuePositionDataSig.Get(), ContentFinderQueuePositionDataDetour);
        ContentFinderQueuePositionDataHook.Enable();
    }

    private static double CalculateWaitTime(int position)
    {
        if (position <= 0)
        {
            return 0;
        }

        var fullGroups = (position - 1) / 4;

        var fullGroupTime = fullGroups * 10f;

        var remainingPeople = (position - 1) % 4;

        var remainingTime = remainingPeople > 0 ? 10f : 0;
        var totalWaitTime = fullGroupTime + remainingTime;

        return totalWaitTime;
    }

    private unsafe void UpdateWorldTravelDataDetour(nint a1, nint a2)
    {
        var type = *(byte*)(a2 + 16);
        //DService.Log.Debug($"type: {type}");

        if (type == 1)
        {
            var position = *(int*)(a2 + 20);
            _eta = DateTime.Now.AddSeconds(CalculateWaitTime(position));
        }

        UpdateWorldTravelDataHook.Original(a1, a2);
    }

    private unsafe bool AgentWorldTravelUpdaterDetour(nint a1, nint a2, nint a3, bool a4)
    {
        var agentData = ((delegate* unmanaged<nint, AgentId, nint>)GetAgentDataAddress)(a1, AgentId.WorldTravel);
        if (agentData == nint.Zero || !(*(bool*)(agentData + 0x120)))
        {
            return AgentWorldTravelUpdateHook.Original(a1, a2, a3, a4);
        }

        var result = AgentWorldTravelUpdateHook.Original(a1, a2, a3, a4);
        if (!result)
        {
            return false;
        }

        var idx = 5;
        if (*(int*)(*(nint*)(a2 + 32) + 20) > 0)
        {
            idx = 6;
        }

        var position = *(byte*)(agentData + 0x12c);
        var positionStr = $"当前顺序：{position}";
        fixed (byte* strPtr = Encoding.UTF8.GetBytes(positionStr))
        {
            ((delegate* unmanaged<nint, int, byte*, int, int, void>)SetStringAddress)(a3, idx, strPtr, 0, 1);
        }

        var queueTime = TimeSpan.FromSeconds(*(int*)(agentData + 0x128));
        var str = $@"已等待时间：{queueTime:mm\:ss} / 预计到达时间还剩：{_eta - DateTime.Now:mm\:ss}";
        fixed (byte* strBytesPtr = Encoding.UTF8.GetBytes(str))
        {
            ((delegate* unmanaged<nint, int, byte*, int, int, void>)SetStringAddress)(a3, idx + 1, strBytesPtr, 0, 1);
        }

        return true;
    }

    private byte ContentFinderQueuePositionDataDetour(nint a1, uint a2, nint a3)
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
}
