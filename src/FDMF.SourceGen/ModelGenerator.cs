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

        HashSet<EnumDefinition> enumsToGenerate = [];

        foreach (var entity in GetAllEntityDefinitions(model))
        {
            if (ShouldSkipEntity(entity, includeMetaModel))
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

            var flattenedFields = FlattenFieldDefinitions(entity);
            var flattenedRefFields = FlattenReferenceFieldDefinitions(entity);
            var lineage = GetLineage(entity);
            var ancestors = lineage.Count > 1 ? lineage.GetRange(0, lineage.Count - 1) : [];

            foreach (var field in flattenedFields)
            {
                string? enumTypeName = null;
                if (field.DataType == FieldDataType.Enum)
                {
                    var enumDefOpt = field.Enum;
                    if (!enumDefOpt.HasValue)
                        throw new Exception($"Enum-typed field '{entity.Key}.{field.Key}' is missing EnumDefinition association");

                    enumTypeName = enumDefOpt.Value.Name;
                    enumsToGenerate.Add(enumDefOpt.Value);
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

            foreach (var refField in flattenedRefFields)
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

            if (ancestors.Count > 0)
            {
                sourceBuilder.AppendLine();
                foreach (var ancestor in ancestors)
                {
                    sourceBuilder.AppendLine($"public static implicit operator {ancestor.Key}({entity.Key} value) => new {ancestor.Key} {{ DbSession = value.DbSession, ObjId = value.ObjId }};");
                }

                sourceBuilder.AppendLine();
                foreach (var ancestor in ancestors)
                {
                    sourceBuilder.AppendLine($"public static explicit operator {entity.Key}({ancestor.Key} value)");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.AddIndent();
                    sourceBuilder.AppendLine("var actual = value.DbSession.GetTypId(value.ObjId);");
                    sourceBuilder.AppendLine("if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))");
                    sourceBuilder.AddIndent();
                    sourceBuilder.AppendLine($"throw new System.InvalidCastException(\"Cannot cast '{ancestor.Key}' to '{entity.Key}'\");");
                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine($"return new {entity.Key} {{ DbSession = value.DbSession, ObjId = value.ObjId }};");
                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine("}");
                    sourceBuilder.AppendLine();

                    sourceBuilder.AppendLine($"public static bool TryCastFrom({ancestor.Key} value, out {entity.Key} result)");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.AddIndent();
                    sourceBuilder.AppendLine("var actual = value.DbSession.GetTypId(value.ObjId);");
                    sourceBuilder.AppendLine("if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.AddIndent();
                    sourceBuilder.AppendLine($"result = new {entity.Key} {{ DbSession = value.DbSession, ObjId = value.ObjId }};");
                    sourceBuilder.AppendLine("return true;");
                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine("}");
                    sourceBuilder.AppendLine("result = default;");
                    sourceBuilder.AppendLine("return false;");
                    sourceBuilder.RemoveIndent();
                    sourceBuilder.AppendLine("}");
                    sourceBuilder.AppendLine();
                }
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

            foreach (var fieldDefinition in flattenedFields)
            {
                sourceBuilder.AppendLine($"///{fieldDefinition.Id}");
                sourceBuilder.AppendLine($"public static readonly Guid {fieldDefinition.Key} = {GetGuidLiteral(fieldDefinition.Id)};");
            }

            foreach (var fieldDefinition in flattenedRefFields)
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

        foreach (var enumDefinition in enumsToGenerate)
        {
            var enumBuilder = new SourceBuilder();
            enumBuilder.AppendLine($"namespace {@namespace};");
            enumBuilder.AppendLine();
            var enumName = enumDefinition.Name;
            enumBuilder.AppendLine($"public enum {enumName}");
            enumBuilder.AppendLine("{");
            enumBuilder.AddIndent();

            foreach (var v in enumDefinition.Variants.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                enumBuilder.AppendLine($"{v},");
            }

            enumBuilder.RemoveIndent();
            enumBuilder.AppendLine("}");

            var enumPath = Path.Combine(targetDir, $"{enumName}.cs");
            File.WriteAllText(enumPath, enumBuilder.ToString());
        }
    }

    private static List<EntityDefinition> GetAllEntityDefinitions(Model root)
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

    private static bool ShouldSkipEntity(EntityDefinition entity, bool includeMetaModel)
    {
        if (includeMetaModel)
            return false;

        // Skip MetaModel entities when generating domain models.
        if (entity.ObjId == EntityDefinition.TypId || entity.ObjId == FieldDefinition.TypId || entity.ObjId == ReferenceFieldDefinition.TypId || entity.ObjId == Model.TypId)
            return true;

        // RootEntity is part of the MetaModel.
        if (entity.ObjId == Guid.Parse("0c6d2581-7b18-4c35-bd16-3ae403bbf7a4"))
            return true;

        return false;
    }

    private static List<EntityDefinition> GetLineage(EntityDefinition entity)
    {
        var chain = new List<EntityDefinition>();
        var seen = new HashSet<Guid>();

        var current = entity;
        while (true)
        {
            chain.Add(current);
            var parent = current.Parent;
            if (!parent.HasValue)
                break;

            current = parent.Value;
            if (!seen.Add(current.ObjId))
                throw new Exception($"Inheritance cycle detected at '{current.Key}'");
        }

        chain.Reverse();
        return chain;
    }

    private static List<FieldDefinition> FlattenFieldDefinitions(EntityDefinition entity)
    {
        var lineage = GetLineage(entity);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<FieldDefinition>();

        foreach (var e in lineage)
        {
            foreach (var f in e.FieldDefinitions)
            {
                if (!seen.Add(f.Key))
                    throw new Exception($"Duplicate field key '{f.Key}' in inheritance chain for '{entity.Key}'");
                result.Add(f);
            }
        }

        return result;
    }

    private static List<ReferenceFieldDefinition> FlattenReferenceFieldDefinitions(EntityDefinition entity)
    {
        var lineage = GetLineage(entity);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ReferenceFieldDefinition>();

        foreach (var e in lineage)
        {
            foreach (var f in e.ReferenceFieldDefinitions)
            {
                if (!seen.Add(f.Key))
                    throw new Exception($"Duplicate reference field key '{f.Key}' in inheritance chain for '{entity.Key}'");
                result.Add(f);
            }
        }

        return result;
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
