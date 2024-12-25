using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.GeneratedSheets;
using Vector3 = System.Numerics.Vector3;

namespace DailyRoutines.Modules;

public class FixedPetPosition : DailyModuleBase
{
    private static Config ModuleConfig = null!;
    private DateTime BattleStartTime = DateTime.MinValue;
    private static string ContentSearchInput = string.Empty;
    private bool IsPicking = false;
    private (uint territoryKey, int index)? currentPickingRow = null;

    public override ModuleInfo Info => new()
    {
        Author = ["Wotou"],
        Title = GetLoc("FixedPetPositionTitle"),
        Description = GetLoc("FixedPetPositionDescription"),
        Category = ModuleCategories.Combat
    };

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };

        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
        DService.DutyState.DutyRecommenced += OnDutyRecommenced;
        DService.Condition.ConditionChange += OnConditionChanged;

        TaskHelper.Enqueue(SchedulePetMovements);
        CleanEmptyLists();
    }

    public override void ConfigUI()
    {
        ImGui.Spacing();

        var tableWidth = (ImGui.GetContentRegionAvail() - ScaledVector2(100f)) with { Y = 0 };

        var addColText = "Add"; // or "添加"
        var addColWidth = ScaledVector2(20f).X;

        var regionIdText = GetLoc("Zone"); // “区域”
        var regionIdWidth = (tableWidth * 0.28f).X;

        var remarkText = GetLoc("Note"); // “备注”
        var remarkWidth = (tableWidth * 0.15f).X;

        var delayText = GetLoc("FixedPetPosition-Delay"); // “进入战斗后生效(秒)”
        var delayWidth = (tableWidth * 0.1f).X;

        var posText = GetLoc("Position"); // “坐标”
        var posWidth = (tableWidth * 0.2f).X;

        // 绘制表格
        using var table = ImRaii.Table("PositionSchedulesTable", 6,
                                       ImGuiTableFlags.Borders
                                       | ImGuiTableFlags.RowBg,
                                       tableWidth);
        if (!table)
            return;

        // 设置每列宽度
        ImGui.TableSetupColumn(addColText, ImGuiTableColumnFlags.WidthFixed, addColWidth);
        ImGui.TableSetupColumn(regionIdText, ImGuiTableColumnFlags.WidthFixed, regionIdWidth);
        ImGui.TableSetupColumn(remarkText, ImGuiTableColumnFlags.WidthFixed, remarkWidth);
        ImGui.TableSetupColumn(delayText, ImGuiTableColumnFlags.WidthFixed, delayWidth);
        ImGui.TableSetupColumn(posText, ImGuiTableColumnFlags.WidthFixed, posWidth);
        ImGui.TableSetupColumn(GetLoc("Operation"), ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        // (1) 第一列：添加
        ImGui.TableNextColumn();
        if (ImGuiOm.ButtonIconSelectable("AddNewPreset", FontAwesomeIcon.Plus))
        {
            // 添加新 TerritoryId (默认等于 0)
            if (!ModuleConfig.PositionSchedules.ContainsKey(0))
                ModuleConfig.PositionSchedules[0] = new List<PositionSchedule>();

            ModuleConfig.PositionSchedules[0].Add(new PositionSchedule
            {
                Enabled = true,
                ZoneID = 0,
                TimeInSeconds = 0,
                PosX = 0f,
                PosZ = 0f
            });
            SaveConfig(ModuleConfig);

            TaskHelper.Abort();
            TaskHelper.Enqueue(SchedulePetMovements);
        }

        // (2) 其余表头
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(regionIdText);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(remarkText);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(delayText);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(posText);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(GetLoc("Operation"));

        foreach (var (territoryKey, scheduleList) in ModuleConfig.PositionSchedules.ToArray())
        {
            // 如果当前列表为空，就跳过
            if (scheduleList.Count == 0)
                continue;

            for (var i = 0; i < scheduleList.Count; i++)
            {
                var schedule = scheduleList[i];
                ImGui.TableNextRow();

                // 0) 启用 CheckBox
                ImGui.TableNextColumn();
                var enabled = schedule.Enabled;
                if (ImGui.Checkbox($"##Enable_{territoryKey}_{i}", ref enabled))
                {
                    if (enabled != schedule.Enabled)
                    {
                        schedule.Enabled = enabled;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 1) TerritoryId (区域ID)
                ImGui.TableNextColumn();
                var editingTerritoryId = schedule.ZoneID;
                var filteredContents = PresetData.Contents
                                                 .Where(pair => pair.Value.ContentMemberType.Row == 3) // 只要 8人本
                                                 .ToDictionary(pair => pair.Key, pair => pair.Value);

                ImGui.SetNextItemWidth(regionIdWidth);
                using (ImRaii.PushId($"SingleSelectCombo_Id_{territoryKey}_{i}"))
                {
                    if (SingleSelectCombo(filteredContents, ref editingTerritoryId, ref ContentSearchInput,
                                          x => $"{x.Name}",
                                          new[] { (GetLoc("Name"), ImGuiTableColumnFlags.None, 0f) },
                                          [
                                              x => () => { ImGui.Text($"{x.Name}"); }
                                          ],
                                          [x => x.Name.RawString, x => x.RowId.ToString()]))
                    {
                        // 只有在输入完毕后再写回
                        editingTerritoryId = Math.Max(0, editingTerritoryId);
                        //NotifyHelper.Chat($"editingTerritoryId: {editingTerritoryId}");
                        //NotifyHelper.Chat($"schedule.ZoneID: {schedule.ZoneID}");


                        if (editingTerritoryId != schedule.ZoneID)
                        {
                            // 如果 TerritoryId 改了，需要把这条 schedule 移动到新的 key
                            // 1. 从旧列表移除
                            scheduleList.RemoveAt(i);
                            if (scheduleList.Count == 0)
                                ModuleConfig.PositionSchedules.Remove(territoryKey);
                            i--;

                            // 2. 放到新 key 下 (如果不存在就新建)
                            if (!ModuleConfig.PositionSchedules.ContainsKey((uint)editingTerritoryId))
                                ModuleConfig.PositionSchedules[editingTerritoryId] = new List<PositionSchedule>();

                            ModuleConfig.PositionSchedules[editingTerritoryId].Add(new PositionSchedule
                            {
                                Enabled = schedule.Enabled,
                                ZoneID = editingTerritoryId,
                                TimeInSeconds = schedule.TimeInSeconds,
                                PosX = schedule.PosX,
                                PosZ = schedule.PosZ
                            });

                            SaveConfig(ModuleConfig);
                            TaskHelper.Abort();
                            TaskHelper.Enqueue(SchedulePetMovements);
                            continue;
                        }
                    }
                }

                // 2) 备注
                ImGui.TableNextColumn();
                {
                    var remark = schedule.Remark;
                    ImGui.SetNextItemWidth(remarkWidth);
                    ImGui.InputText($"##Remark_{territoryKey}_{i}", ref remark, 256);
                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        if (remark != schedule.Remark)
                        {
                            schedule.Remark = remark;
                            SaveConfig(ModuleConfig);
                            TaskHelper.Abort();
                            TaskHelper.Enqueue(SchedulePetMovements);
                        }
                    }
                }

                // 3) TimeInSeconds
                ImGui.TableNextColumn();
                var timeInSeconds = schedule.TimeInSeconds;
                ImGui.SetNextItemWidth(delayWidth);
                ImGui.InputInt($"##Time_{territoryKey}_{i}", ref timeInSeconds, 0, 0);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    timeInSeconds = Math.Max(0, timeInSeconds);
                    if (timeInSeconds != schedule.TimeInSeconds)
                    {
                        schedule.TimeInSeconds = timeInSeconds;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }


                // 4) PosX, PosZ
                ImGui.TableNextColumn();
                var posX = schedule.PosX;
                var posZ = schedule.PosZ;

                ImGui.SetNextItemWidth((posWidth - 70) / 2);
                ImGui.InputFloat($"##PosX_{territoryKey}_{i}", ref posX, 0f, 0f, "%.1f");
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    // 简单判断浮点数变化
                    if (MathF.Abs(posX - schedule.PosX) > 0.0001f)
                    {
                        schedule.PosX = posX;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                ImGui.SameLine();
                ImGui.SetNextItemWidth((posWidth - 70) / 2);
                ImGui.InputFloat($"##PosZ_{territoryKey}_{i}", ref posZ, 0f, 0f, "%.1f");
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (MathF.Abs(posZ - schedule.PosZ) > 0.0001f)
                    {
                        schedule.PosZ = posZ;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                ImGui.SameLine();
                if (ImGuiOm.ButtonIcon($"GetCurrent_{territoryKey}_{i}", FontAwesomeIcon.Crosshairs,
                                       GetLoc("FixedPetPosition-GetCurrent")))
                {
                    var localPlayer = DService.ClientState.LocalPlayer;
                    if (localPlayer != null)
                    {
                        schedule.PosX = localPlayer.Position.X;
                        schedule.PosZ = localPlayer.Position.Z;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 按钮：取鼠标
                ImGui.SameLine();
                if (!IsPicking)
                {
                    if (ImGuiOm.ButtonIcon($"TakeMouse_{territoryKey}_{i}", FontAwesomeIcon.MousePointer,
                                           GetLoc("FixedPetPosition-GetMouse")))
                    {
                        IsPicking = true;
                        currentPickingRow = (territoryKey, i);
                        //NotifyHelper.Chat("请用鼠标指向游戏世界中的目标位置，然后按 Ctrl + Alt 确定目标位置");
                    }
                }
                else
                {
                    if (ImGuiOm.ButtonIcon($"CancelPick_{territoryKey}_{i}", FontAwesomeIcon.Times,
                                           GetLoc("Cancel")))
                    {
                        IsPicking = false;
                        currentPickingRow = null;
                        //NotifyHelper.Chat("已取消选取。");
                    }
                }

                if (IsPicking)
                {
                    if ((ImGui.IsKeyDown(ImGuiKey.LeftAlt) || ImGui.IsKeyDown(ImGuiKey.RightAlt)) &&
                        (ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl)))
                    {
                        Vector2 mousePos = ImGui.GetMousePos();
                        //NotifyHelper.Chat($"鼠标位置：{mousePos}");
                        DService.Gui.ScreenToWorld(mousePos, out var worldPos);
                        if (worldPos != null)
                        {
                            ModuleConfig.PositionSchedules[currentPickingRow!.Value.territoryKey][
                                currentPickingRow!.Value.index].PosX = worldPos.X;
                            ModuleConfig.PositionSchedules[currentPickingRow!.Value.territoryKey][
                                currentPickingRow!.Value.index].PosZ = worldPos.Z;
                            SaveConfig(ModuleConfig);
                            TaskHelper.Abort();
                            TaskHelper.Enqueue(SchedulePetMovements);
                            IsPicking = false;
                            currentPickingRow = null;
                        }
                    }
                }


                // 5) 操作（删除）
                ImGui.TableNextColumn();

                if (ImGuiOm.ButtonIcon($"Delete_{territoryKey}_{i}", FontAwesomeIcon.TrashAlt,
                                       GetLoc("AutoDiscard-DeleteWhenHoldCtrl")))
                {
                    if (ImGui.IsKeyDown(ImGuiKey.LeftCtrl))
                    {
                        scheduleList.RemoveAt(i);
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                ImGui.SameLine();
                if (ImGuiOm.ButtonIcon($"Export_{territoryKey}_{i}", FontAwesomeIcon.FileExport, GetLoc("Export")))
                    ExportToClipboard(schedule);

                ImGui.SameLine();
                if (ImGuiOm.ButtonIcon($"Import_{territoryKey}_{i}", FontAwesomeIcon.FileImport, GetLoc("Import")))
                {
                    // 从剪贴板读取一个“单个”的 schedule 对象
                    var importedSchedule = ImportFromClipboard<PositionSchedule>();
                    if (importedSchedule == null) return;

                    // 根据 schedule.ZoneID 放到你的 Dictionary<uint, List<PositionSchedule>> 里
                    uint zoneId = importedSchedule.ZoneID;
                    if (!ModuleConfig.PositionSchedules.TryGetValue(zoneId, out var thisScheduleList))
                    {
                        thisScheduleList = new List<PositionSchedule>();
                        ModuleConfig.PositionSchedules[zoneId] = thisScheduleList;
                    }

                    // 加进去
                    scheduleList.Add(importedSchedule);

                    // 保存并刷新
                    scheduleList.RemoveAt(i);
                    SaveConfig(ModuleConfig);
                    TaskHelper.Abort();
                    TaskHelper.Enqueue(SchedulePetMovements);
                }
            }
        }
    }

    private void OnTerritoryChanged(ushort zone)
    {
        ResetBattleTimer();
        TaskHelper.Abort();
        TaskHelper.Enqueue(SchedulePetMovements);
    }

    private void OnDutyRecommenced(object? sender, ushort e)
    {
        ResetBattleTimer();
        TaskHelper.Abort();
        TaskHelper.Enqueue(SchedulePetMovements);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (value && flag == ConditionFlag.InCombat)
        {
            ResetBattleTimer();
            StartBattleTimer();
            TaskHelper.Abort();
            TaskHelper.Enqueue(SchedulePetMovements);
        }
    }

    private void StartBattleTimer()
    {
        BattleStartTime = DateTime.Now;
    }

    private void ResetBattleTimer()
    {
        BattleStartTime = DateTime.MinValue;
    }

    private void SchedulePetMovements()
    {
        if (!CheckIsEightPlayerDuty())
            return;

        // 获取当前区域
        var zoneID = DService.ClientState.TerritoryType;
        if (!ModuleConfig.PositionSchedules.TryGetValue(zoneID, out var schedulesForThisDuty))
            return;

        // 只选启用的
        var enabledSchedules = schedulesForThisDuty.Where(x => x.Enabled).ToList();
        var elapsedTimeInSeconds = (DateTime.Now - BattleStartTime).TotalSeconds;

        if (DService.Condition[ConditionFlag.InCombat])
        {
            // 在所有 TimeInSeconds <= 已过战斗时间中，找最大
            var bestSchedule = enabledSchedules
                               .Where(x => x.TimeInSeconds <= elapsedTimeInSeconds)
                               .OrderByDescending(x => x.TimeInSeconds)
                               .FirstOrDefault();

            if (bestSchedule != null) TaskHelper.Enqueue(() => MovePetToLocation(bestSchedule.PosX, bestSchedule.PosZ));
        }
        else
        {
            // 没在战斗，如果有 TimeInSeconds == 0 的，就执行第一条
            var scheduleForZero = enabledSchedules
                .FirstOrDefault(x => x.TimeInSeconds == 0);

            if (scheduleForZero != null)
                TaskHelper.Enqueue(() => MovePetToLocation(scheduleForZero.PosX, scheduleForZero.PosZ));
        }

        // 循环执行
        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(SchedulePetMovements);
    }

    private unsafe void MovePetToLocation(float posX, float posZ)
    {
        if (!CheckIsEightPlayerDuty())
            return;

        var player = DService.ClientState.LocalPlayer;
        if (player == null)
            return;

        var pet = CharacterManager.Instance()->LookupPetByOwnerObject((BattleChara*)player.Address);
        if (pet == null)
            return;

        var groundY = pet->Position.Y;

        var groundCheck = MovementManager.TryDetectGroundDownwards(
            new Vector3(posX, groundY + 5f, posZ),
            out var groundPos);

        var location = new Vector3(posX, groundY, posZ);
        if (groundCheck == true)
            location = groundPos.Point;

        TaskHelper.Enqueue(() =>
                               ExecuteCommandManager.ExecuteCommandComplexLocation(
                                   ExecuteCommandComplexFlag.PetAction, location, 3));
    }

    private bool CheckIsEightPlayerDuty()
    {
        uint territoryId = DService.ClientState.TerritoryType;
        var territory = LuminaCache.GetRow<TerritoryType>(territoryId);
        if (territory == null)
        {
            //NotifyHelper.Chat("无法获取当前地图信息！");
            return false;
        }

        var cfc = territory.ContentFinderCondition.Value;
        if (cfc == null)
        {
            //NotifyHelper.Chat($"当前区域 {territoryId} 不是副本。");
            return false;
        }

        // 3 -> 8 人副本
        return cfc.ContentMemberType.Row == 3;
    }

    // 清理ModuleConfig中List<PositionSchedule>>中的空列表
    private void CleanEmptyLists()
    {
        foreach (var (territoryKey, scheduleList) in ModuleConfig.PositionSchedules.ToArray())
        {
            if (scheduleList.Count == 0)
                ModuleConfig.PositionSchedules.Remove(territoryKey);
        }
    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnTerritoryChanged;
        DService.DutyState.DutyRecommenced -= OnDutyRecommenced;
        DService.Condition.ConditionChange -= OnConditionChanged;
        CleanEmptyLists();
        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        // Key: TerritoryId(区域ID) → Value: 多条调度
        public readonly Dictionary<uint, List<PositionSchedule>> PositionSchedules = new();
    }

    public class PositionSchedule
    {
        public bool Enabled { get; set; } = true;          // 是否启用
        public uint ZoneID { get; set; }                   // 区域 ID
        public string Remark { get; set; } = string.Empty; // 备注
        public int TimeInSeconds { get; set; }             // 战斗开始后的时间点 (秒)
        public float PosX { get; set; }                    // X 坐标
        public float PosZ { get; set; }                    // Z 坐标
    }
}
