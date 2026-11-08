using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using Android.Views;
using SSSMovementLogger.Core;

namespace SSSMovementLogger;

[Activity(Label = "設定", Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class SettingsActivity : Activity
{
    private TextView status = null!;
    private TextView diagnosticsText = null!;
    private TextView logs = null!;
    private TextView startupLogs = null!;
    private Button start = null!;
    private Button stop = null!;
    private Button clearHistory = null!;
    private bool clearingHistory;
    private Switch commentNotifications = null!;



    private RecorderState? savedHistory;
    private Button automaticStart = null!;
    private RecorderState? subscribed;
    private readonly Handler main = new(Looper.MainLooper!);
    private bool visible;
    private bool startRequested;
    private bool locationRequested;
    private bool notificationRequested;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        startRequested = savedInstanceState?.GetBoolean(nameof(startRequested)) ?? false;
        locationRequested = savedInstanceState?.GetBoolean(nameof(locationRequested)) ?? false;
        notificationRequested = savedInstanceState?.GetBoolean(nameof(notificationRequested)) ?? false;

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        layout.SetPadding(padding, padding, padding, padding);
        layout.SetBackgroundColor(Android.Graphics.Color.Rgb(250, 247, 252));
        var back = new Button(this) { Text = "‹ こもりの部屋に戻る" };
        back.Click += (_, _) => Finish();
        layout.AddView(back);
        layout.AddView(new TextView(this) { Text = "設定", TextSize = 26 });
        start = new Button(this) { Text = "記録開始" };
        start.Click += (_, _) => RequestStart();
        layout.AddView(start);
        stop = new Button(this) { Text = "記録停止" };
        stop.Click += async (_, _) =>
        {
            start.Enabled = stop.Enabled = false;
            try { await BackgroundRecorder.StopAsync(this); }
            catch (Exception exception) { StartupDiagnostics.LogFailure(this, "記録停止に失敗", exception); }
            Render();
        };
        layout.AddView(stop);
        commentNotifications = new Switch(this) { Text = "こもりからの通知", Checked = AppPreferences.CommentsEnabled(this) };
        commentNotifications.CheckedChange += (_, args) =>
        {
            AppPreferences.SetCommentsEnabled(this, args.IsChecked);
            if (args.IsChecked && OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
                RequestPermissions(new[] { Manifest.Permission.PostNotifications }, 12);
        };
        layout.AddView(commentNotifications);
        layout.AddView(new TextView(this)
        {
            Text = "位置情報：常に許可（おおよその位置でも可）\n通知：通知を使う場合のみ許可",
            TextSize = 14
        });
        automaticStart = new Button(this) { Text = "権限を設定" };
        automaticStart.Click += (_, _) => StartActivity(new Intent(
            Android.Provider.Settings.ActionApplicationDetailsSettings,
            Android.Net.Uri.Parse("package:" + PackageName)));
        layout.AddView(automaticStart);
        var details = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        var showLogs = new Button(this) { Text = "ログを見る" };
        showLogs.Click += (_, _) =>
        {
            var show = details.Visibility == ViewStates.Gone;
            details.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
            showLogs.Text = show ? "ログを閉じる" : "ログを見る";
        };
        layout.AddView(showLogs);
        status = new TextView(this) { TextSize = 14 };
        details.AddView(status);
        diagnosticsText = new TextView(this) { TextSize = 13 };
        details.AddView(diagnosticsText);
        details.AddView(new TextView(this) { Text = "直近30件の位置取得（新しい順）", TextSize = 18 });
        logs = new TextView(this) { TextSize = 14 };
        details.AddView(logs);
        details.AddView(new TextView(this) { Text = "起動・停止ログ（RAMのみ・新しい順）", TextSize = 18 });
        startupLogs = new TextView(this) { TextSize = 14 };
        startupLogs.SetTextIsSelectable(true);
        details.AddView(startupLogs);

        layout.AddView(details);
        clearHistory = new Button(this) { Text = "足あとをすべて消す" };
        clearHistory.Click += (_, _) => new AlertDialog.Builder(this)!
            .SetTitle("足あとをすべて消しますか？")!
            .SetMessage("これまでの足あとをすべて消します。元に戻せません。設定は残り、記録中ならこの後も記録を続けます。")!
            .SetNegativeButton("キャンセル", (_, _) => { })!
            .SetPositiveButton("すべて消す", async (_, _) =>
            {
                if (clearingHistory) return;
                clearingHistory = true;
                Render();
                try
                {
                    await BackgroundRecorder.ClearHistoryAsync(ApplicationContext!);
                    savedHistory = MemorySession.Current;
                    Toast.MakeText(this, "足あとをすべて消しました。", ToastLength.Short)!.Show();
                }
                catch (Exception exception)
                {
                    StartupDiagnostics.LogFailure(this, "足あとの削除に失敗", exception);
                    Toast.MakeText(this, "足あとを消せませんでした。", ToastLength.Long)!.Show();
                }
                finally
                {
                    clearingHistory = false;
                    if (visible) AttachSession();
                }
            })!
            .Show();
        layout.AddView(clearHistory);
        var scroll = new ScrollView(this);
        scroll.SetOnApplyWindowInsetsListener(new SystemBarInsets());
        scroll.AddView(layout);
        SetContentView(scroll);
    }

    protected override async void OnResume()
    {
        base.OnResume();
        visible = true;
        MemorySession.Changed += SessionChanged;
        MemorySession.Startup.Changed += StateChanged;
        if (MemorySession.Startup.View().Length == 0)
            MemorySession.Startup.Add("このプロセスには起動通知の受信履歴がありません。未受信か、受信後にプロセスが終了したかはRAMログだけでは判別できません。以下は現在の設定です。");
        StartupDiagnostics.LogCurrent(this, "画面を表示");
        await BackgroundRecorder.TryAutomaticStartAsync(ApplicationContext!);
        if (!visible) return;
        try { savedHistory = BackgroundRecorder.GetSession(this); }
        catch (Exception exception) { MemorySession.Startup.Add("保存記録を開けません：" + exception.Message); }
        AttachSession();
        ContinueStart();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutBoolean(nameof(startRequested), startRequested);
        outState.PutBoolean(nameof(locationRequested), locationRequested);
        outState.PutBoolean(nameof(notificationRequested), notificationRequested);
        base.OnSaveInstanceState(outState);
    }

    protected override void OnPause()
    {
        visible = false;
        MemorySession.Changed -= SessionChanged;
        MemorySession.Startup.Changed -= StateChanged;
        if (subscribed is not null) subscribed.Changed -= StateChanged;
        subscribed = null;
        base.OnPause();
    }

    private void AttachSession()
    {
        if (subscribed is not null) subscribed.Changed -= StateChanged;
        subscribed = MemorySession.Current;
        if (subscribed is not null) subscribed.Changed += StateChanged;
        Render();
    }

    private void StateChanged() => main.Post(() => { if (visible) Render(); });
    private void SessionChanged() => main.Post(() => { if (visible) AttachSession(); });

    private void RequestStart()
    {
        StartupDiagnostics.LogCurrent(this, "画面から記録開始を要求");
        locationRequested = notificationRequested = false;
        startRequested = true;
        ContinueStart();
    }

    private void ContinueStart()
    {
        if (!visible || !startRequested) return;
        if (MemorySession.Current?.View().Running == true)
        {
            startRequested = false;
            Render();
            return;
        }
        if (CheckSelfPermission(Manifest.Permission.AccessCoarseLocation) != Permission.Granted &&
            CheckSelfPermission(Manifest.Permission.AccessFineLocation) != Permission.Granted)
        {
            if (locationRequested)
            {
                MemorySession.Startup.Add("手動開始を中止：位置情報が未許可。おおよその位置情報でも利用できます");
                startRequested = false;
                Toast.MakeText(this, "権限を設定 → 位置情報を許可してください。", ToastLength.Long)!.Show();
                return;
            }
            locationRequested = true;
            RequestPermissions(new[] { Manifest.Permission.AccessCoarseLocation }, 10);
            return;
        }
        if (CheckSelfPermission(Manifest.Permission.AccessBackgroundLocation) != Permission.Granted)
        {
            startRequested = false;
            Toast.MakeText(this, "権限を設定 → 位置情報を「常に許可」にしてください。", ToastLength.Long)!.Show();
            return;
        }
        if (AppPreferences.CommentsEnabled(this) && OperatingSystem.IsAndroidVersionAtLeast(33) && !notificationRequested &&
            CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            notificationRequested = true;
            RequestPermissions(new[] { Manifest.Permission.PostNotifications }, 11);
            return;
        }
        startRequested = false;
        StartRecording();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 12 && (grantResults.Length == 0 || grantResults[0] != Permission.Granted))
        {
            commentNotifications.Checked = false;
            Toast.MakeText(this, "こもりの通知には通知権限が必要です。", ToastLength.Long)!.Show();
        }
        if (requestCode is 10 or 11) ContinueStart();
    }

    private async void StartRecording()
    {
        try
        {
            start.Enabled = stop.Enabled = false;
            await BackgroundRecorder.StartAsync(this);
            Render();
            start.Enabled = false;
            status.Text = "記録を開始しています…";
        }
        catch (Exception exception)
        {
            StartupDiagnostics.LogFailure(this, "手動開始の要求に失敗", exception);
            Toast.MakeText(this, "記録を開始できませんでした。ログを確認してください。", ToastLength.Long)!.Show();
            start.Enabled = true;
        }
    }

    private static string Timestamp(DateTimeOffset? time) => time is null ? "―" :
        time.Value.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy/MM/dd HH:mm:ss") + " JST";

    private void Render()
    {
        var history = MemorySession.Current ?? savedHistory;
        var view = history?.View();
        var running = view?.Running == true;
        var stopping = BackgroundRecorder.IsStopping;
        start.Enabled = !running && !stopping && !clearingHistory;
        stop.Enabled = running && !stopping && !clearingHistory;
        clearHistory.Enabled = !stopping && !clearingHistory;
        var diagnostics = StartupDiagnostics.Read(this);
        var canAutoStart = diagnostics.HasAutomaticLocationPermissions;
        automaticStart.Enabled = true;
        status.Text = $"状態：{(stopping ? "停止処理中" : running ? "記録中" : "停止中")}\n" +
            $"位置取得：{view?.LocationStatus ?? "未取得"} / 記録済み：{view?.RecordedCount ?? 0}件\n" +
            $"最終測位：{Timestamp(view?.LastPosition?.Time)}";
        diagnosticsText.Text = $"自動開始の位置権限：{(canAutoStart ? "許可済み" : diagnostics.MissingAutomaticPermissions)}\n" + diagnostics.Describe();
        logs.Text = view is null || view.Logs.Length == 0 ? "ログなし" :
            string.Join("\n\n", view.Logs.Select(log => $"{Timestamp(log.Time)}\n{log.Message}"));
        var events = MemorySession.Startup.View();
        startupLogs.Text = events.Length == 0
            ? "このプロセスには起動履歴がありません。起動通知が未受信か、受信後にプロセスが終了した可能性があります。"
            : string.Join("\n\n", events.Select(log => $"{Timestamp(log.Time)}\n{log.Message}"));
    }

    private sealed class SystemBarInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            var bars = insets!.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            view!.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            return insets;
        }
    }
}
