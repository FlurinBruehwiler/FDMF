using System.Diagnostics.CodeAnalysis;
using LightningDB;
using Shared.Utils;

namespace Shared.Database;

public sealed class TransactionalKvStore : IDisposable
{
    public readonly LightningTransaction ReadTransaction;
    public readonly LightningDatabase Database;
    private readonly Arena _arena;
    public readonly LightningEnvironment Environment;

    public bool IsReadOnly { get; }

    // Flag is stored in the last byte of the key. The changeset tree uses a comparer that ignores that byte.
    public readonly BPlusTree? ChangeSet;

    private byte[] _searchKeyBuffer = new byte[64];

    public TransactionalKvStore(LightningEnvironment env, LightningDatabase database, Arena arena, bool readOnly = false)
    {
        ReadTransaction = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
        Database = database;
        _arena = arena;
        Environment = env;
        IsReadOnly = readOnly;

        if (!readOnly)
        {
            ChangeSet = new BPlusTree(comparer: BPlusTree.CompareIgnoreLastByte);
        }
    }

    public void Commit(LightningTransaction writeTransaction)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("TransactionalKvStore is read-only");

        var cursor = ChangeSet!.CreateCursor();

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

        ChangeSet!.Clear();
    }

    public ResultCode Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("TransactionalKvStore is read-only");

        var keySlice = CopyKeyWithFlag(key, ValueFlag.AddModify);
        var valueSlice = _arena.AllocateSlice(value);

        return ChangeSet!.Put(keySlice, valueSlice);
    }

    public ResultCode Get(ReadOnlySpan<byte> key, [UnscopedRef] out ReadOnlySpan<byte> value)
    {
        if (!IsReadOnly)
        {
            var searchKey = CreateSearchKey(key);

            var res = ChangeSet!.Get(searchKey);
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
        if (IsReadOnly)
            throw new InvalidOperationException("TransactionalKvStore is read-only");

        if (ReadTransaction.Get(Database, key).resultCode == MDBResultCode.Success)
        {
            var keySlice = CopyKeyWithFlag(key, ValueFlag.Delete);
            ChangeSet!.Put(keySlice, Slice<byte>.Empty);
        }
        else
        {
            Span<byte> searchKey = stackalloc byte[key.Length + 1];
            key.CopyTo(searchKey);
            searchKey[^1] = 0;
            ChangeSet!.Delete(searchKey);
        }

        return ResultCode.Success;
    }

    public Cursor CreateCursor()
    {
        return new Cursor(this)
        {
            LightningCursor = ReadTransaction.CreateCursor(Database),
            ChangeSetCursor = IsReadOnly ? null : ChangeSet!.CreateCursor()
        };
    }

    private Slice<byte> CopyKeyWithFlag(ReadOnlySpan<byte> key, ValueFlag flag)
    {
        var mem = _arena.AllocateSlice<byte>(key.Length + 1);
        key.CopyTo(mem.Span);
        mem.Span[^1] = (byte)flag;
        return mem;
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
        public BPlusTree.Cursor? ChangeSetCursor;

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

            if (_store.IsReadOnly)
            {
                ChangeIsFinished = true;
                return BaseIsFinished ? ResultCode.NotFound : ResultCode.Success;
            }

            Span<byte> searchKey = stackalloc byte[key.Length + 1];
            key.CopyTo(searchKey);
            searchKey[^1] = 0;
            ChangeIsFinished = ChangeSetCursor!.SetRange(searchKey) == ResultCode.NotFound;

            if (!ChangeIsFinished && !BaseIsFinished)
            {
                var a = LightningCursor.GetCurrent();
                var b = ChangeSetCursor!.GetCurrent();

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
            if (_store.IsReadOnly)
                throw new InvalidOperationException("TransactionalKvStore is read-only");

            var current = GetCurrent();
            var currentKey = current.Key;

            if (current.ResultCode != ResultCode.Success || currentKey.Length == 0)
                return ResultCode.NotFound;

            // Determine where the current value came from.
            bool currentFromChange;
            bool baseHasSameKey;

            if (ChangeIsFinished)
            {
                currentFromChange = false;
                baseHasSameKey = true;
            }
            else if (BaseIsFinished)
            {
                currentFromChange = true;
                baseHasSameKey = false;
            }
            else
            {
                var baseCurrent = LightningCursor.GetCurrent();
                var changeCurrent = ChangeSetCursor!.GetCurrent();
                var changeKey = changeCurrent.Key.Slice(0, changeCurrent.Key.Length - 1);

                var comp = BPlusTree.CompareLexicographic(baseCurrent.key.AsSpan(), changeKey);
                currentFromChange = comp >= 0;
                baseHasSameKey = comp == 0;
            }

            if (!currentFromChange)
            {
                // Current came from base, so it definitely exists in base.
                _store.ChangeSet!.Put(_store.CopyKeyWithFlag(currentKey, ValueFlag.Delete), Slice<byte>.Empty);

                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;

                Span<byte> searchKey = stackalloc byte[currentKey.Length + 1];
                currentKey.CopyTo(searchKey);
                searchKey[^1] = 0;
                ChangeIsFinished = ChangeSetCursor!.SetRange(searchKey) == ResultCode.NotFound;

                // Ensure we don't sit on a delete marker.
                if (!ChangeIsFinished)
                {
                    var cc = ChangeSetCursor!.GetCurrent();
                    if (cc.Key.Length > 0 && (ValueFlag)cc.Key[^1] == ValueFlag.Delete)
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                }

                return ResultCode.Success;
            }

            // Current came from changeset.
            if (baseHasSameKey)
            {
                // Changeset entry shadows a base key -> final state should be Delete.
                _store.ChangeSet!.Put(_store.CopyKeyWithFlag(currentKey, ValueFlag.Delete), Slice<byte>.Empty);

                // Skip the base key as well.
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;

                Span<byte> searchKey = stackalloc byte[currentKey.Length + 1];
                currentKey.CopyTo(searchKey);
                searchKey[^1] = 0;
                ChangeIsFinished = ChangeSetCursor!.SetRange(searchKey) == ResultCode.NotFound;

                if (!ChangeIsFinished)
                {
                    var cc = ChangeSetCursor!.GetCurrent();
                    if (cc.Key.Length > 0 && (ValueFlag)cc.Key[^1] == ValueFlag.Delete)
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                }

                return ResultCode.Success;
            }

            // Changeset-only entry -> remove it from changeset.
            ChangeSetCursor!.Delete();
            var after = ChangeSetCursor!.GetCurrent();
            ChangeIsFinished = after.ResultCode == ResultCode.NotFound;

            return ResultCode.Success;
        }

        public CursorResult GetCurrent()
        {
            while (true)
            {
                if (ChangeIsFinished && BaseIsFinished)
                    return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

                if (ChangeIsFinished)
                {
                    var baseSet = LightningCursor.GetCurrent();
                    return new CursorResult(ResultCode.Success, baseSet.key.AsSpan(), baseSet.value.AsSpan());
                }

                var changeCurrent = ChangeSetCursor!.GetCurrent();
                if (changeCurrent.ResultCode != ResultCode.Success || changeCurrent.Key.Length == 0)
                {
                    ChangeIsFinished = true;
                    continue;
                }

                var changeKey = changeCurrent.Key.Slice(0, changeCurrent.Key.Length - 1);
                var changeFlag = (ValueFlag)changeCurrent.Key[^1];

                if (BaseIsFinished)
                {
                    if (changeFlag == ValueFlag.Delete)
                    {
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                        continue;
                    }

                    return new CursorResult(ResultCode.Success, changeKey, changeCurrent.Value);
                }

                var baseCurrent = LightningCursor.GetCurrent();
                var comp = BPlusTree.CompareLexicographic(baseCurrent.key.AsSpan(), changeKey);

                if (changeFlag == ValueFlag.Delete)
                {
                    if (comp == 0)
                    {
                        BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                        continue;
                    }

                    if (comp > 0)
                    {
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                        continue;
                    }

                    return new CursorResult(ResultCode.Success, baseCurrent.key.AsSpan(), baseCurrent.value.AsSpan());
                }

                // Add/modify
                if (comp < 0)
                {
                    return new CursorResult(ResultCode.Success, baseCurrent.key.AsSpan(), baseCurrent.value.AsSpan());
                }

                return new CursorResult(ResultCode.Success, changeKey, changeCurrent.Value);
            }
        }

        public CursorResult Next()
        {
            if (ChangeIsFinished && BaseIsFinished)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            if (ChangeIsFinished)
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                return GetCurrent();
            }

            if (BaseIsFinished)
            {
                ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                return GetCurrent();
            }

            var baseCurrent = LightningCursor.GetCurrent();
            var changeCurrent = ChangeSetCursor!.GetCurrent();

            if (changeCurrent.ResultCode != ResultCode.Success || changeCurrent.Key.Length == 0)
            {
                ChangeIsFinished = true;
                return Next();
            }

            var changeKey = changeCurrent.Key.Slice(0, changeCurrent.Key.Length - 1);
            var comp = BPlusTree.CompareLexicographic(baseCurrent.key.AsSpan(), changeKey);

            if (comp == 0)
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                return GetCurrent();
            }

            if (comp < 0)
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                return GetCurrent();
            }

            ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
            return GetCurrent();
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
