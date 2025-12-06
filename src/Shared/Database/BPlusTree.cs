namespace Shared.Database;

// A simple B+Tree implementation that stores key-value pairs as byte[]
// Keys are compared lexicographically.
public class BPlusTree
{
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
    private static int Compare(byte[] a, byte[] b)
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
    public void Insert(byte[] key, byte[] value)
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
            int pos = leaf.Keys.BinarySearch(key, Comparer<byte[]>.Create(Compare));

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
            int childIndex = internalNode.Keys.BinarySearch(key, Comparer<byte[]>.Create(Compare));
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
    public byte[] Search(byte[] key)
    {
        return SearchExactInternal(_root, key);
    }

    private byte[] SearchExactInternal(Node node, byte[] key)
    {
        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int pos = leaf.Keys.BinarySearch(key, Comparer<byte[]>.Create(Compare));
            return pos >= 0 ? leaf.Values[pos] : null;
        }
        else
        {
            var internalNode = (InternalNode)node;
            int childIndex = internalNode.Keys.BinarySearch(key, Comparer<byte[]>.Create(Compare));
            if (childIndex >= 0) childIndex++;
            else childIndex = ~childIndex;
            return SearchExactInternal(internalNode.Children[childIndex], key);
        }
    }

    private (byte[] key, byte[] value, bool didNotFindGreater) SearchGreaterOrEqualsThan(Node node, byte[] key, bool onlyGreater)
    {
        if (node.IsLeaf)
        {
            var leaf = (LeafNode)node;
            int pos = leaf.Keys.BinarySearch(key, Comparer<byte[]>.Create(Compare));

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
            int childIndex = internalNode.Keys.BinarySearch(key, Comparer<byte[]>.Create(Compare));
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
            (key, value, success) = tree.SearchGreaterOrEqualsThan(tree._root, key, true);
            return (!success, key, value);
        }
    }
}
