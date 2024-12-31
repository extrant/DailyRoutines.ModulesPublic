#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Party;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using LuminaAction = Lumina.Excel.GeneratedSheets.Action;
using FFXIVClientStructs.FFXIV.Client.System.Memory;

namespace DailyRoutines.Modules;

public class ASTHelper : DailyModuleBase
{
    #region Core

    public override ModuleInfo Info => new()
    {
        Author      = ["HaKu"],
        Title       = GetLoc("ASTHelperTitle"),
        Description = GetLoc("ASTHelperDescription"),
        Category    = ModuleCategories.Action
    };

    private static Config? ModuleConfig;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();

        // task helper for select candidates
        TaskHelper ??= new TaskHelper { TimeLimitMS = 20_000 };

        // mark nodes
        MarkIsBuild = false;

        // life cycle hooks
        UseActionManager.Register(OnPreUseAction);
        DService.ClientState.TerritoryChanged += OnZoneChanged;
        DService.DutyState.DutyRecommenced    += OnDutyRecommenced;
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "_PartyList", OnPartyListPostDraw);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_PartyList", OnPartyListPreFinalize);
    }

    public override unsafe void Uninit()
    {
        UseActionManager.Unregister(OnPreUseAction);
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        DService.DutyState.DutyRecommenced    -= OnDutyRecommenced;
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PostDraw, OnPartyListPostDraw);
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, OnPartyListPreFinalize);

        ResetPartyList();
        ReleaseImagesNodes();

        base.Uninit();
    }

    public override void ConfigUI()
    {
        // auto play card
        ImGui.TextColored(LightSkyBlue, GetLoc("ASTHelper-AutoPlayCardTitle"));
        ImGuiOm.HelpMarker(GetLoc("ASTHelper-AutoPlayCardDescription", LuminaCache.GetRow<LuminaAction>(17055).Name.ExtractText()));

        ImGui.Spacing();

        using (ImRaii.PushIndent())
        {
            if (ImGui.RadioButton($"{GetLoc("Disable")}",
                                  ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Disable))
            {
                ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Disable;
                SaveConfig(ModuleConfig);
            }

            if (ImGui.RadioButton($"{GetLoc("Common")} ({GetLoc("ASTHelper-AutoPlayCard-CommonDescription")})",
                                  ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Default))
            {
                ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Default;
                SaveConfig(ModuleConfig);
            }

            if (ImGui.RadioButton($"{GetLoc("Advance")} ({GetLoc("ASTHelper-AutoPlayCard-AdvanceDescription")})",
                                  ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance))
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
                ImGui.TextColored(LightGoldenrod, "FFLogs V1 API Key");

                ImGui.Spacing();

                if (ImGui.InputText("##FFLogsAPIKey", ref ModuleConfig.FFLogsAPIKey, 32))
                    SaveConfig(ModuleConfig);

                ImGui.SameLine();
                if (ImGui.Button(GetLoc("Save")))
                {
                    if (string.IsNullOrWhiteSpace(ModuleConfig.FFLogsAPIKey) || ModuleConfig.FFLogsAPIKey.Length != 32)
                    {
                        ModuleConfig.KeyValid = false;
                        SaveConfig(ModuleConfig);
                    }
                    else
                        DService.Framework.RunOnTick(async () => await CheckKeyStatus());
                }

                // key status (valid or invalid)
                ImGui.Spacing();

                ImGui.AlignTextToFramePadding();
                ImGui.Text(GetLoc("ASTHelper-LogsApi-Status"));

                ImGui.SameLine();
                if (ModuleConfig.KeyValid)
                    ImGui.TextColored(LightGreen, GetLoc("Connected"));
                else
                    ImGui.TextColored(LightPink, GetLoc("Disconnected"));
            }
        }

        ImGui.NewLine();

        // easy heal
        ImGui.TextColored(LightSkyBlue, GetLoc("ASTHelper-EasyHealTitle"));
        ImGuiOm.HelpMarker(GetLoc("ASTHelper-EasyHealDescription"));

        ImGui.Spacing();

        using (ImRaii.PushIndent())
        {
            if (ImGui.RadioButton($"{GetLoc("Disable")}",
                                  ModuleConfig.EasyHeal == EasyHealStatus.Disable))
            {
                ModuleConfig.EasyHeal = EasyHealStatus.Disable;
                SaveConfig(ModuleConfig);
            }

            if (ImGui.RadioButton($"{GetLoc("Enable")} ({GetLoc("ASTHelper-EasyHeal-EnableDescription")})",
                                  ModuleConfig.EasyHeal == EasyHealStatus.Enable))
            {
                ModuleConfig.EasyHeal = EasyHealStatus.Enable;
                SaveConfig(ModuleConfig);
            }

            // heal threshold
            if (ModuleConfig.EasyHeal == EasyHealStatus.Enable)
            {
                ImGui.Spacing();

                ImGui.TextColored(LightGreen, GetLoc("ASTHelper-EasyHeal-HealThreshold"));
                ImGuiOm.HelpMarker(GetLoc("ASTHelper-EasyHeal-HealThresholdHelp"));

                ImGui.Spacing();

                if (ImGui.SliderFloat("##HealThreshold", ref ModuleConfig.NeedHealThreshold, 0.0f, 1.0f, "%.2f"))
                    SaveConfig(ModuleConfig);

                // all time heal warning
                if (ModuleConfig.NeedHealThreshold > 0.92f)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(Orange, GetLoc("ASTHelper-EasyHeal-OverhealWarning"));
                }
            }
        }

        ImGui.NewLine();
        
        using (ImRaii.PushIndent())
        {
            if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
                SaveConfig(ModuleConfig);

            if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
                SaveConfig(ModuleConfig);

            if (ImGui.Checkbox($"{GetLoc("Show")}{GetLoc("Mark")}", ref ModuleConfig.OverlayMark))
                SaveConfig(ModuleConfig);

            if (ModuleConfig.OverlayMark)
            {
                ImGui.Spacing();

                ImGui.TextColored(LightSalmon, GetLoc("MarkerInPartyList-IconScale"));
                ImGui.SameLine();
                if (ImGui.SliderFloat("##MarkScale", ref ModuleConfig.MarkScale, 0.1f, 1.0f, "%.2f"))
                {
                    SaveConfig(ModuleConfig);
                    NewMark(0, LuminaCache.GetRow<LuminaAction>(37023).Icon);
                    DService.Framework.RunOnTick(async () => await PreviewTimer(() => DelMark(0), 6000));
                    RefreshMarks();
                }

                ImGui.TextColored(LightSalmon, GetLoc("MarkerInPartyList-IconOffset"));
                ImGui.SameLine();
                if (ImGui.SliderFloat2("##MarkMargin", ref ModuleConfig.MarkOffset, -50f, 50f, "%.2f"))
                {
                    SaveConfig(ModuleConfig);
                    NewMark(0, LuminaCache.GetRow<LuminaAction>(37023).Icon);
                    DService.Framework.RunOnTick(async () => await PreviewTimer(() => DelMark(0), 6000));
                    RefreshMarks();
                }
            }
        }
    }

    #endregion

    #region Hooks

    // hook before play card & target heal
    private static void OnPreUseAction(
        ref bool  isPrevented, ref ActionType type,     ref uint actionID,
        ref ulong targetID,    ref Vector3    location, ref uint extraParam)
    {
        if (type != ActionType.Action || DService.PartyList.Length == 0) return;

        // auto play card
        if (actionID is (37023 or 37026) && ModuleConfig.AutoPlayCard != AutoPlayCardStatus.Disable)
        {
            var partyMemberIds = DService.PartyList.Select(m => m.ObjectId).ToHashSet();
            if (!partyMemberIds.Contains((uint)targetID))
            {
                targetID = actionID switch
                {
                    37023 => FetchCandidateId("Melee"),
                    37026 => FetchCandidateId("Range"),
                };

                var member = FetchMember((uint)targetID);
                if (member != null)
                {
                    var name         = member.Name.ExtractText();
                    var classJobIcon = member.ClassJob.GameData.ToBitmapFontIcon();
                    var classJobName = member.ClassJob.GameData.Name.ExtractText();

                    var locKey = actionID switch
                    {
                        37023 => "Melee",
                        37026 => "Range",
                    };

                    if (ModuleConfig.SendChat)
                        Chat(GetSLoc($"ASTHelper-AutoPlayCard-Message-{locKey}", name, classJobIcon, classJobName));
                    if (ModuleConfig.SendNotification)
                        NotificationInfo(GetLoc($"ASTHelper-AutoPlayCard-Message-{locKey}", name, string.Empty, classJobName));
                }
            }
        }

        // easy heal
        if (ModuleConfig.EasyHeal == EasyHealStatus.Enable && TargetHealActions.Contains(actionID))
        {
            var partyMemberIds = DService.PartyList.Select(m => m.ObjectId).ToHashSet();
            if (!partyMemberIds.Contains((uint)targetID))
            {
                // find target with the lowest HP ratio within range and satisfy threshold
                targetID = TargetNeedHeal();
                if (targetID == UnspecificTargetId)
                {
                    isPrevented = true;
                    return;
                }

                var member = FetchMember((uint)targetID);
                if (member != null)
                {
                    var name         = member.Name.ExtractText();
                    var classJobIcon = member.ClassJob.GameData.ToBitmapFontIcon();
                    var classJobName = member.ClassJob.GameData.Name.ExtractText();

                    if (ModuleConfig.SendChat)
                        Chat(GetSLoc("ASTHealer-EasyHeal-Message", name, classJobIcon, classJobName));
                    if (ModuleConfig.SendNotification)
                        NotificationInfo(GetLoc("ASTHealer-EasyHeal-Message", name, string.Empty, classJobName));
                    if (ModuleConfig.OverlayMark)
                    {
                        var idx = FetchMemberIndex((uint)targetID) ?? 0;
                        NewMark(idx, LuminaCache.GetRow<LuminaAction>(actionID).Icon);
                        DService.Framework.RunOnTick(async () => await MarkTimer(() => DelMark(idx), 6000));
                    }
                }
            }
        }
    }

    private void OnZoneChanged(ushort zone)
    {
        MemberBestRecords.Clear();
        ZoneId = zone;
    }

    private void OnDutyRecommenced(object? sender, ushort e)
        => OrderCandidates();

    // fetch member information by id
    private static IPartyMember? FetchMember(uint id)
        => DService.PartyList.FirstOrDefault(m => m.ObjectId == id);

    // fetch member index in ui
    private static unsafe uint? FetchMemberIndex(uint id)
        => (uint)AgentModule.Instance()->GetAgentHUD()->PartyMembers.ToArray()
                                                                    .Select((m, i) => (m, i))
                                                                    .FirstOrDefault(t => t.m.EntityId == id).i;

    private static bool NotifyErrorOnce = true;

    public static unsafe void OnPartyListPostDraw(AddonEvent type, AddonArgs args)
    {
        // build marks
        lock (Lock)
            if (ModuleConfig.OverlayMark && !MarkIsBuild)
                BuildImagesNodes();

        // clear all marks
        if (MarkNeedClear && MarkedStatus.Count is 0)
        {
            ResetPartyList((AtkUnitBase*)args.Addon);
            MarkNeedClear = false;
        }

        // party member changed?
        try
        {
            if (DService.PartyList.Length is not 0)
            {
                // need to update candidates?
                var ids = DService.PartyList.Select(m => m.ObjectId).ToHashSet();
                if (!ids.SetEquals(PartyMemberIdsCache))
                {
                    // party member changed, update candidates
                    OrderCandidates();
                    PartyMemberIdsCache = ids;
                }

                // draw marks
                if (ModuleConfig.OverlayMark)
                {
                    // melee
                    var meleeId  = FetchCandidateId("Melee");
                    var meleeIdx = FetchMemberIndex(meleeId) ?? 0;
                    if (meleeIdx != MeleeCandidateIdxCache)
                    {
                        DelMark(MeleeCandidateIdxCache);
                        NewMark(meleeIdx, LuminaCache.GetRow<LuminaAction>(37023).Icon);
                        MeleeCandidateIdxCache = meleeIdx;
                    }

                    // range
                    var rangeId  = FetchCandidateId("Range");
                    var rangeIdx = FetchMemberIndex(rangeId) ?? 0;
                    if (rangeIdx != RangeCandidateIdxCache)
                    {
                        DelMark(RangeCandidateIdxCache);
                        NewMark(rangeIdx, LuminaCache.GetRow<LuminaAction>(37026).Icon);
                        RangeCandidateIdxCache = rangeIdx;
                    }
                }
            }
            else
            {
                // no party member, clear all marks
                RefreshMarks();
            }
        }
        catch (Exception)
        {
            if (NotifyErrorOnce)
            {
                Chat(GetLoc("ASTHelper-Error"));
                NotifyErrorOnce = false;
            }
        }
    }

    public static void OnPartyListPreFinalize(AddonEvent type, AddonArgs args)
        => ReleaseImagesNodes();

    #endregion

    #region AutoPlayCard

    private const uint UnspecificTargetId = 0xE000_0000;

    private static ushort        ZoneId;
    private static HashSet<uint> PartyMemberIdsCache = new(); // check party member changed or not

    private static readonly List<(uint id, double DPS)> MeleeCandidateOrder = new();
    private static readonly List<(uint id, double DPS)> RangeCandidateOrder = new();
    private static          uint                        MeleeCandidateIdxCache;
    private static          uint                        RangeCandidateIdxCache;

    private static void OrderCandidates()
    {
        // reset candidates before select new candidates
        MeleeCandidateOrder.Clear();
        RangeCandidateOrder.Clear();

        // find card candidates
        var partyList = DService.PartyList; // role [0 tank, 2 melee, 3 range, 4 healer]
        if (partyList.Length is 0 || DService.ClientState.LocalPlayer.ClassJob.GameData.Abbreviation != "AST" || ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Disable)
            return;

        // advance fallback when no valid zone id or invalid key
        if (!Dal2LogsZoneMap.ContainsKey(ZoneId) || ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance)
        {
            if (!ModuleConfig.KeyValid)
            {
                if (FirstTimeFallback)
                {
                    Chat(GetLoc("ASTHealer-AutoPlayCard-AdvanceFallback"));
                    FirstTimeFallback = false;
                }

                ModuleConfig.AutoPlayCard = AutoPlayCardStatus.Default;
            }
        }

        // select the best candidate for melee and range according to rdps
        if (ModuleConfig.AutoPlayCard == AutoPlayCardStatus.Advance)
        {
            foreach (var member in partyList)
            {
                var bestRecord = FetchBestLogsRecord(ZoneId, member).GetAwaiter().GetResult();
                if (bestRecord is null) continue;

                switch (member.ClassJob.GameData.Role)
                {
                    case 2:
                        MeleeCandidateOrder.Add((member.ObjectId, bestRecord.DPS));
                        break;
                    case 3:
                        RangeCandidateOrder.Add((member.ObjectId, bestRecord.DPS));
                        break;
                }
            }

            MeleeCandidateOrder.Sort((a, b) => b.DPS.CompareTo(a.DPS));
            RangeCandidateOrder.Sort((a, b) => b.DPS.CompareTo(a.DPS));
        }

        // fallback: select candidates according to default order
        foreach (var meleeJob in MeleeOrder)
        {
            var member = partyList.FirstOrDefault(m => m.ClassJob.GameData.NameEnglish == meleeJob);
            if (member is not null && MeleeCandidateOrder.All(m => m.id != member.ObjectId))
                MeleeCandidateOrder.Add((member.ObjectId, 0));
        }

        foreach (var rangeJob in RangeOrder)
        {
            var member = partyList.FirstOrDefault(m => m.ClassJob.GameData.NameEnglish == rangeJob);
            if (member is not null && RangeCandidateOrder.All(m => m.id != member.ObjectId))
                RangeCandidateOrder.Add((member.ObjectId, 0));
        }

        // finally fallback: select the first and last member
        if (MeleeCandidateOrder.Count is 0)
            MeleeCandidateOrder.Add((partyList.First().ObjectId, -5));

        if (RangeCandidateOrder.Count is 0)
            RangeCandidateOrder.Add((partyList.Last().ObjectId, -5));
    }

    private static uint FetchCandidateId(string role)
    {
        var candidates = role switch
        {
            "Melee" => MeleeCandidateOrder,
            "Range" => RangeCandidateOrder,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

        var needResort = false;
        for (var i = 0; i < candidates.Count; i++)
        {
            var member    = candidates[i];
            var candidate = DService.PartyList.FirstOrDefault(m => m.ObjectId == member.id);
            if (candidate is null) continue;

            // skip dead member in this round (refresh on duty recommenced)
            if (candidate.CurrentHP <= 0)
            {
                switch (role)
                {
                    case "Melee":
                        MeleeCandidateOrder[i] = (candidate.ObjectId, -2);
                        break;
                    case "Range":
                        RangeCandidateOrder[i] = (candidate.ObjectId, -2);
                        break;
                }

                needResort = true;
                continue;
            }

            // skip member out of range for this action
            if (Vector3.Distance(candidate.Position, DService.ClientState.LocalPlayer.Position) > 30)
                continue;


            return member.id;
        }

        if (needResort)
            candidates.Sort((a, b) => b.DPS.CompareTo(a.DPS));

        return DService.ClientState.LocalPlayer.EntityId;
    }

    #endregion

    #region EasyHeal

    private static uint TargetNeedHeal()
    {
        var partyList  = DService.PartyList;
        var lowRatio   = 2f;
        var needHealId = UnspecificTargetId;
        foreach (var member in partyList)
        {
            if (member.CurrentHP <= 0 || Vector3.Distance(member.Position, DService.ClientState.LocalPlayer.Position) > 30)
                continue;

            var ratio = member.CurrentHP / (float)member.MaxHP;
            if (ratio < lowRatio && ratio <= ModuleConfig.NeedHealThreshold)
            {
                lowRatio   = ratio;
                needHealId = member.ObjectId;
            }
        }

        return needHealId;
    }

    #endregion

    #region FFLogs

    // FFLogs related (auto play card advance mode)
    private static readonly HttpClient Client    = new();
    private const           string     FFLogsUri = "https://www.fflogs.com/v1";

    private static Dictionary<uint, LogsRecord> MemberBestRecords = new Dictionary<uint, LogsRecord>();

    // warning log
    private static bool FirstTimeFallback = true;

    private async Task CheckKeyStatus()
    {
        try
        {
            var uri      = $"{FFLogsUri}/classes?api_key={ModuleConfig.FFLogsAPIKey}";
            var response = await Client.GetStringAsync(uri);
            ModuleConfig.KeyValid = !string.IsNullOrWhiteSpace(response);
            FirstTimeFallback     = true; // only notify once per exec time
            SaveConfig(ModuleConfig);
        }
        catch (Exception)
        {
            ModuleConfig.KeyValid = false;
            SaveConfig(ModuleConfig);
        }
    }

    private static string GetRegion()
    {
        return DService.ClientState.LocalPlayer.CurrentWorld.GameData.DataCenter.Value.Region switch
        {
            1 => "JP",
            2 => "NA",
            3 => "EU",
            4 => "OC",
            5 => "CN",
            _ => string.Empty
        };
    }

    private static async Task<LogsRecord?> FetchBestLogsRecord(ushort zone, IPartyMember member)
    {
        // find in cache
        if (MemberBestRecords.TryGetValue(member.ObjectId, out var bestRecord))
            return bestRecord;

        // get character info
        var charaName  = member.Name;
        var serverSlug = member.World.GameData.Name;
        var region     = GetRegion();
        var job        = member.ClassJob.GameData.NameEnglish;

        // fetch record
        try
        {
            var uri   = $"{FFLogsUri}/parses/character/{charaName}/{serverSlug}/{region}";
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["api_key"]   = ModuleConfig.FFLogsAPIKey;
            query["metric"]    = "ndps";
            query["encounter"] = Dal2LogsZoneMap[zone].ToString();

            // contains all ultimates and current savage in current patch
            var response = await Client.GetStringAsync($"{uri}?{query}");
            var records  = JsonConvert.DeserializeObject<LogsRecord[]>(response);
            if (records == null || records.Length == 0) return null;

            // find best record
            bestRecord = records.Where(r => r.JobName == job)
                                .OrderByDescending(r => r.Difficulty)
                                .ThenByDescending(r => r.DPS)
                                .FirstOrDefault();
            MemberBestRecords[member.ObjectId] = bestRecord;
            return bestRecord;
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion

    #region Timer

    private static CancellationTokenSource CTS = new();

    private static async Task PreviewTimer(Action onElapsed, int delay)
    {
        CTS.Cancel();
        CTS = new CancellationTokenSource();
        var token = CTS.Token;

        try
        {
            await Task.Delay(delay, token);
            if (!token.IsCancellationRequested)
                onElapsed();
        }
        catch (TaskCanceledException) { }
    }

    private static async Task MarkTimer(Action onElapsed, int delay)
    {
        await Task.Delay(delay);
        onElapsed();
    }

    #endregion

    #region ImageNode

    private static readonly List<nint>               ImageNodes   = new(8);
    private static readonly Dictionary<ushort, uint> MarkedStatus = new();

    private static readonly object Lock = new();
    private static          bool   MarkIsBuild;
    private static          bool   MarkNeedClear;

    private static unsafe AtkImageNode* CreateImageNode()
    {
        var node = (AtkImageNode*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkImageNode), 8);
        if (node is null) return null;
        IMemorySpace.Memset(node, 0, (ulong)sizeof(AtkImageNode));
        node->Ctor();

        node->AtkResNode.Type      = NodeType.Image;
        node->AtkResNode.NodeFlags = NodeFlags.AnchorLeft | NodeFlags.AnchorTop;
        node->AtkResNode.DrawFlags = 0;

        node->WrapMode =  1;
        node->Flags    |= (byte)ImageNodeFlags.AutoFit;

        var partsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);
        if (partsList is null)
        {
            node->AtkResNode.Destroy(true);
            return null;
        }

        partsList->Id        = 0;
        partsList->PartCount = 1;

        var part = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPart), 8);
        if (part is null)
        {
            IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
            node->AtkResNode.Destroy(true);
            return null;
        }

        part->U      = 0;
        part->V      = 0;
        part->Width  = 80;
        part->Height = 80;

        partsList->Parts = part;

        var asset = (AtkUldAsset*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldAsset), 8);
        if (asset is null)
        {
            IMemorySpace.Free(part, (ulong)sizeof(AtkUldPart));
            IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
            node->AtkResNode.Destroy(true);
            return null;
        }

        asset->Id = 0;
        asset->AtkTexture.Ctor();

        part->UldAsset = asset;

        node->PartsList = partsList;
        node->LoadIconTexture(61201, 0);
        node->AtkResNode.SetPriority(5);
        return node;
    }

    private static unsafe void ShowImageNode(uint idx, ushort iconId)
    {
        if (idx > 7 || PartyList is null || ImageNodes.Count <= idx) return;

        var node = (AtkImageNode*)ImageNodes[(int)idx];
        if (node is null) return;

        var anchor = PartyList->GetNodeById(10 + idx);
        if (anchor is null) return;

        var pos = new Vector2(anchor->X, anchor->Y) + ModuleConfig.MarkOffset;
        node->AtkResNode.SetPositionFloat(pos.X, pos.Y);

        var size = (ushort)(anchor->Height * ModuleConfig.MarkScale);

        node->AtkResNode.SetWidth(size);
        node->AtkResNode.SetHeight(size);

        node->LoadIconTexture(iconId, 0);
        node->ToggleVisibility(true);

        ModifyPartyMember(PartyList, false);
    }

    private static unsafe void HideImageNode(uint idx)
    {
        if (idx > 7 || PartyList is null || ImageNodes.Count <= idx) return;

        var node = (AtkImageNode*)ImageNodes[(int)idx];
        if (node is null) return;

        node->ToggleVisibility(false);
    }

    private static unsafe void BuildImagesNodes()
    {
        if (PartyList is null) return;

        lock (Lock)
        {
            if (MarkIsBuild) return;

            foreach (var idx in Enumerable.Range(10, 8))
            {
                var node = CreateImageNode();
                if (node is null) continue;

                node->AtkResNode.NodeId = 202412;
                ImageNodes.Add((nint)node);

                LinkNodeAtEnd((AtkResNode*)node, PartyList);
            }

            MarkIsBuild = true;
        }
    }

    private static unsafe void ReleaseImagesNodes()
    {
        lock (Lock)
        {
            if (!MarkIsBuild) return;
            if (PartyList is null || PartyList->UldManager.LoadedState is not AtkLoadState.Loaded) return;

            foreach (var node in ImageNodes)
                UnlinkAndFreeImageNode((AtkImageNode*)node, PartyList);
            ImageNodes.Clear();
        }
    }

    private static unsafe void ResetPartyList(AtkUnitBase* partyList = null)
    {
        if (partyList is null)
            partyList = PartyList;

        if (partyList is not null && partyList->UldManager.LoadedState is AtkLoadState.Loaded)
            ModifyPartyMember(partyList, true);
    }

    private static unsafe void ModifyPartyMember(AtkUnitBase* partyList, bool visible)
    {
        if (partyList is null && !visible) return;

        foreach (var idx in Enumerable.Range(10, 8))
        {
            var member = partyList->GetNodeById((uint)idx);
            if (member is null || member->GetComponent() is null) continue;
            if (!member->IsVisible()) break;

            var textNode = member->GetComponent()->UldManager.SearchNodeById(15);
            if (textNode is null && textNode->IsVisible() != visible)
                textNode->ToggleVisibility(visible);
        }
    }

    private static void NewMark(uint idx, ushort iconId)
    {
        MarkedStatus[iconId] = idx;
        ShowImageNode(idx, iconId);
        MarkNeedClear = false;
    }

    private static void DelMark(uint idx)
    {
        if (MarkedStatus.ContainsValue(idx))
        {
            MarkedStatus.Remove(MarkedStatus.First(x => x.Value == idx).Key);
            HideImageNode(idx);
        }

        if (MarkedStatus.Count is 0)
            MarkNeedClear = true;
    }

    private static void RefreshMarks()
    {
        foreach (var (iconId, idx) in MarkedStatus)
        {
            DelMark(idx);
            if (idx < DService.PartyList.Length || (DService.PartyList.Length is 0 && idx is 0))
                NewMark(idx, iconId);
        }
    }

    #endregion


    #region Structs

    private class Config : ModuleConfiguration
    {
        // auto play card
        public AutoPlayCardStatus AutoPlayCard = AutoPlayCardStatus.Default;

        // FFLogs API Key v1 for fetching records (auto play card advance mode)
        public string FFLogsAPIKey = string.Empty;
        public bool   KeyValid;

        // easy heal
        public EasyHealStatus EasyHeal          = EasyHealStatus.Enable;
        public float          NeedHealThreshold = 0.8f;

        // notification
        public bool SendChat;
        public bool SendNotification = true;
        public bool OverlayMark;

        // use overlay to mark candidate if OverlayMark is true
        public float   MarkScale  = 0.33f;
        public Vector2 MarkOffset = new(-24, 40);
    }

    // Dalamud-FFLogs zone match map (ultimates and current savage)
    public static readonly Dictionary<uint, uint> Dal2LogsZoneMap = new()
    {
        // ultimates
        [733]  = 1073, // Bahamut f1bz
        [777]  = 1074, // Weapon w1fz
        [887]  = 1075, // Alexander d2az
        [968]  = 1076, // Dragonsong r1fz
        [1122] = 1077, // Omega z3oz
        // m1-4s
        [1226] = 93,
        [1228] = 94,
        [1230] = 95,
        [1232] = 96,
    };

    public class LogsRecord
    {
        // job english name
        [JsonProperty("spec")]
        public string JobName { get; set; }

        // record difficulty
        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        // rDPS
        [JsonProperty("total")]
        public double DPS { get; set; }
    }

    public enum AutoPlayCardStatus
    {
        Disable, // disable auto play card
        Default, // select target based on predefined order when no target selected
        Advance  // select target based on FFLogs rDPS records when no target selected
    }

    public enum EasyHealStatus
    {
        Disable, // disable easy heal
        Enable   // select target with the lowest HP ratio within range when no target selected
    }

    // predefined card priority, arranged based on FFLogs statistics current order.
    // https://www.fflogs.com/zone/statistics/62
    public static readonly string[] MeleeOrder = ["Viper", "Dragon", "Ninja", "Monk", "Reaper", "Samurai"];

    public static readonly string[] RangeOrder =
        ["Black Mage", "Pictomancer", "Red Mage", "Machinist", "Summoner", "Bard", "Dancer"];

    // list of target healing actions
    public static readonly uint[] TargetHealActions =
    [
        3594, // benefic 1
        3610, // benefic 2
        3614, // essential dignity
        3595, // aspected benefic
        // 37024, // arrow card (+10% recovery)
        37025, // spire card (400 barrier)
        // 37027, // bole card (-10% damage)
        37028, // ewer card (200 hot 15s)
        16556, // celestial intersection
        // 25873  // exaltation
    ];

    #endregion
}

#endif
