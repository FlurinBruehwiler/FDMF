using Shared.Database;

namespace Tests;

public class BPlusTreeTests
{
    private static byte[] B(params byte[] x) => x;

    [Fact]
    public void Insert_And_Search_Single_Key()
    {
        var tree = new BPlusTree();
        var key = B(1,2,3);
        var value = B(9,9,9);

        tree.Put(key, value);

        var result = tree.Get(key);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public void Search_Missing_Key_Returns_NotFound()
    {
        var tree = new BPlusTree();
        tree.Put(B(1), B(10));

        Assert.True(tree.Get(B(2)).ResultCode == ResultCode.NotFound);
    }

    [Fact]
    public void Insert_Multiple_In_Ascending_Order()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        for (int i = 0; i < 50; i++)
            tree.Put([(byte)i], [(byte)(i+1)]);

        for (int i = 0; i < 50; i++)
            Assert.Equal(new[]{(byte)(i+1)}, tree.Get([(byte)i]).Value);
    }

    [Fact]
    public void Insert_Multiple_In_Descending_Order()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        for (int i = 50; i >= 0; i--)
            tree.Put([(byte)i], [(byte)(i+1)]);

        for (int i = 50; i >= 0; i--)
            Assert.Equal(new[]{(byte)(i+1)}, tree.Get([(byte)i]).Value);
    }

    [Fact]
    public void Insert_Triggers_Multiple_Splits()
    {
        var tree = new BPlusTree(branchingFactor: 3);

        // Force several splits
        for (int i = 0; i < 200; i++)
            tree.Put([(byte)i], [(byte)(i*2)]);

        for (int i = 0; i < 200; i++)
            Assert.Equal(new[]{(byte)(i*2)}, tree.Get([(byte)i]).Value);
    }

    [Fact]
    public void Variable_Length_Keys_Work()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        tree.Put(B(1,2,3), B(5));
        tree.Put(B(1,2), B(6));
        tree.Put(B(1,2,3,4), B(7));

        Assert.Equal(B(5), tree.Get(B(1,2,3)).Value);
        Assert.Equal(B(6), tree.Get(B(1,2)).Value);
        Assert.Equal(B(7), tree.Get(B(1,2,3,4)).Value);
    }

    [Fact]
    public void Overwrite_Value_Of_Existing_Key()
    {
        var tree = new BPlusTree();

        var key = B(5);
        tree.Put(key, B(10));
        tree.Put(key, B(20));

        Assert.Equal(B(20), tree.Get(key).Value);
    }

    [Fact]
    public void Cursor_Starting_From_Existing_Item()
    {
        var tree = new BPlusTree();

        for (byte i = 0; i < 20; i++)
            tree.Put(B(i), B((byte)(i * 2)));

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
            tree.Put(B(i), B((byte)(i * 2)));

        for (byte i = 20; i < 30; i++)
            tree.Put(B(i), B((byte)(i * 2)));

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

    [Fact]
    public void Delete()
    {
        var tree = new BPlusTree();

        var key = B(1);

        tree.Put(key, B(2));

        Assert.True(tree.Delete(key) == ResultCode.Success);
        Assert.True(tree.Get(key).ResultCode == ResultCode.NotFound);
    }
}