namespace SSSMovementLogger.Core;

internal static class LocationFixPolicy
{
    // Background deliveries may contain delayed batches; age alone must not discard history.
    public static bool IsUsableBackground(long nowNanos, long fixNanos, long nowMillis, long timeMillis,
        double latitude, double longitude, bool mock, long historyClearedAtMillis = 0) =>
        !mock && fixNanos > 0 && fixNanos <= nowNanos && timeMillis > historyClearedAtMillis && timeMillis > 0 && timeMillis <= nowMillis &&
        PositionStore.Valid(latitude, longitude);
}
