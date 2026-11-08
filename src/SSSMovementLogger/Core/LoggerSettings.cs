namespace SSSMovementLogger.Core;

internal static class LoggerSettings
{
    public static readonly TimeSpan LocationInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ConversationWindow = TimeSpan.FromHours(3);
    public const int MaxLogs = 30;
}
