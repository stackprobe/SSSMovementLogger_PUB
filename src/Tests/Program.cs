using SSSMovementLogger.Core;

StartupDiagnostics();
LocationPolicy();
PositionStoreTests.Run(Check);
MovementTests();
TransitionTests();
Console.WriteLine("PASS: permissions, location policy, unlimited persistence, separate app storage, crash recovery, movement, dialogue, delayed background batches");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void StartupDiagnostics()
{
    foreach (var fine in new[] { false, true })
    foreach (var background in new[] { false, true })
    {
        var status = new StartupStatus(fine, background, false, false, false, false);
        Check(status.HasAutomaticLocationPermissions == (fine && background), "Wrong permission decision");
        Check(status.MissingAutomaticPermissions.Contains("位置情報が未許可") == !fine,
            "Fine location denial was misreported");
        Check(status.MissingAutomaticPermissions.Contains("「常に許可」が未許可") == !background,
            "Background location denial was misreported");
        Check(status.Describe().Contains("OFF（測位できません）"), "Location switch not explained");
        Check(status.Describe().Contains("未許可（記録開始は可能）"), "Notification denial not explained");
    }
    var allowed = new StartupStatus(true, true, true, true, false, false);
    Check(allowed.HasAutomaticLocationPermissions && allowed.MissingAutomaticPermissions == "",
        "Battery optimization was treated as a missing permission");
    Check((allowed with { BackgroundRestricted = true }).Describe().Contains("起動通知が届かない場合"),
        "Background restriction warning missing");
}
static DateTimeOffset Epoch() => new(2026, 9, 5, 16, 2, 3, TimeSpan.Zero);
static void LocationPolicy()
{
    const long nowNanos = 1_000_000_000_000;
    var now = Epoch();
    var nowMillis = now.ToUnixTimeMilliseconds();
    bool Accept(long fixNanos, long timeMillis, double lat = 35, double lon = 139, bool mock = false) =>
        LocationFixPolicy.IsUsableBackground(nowNanos, fixNanos, nowMillis, timeMillis, lat, lon, mock);
    Check(Accept(nowNanos, nowMillis), "Current position rejected");
    var clearedAt = nowMillis - 1000;
    foreach (var time in new[] { clearedAt - 1, clearedAt, clearedAt + 1 })
        Check(LocationFixPolicy.IsUsableBackground(nowNanos, nowNanos, nowMillis, time, 35, 139, false, clearedAt)
            == (time > clearedAt), "Deleted history returned in a delayed location batch");
    Check(Accept(1, now.AddHours(-2).ToUnixTimeMilliseconds()), "Delayed background batch rejected");
    Check(!Accept(nowNanos + 1, nowMillis) && !Accept(0, nowMillis), "Future/invalid monotonic timestamp accepted");
    Check(!Accept(nowNanos, nowMillis + 1) && !Accept(nowNanos, 0), "Future/invalid wall time accepted");
    Check(!Accept(nowNanos, nowMillis, mock: true), "Mock fix accepted");
    Check(!Accept(nowNanos, nowMillis, double.NaN) && !Accept(nowNanos, nowMillis, lon: double.PositiveInfinity) &&
        !Accept(nowNanos, nowMillis, 91) && !Accept(nowNanos, nowMillis, lon: -181), "Invalid coordinates accepted");
    var state = new RecorderState(TimeSpan.Zero, utcNow: () => now);
    state.LocationResult(TimeSpan.Zero, now.AddHours(-2), 35, 139);
    state.LocationResult(TimeSpan.Zero, now, 35, 139);
    state.LocationResult(TimeSpan.Zero, now, 35, 139);
    Check(state.View().RecordedCount == 2, "Delayed batch lost points or duplicated last fix");
}
static void MovementTests()
{
    var now = Epoch().AddHours(3);
    PositionPoint[] Trail(double step) => Enumerable.Range(0, 13)
        .Select(i => new PositionPoint(i, now.AddMinutes((i - 12) * 5), 35 + step * i, 139)).ToArray();
    Check(MovementSummary.Analyze([], now).Category == "insufficient", "Empty history judged stationary");
    Check(MovementSummary.Analyze(Trail(0), now).Category == "still", "Stationary history misclassified");
    Check(MovementSummary.Analyze(Trail(0.0001), now).Category == "still", "Small drift counted as movement");
    Check(MovementSummary.Analyze(Trail(0.001), now).Category == "moving", "Slow movement not accumulated");
    Check(MovementSummary.Analyze(Trail(0.005), now).Category == "active", "Active history misclassified");
    Check(MovementSummary.Analyze(Trail(0), now.AddHours(1)).Category == "stale", "Stale history judged stationary");
    Check(MovementSummary.Analyze([new(1, now.AddHours(-2), 35, 139), new(2, now, 36, 139)], now).Category == "insufficient", "Missing hours treated as observation");
    Check(MovementSummary.Analyze([new(1, now.AddDays(-1), 35, 139)], now).Category == "insufficient", "Old data included");
    using var json = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "dialogue.json"));
    var catalog = new DialogueCatalog(json);
    foreach (var category in new[] { "insufficient", "stale", "still", "moving", "active", "started", "stopped" })
    {
        var summary = new MovementSummary(category, 1234, 12);
        var first = catalog.Speak(summary);
        var second = catalog.Speak(summary);
        Check(first != second && !first.Contains('{') && !second.Contains('{'), "Dialogue repeats or leaves placeholders");
    }
    foreach (var category in new[] { "insufficient", "stale", "still", "moving", "active" })
    {
        var summary = new MovementSummary(category, 1234, 12);
        var first = catalog.Speak(summary, recording: false);
        var second = catalog.Speak(summary, recording: false);
        Check(first.Contains("記録が停止中") && second.Contains("記録が停止中") && first != second,
            "Paused recording did not override movement dialogue or repeated the same line");
        Check(!catalog.Speak(summary, recording: true).Contains("記録が停止中"),
            "Recording resumed but paused dialogue remained");
    }
}

