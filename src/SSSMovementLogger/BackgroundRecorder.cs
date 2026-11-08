using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.OS;
using SSSMovementLogger.Core;

namespace SSSMovementLogger;

internal static class MemorySession
{
    private static RecorderState? current;
    public static StartupHistory Startup { get; } = new();
    public static event Action? Changed;
    public static RecorderState? Current
    {
        get => current;
        set { current = value; Changed?.Invoke(); }
    }
}

// FLP delivers locations through a PendingIntent even when our process is not running.
// No service is started, so recording never creates an ongoing notification.
internal static class BackgroundRecorder
{
    private const string LocationAction = "net.sss.movementlogger.LOCATION";
    private static readonly SemaphoreSlim Operations = new(1);
    private static readonly object SessionGate = new();
    private static MovementTransitionDetector? transitions;
    private static bool registeredInProcess;
    public static bool IsStopping { get; private set; }

    public static RecorderState GetSession(Context context)
    {
        lock (SessionGate)
        {
            if (MemorySession.Current is { } current) return current;
            ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(1);
            var state = Load(context);
            if (!AppPreferences.RecordingEnabled(context)) state.Stop();
            MemorySession.Current = state;
            return state;
        }
    }

    private static PositionStore Store(Context context) =>
        new(Path.Combine(context.NoBackupFilesDir!.AbsolutePath, PositionStore.FileName));

    private static RecorderState Load(Context context) => new(TimeSpan.Zero, Store(context));

    public static async Task ClearHistoryAsync(Context context)
    {
        await Operations.WaitAsync();
        try
        {
            var clearedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (SessionGate)
            {
                Store(context).Clear();
                AppPreferences.SetHistoryClearedAt(context, clearedAt);
                var recorder = Load(context);
                if (!AppPreferences.RecordingEnabled(context)) recorder.Stop();
                transitions = null;
                MemorySession.Current = recorder;
            }
            ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(2);
            MemorySession.Startup.Add("足あとをすべて削除");
        }
        finally { Operations.Release(); }
    }

    private static PendingIntent Delivery(Context context) => PendingIntent.GetBroadcast(context, 10,
        new Intent(context, typeof(LocationUpdateReceiver)).SetAction(LocationAction),
        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;

    public static async Task TryAutomaticStartAsync(Context context)
    {
        if (!StartupDiagnostics.Read(context).HasAutomaticLocationPermissions) return;
        try { await StartAsync(context, onlyIfNeeded: true); }
        catch (Exception exception) { StartupDiagnostics.LogFailure(context, "記録の自動開始に失敗", exception); }
    }

    public static async Task StartAsync(Context context, bool onlyIfNeeded = false)
    {
        await Operations.WaitAsync();
        try
        {
            if (onlyIfNeeded && registeredInProcess) return;
            if (!StartupDiagnostics.Read(context).HasAutomaticLocationPermissions)
                throw new InvalidOperationException("位置情報を「常に許可」にしてください。");
            if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(context) != ConnectionResult.Success)
                throw new InvalidOperationException("Google Play開発者サービスを有効にしてください。");
            var recorder = Load(context);
            using var client = LocationServices.GetFusedLocationProviderClient(context);
            using var request = new LocationRequest.Builder(Priority.PriorityLowPower,
                (long)LoggerSettings.LocationInterval.TotalMilliseconds)
                .SetMinUpdateIntervalMillis((long)LoggerSettings.LocationInterval.TotalMilliseconds)
                .SetMaxUpdateDelayMillis((long)TimeSpan.FromMinutes(15).TotalMilliseconds)
                .SetWaitForAccurateLocation(false).Build();
            using var delivery = Delivery(context);
            await client.RequestLocationUpdatesAsync(request, delivery);
            registeredInProcess = true;
            AppPreferences.SetRecordingEnabled(context, true);
            transitions = new MovementTransitionDetector();
            foreach (var point in recorder.RecentPoints()) transitions.Add(point);
            lock (SessionGate) MemorySession.Current = recorder;
            MemorySession.Startup.Add("通知なしのバックグラウンド測位を登録");
        }
        finally { Operations.Release(); }
    }

