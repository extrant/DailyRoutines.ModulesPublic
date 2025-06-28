using DailyRoutines.Abstracts;
using DailyRoutines.Managers;

namespace DailyRoutines.ModulesPublic;

public class AutoRequestNecessity : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRequestNecessityTitle"),
        Description = GetLoc("AutoRequestNecessityDescription"),
        Category    = ModuleCategories.System,
    };
    
    public override void Init()
    {
        TaskHelper ??= new();

        DService.ClientState.Login  += OnLogin;
        DService.ClientState.Logout += OnLogout;
        
        if (!DService.ClientState.IsLoggedIn) return;
        OnLogin();
    }

    private void OnLogin()
    {
        if (TaskHelper.IsBusy) return;
        
        // 投影模板
        EnqueueRequest(ExecuteCommandFlag.RequestGlamourPlates);
        // 肖像列表
        EnqueueRequest(ExecuteCommandFlag.RequestPortraits);
        // 投影台
        EnqueueRequest(ExecuteCommandFlag.RequestPrismBox);
        // 挑战笔记
        EnqueueRequest(ExecuteCommandFlag.RequestContentsNote);
        // 收藏柜
        EnqueueRequest(ExecuteCommandFlag.RequestCabinet);
        // 背包
        EnqueueRequest(ExecuteCommandFlag.InventoryRefresh);
        // 跨界传送
        EnqueueRequest(ExecuteCommandFlag.RequestWorldTravel);
        // 成就
        EnqueueRequest(ExecuteCommandFlag.RequestAllAchievement);
        EnqueueRequest(ExecuteCommandFlag.RequestNearCompletionAchievement);
        // 亲信战友
        EnqueueRequest(ExecuteCommandFlag.RequestTrustedFriend);
        // 剧情辅助器
        EnqueueRequest(ExecuteCommandFlag.RequestDutySupport);
        // 青魔法书
        EnqueueRequest(ExecuteCommandFlag.RequstAOZNotebook);
        // 无人岛
        EnqueueRequest(ExecuteCommandFlag.MJIFavorStateRequest);
        EnqueueRequest(ExecuteCommandFlag.MJIWorkshopRequestItem);
        // 金碟游乐场面板
        EnqueueRequest(ExecuteCommandFlag.RequestGSMahjong);
        EnqueueRequest(ExecuteCommandFlag.RequestGSGeneral);
        EnqueueRequest(ExecuteCommandFlag.RequestGSLordofVerminion);
    }
    
    private void OnLogout(int type, int code) => TaskHelper?.Abort();

    private void EnqueueRequest(ExecuteCommandFlag command, uint param1 = 0, uint param2 = 0, uint param3 = 0, uint param4 = 0)
    {
        TaskHelper.Enqueue(() => ExecuteCommandManager.ExecuteCommand(command, param1, param2, param3, param4), 
                           $"{command}_{param1}_{param2}_{param3}{param4}");
        TaskHelper.DelayNext(10, $"Delay_{command}_{param1}_{param2}_{param3}{param4}");
    }

    public override void Uninit()
    {
        DService.ClientState.Login  -= OnLogin;
        DService.ClientState.Logout -= OnLogout;
        
        base.Uninit();
    }
}
