using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LightningDB;

namespace Shared.Database;

public sealed class TransactionalKvStore : IDisposable
{
    public readonly LightningTransaction ReadTransaction;
    public readonly LightningDatabase Database;
    public readonly LightningEnvironment Environment;

    // Flag is stored in the last byte of the key. The changeset tree uses a comparer that ignores that byte.
    public readonly BPlusTree ChangeSet = new(comparer: BPlusTree.CompareIgnoreLastByte);

    private readonly ByteArena _arena = new();
    private byte[] _searchKeyBuffer = new byte[64];

    public TransactionalKvStore(LightningEnvironment env, LightningDatabase database)
    {
        ReadTransaction = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        Database = database;
        Environment = env;
    }

    public void Commit(LightningTransaction writeTransaction)
    {
        var cursor = ChangeSet.CreateCursor();

        Span<byte> startKey = stackalloc byte[2]; // 1 byte key + 1 byte flag
        startKey[0] = 0;
        startKey[1] = 0;

        if (cursor.SetRange(startKey) == ResultCode.Success)
        {
            do
            {
                var (_, keyWithFlag, value) = cursor.GetCurrent();

                if (keyWithFlag.Length == 0)
                {
                    Logging.Log(LogFlags.Error, "Invalid key!!");
                    continue;
                }

                var flag = (ValueFlag)keyWithFlag[^1];
                var key = keyWithFlag.Slice(0, keyWithFlag.Length - 1);

                if (flag == ValueFlag.AddModify)
                {
                    writeTransaction.Put(Database, key, value);
                }
                else if (flag == ValueFlag.Delete)
                {
                    writeTransaction.Delete(Database, key);
                }
                else
                {
                    Logging.Log(LogFlags.Error, "Invalid value flag!!");
                }
            } while (cursor.Next().ResultCode == ResultCode.Success);
        }

        ChangeSet.Clear();
        _arena.Reset();
    }

    public ResultCode Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var keySlice = CopyKeyWithFlag(key, ValueFlag.AddModify);
        var valueSlice = _arena.Copy(value);

