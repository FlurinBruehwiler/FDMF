namespace Client;

public class TestClass
{
    public void Test()
    {
        var x = "";
        x = "Hallo";
        var y = x = "Hallo";
        Test2(1);
    }

    public void Test2(int a)
    {

        Test();
        var y = 1;
        var x = y + 2;
    }
}