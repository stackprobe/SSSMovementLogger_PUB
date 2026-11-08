namespace SSSMovementLogger.Core;

// Startup diagnostics are RAM-only, independent of the position recording session.
internal sealed class StartupHistory
{
    private readonly object gate = new();
    private readonly Queue<ActivityLog> entries = new();
    public event Action? Changed;

    public void Add(string message)
    {
        lock (gate)
        {
            entries.Enqueue(new(DateTimeOffset.UtcNow, message));
            while (entries.Count > LoggerSettings.MaxLogs) entries.Dequeue();
        }
        Changed?.Invoke();
    }

    public ActivityLog[] View()
    {
        lock (gate) return entries.Reverse().ToArray();
    }
}
