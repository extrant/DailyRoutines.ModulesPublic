using System;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Memory;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public class GrandCompanyExchangeHelper : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = "军票交换所助手",
        Description = "新增 {0} {1} 指令, 允许使用指令快速交换军票交换所内指定数量的目标物品",
        Category    = ModuleCategories.Assist
    };

    public bool IsExchanging => TaskHelper?.IsBusy ?? false;

    private static IPC? ModuleIPC;
    
    public override void Init()
    {
        TaskHelper ??= new();
        ModuleIPC ??= new();
    }

    public unsafe void EnqueueByName(string itemName, int itemCount = -1)
    {
        var grandCompany = PlayerState.Instance()->GrandCompany;
        var gcRank       = PlayerState.Instance()->GetGrandCompanyRank();
        var seals        = InventoryManager.Instance()->GetCompanySeals(grandCompany);
        if (seals == 0) return;

        var result = LuminaCache.GetSub<GCScripShopItem>()
                                .SelectMany(x => x)
                                .Where(x => LuminaCache.GetRow<GCScripShopCategory>(x.RowId)!.Value.GrandCompany.RowId == grandCompany)
                                .Where(x => gcRank >= x.RequiredGrandCompanyRank.RowId)
                                .FirstOrDefault(x => (x.Item.ValueNullable?.Name.ExtractText() ?? string.Empty).Contains(itemName));
        if (result.RowId == 0) return;

        var singleCost             = result.CostGCSeals;
        var availableExchangeCount = (int)(seals / singleCost);
        var exchangeCount = Math.Min(itemCount == -1 ? availableExchangeCount : itemCount, availableExchangeCount);

        var categoryData = LuminaCache.GetRow<GCScripShopCategory>(result.RowId)!.Value;
        var tier         = categoryData.Tier;
        var subCategory  = categoryData.SubCategory;

        if (GrandCompanyExchange->AtkValues[2].UInt != (uint)tier - 1)
        {
            TaskHelper.Enqueue(() =>
            {
                if (GrandCompanyExchange->AtkValues[2].UInt == (uint)tier - 1) return true;
                Callback(GrandCompanyExchange, true, 1, tier - 1);
                return false;
            }, "点击军衔类别");
        }

        TaskHelper.Enqueue(() => Callback(GrandCompanyExchange, true, 2, (int)subCategory), "点击道具类别");
        
        TaskHelper.Enqueue(() =>
        {
            var listNode = (AtkComponentNode*)GrandCompanyExchange->GetNodeById(57);
            if (listNode == null) return;

            for (var i = 0; i < 40; i++)
            {
                try
                {
                    var offset   = 17 + i;
                    var atkValue = GrandCompanyExchange->AtkValues[offset];
                    var name     = MemoryHelper.ReadSeStringNullTerminated((nint)atkValue.String);
                    if (string.IsNullOrWhiteSpace(name.ExtractText()) || name.ExtractText() != result.Item.Value.Name.ExtractText()) continue;

                    var listComponent = (AtkComponentList*)listNode->Component;
                    if (listComponent->GetItemDisabledState(i)) continue;

                    var numericInputNode = (AtkComponentNumericInput*)listComponent->GetItemRenderer(i)->UldManager.SearchNodeById(6)->GetComponent();
                    if (numericInputNode == null) continue;

                    var maxExchangeCount = Math.Min(numericInputNode->Data.Max, exchangeCount);
                    listComponent->SetItemCount(maxExchangeCount);
                    numericInputNode->SetValue(maxExchangeCount);
                    
                    listComponent->SelectItem(i, true);
                    listComponent->DispatchItemEvent(i, AtkEventType.ListItemToggle);
                    break;
                }
                catch
                {
                    // ignored
                }
            }
        }, "点击道具");
    }

    public class IPC : DailyModuleIPCBase
    {
        private const string IsBusyName = "DailyRoutines.Modules.GrandCompanyExchangeHelper.IsBusy";
        private static ICallGateProvider<bool>? IsBusyIPC;
        
        public override void Init()
        {
            IsBusyIPC ??=  DService.PI.GetIpcProvider<bool>(IsBusyName);
            IsBusyIPC.RegisterFunc(() => ModuleManager.GetModule<GrandCompanyExchangeHelper>().IsExchanging);
        }
    }
}