static void TransitionTests()
{
    var clock = Epoch();
    PositionPoint Point(int minutes, double lat = 35) => new(minutes + 1, clock.AddMinutes(minutes), lat, 139);
    var detector = new MovementTransitionDetector();
    for (int i = 0; i <= 30; i += 5) Check(detector.Add(Point(i)) is null, "Stationary history notified");
    Check(detector.Add(Point(35, 35.004)) is null, "Departure not confirmed twice");
    Check(detector.Add(Point(40, 35.008)) == "started", "Long rest followed by movement not detected");
    Check(detector.Add(Point(45, 35.008)) is null, "Stop detected too soon");
    Check(detector.Add(Point(50, 35.008)) == "stopped", "Stop not detected after ten minutes");
    Check(detector.Add(Point(55, 35.008)) is null, "Stop repeated");
    Check(detector.Add(Point(90, 35.008)) is null, "Gap implied a stop");
    var noise = new MovementTransitionDetector();
    for (int i = 0; i <= 30; i += 5) noise.Add(Point(i));
    Check(noise.Add(Point(35, 35.004)) is null && noise.Add(Point(40)) is null, "One-point drift notified");
    Check(noise.Add(Point(40)) is null, "Duplicate notified");
    Check(noise.Add(Point(45, 80)) is null, "Implausible jump notified");
    var now = clock.AddHours(3);
    var summary = MovementSummary.Analyze([new(1, now.AddMinutes(-60), 35, 139), new(2, now.AddMinutes(-55), 35, 139), new(3, now, 35, 139)], now);
    Check(summary.ObservedMinutes == 5 && summary.GapMinutes == 55, "Missing interval counted as observation");
    var stale = MovementSummary.Analyze([new(1, now.AddMinutes(-40), 35, 139), new(2, now.AddMinutes(-30), 35, 139)], now);
    Check(stale.LastFixMinutes == 30 && stale.Category == "stale", "Stale dialogue uses wrong measurement age");
}
