using LightningDB;
using Shared.Database;

namespace Tests;

public class Fixture
{
    public const string TestDirectory = "TestDbs";

    public Fixture()
    {
        if (Directory.Exists(TestDirectory))
        {
            Directory.Delete(TestDirectory, recursive: true);
        }
    }
}

public class TransactionalKvStoreTests(Fixture fixture) : IClassFixture<Fixture>
{
    private readonly Fixture _fixture = fixture;

    [Fact]
    public void Data_From_The_Base_Set_Is_Visible()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Data_From_The_Base_Set_Is_Visible)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        Assert.Equal([(byte)2], store.Get([1]).value.AsSpan());
    }

    [Fact]
    public void Data_From_The_Change_Set_Is_Visible()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Data_From_The_Change_Set_Is_Visible)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Put([3], [6]);

        Assert.Equal([(byte)6], store.Get([3]).value.AsSpan());
    }

    [Fact]
    public void Data_From_The_Change_Overrides_Base_Set()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Data_From_The_Change_Overrides_Base_Set)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Put([1], [3]);

        Assert.Equal([(byte)3], store.Get([1]).value.AsSpan());
    }

    [Fact]
    public void Entry_Can_Be_Deleted()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Entry_Can_Be_Deleted)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1]).resultCode);
    }

    [Fact]
    public void Entry_Can_Be_Deleted_And_Added_Again()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Entry_Can_Be_Deleted_And_Added_Again)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1]).resultCode);

        store.Put([1], [3]);

        Assert.Equal([(byte)3], store.Get([1]).value.AsSpan());
    }

    [Fact]
    public void Entry_Can_Be_Overriden_And_Then_Deleted()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Entry_Can_Be_Overriden_And_Then_Deleted)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Put([1], [3]);

        Assert.Equal([(byte)3], store.Get([1]).value.AsSpan());

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1]).resultCode);
    }

    [Fact]
    public void Cursor_Simple()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Cursor_Simple)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Put([2], [3]);

        var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        Assert.Equal([(byte)2], cursor.GetCurrent().value);
        Assert.Equal([(byte)3], cursor.Next().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Complex()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Cursor_Complex)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [1]);
            tx.Put(db, [2], [2]);
            tx.Put(db, [3], [3]);
            tx.Put(db, [4], [4]);

            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };

        store.Put([4], [8]);
        store.Put([5], [10]);
        store.Put([6], [12]);

        var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal([(byte)1], cursor.GetCurrent().value);
        Assert.Equal([(byte)2], cursor.Next().value);
        Assert.Equal([(byte)3], cursor.Next().value);
        Assert.Equal([(byte)8], cursor.Next().value);
        Assert.Equal([(byte)10], cursor.Next().value);
        Assert.Equal([(byte)12], cursor.Next().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Complex_2()
    {
        var env = new LightningEnvironment($"{Fixture.TestDirectory}/{nameof(Cursor_Complex_2)}");
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [4], [8]);
            tx.Put(db, [5], [10]);
            tx.Put(db, [6], [12]);

            tx.Commit();
        }

        using var rtx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        var store = new TransactionalKvStore
        {
            Database = db,
            ReadTransaction = rtx,
        };


        store.Put([1], [1]);
        store.Put([2], [2]);
        store.Put([3], [3]);
        store.Put([4], [4]);

        var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal([(byte)1], cursor.GetCurrent().value);
        Assert.Equal([(byte)2], cursor.Next().value);
        Assert.Equal([(byte)3], cursor.Next().value);
        Assert.Equal([(byte)4], cursor.Next().value);
        Assert.Equal([(byte)10], cursor.Next().value);
        Assert.Equal([(byte)12], cursor.Next().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }
}