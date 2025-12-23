using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Config;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using OmenTools.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DailyRoutines.Modules;

public unsafe class AutoBattleEffectsChanger : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("AutoBattleEffectsChangerTitle"),
        Description = GetLoc("AutoBattleEffectsChangerDescription"),
        Category = ModuleCategories.System,
        Author = ["Siren"]
    };

    // 缓存
    private EffectSettings? lastAppliedSettings = null;
    private Config ModuleConfig = null!;

    protected override void Init()
    {
        // 加载配置，如果为空则创建默认值
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        DService.Framework.Update += OnUpdate;
    }

    protected override void Uninit()
    {
        DService.Framework.Update -= OnUpdate;
        SaveConfig(ModuleConfig);
        base.Uninit();
    }

    protected override void ConfigUI()
    {
        ImGui.TextWrapped(GetLoc("AutoBattleEffectsChanger-ConfigIntro"));
        ImGui.Separator();

        if (ImGui.BeginTabBar("AutoBattleEffectsChangerTabBar"))
        {

            if (ImGui.BeginTabItem(GetLoc("AutoBattleEffectsChanger-TabOverworld")))
            {
                DrawOverworldSettings();
                ImGui.EndTabItem();
            }


            if (ImGui.BeginTabItem(GetLoc("AutoBattleEffectsChanger-TabDuty")))
            {
                DrawDutySettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    // 飞副本状态
    private void DrawOverworldSettings()
    {
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-OverworldHeader"));

        ImGui.Spacing();

        // 阈值
        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-Limit1Label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("##Limit1", ref ModuleConfig.AroundCountLimit1))
            SaveConfig(ModuleConfig);

        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-Limit2Label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("##Limit2", ref ModuleConfig.AroundCountLimit2))
            SaveConfig(ModuleConfig);

        ImGui.Separator();

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-ConditionLow", ModuleConfig.AroundCountLimit1), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("Tier1", ModuleConfig.OverworldLow);

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-ConditionMid", ModuleConfig.AroundCountLimit1, ModuleConfig.AroundCountLimit2), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("Tier2", ModuleConfig.OverworldMid);

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-ConditionHigh", ModuleConfig.AroundCountLimit2), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("Tier3", ModuleConfig.OverworldHigh);
    }

    // 绘制副本设置部分
    private void DrawDutySettings()
    {
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-DutyHeader"));
        ImGui.Spacing();

        // 默认副本设置
        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-DutyDefaultHeader"), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("DutyDefault", ModuleConfig.DefaultDutySettings);

        ImGui.Separator();

        // 获取所有 ContentType 如果没Name就跳过
        var contentTypes = DService.Data.GetExcelSheet<ContentType>();
        if (contentTypes == null) return;

        foreach (var contentType in contentTypes)
        {
            var name = contentType.Name.ToString();
            if (string.IsNullOrEmpty(name)) continue; // !!跳过没名字的

            var id = contentType.RowId;

            if (!ModuleConfig.DutySpecificSettings.ContainsKey(id))
                ModuleConfig.DutySpecificSettings[id] = new EffectSettings();

            var settings = ModuleConfig.DutySpecificSettings[id];

            var icon = DService.Texture.GetFromGameIcon(new GameIconLookup(contentType.Icon));
            if (icon != null)
            {
                ImGui.Image(icon.GetWrapOrEmpty().Handle, new Vector2(24, 24));
                ImGui.SameLine();
            }

            // 使用 TreeNode 组织界面
            if (ImGui.TreeNode($"{name} (ID: {id})##Type{id}"))
            {
                ImGui.Checkbox(GetLoc("AutoBattleEffectsChanger-EnableSpecific"), ref settings.Enabled);
                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveConfig(ModuleConfig);

                if (settings.Enabled)
                    DrawEffectSettingRow($"Duty_{id}", settings);
                else
                    ImGui.TextDisabled(GetLoc("AutoBattleEffectsChanger-UseDefaultNotice"));
                ImGui.TreePop();
            }
        }
    }

    private void DrawEffectSettingRow(string idPrefix, EffectSettings settings)
    {
        ImGui.PushID(idPrefix);

        // 使用 Table 布局对齐
        if (ImGui.BeginTable("EffectTable", 4, ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColSelf"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColParty"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColOther"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColEnemy"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawCombo("##Self", ref settings.Self);

            ImGui.TableNextColumn();
            DrawCombo("##Party", ref settings.Party);

            ImGui.TableNextColumn();
            DrawCombo("##Other", ref settings.Other);

            ImGui.TableNextColumn();
            DrawCombo("##Enemy", ref settings.Enemy);

            ImGui.EndTable();
        }
        ImGui.PopID();
    }

    private void DrawCombo(string label, ref uint value)
    {
        // 0:显示所有, 1:简易显示, 2:不显示
        string[] options = { GetLoc("AutoBattleEffectsChanger-OptionAll"), GetLoc("AutoBattleEffectsChanger-OptionLimited"), GetLoc("AutoBattleEffectsChanger-OptionNone") };
        int current = (int)Math.Clamp(value, 0, 2);

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo(label, ref current, options, options.Length))
        {
            value = (uint)current;
            SaveConfig(ModuleConfig);
        }
    }

    protected void OnUpdate(IFramework framework)
    {
        if (DService.PlayerState == null) return;

        EffectSettings? targetSettings = null;

        //判断是否在副本中
        if (DService.Condition[ConditionFlag.BoundByDuty])
        {
            var instance = GameMain.Instance();
            if (instance != null && instance->CurrentContentFinderConditionId != 0)
            {
                // 获取当前的 ContentType
                if (LuminaGetter.TryGetRow<ContentFinderCondition>(instance->CurrentContentFinderConditionId, out var content)
                    && content.ContentType.IsValid)
                {
                    uint typeId = content.ContentType.Value.RowId;

                    // 检查是否有针对此类型的单独配置
                    if (ModuleConfig.DutySpecificSettings.TryGetValue(typeId, out var specificConfig) && specificConfig.Enabled)
                        targetSettings = specificConfig;
                }
            }

            // 如果没有特定的副本配置，或者获取失败，使用默认副本配置
            targetSettings ??= ModuleConfig.DefaultDutySettings;
        }
        //人数判断
        else
        {
            int playerCount = PlayersManager.PlayersAroundCount;

            if (playerCount < ModuleConfig.AroundCountLimit1)
                targetSettings = ModuleConfig.OverworldLow;
            else if (playerCount < ModuleConfig.AroundCountLimit2)
                targetSettings = ModuleConfig.OverworldMid;
            else
                targetSettings = ModuleConfig.OverworldHigh;
        }

        //应用设置 (仅当设置发生变化时)
        ApplySettings(targetSettings);
    }

    private void ApplySettings(EffectSettings? settings)
    {
        if (settings == null) return;

        // 缓存发力了
        if (lastAppliedSettings != null && settings.Equals(lastAppliedSettings))
            return;

        try
        {
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectSelf.ToString(), (uint)settings.Self);
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectParty.ToString(), (uint)settings.Party);
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectOther.ToString(), (uint)settings.Other);
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectPvPEnemyPc.ToString(), (uint)settings.Enemy);

            // 更新缓存
            lastAppliedSettings = settings.Clone();
        }
        catch (Exception ex)
        {
            DService.Log.Error(ex.ToString());
        }
    }


    public class EffectSettings : IEquatable<EffectSettings>
    {
        public bool Enabled = false; // 副本列表开关
        public uint Self = 0; // 0: All
        public uint Party = 1; // 1: Limited
        public uint Other = 2; // 2: None
        public uint Enemy = 0; // 0: All

        public EffectSettings Clone()
        {
            return (EffectSettings)this.MemberwiseClone();
        }

        public bool Equals(EffectSettings? other)
        {
            if (other is null) return false;
            return Self == other.Self &&
                   Party == other.Party &&
                   Other == other.Other &&
                   Enemy == other.Enemy;
        }
    }

    public class Config : ModuleConfiguration
    {
        public int AroundCountLimit1 = 20;
        public int AroundCountLimit2 = 40;

        public EffectSettings OverworldLow = new() { Self = 0, Party = 0, Other = 0, Enemy = 0 };
        public EffectSettings OverworldMid = new() { Self = 0, Party = 1, Other = 1, Enemy = 0 };
        public EffectSettings OverworldHigh = new() { Self = 0, Party = 1, Other = 2, Enemy = 0 };

        // 默认副本配置
        public EffectSettings DefaultDutySettings = new() { Self = 0, Party = 0, Other = 1, Enemy = 0 };

        // 特定副本类型的配置 (Key: ContentType RowId)
        public Dictionary<uint, EffectSettings> DutySpecificSettings = new();
    }
}
