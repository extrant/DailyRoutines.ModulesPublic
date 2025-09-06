using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DailyRoutines.Abstracts;
using DailyRoutines.IPC;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Addon;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using ActionKind = FFXIVClientStructs.FFXIV.Client.UI.Agent.ActionKind;

namespace DailyRoutines.ModulesPublic;

public class OptimizedRecipeNote : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("OptimizedRecipeNoteTitle"),
        Description = GetLoc("OptimizedRecipeNoteDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    private static readonly Dictionary<uint, CaculationResult> CaculationResults = [];
    
    private static Hook<AgentReceiveEventDelegate>? AgentRecipeNoteReceiveEventHook;
    
    private static TextButtonNode? RecipeCaculationButton;
    private static TextButtonNode? SwitchJobButton;
    
    private static DalamudLinkPayload? InstallRaphaelLinkPayload;
    private static Task?               InstallRaphaelTask;
    
    protected override unsafe void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 15_000 };
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "RecipeNote", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,    "RecipeNote", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RecipeNote", OnAddon);

        AgentRecipeNoteReceiveEventHook ??= DService.Hook.HookFromAddress<AgentReceiveEventDelegate>(
            GetVFuncByName(AgentRecipeNote.Instance()->VirtualTable, "ReceiveEvent"),
            AgentRecipeNoteReceiveEventDetour);
        AgentRecipeNoteReceiveEventHook.Enable();
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);
        OnAddon(AddonEvent.PreFinalize, null);
        
        AddonActionsPreview.Addon?.Dispose();
        AddonActionsPreview.Addon = null;
        
        CaculationResults.Values.ForEach(x =>
        {
            LinkPayloadManager.Unregister(x.CopyLinkPayload.CommandId);
            LinkPayloadManager.Unregister(x.PreviewLinkPayload.CommandId);
        });
        CaculationResults.Clear();

        if (InstallRaphaelLinkPayload != null)
            LinkPayloadManager.Unregister(InstallRaphaelLinkPayload.CommandId);
        InstallRaphaelTask = null;
    }
    
    private unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PreFinalize:
                Service.AddonController.DetachNode(RecipeCaculationButton);
                RecipeCaculationButton = null;
                
                Service.AddonController.DetachNode(SwitchJobButton);
                SwitchJobButton = null;
                break;
            case AddonEvent.PostSetup:
                if (AddonActionsPreview.Addon?.Nodes is not { Count: > 0 } nodes) return;
                    nodes.ForEach(x => x.Alpha = 1);
                    break;
            case AddonEvent.PostDraw:
                if (InfosOm.RecipeNote == null) return;

                if (RecipeCaculationButton == null)
                {
                    RecipeCaculationButton = new()
                    {
                        Position  = new(228, 490),
                        Size      = new(140, 32),
                        Label     = GetLoc("OptimizedRecipeNote-Button-CaculateRecipe"),
                    };
                    RecipeCaculationButton.OnClick = () =>
                    {
                        if (!IPCManager.IsIPCAvailable<RaphaelIPC>())
                        {
                            InstallRaphaelLinkPayload ??= LinkPayloadManager.Register(OnClickInstallRaphaelPayload, out _);
                            
                            var message = new SeStringBuilder().AddIcon(BitmapFontIcon.Warning)
                                                               .AddText($" {GetLoc("OptimizedRecipeNote-Message-InstallRapheal")}")
                                                               .Add(NewLinePayload.Payload)
                                                               .AddText($"{GetLoc("Operation")}: ")
                                                               .Add(RawPayload.LinkTerminator)
                                                               .Add(InstallRaphaelLinkPayload)
                                                               .AddText("[")
                                                               .AddUiForeground(35)
                                                               .AddText($"{GetLoc("Enable")} / {GetLoc("Install")}")
                                                               .AddUiForegroundOff()
                                                               .AddText("]")
                                                               .Add(RawPayload.LinkTerminator)
                                                               .Build();
                            Chat(message);
                            return;
                        }

                        var recipeID = RaphaelIPC.GetCurrentRecipeID();
                        if (recipeID == 0 || !LuminaGetter.TryGetRow(recipeID, out Recipe recipe)) return;

                        // 职业不对
                        if (recipe.CraftType.RowId != LocalPlayerState.ClassJob - 8) return;

                        // TODO: FFCS 7.3
                        var craftPoint    = PlayerState.Instance()->Attributes[11];
                        var craftsmanship = PlayerState.Instance()->Attributes[70];
                        var control       = PlayerState.Instance()->Attributes[71];
                        var id            = RaphaelIPC.StartCalculation();
                        RecipeCaculationButton.IsEnabled = false;
                        TaskHelper.Enqueue(() =>
                        {
                            var response = RaphaelIPC.GetCalculationStatus(id);
                            switch (response.Status)
                            {
                                case RaphaelCalculationStatus.Success:
                                    RecipeCaculationButton.IsEnabled = true;
                                    
                                    var copyLinkPayload    = LinkPayloadManager.Register(OnClickCopyPayload,    out _);
                                    var previewLinkPayload = LinkPayloadManager.Register(OnClickPreviewPayload, out _);
                                    CaculationResults[id] = new(response.Actions,
                                                                recipeID,
                                                                craftPoint,
                                                                craftsmanship,
                                                                control,
                                                                copyLinkPayload,
                                                                previewLinkPayload);
                                    PrintActionsMessage(CaculationResults[id]);
                                    return true;
                                case RaphaelCalculationStatus.Failed:
                                    RecipeCaculationButton.IsEnabled = true;
                                    TaskHelper.Abort();
                                    return true;
                                default:
                                    return false;
                            }
                        }, "请求技能数据");
                    };
                    
                    Service.AddonController.AttachNode(RecipeCaculationButton, InfosOm.RecipeNote->GetNodeById(57));
                }

                if (SwitchJobButton == null)
                {
                    SwitchJobButton = new()
                    {
                        Position  = new(228, 490),
                        Size      = new(140, 32),
                        Label     = GetLoc("OptimizedRecipeNote-Button-SwitchJob"),
                    };
                    SwitchJobButton.OnClick = () =>
                    {
                        if (!IPCManager.IsIPCAvailable<RaphaelIPC>())
                        {
                            InstallRaphaelLinkPayload ??= LinkPayloadManager.Register(OnClickInstallRaphaelPayload, out _);
                            
                            var message = new SeStringBuilder().AddIcon(BitmapFontIcon.Warning)
                                                               .AddText($" {GetLoc("OptimizedRecipeNote-Message-InstallRapheal")}")
                                                               .Add(NewLinePayload.Payload)
                                                               .AddText($"{GetLoc("Operation")}: ")
                                                               .Add(RawPayload.LinkTerminator)
                                                               .Add(InstallRaphaelLinkPayload)
                                                               .AddText("[")
                                                               .AddUiForeground(35)
                                                               .AddText($"{GetLoc("Enable")} / {GetLoc("Install")}")
                                                               .AddUiForegroundOff()
                                                               .AddText("]")
                                                               .Add(RawPayload.LinkTerminator)
                                                               .Build();
                            Chat(message);
                            return;
                        }

                        var recipeID = RaphaelIPC.GetCurrentRecipeID();
                        if (recipeID == 0 || !LuminaGetter.TryGetRow(recipeID, out Recipe recipe)) return;
                        // 职业对了
                        if (recipe.CraftType.RowId == LocalPlayerState.ClassJob - 8) return;

                        // 能直接切换
                        if (!DService.Condition[ConditionFlag.PreparingToCraft])
                        {
                            LocalPlayerState.SwitchGearset(recipe.CraftType.RowId + 8);
                            return;
                        }
                        
                        TaskHelper.Enqueue(() => AgentRecipeNote.Instance()->Hide());
                        TaskHelper.Enqueue(() => !DService.Condition[ConditionFlag.PreparingToCraft]);
                        TaskHelper.Enqueue(() => LocalPlayerState.SwitchGearset(recipe.CraftType.RowId + 8));
                        TaskHelper.Enqueue(() => AgentRecipeNote.Instance()->OpenRecipeByRecipeId(recipeID));
                    };
                    
                    Service.AddonController.AttachNode(SwitchJobButton, InfosOm.RecipeNote->GetNodeById(57));
                }

                if (Throttler.Throttle("OptimizedRecipeNote-UpdateAddon", 1000))
                    UpdateRecipeAddonButton();
                
                break;
        }
    }

    private static void OnClickInstallRaphaelPayload(uint id, SeString _)
    {
        if (DService.PI.InstalledPlugins.Any(x => x.InternalName == "Raphael.Dalamud"))
        {
            ChatHelper.SendMessage("/xlenableplugin Raphael.Dalamud");
            return;
        }
        
        if (InstallRaphaelTask != null) return;

        InstallRaphaelTask = DService.Framework
                                     .RunOnTick(async () => await AddPlugin("https://raw.githubusercontent.com/AtmoOmen/DalamudPlugins/main/pluginmaster.json",
                                                                            "Raphael.Dalamud"))
                                     .ContinueWith(_ => InstallRaphaelTask = null);
    }

    private void OnClickPreviewPayload(uint id, SeString _)
    {
        if (CaculationResults.FirstOrDefault(x => x.Value.PreviewLinkPayload.CommandId == id) is not { Value.RecipeID: > 0 } result) return;
        
        AddonActionsPreview.OpenWithActions(TaskHelper, result.Value);
    }

    private static void OnClickCopyPayload(uint id, SeString _)
    {
        if (CaculationResults.FirstOrDefault(x => x.Value.CopyLinkPayload.CommandId == id) is not { Value.RecipeID: > 0 } result) return;

        var builder = new StringBuilder();
        foreach (var action in result.Value.Actions)
            builder.AppendLine($"/ac {LuminaWrapper.GetActionName(action)} <wait.3>");
        ImGui.SetClipboardText(builder.ToString());
        
        NotificationSuccess($"{GetLoc("CopiedToClipboard")}");
    }
    
    private static unsafe AtkValue* AgentRecipeNoteReceiveEventDetour(
        AgentInterface* agent,
        AtkValue*       returnValues,
        AtkValue*       values,
        uint            valueCount,
        ulong           eventKind)
    {
        var orig = AgentRecipeNoteReceiveEventHook.Original(agent, returnValues, values, valueCount, eventKind);
        DService.Framework.RunOnTick(UpdateRecipeAddonButton, TimeSpan.FromMilliseconds(100));
        return orig;
    }

    private static unsafe void UpdateRecipeAddonButton()
    {
        if (InfosOm.RecipeNote == null || RecipeCaculationButton == null) return;
        
        if (!IPCManager.IsIPCAvailable<RaphaelIPC>()) return;
                    
        var recipeID = RaphaelIPC.GetCurrentRecipeID();
        if (recipeID == 0 || !LuminaGetter.TryGetRow(recipeID, out Recipe recipe))
        {
            RecipeCaculationButton.IsVisible = false;
            SwitchJobButton.IsVisible        = false;
            return;
        }

        if (recipe.CraftType.RowId != LocalPlayerState.ClassJob - 8)
        {
            RecipeCaculationButton.IsVisible = false;
            SwitchJobButton.IsVisible        = true;
        }
        else
        {
            RecipeCaculationButton.IsVisible = true;
            SwitchJobButton.IsVisible        = false;
        }
    }

    private static void PrintActionsMessage(CaculationResult result)
    {
        var builder = new SeStringBuilder();
        builder.AddText($"{GetLoc("OptimizedRecipeNote-Message-CaculationResult")}")
               .Add(NewLinePayload.Payload)
               .AddText($"{GetLoc("Recipe")}: ")
               .AddItemLink(result.GetRecipe().ItemResult.RowId)
               .AddText(" (")
               .AddIcon(result.GetJob().ToBitmapFontIcon())
               .AddText(result.GetJob().Name.ExtractText())
               .AddText(")")
               .Add(NewLinePayload.Payload)
               .AddText($"{GetLoc("Step")}: ")
               .AddText($"{GetLoc("OptimizedRecipeNote-Message-StepsInfo", result.Actions.Count, result.Actions.Count * 3)}")
               .Add(NewLinePayload.Payload)
               .AddText($"{GetLoc("Operation")}: ")
               .Add(RawPayload.LinkTerminator)
               .Add(result.CopyLinkPayload)
               .AddText("[")
               .AddUiForeground(35)
               .AddText($"{GetLoc("Copy")}")
               .AddUiForegroundOff()
               .AddText("]")
               .Add(RawPayload.LinkTerminator)
               .AddText(" / ")
               .Add(RawPayload.LinkTerminator)
               .Add(result.PreviewLinkPayload)
               .AddText("[")
               .AddUiForeground(35)
               .AddText($"{GetLoc("Preview")}")
               .AddUiForegroundOff()
               .AddText("]")
               .Add(RawPayload.LinkTerminator);
        Chat(builder.Build());
    }

    private class AddonActionsPreview(TaskHelper taskHelper, CaculationResult result) : NativeAddon
    {
        public static AddonActionsPreview? Addon  { get; set; }
        public        CaculationResult     Result { get; private set; } = result;
        public        List<DragDropNode>   Nodes  { get; set; }         = [];
        
        public WeakReference<TaskHelper> TaskHelper { get; private set; } = new(taskHelper);
        
        public TextButtonNode ExecuteButton { get; private set; }

        private static Task? OpenAddonTask;
        
        public static void OpenWithActions(TaskHelper taskHelper, CaculationResult result)
        {
            if (OpenAddonTask != null) return;
            
            var isAddonExisted = Addon?.IsOpen ?? false;
            if (Addon != null)
            {
                Addon.Dispose();
                Addon = null;
            }

            OpenAddonTask = DService.Framework.RunOnTick(() =>
            {
                var rowCount = MathF.Ceiling(result.Actions.Count / 10f);
                Addon ??= new(taskHelper, result)
                {
                    InternalName          = "DRAutoCaculateRecipe",
                    Title                 = $"{GetLoc("OptimizedRecipeNote-AddonTitle")}",
                    Subtitle              = $"{GetLoc("OptimizedRecipeNote-Message-StepsInfo", result.Actions.Count, result.Actions.Count * 3)}",
                    Size                  = new(500f, 150f + (50f * (rowCount - 1))),
                    Position              = new(800f, 350f),
                    NativeController      = Service.AddonController,
                    RememberClosePosition = true,
                };
                Addon.Open();
            }, TimeSpan.FromMilliseconds(isAddonExisted ? 500 : 0)).ContinueWith(_ => OpenAddonTask = null);
        }
        
        protected override unsafe void OnSetup(AtkUnitBase* addon)
        {
            if (Result.Actions.Count == 0) return;

            var statsRow = new HorizontalListNode()
            {
                IsVisible = true,
                Position  = new(12, 40),
                Size      = new(0, 44)
            };

            var jobTextNode = new TextNode
            {
                IsVisible = true,
                TextFlags = TextFlags.AutoAdjustNodeSize,
                Text = new SeStringBuilder()
                       .AddText($"{LuminaWrapper.GetAddonText(294)}: ")
                       .AddIcon(Result.GetJob().ToBitmapFontIcon())
                       .AddText(Result.GetJob().Name.ExtractText())
                       .Build()
            };
            jobTextNode.Size =  jobTextNode.GetTextDrawSize($"{jobTextNode.Text}123");
            statsRow.Width   += jobTextNode.Width;
            statsRow.AddNode(jobTextNode);

            var craftmanshipTextNode = new TextNode
            {
                IsVisible = true,
                TextFlags = TextFlags.AutoAdjustNodeSize,
                Text      = $"{LuminaWrapper.GetAddonText(3261)}: {Result.Craftmanship}"
            };
            statsRow.Width += craftmanshipTextNode.Width;
            statsRow.AddNode(craftmanshipTextNode);
            
            statsRow.Width += 4;
            statsRow.AddDummy(4);
            
            var controlTextNode = new TextNode
            {
                IsVisible = true,
                TextFlags = TextFlags.AutoAdjustNodeSize,
                Text      = $"{LuminaWrapper.GetAddonText(3262)}: {Result.Control}"
            };
            statsRow.Width       += controlTextNode.Width;
            statsRow.AddNode(controlTextNode);
            
            statsRow.Width += 4;
            statsRow.AddDummy(4);
            
            var craftPointTextNode = new TextNode
            {
                IsVisible = true,
                TextFlags = TextFlags.AutoAdjustNodeSize,
                Text      = $"{LuminaWrapper.GetAddonText(3223)}: {Result.CraftPoint}"
            };
            statsRow.Width          += craftPointTextNode.Width;
            statsRow.AddNode(craftPointTextNode);
            
            AttachNode(statsRow);
            
            var operationRow = new HorizontalFlexNode
            {
                IsVisible = true,
                Position  = new(8, 60),
                Size      = new(0, 44)
            };

            ExecuteButton = new TextButtonNode
            {
                IsVisible = true,
                Size      = new(100, 24),
                Label     = GetLoc("Execute"),
                OnClick = () =>
                {
                    if (Synthesis == null) return;
                    if (Result.Actions is not { Count: > 0 } actions) return;
                    if (!TaskHelper.TryGetTarget(out var taskHelper)) return;

                    for (var index = 0; index < actions.Count; index++)
                    {
                        var x = actions[index];
                        var i = index;
                        taskHelper.Enqueue(() =>
                        {
                            if (DService.Condition[ConditionFlag.ExecutingCraftingAction]) return true;

                            ChatHelper.SendMessage($"/ac {LuminaWrapper.GetActionName(x)}");
                            return false;
                        });
                        taskHelper.Enqueue(() => Nodes[i].Alpha = 0.2f);
                        taskHelper.Enqueue(() => !DService.Condition[ConditionFlag.ExecutingCraftingAction]);
                    }
                }
            };
            operationRow.Width += ExecuteButton.Width;
            operationRow.AddNode(ExecuteButton);
            
            operationRow.Width += 4;
            operationRow.AddDummy(4);
            
            var macroButtonCount = (int)Math.Ceiling(Result.Actions.Count / 15.0);
            for (var i = 0; i < macroButtonCount; i++)
            {
                var macroIndex = i;
                var copyMacroButton = new TextButtonNode
                {
                    IsVisible = true,
                    Size      = new(120, 24),
                    Label     = GetLoc("OptimizedRecipeNote-Button-CopyMacro", macroIndex + 1),
                    OnClick = () =>
                    {
                        var startIndex = macroIndex * 15;
                        var endIndex   = Math.Min(startIndex + 15, Result.Actions.Count);
                        var actionsForMacro = Result.Actions.Skip(startIndex).Take(endIndex - startIndex);
                        
                        var builder = new StringBuilder();
                        foreach (var action in actionsForMacro)
                            builder.AppendLine($"/ac {LuminaWrapper.GetActionName(action)} <wait.3>");
                        ImGui.SetClipboardText(builder.ToString());
                        
                        NotificationSuccess($"{GetLoc("CopiedToClipboard")}");
                    }
                };
                operationRow.Width += copyMacroButton.Width;
                operationRow.AddNode(copyMacroButton);
                
                operationRow.Width += 4;
                operationRow.AddDummy(4);
            }
            
            AttachNode(operationRow);
            
            var container = new VerticalListNode
            {
                IsVisible = true,
                Position  = new(12, 88),
                Size      = new(44)
            };
            
            var currentRow = new HorizontalFlexNode
            {
                IsVisible = true,
                Size      = new(0, 44)
            };
            
            var itemsInCurrentRow = 0;
            for (var index = 0; index < Result.Actions.Count; index++)
            {
                var actionID = Result.Actions[index];
                var iconID   = LuminaWrapper.GetActionIconID(actionID);
                if (iconID == 0) continue;

                if (itemsInCurrentRow >= 10)
                {
                    container.AddNode(currentRow);
                    container.AddDummy(4f);

                    currentRow = new HorizontalFlexNode
                    {
                        IsVisible = true,
                        Size      = new(0, 44)
                    };
                    itemsInCurrentRow = 0;
                }

                var dragDropNode = new DragDropNode
                {
                    Size         = new(44f),
                    IsVisible    = true,
                    IconId       = iconID,
                    AcceptedType = DragDropType.Nothing,
                    IsDraggable  = true,
                    IsClickable  = true,
                    Payload = new()
                    {
                        Type = actionID > 10_0000 ? DragDropType.CraftingAction : DragDropType.Action,
                        Int2 = (int)actionID,
                    },
                    OnRollOver = (node, _) =>
                        node.ShowTooltip(AtkTooltipManager.AtkTooltipType.Action, actionID > 10_0000 ? ActionKind.CraftingAction : ActionKind.Action),
                    OnRollOut = (node, _) => node.HideTooltip(),
                };
                dragDropNode.OnClicked = (_, _) =>
                {
                    if (DService.Condition[ConditionFlag.ExecutingCraftingAction] ||
                        (TaskHelper.TryGetTarget(out var taskHelper) && taskHelper.IsBusy))
                        return;

                    if (Synthesis != null)
                        dragDropNode.Alpha = 0.2f;
                    ChatHelper.SendMessage($"/ac {LuminaWrapper.GetActionName(actionID)}");
                };
                Nodes.Add(dragDropNode);

                var actionIndexNode = new TextNode
                {
                    IsVisible        = true,
                    Position         = new(-4),
                    Text             = $"{index + 1}",
                    FontType         = FontType.MiedingerMed,
                    TextFlags        = TextFlags.Edge,
                    TextOutlineColor = KnownColor.OrangeRed.Vector()
                };
                Service.AddonController.AttachNode(actionIndexNode, dragDropNode);

                currentRow.AddNode(dragDropNode);
                currentRow.AddDummy(4);
                currentRow.Width += dragDropNode.Size.X + 4;

                itemsInCurrentRow++;
            }

            if (itemsInCurrentRow > 0)
                container.AddNode(currentRow);
            
            AttachNode(container);
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            if (ExecuteButton != null && TaskHelper.TryGetTarget(out var taskHelper)) 
                ExecuteButton.IsEnabled = Synthesis != null && !taskHelper.IsBusy;
        }
    }

    private record CaculationResult(
        List<uint>         Actions,
        uint               RecipeID,
        int                CraftPoint,
        int                Craftmanship,
        int                Control,
        DalamudLinkPayload CopyLinkPayload,
        DalamudLinkPayload PreviewLinkPayload)
    {
        public Recipe GetRecipe() =>
            LuminaGetter.GetRow<Recipe>(RecipeID).GetValueOrDefault();

        public ClassJob GetJob() =>
            LuminaGetter.GetRow<ClassJob>(GetRecipe().CraftType.RowId + 8).GetValueOrDefault();
    }
}
