using System.Runtime.InteropServices;
using System.Text;
using Shared;

namespace SourceGen;

public static class ModelGenerator
{
    public static void Generate(string modelDirectory)
    {
        var model = ProjectModel.CreateFromDirectory(modelDirectory);

        foreach (var entity in model.EntityDefinitions)
        {
            var sourceBuilder = new SourceBuilder();

            sourceBuilder.AppendLine("// ReSharper disable All");
            sourceBuilder.AppendLine("using System.Runtime.InteropServices;");
            sourceBuilder.AppendLine("using System.Text;");
            sourceBuilder.AppendLine("using MemoryPack;");
            sourceBuilder.AppendLine("using Shared;");
            sourceBuilder.AppendLine("using Shared.Database;");

            sourceBuilder.AppendLine($"namespace {Path.GetFileName(modelDirectory)}.Generated;");
            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine("[MemoryPackable]");
            sourceBuilder.AppendLine($"public partial struct {entity.Key} : ITransactionObject, IEquatable<{entity.Key}>");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AddIndent();

            sourceBuilder.AppendLine("[Obsolete]");
            sourceBuilder.AppendLine("[MemoryPackConstructor]");
            sourceBuilder.AppendLine($"public {entity.Key}() {{ }}");


            sourceBuilder.AppendLine($"public {entity.Key}(DbSession dbSession)");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AddIndent();

            sourceBuilder.AppendLine("DbSession = dbSession;");
            sourceBuilder.AppendLine("ObjId = DbSession.CreateObj(TypId);");
            sourceBuilder.RemoveIndent();
            sourceBuilder.AppendLine("}");

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine("[MemoryPackIgnore]");
            sourceBuilder.AppendLine("public DbSession DbSession { get; set; } = null!;");
            sourceBuilder.AppendLine("public Guid ObjId { get; set; }");
            sourceBuilder.AppendLine();

            foreach (var field in entity.Fields)
            {
                var dataType = field.DataType switch
                {
                    FieldDataType.Integer => "long",
                    FieldDataType.Decimal => "decimal",
                    FieldDataType.String => "string",
                    FieldDataType.DateTime => "DateTime",
                    FieldDataType.Boolean => "bool",
                    _ => throw new ArgumentOutOfRangeException()
                };

                var toFunction = field.DataType switch
                {
                    FieldDataType.Integer => "MemoryMarshal.Read<long>({0})",
                    FieldDataType.Decimal => "MemoryMarshal.Read<decimal>({0})",
                    FieldDataType.String => "Encoding.Unicode.GetString({0})",
                    FieldDataType.DateTime => "MemoryMarshal.Read<DateTime>({0})",
                    FieldDataType.Boolean => "MemoryMarshal.Read<bool>({0})",
                    _ => throw new ArgumentOutOfRangeException()
                };

                var fromFunction = field.DataType switch
                {
                    FieldDataType.Integer => "value.AsSpan()",
                    FieldDataType.Decimal => "value.AsSpan()",
                    FieldDataType.String => "Encoding.Unicode.GetBytes(value)",
                    FieldDataType.DateTime => "value.AsSpan()",
                    FieldDataType.Boolean => "value.AsSpan()",
                    _ => throw new ArgumentOutOfRangeException()
                };

                //could be improving performance here....
                sourceBuilder.AppendLine("[MemoryPackIgnore]");
                sourceBuilder.AppendLine($"public {dataType} {field.Key}");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AddIndent();
                sourceBuilder.AppendLine($"get => {string.Format(toFunction, $"DbSession.GetFldValue(ObjId, Fields.{field.Key}).AsSpan()")};");
                sourceBuilder.AppendLine($"set => DbSession.SetFldValue(ObjId, Fields.{field.Key}, {fromFunction});");
                sourceBuilder.RemoveIndent();
                sourceBuilder.AppendLine("}");
            }

            foreach (var refField in entity.ReferenceFields)
            {
                if (refField.RefType is RefType.SingleMandatory or RefType.SingleOptional)
                {
                    var optional = refField.RefType == RefType.SingleOptional ? "?" : string.Empty;
                    var getMethod = refField.RefType == RefType.SingleOptional ? "GetNullableAssoc" : "GetAssoc";

                    sourceBuilder.AppendLine("[MemoryPackIgnore]");
                    sourceBuilder.AppendLine($"public {refField.OtherReferenceField.OwningEntity.Key}{optional} {refField.Key}");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.AddIndent();

                    sourceBuilder.AppendLine($"get => GeneratedCodeHelper.{getMethod}<{entity.Key}>(DbSession, ObjId, Fields.{refField.Key});");
                    sourceBuilder.AppendLine($"set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.{refField.Key}, value?.ObjId ?? Guid.Empty, {refField.OtherReferenceField.OwningEntity.Key}.Fields.{refField.OtherReferenceField.Key});");

                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine("}");
                }
                else if (refField.RefType == RefType.Multiple)
                {
                    sourceBuilder.AppendLine("[MemoryPackIgnore]");
                    sourceBuilder.AppendLine($"public AssocCollection<{refField.OtherReferenceField.OwningEntity.Key}> {refField.Key} => new(DbSession, ObjId, Fields.{refField.Key}, {refField.OtherReferenceField.OwningEntity.Key}.Fields.{refField.OtherReferenceField.Key});");
                }
            }

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($"public static bool operator ==({entity.Key} a, {entity.Key} b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;");
            sourceBuilder.AppendLine($"public static bool operator !=({entity.Key} a, {entity.Key} b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;");

            sourceBuilder.AppendLine($"public bool Equals({entity.Key} other) => this == other;");
            sourceBuilder.AppendLine($"public override bool Equals(object? obj) => obj is {entity.Key} other && Equals(other);");
            sourceBuilder.AppendLine("public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);");
            sourceBuilder.AppendLine("public override string ToString() => ObjId.ToString();");

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($"public static Guid TypId {{ get; }} = {GetGuidLiteral(entity.Id)};");
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

            var generatedPath = Path.Combine(modelDirectory, "../Generated", $"{entity.Key}.cs");
            File.WriteAllText(generatedPath, sourceBuilder.ToString());
        }
    }

    private static string GetGuidLiteral(Guid guid)
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
}