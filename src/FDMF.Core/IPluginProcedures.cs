namespace FDMF.Core;

// Host-side API exposed to plugins.
public interface IHostProcedures
{
    void Ping();
    Task<string> Echo(string msg);
}

// Plugin-side API exposed to the host.
public interface IPluginProcedures
{
    Task<int> Add(int a, int b);
}
