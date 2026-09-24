using System.Buffers.Binary;
using System.Text;

namespace NpApi.Api.Tests.Infrastructure;

// Builds JPEG bytes carrying real EXIF (TIFF) metadata, without an imaging library: SOI, an APP1
// "Exif" segment, EOI. Metadata readers stop before image data, so no pixels are needed; the image
// host is faked in tests.
public static class TestImages
{
    public static byte[] Jpeg(
        DateTime? dateTimeOriginal = null,
        string? offsetTimeOriginal = null,
        (double Latitude, double Longitude)? gps = null,
        DateTime? gpsUtc = null)
    {
        var exifEntries = new List<Entry>();
        if (dateTimeOriginal is { } taken)
        {
            exifEntries.Add(Ascii(0x9003, taken.ToString("yyyy:MM:dd HH:mm:ss")));
        }
        if (offsetTimeOriginal is not null)
        {
            exifEntries.Add(Ascii(0x9011, offsetTimeOriginal));
        }

        var gpsEntries = new List<Entry>();
        if (gps is { } position)
        {
            gpsEntries.Add(new Entry(0x0000, 1, 4, [2, 3, 0, 0])); // GPSVersionID
            gpsEntries.Add(Ascii(0x0001, position.Latitude >= 0 ? "N" : "S"));
            gpsEntries.Add(Rationals(0x0002, ToDegreesMinutesSeconds(position.Latitude)));
            gpsEntries.Add(Ascii(0x0003, position.Longitude >= 0 ? "E" : "W"));
            gpsEntries.Add(Rationals(0x0004, ToDegreesMinutesSeconds(position.Longitude)));
        }
        if (gpsUtc is { } utc)
        {
            gpsEntries.Add(Rationals(0x0007, [(utc.Hour, 1), (utc.Minute, 1), (utc.Second, 1)]));
            gpsEntries.Add(Ascii(0x001D, utc.ToString("yyyy:MM:dd")));
        }

        var tiff = BuildTiff(exifEntries, gpsEntries);

        using var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);                                   // SOI
        jpeg.Write([0xFF, 0xE1]);                                   // APP1
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(2 + 6 + tiff.Length));
        jpeg.Write(length);
        jpeg.Write("Exif\0\0"u8);
        jpeg.Write(tiff);
        jpeg.Write([0xFF, 0xD9]);                                   // EOI
        return jpeg.ToArray();
    }

    public static byte[] NotAnImage() => "definitely not an image"u8.ToArray();

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Data);

    private static Entry Ascii(ushort tag, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        return new Entry(tag, 2, (uint)bytes.Length, bytes);
    }

    private static Entry Rationals(ushort tag, (long Numerator, long Denominator)[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8), (uint)values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8 + 4), (uint)values[i].Denominator);
        }
        return new Entry(tag, 5, (uint)values.Length, bytes);
    }

    private static (long, long)[] ToDegreesMinutesSeconds(double value)
    {
        value = Math.Abs(value);
        var degrees = Math.Floor(value);
        var minutes = Math.Floor((value - degrees) * 60);
        var seconds = (value - degrees - minutes / 60) * 3600;
        return [((long)degrees, 1), ((long)minutes, 1), ((long)Math.Round(seconds * 10000), 10000)];
    }

    // Little-endian TIFF: IFD0 points at the Exif and GPS sub-IFDs, each followed by its data area.
    private static byte[] BuildTiff(List<Entry> exif, List<Entry> gps)
    {
        const int headerSize = 8;
        var ifd0Count = (exif.Count > 0 ? 1 : 0) + (gps.Count > 0 ? 1 : 0);
        var ifd0Size = IfdSize(ifd0Count, []);
        var exifOffset = headerSize + ifd0Size;
        var gpsOffset = exifOffset + (exif.Count > 0 ? IfdSize(exif.Count, exif) : 0);

        var ifd0 = new List<Entry>();
        if (exif.Count > 0)
        {
            ifd0.Add(Long(0x8769, (uint)exifOffset));
        }
        if (gps.Count > 0)
        {
            ifd0.Add(Long(0x8825, (uint)gpsOffset));
        }

        using var tiff = new MemoryStream();
        tiff.Write("II"u8);
        tiff.Write([0x2A, 0x00, 0x08, 0x00, 0x00, 0x00]);
        WriteIfd(tiff, ifd0, headerSize);
        if (exif.Count > 0)
        {
            WriteIfd(tiff, exif, exifOffset);
        }
        if (gps.Count > 0)
        {
            WriteIfd(tiff, gps, gpsOffset);
        }
        return tiff.ToArray();
    }

    private static Entry Long(ushort tag, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return new Entry(tag, 4, 1, bytes);
    }

    private static int IfdSize(int count, List<Entry> entries) =>
        2 + count * 12 + 4 + entries.Where(e => e.Data.Length > 4).Sum(e => Padded(e.Data.Length));

    private static int Padded(int length) => length + (length % 2);

    private static void WriteIfd(Stream stream, List<Entry> entries, int ifdOffset)
    {
        var sorted = entries.OrderBy(e => e.Tag).ToList();
        var dataOffset = ifdOffset + 2 + sorted.Count * 12 + 4;
        var data = new MemoryStream();
        var buffer = new byte[12];

        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)sorted.Count);
        stream.Write(buffer, 0, 2);

        foreach (var entry in sorted)
        {
            Array.Clear(buffer);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0), entry.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), entry.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), entry.Count);
            if (entry.Data.Length <= 4)
            {
                entry.Data.CopyTo(buffer, 8);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), (uint)(dataOffset + data.Length));
                data.Write(entry.Data);
                if (entry.Data.Length % 2 == 1)
                {
                    data.WriteByte(0);
                }
            }
            stream.Write(buffer, 0, 12);
        }

        stream.Write(new byte[4]); // no next IFD
        data.Position = 0;
        data.CopyTo(stream);
    }
}
