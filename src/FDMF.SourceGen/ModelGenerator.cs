using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.SourceGen;

public static class ModelGenerator
{
    public static void Generate(Model model, string targetDir, string targetNamespace, bool includeMetaModel)
    {
        // Directory.Delete(targetDir, recursive: true);
        Directory.CreateDirectory(targetDir);

        var @namespace = targetNamespace;

        static List<EntityDefinition> GetAllEntityDefinitions(Model root)
        {
            var result = new List<EntityDefinition>();
            var seenModels = new HashSet<Guid>();

            AddFromModel(root);
            return result;

            void AddFromModel(Model mdl)
            {
                if (!seenModels.Add(mdl.ObjId))
                    return;

                foreach (var importedModel in mdl.ImportedModels)
                    AddFromModel(importedModel);

                foreach (var ed in mdl.EntityDefinitions)
                    result.Add(ed);
            }
        }

        // Generate C# enums for any EnumDefinition used by fields.
        var enumNameToVariants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in GetAllEntityDefinitions(model))
        {
            if (!includeMetaModel && (entity.ObjId == EntityDefinition.TypId || entity.ObjId == FieldDefinition.TypId || entity.ObjId == ReferenceFieldDefinition.TypId || entity.ObjId == Model.TypId))
                continue;

            foreach (var field in entity.FieldDefinitions)
            {
                if (field.DataType != FieldDataType.Enum)
                    continue;

                var enumDefOpt = field.Enum;
                if (!enumDefOpt.HasValue)
                    throw new Exception($"Enum-typed field '{field.Key}' is missing EnumDefinition association");

                var enumDef = enumDefOpt.Value;
                if (string.IsNullOrWhiteSpace(enumDef.Name))
                    throw new Exception($"Enum-typed field '{field.Key}' is missing EnumDefinition name");

                var enumName = SanitizeIdentifier(enumDef.Name);

                if (!enumNameToVariants.TryAdd(enumName, enumDef.Variants))
                {
                    if (!string.Equals(enumNameToVariants[enumName], enumDef.Variants, StringComparison.Ordinal))
                        throw new Exception($"Conflicting enum variants for '{enumName}'");
                }
            }
        }

        foreach (var (enumName, variantsRaw) in enumNameToVariants)
        {
            var enumBuilder = new SourceBuilder();
            enumBuilder.AppendLine($"namespace {@namespace};");
            enumBuilder.AppendLine();
            enumBuilder.AppendLine($"public enum {enumName}");
            enumBuilder.AppendLine("{");
            enumBuilder.AddIndent();

            foreach (var v in variantsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                enumBuilder.AppendLine($"{SanitizeIdentifier(v)},");
            }

            enumBuilder.RemoveIndent();
            enumBuilder.AppendLine("}");

            var enumPath = Path.Combine(targetDir, $"{enumName}.cs");
            File.WriteAllText(enumPath, enumBuilder.ToString());
        }

