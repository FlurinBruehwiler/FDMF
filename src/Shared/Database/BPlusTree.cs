namespace Shared.Database;

// A simple B+Tree implementation that stores key-value pairs as byte[]
// Keys are compared lexicographically.
// TODO, this is a very bad and unoptimized implementation of a B+ Tree, this should be refactored to allocate way less memory.

public class BPlusTree
{
    public ref struct Result
    {
        public ResultCode ResultCode;
        public ReadOnlySpan<byte> Key;
        public ReadOnlySpan<byte> Value;

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

    private abstract class Node
    {
        public List<byte[]> Keys = new List<byte[]>();
        public abstract bool IsLeaf { get; }
    }

    private class InternalNode : Node
    {
        public List<Node> Children = new List<Node>();
        public override bool IsLeaf => false;
    }

    private class LeafNode : Node
    {
        public List<byte[]> Values = new List<byte[]>();
        public LeafNode Next;
        public override bool IsLeaf => true;
    }

    private readonly int _branchingFactor;
    private Node _root;

    public BPlusTree(int branchingFactor = 32)
    {
        if (branchingFactor < 3) throw new ArgumentException("branchingFactor must be >= 3");
        _branchingFactor = branchingFactor;
        _root = new LeafNode();
    }

    // Lexicographic compare of byte[] keys
    public static int CompareSpan(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int diff = a[i].CompareTo(b[i]);
            if (diff != 0) return diff;
        }
        return a.Length.CompareTo(b.Length);
    }

    // Public Insert API
    public ResultCode Put(byte[] key, byte[] value)
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

    public ResultCode Delete(byte[] key)
    {
        bool removed = DeleteInternal(_root, key, out _);

        // root collapsed?
        if (_root is InternalNode internalRoot &&
            internalRoot.Children.Count == 1)
        {
            _root = internalRoot.Children[0];
        }

        return removed ? ResultCode.Success : ResultCode.NotFound;
    }

    private bool DeleteInternal(Node node, byte[] key, out bool shouldDeleteNode)
    {
        shouldDeleteNode = false;

        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int index = leaf.Keys.BinarySearch(key);

            if (index < 0)
                return false; // not found

            // Remove key/value
            leaf.Keys.RemoveAt(index);
            leaf.Values.RemoveAt(index);

            // Check underflow
            shouldDeleteNode = leaf.Keys.Count == 0;
            return true;
        }

        var internalNode = (InternalNode)node;

        // Find child
        int childIndex = internalNode.Keys.BinarySearch(key);
        if (childIndex >= 0)
            childIndex++;
        else
            childIndex = ~childIndex;

        bool removed = DeleteInternal(internalNode.Children[childIndex], key, out bool deleteChild);

        if (!removed) return false;

        // If child node became empty, remove it
        if (deleteChild)
        {
            internalNode.Children.RemoveAt(childIndex);

            // Remove the separator (unless child was last)
            if (childIndex < internalNode.Keys.Count)
                internalNode.Keys.RemoveAt(childIndex);
            else if (childIndex > 0)
                internalNode.Keys.RemoveAt(childIndex - 1);
        }
        else
        {
            // Fix separator keys after leaf delete
            if (childIndex > 0 && internalNode.Keys.Count > 0)
            {
                var child = internalNode.Children[childIndex];
                if (child.IsLeaf)
                {
                    internalNode.Keys[childIndex - 1] =
                        ((LeafNode)child).Keys[0];
                }
            }
        }

