using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DailyRoutines.ModulesPublic;

public class AutoRecordSubTimeLeft : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = "自动记录剩余游戏时间",
        Description = "登录时, 自动记录保存当前账号剩余的游戏时间, 并显示在服务器信息栏",
        Category    = ModuleCategories.General,
        Author      = ["Due"]
    };

    public override ModulePermission Permission { get; } = new() { CNOnly = true };

    private static readonly CompSig AgentLobbyOnLoginSig = new("E8 ?? ?? ?? ?? 41 C6 45 ?? ?? E9 ?? ?? ?? ?? 83 FB 03");

    private unsafe delegate nint AgentLobbyOnLoginDelegate(AgentLobby* agent);

    private static Hook<AgentLobbyOnLoginDelegate>? AgentLobbyOnLoginHook;

    private static Config        ModuleConfig = null!;
    private static IDtrBarEntry? Entry;
    private static Tracker?      PlaytimeTracker;

    protected override unsafe void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new();

        PlaytimeTracker ??= new(Path.Join(ConfigDirectoryPath, "PlatimeData.log"));

        Entry         ??= DService.DtrBar.Get("DailyRoutines-GameTimeLeft");
        Entry.OnClick =   OnDTREntryClick;

        UpdateEntryAndTimeInfo();

        AgentLobbyOnLoginHook ??= AgentLobbyOnLoginSig.GetHook<AgentLobbyOnLoginDelegate>(AgentLobbyOnLoginDetour);
        AgentLobbyOnLoginHook.Enable();

        DService.ClientState.Login  += OnLogin;
        DService.ClientState.Logout += OnLogout;

        FrameworkManager.Reg(OnUpdate, throttleMS: 5_000);

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_CharaSelectRemain", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,            "_CharaSelectRemain", OnAddon);
    }

    protected override void ConfigUI()
    {
        var contentID = LocalPlayerState.ContentID;
        if (contentID == 0) return;

        if (!ModuleConfig.Infos.TryGetValue(contentID, out var info) ||
            info.Record == DateTime.MinValue                         ||
            (info.LeftMonth == TimeSpan.MinValue && info.LeftTime == TimeSpan.MinValue))
        {
            ImGui.TextColored(KnownColor.Orange.ToVector4(), "当前角色暂无数据, 请重新登录游戏以记录");
            return;
        }

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), "上次记录:");

        ImGui.SameLine();
        ImGui.Text($"{info.Record}");

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), "月卡剩余时间:");

        ImGui.SameLine();
        ImGui.Text(FormatTimeSpan(info.LeftMonth == TimeSpan.MinValue ? TimeSpan.Zero : info.LeftMonth));

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), "点卡剩余时间:");

        ImGui.SameLine();
        ImGui.Text(FormatTimeSpan(info.LeftTime));
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);

        FrameworkManager.Unreg(OnUpdate);

        Entry?.Remove();
        Entry = null;

        PlaytimeTracker?.Dispose();
        PlaytimeTracker = null;

        DService.ClientState.Login  -= OnLogin;
        DService.ClientState.Logout -= OnLogout;
    }

    private void OnLogin()
    {
        TaskHelper.Enqueue(() =>
        {
            var contentID = LocalPlayerState.ContentID;
            if (contentID == 0) return false;

            UpdateEntryAndTimeInfo(contentID);
            return true;
        });
    }

    private void OnUpdate(IFramework _) =>
        UpdateEntryAndTimeInfo();

    private void OnLogout(int code, int type) =>
        TaskHelper?.Abort();

    private unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (CharaSelectRemain == null) return;
        if (type == AddonEvent.PostDraw && !Throttler.Throttle("AutoRecordSubTimeLeft-OnAddonDraw")) return;

        var agent = AgentLobby.Instance();
        if (agent == null) return;

        var info = agent->LobbyData.LobbyUIClient.SubscriptionInfo;
        if (info == null) return;

        var contentID = agent->LobbyData.ContentId;
        if (contentID == 0) return;

        var timeInfo = GetLeftTimeSecond(*info);
        ModuleConfig.Infos[contentID]
            = new(DateTime.Now,
                  timeInfo.MonthTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.MonthTime),
                  timeInfo.PointTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.PointTime));
        ModuleConfig.Save(this);

        var textNode = CharaSelectRemain->GetTextNodeById(7);
        if (textNode != null)
        {
            textNode->SetPositionFloat(-20, 40);
            textNode->SetText($"剩余天数: {FormatTimeSpan(TimeSpan.FromSeconds(timeInfo.MonthTime))}\n" +
                              $"剩余时长: {FormatTimeSpan(TimeSpan.FromSeconds(timeInfo.PointTime))}");
        }

        UpdateEntryAndTimeInfo(contentID);
    }

    private unsafe nint AgentLobbyOnLoginDetour(AgentLobby* agent)
    {
        var ret = AgentLobbyOnLoginHook.Original(agent);
        UpdateSubInfo(agent);
        return ret;
    }

    private unsafe void UpdateSubInfo(AgentLobby* agent)
    {
        TaskHelper.Enqueue(() =>
        {
            try
            {
                var info = agent->LobbyData.LobbyUIClient.SubscriptionInfo;
                if (info == null) return false;

                var contentID = agent->LobbyData.ContentId;
                if (contentID == 0) return false;

                var timeInfo = GetLeftTimeSecond(*info);
                ModuleConfig.Infos[contentID]
                    = new(DateTime.Now,
                          timeInfo.MonthTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.MonthTime),
                          timeInfo.PointTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.PointTime));
                ModuleConfig.Save(this);

                UpdateEntryAndTimeInfo(contentID);
            }
            catch (Exception ex)
            {
                Warning("更新游戏点月卡订阅信息失败", ex);
                NotificationWarning(ex.Message, "更新游戏点月卡订阅信息失败");
            }

            return true;
        }, "更新订阅信息");
    }

    private static (int MonthTime, int PointTime) GetLeftTimeSecond(LobbySubscriptionInfo info)
    {
        var size = Marshal.SizeOf(info);
        var arr  = new byte[size];
        var ptr  = nint.Zero;

        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(info, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
        } finally { Marshal.FreeHGlobal(ptr); }

        var month = string.Join(string.Empty, arr.Skip(16).Take(3).Reverse().Select(x => x.ToString("X2")));
        var point = string.Join(string.Empty, arr.Skip(24).Take(3).Reverse().Select(x => x.ToString("X2")));
        return (Convert.ToInt32(month, 16), Convert.ToInt32(point, 16));
    }

    private void UpdateEntryAndTimeInfo(ulong contentID = 0)
    {
        if (DService.ClientState.IsLoggedIn)
            PlaytimeTracker.Start();
        else
            PlaytimeTracker.Stop();

        if (Entry == null) return;

        if (contentID == 0)
            contentID = LocalPlayerState.ContentID;

        if (contentID == 0                                           ||
            DService.Condition[ConditionFlag.InCombat]               ||
            !ModuleConfig.Infos.TryGetValue(contentID, out var info) ||
            info.Record == DateTime.MinValue                         ||
            (info.LeftMonth == TimeSpan.MinValue && info.LeftTime == TimeSpan.MinValue))
        {
            Entry.Shown = false;
            return;
        }

        var isMonth    = info.LeftMonth != TimeSpan.MinValue;
        var expireTime = info.Record + (isMonth ? info.LeftMonth : info.LeftTime);

        var query = new Query(Path.Join(ConfigDirectoryPath, "PlatimeData.log"));

        var textBuilder = new SeStringBuilder();
        textBuilder.AddUiForeground($"[{(isMonth ? "月卡" : "点卡")}] ", 25)
                   .AddText($"{expireTime:MM/dd HH:mm}");
        Entry.Text = textBuilder.Build();

        var tooltipBuilder = new SeStringBuilder();
        tooltipBuilder.AddUiForeground("[过期时间]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{expireTime}")
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[剩余时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(expireTime - DateTime.Now)}")
                      .Add(NewLinePayload.Payload)
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[本日游玩时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(query.GetTotalUsageBetween(DateTime.Today, TimeSpan.FromDays(1)))}")
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[昨日游玩时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(query.GetTotalUsageBetween(DateTime.Today - TimeSpan.FromDays(1), TimeSpan.FromDays(1)))}")
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[七日游玩时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(query.GetTotalUsageBetween(DateTime.Today - TimeSpan.FromDays(6), TimeSpan.FromDays(7)))}")
                      .Add(NewLinePayload.Payload)
                      .Add(NewLinePayload.Payload)
                      .AddText("(左键: ")
                      .AddUiForeground("模块配置界面", 34)
                      .AddText(")")
                      .Add(NewLinePayload.Payload)
                      .AddText("(右键: ")
                      .AddUiForeground("时长充值页面", 34)
                      .AddText(")");
        Entry.Tooltip = tooltipBuilder.Build();

        Entry.Shown = true;
    }

    private static void OnDTREntryClick(DtrInteractionEvent eventData)
    {
        switch (eventData.ClickType)
        {
            case MouseClickType.Left:
                ChatHelper.SendMessage($"/pdr search {nameof(AutoRecordSubTimeLeft)}");
                break;
            case MouseClickType.Right:
                Util.OpenLink("https://pay.sdo.com/item/GWPAY-100001900");
                break;
        }

    }

    private static string FormatTimeSpan(TimeSpan timeSpan) =>
        $"{timeSpan.Days} 天 {timeSpan.Hours} 小时 {timeSpan.Minutes} 分 {timeSpan.Seconds} 秒";

    private class Config : ModuleConfiguration
    {
        public Dictionary<ulong, (DateTime Record, TimeSpan LeftMonth, TimeSpan LeftTime)> Infos = [];
    }

    private static class UsageLogFileUtils
    {
        internal record UsageEvent(string SessionID, int ProcessID, string EventType, DateTime TsUtc);

        internal static IEnumerable<UsageEvent> ReadEvents(string path)
        {
            if (!File.Exists(path))
                yield break;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);

            while (sr.ReadLine() is { } line)
            {
                var parts = line.Split('\t');
                if (parts.Length == 4                   &&
                    int.TryParse(parts[1], out var pid) &&
                    DateTime.TryParse(parts[3], null, DateTimeStyles.RoundtripKind, out var ts))
                    yield return new UsageEvent(parts[0], pid, parts[2], ts);
            }
        }
    }

    private sealed class Tracker : IDisposable
    {
        private readonly string   logPath;
        private readonly string   sessionID         = Guid.NewGuid().ToString("N");
        private readonly int      processID         = Environment.ProcessId;
        private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan staleGrace        = TimeSpan.FromSeconds(90);
        private readonly Mutex    fileMutex;

        private int runState;

        private CancellationTokenSource? cancellation;
        private Task?                    heartbeatTask;

        public Tracker(string path)
        {
            logPath = path ?? throw new ArgumentNullException(nameof(path));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var mutexName = $@"Global\{nameof(Tracker)}-{GetStableHashCode(Path.GetFullPath(logPath).ToUpperInvariant())}";
            fileMutex = new Mutex(false, mutexName);

            AutoCloseStaleSessions();
        }

        private static uint GetStableHashCode(string str)
        {
            var hash = 2166136261;
            foreach (var c in str)
                hash = (hash * 16777619) ^ c;

            return hash;
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref runState, 1, 0) == 0)
            {
                cancellation = new CancellationTokenSource();
                WriteEvent("start", DateTime.UtcNow);

                var token = cancellation.Token;
                heartbeatTask = Task.Run(async () =>
                {
                    try
                    {
                        var timer = new PeriodicTimer(heartbeatInterval);
                        while (await timer.WaitForNextTickAsync(token))
                            WriteEvent("heartbeat", DateTime.UtcNow);
                    }
                    catch (OperationCanceledException) { }
                }, token);
            }
        }

        public void Stop()
        {
            if (Interlocked.CompareExchange(ref runState, 0, 1) == 1)
            {
                try
                {
                    cancellation?.Cancel();
                }
                catch
                {
                    // ignored
                }

                try
                {
                    heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // ignored
                }

                try
                {
                    WriteEvent("stop", DateTime.UtcNow);
                }
                catch
                {
                    // ignored
                }

                cancellation?.Dispose();
                cancellation  = null;
                heartbeatTask = null;
            }
        }

        public void Dispose()
        {
            Stop();
            fileMutex.Dispose();
        }

        private void WriteEvent(string type, DateTime utc)
        {
            var line = $"{sessionID}\t{processID}\t{type}\t{utc:o}" + Environment.NewLine;

            try
            {
                fileMutex.WaitOne();
                File.AppendAllText(logPath, line);
            } finally { fileMutex.ReleaseMutex(); }
        }

        private void AutoCloseStaleSessions()
        {
            var sessionStates = new Dictionary<string, (DateTime LastEvent, bool HasStop)>();

            try
            {
                foreach (var e in UsageLogFileUtils.ReadEvents(logPath))
                {
                    sessionStates.TryGetValue(e.SessionID, out var currentState);

                    var isStopEvent = e.EventType is "stop" or "autoClose";

                    if (e.TsUtc > currentState.LastEvent)
                        currentState.LastEvent = e.TsUtc;

                    if (isStopEvent)
                        currentState.HasStop = true;

                    sessionStates[e.SessionID] = currentState;
                }
            }
            catch (IOException) { return; }

            var now         = DateTime.UtcNow;
            var eventsToAdd = new List<string>();

            foreach (var session in sessionStates)
            {
                if (session.Value.HasStop) continue;

                var endUtc = session.Value.LastEvent + staleGrace;
                if (endUtc < now)
                    eventsToAdd.Add($"{session.Key}\t-1\tautoClose\t{endUtc:o}");
            }

            if (eventsToAdd.Count > 0)
            {
                try
                {
                    fileMutex.WaitOne();
                    File.AppendAllLines(logPath, eventsToAdd);
                } finally { fileMutex.ReleaseMutex(); }
            }
        }
    }

    private sealed class Query
    {
        private readonly string   logPath;
        private readonly bool     exists;
        private readonly TimeSpan staleGrace = TimeSpan.FromSeconds(90);

        public Query(string path)
        {
            logPath = path ?? throw new ArgumentNullException(nameof(path));
            exists  = File.Exists(logPath);
        }

        public TimeSpan GetTotalUsageSince(TimeSpan lookback)
        {
            if (!exists || lookback <= TimeSpan.Zero) return TimeSpan.Zero;
            var endLocal   = DateTime.Now;
            var startLocal = endLocal - lookback;
            return GetTotalUsageBetween(startLocal, lookback);
        }

        public TimeSpan GetTotalUsageBetween(DateTime startLocal, TimeSpan span)
        {
            if (!exists || span <= TimeSpan.Zero) return TimeSpan.Zero;

            var tz       = TimeZoneInfo.Local;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Local), tz);
            var endLocal = startLocal + span;
            var endUtc   = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Local), tz);

            var intervals = BuildUsageIntervalsUtc();
            var total     = IntersectSum(intervals, startUtc, endUtc);
            return total;
        }

        private List<(DateTime BeginUtc, DateTime EndUtc)> BuildUsageIntervalsUtc()
        {
            var events = UsageLogFileUtils.ReadEvents(logPath).ToList();
            events.Sort((a, b) =>
            {
                var sidCompare = string.Compare(a.SessionID, b.SessionID, StringComparison.Ordinal);
                return sidCompare != 0 ? sidCompare : DateTime.Compare(a.TsUtc, b.TsUtc);
            });

            var       result        = new List<(DateTime, DateTime)>();
            string?   curSid        = null;
            DateTime? lastPoint     = null;
            DateTime? lastEventTime = null;

            foreach (var e in events)
            {
                if (e.SessionID != curSid)
                {
                    if (curSid != null && lastPoint != null && lastEventTime != null)
                    {
                        var inferredEnd = MinUtc(DateTime.UtcNow, lastEventTime.Value + staleGrace);
                        if (inferredEnd > lastPoint.Value)
                            result.Add((lastPoint.Value, inferredEnd));
                    }

                    curSid        = e.SessionID;
                    lastPoint     = null;
                    lastEventTime = null;
                }

                switch (e.EventType)
                {
                    case "start" or "heartbeat":
                    {
                        if (lastPoint != null)
                            result.Add((lastPoint.Value, e.TsUtc));

                        lastPoint = e.TsUtc;

                        lastEventTime = e.TsUtc;
                        break;
                    }
                    case "stop":
                    case "autoClose":
                    {
                        if (lastPoint != null && e.TsUtc > lastPoint.Value)
                            result.Add((lastPoint.Value, e.TsUtc));

                        lastPoint     = null;
                        lastEventTime = e.TsUtc;
                        break;
                    }
                }
            }

            if (curSid != null && lastPoint != null && lastEventTime != null)
            {
                var inferredEnd = MinUtc(DateTime.UtcNow, lastEventTime.Value + staleGrace);
                if (inferredEnd > lastPoint.Value)
                    result.Add((lastPoint.Value, inferredEnd));
            }

            return result;
        }

        private static TimeSpan IntersectSum(List<(DateTime BeginUtc, DateTime EndUtc)> intervals, DateTime windowBeginUtc, DateTime windowEndUtc)
        {
            if (windowEndUtc <= windowBeginUtc) return TimeSpan.Zero;

            long totalTicks = 0;
            foreach (var iv in intervals)
            {
                var a = iv.BeginUtc;
                var b = iv.EndUtc;
                if (b <= windowBeginUtc || a >= windowEndUtc) continue;

                var s = a > windowBeginUtc ? a : windowBeginUtc;
                var e = b < windowEndUtc ? b : windowEndUtc;
                if (e > s)
                    totalTicks += (e - s).Ticks;
            }

            return new TimeSpan(totalTicks);
        }

        private static DateTime MinUtc(DateTime a, DateTime b) => a <= b ? a : b;
    }
}
