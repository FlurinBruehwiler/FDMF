using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Shared;

[Flags]
public enum LogFlags
{
    None = 0,
    Info = 1,
    Performance = 2,
    Error = 4,
    Business = 8,
    All = Info | Performance | Error | Business
}

public static class Logging
{
    public static LogFlags LogFlags = LogFlags.All;

    public static void Log(LogFlags logFlags, string? message)
    {
        if ((LogFlags & logFlags) != 0)
        {
            Console.WriteLine(message);
        }
    }

    //todo structured logging
    public static void Log(LogFlags logFlags, [InterpolatedStringHandlerArgument("logFlags")] LogInterpolatedStringHandler message)
    {
        if ((LogFlags & logFlags) != 0)
        {
            Console.WriteLine(message.GetFormattedText());
        }
    }

    public static void LogException(Exception? exception)
    {
        if ((LogFlags & LogFlags.Error) != 0)
        {
            if(exception != null)
                Console.WriteLine(exception.ToString());
        }
    }

    public static ConcurrentDictionary<string, int> metrics = [];

    public static void LogMetric(string category)
    {
        metrics.AddOrUpdate(category, _ => 1, (_, i) => i + 1);
    }
}