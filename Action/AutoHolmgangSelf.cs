using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

public unsafe class AutoHolmgangSelf : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoHolmgangSelfTitle"),
        Description = GetLoc("AutoHolmgangSelfDescription"),
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
        if (actionType is ActionType.Action && actionID is 43) 
            targetID = 0xE0000000UL;
    }

    public override void Uninit()
    {
        UseActionManager.Unregister(OnPreUseAction);
    }
}
