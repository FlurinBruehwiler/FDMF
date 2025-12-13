using Shared.Database;

namespace Shared;

public interface ITransactionObject
{
    DbSession DbSession { get; set; }
    Guid _objId { get; set; }

    static abstract Guid TypId { get; }
}