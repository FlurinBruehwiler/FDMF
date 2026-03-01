namespace FDMF.Core;

public struct PendingRequest
{
    public Action<object> Callback;
    public Type ResponseType;
}

public sealed class ServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        throw new NotImplementedException();
    }
}
