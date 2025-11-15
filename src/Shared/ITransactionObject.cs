namespace Model;

public interface ITransactionObject
{
    Transaction _transaction { get; set; }
    Guid _objId { get; set; }
}