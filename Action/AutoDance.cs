using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.JobGauge.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

public unsafe class AutoDance : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoDanceTitle"),
        Description = GetLoc("AutoDanceDescription"),
        Category = ModuleCategories.Action,
    };

    public override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeLimitMS = 5000 };

        UseActionManager.Register(OnPostUseAction);
    }

    private void OnPostUseAction(
        bool result, ActionType actionType, uint actionID, ulong targetID, uint extraParam,
        ActionManager.UseActionMode queueState, uint comboRouteID, bool* outOptAreaTargeted)
    {
        if (result && actionType is ActionType.Action && actionID is 15997 or 15998)
        {
            var gauge = DService.JobGauges.Get<DNCGauge>();
            if (gauge.IsDancing) return;
            
            TaskHelper.Enqueue(() => gauge.IsDancing);
            TaskHelper.Enqueue(actionID == 15997 ? DanceStandardStep : DanceTechnicalStep);
        }
    }

    private bool? DanceStandardStep() => DanceStep(false);

    private bool? DanceTechnicalStep() => DanceStep(true);

    private bool? DanceStep(bool isTechnicalStep)
    {
        if (!Throttler.Throttle("AutoDance", 200)) return false;
        var gauge = DService.JobGauges.Get<DNCGauge>();
        if (!gauge.IsDancing)
        {
            TaskHelper.Abort();
            return true;
        }

        var nextStep = gauge.NextStep;
        if (gauge.CompletedSteps < (isTechnicalStep ? 4 : 2))
        {
            if (UseActionManager.UseAction(ActionType.Action, nextStep, 0xE0000000, 0U,
                                                   ActionManager.UseActionMode.Queue))
            {
                TaskHelper.Enqueue(() => DanceStep(isTechnicalStep));
                return true;
            }
        }

        return false;
    }

    public override void Uninit()
    {
        UseActionManager.Unregister(OnPostUseAction);

        TaskHelper?.Abort();
        TaskHelper = null;
    }
}
