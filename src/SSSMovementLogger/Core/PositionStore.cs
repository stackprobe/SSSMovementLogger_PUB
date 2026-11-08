namespace SSSMovementLogger.Core;

internal sealed record StoredPositions(long Count, PositionPoint? Last, PositionPoint[] Recent);

// SSSMovementLogger-only format: 8-byte header followed by 24-byte records.
// Only the conversation window is retained in RAM; history on disk is never aged out.
internal sealed class PositionStore(string path)
{
    public const string FileName = "positions.bin";
    private const int Magic = 0x31505353;
    private static readonly object Gate = new();

    public void Clear()
    {
        lock (Gate) File.Delete(path);
    }

    public StoredPositions Load(DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!File.Exists(path)) return new(0, null, []);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            if (stream.Length < 8 || reader.ReadInt32() != Magic)
                throw new InvalidDataException("位置記録のヘッダーが壊れています。ファイルは保持しています。");
            if (reader.ReadInt32() != -1)
                throw new InvalidDataException("位置記録の形式が不正です。ファイルは保持しています。");
            var count = (stream.Length - 8) / 24;
            var recent = new List<PositionPoint>();
            PositionPoint? last = null;
            for (long i = 0; i < count; i++)
            {
                var point = new PositionPoint(i + 1, new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
                    reader.ReadDouble(), reader.ReadDouble());
                if (!Valid(point.Latitude, point.Longitude) || (last is not null && point.Time <= last.Time))
                    throw new InvalidDataException("位置記録が不正です。ファイルは保持しています。");
                last = point;
                if (point.Time >= now - LoggerSettings.ConversationWindow && point.Time <= now) recent.Add(point);
            }
            // Recover an interrupted final append, keeping every complete record.
            if (stream.Length != 8 + count * 24) stream.SetLength(8 + count * 24);
            stream.Flush(true);
            return new(count, last, recent.ToArray());
        }
    }

    public void Append(PositionPoint point)
    {
        lock (Gate)
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            using var writer = new BinaryWriter(stream);
            if (stream.Length == 0) { writer.Write(Magic); writer.Write(-1); }
            var committedLength = stream.Length;
            try
            {
                stream.Position = committedLength;
                writer.Write(point.Time.UtcTicks);
                writer.Write(point.Latitude);
                writer.Write(point.Longitude);
                writer.Flush();
                stream.Flush(true);
            }
            catch { stream.SetLength(committedLength); throw; }
        }
    }

    internal static bool Valid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) &&
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}
