using System.Diagnostics.CodeAnalysis;
using FDMF.Core.Utils;
using LightningDB;

namespace FDMF.Core.DatabaseLayer;

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

        // Empty keys should never be stored - they would create invalid 1-byte ChangeSet entries
        if (key.Length == 0)
            throw new ArgumentException("Cannot store an entry with an empty key", nameof(key));

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

        // Empty keys should never be deleted - they would create invalid 1-byte ChangeSet entries
        if (key.Length == 0)
            throw new ArgumentException("Cannot delete an entry with an empty key", nameof(key));

        // Always write a delete marker into the change set.
        // Even if the key doesn't exist in the base set, removing the changeset entry would shift B+tree leaf indices
        // and break cursors that are currently positioned on later keys.
        var keySlice = CopyKeyWithFlag(key, ValueFlag.Delete);
        ChangeSet!.Put(keySlice, Slice<byte>.Empty);

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

        private bool _isAfterDelete;
        private byte[]? _afterDeleteKey;

        private byte[]? _lastVisibleKey;


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
            _isAfterDelete = false;
            _afterDeleteKey = null;

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

            // LMDB semantics: after cursor_del, the cursor is positioned on the deleted item and GET_CURRENT returns NotFound.
            // The next Next() call should move to the next item.
            _afterDeleteKey = currentKey.ToArray();
            _isAfterDelete = true;

            // Delegate overlay semantics (base vs changeset) to store-level Delete.
            _store.Delete(_afterDeleteKey);

            return ResultCode.Success;
        }


        public CursorResult GetCurrent()
        {
            if (_isAfterDelete)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            while (true)
            {

                if (ChangeIsFinished && BaseIsFinished)
                    return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

                if (ChangeIsFinished)
                {
                    var baseSet = LightningCursor.GetCurrent();
                    var k = baseSet.key.AsSpan();
                    _lastVisibleKey = k.ToArray();
                    return new CursorResult(ResultCode.Success, k, baseSet.value.AsSpan());
                }

                var changeCurrent = ChangeSetCursor!.GetCurrent();
                // ChangeSet keys must be at least 2 bytes: [key][flag]
                // If we encounter an invalid entry, this is a bug - fail loudly
                if (changeCurrent.ResultCode != ResultCode.Success)
                {
                    ChangeIsFinished = true;
                    continue;
                }
                
                if (changeCurrent.Key.Length < 2)
                    throw new InvalidOperationException($"Invalid ChangeSet entry with key length {changeCurrent.Key.Length}. ChangeSet keys must be at least 2 bytes ([key][flag]).");

                var changeKey = changeCurrent.Key.Slice(0, changeCurrent.Key.Length - 1);
                var changeFlag = (ValueFlag)changeCurrent.Key[^1];

                if (BaseIsFinished)
                {
                    if (changeFlag == ValueFlag.Delete)
                    {
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                        continue;
                    }

                    _lastVisibleKey = changeKey.ToArray();
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

                    var k = baseCurrent.key.AsSpan();
                    _lastVisibleKey = k.ToArray();
                    return new CursorResult(ResultCode.Success, k, baseCurrent.value.AsSpan());
                }

                // Add/modify
                if (comp < 0)
                {
                    var k = baseCurrent.key.AsSpan();
                    _lastVisibleKey = k.ToArray();
                    return new CursorResult(ResultCode.Success, k, baseCurrent.value.AsSpan());
                }

                _lastVisibleKey = changeKey.ToArray();
                return new CursorResult(ResultCode.Success, changeKey, changeCurrent.Value);
            }
        }

        public CursorResult Next()
        {
            if (_isAfterDelete)
            {
                _isAfterDelete = false;
                var key = _afterDeleteKey;
                _afterDeleteKey = null;

                if (key == null)
                    return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

                var set = SetRange(key);
                if (set == ResultCode.NotFound)
                    return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

                return GetCurrent();
            }

            if (ChangeIsFinished && BaseIsFinished)
            {
                // Cursor was exhausted. The base set is a read-only LMDB snapshot, but the changeset can still grow.
                // Re-seek from the last visible key to surface newly appended keys.
                if (_lastVisibleKey == null)
                    return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

                var last = _lastVisibleKey;

                var set = SetRange(last);
                if (set == ResultCode.NotFound)
                    return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

                // If we landed back on the last visible key, advance once to get beyond it.
                var cur = GetCurrent();
                if (cur.ResultCode != ResultCode.Success)
                    return cur;

                if (cur.Key.SequenceEqual(last))
                {
                    // This uses the normal merge logic (no recursion into the end-of-cursor branch).
                    if (ChangeIsFinished)
                    {
                        BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                    }
                    else if (BaseIsFinished)
                    {
                        ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                    }
                    else
                    {
                        var baseCur = LightningCursor.GetCurrent();
                        var changeCur = ChangeSetCursor!.GetCurrent();

                        // ChangeSet keys must be at least 2 bytes: [key][flag]
                        // If we encounter an invalid entry, this is a bug - fail loudly
                        if (changeCur.ResultCode != ResultCode.Success)
                        {
                            ChangeIsFinished = true;
                        }
                        else if (changeCur.Key.Length < 2)
                        {
                            throw new InvalidOperationException($"Invalid ChangeSet entry with key length {changeCur.Key.Length}. ChangeSet keys must be at least 2 bytes ([key][flag]).");
                        }
                        else
                        {
                            var changeK = changeCur.Key.Slice(0, changeCur.Key.Length - 1);
                            var cmp = BPlusTree.CompareLexicographic(baseCur.key.AsSpan(), changeK);

                            if (cmp == 0)
                            {
                                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                                ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                            }
                            else if (cmp < 0)
                            {
                                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
                            }
                            else
                            {
                                ChangeIsFinished = ChangeSetCursor!.Next().ResultCode == ResultCode.NotFound;
                            }
                        }
                    }

                    return GetCurrent();
                }

                return cur;
            }


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

            // ChangeSet keys must be at least 2 bytes: [key][flag]
            // If we encounter an invalid entry, this is a bug - fail loudly
            if (changeCurrent.ResultCode != ResultCode.Success)
            {
                ChangeIsFinished = true;
                return Next();
            }
            
            if (changeCurrent.Key.Length < 2)
                throw new InvalidOperationException($"Invalid ChangeSet entry with key length {changeCurrent.Key.Length}. ChangeSet keys must be at least 2 bytes ([key][flag]).");

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
