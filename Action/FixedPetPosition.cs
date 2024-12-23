using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Math;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.ModuleTemplate;

public class FixedPetPosition : DailyModuleBase
{
    private static Config ModuleConfig = null!;
    private DateTime BattleStartTime = DateTime.MinValue;

    private class Config : ModuleConfiguration
    {
        public List<PositionSchedule> PositionSchedules = new();
    }

    public class PositionSchedule
    {
        public bool Enabled { get; set; } = true; // 是否启用
        public int DutyId { get; set; }        // 副本ID
        public int TimeInSeconds { get; set; } // 战斗开始后的时间点(秒)
        public float PosX { get; set; }        // X 坐标
        public float PosZ { get; set; }        // Z 坐标
    }
    
    public override ModuleInfo Info => new()
    {
        Author = ["Wotou"],
        Title = GetLoc("FixedPetPositionTitle"),
        Description = GetLoc("FixedPetPositionDescription"),
        Category = ModuleCategories.Action,
    };

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        TaskHelper ??= new TaskHelper { TimeLimitMS = 30_000 };

        DService.ClientState.TerritoryChanged += OnTerritoryChanged;
        DService.DutyState.DutyRecommenced += OnDutyRecommenced;
        DService.Condition.ConditionChange += OnConditionChanged;

        TaskHelper.Enqueue(SchedulePetMovements);
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(LightSkyBlue, GetLoc("FixedPetPosition-Config"));
        
        ImGui.NewLine();
        
        //显示当前地图ID
        ImGui.Text($"{GetLoc("FixedPetPosition-CurrentTerritoryTypeID")} {DService.ClientState.TerritoryType}");
        
        ImGui.NewLine();

