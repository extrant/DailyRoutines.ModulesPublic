using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.GeneratedSheets;
using Vector3 = System.Numerics.Vector3;

namespace DailyRoutines.ModuleTemplate;

public class FixedPetPosition : DailyModuleBase
{
    private static Config ModuleConfig = null!;
    private DateTime BattleStartTime = DateTime.MinValue;

    public override ModuleInfo Info => new()
    {
        Author = ["Wotou"],
        Title = GetLoc("FixedPetPositionTitle"),
        Description = GetLoc("FixedPetPositionDescription"),
        Category = ModuleCategories.Action
    };

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };

        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
        DService.DutyState.DutyRecommenced += OnDutyRecommenced;
        DService.Condition.ConditionChange += OnConditionChanged;

        TaskHelper.Enqueue(SchedulePetMovements);
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(LightSkyBlue, GetLoc("FixedPetPosition-Config"));
        ImGui.Spacing();

        // 显示当前区域 ID
        ImGui.Text($"{GetLoc("AutoMarksFinder-CurrentZone")} {DService.ClientState.TerritoryType}");
        ImGui.Spacing();

        var tableWidth = (ImGui.GetContentRegionAvail() - ScaledVector2(100f)) with { Y = 0 };

        var addColText = "Add"; // or "添加"
        var addColWidth = ImGui.CalcTextSize(addColText).X + (2 * ImGui.GetStyle().ItemSpacing.X);

        var regionIdText = GetLoc("Zone") + " ID"; // “区域 ID”
        var regionIdWidth = ImGui.CalcTextSize(regionIdText).X
                            + (ImGui.GetStyle().FramePadding.X * 4);

        var delayText = GetLoc("FixedPetPosition-Delay"); // “进入战斗后生效(秒)”
        var delayWidth = ImGui.CalcTextSize(delayText).X
                         + (ImGui.GetStyle().FramePadding.X * 4);

        var xPosText = GetLoc("Position") + "-X"; // “X 坐标”
        var xPosWidth = ImGui.CalcTextSize(xPosText).X
                        + (ImGui.GetStyle().FramePadding.X * 4);

        var yPosText = GetLoc("Position") + "-Y"; // “Y 坐标”
        var yPosWidth = ImGui.CalcTextSize(yPosText).X
                        + (ImGui.GetStyle().FramePadding.X * 4);

        // 绘制表格
        using var table = ImRaii.Table("PositionSchedulesTable", 6,
                                       ImGuiTableFlags.Borders
                                       | ImGuiTableFlags.RowBg
                                       | ImGuiTableFlags.Resizable,
                                       tableWidth);
        if (!table)
            return;

        // 设置每列宽度
        ImGui.TableSetupColumn(addColText, ImGuiTableColumnFlags.WidthFixed, addColWidth);
        ImGui.TableSetupColumn(regionIdText, ImGuiTableColumnFlags.WidthFixed, regionIdWidth);
        ImGui.TableSetupColumn(delayText, ImGuiTableColumnFlags.WidthFixed, delayWidth);
        ImGui.TableSetupColumn(xPosText, ImGuiTableColumnFlags.WidthFixed, xPosWidth);
        ImGui.TableSetupColumn(yPosText, ImGuiTableColumnFlags.WidthFixed, yPosWidth);
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
                TerritoryId = 0,
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
        ImGui.TextUnformatted(delayText);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(xPosText);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(yPosText);
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
                if (ImGui.Checkbox($"##Enable_{territoryKey}_{i}", ref enabled)
                    && ImGui.IsItemDeactivatedAfterEdit())
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
                var editingTerritoryId = schedule.TerritoryId;
                if (ImGui.InputInt($"##TerritoryId_{territoryKey}_{i}", ref editingTerritoryId, 0, 0)
                    && ImGui.IsItemDeactivatedAfterEdit())
                {
                    // 只有在输入完毕后再写回
                    editingTerritoryId = Math.Max(0, editingTerritoryId);
                    if (editingTerritoryId != schedule.TerritoryId)
                    {
                        // 如果 TerritoryId 改了，需要把这条 schedule 移动到新的 key
                        // 1. 从旧列表移除
                        scheduleList.RemoveAt(i);
                        i--;

                        // 2. 放到新 key 下 (如果不存在就新建)
                        if (!ModuleConfig.PositionSchedules.ContainsKey(editingTerritoryId))
                            ModuleConfig.PositionSchedules[editingTerritoryId] = new List<PositionSchedule>();

                        ModuleConfig.PositionSchedules[editingTerritoryId].Add(new PositionSchedule
                        {
                            Enabled = schedule.Enabled,
                            TerritoryId = editingTerritoryId,
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

                // 2) TimeInSeconds
                ImGui.TableNextColumn();
                var timeInSeconds = schedule.TimeInSeconds;
                if (ImGui.InputInt($"##Time_{territoryKey}_{i}", ref timeInSeconds, 0, 0)
                    && ImGui.IsItemDeactivatedAfterEdit())
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

                // 3) PosX
                ImGui.TableNextColumn();
                var posX = schedule.PosX;
                if (ImGui.InputFloat($"##PosX_{territoryKey}_{i}", ref posX, 0f, 0f, "%.3f")
                    && ImGui.IsItemDeactivatedAfterEdit())
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

                // 4) PosZ
                ImGui.TableNextColumn();
                var posZ = schedule.PosZ;
                if (ImGui.InputFloat($"##PosZ_{territoryKey}_{i}", ref posZ, 0f, 0f, "%.3f")
                    && ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (MathF.Abs(posZ - schedule.PosZ) > 0.0001f)
                    {
                        schedule.PosZ = posZ;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 5) 操作（删除）
                ImGui.TableNextColumn();
                if (ImGui.Button($"{GetLoc("Delete")}##Schedule_{territoryKey}_{i}"))
                {
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
        var territoryId = (int)DService.ClientState.TerritoryType;
        if (!ModuleConfig.PositionSchedules.TryGetValue(territoryId, out var schedulesForThisDuty))
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

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnTerritoryChanged;
        DService.DutyState.DutyRecommenced -= OnDutyRecommenced;
        DService.Condition.ConditionChange -= OnConditionChanged;
        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        // Key: TerritoryId(区域ID) → Value: 多条调度
        public readonly Dictionary<int, List<PositionSchedule>> PositionSchedules = new();
    }

    public class PositionSchedule
    {
        public bool Enabled { get; set; } = true; // 是否启用
        public int TerritoryId { get; set; }      // 区域 ID (原 DutyId)
        public int TimeInSeconds { get; set; }    // 战斗开始后的时间点 (秒)
        public float PosX { get; set; }           // X 坐标
        public float PosZ { get; set; }           // Z 坐标
    }
}
