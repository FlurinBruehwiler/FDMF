using System.Runtime.CompilerServices;
using System.Text;

namespace FDMF.Core;

[InterpolatedStringHandler]
public struct LogInterpolatedStringHandler
{
    private bool isEnabled;
    private StringBuilder _stringBuilder = null!;

    public LogInterpolatedStringHandler(int literalLength, int formattedCount, LogFlags logFlags)
    {
        isEnabled = (Logging.LogFlags & logFlags) != 0;
        if (isEnabled)
        {
            _stringBuilder = new();
        }
    }

    public void AppendLiteral(string s)
    {
        if (isEnabled)
        {
            _stringBuilder.Append(s);
        }
    }

    public void AppendFormatted(ReadOnlySpan<char> ros)
    {
        if (isEnabled)
        {
            _stringBuilder.Append(ros);
        }
    }

    public void AppendFormatted<T>(T t)
    {
        if (isEnabled)
        {
            _stringBuilder.Append(t);
        }
    }

    public string GetFormattedText()
    {
        if (isEnabled)
        {
            return _stringBuilder.ToString();
        }
        else
        {
            return string.Empty;
        }
    }
}