namespace PerformanceTests;

public interface IPerformanceTest : IDisposable
{
    public static abstract int[] Counts { get; }
    public void Run(int count);
}