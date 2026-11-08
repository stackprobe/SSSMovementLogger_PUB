using Android.App;
using Android.Content;

namespace SSSMovementLogger;

internal static class AppPreferences
{
    private const string Key = "komori_notifications";
    internal static readonly object NotificationGate = new();
    public static bool CommentsEnabled(Context context) =>
        context.GetSharedPreferences("sssmovementlogger", FileCreationMode.Private)!.GetBoolean(Key, false);

    public static void SetCommentsEnabled(Context context, bool enabled)
    {
        lock (NotificationGate)
        {
            context.GetSharedPreferences("sssmovementlogger", FileCreationMode.Private)!.Edit()!.PutBoolean(Key, enabled)!.Apply();
            if (!enabled) ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(2);
        }
    }

    public static bool RecordingEnabled(Context context) =>
        context.GetSharedPreferences("sssmovementlogger", FileCreationMode.Private)!.GetBoolean("recording_enabled", false);

    public static long HistoryClearedAt(Context context) =>
        context.GetSharedPreferences("sssmovementlogger", FileCreationMode.Private)!.GetLong("history_cleared_at", 0);

    public static void SetHistoryClearedAt(Context context, long time) =>
        context.GetSharedPreferences("sssmovementlogger", FileCreationMode.Private)!.Edit()!.PutLong("history_cleared_at", time)!.Apply();

    public static void SetRecordingEnabled(Context context, bool enabled) =>
        context.GetSharedPreferences("sssmovementlogger", FileCreationMode.Private)!.Edit()!.PutBoolean("recording_enabled", enabled)!.Apply();
}
