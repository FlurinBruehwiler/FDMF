using System.Diagnostics;
using System.Runtime.InteropServices;
using LightningDB;
using Shared.Database;

namespace PerformanceTests;

public class Program
{
    public static void Main()
    {
        // var tree = new BPlusTree();
        //
        // for (int i = 0; i < 10_000; i++)
        // {
        //     tree.Put([(byte)i], []);
        // }
        //
        // PerformanceTest(() =>
        // {
        //     for (int i = 0; i < 10_000; i++)
        //     {
        //         int x = i;
        //         tree.Get(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)));
        //     }
        // });
        //
        // var env = new LightningEnvironment("performanceTest");
        // env.Open();
        //
        // using var lightningTransaction = env.BeginTransaction();
        //
        // using (var tx = env.BeginTransaction())
        // using (var db = tx.OpenDatabase(configuration: new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
        // {
        //     for (int i = 0; i < 10_000; i++)
        //     {
        //         int x = i;
        //         tx.Put(db, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)), ReadOnlySpan<byte>.Empty);
        //     }
        //
        //
        //     PerformanceTest(() =>
        //     {
        //         for (int i = 0; i < 10_000; i++)
        //         {
        //             int x = i;
        //             tx.Get(db, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)));
        //         }
        //     });
        // }

    }

    public static void PerformanceTest(Action c)
    {
        var stopwatch = Stopwatch.GetTimestamp();

        c();

        var t = Stopwatch.GetElapsedTime(stopwatch);
        Console.WriteLine($"{t.TotalMilliseconds}ms");
    }
}