        // 表格，6 列：Enable, DutyID、TimeInSeconds、PosX、PosZ、操作
        if (ImGui.BeginTable("PositionSchedulesTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable, // 允许拖动列宽
                             new Vector2(480f * GlobalFontScale, 0))) 
        {
            ImGui.TableSetupColumn("添加", ImGuiTableColumnFlags.WidthFixed, 25f * GlobalFontScale);
            ImGui.TableSetupColumn("区域ID", ImGuiTableColumnFlags.WidthFixed, 80f * GlobalFontScale);
            ImGui.TableSetupColumn("进入战斗后生效(秒)", ImGuiTableColumnFlags.WidthFixed, 160f * GlobalFontScale);
            ImGui.TableSetupColumn("X 坐标", ImGuiTableColumnFlags.WidthFixed, 80f * GlobalFontScale);
            ImGui.TableSetupColumn("Y 坐标", ImGuiTableColumnFlags.WidthFixed, 80f * GlobalFontScale);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            
            ImGui.TableNextColumn();
            if (ImGuiOm.ButtonIconSelectable("AddNewPreset", FontAwesomeIcon.Plus))
            {
                // 点击 + 号时，添加新一行
                ModuleConfig.PositionSchedules.Add(new PositionSchedule
                {
                    Enabled = true,
                    DutyId = 0,
                    TimeInSeconds = 0,
                    PosX = 0f,
                    PosZ = 0f,
                });
                SaveConfig(ModuleConfig);

                TaskHelper.Abort();
                TaskHelper.Enqueue(SchedulePetMovements);
            }
            
            ImGui.TableNextColumn();
            ImGui.Text(GetLoc("FixedPetPosition-DutyId"));
            ImGui.TableNextColumn();
            ImGui.Text(GetLoc("FixedPetPosition-Delay"));
            ImGui.TableNextColumn();
            ImGui.Text(GetLoc("FixedPetPosition-PositionX"));
            ImGui.TableNextColumn();
            ImGui.Text(GetLoc("FixedPetPosition-PositionY"));
            ImGui.TableNextColumn();
            ImGui.Text(GetLoc("FixedPetPosition-Operation"));

            for (int i = 0; i < ModuleConfig.PositionSchedules.Count; i++)
            {
                var schedule = ModuleConfig.PositionSchedules[i];

                ImGui.TableNextRow();
                
                // 0. 启用 CheckBox
                ImGui.TableNextColumn();
                {
                    var enabled = schedule.Enabled;
                    if (ImGui.Checkbox($"##Enable_{i}", ref enabled))
                    {
                        schedule.Enabled = enabled;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 1. DutyId (副本ID)
                ImGui.TableNextColumn();
                {
                    var editingDutyId = schedule.DutyId;
                    if (ImGui.InputInt($"##DutyID_{i}", ref editingDutyId, 0, 0))
                    {
                        schedule.DutyId = editingDutyId;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 2. TimeInSeconds (战斗开始后的时间点，单位:秒)
                ImGui.TableNextColumn();
                {
                    var timeInSeconds = schedule.TimeInSeconds;
                    if (ImGui.InputInt($"##Time_{i}", ref timeInSeconds, 0, 0))
                    {
                        // 限制最小值 >= 0
                        schedule.TimeInSeconds = Math.Max(0, timeInSeconds);
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 3. PosX
                ImGui.TableNextColumn();
                {
                    var posX = schedule.PosX;
                    if (ImGui.InputFloat($"##PosX_{i}", ref posX, 0f, 0f, "%.3f"))
                    {
                        schedule.PosX = posX;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 4. PosZ
                ImGui.TableNextColumn();
                {
                    var posZ = schedule.PosZ;
                    if (ImGui.InputFloat($"##PosZ_{i}", ref posZ, 0f, 0f, "%.3f"))
                    {
                        schedule.PosZ = posZ;
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                    }
                }

                // 5. 操作（删除）
                ImGui.TableNextColumn();
                {
                    if (ImGui.Button($"{GetLoc("FixedPetPosition-Delete")}##Schedule_{i}"))
                    {
                        // 删除当前行
                        ModuleConfig.PositionSchedules.RemoveAt(i);
                        SaveConfig(ModuleConfig);
                        TaskHelper.Abort();
                        TaskHelper.Enqueue(SchedulePetMovements);
                        i--; // 修正索引，避免下标越界
                        continue;
                    }
                }
            }

            ImGui.EndTable();
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
        if (!CheckIsEightPlayerDuty()) return;

        var territoryId = DService.ClientState.TerritoryType;
        var schedulesForThisDuty = ModuleConfig.PositionSchedules
                                               .Where(x => x.DutyId == territoryId && x.Enabled)
                                               .ToList();
        var elapsedTimeInSeconds = (DateTime.Now - BattleStartTime).TotalSeconds;

        // =======================
        // 1. 处理“战斗中”的情况
        // =======================
        if (DService.Condition[ConditionFlag.InCombat])
        {
            // 在所有 TimeInSeconds <= 已过战斗时间、且 > 0 的 schedule 中，
            // 找到 TimeInSeconds 最大的那条
            var bestSchedule = schedulesForThisDuty
                               .Where(x => x.TimeInSeconds <= elapsedTimeInSeconds)
                               .OrderByDescending(x => x.TimeInSeconds)
                               .FirstOrDefault();

            if (bestSchedule != null)
            {
                //NotifyHelper.Chat($"开始移动宠物至 01 {bestSchedule.PosX}, {bestSchedule.PosZ}");
                TaskHelper.Enqueue(() => MovePetToLocation(bestSchedule.PosX, bestSchedule.PosZ));
            }
        }
        else
        {
            // =======================
            // 2. 处理“没在战斗”的情况
            // =======================
            var scheduleForZero = schedulesForThisDuty
                .FirstOrDefault(x => x.TimeInSeconds == 0);

            if (scheduleForZero != null)
            {
                //NotifyHelper.Chat($"开始移动宠物至 02 {scheduleForZero.PosX}, {scheduleForZero.PosZ}");
                TaskHelper.Enqueue(() => MovePetToLocation(scheduleForZero.PosX, scheduleForZero.PosZ));
            }
        }

        // 继续让这个方法循环执行，可以视需求做间隔
        TaskHelper.DelayNext(1_000);
        TaskHelper.Enqueue(SchedulePetMovements);
    }

    private unsafe void MovePetToLocation(float posX, float posZ)
    {
        if (!CheckIsEightPlayerDuty()) return;

        var player = DService.ClientState.LocalPlayer;
        if (player == null) return;

        var pet = CharacterManager.Instance()->LookupPetByOwnerObject((BattleChara*)player.Address);
        if (pet == null) return;

        var petLocation = pet->Position;
        var location = new Vector3(posX, petLocation.Y, posZ); // 保持宠物当前高度
        //NotifyHelper.Chat("开始移动宠物至 " + location);
        TaskHelper.Enqueue(
            () => ExecuteCommandManager.ExecuteCommandComplexLocation(ExecuteCommandComplexFlag.PetAction, location,
                                                                      3));
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

        var contentFinderCondition = territory.ContentFinderCondition.Value;

        if (contentFinderCondition == null)
        {
            //NotifyHelper.Chat($"当前区域 {territoryId} 不是副本。");
            return false;
        }

        // 检查副本最大人数
        return contentFinderCondition.ContentMemberType.Row == 3;
    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnTerritoryChanged;
        DService.DutyState.DutyRecommenced -= OnDutyRecommenced;
        DService.Condition.ConditionChange -= OnConditionChanged;
        base.Uninit();
    }
}
