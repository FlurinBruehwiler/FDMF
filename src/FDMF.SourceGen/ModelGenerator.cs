using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.SourceGen;

public static class ModelGenerator
{
    public static void Generate(Model model, string targetDir, string targetNamespace)
    {
        Directory.CreateDirectory(targetDir);

        var @namespace = targetNamespace;

        foreach (var entity in model.GetAllEntityDefinitions())
        {
            var sourceBuilder = new SourceBuilder();

            sourceBuilder.AppendLine("// ReSharper disable All");
            sourceBuilder.AppendLine("using System.Runtime.InteropServices;");
            sourceBuilder.AppendLine("using System.Text;");
            sourceBuilder.AppendLine("using MemoryPack;");
            sourceBuilder.AppendLine("using FDMF.Core;");
            sourceBuilder.AppendLine("using FDMF.Core.DatabaseLayer;");

            sourceBuilder.AppendLine($"namespace {@namespace};");
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

            foreach (var field in entity.FieldDefinitions)
            {
                var dataType = field.DataType switch
                {
                    nameof(FieldDataType.Integer) => "long",
                    nameof(FieldDataType.Decimal) => "decimal",
                    nameof(FieldDataType.String) => "string",
                    nameof(FieldDataType.DateTime) => "DateTime",
                    nameof(FieldDataType.Boolean) => "bool",
                    _ => throw new ArgumentOutOfRangeException()
                };

                var toFunction = field.DataType switch
                {
                    nameof(FieldDataType.Integer) => "MemoryMarshal.Read<long>({0})",
                    nameof(FieldDataType.Decimal) => "MemoryMarshal.Read<decimal>({0})",
                    nameof(FieldDataType.String) => "Encoding.Unicode.GetString({0})",
                    nameof(FieldDataType.DateTime) => "MemoryMarshal.Read<DateTime>({0})",
                    nameof(FieldDataType.Boolean) => "MemoryMarshal.Read<bool>({0})",
                    _ => throw new ArgumentOutOfRangeException()
                };

                var fromFunction = field.DataType switch
                {
                    nameof(FieldDataType.Integer) => "value.AsSpan()",
                    nameof(FieldDataType.Decimal) => "value.AsSpan()",
                    nameof(FieldDataType.String) => "Encoding.Unicode.GetBytes(value)",
                    nameof(FieldDataType.DateTime) => "value.AsSpan()",
                    nameof(FieldDataType.Boolean) => "value.AsSpan()",
                    _ => throw new ArgumentOutOfRangeException()
                };

                //could be improving performance here....
                sourceBuilder.AppendLine("[MemoryPackIgnore]");
                sourceBuilder.AppendLine($"public {dataType} {field.Key}");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AddIndent();
                sourceBuilder.AppendLine($"get => {string.Format(toFunction, $"DbSession.GetFldValue(ObjId, Fields.{field.Key})")};");
                sourceBuilder.AppendLine($"set => DbSession.SetFldValue(ObjId, Fields.{field.Key}, {fromFunction});");
                sourceBuilder.RemoveIndent();
                sourceBuilder.AppendLine("}");
            }

            foreach (var refField in entity.ReferenceFieldDefinitions)
            {
                if (refField.RefType is nameof(RefType.SingleMandatory) or nameof(RefType.SingleOptional))
                {
                    var optional = refField.RefType == nameof(RefType.SingleOptional) ? "?" : string.Empty;
                    var getMethod = refField.RefType == nameof(RefType.SingleOptional) ? "GetNullableAssoc" : "GetAssoc";

                    sourceBuilder.AppendLine("[MemoryPackIgnore]");
                    sourceBuilder.AppendLine($"public {refField.OtherReferenceFields.OwningEntity.Key}{optional} {refField.Key}");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.AddIndent();

                    var valueAccess = refField.RefType == nameof(RefType.SingleOptional) ? "value?.ObjId ?? Guid.Empty" : "value.ObjId";

                    sourceBuilder.AppendLine($"get => GeneratedCodeHelper.{getMethod}<{refField.OtherReferenceFields.OwningEntity.Key}>(DbSession, ObjId, Fields.{refField.Key});");
                    sourceBuilder.AppendLine($"set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.{refField.Key}, {valueAccess}, {@namespace}.{refField.OtherReferenceFields.OwningEntity.Key}.Fields.{refField.OtherReferenceFields.Key});");

                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine("}");
                }
                else if (refField.RefType == nameof(RefType.Multiple))
                {
                    sourceBuilder.AppendLine("[MemoryPackIgnore]");
                    sourceBuilder.AppendLine($"public AssocCollection<{refField.OtherReferenceFields.OwningEntity.Key}> {refField.Key} => new(DbSession, ObjId, Fields.{refField.Key}, {refField.OtherReferenceFields.OwningEntity.Key}.Fields.{refField.OtherReferenceFields.Key});");
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

            sourceBuilder.AppendLine($"//{entity.Id}");
            sourceBuilder.AppendLine($"public static Guid TypId {{ get; }} = {GetGuidLiteral(entity.Id)};");
            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($"public static class Fields");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AddIndent();

            foreach (var fieldDefinition in entity.FieldDefinitions)
            {
                sourceBuilder.AppendLine($"//{fieldDefinition.Id}");
                sourceBuilder.AppendLine($"public static readonly Guid {fieldDefinition.Key} = {GetGuidLiteral(fieldDefinition.Id)};");
            }

            foreach (var fieldDefinition in entity.ReferenceFieldDefinitions)
            {
                sourceBuilder.AppendLine($"//{fieldDefinition.Id}");
                sourceBuilder.AppendLine($"public static readonly Guid {fieldDefinition.Key} = {GetGuidLiteral(fieldDefinition.Id)};");
            }

            sourceBuilder.RemoveIndent();
            sourceBuilder.AppendLine("}");


            sourceBuilder.RemoveIndent();
            sourceBuilder.AppendLine("}");

            var generatedPath = Path.Combine(targetDir, $"{entity.Key}.cs");
            File.WriteAllText(generatedPath, sourceBuilder.ToString());
        }
    }

    public static string GetGuidLiteral(string g)
    {
        var guid = Guid.Parse(g);
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