    public static async Task StopAsync(Context context)
    {
        await Operations.WaitAsync();
        try
        {
            IsStopping = true;
            AppPreferences.SetRecordingEnabled(context, false);
            registeredInProcess = false;
            MemorySession.Current?.Stop();
            ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(2);
            using var client = LocationServices.GetFusedLocationProviderClient(context);
            using var delivery = Delivery(context);
            await client.RemoveLocationUpdatesAsync(delivery);
            transitions = null;
            MemorySession.Startup.Add("バックグラウンド測位を解除");
        }
        finally { IsStopping = false; Operations.Release(); }
    }

    public static async Task ReceiveAsync(Context context, Intent intent)
    {
        await Operations.WaitAsync();
        try
        {
            if (!AppPreferences.RecordingEnabled(context) || intent.Action != LocationAction) return;
            var recorder = GetSession(context);
            if (transitions is null)
            {
                transitions = new MovementTransitionDetector();
                foreach (var point in recorder.RecentPoints()) transitions.Add(point);
            }
            using var result = LocationResult.ExtractResult(intent);
            if (result?.Locations is null) return;
            string? change = null;
            DateTimeOffset? changeTime = null;
            var clearedAt = AppPreferences.HistoryClearedAt(context);
            foreach (var location in result.Locations.OrderBy(location => location.Time))
            {
                // Do not restore deleted history from a delayed FLP batch.
                if (!LocationFixPolicy.IsUsableBackground(SystemClock.ElapsedRealtimeNanos(), location.ElapsedRealtimeNanos,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), location.Time, location.Latitude, location.Longitude, location.Mock,
                    historyClearedAtMillis: clearedAt)) continue;
                var previous = recorder.View().LastPosition?.Id;
                recorder.LocationResult(TimeSpan.Zero, DateTimeOffset.FromUnixTimeMilliseconds(location.Time),
                    location.Latitude, location.Longitude);
                var point = recorder.View().LastPosition;
                if (point is null || point.Id == previous) continue;
                var detected = transitions.Add(point);
                if (detected is not null) { change = detected; changeTime = point.Time; }
            }
            // Delayed batches are saved, but never replay old comments as fresh events.
            if (change is not null && changeTime is not null && DateTimeOffset.UtcNow - changeTime.Value <= TimeSpan.FromMinutes(15))
                NotifyComment(context, change, recorder.Movement());
        }
        catch (Exception exception)
        {
            AppPreferences.SetRecordingEnabled(context, false);
            MemorySession.Current?.Stop();
            StartupDiagnostics.LogFailure(context, "位置記録を停止", exception);
        }
        finally { Operations.Release(); }
    }

    private static void NotifyComment(Context context, string change, MovementSummary summary)
    {
        lock (AppPreferences.NotificationGate)
        {
            try
            {
                if (!AppPreferences.CommentsEnabled(context)) return;
                if (OperatingSystem.IsAndroidVersionAtLeast(33) && context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted) return;
                var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
                manager.CreateNotificationChannel(new NotificationChannel("komori_comments", "こもりからの通知", NotificationImportance.Default));
                using var json = context.Assets!.Open("dialogue.json");
                var text = new DialogueCatalog(json).Speak(summary with { Category = change });
                var open = PendingIntent.GetActivity(context, 2, new Intent(context, typeof(MainActivity)),
                    PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
                manager.Notify(2, new Notification.Builder(context, "komori_comments")
                    .SetContentTitle("こもり").SetContentText(text).SetStyle(new Notification.BigTextStyle().BigText(text))
                    .SetSmallIcon(Resource.Drawable.ic_launcher_monochrome).SetContentIntent(open).SetAutoCancel(true).Build());
            }
            catch (Exception exception) { Android.Util.Log.Warn("SSSMovementLogger", exception.Message); }
        }
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class LocationUpdateReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var pending = GoAsync();
        var app = context.ApplicationContext!;
        _ = Task.Run(async () =>
        {
            try { await BackgroundRecorder.ReceiveAsync(app, intent); }
            finally { pending?.Finish(); }
        });
    }
}
