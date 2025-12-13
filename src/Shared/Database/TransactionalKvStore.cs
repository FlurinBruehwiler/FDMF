using System.Diagnostics;
using LightningDB;

namespace Shared.Database;

public sealed class TransactionalKvStore : IDisposable
{
    public readonly LightningTransaction ReadTransaction;
    public readonly LightningDatabase Database;
    public readonly LightningEnvironment Environment;

    //todo we should combine these changesets, the problem is, we need a flag to indicate if a value was deleted or not,
    //if we have the flag in the value, we have to copy the value around (to insert the flag at the beginning),
    //so we could also just append it to the key, which is usually smaller....
    public readonly BPlusTree ChangeSet = new();

    public TransactionalKvStore(LightningEnvironment env, LightningDatabase database)
    {
        ReadTransaction = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        Database = database;
        Environment = env;
    }

    public void Commit()
    {
        var writeTransaction = Environment.BeginTransaction();

        //loop over changeset and apply changes
        var cursor = ChangeSet.CreateCursor();
        if (cursor.SetRange([0]) == ResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (value[0] == (byte)ValueFlag.AddModify)
                {
                    writeTransaction.Put(Database, key.AsSpan(), value.AsSpan(1));
                }
                else if (value[0] == (byte)ValueFlag.Delete)
                {
                    writeTransaction.Delete(Database, key);
                }
                else
                {
                    Console.WriteLine("Invalid value!!");
                }

            } while (cursor.Next().resultCode == ResultCode.Success);
        }
    }

    public ResultCode Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        //remove from deleteChangeSet if exists
        // DeleteChangeSet.Delete(key.ToArray());

        //todo, don't call ToArray
        return ChangeSet.Put(key.ToArray(), WrapValue(value.ToArray(), ValueFlag.AddModify));
    }

    public (ResultCode resultCode, Slice<byte> key, byte[] value) Get(ReadOnlySpan<byte> key)
    {
        //check the deleted changeset
        //check the additive changeset
        //check the lmdb transaction

        //todo don't call ToArray
        var (additiveResult, _, newValue) = ChangeSet.Get(key);
        if (additiveResult == ResultCode.Success)
        {
            if(newValue[0] == (byte)ValueFlag.Delete)
                return (ResultCode.NotFound, Slice<byte>.Empty(), []);

            return (ResultCode.Success, new Slice<byte>(key), newValue.Slice(1).ToArray());
        }

        var (lmdbResult, _, value) = ReadTransaction.Get(Database, key);
        if (lmdbResult == MDBResultCode.Success)
            return (ResultCode.Success, new Slice<byte>(key), value.CopyToNewArray()); //todo don't create array

        return (ResultCode.NotFound, Slice<byte>.Empty(), []);
    }

    public ResultCode Delete(ReadOnlySpan<byte> key)
    {
        if (ReadTransaction.Get(Database, key).resultCode == MDBResultCode.Success)
        {
            ChangeSet.Put(key.ToArray(), [ (byte)ValueFlag.Delete ]);
        }
        else
        {
            ChangeSet.Delete(key.ToArray());
        }

        return ResultCode.Success; //todo should we check if it existed
    }

    public Cursor CreateCursor()
    {
        return new Cursor
        {
            LightningCursor = ReadTransaction.CreateCursor(Database),
            ChangeSetCursor = ChangeSet.CreateCursor(),
            ChangeSet = ChangeSet
        };
    }

    private static byte[] WrapValue(ReadOnlySpan<byte> data, ValueFlag flag)
    {
        var arr = new byte[data.Length + 1];
        arr[0] = (byte)flag;
        data.CopyTo(arr.AsSpan(1));
        return arr;
    }

    public class Cursor : IDisposable
    {
        public required LightningCursor LightningCursor;
        public required BPlusTree.Cursor ChangeSetCursor;

        public bool BaseIsFinished;
        public bool ChangeIsFinished;

        public required BPlusTree ChangeSet;

        public ResultCode SetRange(ReadOnlySpan<byte> key)
        {
            BaseIsFinished = LightningCursor.SetRange(key) == MDBResultCode.NotFound;
            ChangeIsFinished = ChangeSetCursor.SetRange(key.ToArray()) == ResultCode.NotFound;

            if (!ChangeIsFinished && !BaseIsFinished)
            {
                var a = LightningCursor.GetCurrent();
                var b = ChangeSetCursor.GetCurrent();

                if (BPlusTree.CompareSpan(a.key.AsSpan(), b.key) == 0 && b.value[0] == (byte)ValueFlag.Delete)
                {
                    return Next().resultCode;
                }
            }

            return ResultCode.Success;
        }

        public ResultCode Delete()
        {
            var baseSet = LightningCursor.GetCurrent();
            var changeSet = ChangeSetCursor.GetCurrent();

            var comp = BPlusTree.CompareSpan(baseSet.key.AsSpan(), changeSet.key.AsSpan());
            if (comp <= 0)
            {
                ChangeSet.Put(baseSet.key.CopyToNewArray(), []);
            }
            else
            {
                //change is smaller than base
                ChangeSetCursor.Delete();
            }

            return ResultCode.Success;
        }

        public (ResultCode resultCode, byte[] key, byte[] value) GetCurrent()
        {
            var baseSet = LightningCursor.GetCurrent();
            var changeSet = ChangeSetCursor.GetCurrent();

            //return the lower, if both are the same, return changeSet

            var comp = BPlusTree.CompareSpan(baseSet.key.AsSpan(), changeSet.key.AsSpan());
            if (ChangeIsFinished || (comp < 0 && !BaseIsFinished))
            {
                // baseSet is smaller than changeSet
                return (ResultCode.Success, baseSet.key.CopyToNewArray(), baseSet.value.CopyToNewArray());
            }

            Debug.Assert(changeSet.value[0] == (byte)ValueFlag.AddModify);

            return (ResultCode.Success, changeSet.key, changeSet.value.AsSpan(1).ToArray());
        }

        public (ResultCode resultCode, byte[] key, byte[] value) Next()
        {
            //we are currently on a value, it is either from the read or the change dataset
            //both read and change have a current value
            //we take the lower of the two and call next

            //if both are the same, we advance both. GetCurrent returns the lower value!

            next:
            var a = LightningCursor.GetCurrent();
            var b = ChangeSetCursor.GetCurrent();

            var comp = BPlusTree.CompareSpan(a.key.AsSpan(), b.key.AsSpan());

            if (comp == 0)
            {
                AdvanceBase();
                if (AdvanceChange())
                {
                    goto next;
                }
            }
            else
            {
                if (ChangeIsFinished || (!BaseIsFinished && comp < 0))
                {
                    AdvanceBase();
                    if (b.value[0] == (byte)ValueFlag.Delete && BPlusTree.CompareSpan(LightningCursor.GetCurrent().key.AsSpan(), b.key.AsSpan()) == 0)
                    {
                        goto next;
                    }
                }
                else if (BaseIsFinished || (!ChangeIsFinished && comp > 0))
                {
                    if (AdvanceChange())
                    {
                        goto next;
                    }
                }
            }

            if (ChangeIsFinished && BaseIsFinished)
                return (ResultCode.NotFound, [], []);

            return GetCurrent();

            void AdvanceBase()
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
            }

            bool AdvanceChange()
            {
                var (result, _, value) = ChangeSetCursor.Next();

                ChangeIsFinished = result == ResultCode.NotFound;

                if (result == ResultCode.Success)
                {
                    if (value[0] == (byte)ValueFlag.Delete)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Dispose()
        {
            LightningCursor.Dispose();
        }
    }

    public void Dispose()
    {
        ReadTransaction.Dispose();
    }
}