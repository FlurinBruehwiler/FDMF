using Shared.Database;

namespace Shared;

public interface ITransactionObject
{
    Transaction _transaction { get; set; }
    Guid _objId { get; set; }

    static abstract Guid TypId { get; }
}