        shouldDeleteNode = internalNode.Children.Count == 0;
        return true;
    }

    private class SplitResult
    {
        public byte[] Separator;
        public Node Left;
        public Node Right;
    }

    private SplitResult InsertInternal(Node node, byte[] key, byte[] value)
    {
        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int pos = BinarySearch(leaf.Keys, key);

            //if the key already exists, we replace the value
            if (pos >= 0)
            {
                leaf.Values[pos] = value;
                return null;
            }

            pos = ~pos;
            leaf.Keys.Insert(pos, key);
            leaf.Values.Insert(pos, value);

            if (leaf.Keys.Count <= _branchingFactor) return
                null;
            return SplitLeaf(leaf);
        }
        else
        {
            var internalNode = (InternalNode)node;
            int childIndex = BinarySearch(internalNode.Keys, key);
            if (childIndex >= 0) childIndex++;
            else childIndex = ~childIndex;

            var split = InsertInternal(internalNode.Children[childIndex], key, value);
            if (split == null) return null;

            internalNode.Keys.Insert(childIndex, split.Separator);
            internalNode.Children[childIndex] = split.Left;
            internalNode.Children.Insert(childIndex + 1, split.Right);

            if (internalNode.Keys.Count <= _branchingFactor) return null;
            return SplitInternal(internalNode);
        }
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

        byte[] separator = node.Keys[mid];

        node.Keys.RemoveRange(mid, node.Keys.Count - mid);
        node.Children.RemoveRange(mid + 1, node.Children.Count - (mid + 1));

        return new SplitResult
        {
            Separator = separator,
            Left = node,
            Right = right
        };
    }

    // Lookup
    public Result Get(ReadOnlySpan<byte> key)
    {
        return SearchExactInternal(_root, key);
    }

    private Result SearchExactInternal(Node node, ReadOnlySpan<byte> key)
    {
        if (node.IsLeaf) {
            var leaf = (LeafNode)node;
            int pos = BinarySearch(leaf.Keys, key);
            if (pos >= 0)
                return new Result(ResultCode.Success, key, leaf.Values[pos]);
            else
                return new Result(ResultCode.NotFound, default, default);
        }
        else
        {
            var internalNode = (InternalNode)node;
            int childIndex = BinarySearch(internalNode.Keys, key);
            if (childIndex >= 0) childIndex++;
            else childIndex = ~childIndex;
            return SearchExactInternal(internalNode.Children[childIndex], key);
        }
    }

    private int BinarySearch(List<byte[]> array, ReadOnlySpan<byte> value)
    {
        int lo = 0;
        int hi = array.Count - 1;
        while (lo <= hi)
        {
            int i = lo + ((hi - lo) >> 1);
            int order = CompareSpan(array[i], value);

            if (order == 0) return i;
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

    private (byte[] key, byte[] value, bool didNotFindGreater) SearchGreaterOrEqualsThan(Node node, byte[] key, bool onlyGreater)
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
                    if (leaf.Values.Count > pos)
                    {
                        return (leaf.Keys[pos], leaf.Values[pos], false);
                    }
                    else
                    {
                        return (null, null, true);
                    }
                }
                else
                {
                    return (key, leaf.Values[pos], false);
                }

            }

            pos = ~pos;
            return (leaf.Keys[pos], leaf.Values[pos], false);
        }
        else
        {
            var internalNode = (InternalNode)node;
            int childIndex = BinarySearch(internalNode.Keys, key);
            if (childIndex >= 0) childIndex++;
            else childIndex = ~childIndex;
            var res = SearchGreaterOrEqualsThan(internalNode.Children[childIndex], key, onlyGreater);
            if (!res.didNotFindGreater)
            {
                return res;
            }
            else
            {
                childIndex++;
                if (internalNode.Children.Count > childIndex)
                {
                    return SearchGreaterOrEqualsThan(internalNode.Children[childIndex], key, onlyGreater);
                }

                return (null, null, false);
            }
        }
    }

    public Cursor CreateCursor()
    {
        return new Cursor(this);
    }

    public class Cursor(BPlusTree tree)
    {
        private bool success;
        private byte[] key;
        private byte[] value;

        public void SetRange(byte[] inputKey)
        {
            (key, value, success) = tree.SearchGreaterOrEqualsThan(tree._root, inputKey, false);
        }

        public (bool success, byte[] key, byte[] value) GetCurrent()
        {
            return (success, key, value);
        }

        public (bool success, byte[] key, byte[] value) Next()
        {
            //don't search again from the top...
            (key, value, success) = tree.SearchGreaterOrEqualsThan(tree._root, key, true);
            return (!success, key, value);
        }

        public ResultCode Delete()
        {
            //todo don't search again
            return tree.Delete(key);
        }
    }
}
