using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core.Database;

namespace FDMF.Cli.Utils;

public static class EncodingUtils
{
    public static string DecodeScalar(FieldDataType type, byte[] payload)
    {
        if (payload.Length == 0)
            return string.Empty;

        try
        {
            return type switch
            {
                FieldDataType.Integer => MemoryMarshal.Read<long>(payload).ToString(CultureInfo.InvariantCulture),
                FieldDataType.Decimal => MemoryMarshal.Read<decimal>(payload).ToString(CultureInfo.InvariantCulture),
                FieldDataType.String => Encoding.Unicode.GetString(payload),
                FieldDataType.DateTime => MemoryMarshal.Read<DateTime>(payload).ToString("O", CultureInfo.InvariantCulture),
                FieldDataType.Boolean => MemoryMarshal.Read<bool>(payload) ? "true" : "false",
                _ => string.Empty
            };
        }
        catch
        {
            return "<invalid>";
        }
    }

    public static ReadOnlySpan<byte> EncodeScalar(FieldDataType type, string value)
    {
        return type switch
        {
            FieldDataType.Integer => EncodeUnmanaged(long.Parse(value, CultureInfo.InvariantCulture)),
            FieldDataType.Decimal => EncodeUnmanaged(decimal.Parse(value, CultureInfo.InvariantCulture)),
            FieldDataType.String => Encoding.Unicode.GetBytes(value),
            FieldDataType.DateTime => EncodeUnmanaged(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            FieldDataType.Boolean => EncodeUnmanaged(ParseBool(value)),
            _ => ReadOnlySpan<byte>.Empty
        };
    }

    private static bool ParseBool(string s)
    {
        if (bool.TryParse(s, out var b))
            return b;
        if (s == "0") return false;
        if (s == "1") return true;
        throw new Exception($"Invalid boolean '{s}'");
    }

    private static ReadOnlySpan<byte> EncodeUnmanaged<T>(T value) where T : unmanaged
    {
        Span<byte> buf = stackalloc byte[Unsafe.SizeOf<T>()];
        MemoryMarshal.Write(buf, value);
        return buf.ToArray();
    }
}
