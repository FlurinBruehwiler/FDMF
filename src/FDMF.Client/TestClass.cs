namespace FDMF.Client;

public sealed class TestClass
{
    public void Test()
    {
        Test2(1);
    }

    public void Test2(int a)
    {

        Test();
        var y = 1;
        var x = y + 2;
    }
}