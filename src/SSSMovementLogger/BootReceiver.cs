using Android.App;
using Android.Content;

namespace SSSMovementLogger;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || (intent?.Action != Intent.ActionBootCompleted && intent?.Action != Intent.ActionMyPackageReplaced)) return;
        var pending = GoAsync();
        var app = context.ApplicationContext!;
        _ = Task.Run(async () =>
        {
            try { await BackgroundRecorder.TryAutomaticStartAsync(app); }
            catch (Exception exception) { StartupDiagnostics.LogFailure(app, "測位の再登録に失敗", exception); }
            finally { pending?.Finish(); }
        });
    }
}
