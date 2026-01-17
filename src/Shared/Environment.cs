using System.Runtime.InteropServices;
using LightningDB;
using Shared.Database;

namespace Shared;

public class Environment : IDisposable
{
    public required LightningEnvironment LightningEnvironment;
    public required LightningDatabase ObjectDb;
    public required LightningDatabase HistoryDb;
    public required LightningDatabase HistoryObjIndexDb;
    public required LightningDatabase StringSearchIndex;
    public required LightningDatabase NonStringSearchIndex;
    public required LightningDatabase FieldPresenceIndex;
    public required ProjectModel Model;

    public static Environment Create(ProjectModel model, string dbName = "database")

    {
        // NOTE: This is intentionally destructive and used by tests/dev.
        if (Directory.Exists(dbName))
        {
            Directory.Delete(dbName, recursive: true);
        }

        return OpenInternal(model, dbName, create: true);
    }

    public static Environment Init(ProjectModel model, string dbName)
    {
        Directory.CreateDirectory(dbName);
        return OpenInternal(model, dbName, create: true);
    }

    public static Environment Open(ProjectModel model, string dbName)
    {
        if (!Directory.Exists(dbName))
            throw new DirectoryNotFoundException($"Database directory '{dbName}' does not exist");

        return OpenInternal(model, dbName, create: false);
    }

    private static Environment OpenInternal(ProjectModel model, string dbName, bool create)
    {
        var env = new LightningEnvironment(dbName, new EnvironmentConfiguration
        {
            MaxDatabases = 128
        });
        env.Open();

        using var lightningTransaction = env.BeginTransaction();

        var createFlag = create ? DatabaseOpenFlags.Create : 0;

        var objDb = lightningTransaction.OpenDatabase(null, new DatabaseConfiguration
        {
            Flags = createFlag
        });

        var histDb = lightningTransaction.OpenDatabase(name: "HistoryDb", new DatabaseConfiguration
        {
            Flags = createFlag
        });

        var historyObjIndexDb = lightningTransaction.OpenDatabase(name: "HistoryObjIndexDb", new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort
        });

        var stringSearchIndex = lightningTransaction.OpenDatabase(name: "StringIndexDb", new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort
        });

        // We could combine string and nonstring index dbs. They're separate for now since custom comparers may be slower.
        var customComparer = new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort,
        };
        customComparer.CompareWith(new CustomIndexComparer());

        var nonStringSearchIndex = lightningTransaction.OpenDatabase(name: "NonStringIndexDb", customComparer);
 
        var fieldPresenceIndex = lightningTransaction.OpenDatabase(name: "FieldPresenceIndexDb", new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort
        });

        lightningTransaction.Commit();
 
        return new Environment
        {
            LightningEnvironment = env,
            ObjectDb = objDb,
            HistoryDb = histDb,
            HistoryObjIndexDb = historyObjIndexDb,
            StringSearchIndex = stringSearchIndex,
            NonStringSearchIndex = nonStringSearchIndex,
            FieldPresenceIndex = fieldPresenceIndex,
            Model = model
        };
    }


    public void Dispose()
    {
        ObjectDb.Dispose();
        HistoryDb.Dispose();
        HistoryObjIndexDb.Dispose();
        StringSearchIndex.Dispose();
        NonStringSearchIndex.Dispose();
        FieldPresenceIndex.Dispose();
        LightningEnvironment.Dispose();
    }
}

public class CustomIndexComparer : IComparer<MDBValue>
{
    public enum Comparison : byte
    {
        SignedLong,
        DateTime,
        Decimal,
        Boolean,
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
            Comparison.Boolean => CompareGeneric<bool>(aData, bData),
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