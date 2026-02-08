using FDMF.Core.DatabaseLayer;

namespace FDMF.Core;

public interface ITransactionObject
{
    DbSession DbSession { get; set; }
    Guid ObjId { get; set; }

    static abstract Guid TypId { get; }
}