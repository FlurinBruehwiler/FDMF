using FDMF.Core.Utils;

namespace FDMF.Core.Database;

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
        public List<Slice<byte>> Keys = [];
        public InternalNode? Parent;
        public int ParentIndex;
        public abstract bool IsLeaf { get; }
    }

    private sealed class InternalNode : Node
    {
        public List<Node> Children = [];
        public override bool IsLeaf => false;
    }

    private sealed class LeafNode : Node
    {
        public List<Slice<byte>> Values = [];
        public LeafNode? Next;
        public LeafNode? Prev;
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

    // public ResultCode Put(byte[] key, byte[] value)
    // {
    //     return Put((ReadOnlyMemory<byte>)key, (ReadOnlyMemory<byte>)value);
    // }

    public ResultCode Put(Slice<byte> key, Slice<byte> value)
    {
        var split = InsertInternal(_root, key, value);
        if (split != null)
        {
            var newRoot = new InternalNode();
            newRoot.Keys.Add(split.Separator);
            newRoot.Children.Add(split.Left);
            newRoot.Children.Add(split.Right);

            split.Left.Parent = newRoot;
            split.Left.ParentIndex = 0;
            split.Right.Parent = newRoot;
            split.Right.ParentIndex = 1;

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
            _root.Parent = null;
            _root.ParentIndex = 0;
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

            if (leaf.Keys.Count == 0 && leaf.Parent != null)
            {
                // Keep leaf chain consistent.
                if (leaf.Prev != null)
                    leaf.Prev.Next = leaf.Next;
                if (leaf.Next != null)
                    leaf.Next.Prev = leaf.Prev;
            }

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

            FixChildParentIndices(internalNode, childIndex);
        }
        else
        {
            if (childIndex > 0 && internalNode.Keys.Count > 0)
            {
                internalNode.Keys[childIndex - 1] = GetFirstKey(internalNode.Children[childIndex]);
            }
        }

        shouldDeleteNode = internalNode.Children.Count == 0;
        return true;
    }

    private sealed class SplitResult
    {
        public required Slice<byte> Separator;
        public required Node Left;
        public required Node Right;
    }

    private SplitResult? InsertInternal(Node node, Slice<byte> key, Slice<byte> value)
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
            {
                // If the first key changed and this leaf is a right child, update ancestors.
                if (pos == 0)
                    UpdateAncestorsFirstKey(leaf);

                return null;
            }

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

        split.Left.Parent = internalNode;
        split.Left.ParentIndex = childIndex;
        split.Right.Parent = internalNode;
        split.Right.ParentIndex = childIndex + 1;

        FixChildParentIndices(internalNode, childIndex + 2);

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
        if (right.Next != null)
            right.Next.Prev = right;

        right.Prev = leaf;
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

        FixChildParentIndices(node, 0);
        FixChildParentIndices(right, 0);

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

    private int BinarySearch(List<Slice<byte>> array, ReadOnlySpan<byte> value)
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

    private static Slice<byte> GetFirstKey(Node node)
    {
        while (!node.IsLeaf)
        {
            node = ((InternalNode)node).Children[0];
        }

        var leaf = (LeafNode)node;
        return leaf.Keys[0];
    }

    private static void FixChildParentIndices(InternalNode node, int startIndex)
    {
        for (int i = startIndex; i < node.Children.Count; i++)
        {
            node.Children[i].Parent = node;
            node.Children[i].ParentIndex = i;
        }
    }

    private static void UpdateAncestorsFirstKey(Node node)
    {
        while (node.Parent != null)
        {
            var parent = node.Parent;
            var idx = node.ParentIndex;

            if (idx > 0)
            {
                parent.Keys[idx - 1] = GetFirstKey(node);
            }

            node = parent;
        }
    }

    public Cursor CreateCursor() => new(this);

    public sealed class Cursor
    {
        private readonly BPlusTree _tree;
        private LeafNode? _leaf;
        private int _index;
        private bool _valid;

        public Cursor(BPlusTree tree)
        {
            _tree = tree;
        }

        public ResultCode SetRange(ReadOnlySpan<byte> inputKey)
        {
            (_leaf, _index, _valid) = _tree.FindFirstAtOrAfter(inputKey);
            return _valid ? ResultCode.Success : ResultCode.NotFound;
        }

        public CursorResult GetCurrent()
        {
            if (!_valid || _leaf == null)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            return new CursorResult(ResultCode.Success, _leaf.Keys[_index].Span, _leaf.Values[_index].Span);
        }

        public CursorResult Next()
        {
            if (!_valid || _leaf == null)
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

            _index++;
            if (_index < _leaf.Keys.Count)
                return GetCurrent();

            var nextLeaf = _leaf.Next;
            while (nextLeaf != null && nextLeaf.Keys.Count == 0)
            {
                nextLeaf = nextLeaf.Next;
            }

            if (nextLeaf == null)
            {
                _valid = false;
                _leaf = null;
                _index = 0;
                return new CursorResult(ResultCode.NotFound, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);
            }

            _leaf = nextLeaf;
            _index = 0;
            return GetCurrent();
        }

        public ResultCode Delete()
        {
            if (!_valid || _leaf == null)
                return ResultCode.NotFound;

            var leaf = _leaf;

            leaf.Keys.RemoveAt(_index);
            leaf.Values.RemoveAt(_index);

            if (leaf.Keys.Count == 0)
            {
                var next = leaf.Next;
                while (next != null && next.Keys.Count == 0)
                    next = next.Next;

                _tree.RemoveEmptyLeaf(leaf);

                if (next == null)
                {
                    _valid = false;
                    _leaf = null;
                    _index = 0;
                    return ResultCode.Success;
                }

                _leaf = next;
                _index = 0;
                _valid = true;
                return ResultCode.Success;
            }

            if (_index == 0)
            {
                UpdateAncestorsFirstKey(leaf);
            }

            if (_index >= leaf.Keys.Count)
            {
                var next = leaf.Next;
                while (next != null && next.Keys.Count == 0)
                    next = next.Next;

                if (next == null)
                {
                    _valid = false;
                    _leaf = null;
                    _index = 0;
                    return ResultCode.Success;
                }

                _leaf = next;
                _index = 0;
                _valid = true;
                return ResultCode.Success;
            }

            _leaf = leaf;
            _valid = true;
            return ResultCode.Success;
        }
    }

    private (LeafNode? leaf, int index, bool found) FindFirstAtOrAfter(ReadOnlySpan<byte> key)
    {
        var node = _root;
        while (!node.IsLeaf)
        {
            var internalNode = (InternalNode)node;
            int childIndex = BinarySearch(internalNode.Keys, key);
            if (childIndex >= 0)
                childIndex++;
            else
                childIndex = ~childIndex;

            node = internalNode.Children[childIndex];
        }

        var leaf = (LeafNode)node;

        while (true)
        {
            int pos = BinarySearch(leaf.Keys, key);
            if (pos < 0)
                pos = ~pos;

            if (pos < leaf.Keys.Count)
                return (leaf, pos, true);

            leaf = leaf.Next;
            while (leaf != null && leaf.Keys.Count == 0)
                leaf = leaf.Next;

            if (leaf == null)
                return (null, 0, false);
        }
    }

    private void RemoveEmptyLeaf(LeafNode leaf)
    {
        if (leaf.Prev != null)
            leaf.Prev.Next = leaf.Next;
        if (leaf.Next != null)
            leaf.Next.Prev = leaf.Prev;

        var parent = leaf.Parent;
        if (parent == null)
        {
            // Root leaf.
            _root = new LeafNode();
            return;
        }

        int childIndex = leaf.ParentIndex;

        parent.Children.RemoveAt(childIndex);
        if (childIndex < parent.Keys.Count)
            parent.Keys.RemoveAt(childIndex);
        else if (childIndex > 0)
            parent.Keys.RemoveAt(childIndex - 1);

        FixChildParentIndices(parent, childIndex);
        RefreshInternalSeparators(parent, Math.Max(0, childIndex - 1));

        RemoveEmptyInternalIfNeeded(parent);
    }

    private void RemoveEmptyInternalIfNeeded(InternalNode node)
    {
        if (node.Children.Count != 0)
        {
            if (node.Parent == null && node.Children.Count == 1)
            {
                _root = node.Children[0];
                _root.Parent = null;
                _root.ParentIndex = 0;
            }

            return;
        }

        var parent = node.Parent;
        if (parent == null)
        {
            _root = new LeafNode();
            return;
        }

        int idx = node.ParentIndex;
        parent.Children.RemoveAt(idx);
        if (idx < parent.Keys.Count)
            parent.Keys.RemoveAt(idx);
        else if (idx > 0)
            parent.Keys.RemoveAt(idx - 1);

        FixChildParentIndices(parent, idx);
        RefreshInternalSeparators(parent, Math.Max(0, idx - 1));

        RemoveEmptyInternalIfNeeded(parent);
    }

    private static void RefreshInternalSeparators(InternalNode node, int startKeyIndex)
    {
        // Invariant: node.Keys[i] == first key of node.Children[i+1]
        for (int i = startKeyIndex; i < node.Keys.Count; i++)
        {
            node.Keys[i] = GetFirstKey(node.Children[i + 1]);
        }

        UpdateAncestorsFirstKey(node);
    }
}
