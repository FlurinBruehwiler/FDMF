using System.Text;
namespace TheProject.Generated;

public struct Folder : ITransactionObject
{
    public Folder(Transaction transaction)
    {
        _transaction = transaction;
        _objId = _transaction.CreateObj(TypId);
    }

    public Transaction _transaction { get; set; }
    public Guid _objId { get; set; }

    public string Name
    {
        get => Encoding.Unicode.GetString(_transaction.GetFldValue(_objId, Fields.Name).AsSpan());
        set => _transaction.SetFldValue(_objId, Fields.Name, Encoding.Unicode.GetBytes(value).AsSpan().AsSlice());
    }
    public AssocCollection<Folder> Subfolders => new(_transaction, _objId, Fields.Subfolders, Folder.Fields.Parent);
    public Folder? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Folder>(_transaction, _objId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(_transaction, _objId, Fields.Parent, value?._objId ?? Guid.Empty, Folder.Fields.Subfolders);
    }

    public static readonly Guid TypId = new Guid([139, 189, 204, 163, 86, 34, 75, 65, 164, 2, 26, 9, 28, 180, 7, 165]);

    public static class Fields
    {
        public static readonly Guid Name = new Guid([123, 108, 105, 24, 93, 11, 106, 64, 159, 76, 242, 232, 48, 204, 15, 85]);
        public static readonly Guid Subfolders = new Guid([118, 212, 11, 180, 163, 217, 84, 69, 162, 246, 79, 119, 96, 192, 255, 228]);
        public static readonly Guid Parent = new Guid([167, 173, 160, 131, 83, 186, 119, 72, 130, 98, 189, 71, 82, 14, 234, 199]);
    }
}
