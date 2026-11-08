namespace SSSMovementLogger.Core;

internal sealed record PositionPoint(long Id, DateTimeOffset Time, double Latitude, double Longitude);
internal sealed record ActivityLog(DateTimeOffset Time, string Message);
internal sealed record RecorderView(bool Running, string LocationStatus, long RecordedCount,
    PositionPoint? LastPosition, ActivityLog[] Logs);

internal sealed class RecorderState
{
    private readonly object gate = new();
    private readonly List<PositionPoint> recent = new();
    private readonly Queue<ActivityLog> logs = new();
    private readonly PositionStore? store;
    private readonly Func<DateTimeOffset> utcNow;
    private bool running = true;
    private long count;
    private PositionPoint? lastPosition;
    private string locationStatus = "測位待ち";

    public RecorderState(TimeSpan now, PositionStore? store = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.store = store;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        if (store is null) return;
        var saved = store.Load(this.utcNow());
        count = saved.Count;
        lastPosition = saved.Last;
        recent.AddRange(saved.Recent);
    }

    public event Action? Changed;
    public RecorderView View()
    {
        lock (gate) return new(running, locationStatus, count, lastPosition, logs.Reverse().ToArray());
    }
    public PositionPoint[] RecentPoints()
    {
        lock (gate) return recent.ToArray();
    }
    public MovementSummary Movement()
    {
        lock (gate) return MovementSummary.Analyze(recent, utcNow());
    }
    public void LocationResult(TimeSpan now, DateTimeOffset? time, double latitude = 0,
        double longitude = 0, string failure = "取得不可", double? accuracyMeters = null)
    {
        lock (gate)
        {
            if (!running) return;
            var clock = utcNow();
            recent.RemoveAll(p => p.Time < clock - LoggerSettings.ConversationWindow);
            if (time is null) locationStatus = failure;
            else if (!PositionStore.Valid(latitude, longitude) || time > clock ||
                (lastPosition is not null && time <= lastPosition.Time))
                locationStatus = "古い点・重複点・不正な点を除外";
            else
            {
                var point = new PositionPoint(count + 1, time.Value, latitude, longitude);
                // Commit before changing memory. Storage errors must stop recording, not pretend success.
                store?.Append(point);
                count++;
                lastPosition = point;
                if (time >= clock - LoggerSettings.ConversationWindow) recent.Add(point);
                locationStatus = "取得成功";
            }
            logs.Enqueue(new(clock, "位置取得：" + locationStatus));
            while (logs.Count > LoggerSettings.MaxLogs) logs.Dequeue();
        }
        Changed?.Invoke();
    }
    public void Stop()
    {
        lock (gate) { running = false; locationStatus = "停止中"; }
        Changed?.Invoke();
    }
}
