using System;
using Shared.Database;
using Xunit;

public class BPlusTreeTests
{
    private static byte[] B(params byte[] x) => x;

    [Fact]
    public void Insert_And_Search_Single_Key()
    {
        var tree = new BPlusTree();
        var key = B(1,2,3);
        var value = B(9,9,9);

        tree.Insert(key, value);

        var result = tree.Search(key);
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public void Search_Missing_Key_Returns_Null()
    {
        var tree = new BPlusTree();
        tree.Insert(B(1), B(10));

        Assert.Null(tree.Search(B(2)));
    }

    [Fact]
    public void Insert_Multiple_In_Ascending_Order()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        for (int i = 0; i < 50; i++)
            tree.Insert(new[]{(byte)i}, new[]{(byte)(i+1)});

        for (int i = 0; i < 50; i++)
            Assert.Equal(new[]{(byte)(i+1)}, tree.Search(new[]{(byte)i}));
    }

    [Fact]
    public void Insert_Multiple_In_Descending_Order()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        for (int i = 50; i >= 0; i--)
            tree.Insert(new[]{(byte)i}, new[]{(byte)(i+1)});

        for (int i = 50; i >= 0; i--)
            Assert.Equal(new[]{(byte)(i+1)}, tree.Search(new[]{(byte)i}));
    }

    [Fact]
    public void Insert_Triggers_Multiple_Splits()
    {
        var tree = new BPlusTree(branchingFactor: 3);

        // Force several splits
        for (int i = 0; i < 200; i++)
            tree.Insert(new[]{(byte)i}, new[]{(byte)(i*2)});

        for (int i = 0; i < 200; i++)
            Assert.Equal(new[]{(byte)(i*2)}, tree.Search(new[]{(byte)i}));
    }

    [Fact]
    public void Variable_Length_Keys_Work()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        tree.Insert(B(1,2,3), B(5));
        tree.Insert(B(1,2), B(6));
        tree.Insert(B(1,2,3,4), B(7));

        Assert.Equal(B(5), tree.Search(B(1,2,3)));
        Assert.Equal(B(6), tree.Search(B(1,2)));
        Assert.Equal(B(7), tree.Search(B(1,2,3,4)));
    }

    [Fact]
    public void Overwrite_Value_Of_Existing_Key()
    {
        var tree = new BPlusTree();

        var key = B(5);
        tree.Insert(key, B(10));
        tree.Insert(key, B(20));

        Assert.Equal(B(20), tree.Search(key));
    }

    [Fact]
    public void Cursor_Starting_From_Existing_Item()
    {
        var tree = new BPlusTree();

        for (byte i = 0; i < 20; i++)
            tree.Insert(B(i), B((byte)(i * 2)));

        var cursor = tree.CreateCursor();
        cursor.SetRange(B(10));

        var current = cursor.GetCurrent();
        Assert.Equal(B(10), current.key);

        for (byte i = 11; i < 20; i++)
        {
            var n = cursor.Next();

            Assert.True(n.success);
            Assert.Equal(B(i), n.key);
            Assert.Equal(B((byte)(i * 2)), n.value);
        }

        Assert.False(cursor.Next().success);
    }

    [Fact]
    public void Cursor_Starting_From_Non_Existing_Item()
    {
        var tree = new BPlusTree();

        for (byte i = 0; i < 10; i++)
            tree.Insert(B(i), B((byte)(i * 2)));

        for (byte i = 20; i < 30; i++)
            tree.Insert(B(i), B((byte)(i * 2)));

        var cursor = tree.CreateCursor();
        cursor.SetRange(B(15));

        var current = cursor.GetCurrent();
        Assert.Equal(B(20), current.key);

        for (byte i = 21; i < 30; i++)
        {
            var n = cursor.Next();

            Assert.True(n.success);
            Assert.Equal(B(i), n.key);
            Assert.Equal(B((byte)(i * 2)), n.value);
        }

        Assert.False(cursor.Next().success);
    }
}
