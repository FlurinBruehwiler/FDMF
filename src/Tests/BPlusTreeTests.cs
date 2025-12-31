using Shared.Database;

namespace Tests;

public class BPlusTreeTests
{
    private static byte[] B(params byte[] x) => x;

    [Fact]
    public void Insert_And_Search_Single_Key()
    {
        var tree = new BPlusTree();
        var key = B(1, 2, 3);
        var value = B(9, 9, 9);

        tree.Put(key, value);

        var result = tree.Get(key);
        AssertBytes.Equal(value, result.Value);
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
            tree.Put([(byte)i], [(byte)(i + 1)]);

        for (int i = 0; i < 50; i++)
            AssertBytes.Equal(new[] { (byte)(i + 1) }, tree.Get([(byte)i]).Value);
    }

    [Fact]
    public void Insert_Multiple_In_Descending_Order()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        for (int i = 50; i >= 0; i--)
            tree.Put([(byte)i], [(byte)(i + 1)]);

        for (int i = 50; i >= 0; i--)
            AssertBytes.Equal(new[] { (byte)(i + 1) }, tree.Get([(byte)i]).Value);
    }

    [Fact]
    public void Insert_Triggers_Multiple_Splits()
    {
        var tree = new BPlusTree(branchingFactor: 3);

        for (int i = 0; i < 200; i++)
            tree.Put([(byte)i], [(byte)(i * 2)]);

        for (int i = 0; i < 200; i++)
            AssertBytes.Equal(new[] { (byte)(i * 2) }, tree.Get([(byte)i]).Value);
    }

    [Fact]
    public void Variable_Length_Keys_Work()
    {
        var tree = new BPlusTree(branchingFactor: 4);

        tree.Put(B(1, 2, 3), B(5));
        tree.Put(B(1, 2), B(6));
        tree.Put(B(1, 2, 3, 4), B(7));

        AssertBytes.Equal(B(5), tree.Get(B(1, 2, 3)).Value);
        AssertBytes.Equal(B(6), tree.Get(B(1, 2)).Value);
        AssertBytes.Equal(B(7), tree.Get(B(1, 2, 3, 4)).Value);
    }

    [Fact]
    public void Overwrite_Value_Of_Existing_Key()
    {
        var tree = new BPlusTree();

        var key = B(5);
        tree.Put(key, B(10));
        tree.Put(key, B(20));

        AssertBytes.Equal(B(20), tree.Get(key).Value);
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
        AssertBytes.Equal(B(10), current.Key);

        for (byte i = 11; i < 20; i++)
        {
            var n = cursor.Next();

            Assert.Equal(ResultCode.Success, n.ResultCode);
            AssertBytes.Equal(B(i), n.Key);
            AssertBytes.Equal(B((byte)(i * 2)), n.Value);
        }

        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
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
        AssertBytes.Equal(B(20), current.Key);

        for (byte i = 21; i < 30; i++)
        {
            var n = cursor.Next();

            Assert.Equal(ResultCode.Success, n.ResultCode);
            AssertBytes.Equal(B(i), n.Key);
            AssertBytes.Equal(B((byte)(i * 2)), n.Value);
        }

        Assert.Equal(ResultCode.NotFound, cursor.Next().ResultCode);
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
