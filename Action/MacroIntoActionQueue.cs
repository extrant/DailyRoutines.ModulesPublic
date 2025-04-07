using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

public unsafe class MacroIntoActionQueue : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("MacroIntoActionQueueTitle"),
        Description = GetLoc("MacroIntoActionQueueDescription"),
        Category = ModuleCategories.Action,
    };

    public override void Init()
    {
        UseActionManager.Register(OnPreUseAction);
    }

    private static void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType, ref uint actionID, ref ulong targetID, ref uint extraParam,
        ref ActionManager.UseActionMode queueState, ref uint comboRouteID, ref bool* outOptAreaTargeted)
    {
        queueState = ActionManager.UseActionMode.Queue;
        ActionManager.Instance()->QueueType = ActionManager.UseActionMode.Queue;

        // 冲刺
        if (actionType == ActionType.GeneralAction && actionID == 4)
        {
            actionType = ActionType.Action;
            actionID = 3;
            targetID = 0xE000_0000;
        }
    }

    public override void Uninit()
    {
        UseActionManager.Unregister(OnPreUseAction);
    }
}
