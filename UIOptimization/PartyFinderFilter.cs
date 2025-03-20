using DailyRoutines.Abstracts;
using DailyRoutines.Windows;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DailyRoutines.ModulesPublic.UIOptimization;

public class PartyFinderFilter : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = GetLoc("PartyFinderFilterTitle"),
        Description = GetLoc("PartyFinderFilterDescription"),
        Category    = ModuleCategories.UIOptimization,
        Author      = ["status102"]
    };

    private int batchIndex;
    private bool isSecret;
    private bool isRaid;
    private readonly HashSet<(ushort, string)> descriptionSet = [];
    private static Config ModuleConfig = null!;

    public override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new Config();
        Overlay      ??= new Overlay(this);

        DService.PartyFinder.ReceiveListing += OnReceiveListing;
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(LightSkyBlue, $"{Lang.Get("WorkTheory")}:");
        ImGuiOm.HelpMarker(Lang.Get("PartyFinderFilter-WorkTheoryHelp"));

        ImGui.Spacing();

        DrawRoleCountSettings();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(LightSkyBlue, $"{Lang.Get("PartyFinderFilter-CurrentMode")}:");

        ImGui.SameLine();
        if (ImGuiComponents.ToggleButton("ModeToggle", ref ModuleConfig.IsWhiteList))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGui.Text(ModuleConfig.IsWhiteList ? Lang.Get("Whitelist") : Lang.Get("Blacklist"));

        if (ImGui.Checkbox(Lang.Get("PartyFinderFilter-FilterDuplicate"), ref ModuleConfig.NeedFilterDuplicate))
            SaveConfig(ModuleConfig);

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Lang.Get("PartyFinderFilter-AddPreset")))
            ModuleConfig.BlackList.Add(new(true, string.Empty));

        DrawBlacklistEditor();
    }

    private void DrawRoleCountSettings()
    {
        ImGui.BeginGroup();
        ImGui.Checkbox("##HighEndDuty", ref ModuleConfig.HighEndDuty);
        ImGui.SameLine();
        ImGui.TextColored(LightSkyBlue, Lang.Get("PartyFinderFilter-RoleCount"));
        ImGui.SetNextItemWidth(150 * GlobalFontScale);
        ImGui.InputInt3(Lang.Get("PartyFinderFilter-RoleCountTH"), ref ModuleConfig.HighEndDutyRoleCount[0]);
        ImGui.SetNextItemWidth(150 * GlobalFontScale);
        ImGui.InputInt3(Lang.Get("PartyFinderFilter-RoleCountDPS"), ref ModuleConfig.HighEndDutyRoleCount[3]);
        ImGui.EndGroup();
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);
    }

    private void DrawBlacklistEditor()
    {
        var index = 0;
        foreach (var item in ModuleConfig.BlackList.ToList())
        {
            var enableState = item.Key;
            if (ImGui.Checkbox($"##available{index}", ref enableState))
            {
                ModuleConfig.BlackList[index] = new(enableState, item.Value);
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            if (DrawBlacklistItemText(index, item))
                index++;
        }
    }

    private bool DrawBlacklistItemText(int index, KeyValuePair<bool, string> item)
    {
        var value = item.Value;
        ImGui.InputText($"##{index}", ref value, 500);

        if (ImGui.IsItemDeactivatedAfterEdit())
            HandleRegexUpdate(index, item.Key, value);

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon($"##delete{index}", FontAwesomeIcon.Trash))
            ModuleConfig.BlackList.RemoveAt(index);
        return true;
    }

    private void HandleRegexUpdate(int index, bool key, string value)
    {
        try
        {
            _ = new Regex(value);
            ModuleConfig.BlackList[index] = new(key, value);
            SaveConfig(ModuleConfig);
        }
        catch (ArgumentException)
        {
            NotificationWarning(Lang.Get("PartyFinderFilter-RegexError"));
            ModuleConfig = LoadConfig<Config>() ?? new Config();
        }
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (batchIndex != args.BatchNumber)
        {
            isSecret = listing.SearchArea.HasFlag(SearchAreaFlags.Private);
            isRaid = listing.Category == DutyCategory.HighEndDuty;
            batchIndex = args.BatchNumber;
            descriptionSet.Clear();
        }

        args.Visible &= isSecret || !ModuleConfig.HighEndDuty || HighEndDutyFilterRoles(listing);
        args.Visible &= isSecret || Verify(listing);
    }

    private bool Verify(IPartyFinderListing listing)
    {
        var description = listing.Description.ToString();

        if (!string.IsNullOrEmpty(description) && ModuleConfig.NeedFilterDuplicate && !descriptionSet.Add((listing.RawDuty, description)))
            return false;

        var isMatch = ModuleConfig.BlackList
                                  .Where(i => i.Key)
                                  .Any(item => Regex.IsMatch(listing.Name.ToString(), item.Value) ||
                                               Regex.IsMatch(description, item.Value));

        return ModuleConfig.IsWhiteList ? isMatch : !isMatch;
    }

    private bool HighEndDutyFilterRoles(IPartyFinderListing listing)
    {
        if (!isRaid)
            return true;

        var j = DService.ClientState.LocalPlayer?.ClassJob.ValueNullable;
        if (j is null)
            return true;

        var job = j.Value;
        return job.Unknown11 switch // PartyBonus 下一个
        {
            0 => true, // 生产职业 or 基础职业
            1 => RoleCounter(ModuleConfig.HighEndDutyRoleCount[0]), // T
            2 => RoleCounter(ModuleConfig.HighEndDutyRoleCount[1]), // 血奶
            6 => RoleCounter(ModuleConfig.HighEndDutyRoleCount[2]), // 盾奶
            3 or 4 or 5 => RoleCounter(ModuleConfig.HighEndDutyRoleCount[job.Unknown11]), // 3近 4远敏 5法
            _ => true,
        };

        bool RoleCounter(int maxCount)
        {
            var count = 0;
            var hasSlot = false;
            foreach (var i in Enumerable.Range(0, 8))
            {
                if (listing.Slots.Count <= i || listing.JobsPresent.Count <= i || count >= maxCount)
                    break;

                if (listing.JobsPresent.ElementAt(i).Value.RowId == job.RowId)
                    return false;
                else if (listing.JobsPresent.ElementAt(i).Value.RowId != 0)
                {
                    if (listing.JobsPresent.ElementAt(i).Value.Unknown11 == job.Unknown11)
                        count++;
                }
                else if (listing.Slots.ElementAt(i)[ClassJobFlag(job.RowId)])
                    hasSlot = true;
            }

            return count < maxCount && hasSlot;
        }
    }

    private static JobFlags ClassJobFlag(uint index)
    {
        if (!Enum.IsDefined(typeof(Job), index))
            return 0;

        var job = (Job)index;
        if (Enum.TryParse<JobFlags>(job.ToString(), out var flag))
            return flag;

        return 0;
    }

    public override void Uninit()
    {
        DService.PartyFinder.ReceiveListing -= OnReceiveListing;
        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public List<KeyValuePair<bool, string>> BlackList = [];
        public bool IsWhiteList;
        public bool NeedFilterDuplicate = true;
        public bool HighEndDuty = true;
        public int[] HighEndDutyRoleCount = { 2, 1, 1, 2, 1, 2 }; // T2, 血奶1, 盾奶1, 近2, 远1, 法2
    }

    #region enum
    private enum Job : uint
    {

        /// <summary>
        /// Gladiator (GLD).
        /// </summary>
        Gladiator = 1,

        /// <summary>
        /// Pugilist (PGL).
        /// </summary>
        Pugilist,

        /// <summary>
        /// Marauder (MRD).
        /// </summary>
        Marauder,

        /// <summary>
        /// Lancer (LNC).
        /// </summary>
        Lancer,

        /// <summary>
        /// Archer (ARC).
        /// </summary>
        Archer,

        /// <summary>
        /// Conjurer (CNJ).
        /// </summary>
        Conjurer,

        /// <summary>
        /// Thaumaturge (THM).
        /// </summary>
        Thaumaturge,

        /// <summary>
        /// Paladin (PLD).
        /// </summary>
        Paladin = 19,

        /// <summary>
        /// Monk (MNK).
        /// </summary>
        Monk,

        /// <summary>
        /// Warrior (WAR).
        /// </summary>
        Warrior,

        /// <summary>
        /// Dragoon (DRG).
        /// </summary>
        Dragoon,

        /// <summary>
        /// Bard (BRD).
        /// </summary>
        Bard,

        /// <summary>
        /// White mage (WHM).
        /// </summary>
        WhiteMage,

        /// <summary>
        /// Black mage (BLM).
        /// </summary>
        BlackMage,

        /// <summary>
        /// Arcanist (ACN).
        /// </summary>
        Arcanist,

        /// <summary>
        /// Summoner (SMN).
        /// </summary>
        Summoner,

        /// <summary>
        /// Scholar (SCH).
        /// </summary>
        Scholar,

        /// <summary>
        /// Rogue (ROG).
        /// </summary>
        Rogue,

        /// <summary>
        /// Ninja (NIN).
        /// </summary>
        Ninja,

        /// <summary>
        /// Machinist (MCH).
        /// </summary>
        Machinist,

        /// <summary>
        /// Dark Knight (DRK).
        /// </summary>
        DarkKnight,

        /// <summary>
        /// Astrologian (AST).
        /// </summary>
        Astrologian,

        /// <summary>
        /// Samurai (SAM).
        /// </summary>
        Samurai,

        /// <summary>
        /// Red mage (RDM).
        /// </summary>
        RedMage,

        /// <summary>
        /// Blue mage (BLU).
        /// </summary>
        BlueMage,

        /// <summary>
        /// Gunbreaker (GNB).
        /// </summary>
        Gunbreaker,

        /// <summary>
        /// Dancer (DNC).
        /// </summary>
        Dancer,

        /// <summary>
        /// Reaper (RPR).
        /// </summary>
        Reaper,

        /// <summary>
        /// Sage (SGE).
        /// </summary>
        Sage,

        /// <summary>
        /// Viper (VPR).
        /// </summary>
        Viper,

        /// <summary>
        /// Pictomancer (PCT).
        /// </summary>
        Pictomancer,
    }
    #endregion
}
