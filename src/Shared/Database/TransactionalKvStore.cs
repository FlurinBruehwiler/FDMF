using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LightningDB;

namespace Shared.Database;

public sealed class TransactionalKvStore : IDisposable
{
    public readonly LightningTransaction ReadTransaction;
    public readonly LightningDatabase Database;
    public readonly LightningEnvironment Environment;

    // Values in the changeset are prefixed with a ValueFlag.
    public readonly BPlusTree ChangeSet = new();

    private readonly ByteArena _arena = new();



    public TransactionalKvStore(LightningEnvironment env, LightningDatabase database)
    {
        ReadTransaction = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        Database = database;
        Environment = env;
    }

    public void Commit(LightningTransaction writeTransaction)
    {
        var cursor = ChangeSet.CreateCursor();
        if (cursor.SetRange([0]) == ResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (value.Length == 0)
                {
                    Logging.Log(LogFlags.Error, "Invalid value!!");
                    continue;
                }

                if (value[0] == (byte)ValueFlag.AddModify)
                {
                    writeTransaction.Put(Database, key, value.Slice(1));
                }
                else if (value[0] == (byte)ValueFlag.Delete)
                {
                    writeTransaction.Delete(Database, key);
                }
                else
                {
                    Logging.Log(LogFlags.Error, "Invalid value!!");
                }
            } while (cursor.Next().ResultCode == ResultCode.Success);
        }

