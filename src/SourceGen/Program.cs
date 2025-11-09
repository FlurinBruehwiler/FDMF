using System.Runtime.InteropServices;
using System.Text;
using SourceGen;
using TheProject;

var model = ProjectModel.CreateFromDirectory("Model");

foreach (var entity in model.EntityDefinitions)
{
    var sourceBuilder = new SourceBuilder();

    sourceBuilder.AppendLine("using System.Text;");

    sourceBuilder.AppendLine("namespace TheProject.Generated;");
    sourceBuilder.AppendLine();

    sourceBuilder.AppendLine($"public struct {entity.Key} : ITransactionObject");
    sourceBuilder.AppendLine("{");
    sourceBuilder.AddIndent();

    sourceBuilder.AppendLine($"public {entity.Key}(Transaction transaction)");
    sourceBuilder.AppendLine("{");
    sourceBuilder.AddIndent();
    sourceBuilder.AppendLine("_transaction = transaction;");
    sourceBuilder.AppendLine("_objId = _transaction.CreateObj(TypId);");
    sourceBuilder.RemoveIndent();
    sourceBuilder.AppendLine("}");

    sourceBuilder.AppendLine();

    sourceBuilder.AppendLine("public Transaction _transaction { get; set; }");
    sourceBuilder.AppendLine("public Guid _objId { get; set; }");
    sourceBuilder.AppendLine();

    foreach (var field in entity.Fields)
    {
        var dataType = field.DataType switch
        {
            FieldDataType.Integer => "int",
            FieldDataType.Decimal => "decimal",
            FieldDataType.String => "string",
            FieldDataType.DateTime => "DateTime",
            FieldDataType.Boolean => "bool",
            _ => throw new ArgumentOutOfRangeException()
        };

        var toFunction = field.DataType switch
        {
            FieldDataType.Integer => "MemoryMarshal.Read<int>({0})",
            FieldDataType.Decimal => "MemoryMarshal.Read<decimal>({0})",
            FieldDataType.String => "Encoding.Unicode.GetString({0}.AsSpan())",
            FieldDataType.DateTime => "MemoryMarshal.Read<DateTime>({0})",
            FieldDataType.Boolean => "MemoryMarshal.Read<bool>({0})",
            _ => throw new ArgumentOutOfRangeException()
        };

        var fromFunction = field.DataType switch
        {
            FieldDataType.Integer => "new Slice<int>(&value, 1).AsByteSlice()",
            FieldDataType.Decimal => "new Slice<decimal>(&value, 1).AsByteSlice()",
            FieldDataType.String => "Encoding.Unicode.GetBytes(value).AsSpan().AsSlice()",
            FieldDataType.DateTime => "new Slice<DateTime>(&value, 1).AsByteSlice()",
            FieldDataType.Boolean => "new Slice<bool>(&value, 1).AsByteSlice()",
            _ => throw new ArgumentOutOfRangeException()
        };

        //could be improving performance here....
        sourceBuilder.AppendLine($"public {dataType} {field.Key}");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AddIndent();
        sourceBuilder.AppendLine($"get => {string.Format(toFunction, $"_transaction.GetFldValue(_objId, Fields.{field.Key})")};");
        sourceBuilder.AppendLine($"set => _transaction.SetFldValue(_objId, Fields.{field.Key}, {fromFunction});");
        sourceBuilder.RemoveIndent();
        sourceBuilder.AppendLine("}");
    }

    foreach (var refField in entity.ReferenceFields)
    {
        if (refField.RefType is RefType.SingleMandatory or RefType.SingleOptional)
        {
            var optional = refField.RefType == RefType.SingleOptional ? "?" : string.Empty;
            var getMethod = refField.RefType == RefType.SingleOptional ? "GetNullableAssoc" : "GetAssoc";

            sourceBuilder.AppendLine($"public {refField.OtherReferenceField.OwningEntity.Key}{optional} {refField.Key}");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AddIndent();

            sourceBuilder.AppendLine($"get => GeneratedCodeHelper.{getMethod}<{entity.Key}>(_transaction, _objId, Fields.{refField.Key});");
            sourceBuilder.AppendLine($"set => GeneratedCodeHelper.SetAssoc(_transaction, _objId, Fields.{refField.Key}, value?._objId ?? Guid.Empty, {refField.OtherReferenceField.OwningEntity.Key}.Fields.{refField.OtherReferenceField.Key});");

            sourceBuilder.RemoveIndent();
            sourceBuilder.AppendLine("}");
        }
        else if (refField.RefType == RefType.Multiple)
        {
            sourceBuilder.AppendLine($"public AssocCollection<{refField.OtherReferenceField.OwningEntity.Key}> {refField.Key} => new(_transaction, _objId, Fields.{refField.Key}, {refField.OtherReferenceField.OwningEntity.Key}.Fields.{refField.OtherReferenceField.Key});");
        }
    }

    sourceBuilder.AppendLine();

    sourceBuilder.AppendLine($"public static readonly Guid TypId = {GetGuidLiteral(entity.Id)};");
    sourceBuilder.AppendLine();

    sourceBuilder.AppendLine($"public static class Fields");
    sourceBuilder.AppendLine("{");
    sourceBuilder.AddIndent();

    foreach (var fieldDefinition in entity.Fields)
    {
        sourceBuilder.AppendLine($"public static readonly Guid {fieldDefinition.Key} = {GetGuidLiteral(fieldDefinition.Id)};");
    }
    foreach (var fieldDefinition in entity.ReferenceFields)
    {
        sourceBuilder.AppendLine($"public static readonly Guid {fieldDefinition.Key} = {GetGuidLiteral(fieldDefinition.Id)};");
    }

    sourceBuilder.RemoveIndent();
    sourceBuilder.AppendLine("}");


    sourceBuilder.RemoveIndent();
    sourceBuilder.AppendLine("}");

    File.WriteAllText($"../../../../TheProject/Generated/{entity.Key}.cs", sourceBuilder.ToString());
}

string GetGuidLiteral(Guid guid)
{
    Span<byte> guidData = stackalloc byte[16];
    MemoryMarshal.Write(guidData, guid);

    var sb = new StringBuilder();

    sb.Append("new Guid([");

    var isFirst = true;
    foreach (var b in guidData)
    {
        if (!isFirst)
            sb.Append(", ");

        sb.Append(b);
        isFirst = false;
    }

    sb.Append("])");

    return sb.ToString();
}
