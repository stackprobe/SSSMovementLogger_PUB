using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using SSSMovementLogger.Core;

namespace SSSMovementLogger;

internal static class StartupDiagnostics
{
    public static StartupStatus Read(Context context) => new(
        context.CheckSelfPermission(Manifest.Permission.AccessCoarseLocation) == Permission.Granted ||
            context.CheckSelfPermission(Manifest.Permission.AccessFineLocation) == Permission.Granted,
        context.CheckSelfPermission(Manifest.Permission.AccessBackgroundLocation) == Permission.Granted,
        !OperatingSystem.IsAndroidVersionAtLeast(33) ||
            context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted,
        ((LocationManager)context.GetSystemService(Context.LocationService)!).IsLocationEnabled,
        ((ActivityManager)context.GetSystemService(Context.ActivityService)!).IsBackgroundRestricted,
        ((PowerManager)context.GetSystemService(Context.PowerService)!).IsIgnoringBatteryOptimizations(context.PackageName));

    public static void LogCurrent(Context context, string source)
    {
        var status = Read(context);
        MemorySession.Startup.Add(source + "：現在の設定\n" + status.Describe() + "\n" +
            (status.HasAutomaticLocationPermissions
                ? "自動開始に必要な位置権限は揃っています（起動成功を示すものではありません）"
                : "自動開始に不足：" + status.MissingAutomaticPermissions));
    }

    public static void LogFailure(Context context, string operation, Exception exception)
    {
        LogCurrent(context, operation + "時点");
        MemorySession.Startup.Add(operation + "：" + exception.GetType().FullName + "\n" + exception.Message);
        Android.Util.Log.Warn("SSSMovementLogger", operation + "：" + exception);
    }
}
