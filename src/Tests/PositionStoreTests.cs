using SSSMovementLogger.Core;

internal static class PositionStoreTests
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "positions-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "positions.bin");
            var clock = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
            var store = new PositionStore(path);
            RecorderState Open() => new(TimeSpan.Zero, store, () => clock);
            var state = Open();
            for (int i = 0; i < 4400; i++)
            {
                clock = clock.AddMinutes(5);
                state.LocationResult(TimeSpan.Zero, clock, 35, 139);
            }
            check(state.View().RecordedCount == 4400, "Old count limit remains");
            check(new FileInfo(path).Length == 8 + 4400 * 24, "History was not appended");
            state.Stop();
            state.LocationResult(TimeSpan.Zero, clock.AddMinutes(5), 36, 140);
            clock = clock.AddDays(30);
            state = Open();
            check(state.View().RecordedCount == 4400 && state.View().Logs.Length == 0, "Restart or age deleted history");
            check(state.Movement().Category == "insufficient", "Old history used for recent activity");
            state.LocationResult(TimeSpan.Zero, clock, 35, 139);
            state.LocationResult(TimeSpan.Zero, clock, 35, 139);
            state.LocationResult(TimeSpan.Zero, clock.AddMinutes(1), 35, 139);
            state.LocationResult(TimeSpan.Zero, clock, double.NaN, 139);
            check(state.View().RecordedCount == 4401, "Duplicate/future/invalid data retained");
            using (var tail = new FileStream(path, FileMode.Append)) tail.Write([1, 2, 3]);
            check(Open().View().RecordedCount == 4401 && new FileInfo(path).Length == 8 + 4401 * 24, "Torn final record not recovered");
            var legacy = Path.Combine(directory, "legacy.bin");
            using (var writer = new BinaryWriter(File.Create(legacy)))
            {
                writer.Write(0x31504C54); writer.Write(1);
                writer.Write(clock.AddDays(-60).UtcTicks); writer.Write(35.0); writer.Write(139.0);
            }
            var original = File.ReadAllBytes(legacy);
            try { new PositionStore(legacy).Load(clock); throw new Exception("TTTLATLONLogger history accepted"); }
            catch (InvalidDataException) { check(File.ReadAllBytes(legacy).SequenceEqual(original), "Other app history modified"); }
            File.WriteAllBytes(legacy, [1, 2, 3]);
            try { new PositionStore(legacy).Load(clock); throw new Exception("Corruption silently accepted"); }
            catch (InvalidDataException) { check(File.ReadAllBytes(legacy).Length == 3, "Corrupt file deleted"); }

            store.Clear();
            check(!File.Exists(path), "Deleted history remains on disk");
            state = Open();
            check(state.View().RecordedCount == 0 && state.View().LastPosition is null &&
                state.RecentPoints().Length == 0 && state.Movement().Category == "insufficient",
                "Deleted history survived reopening");
            state.LocationResult(TimeSpan.Zero, clock, 36, 140);
            var reopened = Open();
            check(reopened.View().RecordedCount == 1 && reopened.View().LastPosition?.Latitude == 36 &&
                new FileInfo(path).Length == 8 + 24, "Recording after deletion did not start a fresh history");
            store.Clear();
            store.Clear();
            check(Open().View().RecordedCount == 0, "Deleting empty history failed");
            var corruptStore = new PositionStore(legacy);
            corruptStore.Clear();
            check(corruptStore.Load(clock).Count == 0, "Corrupt history could not be deleted");
            check(Directory.Exists(directory), "History deletion removed its containing directory");
        }
        finally { Directory.Delete(directory, true); }
    }
}
