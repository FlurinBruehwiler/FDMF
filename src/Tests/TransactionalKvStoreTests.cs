using LightningDB;
using Shared.Database;

namespace Tests;

[CollectionDefinition(DatabaseCollection.DatabaseCollectionName)]
public class TransactionalKvStoreTests
{
    [Fact]
    public void Data_From_The_Base_Set_Is_Visible()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        Assert.Equal([(byte)2], store.Get([1]).value.AsSpan());
    }


    [Fact]
    public void Data_From_The_Change_Set_Is_Visible()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Put([3], [6]);

        Assert.Equal([(byte)6], store.Get([3]).value.AsSpan());
    }

    [Fact]
    public void Data_From_The_Change_Overrides_Base_Set()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Put([1], [3]);

        Assert.Equal([(byte)3], store.Get([1]).value.AsSpan());
    }

    [Fact]
    public void Entry_Can_Be_Deleted()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1]).resultCode);
    }

    [Fact]
    public void Entry_Can_Be_Deleted_And_Added_Again()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1]).resultCode);

        store.Put([1], [3]);

        Assert.Equal([(byte)3], store.Get([1]).value.AsSpan());
    }

    [Fact]
    public void Entry_Can_Be_Overriden_And_Then_Deleted()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Put([1], [3]);

        Assert.Equal([(byte)3], store.Get([1]).value.AsSpan());

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1]).resultCode);
    }

    [Fact]
    public void Cursor_Simple()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Put([2], [3]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        Assert.Equal([(byte)2], cursor.GetCurrent().value);
        Assert.Equal([(byte)3], cursor.Next().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Simple_2()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [2], [3]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Put([1], [2]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        Assert.Equal([(byte)2], cursor.GetCurrent().value);
        Assert.Equal([(byte)3], cursor.Next().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Simple_3()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        var store = new TransactionalKvStore(env, db);

        store.Put([1], [3]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        Assert.Equal([(byte)3], cursor.GetCurrent().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Complex()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        var store = new TransactionalKvStore(env, db);

        store.Put([4], [8]);
        store.Put([5], [10]);
        store.Put([6], [12]);

        using var cursor = store.CreateCursor();
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
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        var store = new TransactionalKvStore(env, db);

        store.Put([1], [1]);
        store.Put([2], [2]);
        store.Put([3], [3]);
        store.Put([4], [4]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal([(byte)1], cursor.GetCurrent().value);
        Assert.Equal([(byte)2], cursor.Next().value);
        Assert.Equal([(byte)3], cursor.Next().value);
        Assert.Equal([(byte)4], cursor.Next().value);
        Assert.Equal([(byte)10], cursor.Next().value);
        Assert.Equal([(byte)12], cursor.Next().value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Delete()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        var store = new TransactionalKvStore(env, db);

        store.Delete([5]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal([(byte)8], cursor.GetCurrent().value);
        Assert.Equal([(byte)12], cursor.Next().value);

        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Delete_2()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        var store = new TransactionalKvStore(env, db);

        store.Delete([6]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal([(byte)8], cursor.GetCurrent().value);
        Assert.Equal([(byte)10], cursor.Next().value);

        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Delete_3()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        var store = new TransactionalKvStore(env, db);

        store.Delete([4]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal([(byte)10], cursor.GetCurrent().value);
        Assert.Equal([(byte)12], cursor.Next().value);

        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }

    [Fact]
    public void Cursor_Delete_4()
    {
        var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        var store = new TransactionalKvStore(env, db);

        store.Delete([4]);
        store.Delete([5]);
        store.Delete([6]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal(ResultCode.NotFound, cursor.Next().resultCode);
    }
}