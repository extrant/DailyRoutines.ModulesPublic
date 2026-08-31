using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic.Interface;

public unsafe partial class BetterMarketBoard
{
    private static bool IsAbleToSearchMarket() =>
        IsAbleToSearchLocalMarket()              &&
        !ICondition.Instance().IsOccupiedInEvent &&
        !ItemSearchResult->IsAddonAndNodesReady();

    private static bool IsAbleToSearchLocalMarket() =>
        GameState.IsLoggedIn &&
        GameState.ContentFinderCondition == 0;
}
