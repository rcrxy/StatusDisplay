using System.Buffers.Binary;

namespace HostLed;

// Produces raw 32-byte VIA reports. The transport's report ID is not included.
// Session allocation, acknowledgement handling and retries belong to the sender.
public static class SnapshotReportCodec
{
    public static byte[][] Encode(ulong session, uint transaction, uint snapshotVersion,
        IReadOnlyList<DisplayItem> items)
    {
        if (snapshotVersion == 0) throw new ArgumentOutOfRangeException(nameof(snapshotVersion));
        if (items.Count > 108) throw new ArgumentOutOfRangeException(nameof(items));
        var payload = LedItemCodec.Encode(items);
        var count = (items.Count + 1) / 2;
        var reports = new byte[count + 2][];
        reports[0] = Header(7, 2, session, transaction);
        BinaryPrimitives.WriteUInt32LittleEndian(reports[0].AsSpan(16), snapshotVersion);
        reports[0][20] = (byte)count;
        reports[0][21] = (byte)items.Count;
        for (var sequence = 0; sequence < count; sequence++)
        {
            var report = Header(7, 3, session, transaction);
            var itemCount = Math.Min(2, items.Count - sequence * 2);
            report[16] = (byte)sequence;
            report[17] = (byte)itemCount;
            payload.AsSpan(sequence * 12, itemCount * 6).CopyTo(report.AsSpan(18));
            reports[sequence + 1] = report;
        }
        reports[^1] = Header(7, 4, session, transaction);
        return reports;
    }

    public static byte[] GetInfo() => [8, 0xAC, 1, 1, .. new byte[28]];
    public static byte[] GetStatus(ulong session, uint transaction) => Header(8, 5, session, transaction);

    private static byte[] Header(byte via, byte command, ulong session, uint transaction)
    {
        if (session == 0) throw new ArgumentOutOfRangeException(nameof(session));
        if (transaction == 0) throw new ArgumentOutOfRangeException(nameof(transaction));
        var report = new byte[32];
        report[0] = via; report[1] = 0xAC; report[2] = command; report[3] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(report.AsSpan(4), session);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(12), transaction);
        return report;
    }
}
