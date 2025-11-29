namespace Shared;

public interface ITransactionObject
{
    Transaction _transaction { get; set; }
    Guid _objId { get; set; }
}