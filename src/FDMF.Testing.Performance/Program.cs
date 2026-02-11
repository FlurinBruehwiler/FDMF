using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FDMF.Testing.Shared;

namespace PerformanceTests;

public class RunResult
{
    public required DateTime Time;
    public required int PerRunCount;
    public required int Runs;
    public required double Average;
    public required double Median;
    public required double Min;
    public required double Max;
}

public sealed class Program
{
    public static void Main()
    {
        try
        {
            // RunTest<WriteTests>();
            RunTest<TraversingAssocsTest>();

            //Ideas for performance tests:
            //Database Read
            //Database Write
            //Path Evaluation
        }
        catch (Exception e)
        {
            Console.WriteLine(e.InnerException);
            Console.WriteLine(e);
        }
    }

    public static void RunTest<T>() where T : IPerformanceTest, new()
    {
        const int runCount = 10;

        Console.WriteLine($"Running {typeof(T).Name} ({runCount} iterations)");

        var counts = T.Counts.OrderBy(x => x).ToArray();

        //warmup
        RunTestWithCount<T>(counts.First());

        foreach (var count in counts)
        {
            List<double> runs = [];

            for (int i = 0; i < runCount; i++)
            {
                var res = RunTestWithCount<T>(count);
                if (res == 0)
                    return; //error

                runs.Add(res);
            }

            var result = new RunResult
            {
                Max = runs.Max(),
                Min = runs.Min(),
                Average = runs.Average(),
                Median = Median(runs),
                Runs = runCount,
                PerRunCount = count,
                Time = DateTime.Now,
            };
            Console.WriteLine($"Count {count} took {result.Average}ms");

            var serializedResult = JsonSerializer.Serialize(result, new JsonSerializerOptions{ IncludeFields = true});

            File.AppendAllLines(Path.Combine(GetRunHistoryDirectory(), $"{typeof(T).Name}.txt"), [serializedResult]);
        }
    }

    private static double RunTestWithCount<T>(int count) where T : IPerformanceTest, new()
    {
        try
        {
            TempDbHelper.ClearDatabases();

            using var test = new T();

            var s = Stopwatch.GetTimestamp();

            test.Run(count);

            return Stopwatch.GetElapsedTime(s).TotalMilliseconds;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return 0;
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        int n = sorted.Length;

        if (n == 0)
            throw new InvalidOperationException("Sequence contains no elements");

        if (n % 2 == 1)
            return sorted[n / 2];

        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static string GetRunHistoryDirectory([CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "RunHistory");
    }
}