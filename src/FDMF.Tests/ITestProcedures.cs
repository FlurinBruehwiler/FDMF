using System.Threading.Tasks;

namespace FDMF.Tests;

public interface ITestProcedures
{
    void Ping();
    Task<int> Add(int a, int b);
}
