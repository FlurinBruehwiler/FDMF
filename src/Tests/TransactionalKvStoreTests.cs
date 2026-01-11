using LightningDB;
using Shared.Database;
using Shared.Utils;

namespace Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public class TransactionalKvStoreTests
{
    [Fact]
    public void ReadOnly_Forwards_Reads_And_Blocks_Writes()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000), readOnly: true);

        Assert.Equal(ResultCode.Success, store.Get([1], out var value));
        AssertBytes.Equal([(byte)2], value);

        Assert.Throws<InvalidOperationException>(() => store.Put([3], [6]));
        Assert.Throws<InvalidOperationException>(() => store.Delete([1]));

        using var cursor = store.CreateCursor();
        Assert.Equal(ResultCode.Success, cursor.SetRange([0]));
        AssertBytes.Equal([(byte)2], cursor.GetCurrent().Value);
        Assert.Throws<InvalidOperationException>(() => cursor.Delete());
    }

    [Fact]
    public void Data_From_The_Base_Set_Is_Visible()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        Assert.Equal(ResultCode.Success, store.Get([1], out var value));
        AssertBytes.Equal([(byte)2], value);
    }

    [Fact]
    public void Data_From_The_Change_Set_Is_Visible()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([3], [6]);

        Assert.Equal(ResultCode.Success, store.Get([3], out var value));
        AssertBytes.Equal([(byte)6], value);
    }

    [Fact]
    public void Data_From_The_Change_Overrides_Base_Set()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([1], [3]);

        Assert.Equal(ResultCode.Success, store.Get([1], out var value));
        AssertBytes.Equal([(byte)3], value);
    }

    [Fact]
    public void Entry_Can_Be_Deleted()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));
    }

    [Fact]
    public void Entry_Can_Be_Deleted_And_Added_Again()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));

        store.Put([1], [3]);

        Assert.Equal(ResultCode.Success, store.Get([1], out var value));
        AssertBytes.Equal([(byte)3], value);
    }

    [Fact]
    public void Entry_Can_Be_Overriden_And_Then_Deleted()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([1], [3]);

        Assert.Equal(ResultCode.Success, store.Get([1], out var value));
        AssertBytes.Equal([(byte)3], value);

        store.Delete([1]);

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));
    }

    [Fact]
    public void Cursor_Simple()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([2], [3]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)2], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Simple_2()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [2], [3]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([1], [2]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)2], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Simple_3()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([1], [3]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)3], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Complex()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([4], [8]);
        store.Put([5], [10]);
        store.Put([6], [12]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)2], cursor.Next().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        AssertBytes.Equal([(byte)8], cursor.Next().Value);
        AssertBytes.Equal([(byte)10], cursor.Next().Value);
        AssertBytes.Equal([(byte)12], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Complex_2()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Put([1], [1]);
        store.Put([2], [2]);
        store.Put([3], [3]);
        store.Put([4], [4]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)2], cursor.Next().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        AssertBytes.Equal([(byte)4], cursor.Next().Value);
        AssertBytes.Equal([(byte)10], cursor.Next().Value);
        AssertBytes.Equal([(byte)12], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Delete([5]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        AssertBytes.Equal([(byte)8], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)12], cursor.Next().Value);

        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_2()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Delete([6]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        AssertBytes.Equal([(byte)8], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)10], cursor.Next().Value);

        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_3()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Delete([4]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        AssertBytes.Equal([(byte)10], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)12], cursor.Next().Value);

        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_4()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
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

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        store.Delete([4]);
        store.Delete([5]);
        store.Delete([6]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);

        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Base_Entry()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();

            tx.Put(db, [1], [1]);
            tx.Put(db, [2], [2]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));

        AssertBytes.Equal([(byte)2], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_ChangeSet_Only_Entry()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));
        store.Put([1], [1]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Overridden_Base_Entry()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();
            tx.Put(db, [1], [1]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));
        store.Put([1], [9]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)9], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);
    }
}
