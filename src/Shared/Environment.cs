using System.Runtime.InteropServices;
using LightningDB;
using Shared.Database;

namespace Shared;

public class Environment
{
    public required LightningEnvironment LightningEnvironment;
    public required LightningDatabase ObjectDb;
    public required LightningDatabase HistoryDb;
    public required LightningDatabase StringSearchIndex;
    public required LightningDatabase NonStringSearchIndex;
    public required ProjectModel Model;

    public static Environment Create(ProjectModel model, string dbName = "database")
    {
        //during testing we delete the old db
        if (Directory.Exists(dbName))
        {
            Directory.Delete(dbName, recursive: true);
        }

        var env = new LightningEnvironment(dbName, new EnvironmentConfiguration
        {
            MaxDatabases = 128
        });
        env.Open();

        using var lightningTransaction = env.BeginTransaction();

        var objDb = lightningTransaction.OpenDatabase(null, new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create
        });

        var histDb = lightningTransaction.OpenDatabase(name: "HistoryDb", new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create
        });

        var stringSearchIndex = lightningTransaction.OpenDatabase(name: "StringIndexDb", new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort
        });

        //we could combine string and nonstring index dbs, the reason why they are separated for now,
        //is that I'm not sure about the performance of custom comparers, they have to be slower because they involve dynamic dispatch,
        //but I'm not sure if this actually makes a difference
        var customComparer = new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort,
        };
        customComparer.CompareWith(new CustomIndexComparer());
        var nonStringSearchIndex = lightningTransaction.OpenDatabase(name: "NonStringIndexDb", customComparer);

        lightningTransaction.Commit();

        return new Environment
        {
            LightningEnvironment = env,
            ObjectDb = objDb,
            HistoryDb = histDb,
            StringSearchIndex = stringSearchIndex,
            NonStringSearchIndex = nonStringSearchIndex,
            Model = model
        };
    }
}

public class CustomIndexComparer : IComparer<MDBValue>
{
    public enum Comparison : byte
    {
        SignedLong,
        DateTime,
        Decimal,
        Assoc,
        Type
    }

    public static int CompareStatic(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a[0] != b[0])
        {
            return a[0].CompareTo(b[0]);
        }

        var aData = a.Slice(1 + 16);
        var bData = b.Slice(1 + 16);

        var comparison = (Comparison)a[0];

        return comparison switch
        {
            Comparison.SignedLong => CompareGeneric<long>(aData, bData),
            Comparison.DateTime => CompareGeneric<DateTime>(aData, bData),
            Comparison.Decimal => CompareGeneric<decimal>(aData, bData),
            Comparison.Assoc => BPlusTree.CompareLexicographic(aData, bData),
            Comparison.Type => BPlusTree.CompareLexicographic(aData, bData),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static int CompareGeneric<T>(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y) where T : unmanaged, IComparable<T>
    {
        return MemoryMarshal.Read<T>(x).CompareTo(MemoryMarshal.Read<T>(y));
    }

    public int Compare(MDBValue a, MDBValue b)
    {
        return CompareStatic(a.AsSpan(), b.AsSpan());
    }
}

public class GuidComparer : IComparer<Guid>
{
    public int Compare(Guid x, Guid y)
    {
        return BPlusTree.CompareLexicographic(x.AsSpan(), y.AsSpan());
    }
}