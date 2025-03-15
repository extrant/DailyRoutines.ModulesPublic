using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DailyRoutines.Modules;

public unsafe class BetterBlueSetLoad : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("BetterBlueSetLoadTitle"),
        Description = GetLoc("BetterBlueSetLoadDescription"),
        Category    = ModuleCategories.UIOptimization,
    };
    
    private static readonly CompSig AgentAozNotebookReceiveEventSig =
        new("40 53 55 56 57 41 56 48 81 EC 30 01 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 20 01 00 00 48 8B BC 24 80 01 00 00");
    private static Hook<AgentReceiveEventDelegate>? AgentAozNotebookReceiveEventHook;

    public override void Init()
    {
        AgentAozNotebookReceiveEventHook ??= AgentAozNotebookReceiveEventSig.GetHook<AgentReceiveEventDelegate>(AgentAozNotebookReceiveEventDetour);
        AgentAozNotebookReceiveEventHook.Enable();
    }

    private AtkValue* AgentAozNotebookReceiveEventDetour(
        AgentInterface* agent, AtkValue* returnvalues, AtkValue* values, uint valueCount, ulong eventKind)
    {
        if (!IsAddonAndNodesReady(AOZNotebookPresetList) || AOZNotebookPresetList->AtkValues->UInt != 0)
            return InvokeOriginal();
        if (eventKind != 1 || valueCount != 2)
            return InvokeOriginal();
        
        var index = values[1].UInt;
        if (values[1].Type != ValueType.UInt || index > 4) return InvokeOriginal();
        
        CompareAndApply((int)index);
        CompareAndApply((int)index);

        var setName = AozNoteModule.Instance()->ActiveSets[(int)index].CustomNameString;
        NotificationSuccess(GetLoc("BetterBlueSetLoad-Notification", index + 1) + 
                            (string.IsNullOrWhiteSpace(setName) ? string.Empty : $": {setName}"));

        using var returnValue = new AtkValueArray(false);
        return returnValue;

        AtkValue* InvokeOriginal() => AgentAozNotebookReceiveEventHook.Original(agent, returnvalues, values, valueCount, eventKind);
    }
    
    private static void CompareAndApply(int index)
    {
        if (index > 4) return;

        var blueModule    = AozNoteModule.Instance();
        var actionManager = ActionManager.Instance();

        Span<uint> presetActions = stackalloc uint[24];
        fixed (uint* actions = blueModule->ActiveSets[index].ActiveActions)
        {
            for (var i = 0; i < 24; i++)
            {
                var action = actions[i];
                if (action == 0) continue;

                presetActions[i] = action;
            }
        }

        Span<uint> currentActions = stackalloc uint[24];
        for (var i = 0; i < 24; i++)
        {
            var action = actionManager->GetActiveBlueMageActionInSlot(i);
            if (action == 0) continue;
            currentActions[i] = action;
        }

        Span<uint> finalActions = stackalloc uint[24];
        presetActions.CopyTo(finalActions);

        for (var i = 0; i < 24; i++)
        {
            if (finalActions[i] == 0) continue;
            for (var j = 0; j < 24; j++)
                if (finalActions[i] == currentActions[j])
                {
                    actionManager->SwapBlueMageActionSlots(i, j);
                    finalActions[i] = 0;
                    break;
                }
        }

        for (var i = 0; i < 24; i++)
        {
            var action = finalActions[i];
            if (action == 0) continue;
            actionManager->AssignBlueMageActionToSlot(i, action);
        }

        blueModule->LoadActiveSetHotBars(index);
    }
}
