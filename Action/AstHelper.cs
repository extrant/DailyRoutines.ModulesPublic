using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using DailyRoutines.Abstracts;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using OmenTools;
using OmenTools.Helpers;
using OmenTools.Infos;


namespace DailyRoutines.Modules;

public class ASTHelper : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Author = ["HaKu"],
        Title = GetLoc("ASTHelper-Title"),
        Description = GetLoc("ASTHelper-Description"),
        Category = ModuleCategories.Action
    };

    // module specific config
    private static Config? ModuleConfig;

    // auto play card candidates (melee and range)
    private const uint UnspecificTargetId = 0xE0000000;
    private static uint MeleeCandidateId = UnspecificTargetId;
    private static uint RangeCandidateId = UnspecificTargetId;

    // debug mode
    private const bool DEBUG = false;

    // HttpClient for fetching FFLogs (auto play card advance mode)
    private static readonly HttpClient Client = new();
    private const string FFLogsUri = "https://www.fflogs.com/v1";

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        if (DEBUG) NotifyHelper.Chat("AST Helper Loaded (DEBUG Mode)");

        // hook before send card & heal
        UseActionManager.Register(OnPreUseAction);

        // fetch team logs when zone changed (for ultimates and current savage)
        DService.ClientState.TerritoryChanged += OnZoneChanged;
    }

    public override void ConfigUI()
    {
        // auto play card
        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("ASTHelper-AutoPlayCard-Title"));
        ImGui.Text(GetLoc("ASTHelper-AutoPlayCard-Description"));
        ImGui.Spacing();
        if (ImGui.RadioButton(GetLoc("ASTHelper-AutoPlayCard-Disabled"), ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Disable))
        {
            ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Disable;
            SaveConfig(ModuleConfig);
        }

        if (ImGui.RadioButton(GetLoc("ASTHelper-AutoPlayCard-Default"), ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Default))
        {
            ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Default;
            SaveConfig(ModuleConfig);
        }

        if (ImGui.RadioButton(GetLoc("ASTHelper-AutoPlayCard-Advance"), ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance))
        {
            ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Advance;
            SaveConfig(ModuleConfig);
        }

        // Api Key [v1] for fetching FFLogs records (auto play card advance mode)
        if (ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance)
        {
            // api key (v1)
            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(GetLoc("ASTHelper-LogsApi"));
            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            if (ImGui.InputText("##FFLogsAPIKey", ref ModuleConfig.FFLogsAPIKey, 32))
                SaveConfig(ModuleConfig);
            ImGui.SameLine();
            if (ImGui.Button(GetLoc("Save")))
            {
                if (string.IsNullOrWhiteSpace(ModuleConfig.FFLogsAPIKey) || ModuleConfig.FFLogsAPIKey.Length != 32)
                {
                    ModuleConfig.KeyValid = false;
                    SaveConfig(ModuleConfig);
                    HelpersOm.NotificationError(GetLoc("ASTHelper-LogsApi-LengthError"), GetLoc("ASTHelper-LogsApi-Invalid"));
                    return;
                }

                CheckKeyStatus();
            }

            // key status (valid or invalid)
            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(GetLoc("ASTHelper-LogsApi-Status"));
            ImGui.SameLine();
            ImGui.Text(ModuleConfig.KeyValid ? GetLoc("Connected") : GetLoc("Disconnected"));
        }

        // easy heal
        ImGui.NewLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("ASTHelper-EasyHeal-Title"));
        ImGui.Text(GetLoc("ASTHelper-EasyHeal-Description"));
        ImGui.Spacing();

        if (ImGui.RadioButton(GetLoc("ASTHelper-EasyHeal-Disabled"), ModuleConfig.EasyHeal == EasyHealStatus.Disable))
        {
            ModuleConfig.EasyHeal = EasyHealStatus.Disable;
            SaveConfig(ModuleConfig);
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(GetLoc("ASTHelper-EasyHeal-Enabled"), ModuleConfig.EasyHeal == EasyHealStatus.Enable))
        {
            ModuleConfig.EasyHeal = EasyHealStatus.Enable;
            SaveConfig(ModuleConfig);
        }

        // heal threshold
        if (ModuleConfig.EasyHeal == EasyHealStatus.Enable)
        {
            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(GetLoc("ASTHelper-EasyHeal-HealThreshold"));
            ImGui.Spacing();
            if (ImGui.SliderFloat("##HealThreshold", ref ModuleConfig.NeedHealThreshold, 0.0f, 1.2f, "%.2f"))
                SaveConfig(ModuleConfig);

            // all time heal warning
            if (ModuleConfig.NeedHealThreshold > 0.95f)
            {
                ImGui.Spacing();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(GetLoc("ASTHelper-EasyHeal-OverhealWarning"));
            }
        }

        // debug
        if (DEBUG)
        {
            ImGui.NewLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Debug Tools:");
            ImGui.SameLine();
            if (ImGui.Button("Fetch Candidate Manually"))
                OnZoneChanged(1226);
        }
    }


    // hook before play card & target heal
    private static void OnPreUseAction(
        ref bool isPrevented, ref ActionType type, ref uint actionId,
        ref ulong targetId, ref Vector3 location, ref uint extraParam)
    {
        {
            if (type != ActionType.Action || DService.PartyList.Length == 0) return;

            // auto play card
            if (ModuleConfig.AutoPlayCard != AutoPlayCardStatus.Disable)
            {
                // melee card: the balance 37023
                if (targetId == UnspecificTargetId && actionId == 37023)
                {
                    NotifyHelper.Chat(GetLoc("ASTHelper-AutoPlayCard-Message-Melee", FetchMemberNameById(MeleeCandidateId)));
                    targetId = MeleeCandidateId;
                }
                // range card: the spear 37026
                else if (targetId == UnspecificTargetId && actionId == 37026)
                {
                    NotifyHelper.Chat(GetLoc("ASTHelper-AutoPlayCard-Message-Melee", FetchMemberNameById(RangeCandidateId)));
                    targetId = RangeCandidateId;
                }
            }

            // easy heal
            if (ModuleConfig.EasyHeal == EasyHealStatus.Enable)
            {
                if (targetId == UnspecificTargetId && TargetHealActions.Contains(actionId))
                {
                    // find target with the lowest HP ratio within range and satisfy threshold
                    targetId = TargetNeedHeal();
                    if (targetId == UnspecificTargetId)
                    {
                        NotifyHelper.Chat(GetLoc("ASTHelper-EasyHeal-PreventOverHeal"));
                        isPrevented = true;
                        return;
                    }

                    NotifyHelper.Chat(GetLoc("ASTHealer-EasyHeal-Message", FetchMemberNameById((uint)targetId)));
                }
            }
        }
    }

    // fetch member information by id
    private static SeString FetchMemberNameById(uint id)
    {
        var member = DService.PartyList.FirstOrDefault(m => m.ObjectId == id);
        return member != null ? member.Name : "Unknown";
    }

    // update candidates when zone changed
    private void OnZoneChanged(ushort zone)
    {
        // disable auto play card or no party member
        if (ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Disable || DService.PartyList.Length == 0) return;


        // reset candidates when zone changed
        MeleeCandidateId = 0;
        RangeCandidateId = 0;

        // find card candidates
        var partyList = DService.PartyList; // role [0 tank, 2 melee, 3 range, 4 healer]
        var selectedMeleeReason = string.Empty;
        var selectedRangeReason = string.Empty;

        // check FFLogs API Key status when in advance mode
        if (ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance && !ModuleConfig.KeyValid)
        {
            NotifyHelper.Chat(GetLoc("ASTHealer-AutoPlayCard-AdvanceFallback"));
            ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Default;
            SaveConfig(ModuleConfig);
        }

        // select the best candidate for melee and range according to rdps
        if (ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance)
        {
            var tmpBestMeleeDps = -1.0;
            var tmpBestRangeDps = -1.0;
            foreach (var member in partyList)
            {
                // fetch highest rDPS record in current region, job and patch
                var bestRecord = FetchBestLogsRecord(zone, member).GetAwaiter().GetResult();
                if (bestRecord == null) continue;

                switch (member.ClassJob.GameData.Role)
                {
                    case 2:
                        // better melee candidate?
                        if (bestRecord.DPS > tmpBestMeleeDps)
                        {
                            MeleeCandidateId = member.ObjectId;
                            tmpBestMeleeDps = bestRecord.DPS;
                            selectedMeleeReason = GetLoc("ASTHelper-AutoPlayCard-Message-HighestDPS", $"{bestRecord.DPS:0.0}");
                        }

                        break;
                    case 3:
                        // better range candidate?
                        if (bestRecord.DPS > tmpBestRangeDps)
                        {
                            RangeCandidateId = member.ObjectId;
                            tmpBestRangeDps = bestRecord.DPS;
                            selectedMeleeReason = GetLoc("ASTHelper-AutoPlayCard-Message-HighestDPS", $"{bestRecord.DPS:0.0}");
                        }

                        break;
                }
            }
        }

        // select candidates according to default order
        else if (ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Default)
        {
            // melee with higher order
            foreach (var meleeJob in MeleeOrder)
            {
                var meleeMember = partyList.FirstOrDefault(m => m.ClassJob.GameData.NameEnglish == meleeJob);
                if (meleeMember != null)
                {
                    MeleeCandidateId = meleeMember.ObjectId;
                    selectedMeleeReason = GetLoc("ASTHelper-AutoPlayCard-Message-HighestOrder", meleeMember.ClassJob.GameData.Name);
                    break;
                }
            }

            // range with higher order
            foreach (var rangeJob in RangeOrder)
            {
                var rangeMember = partyList.FirstOrDefault(m => m.ClassJob.GameData.NameEnglish == rangeJob);
                if (rangeMember != null)
                {
                    RangeCandidateId = rangeMember.ObjectId;
                    selectedMeleeReason = GetLoc("ASTHelper-AutoPlayCard-Message-HighestOrder", rangeMember.ClassJob.GameData.Name);
                    break;
                }
            }
        }

        // melee fallback: if no candidate found, select the first melee or first party member.
        if (MeleeCandidateId == 0)
        {
            MeleeCandidateId = partyList.FirstOrDefault(m => m.ClassJob.GameData.Role == 2, partyList.First()).ObjectId;
            selectedMeleeReason = GetLoc("ASTHelper-AutoPlayCard-Message-FallFirst");
        }

        // range fallback: if no candidate found, select the last range or last party member.
        if (RangeCandidateId == 0)
        {
            RangeCandidateId = partyList.LastOrDefault(m => m.ClassJob.GameData.Role == 3, partyList.Last()).ObjectId;
            selectedRangeReason = GetLoc("ASTHelper-AutoPlayCard-Message-FallLast");
        }

        // notify candidates
        var meleeCandidateName = FetchMemberNameById(MeleeCandidateId);
        var rangeCandidateName = FetchMemberNameById(RangeCandidateId);
        NotifyHelper.Chat(GetLoc("ASTHelper-AutoPlayCard-Message-MeleeCandidate", meleeCandidateName, selectedMeleeReason));
        NotifyHelper.Chat(GetLoc("ASTHelper-AutoPlayCard-Message-RangeCandidate", rangeCandidateName, selectedRangeReason));
    }

    private static uint TargetNeedHeal()
    {
        var partyList = DService.PartyList;
        var lowRatio = 2f;
        var needHealId = UnspecificTargetId;
        foreach (var member in partyList)
        {
            var ratio = member.CurrentHP / (float)member.MaxHP;
            if (DEBUG) NotifyHelper.Chat($"{member.Name} | {ratio:0.00} | {ModuleConfig.NeedHealThreshold}");
            if (ratio < lowRatio && ratio < ModuleConfig.NeedHealThreshold)
            {
                lowRatio = ratio;
                needHealId = member.ObjectId;
            }
        }

        return needHealId;
    }

    private async Task CheckKeyStatus()
    {
        try
        {
            var uri = $"{FFLogsUri}/classes?api_key={ModuleConfig.FFLogsAPIKey}";
            var response = await Client.GetStringAsync(uri);
            if (!string.IsNullOrWhiteSpace(response))
            {
                ModuleConfig.KeyValid = true;
                SaveConfig(ModuleConfig);
                HelpersOm.NotificationSuccess(GetLoc("ASTHelper-LogsApi-Message-Success"), GetLoc("Connected"));
            }
            else
            {
                ModuleConfig.KeyValid = false;
                SaveConfig(ModuleConfig);
                if (DEBUG) HelpersOm.NotificationError("Connect to FFLogs failed. (Wrong Response)", "Invalid API Key");
            }
        }
        catch (HttpRequestException httpEx)
        {
            ModuleConfig.KeyValid = false;
            SaveConfig(ModuleConfig);
            if (DEBUG)
                HelpersOm.NotificationError($"Connection to FFLogs failed. (Http Connection)", "Invalid API Key");
        }
        catch (Exception ex)
        {
            ModuleConfig.KeyValid = false;
            SaveConfig(ModuleConfig);
            if (DEBUG)
                HelpersOm.NotificationError($"Connection to FFLogs failed. (Unexpected Error)", "Invalid API Key");
        }
    }

    private static async Task<LogsRecord?> FetchBestLogsRecord(ushort zone, IPartyMember member)
    {
        // get character info
        var charaName = member.Name;
        var serverSlug = member.World.GameData.Name;
        var region = "CN"; // cn client
        var job = member.ClassJob.GameData.NameEnglish;

        // fetch record
        try
        {
            var uri = $"{FFLogsUri}/parses/character/{charaName}/{serverSlug}/{region}";
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["api_key"] = ModuleConfig.FFLogsAPIKey;
            query["metric"] = "rdps";
            query["encounter"] = Dal2LogsZoneMap[zone].ToString();

            // contains all ultimates and current savage in current patch
            var response = await Client.GetStringAsync($"{uri}?{query}");
            var records = JsonSerializer.Deserialize<LogsRecord[]>(response);
            if (records == null || records.Length == 0) return null;

            // find best record
            var bestRecord = records.Where(r => r.JobName == job)
                                    .OrderByDescending(r => r.Difficulty)
                                    .ThenByDescending(r => r.DPS)
                                    .FirstOrDefault();
            return bestRecord;
        }
        catch (HttpRequestException httpEx)
        {
            if (DEBUG) HelpersOm.NotificationError($"Failed to fetch FFLogs record for {charaName}. (Http Connection)");
            return null;
        }
        catch (JsonException jsonEx)
        {
            if (DEBUG) HelpersOm.NotificationError($"Failed to fetch FFLogs record for {charaName}. (Json Parse)");
            return null;
        }
        catch (Exception ex)
        {
            if (DEBUG)
                HelpersOm.NotificationError($"Failed to fetch FFLogs record for {charaName}. (Unexpected Error)");
            return null;
        }
    }

    public override void Uninit()
    {
        UseActionManager.Unregister(OnPreUseAction);
        DService.ClientState.TerritoryChanged -= OnZoneChanged;

        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        // auto play card
        public AutoPlayCardStatus AutoPlayCard = AutoPlayCardStatus.Default;

        // FFLogs API Key v1 for fetching records (auto play card advance mode)
        public string FFLogsAPIKey = string.Empty;
        public bool KeyValid;

        // easy heal
        public EasyHealStatus EasyHeal = EasyHealStatus.Enable;
        public float NeedHealThreshold = 0.8f;
    }

    // Dalamud-FFLogs zone match map (ultimates and current savage)
    public static readonly Dictionary<uint, uint> Dal2LogsZoneMap = new()
    {
        // ultimates
        { 733, 1073 },  // Bahamut f1bz
        { 777, 1074 },  // Weapon w1fz
        { 887, 1075 },  // Alexander d2az
        { 968, 1076 },  // Dragonsong r1fz
        { 1122, 1077 }, // Omega z3oz
        // m1-4s
        { 1226, 93 },
        { 1228, 94 },
        { 1230, 95 },
        { 1232, 96 }
    };

    public class LogsRecord
    {
        // job english name
        [JsonPropertyName("spec")]
        public string JobName { get; set; }

        // record difficulty
        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; }

        // rDPS
        [JsonPropertyName("total")]
        public double DPS { get; set; }
    }

    public enum AutoPlayCardStatus
    {
        Disable, // disable auto play card
        Default, // select target based on predefined order when no target selected
        Advance, // select target based on FFLogs rDPS records when no target selected
    }

    public enum EasyHealStatus
    {
        Disable, // disable easy heal
        Enable,  // select target with the lowest HP ratio within range when no target selected
    }

    // predefined card priority, arranged based on FFLogs statistics current order.
    // https://www.fflogs.com/zone/statistics/62
    public static readonly string[] MeleeOrder = ["Viper", "Dragon", "Ninja", "Monk", "Reaper", "Samurai"];

    public static readonly string[] RangeOrder =
        ["Black Mage", "Pictomancer", "Red Mage", "Machinist", "Summoner", "Bard", "Dancer"];

    // list of target healing actions
    public static readonly uint[] TargetHealActions =
    [
        3594,  // benefic 1
        3610,  // benefic 2
        3614,  // essential dignity
        3595,  // aspected benefic
        37024, // arrow card (+10% recovery)
        37025, // spire card (400 barrier)
        37027, // bole card (-10% damage)
        37028, // ewer card (200 hot 15s)
        16556, // celestial intersection
        25873, // exaltation
    ];
}
