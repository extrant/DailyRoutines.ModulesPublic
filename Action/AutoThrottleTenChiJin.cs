using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Collections.Generic;
using DailyRoutines.Infos;
using Dalamud.Hooking;

namespace DailyRoutines.Modules;

public unsafe class AutoThrottleTenChiJin : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("AutoThrottleTenChiJinTitle"),
        Description = GetLoc("AutoThrottleTenChiJinDescription"),
        Category    = ModuleCategories.Action,
    };
    
    private static readonly HashSet<uint> ShinobiActionsStart   = [2259, 2261, 2263];
    private static readonly HashSet<uint> ShinobiActionsProcess = [18805, 18806, 18807];
    
    private static readonly HashSet<uint> UsedShinobiActions = [];

    public override void Init() => GamePacketManager.Register(OnPreSendActionPacket);
    
    private static void OnPreSendActionPacket(ref bool isPrevented, int opcode, ref byte* packet)
    {
        if (opcode != GamePacketOpcodes.UseActionOpcode) return;
        
        var data = (UseActionPacket*)packet;
        if (ShinobiActionsStart.Contains(data->ID))
        {
            UsedShinobiActions.Clear();
            UsedShinobiActions.Add((data->ID % 2259 / 2) + 18805);
        }
        else if (ShinobiActionsProcess.Contains(data->ID))
        {
            if (!UsedShinobiActions.Add(data->ID)) 
                isPrevented = true;
        }
    }

    public override void Uninit() => GamePacketManager.Unregister(OnPreSendActionPacket);
}
