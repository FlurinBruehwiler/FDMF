namespace Shared.Database;

// A simple B+Tree implementation.
// Keys are compared lexicographically by default.
// TODO: This is a very bad and unoptimized implementation of a B+ Tree.

public sealed class BPlusTree
{
    public delegate int KeyComparer(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b);

    public static int CompareLexicographic(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int diff = a[i].CompareTo(b[i]);
            if (diff != 0)
                return diff;
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <summary>
    /// Compares keys lexicographically while ignoring the last byte.
    /// Intended for "flag-in-key" overlays.
    /// </summary>
    public static int CompareIgnoreLastByte(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var aLen = Math.Max(0, a.Length - 1);
        var bLen = Math.Max(0, b.Length - 1);

        int len = Math.Min(aLen, bLen);
        for (int i = 0; i < len; i++)
        {
            int diff = a[i].CompareTo(b[i]);
            if (diff != 0)
                return diff;
        }

        return aLen.CompareTo(bLen);
    }

    public readonly ref struct Result
    {
        public readonly ResultCode ResultCode;
        public readonly ReadOnlySpan<byte> Key;
        public readonly ReadOnlySpan<byte> Value;

        public Result(ResultCode resultCode, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
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

    private abstract class Node
    {
        public List<ReadOnlyMemory<byte>> Keys = [];
        public abstract bool IsLeaf { get; }
    }

    private sealed class InternalNode : Node
    {
        public List<Node> Children = [];
        public override bool IsLeaf => false;
    }

    private sealed class LeafNode : Node
    {
        public List<ReadOnlyMemory<byte>> Values = [];
        public LeafNode? Next;
        public override bool IsLeaf => true;
    }

    private readonly int _branchingFactor;
    private readonly KeyComparer _compare;
    private Node _root;

    public BPlusTree(int branchingFactor = 32, KeyComparer? comparer = null)
    {
        if (branchingFactor < 3)
            throw new ArgumentException("branchingFactor must be >= 3");

        _branchingFactor = branchingFactor;
        _compare = comparer ?? CompareLexicographic;
        _root = new LeafNode();
    }

    public void Clear()
    {
        _root = new LeafNode();
    }

    public ResultCode Put(byte[] key, byte[] value)
    {
        return Put((ReadOnlyMemory<byte>)key, (ReadOnlyMemory<byte>)value);
    }

    public ResultCode Put(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
    {
        var split = InsertInternal(_root, key, value);
        if (split != null)
        {
            var newRoot = new InternalNode();
            newRoot.Keys.Add(split.Separator);
            newRoot.Children.Add(split.Left);
            newRoot.Children.Add(split.Right);
            _root = newRoot;
        }

        return ResultCode.Success;
    }

    public ResultCode Delete(byte[] key) => Delete(key.AsSpan());

    public ResultCode Delete(ReadOnlySpan<byte> key)
    {
        bool removed = DeleteInternal(_root, key, out _);

        if (_root is InternalNode internalRoot && internalRoot.Children.Count == 1)
        {
            _root = internalRoot.Children[0];
        }

        return removed ? ResultCode.Success : ResultCode.NotFound;
    }

    private bool DeleteInternal(Node node, ReadOnlySpan<byte> key, out bool shouldDeleteNode)
    {
        shouldDeleteNode = false;

        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int index = BinarySearch(leaf.Keys, key);

            if (index < 0)
                return false;

            leaf.Keys.RemoveAt(index);
            leaf.Values.RemoveAt(index);

            shouldDeleteNode = leaf.Keys.Count == 0;
            return true;
        }

        var internalNode = (InternalNode)node;

        int childIndex = BinarySearch(internalNode.Keys, key);
        if (childIndex >= 0)
            childIndex++;
        else
            childIndex = ~childIndex;

        bool removed = DeleteInternal(internalNode.Children[childIndex], key, out bool deleteChild);

        if (!removed)
            return false;

        if (deleteChild)
        {
            internalNode.Children.RemoveAt(childIndex);

            if (childIndex < internalNode.Keys.Count)
                internalNode.Keys.RemoveAt(childIndex);
            else if (childIndex > 0)
                internalNode.Keys.RemoveAt(childIndex - 1);
        }
        else
        {
            if (childIndex > 0 && internalNode.Keys.Count > 0)
            {
                var child = internalNode.Children[childIndex];
                if (child.IsLeaf)
                {
                    internalNode.Keys[childIndex - 1] = ((LeafNode)child).Keys[0];
                }
            }
        }

        shouldDeleteNode = internalNode.Children.Count == 0;
        return true;
    }

    private sealed class SplitResult
    {
        public required ReadOnlyMemory<byte> Separator;
        public required Node Left;
        public required Node Right;
    }

    private SplitResult? InsertInternal(Node node, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
    {
        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int pos = BinarySearch(leaf.Keys, key.Span);

            if (pos >= 0)
            {
                leaf.Values[pos] = value;
                leaf.Keys[pos] = key;
                return null;
            }

            pos = ~pos;
            leaf.Keys.Insert(pos, key);
            leaf.Values.Insert(pos, value);

            if (leaf.Keys.Count <= _branchingFactor)
                return null;

            return SplitLeaf(leaf);
        }

        var internalNode = (InternalNode)node;
        int childIndex = BinarySearch(internalNode.Keys, key.Span);
        if (childIndex >= 0)
            childIndex++;
        else
            childIndex = ~childIndex;

        var split = InsertInternal(internalNode.Children[childIndex], key, value);
        if (split == null)
            return null;

        internalNode.Keys.Insert(childIndex, split.Separator);
        internalNode.Children[childIndex] = split.Left;
        internalNode.Children.Insert(childIndex + 1, split.Right);

        if (internalNode.Keys.Count <= _branchingFactor)
            return null;

        return SplitInternal(internalNode);
    }

    private SplitResult SplitLeaf(LeafNode leaf)
    {
        int mid = leaf.Keys.Count / 2;

        var right = new LeafNode();
        right.Keys.AddRange(leaf.Keys.GetRange(mid, leaf.Keys.Count - mid));
        right.Values.AddRange(leaf.Values.GetRange(mid, leaf.Values.Count - mid));

        leaf.Keys.RemoveRange(mid, leaf.Keys.Count - mid);
        leaf.Values.RemoveRange(mid, leaf.Values.Count - mid);

        right.Next = leaf.Next;
        leaf.Next = right;

        return new SplitResult
        {
            Separator = right.Keys[0],
            Left = leaf,
            Right = right
        };
    }

    private SplitResult SplitInternal(InternalNode node)
    {
        int mid = node.Keys.Count / 2;

        var right = new InternalNode();
        right.Keys.AddRange(node.Keys.GetRange(mid + 1, node.Keys.Count - (mid + 1)));
        right.Children.AddRange(node.Children.GetRange(mid + 1, node.Children.Count - (mid + 1)));

        var separator = node.Keys[mid];

        node.Keys.RemoveRange(mid, node.Keys.Count - mid);
        node.Children.RemoveRange(mid + 1, node.Children.Count - (mid + 1));

        return new SplitResult
        {
            Separator = separator,
            Left = node,
            Right = right
        };
    }

    public Result Get(ReadOnlySpan<byte> key)
    {
        return SearchExactInternal(_root, key);
    }

    private Result SearchExactInternal(Node node, ReadOnlySpan<byte> key)
    {
        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int pos = BinarySearch(leaf.Keys, key);
            if (pos >= 0)
                return new Result(ResultCode.Success, leaf.Keys[pos].Span, leaf.Values[pos].Span);

            return new Result(ResultCode.NotFound, default, default);
        }

        var internalNode = (InternalNode)node;
        int childIndex = BinarySearch(internalNode.Keys, key);
        if (childIndex >= 0)
            childIndex++;
        else
            childIndex = ~childIndex;

        return SearchExactInternal(internalNode.Children[childIndex], key);
    }

    private int BinarySearch(List<ReadOnlyMemory<byte>> array, ReadOnlySpan<byte> value)
    {
        int lo = 0;
        int hi = array.Count - 1;
        while (lo <= hi)
        {
            int i = lo + ((hi - lo) >> 1);
            int order = _compare(array[i].Span, value);

            if (order == 0)
                return i;

            if (order < 0)
            {
                lo = i + 1;
            }
            else
            {
                hi = i - 1;
            }
        }

        return ~lo;
    }

    private (ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, bool didNotFindGreater) SearchGreaterOrEqualsThan(Node node, ReadOnlySpan<byte> key, bool onlyGreater)
    {
        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int pos = BinarySearch(leaf.Keys, key);

            if (pos >= 0)
            {
                if (onlyGreater)
                {
                    pos++;
                }

                if (pos < leaf.Keys.Count)
                    return (leaf.Keys[pos], leaf.Values[pos], false);

                if (leaf.Next != null && leaf.Next.Keys.Count > 0)
                    return (leaf.Next.Keys[0], leaf.Next.Values[0], false);

                return (default, default, true);
            }

            pos = ~pos;

            if (pos < leaf.Keys.Count)
                return (leaf.Keys[pos], leaf.Values[pos], false);

            if (leaf.Next != null && leaf.Next.Keys.Count > 0)
                return (leaf.Next.Keys[0], leaf.Next.Values[0], false);

            return (default, default, true);
        }

        var internalNode = (InternalNode)node;
        int childIndex = BinarySearch(internalNode.Keys, key);
        if (childIndex >= 0)
            childIndex++;
        else
            childIndex = ~childIndex;

        var res = SearchGreaterOrEqualsThan(internalNode.Children[childIndex], key, onlyGreater);
        if (!res.didNotFindGreater)
            return res;

        childIndex++;
        if (internalNode.Children.Count > childIndex)
        {
            return SearchGreaterOrEqualsThan(internalNode.Children[childIndex], key, onlyGreater);
        }

        return (default, default, true);
    }

    public Cursor CreateCursor() => new(this);

    public sealed class Cursor
    {
        private readonly BPlusTree _tree;
        private ReadOnlyMemory<byte> _key;
        private ReadOnlyMemory<byte> _value;

        public Cursor(BPlusTree tree)
        {
            _tree = tree;
        }

        public ResultCode SetRange(ReadOnlySpan<byte> inputKey)
        {
            (_key, _value, var didNotFind) = _tree.SearchGreaterOrEqualsThan(_tree._root, inputKey, onlyGreater: false);
            return didNotFind ? ResultCode.NotFound : ResultCode.Success;
        }

        public CursorResult GetCurrent()
        {
            return new CursorResult(ResultCode.Success, _key.Span, _value.Span);
        }

        public CursorResult Next()
        {
            var (k, v, didNotFind) = _tree.SearchGreaterOrEqualsThan(_tree._root, _key.Span, onlyGreater: true);

            if (didNotFind)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            _key = k;
            _value = v;

            return new CursorResult(ResultCode.Success, _key.Span, _value.Span);
        }

        public ResultCode Delete()
        {
            return _tree.Delete(_key.Span);
        }
    }
}
