using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using SSSMovementLogger.Core;

namespace SSSMovementLogger;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class MainActivity : Activity
{
    private TextView speech = null!;
    private DialogueCatalog dialogue = null!;
    private RecorderState? savedHistory;
    private string? historyError;
    private bool returningFromSettings;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        using (var json = Assets!.Open("dialogue.json")) dialogue = new DialogueCatalog(json);
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(padding, padding, padding, padding);
        layout.SetBackgroundColor(Android.Graphics.Color.Rgb(250, 247, 252));
        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.AddView(new TextView(this) { Text = "ぐうたら日和", TextSize = 26 },
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var settings = new Button(this) { Text = "設定", ContentDescription = "記録・権限・ログの設定を開く" };
        settings.Click += (_, _) =>
        {
            returningFromSettings = true;
            StartActivity(new Intent(this, typeof(SettingsActivity)));
        };
        header.AddView(settings);
        layout.AddView(header);
        layout.AddView(new TextView(this) { Text = "こもりのお部屋", TextSize = 14 });
        var girl = new ImageView(this)
        {
            ContentDescription = "ノートPCの前のこもり。タップして最近の移動について聞く", Clickable = true, Focusable = true
        };
        girl.SetImageResource(Resource.Drawable.neet_girl);
        girl.SetAdjustViewBounds(true);
        girl.SetScaleType(ImageView.ScaleType.FitCenter);
        girl.Click += (_, _) => Speak();
        layout.AddView(girl, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        speech = new TextView(this) { TextSize = 18, AccessibilityLiveRegion = AccessibilityLiveRegion.Polite };
        speech.SetTextColor(Android.Graphics.Color.Rgb(53, 35, 68));
        speech.SetPadding(padding, padding + (int)(12 * Resources.DisplayMetrics.Density), padding, padding);
        speech.SetBackgroundResource(Resource.Drawable.speech_bubble);
        layout.AddView(speech, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        layout.AddView(new TextView(this) { Text = "こもりをタップして、おしゃべり。", TextSize = 13, Gravity = GravityFlags.Center });
        var scroll = new ScrollView(this);
        scroll.SetOnApplyWindowInsetsListener(new SystemBarInsets());
        scroll.AddView(layout);
        SetContentView(scroll);
    }

    protected override async void OnResume()
    {
        base.OnResume();
        // Preserve a stop made in settings until the next app visit.
        var shouldStart = !returningFromSettings;
        returningFromSettings = false;
        if (shouldStart)
        {
            await BackgroundRecorder.TryAutomaticStartAsync(ApplicationContext!);
            if (IsFinishing || IsDestroyed) return;
        }
        // Settings can start/stop the recorder while this screen is paused.
        savedHistory = null;
        historyError = null;
        try { savedHistory = BackgroundRecorder.GetSession(this); }
        catch (Exception exception)
        {
            MemorySession.Startup.Add("保存記録を開けません：" + exception.Message);
            historyError = "足あとのノートが開けなかったよ。設定のお部屋で、何があったか一緒に見てみよ。";
        }
        Speak();
    }

    private void Speak()
    {
        var session = MemorySession.Current ?? savedHistory;
        var summary = session?.Movement()
            ?? MovementSummary.Analyze([], DateTimeOffset.UtcNow);
        var recording = AppPreferences.RecordingEnabled(this) && session?.View().Running == true;
        speech.Text = historyError ?? dialogue.Speak(summary, recording);
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
