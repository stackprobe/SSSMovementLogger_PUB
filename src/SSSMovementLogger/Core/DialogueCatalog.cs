using System.Globalization;
using System.Text.Json;

namespace SSSMovementLogger.Core;

internal sealed record MovementSummary(string Category, double DistanceMeters, int Samples,
    double ObservedMinutes = 0, double GapMinutes = 0, double DisplacementMeters = 0, double LastFixMinutes = 0)
{
    public static MovementSummary Analyze(IEnumerable<PositionPoint> history, DateTimeOffset now)
    {
        var points = history.Where(p => p.Time >= now - LoggerSettings.ConversationWindow && p.Time <= now)
            .OrderBy(p => p.Time).ToArray();

        if (points.Length == 0) return new("insufficient", 0, 0);
        double distance = 0;
        double gapMinutes = 0;
        var observed = TimeSpan.Zero;
        var anchor = points[0];
        for (int i = 1; i < points.Length; i++)
        {
            var gap = points[i].Time - points[i - 1].Time;
            if (gap > TimeSpan.FromMinutes(20)) { gapMinutes += gap.TotalMinutes; anchor = points[i]; continue; }
            observed += gap;
            var meters = Distance(anchor, points[i]);
            // Ignore small drift and implausible jumps. Slow movement accumulates relative to anchor.
            if (meters < 200) continue;
            if (meters / Math.Max(1, (points[i].Time - anchor.Time).TotalSeconds) < 100) distance += meters;
            anchor = points[i];
        }
        var lastFixMinutes = (now - points[^1].Time).TotalMinutes;
        var category = lastFixMinutes > 15 ? "stale" : observed.TotalMinutes < 30 ? "insufficient"
            : distance >= 3000 ? "active" : distance >= 500 ? "moving" : "still";
        return new(category, distance, points.Length, observed.TotalMinutes, gapMinutes,
            Distance(points[0], points[^1]), lastFixMinutes);
    }

    internal static double Distance(PositionPoint a, PositionPoint b)
    {
        const double rad = Math.PI / 180;
        var lat = (b.Latitude - a.Latitude) * rad;
        var lon = (b.Longitude - a.Longitude) * rad;
        var h = Math.Pow(Math.Sin(lat / 2), 2) + Math.Cos(a.Latitude * rad) * Math.Cos(b.Latitude * rad) * Math.Pow(Math.Sin(lon / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }
}

internal sealed class DialogueCatalog
{
    private static readonly string[] Categories = ["insufficient", "stale", "active", "moving", "still", "started", "stopped", "recording_paused"];
    private readonly Dictionary<string, string[]> lines;
    private string? previous;
    public DialogueCatalog(Stream json)
    {
        lines = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json) ?? throw new InvalidDataException("セリフが空です。");
        foreach (var category in Categories)
            if (!lines.TryGetValue(category, out var values) || values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("セリフが不足しています：" + category);
    }
    public string Speak(MovementSummary summary, bool recording = true)
    {
        var choices = lines[recording ? summary.Category : "recording_paused"];
        var candidates = choices.Where(s => s != previous).ToArray();
        previous = candidates.Length == 0 ? choices[0] : candidates[Random.Shared.Next(candidates.Length)];
        return previous.Replace("{distanceKm}", (summary.DistanceMeters / 1000).ToString("0.0", CultureInfo.InvariantCulture))
            .Replace("{samples}", summary.Samples.ToString(CultureInfo.InvariantCulture))
            .Replace("{observedMinutes}", summary.ObservedMinutes.ToString("0", CultureInfo.InvariantCulture))
            .Replace("{displacementKm}", (summary.DisplacementMeters / 1000).ToString("0.0", CultureInfo.InvariantCulture))
            .Replace("{lastFixMinutes}", Math.Floor(summary.LastFixMinutes).ToString("0", CultureInfo.InvariantCulture))
            .Replace("{coverageNote}", summary.GapMinutes > 0
                ? $"途中の約{summary.GapMinutes:0}分は見えていないから、その間の行動は保留ね。"
                : "位置の誤差はあるから、距離はざっくりした目安ね。");
    }
}
