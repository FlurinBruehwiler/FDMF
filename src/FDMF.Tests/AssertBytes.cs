using System.Text;

namespace FDMF.Tests;

public static class AssertBytes
{
    public static void Equal(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (expected.SequenceEqual(actual))
            return;

        var message = BuildDiffMessage(expected, actual);
        Assert.Fail(message);
    }

    private static string BuildDiffMessage(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Byte sequences differ.");
        sb.AppendLine($"Expected length: {expected.Length}");
        sb.AppendLine($"Actual length:   {actual.Length}");

        int firstMismatch = -1;
        int shared = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < shared; i++)
        {
            if (expected[i] != actual[i])
            {
                firstMismatch = i;
                break;
            }
        }

        if (firstMismatch == -1 && expected.Length != actual.Length)
            firstMismatch = shared;

        if (firstMismatch >= 0)
        {
            sb.AppendLine($"First mismatch at index: {firstMismatch}");
            if (firstMismatch < expected.Length)
                sb.AppendLine($"Expected[{firstMismatch}]=0x{expected[firstMismatch]:X2}");
            if (firstMismatch < actual.Length)
                sb.AppendLine($"Actual[{firstMismatch}]=0x{actual[firstMismatch]:X2}");
        }

        sb.AppendLine($"Expected: {ToHex(expected)}");
        sb.AppendLine($"Actual:   {ToHex(actual)}");

        return sb.ToString();
    }

    private static string ToHex(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return "<empty>";

        return BitConverter.ToString(data.ToArray());
    }
}
