namespace SSSMovementLogger.Core;

// Require two fixes away from a long-held anchor; gaps never imply a stop.
internal sealed class MovementTransitionDetector
{
    private PositionPoint? previous;
    private PositionPoint? anchor;
    private DateTimeOffset quietSince;
    private bool moving;
    private bool departurePending;
    private bool departedAfterRest;

    public string? Add(PositionPoint point)
    {
        if (previous is not null && point.Time <= previous.Time) return null;
        if (previous is null || point.Time - previous.Time > TimeSpan.FromMinutes(20) ||
            MovementSummary.Distance(previous, point) / (point.Time - previous.Time).TotalSeconds >= 100)
        {
            previous = anchor = point;
            quietSince = point.Time;
            moving = departurePending = false;
            return null;
        }
        previous = point;
        var away = MovementSummary.Distance(anchor!, point) >= 200;
        if (moving)
        {
            if (away) { anchor = point; quietSince = point.Time; }
            else if (point.Time - quietSince >= TimeSpan.FromMinutes(10))
            {
                moving = false;
                departurePending = false;
                return "stopped";
            }
            return null;
        }
        if (!away) { departurePending = false; return null; }
        if (!departurePending)
        {
            departedAfterRest = point.Time - quietSince >= TimeSpan.FromMinutes(30);
            departurePending = true;
            return null;
        }
        moving = true;
        departurePending = false;
        anchor = point;
        quietSince = point.Time;
        return departedAfterRest ? "started" : null;
    }
}