        return ChangeSet.Put(keySlice, valueSlice);
    }

    public ResultCode Get(ReadOnlySpan<byte> key, [UnscopedRef] out ReadOnlySpan<byte> value)
    {
        var searchKey = CreateSearchKey(key);

        var res = ChangeSet.Get(searchKey);
        if (res.ResultCode == ResultCode.Success)
        {
            if (res.Key.Length == 0)
            {
                value = ReadOnlySpan<byte>.Empty;
                return ResultCode.NotFound;
            }

            var flag = (ValueFlag)res.Key[^1];
            if (flag == ValueFlag.Delete)
            {
                value = ReadOnlySpan<byte>.Empty;
                return ResultCode.NotFound;
            }

            value = res.Value;
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
            var keySlice = CopyKeyWithFlag(key, ValueFlag.Delete);
            ChangeSet.Put(keySlice, ReadOnlyMemory<byte>.Empty);
        }
        else
        {
            Span<byte> searchKey = stackalloc byte[key.Length + 1];
            key.CopyTo(searchKey);
            searchKey[^1] = 0;
            ChangeSet.Delete(searchKey);
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

    private ReadOnlyMemory<byte> CopyKeyWithFlag(ReadOnlySpan<byte> key, ValueFlag flag)
    {
        var mem = _arena.Allocate(key.Length + 1, out var slice);
        key.CopyTo(mem.Span);
        mem.Span[^1] = (byte)flag;
        return slice;
    }

    private ReadOnlySpan<byte> CreateSearchKey(ReadOnlySpan<byte> key)
    {
        var length = key.Length + 1;
        if (_searchKeyBuffer.Length < length)
        {
            _searchKeyBuffer = new byte[Math.Max(length, _searchKeyBuffer.Length * 2)];
        }

        key.CopyTo(_searchKeyBuffer.AsSpan(0, key.Length));
        _searchKeyBuffer[length - 1] = 0;
        return _searchKeyBuffer.AsSpan(0, length);
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

            Span<byte> searchKey = stackalloc byte[key.Length + 1];
            key.CopyTo(searchKey);
            searchKey[^1] = 0;
            ChangeIsFinished = ChangeSetCursor.SetRange(searchKey) == ResultCode.NotFound;

            if (!ChangeIsFinished && !BaseIsFinished)
            {
                var a = LightningCursor.GetCurrent();
                var b = ChangeSetCursor.GetCurrent();

                var bKey = b.Key.Slice(0, b.Key.Length - 1);
                var bFlag = (ValueFlag)b.Key[^1];

                if (BPlusTree.CompareLexicographic(a.key.AsSpan(), bKey) == 0 && bFlag == ValueFlag.Delete)
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

            Span<byte> searchKey = stackalloc byte[currentKey.Length + 1];
            currentKey.CopyTo(searchKey);
            searchKey[^1] = 0;
            ChangeIsFinished = ChangeSetCursor.SetRange(searchKey) == ResultCode.NotFound;

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
                Debug.Assert(changeSet.Key.Length > 0);
                Debug.Assert((ValueFlag)changeSet.Key[^1] == ValueFlag.AddModify);

                return new CursorResult(ResultCode.Success, changeSet.Key.Slice(0, changeSet.Key.Length - 1), changeSet.Value);
            }

            var baseCurrent = LightningCursor.GetCurrent();
            var changeCurrent = ChangeSetCursor.GetCurrent();

            var changeKey = changeCurrent.Key.Slice(0, changeCurrent.Key.Length - 1);
            var changeFlag = (ValueFlag)changeCurrent.Key[^1];

            var comp = BPlusTree.CompareLexicographic(baseCurrent.key.AsSpan(), changeKey);
            if (comp < 0)
            {
                return new CursorResult(ResultCode.Success, baseCurrent.key.AsSpan(), baseCurrent.value.AsSpan());
            }

            if (changeFlag != ValueFlag.AddModify)
            {
                // This can happen when the current key is deleted.
                return Next();
            }

            return new CursorResult(ResultCode.Success, changeKey, changeCurrent.Value);
        }

        public CursorResult Next()
        {
            // Unfortunately this logic is non-trivial.
            next:
            if (ChangeIsFinished && BaseIsFinished)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            var a = LightningCursor.GetCurrent();
            var b = ChangeSetCursor.GetCurrent();

            var bKey = b.Key.Slice(0, b.Key.Length - 1);
            var bFlag = (ValueFlag)b.Key[^1];

            var comp = BPlusTree.CompareLexicographic(a.key.AsSpan(), bKey);

            if (comp == 0)
            {
                AdvanceBase();
                if (AdvanceChangeSkipDeletes())
                {
                    goto next;
                }
            }
            else
            {
                if (ChangeIsFinished || (!BaseIsFinished && comp < 0))
                {
                    AdvanceBase();
                    if (!ChangeIsFinished)
                    {
                        if (bFlag == ValueFlag.Delete && !BaseIsFinished)
                        {
                            var newBaseCurrent = LightningCursor.GetCurrent();
                            if (BPlusTree.CompareLexicographic(newBaseCurrent.key.AsSpan(), bKey) == 0)
                                goto next;
                        }
                    }
                }
                else if (BaseIsFinished || (!ChangeIsFinished && comp > 0))
                {
                    if (AdvanceChangeSkipDeletes())
                    {
                        goto next;
                    }
                }
            }

            return GetCurrent();

            void AdvanceBase()
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
            }

            bool AdvanceChangeSkipDeletes()
            {
                var nextChange = ChangeSetCursor.Next();
                ChangeIsFinished = nextChange.ResultCode == ResultCode.NotFound;

                if (nextChange.ResultCode == ResultCode.Success)
                {
                    if (nextChange.Key.Length > 0 && (ValueFlag)nextChange.Key[^1] == ValueFlag.Delete)
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
        _arena.Dispose();
    }
}