        foreach (var entity in GetAllEntityDefinitions(model))
        {
            if(!includeMetaModel && (entity.ObjId == EntityDefinition.TypId || entity.ObjId == FieldDefinition.TypId || entity.ObjId == ReferenceFieldDefinition.TypId || entity.ObjId == Model.TypId))
                continue;

            var sourceBuilder = new SourceBuilder();

            sourceBuilder.AppendLine("// ReSharper disable All");
            sourceBuilder.AppendLine("using System.Runtime.InteropServices;");
            sourceBuilder.AppendLine("using System.Text;");
            sourceBuilder.AppendLine("using MemoryPack;");
            sourceBuilder.AppendLine("using FDMF.Core;");
            sourceBuilder.AppendLine("using FDMF.Core.DatabaseLayer;");
            sourceBuilder.AppendLine();

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
                string? enumTypeName = null;
                if (field.DataType == FieldDataType.Enum)
                {
                    var enumDefOpt = field.Enum;
                    if (!enumDefOpt.HasValue)
                        throw new Exception($"Enum-typed field '{field.Key}' is missing EnumDefinition association");

                    enumTypeName = SanitizeIdentifier(enumDefOpt.Value.Name);
                    if (string.IsNullOrWhiteSpace(enumTypeName))
                        throw new Exception($"Enum-typed field '{field.Key}' is missing EnumDefinition name");
                }

                var dataType = field.DataType switch
                {
                    FieldDataType.Integer => "long",
                    FieldDataType.Decimal => "decimal",
                    FieldDataType.String => "string",
                    FieldDataType.DateTime => "DateTime",
                    FieldDataType.Boolean => "bool",
                    FieldDataType.Guid => "Guid",
                    FieldDataType.Enum => enumTypeName!,
                    _ => throw new ArgumentOutOfRangeException()
                };

                var toFunction = field.DataType switch
                {
                    FieldDataType.Integer => "MemoryMarshal.Read<long>({0})",
                    FieldDataType.Decimal => "MemoryMarshal.Read<decimal>({0})",
                    FieldDataType.String => "Encoding.Unicode.GetString({0})",
                    FieldDataType.DateTime => "MemoryMarshal.Read<DateTime>({0})",
                    FieldDataType.Boolean => "MemoryMarshal.Read<bool>({0})",
                    FieldDataType.Guid => "MemoryMarshal.Read<Guid>({0})",
                    FieldDataType.Enum => $"MemoryMarshal.Read<{enumTypeName}>({{0}})",
                    _ => throw new ArgumentOutOfRangeException()
                };

                var fromFunction = field.DataType switch
                {
                    FieldDataType.Integer => "value.AsSpan()",
                    FieldDataType.Decimal => "value.AsSpan()",
                    FieldDataType.String => "Encoding.Unicode.GetBytes(value)",
                    FieldDataType.DateTime => "value.AsSpan()",
                    FieldDataType.Boolean => "value.AsSpan()",
                    FieldDataType.Guid => "value.AsSpan()",
                    FieldDataType.Enum => "value.AsSpan()",
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
                sourceBuilder.AppendLine();
            }

            foreach (var refField in entity.ReferenceFieldDefinitions)
            {
                if (refField.RefType is RefType.SingleMandatory or RefType.SingleOptional)
                {
                    var optional = refField.RefType == RefType.SingleOptional ? "?" : string.Empty;
                    var getMethod = refField.RefType == RefType.SingleOptional ? "GetNullableAssoc" : "GetAssoc";

                    sourceBuilder.AppendLine("[MemoryPackIgnore]");
                    sourceBuilder.AppendLine($"public {refField.OtherReferenceFields.OwningEntity.Key}{optional} {refField.Key}");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.AddIndent();

                    var valueAccess = refField.RefType == RefType.SingleOptional ? "value?.ObjId ?? Guid.Empty" : "value.ObjId";

                    sourceBuilder.AppendLine($"get => GeneratedCodeHelper.{getMethod}<{refField.OtherReferenceFields.OwningEntity.Key}>(DbSession, ObjId, Fields.{refField.Key});");
                    sourceBuilder.AppendLine($"set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.{refField.Key}, {valueAccess}, {@namespace}.{refField.OtherReferenceFields.OwningEntity.Key}.Fields.{refField.OtherReferenceFields.Key});");

                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine("}");
                }
                else if (refField.RefType == RefType.Multiple)
                {
                    sourceBuilder.AppendLine("[MemoryPackIgnore]");
                    sourceBuilder.AppendLine($"public AssocCollection<{refField.OtherReferenceFields.OwningEntity.Key}> {refField.Key} => new(DbSession, ObjId, Fields.{refField.Key}, {refField.OtherReferenceFields.OwningEntity.Key}.Fields.{refField.OtherReferenceFields.Key});");
                }

                sourceBuilder.AppendLine();
            }

            sourceBuilder.AppendLine($"public static bool operator ==({entity.Key} a, {entity.Key} b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;");
            sourceBuilder.AppendLine($"public static bool operator !=({entity.Key} a, {entity.Key} b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;");

            sourceBuilder.AppendLine($"public bool Equals({entity.Key} other) => this == other;");
            sourceBuilder.AppendLine($"public override bool Equals(object? obj) => obj is {entity.Key} other && Equals(other);");
            sourceBuilder.AppendLine("public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);");
            sourceBuilder.AppendLine("public override string ToString() => ObjId.ToString();");

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($"///{entity.Id}");
            sourceBuilder.AppendLine($"public static Guid TypId {{ get; }} = {GetGuidLiteral(entity.Id)};");
            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine("public static class Fields");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AddIndent();

            foreach (var fieldDefinition in entity.FieldDefinitions)
            {
                sourceBuilder.AppendLine($"///{fieldDefinition.Id}");
                sourceBuilder.AppendLine($"public static readonly Guid {fieldDefinition.Key} = {GetGuidLiteral(fieldDefinition.Id)};");
            }

            foreach (var fieldDefinition in entity.ReferenceFieldDefinitions)
            {
                sourceBuilder.AppendLine($"///{fieldDefinition.Id}");
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

    private static string SanitizeIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "_";

        Span<char> buf = stackalloc char[raw.Length];
        var len = 0;
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                buf[len++] = ch;
            else
                buf[len++] = '_';
        }

        var s = new string(buf.Slice(0, len));

        if (char.IsDigit(s[0]))
            s = "_" + s;

        return s;
    }

    public static string GetGuidLiteral(Guid guid)
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
