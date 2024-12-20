using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;

namespace DailyRoutines.Modules;

public class BanEscToCancelCast : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("BanEscToCancelCastTitle"),
        Description = GetLoc("BanEscToCancelCastDescription"),
        Category = ModuleCategories.Action,
    };

    public override void Init()
    {
        ExecuteCommandManager.Register(OnPreUseCommand);
    }

    private static void OnPreUseCommand(
        ref bool isPrevented, ref ExecuteCommandFlag command, ref int param1, ref int param2, ref int param3,
        ref int param4)
    {
        if (command != ExecuteCommandFlag.CancelCast) return;
        isPrevented = true;
    }

    public override void Uninit()
    {
        ExecuteCommandManager.Unregister(OnPreUseCommand);
    }
}
