using Shared.Database;

namespace Shared;

public interface ITransactionObject
{
    DbSession DbSession { get; set; }
    Guid ObjId { get; set; }

    static abstract Guid TypId { get; }
}