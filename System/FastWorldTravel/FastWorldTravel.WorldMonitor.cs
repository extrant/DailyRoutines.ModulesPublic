using System.Collections.Concurrent;
using System.Threading.Channels;
using OmenTools.Dalamud;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public partial class FastWorldTravel
{
    private class WorldMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<uint, CancellationTokenSource> activeMonitors = [];

        private readonly Func<uint, (bool, uint)> checkLogicFunc;
        private readonly Channel<uint>            requestChannel = Channel.CreateUnbounded<uint>();

        private readonly CancellationTokenSource serviceCts = new();

        private bool disposed;

        public WorldMonitor
        (
            Func<uint, (bool, uint)> checkLogic
        )
        {
            checkLogicFunc = checkLogic;
            _              = ProcessChannelRequestsAsync(serviceCts.Token);
        }

        public bool JustGo { get; set; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            requestChannel.Writer.TryComplete();
            serviceCts.Cancel();

            foreach (var kvp in activeMonitors)
            {
                try
                {
                    kvp.Value.Cancel();
                }
                catch
                {
                    // ignored
                }
            }

            activeMonitors.Clear();
            serviceCts.Dispose();
        }

        public void AddMonitor
        (
            uint dcID
        )
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(WorldMonitor));

            if (activeMonitors.ContainsKey(dcID) || serviceCts.IsCancellationRequested)
                return;

            if (requestChannel.Writer.TryWrite(dcID))
            {
                DLog.Debug($"[FastWorldTravel] 开始监控 {LuminaWrapper.GetDataCenterName(dcID)} ({dcID}) 大区状态");
                NotifyHelper.Instance().Chat
                    ($"[{Lang.Get("FastWorldTravelTitle")}]\n开始实时监控 [{LuminaWrapper.GetDataCenterName(dcID)}] 大区可通行状态\n检测到可通行时: [{(JustGo ? "直接前往" : "发送通知")}]");
            }
        }

        public void RemoveMonitor
        (
            uint dcID
        )
        {
            if (disposed) return;

            if (activeMonitors.TryRemove(dcID, out var cts))
            {
                DLog.Debug($"[FastWorldTravel] 停止监控 {LuminaWrapper.GetDataCenterName(dcID)} ({dcID}) 大区状态");
                NotifyHelper.Instance().Chat($"[{Lang.Get("FastWorldTravelTitle")}]\n已停止对 [{LuminaWrapper.GetDataCenterName(dcID)}] 大区可通行状态的实时监控");

                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // ignored
                }
            }
        }

        public void Clear()
        {
            var monitors = activeMonitors.ToArray();
            foreach (var monitor in monitors)
                RemoveMonitor(monitor.Key);
        }

        public IEnumerable<uint> GetActiveMonitors() =>
            activeMonitors.Keys;

        private async Task ProcessChannelRequestsAsync
        (
            CancellationToken serviceToken
        )
        {
            try
            {
                while (await requestChannel.Reader.WaitToReadAsync(serviceToken))
                {
                    while (requestChannel.Reader.TryRead(out var serverId))
                    {
                        if (activeMonitors.ContainsKey(serverId)) continue;

                        var taskCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);

                        if (activeMonitors.TryAdd(serverId, taskCts))
                            _ = MonitorRoutineAsync(serverId, taskCts);
                        else
                            taskCts.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception ex)
            {
                DLog.Error("[FastWorldTravel] 主循环发生预期外错误", ex);
            }
        }

        private async Task MonitorRoutineAsync
        (
            uint                    dcID,
            CancellationTokenSource cts
        )
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    if (checkLogicFunc(dcID) is { Item1: true } result)
                    {
                        var message = $"大区 [{LuminaWrapper.GetDataCenterName(dcID)}] 已为可通行状态, 停止监控";
                        NotifyHelper.Instance().Chat(message);
                        NotifyHelper.Instance().NotificationInfo(message);
                        NotifyHelper.Speak(message);

                        if (JustGo)
                            ChatManager.Instance().SendCommand($"/pdr worldtravel {result.Item2}");

                        break;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                DLog.Debug($"[FastWorldTravel] 对大区 {dcID} 的状态监控已被取消");
            }
            finally
            {
                activeMonitors.TryRemove(dcID, out _);
                cts.Dispose();
            }
        }
    }
}
