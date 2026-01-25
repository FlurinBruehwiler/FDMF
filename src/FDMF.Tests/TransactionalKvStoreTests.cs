using FDMF.Core.Database;
using FDMF.Core.Utils;
using LightningDB;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class TransactionalKvStoreTests
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

        AssertBytes.Equal([(byte)2], cursor.Next().Value);
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

    [Fact]
    public void Cursor_Delete_Overridden_Base_Entry_2()
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
        store.Put([1], [9]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)9], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Overridden_Base_Entry_3()
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
        store.Put([1], [9]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([1]);

        AssertBytes.Equal([(byte)9], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([1], out _));
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Overridden_Base_Entry_4()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();
            tx.Put(db, [3],   [2]);
            tx.Put(db, [8],   [3]);
            tx.Put(db, [100], [4]);
            tx.Put(db, [200], [5]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));
        store.Put([1], [10]);
        store.Put([2], [11]);
        store.Put([3], [12]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([2]);

        AssertBytes.Equal([(byte)11], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([2], out _));
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        Assert.Equal([(byte)12], cursor.Next().Value);
    }

    [Fact]
    public void Cursor_Delete_Then_Put_Same_Key_GetCurrent_Stays_NotFound_Until_Next()
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
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        cursor.SetRange([2]);

        AssertBytes.Equal([(byte)2], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.Success, cursor.Delete());
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        // Interleave a non-cursor operation: re-insert same key in changeset.
        store.Put([2], [99]);
        Assert.Equal(ResultCode.Success, store.Get([2], out var afterPut));
        AssertBytes.Equal([(byte)99], afterPut);

        // LMDB semantics: GetCurrent stays NotFound until cursor is moved.
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        // Next() should now land on the newly inserted key (same key) before advancing to later keys.
        AssertBytes.Equal([(byte)99], cursor.Next().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Then_Put_Same_Key_With_Base_Override()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();
            tx.Put(db, [2], [20]);
            tx.Put(db, [3], [30]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        // Override base.
        store.Put([2], [21]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([2]);
        AssertBytes.Equal([(byte)21], cursor.GetCurrent().Value);

        // Delete via cursor.
        Assert.Equal(ResultCode.Success, cursor.Delete());
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);
        Assert.Equal(ResultCode.NotFound, store.Get([2], out _));

        // Put again with a new value.
        store.Put([2], [22]);
        Assert.Equal(ResultCode.Success, store.Get([2], out var v));
        AssertBytes.Equal([(byte)22], v);

        // Cursor still NotFound until moved.
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        // Next should return the re-inserted value, then next base key.
        AssertBytes.Equal([(byte)22], cursor.Next().Value);
        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Then_Store_Delete_Then_Put_Still_Visible_On_Next()
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
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        cursor.SetRange([2]);
        Assert.Equal(ResultCode.Success, cursor.Delete());

        // Non-cursor deletes should keep the key hidden.
        store.Delete([2]);
        Assert.Equal(ResultCode.NotFound, store.Get([2], out _));

        // Recreate it.
        store.Put([2], [200]);
        Assert.Equal(ResultCode.Success, store.Get([2], out var v));
        AssertBytes.Equal([(byte)200], v);

        // Cursor sees it only after Next.
        AssertBytes.Equal([(byte)200], cursor.Next().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_Then_Put_New_Max_Key_Next_Returns_It()
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
        Assert.Equal(ResultCode.Success, cursor.SetRange([0]));

        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)2], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // Interleave: append a new max key into the changeset.
        store.Put([3], [30]);
        Assert.Equal(ResultCode.Success, store.Get([3], out var v));
        AssertBytes.Equal([(byte)30], v);

        // Cursor was exhausted; next Next() should surface the newly appended key.
        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_Then_Put_New_Max_Key_When_Base_Empty()
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

        store.Put([1], [10]);
        store.Put([2], [20]);

        using var cursor = store.CreateCursor();
        Assert.Equal(ResultCode.Success, cursor.SetRange([0]));

        AssertBytes.Equal([(byte)10], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)20], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        store.Put([3], [30]);

        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_Then_Delete_Last_Then_Put_New_Last_Next_Returns_It()
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
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        Assert.Equal(ResultCode.Success, cursor.SetRange([0]));

        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)2], cursor.Next().Value);
        AssertBytes.Equal([(byte)3], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // Delete last key and add a new last key.
        store.Delete([3]);
        store.Put([4], [40]);

        AssertBytes.Equal([(byte)40], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_GetCurrent_Stays_NotFound_Until_Next_When_New_Key_Appended()
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
        cursor.SetRange([0]);
        cursor.Next();
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        store.Put([3], [30]);
        Assert.Equal(ResultCode.Success, store.Get([3], out _));

        // Still not-found until moved.
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_Then_Update_Last_Key_Does_Not_Resurface_On_Next()
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
        cursor.SetRange([0]);
        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)2], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // Update existing last key.
        store.Put([2], [99]);
        Assert.Equal(ResultCode.Success, store.Get([2], out var v));
        AssertBytes.Equal([(byte)99], v);

        // Key 2 was already visited; we should not see it again just because its value changed.
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_With_Trailing_DeleteMarker_Then_Append_New_Key()
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

        // Delete the last key in the changeset so the merged cursor ends at key=1.
        store.Delete([2]);

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);
        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // Append new max key.
        store.Put([3], [30]);

        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_At_End_Then_Insert_Key_In_Middle_Does_Not_Resurface()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();
            tx.Put(db, [10], [10]);
            tx.Put(db, [20], [20]);
            tx.Put(db, [30], [30]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        cursor.SetRange([0]);
        AssertBytes.Equal([(byte)10], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)20], cursor.Next().Value);
        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // Insert a key that would belong "in the middle" of the already-visited range.
        store.Put([25], [25]);
        Assert.Equal(ResultCode.Success, store.Get([25], out var v));
        AssertBytes.Equal([(byte)25], v);

        // Cursor is exhausted; Next should NOT go backwards to surface newly inserted middle keys.
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // But appending beyond last should still be visible.
        store.Put([40], [40]);
        AssertBytes.Equal([(byte)40], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
    }

    [Fact]
    public void Cursor_Delete_Then_Insert_Smaller_Key_Next_Does_Not_Go_Backwards()
    {
        using var env = new LightningEnvironment(DatabaseCollection.GetTempDbDirectory());
        env.Open();

        LightningDatabase db;
        using (var tx = env.BeginTransaction())
        {
            db = tx.OpenDatabase();
            tx.Put(db, [10], [10]);
            tx.Put(db, [20], [20]);
            tx.Put(db, [30], [30]);
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        cursor.SetRange([20]);
        AssertBytes.Equal([(byte)20], cursor.GetCurrent().Value);

        Assert.Equal(ResultCode.Success, cursor.Delete());
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);
        Assert.Equal(ResultCode.NotFound, store.Get([20], out _));

        // Insert a smaller key than the deleted key.
        store.Put([15], [15]);
        Assert.Equal(ResultCode.Success, store.Get([15], out var v));
        AssertBytes.Equal([(byte)15], v);

        // Next() should proceed forward from the deletion point, not go backwards to key=15.
        AssertBytes.Equal([(byte)30], cursor.Next().Value);
        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);

        // If we explicitly reseek, we can still reach 15.
        Assert.Equal(ResultCode.Success, cursor.SetRange([0]));
        AssertBytes.Equal([(byte)10], cursor.GetCurrent().Value);
        AssertBytes.Equal([(byte)15], cursor.Next().Value);
    }

    [Fact]
    public void Cursor_SetRange_After_Delete_Clears_AfterDelete_State()
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
            tx.Commit();
        }

        using var dbHandle = db;
        using var store = new TransactionalKvStore(env, db, new Arena(1000));

        using var cursor = store.CreateCursor();
        cursor.SetRange([2]);
        Assert.Equal(ResultCode.Success, cursor.Delete());
        Assert.Equal(ResultCode.NotFound, cursor.GetCurrent().ResultCode);

        // Reseek should behave like LMDB and put the cursor on a valid entry again.
        Assert.Equal(ResultCode.Success, cursor.SetRange([0]));
        AssertBytes.Equal([(byte)1], cursor.GetCurrent().Value);
    }

    [Fact]
    public void Cursor_Delete_Test()
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

        using var cursor = store.CreateCursor();

        store.Put([1], [1]);
        store.Put([2], [2]);
        store.Put([3], [3]);

        cursor.SetRange([2]);

        Assert.Equal([(byte)2], cursor.GetCurrent().Value);
        store.Delete([(byte)1]);

        Assert.Equal(ResultCode.Success, cursor.Delete());

        Assert.Equal(ResultCode.NotFound, store.Get([(byte)2], out _));
        Assert.Equal(ResultCode.Success, store.Get([(byte)3], out _));
    }
}
