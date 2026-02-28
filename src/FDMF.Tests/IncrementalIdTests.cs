using FDMF.Core.DatabaseLayer;

namespace FDMF.Tests;

public sealed class IncrementalIdTests
{
    [Fact]
    public void Create_Increments_Counter_When_Timestamp_Is_Equal()
    {
        var prev = IncrementalId.Create(default);

        for (int i = 0; i < 100; i++)
        {
            var next = IncrementalId.Create(prev);

            Assert.True(BPlusTree.CompareLexicographic(prev.AsSpan(), next.AsSpan()) < 0);

            Assert.False(prev.AsSpan().SequenceEqual(next.AsSpan()));

            prev = next;

            if(i % 10 == 0)
                Thread.Sleep(2);
        }
    }
}
