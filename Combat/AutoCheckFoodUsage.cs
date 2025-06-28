using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;

namespace DailyRoutines.Modules;

public class AutoCheckFoodUsage : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("AutoCheckFoodUsageTitle"),
        Description = GetLoc("AutoCheckFoodUsageDescription"),
        Category = ModuleCategories.Combat,
    };

    private static readonly CompSig                      CountdownInitSig = new("48 89 5C 24 10 57 48 83 EC 40 48 8B DA 48 8B F9 48 8B 49 08");
    public delegate         nint                         CountdownInitDelegate(nint a1, nint a2);
    private static          Hook<CountdownInitDelegate>? CountdownInitHook;

    private static Config ModuleConfig = null!;

    private static uint SelectedItem;
    private static string SelectItemSearch = string.Empty;
    private static bool SelectItemIsHQ = true;
    private static string ZoneSearch = string.Empty;
    private static string ConditionSearch = string.Empty;

    private static Vector2 CheckboxSize = ScaledVector2(20f);
    
    private static readonly DateTime LastFoodUsageTime        = DateTime.MinValue;
    private const           int      FoodUsageCooldownSeconds = 10;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        foreach (var checkPoint in Enum.GetValues<FoodCheckpoint>())
            ModuleConfig.EnabledCheckpoints.TryAdd(checkPoint, false);

        TaskHelper ??= new TaskHelper { TimeLimitMS = 60000 };

        CountdownInitHook ??= DService.Hook.HookFromSignature<CountdownInitDelegate>(CountdownInitSig.Get(), CountdownInitDetour);
        CountdownInitHook.Enable();
        
        DService.ClientState.TerritoryChanged += OnZoneChanged;
        DService.Condition.ConditionChange    += OnConditionChanged;
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(LightSkyBlue, $"{GetLoc("AutoCheckFoodUsage-Checkpoint")}:");

        using (ImRaii.PushIndent())
        {
            ScaledDummy(2f);

            foreach (var checkPoint in Enum.GetValues<FoodCheckpoint>())
            {
                ImGui.SameLine();
                var state = ModuleConfig.EnabledCheckpoints[checkPoint];
                if (ImGui.Checkbox(checkPoint.ToString(), ref state))
                {
                    ModuleConfig.EnabledCheckpoints[checkPoint] = state;
                    SaveConfig(ModuleConfig);
                }
            }

            if (ModuleConfig.EnabledCheckpoints[FoodCheckpoint.条件变更时])
            {
                using (ImRaii.PushIndent())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(LightSkyBlue,
                                      $"{GetLoc("AutoCheckFoodUsage-WhenConditionBegin")}:");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalFontScale);
                    using (var combo = ImRaii.Combo("###ConditionBeginCombo", GetLoc(
                                                        "AutoCheckFoodUsage-SelectedAmount",
                                                        ModuleConfig.ConditionStart.Count),
                                                    ImGuiComboFlags.HeightLarge))
                    {
                        if (combo.Success)
                        {
                            if (ImGui.IsWindowAppearing()) 
                                ConditionSearch = string.Empty;

                            ImGui.SetNextItemWidth(-1f);
                            ImGui.InputTextWithHint("###ConditionBeginSearch", GetLoc("PleaseSearch"),
                                                    ref ConditionSearch, 128);
                            ImGui.Separator();

                            foreach (var conditionFlag in Enum.GetValues<ConditionFlag>())
                            {
                                if (conditionFlag is ConditionFlag.None or ConditionFlag.NormalConditions) continue;
                                var conditionName = conditionFlag.ToString();
                                if (!string.IsNullOrWhiteSpace(ConditionSearch) &&
                                    !conditionName.Contains(ConditionSearch, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                if (ImGui.Selectable($"{conditionName}###{conditionFlag}_Begin",
                                                     ModuleConfig.ConditionStart.Contains(conditionFlag)))
                                {
                                    if (!ModuleConfig.ConditionStart.Remove(conditionFlag))
                                        ModuleConfig.ConditionStart.Add(conditionFlag);
                                    SaveConfig(ModuleConfig);
                                }
                            }
                        }
                    }

                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(LightSkyBlue,
                                      $"{GetLoc("AutoCheckFoodUsage-WhenConditionEnd")}:");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalFontScale);
                    using (var combo = ImRaii.Combo("###ConditionEndCombo", GetLoc(
                                                        "AutoCheckFoodUsage-SelectedAmount",
                                                        ModuleConfig.ConditionEnd.Count), ImGuiComboFlags.HeightLarge))
                    {
                        if (combo.Success)
                        {
                            if (ImGui.IsWindowAppearing()) 
                                ConditionSearch = string.Empty;

                            ImGui.SetNextItemWidth(-1f);
                            ImGui.InputTextWithHint("###ConditionEndSearch", GetLoc("PleaseSearch"),
                                                    ref ConditionSearch, 128);
                            ImGui.Separator();

                            foreach (var conditionFlag in Enum.GetValues<ConditionFlag>())
                            {
                                if (conditionFlag is ConditionFlag.None or ConditionFlag.NormalConditions) continue;

                                var conditionName = conditionFlag.ToString();
                                if (!string.IsNullOrWhiteSpace(ConditionSearch) &&
                                    !conditionName.Contains(ConditionSearch, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (ImGui.Selectable($"{conditionName}###{conditionFlag}_End",
                                                     ModuleConfig.ConditionEnd.Contains(conditionFlag)))
                                {
                                    if (!ModuleConfig.ConditionEnd.Remove(conditionFlag))
                                        ModuleConfig.ConditionEnd.Add(conditionFlag);
                                    SaveConfig(ModuleConfig);
                                }
                            }
                        }
                    }
                }
            }

            ScaledDummy(2f);
        }

        ImGui.TextColored(LightSkyBlue, $"{GetLoc("Settings")}:");

        using (ImRaii.PushIndent())
        {
            ImGui.Dummy(Vector2.One);

            ImGui.SetNextItemWidth(50f * GlobalFontScale);
            ImGui.InputInt(GetLoc("AutoCheckFoodUsage-RefreshThreshold"), ref ModuleConfig.RefreshThreshold, 0, 0);
            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(ModuleConfig);
        
            ImGui.SameLine();
            if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
                SaveConfig(ModuleConfig);

            ImGuiOm.HelpMarker(GetLoc("AutoCheckFoodUsage-RefreshThresholdHelp"));
        }
        
        var       tableSize = (ImGui.GetContentRegionAvail() - ScaledVector2(100f)) with { Y = 0 };
        using var table     = ImRaii.Table("FoodPreset", 4, ImGuiTableFlags.Borders, tableSize);
        if (!table) return;
        ImGui.TableSetupColumn("添加", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
        ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None,       30);
        ImGui.TableSetupColumn("地区", ImGuiTableColumnFlags.None,       30);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None,       30);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableNextColumn();
        if (ImGuiOm.ButtonIconSelectable("AddNewPreset", FontAwesomeIcon.Plus))
            ImGui.OpenPopup("AddNewPresetPopup");

        using (var popup = ImRaii.Popup("AddNewPresetPopup"))
        {
            if (popup)
            {
                ImGui.TextColored(LightSkyBlue,
                                  $"{GetLoc("AutoCheckFoodUsage-AddNewPreset")}:");

                using (ImRaii.PushIndent())
                {
                    ImGui.Dummy(Vector2.One);

                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text($"{GetLoc("Food")}:");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalFontScale);
                    SingleSelectCombo(PresetSheet.Food, ref SelectedItem, ref SelectItemSearch,
                                      x => $"{x.Name.ExtractText()} ({x.RowId})",
                                      [new("物品", ImGuiTableColumnFlags.WidthStretch, 0)],
                                      [
                                          x => () =>
                                          {
                                              var icon = ImageHelper.GetGameIcon(x.Icon, SelectItemIsHQ);

                                              if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, ScaledVector2(20f),
                                                                                  x.Name.ExtractText(), x.RowId == SelectedItem,
                                                                                  ImGuiSelectableFlags.DontClosePopups))
                                                  SelectedItem = SelectedItem == x.RowId ? 0 : x.RowId;
                                          }
                                      ], [x => x.Name.ExtractText(), x => x.RowId.ToString()], true);

                    ImGui.SameLine();
                    ImGui.Checkbox("HQ", ref SelectItemIsHQ);

                    ImGui.SameLine();
                    using (ImRaii.Disabled(SelectedItem == 0))
                    {
                        if (ImGui.Button(GetLoc("Add")))
                        {
                            var preset = new FoodUsagePreset(SelectedItem, SelectItemIsHQ);
                            if (ModuleConfig.Presets.Contains(preset)) return;

                            ModuleConfig.Presets.Add(preset);
                            SaveConfig(ModuleConfig);
                        }
                    }
                }
            }
        }

        ImGui.TableNextColumn();
        ImGui.Text(GetLoc("Food"));

        ImGui.TableNextColumn();
        ImGui.Text(GetLoc("AutoCheckFoodUsage-ZoneRestrictions"));

        ImGui.TableNextColumn();
        ImGui.Text(GetLoc("AutoCheckFoodUsage-JobRestrictions"));

        foreach (var preset in ModuleConfig.Presets.ToArray())
        {
            using var id = ImRaii.PushId($"{preset.ItemID}_{preset.IsHQ}");
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isEnabled = preset.Enabled;
            if (ImGui.Checkbox("", ref isEnabled))
            {
                preset.Enabled = isEnabled;
                SaveConfig(ModuleConfig);
            }

            CheckboxSize = ImGui.GetItemRectSize();

            ImGui.TableNextColumn();
            ImGui.Selectable(
                $"{LuminaGetter.GetRow<Item>(preset.ItemID)!.Value.Name.ExtractText()} {(preset.IsHQ ? "(HQ)" : "")}");

            using (var context = ImRaii.ContextPopupItem("PresetContextMenu"))
            {
                if (context)
                {
                    if (ImGui.MenuItem(
                            $"{GetLoc("AutoCheckFoodUsage-ChangeTo")} {(preset.IsHQ ? "NQ" : "HQ")}"))
                    {
                        preset.IsHQ ^= true;
                        SaveConfig(ModuleConfig);
                    }

                    if (ImGui.MenuItem(GetLoc("Delete")))
                    {
                        ModuleConfig.Presets.Remove(preset);
                        SaveConfig(ModuleConfig);
                    }
                }
            }

            ImGui.TableNextColumn();
            var zones = preset.Zones;
            ImGui.SetNextItemWidth(-1f);
            using (ImRaii.PushId("ZonesSelectCombo"))
            {
                if (MultiSelectCombo(PresetSheet.Zones, ref zones, ref ZoneSearch,
                                     [
                                         new("区域", ImGuiTableColumnFlags.WidthStretch, 0),
                                         new("副本", ImGuiTableColumnFlags.WidthStretch, 0)
                                     ],
                                     [
                                         x => () =>
                                         {
                                             if (ImGui.Selectable($"{x.ExtractPlaceName()}##{x.RowId}",
                                                                  zones.Contains(x.RowId),
                                                                  ImGuiSelectableFlags.SpanAllColumns |
                                                                  ImGuiSelectableFlags.DontClosePopups))
                                             {
                                                 if (!zones.Remove(x.RowId))
                                                 {
                                                     zones.Add(x.RowId);
                                                     SaveConfig(ModuleConfig);
                                                 }
                                             }
                                         },
                                         x => () =>
                                         {
                                             var contentName = x.ContentFinderCondition.Value.Name.ExtractText() ?? "";
                                             ImGui.Text(contentName);
                                         }
                                     ],
                                     [
                                         x => x.ExtractPlaceName(),
                                         x => x.ContentFinderCondition.Value.Name.ExtractText() ?? ""
                                     ], true))
                {
                    preset.Zones = zones;
                    SaveConfig(ModuleConfig);
                }

                ImGuiOm.TooltipHover(GetLoc("AutoCheckFoodUsage-NoZoneSelectHelp"));
            }

            ImGui.TableNextColumn();
            var jobs = preset.ClassJobs;
            ImGui.SetNextItemWidth(-1f);
            if (JobSelectCombo(ref jobs, ref ZoneSearch))
            {
                preset.ClassJobs = jobs;
                SaveConfig(ModuleConfig);
            }
        }
    }

    private bool? EnqueueFoodRefresh()
    {
        if (!IsValidState()) return false;
        if (!IsCooldownElapsed())
        {
            TaskHelper.Abort();
            return true;
        }

        var validPresets = GetValidPresets();
        if (validPresets.Count == 0)
        {
            TaskHelper.Abort();
            return true;
        }

        if (TryGetWellFedParam(out var itemFood, out var remainingTime))
        {
            var existedStatus = validPresets.FirstOrDefault(x => ToFoodRowID(x.ItemID) == itemFood);
            if (existedStatus != null && !ShouldRefreshFood(remainingTime))
            {
                TaskHelper.Abort();
                return true;
            }
        }

        var finalPreset = validPresets.FirstOrDefault();
        TaskHelper.Enqueue(() => TakeFood(finalPreset));
        return true;
    }

    private bool TakeFood(FoodUsagePreset preset) => TakeFood(preset.ItemID, preset.IsHQ);

    private bool TakeFood(uint itemID, bool isHQ)
    {
        if (!Throttler.Throttle("AutoCheckFoodUsage-TakeFood", 1000)) return false;
        if (!IsValidState()) return false;

        TaskHelper.Enqueue(() => TakeFoodInternal(itemID, isHQ));
        return true;
    }

    private bool? TakeFoodInternal(uint itemID, bool isHQ)
    {
        TaskHelper.Abort();
        if (TryGetWellFedParam(out var itemFoodId, out var remainingTime) &&
            itemFoodId                 == ToFoodRowID(itemID)             &&
            remainingTime.TotalMinutes >= 25)
            return true;

        UseActionManager.UseActionLocation(ActionType.Item, isHQ ? itemID  + 100_0000 : itemID, 0xE0000000, default, 0xFFFF);
        
        TaskHelper.DelayNext(3_000);
        TaskHelper.Enqueue(() => CheckFoodState(itemID, isHQ));
        return true;
    }
    
    private bool? CheckFoodState(uint itemID, bool isHQ)
    {
        TaskHelper.Abort();

        if (TryGetWellFedParam(out var itemFoodId, out var remainingTime) &&
            itemFoodId                 == ToFoodRowID(itemID)             &&
            remainingTime.TotalMinutes >= 25)
        {
            Chat(GetSLoc("AutoCheckFoodUsage-NoticeMessage", new SeStringBuilder().AddItemLink(itemID, isHQ)));
            return true;
        }

        TaskHelper.DelayNext(3000);
        TaskHelper.Enqueue(() => TakeFoodInternal(itemID, isHQ));
        return false;
    }

    private static unsafe bool TryGetWellFedParam(out uint itemFoodRowID, out TimeSpan remainingTime)
    {
        itemFoodRowID = 0;
        remainingTime = TimeSpan.Zero;

        if (DService.ObjectTable.LocalPlayer is not { } localPlayer) return false;

        var statusManager = localPlayer.ToStruct()->StatusManager;
        var statusIndex   = statusManager.GetStatusIndex(48);
        if (statusIndex == -1) return false;

        var status = statusManager.Status[statusIndex];
        itemFoodRowID = (uint)status.Param % 10_000;
        remainingTime = TimeSpan.FromSeconds(status.RemainingTime);
        return true;
    }

    private nint CountdownInitDetour(nint a1, nint a2)
    {
        var original = CountdownInitHook.Original(a1, a2);
        
        if (ModuleConfig.EnabledCheckpoints[FoodCheckpoint.倒计时开始时] && !GameMain.IsInPvPArea())
        {
            TaskHelper.Abort();
            TaskHelper.Enqueue(() => EnqueueFoodRefresh());
        }
        
        return original;
    }

    private void OnZoneChanged(ushort zone)
    {
        if (!ModuleConfig.EnabledCheckpoints[FoodCheckpoint.区域切换时] || GameMain.IsInPvPArea()) return;

        TaskHelper.Abort();
        TaskHelper.Enqueue(() => EnqueueFoodRefresh());
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (!ModuleConfig.EnabledCheckpoints[FoodCheckpoint.条件变更时] || GameMain.IsInPvPArea() ||
            ((!value || !ModuleConfig.ConditionStart.Contains(flag)) &&
             (value  || !ModuleConfig.ConditionEnd.Contains(flag))))
            return;
        
        TaskHelper.Abort();
        TaskHelper.Enqueue(() => EnqueueFoodRefresh());
    }
    
    public override void Uninit()
    {
        DService.Condition.ConditionChange -= OnConditionChanged;
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        base.Uninit();
    }
    
    private static unsafe bool IsValidState() =>
        !BetweenAreas                            &&
        !OccupiedInEvent                         &&
        !IsCasting                               &&
        DService.ObjectTable.LocalPlayer != null &&
        IsScreenReady()                          &&
        ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 2) == 0;
    
    private static bool IsCooldownElapsed() 
        => (DateTime.Now - LastFoodUsageTime).TotalSeconds >= FoodUsageCooldownSeconds;
    
    private static uint ToFoodRowID(uint id) 
        => LuminaGetter.GetRow<ItemFood>(LuminaGetter.GetRow<Item>(id)!.Value.ItemAction.Value.Data[1])?.RowId ?? 0;

    private static unsafe List<FoodUsagePreset> GetValidPresets()
    {
        var instance = InventoryManager.Instance();
        var zone     = DService.ClientState.TerritoryType;
        if (instance == null || zone == 0) return [];
        
        return ModuleConfig.Presets
                           .Where(x => x.Enabled                                            && 
                                       (x.Zones.Count == 0 || x.Zones.Contains(zone)) &&
                                       (x.ClassJobs.Count == 0 || 
                                        x.ClassJobs.Contains(DService.ObjectTable.LocalPlayer.ClassJob.RowId)) &&
                                       instance->GetInventoryItemCount(x.ItemID, x.IsHQ) > 0)
                           .OrderByDescending(x => x.Zones.Contains(zone))
                           .ToList();
    }

    private static bool ShouldRefreshFood(TimeSpan remainingTime) =>
        remainingTime <= TimeSpan.FromSeconds(ModuleConfig.RefreshThreshold) &&
        remainingTime <= TimeSpan.FromMinutes(55);

    public class FoodUsagePreset : IEquatable<FoodUsagePreset>
    {
        public uint          ItemID    { get; set; }
        public bool          IsHQ      { get; set; } = true;
        public HashSet<uint> Zones     { get; set; } = [];
        public HashSet<uint> ClassJobs { get; set; } = [];
        public bool          Enabled   { get; set; } = true;

        public FoodUsagePreset() { }
        public FoodUsagePreset(uint itemID) => ItemID = itemID;
        public FoodUsagePreset(uint itemID, bool isHQ) : this(itemID) => IsHQ = isHQ;

        public override bool Equals(object? obj) 
            => Equals(obj as FoodUsagePreset);
        
        public bool Equals(FoodUsagePreset? other) 
            => other != null && ItemID == other.ItemID && IsHQ == other.IsHQ;
        
        public override int GetHashCode() 
            => HashCode.Combine(ItemID, IsHQ);

        public static bool operator ==(FoodUsagePreset? left, FoodUsagePreset? right) =>
            EqualityComparer<FoodUsagePreset>.Default.Equals(left, right);

        public static bool operator !=(FoodUsagePreset? left, FoodUsagePreset? right) 
            => !(left == right);
    }

    private class Config : ModuleConfiguration
    {
        public List<FoodUsagePreset>            Presets            = [];
        public Dictionary<FoodCheckpoint, bool> EnabledCheckpoints = [];
        public HashSet<ConditionFlag>           ConditionStart     = [];
        public HashSet<ConditionFlag>           ConditionEnd       = [];
        public int                              RefreshThreshold   = 600; // 秒
        public bool                             SendChat           = true;
    }

    private enum FoodCheckpoint
    {
        区域切换时,
        倒计时开始时,
        条件变更时,
    }
}