        ChangeSet.Clear();
        _arena.Reset();
    }

    public ResultCode Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var keySlice = _arena.Copy(key);

        var valueMem = _arena.Allocate(1 + value.Length, out var valueSlice);
        valueMem.Span[0] = (byte)ValueFlag.AddModify;
        value.CopyTo(valueMem.Span.Slice(1));

        return ChangeSet.Put(keySlice, valueSlice);
    }

    public ResultCode Get(ReadOnlySpan<byte> key, [UnscopedRef] out ReadOnlySpan<byte> value)
    {
        var res = ChangeSet.Get(key);
        if (res.ResultCode == ResultCode.Success)
        {
            if (res.Value.Length == 0 || res.Value[0] == (byte)ValueFlag.Delete)
            {
                value = ReadOnlySpan<byte>.Empty;
                return ResultCode.NotFound;
            }

            value = res.Value.Slice(1);
            return ResultCode.Success;
        }

        var (lmdbResult, _, lmdbValue) = ReadTransaction.Get(Database, key);
        if (lmdbResult == MDBResultCode.Success)
        {
            value = lmdbValue.AsSpan();
            return ResultCode.Success;
        }

        value = ReadOnlySpan<byte>.Empty;
        return ResultCode.NotFound;
    }


    public ResultCode Delete(ReadOnlySpan<byte> key)
    {
        if (ReadTransaction.Get(Database, key).resultCode == MDBResultCode.Success)
        {
            var keySlice = _arena.Copy(key);

            var valueMem = _arena.Allocate(1, out var valueSlice);
            valueMem.Span[0] = (byte)ValueFlag.Delete;

            ChangeSet.Put(keySlice, valueSlice);
        }
        else
        {
            ChangeSet.Delete(key);
        }

        return ResultCode.Success;
    }

    public Cursor CreateCursor()
    {
        return new Cursor(this)
        {
            LightningCursor = ReadTransaction.CreateCursor(Database),
            ChangeSetCursor = ChangeSet.CreateCursor()
        };
    }

    public sealed class Cursor : IDisposable
    {
        private readonly TransactionalKvStore _store;

        public required LightningCursor LightningCursor;
        public required BPlusTree.Cursor ChangeSetCursor;

        public bool BaseIsFinished;
        public bool ChangeIsFinished;

        public Cursor(TransactionalKvStore store)
        {
            _store = store;
        }

        public readonly ref struct CursorResult
        {
            public readonly ResultCode ResultCode;
            public readonly ReadOnlySpan<byte> Key;
            public readonly ReadOnlySpan<byte> Value;

            public CursorResult(ResultCode resultCode, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
            {
                ResultCode = resultCode;
                Key = key;
                Value = value;
            }

            public void Deconstruct(out ResultCode resultCode, out ReadOnlySpan<byte> key, out ReadOnlySpan<byte> value)
            {
                resultCode = ResultCode;
                key = Key;
                value = Value;
            }
        }

        public ResultCode SetRange(ReadOnlySpan<byte> key)
        {
            BaseIsFinished = LightningCursor.SetRange(key) == MDBResultCode.NotFound;
            ChangeIsFinished = ChangeSetCursor.SetRange(key) == ResultCode.NotFound;

            if (!ChangeIsFinished && !BaseIsFinished)
            {
                var baseCurrent = LightningCursor.GetCurrent();
                var changeCurrent = ChangeSetCursor.GetCurrent();

                if (BPlusTree.CompareSpan(baseCurrent.key.AsSpan(), changeCurrent.Key) == 0 && changeCurrent.Value.Length > 0 && changeCurrent.Value[0] == (byte)ValueFlag.Delete)
                {
                    return Next().ResultCode;
                }
            }

            return ResultCode.Success;
        }

        public ResultCode Delete()
        {
            var currentKey = GetCurrent().Key;
            if (currentKey.Length == 0)
                return ResultCode.NotFound;

            _store.Delete(currentKey);

            ChangeIsFinished = ChangeSetCursor.SetRange(currentKey) == ResultCode.NotFound;

            return ResultCode.Success;
        }

        public CursorResult GetCurrent()
        {
            if (ChangeIsFinished && BaseIsFinished)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            if (ChangeIsFinished)
            {
                var baseSet = LightningCursor.GetCurrent();
                return new CursorResult(ResultCode.Success, baseSet.key.AsSpan(), baseSet.value.AsSpan());
            }

            if (BaseIsFinished)
            {
                var changeSet = ChangeSetCursor.GetCurrent();
                Debug.Assert(changeSet.Value.Length > 0);
                Debug.Assert(changeSet.Value[0] == (byte)ValueFlag.AddModify);

                return new CursorResult(ResultCode.Success, changeSet.Key, changeSet.Value.Slice(1));
            }

            var baseCurrent = LightningCursor.GetCurrent();
            var changeCurrent = ChangeSetCursor.GetCurrent();

            var comp = BPlusTree.CompareSpan(baseCurrent.key.AsSpan(), changeCurrent.Key);
            if (comp < 0)
            {
                return new CursorResult(ResultCode.Success, baseCurrent.key.AsSpan(), baseCurrent.value.AsSpan());
            }

            if (changeCurrent.Value.Length == 0 || changeCurrent.Value[0] != (byte)ValueFlag.AddModify)
            {
                // This can happen when the current key is deleted.
                return Next();
            }

            return new CursorResult(ResultCode.Success, changeCurrent.Key, changeCurrent.Value.Slice(1));
        }

        public CursorResult Next()
        {
            next:
            if (ChangeIsFinished && BaseIsFinished)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            var baseCurrent = LightningCursor.GetCurrent();
            var changeCurrent = ChangeSetCursor.GetCurrent();

            var comp = BPlusTree.CompareSpan(baseCurrent.key.AsSpan(), changeCurrent.Key);

            if (comp == 0)
            {
                AdvanceBase();
                if (AdvanceChange())
                    goto next;
            }
            else
            {
                if (ChangeIsFinished || (!BaseIsFinished && comp < 0))
                {
                    AdvanceBase();
                    if (!ChangeIsFinished)
                    {
                        if (changeCurrent.Value.Length > 0 && changeCurrent.Value[0] == (byte)ValueFlag.Delete && !BaseIsFinished)
                        {
                            var newBaseCurrent = LightningCursor.GetCurrent();
                            if (BPlusTree.CompareSpan(newBaseCurrent.key.AsSpan(), changeCurrent.Key) == 0)
                                goto next;
                        }
                    }
                }
                else if (BaseIsFinished || (!ChangeIsFinished && comp > 0))
                {
                    if (AdvanceChange())
                        goto next;
                }
            }

            return GetCurrent();

            void AdvanceBase()
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
            }

            bool AdvanceChange()
            {
                var nextChange = ChangeSetCursor.Next();

                ChangeIsFinished = nextChange.ResultCode == ResultCode.NotFound;

                if (nextChange.ResultCode == ResultCode.Success)
                {
                    if (nextChange.Value.Length > 0 && nextChange.Value[0] == (byte)ValueFlag.Delete)
                        return true;
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
        _arena.Dispose();
    }
}
