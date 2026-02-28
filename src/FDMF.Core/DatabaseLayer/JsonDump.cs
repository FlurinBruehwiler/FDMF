using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FDMF.Core.DatabaseLayer;

//A Database has a "Model" (this model can then reference other models)
//Lets create a metadata table, for the guid of the model is stored


public static class JsonDump
{
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

    public static string GetJsonDump(DbSession dbSession)
    {
        var model = dbSession.GetObjFromGuid<Model>(dbSession.DbEnvironment.ModelGuid);
        var entityById = GetAllEntityDefinitions(model!.Value).ToDictionary(x => x.Id, x => x);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WriteString("modelGuid", dbSession.DbEnvironment.ModelGuid);
            writer.WritePropertyName("entities");
            writer.WriteStartObject();

            foreach (var (objId, typId) in dbSession.EnumerateObjs())
            {
                writer.WritePropertyName(objId.ToString());
                writer.WriteStartObject();

                writer.WriteString("$type", typId.ToString());

                if (entityById.TryGetValue(typId, out var entity))
                {
                    foreach (var field in entity.FieldDefinitions)
                    {
                        var raw = dbSession.GetFldValue(objId, field.Id);
                        if (raw.Length == 0)
                            continue;

                        writer.WritePropertyName(field.Key);
                        WriteScalarFieldValue(writer, field.DataType , raw);
                    }

                    foreach (var refField in entity.ReferenceFieldDefinitions)
                    {
                        ArrayBufferWriter<Guid>? collected = null;
                        foreach (var aso in dbSession.EnumerateAso(objId, refField.Id))
                        {
                            collected ??= new ArrayBufferWriter<Guid>();
                            collected.GetSpan(1)[0] = aso.ObjId;
                            collected.Advance(1);

                            if (refField.RefType != RefType.Multiple)
                                break;
                        }

                        if (collected == null || collected.WrittenCount == 0)
                            continue;

                        writer.WritePropertyName(refField.Key);

                        if (refField.RefType == RefType.Multiple)
                        {
                            writer.WriteStartArray();
                            foreach (var id in collected.WrittenSpan)
                            {
                                writer.WriteStringValue(id.ToString());
                            }
                            writer.WriteEndArray();
                        }
                        else
                        {
                            writer.WriteStringValue(collected.WrittenSpan[0].ToString());
                        }
                    }
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Model FromJson(string json, DbSession dbSession)
    {
        if (dbSession.IsReadOnly)
            throw new InvalidOperationException("DbSession is read-only");

        using var doc = JsonDocument.Parse(json);

        var modelGuid = doc.RootElement.GetProperty("modelGuid").GetGuid();

        var model = dbSession.GetObjFromGuid<Model>(dbSession.CreateObj(Model.TypId, modelGuid))!.Value;

        bool hasImportedModel = false;
        if (doc.RootElement.TryGetProperty("importedModels", out var importedModels) && importedModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var importedModelGuid in importedModels.EnumerateArray())
            {
                if (importedModelGuid.TryGetGuid(out var guid))
                {
                    var importedModel = dbSession.GetObjFromGuid<Model>(guid)!.Value;
                    model.ImportedModels.Add(importedModel);
                    hasImportedModel = true;
                }
            }
        }

        //if there wasn't an imported model, we import the BaseModel
        if (!hasImportedModel)
        {
            var baseModel = dbSession.GetObjFromGuid<Model>(Guid.Parse("8F4D2969-355F-46A3-8892-D86980A80475"))!.Value;
            model.ImportedModels.Add(baseModel);
        }

        if (doc.RootElement.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Object)
            ParseEntities(dbSession, entities, model);

        return model;
    }

    private static bool TryGetModelType(Guid guid)
    {
        return guid == EntityDefinition.TypId || guid == FieldDefinition.TypId || guid == ReferenceFieldDefinition.TypId;
    }

    private static void ParseEntities(DbSession dbSession, JsonElement entities, Model model)
    {
        var _ = GetAllEntityDefinitions(model).ToDictionary(x => x.Id, x => x);

        //todo we would actually need to first discover the new entities that are defined

        // Pass 1 create all objects
        foreach (var entityProp in entities.EnumerateObject())
        {
            if (!Guid.TryParse(entityProp.Name, out var objId))
            {
                Logging.Log(LogFlags.Error, $"Expected Guid, but was {objId}");
                continue;
            }

            var entityJson = entityProp.Value;
            if (entityJson.ValueKind != JsonValueKind.Object)
            {
                Logging.Log(LogFlags.Error, $"Expected Object, but was {entityJson.ValueKind}");
                continue;
            }

            if (!entityJson.TryGetProperty("$type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                continue;

            if (!Guid.TryParse(typeProp.GetString(), out var typId))
            {
                Logging.Log(LogFlags.Error, $"Expected Guid, but was {typeProp.GetString()}");
                continue;
            }

            if(model.ObjId == objId)
                continue; //model was created previously

            var existingTyp = dbSession.GetTypId(objId);
            if (existingTyp == Guid.Empty)
            {
                //we check later if the type actually exists, because at this point the type might not exist
                dbSession.CreateObj(typId, objId);

                if (TryGetModelType(typId))
                {
                    FillFieldsAndAssocs(dbSession, objId, entityProp.Value, dbSession.GetObjFromGuid<EntityDefinition>(typId)!.Value);
                }
            }
            else if (existingTyp != typId)
            {
                Logging.Log(LogFlags.Error, $"FromJson: object {objId} exists with type {existingTyp}, expected {typId}; skipping.");
            }
        }

        var entityById = GetAllEntityDefinitions(model).ToDictionary(x => x.Id, x => x);

        //TODO: better error handling

        // Pass 2: set fields and associations.
        foreach (var entityProp in entities.EnumerateObject())
        {
            if (!Guid.TryParse(entityProp.Name, out var objId))
            {
                Logging.Log(LogFlags.Error, $"Expected Guid, but was {entityProp.Name}");
                continue;
            }

            var entityJson = entityProp.Value;
            if (entityJson.ValueKind != JsonValueKind.Object)
            {
                Logging.Log(LogFlags.Error, $"Expected Object, but was {entityJson.ValueKind}");
                continue;
            }

            if (!entityJson.TryGetProperty("$type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                continue;

            if (!Guid.TryParse(typeProp.GetString(), out var typId))
                continue;

            if(TryGetModelType(typId))
                continue;

            if (!entityById.TryGetValue(typId, out var entity))
                continue;

            // If the object exists with a different type, we skip it (already logged in pass 1).
            if (dbSession.GetTypId(objId) != typId)
                continue;

            FillFieldsAndAssocs(dbSession, objId, entityJson, entity);
        }
    }

    private static void FillFieldsAndAssocs(DbSession dbSession, Guid objId, JsonElement entityJson, EntityDefinition entity)
    {
        //check that all properties are correct
        foreach (var jsonProperty in entityJson.EnumerateObject())
        {
            if (jsonProperty.NameEquals("$type"))
                continue;

            if (entity.FieldDefinitions.All(x => x.Key != jsonProperty.Name) && entity.ReferenceFieldDefinitions.All(x => x.Key != jsonProperty.Name))
            {
                Logging.Log(LogFlags.Info, $"There is no field with the key {jsonProperty.Name} on the type {entity.Key}"); //todo inheritance
            }
        }

        foreach (var field in entity.FieldDefinitions)
        {
            if (entityJson.TryGetProperty(field.Key, out var value))
            {
                SetScalarFieldFromJson(dbSession, objId, field, value);
            }
            else
            {
                // Match dump semantics: missing means "unset".
                dbSession.SetFldValue(objId, field.Id, ReadOnlySpan<byte>.Empty);
            }
        }

        foreach (var refField in entity.ReferenceFieldDefinitions)
        {
            var fldIdA = refField.Id;
            var fldIdB = refField.OtherReferenceFields;

            if (!entityJson.TryGetProperty(refField.Key, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                // dbSession.RemoveAllAso(objId, Guid.Parse(fldIdA));
                continue;
            }

            // Always clear existing connections first so the DB matches the json.
            //dbSession.RemoveAllAso(objId, Guid.Parse(fldIdA));

            if (refField.RefType == RefType.Multiple)
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            continue;

                        if (Guid.TryParse(item.GetString(), out var otherObjId))
                            dbSession.CreateAso(objId, fldIdA, otherObjId, fldIdB.Id);
                    }
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    if (Guid.TryParse(value.GetString(), out var otherObjId))
                        dbSession.CreateAso(objId, fldIdA, otherObjId, fldIdB.Id);
                }

                continue;
            }

            // SingleOptional / SingleMandatory
            if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var singleId))
            {
                if (singleId != Guid.Empty)
                    dbSession.CreateAso(objId, fldIdA, singleId, fldIdB.Id);
            }
        }
    }

    private static void SetScalarFieldFromJson(DbSession dbSession, Guid objId, FieldDefinition fld, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
            return;
        }

        switch (fld.DataType)
        {
            case FieldDataType.String:
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                var s = value.GetString() ?? string.Empty;

                dbSession.SetFldValue(objId, fld.Id, MemoryMarshal.Cast<char, byte>(s.AsSpan()));
                return;
            }
            case FieldDataType.Integer:
            {
                if (value.ValueKind != JsonValueKind.Number)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                long l = value.GetInt64();
                dbSession.SetFldValue(objId, fld.Id, l.AsSpan());
                return;
            }
            case FieldDataType.Decimal:
            {
                if (value.ValueKind != JsonValueKind.Number)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                decimal d = value.GetDecimal();
                dbSession.SetFldValue(objId, fld.Id, d.AsSpan());
                return;
            }
            case FieldDataType.DateTime:
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                var s = value.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                var dt = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                dbSession.SetFldValue(objId, fld.Id, dt.AsSpan());
                return;
            }
            case FieldDataType.Boolean:
            {
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                bool b = value.GetBoolean();
                dbSession.SetFldValue(objId, fld.Id, b.AsSpan());
                return;
            }
            case FieldDataType.Guid:
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                if (value.GetString() is { } s)
                {
                    var g = Guid.Parse(s);
                    dbSession.SetFldValue(objId, fld.Id, g.AsSpan());
                }
                else
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                }

                return;
            }
            case FieldDataType.Enum:
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                    return;
                }

                if (value.GetString() is { } s)
                {
                    var enumDef = fld.Enum;
                    if (!enumDef.HasValue)
                        throw new Exception("Enum field missing EnumDefinition association");

                    var enumVariants = enumDef.Value.Variants.AsSpan();

                    int idx = 0;
                    foreach (var s1 in enumVariants.Split(','))
                    {
                        if (enumVariants[s1].SequenceEqual(s))
                        {
                            dbSession.SetFldValue(objId, fld.Id, idx.AsSpan());
                            return;
                        }

                        idx++;
                    }

                    throw new Exception("invalid enum");
                }

                dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                break;
            }
            default:
                dbSession.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                return;
        }
    }

    private static void WriteScalarFieldValue(Utf8JsonWriter writer, FieldDataType type, ReadOnlySpan<byte> raw)
    {
        // Values are stored as the payload of VAL entries; DbSession.GetFldValue already strips the ValueTyp tag.
        switch (type)
        {
            case FieldDataType.String:
                writer.WriteStringValue(Encoding.Unicode.GetString(raw));
                return;
            case FieldDataType.Integer:
                writer.WriteNumberValue(raw.Length >= sizeof(long) ? MemoryMarshal.Read<long>(raw) : 0L);
                return;
            case FieldDataType.Decimal:
                writer.WriteNumberValue(raw.Length >= sizeof(decimal) ? MemoryMarshal.Read<decimal>(raw) : 0m);
                return;
            case FieldDataType.DateTime:
                writer.WriteStringValue(raw.Length >= sizeof(long)
                    ? MemoryMarshal.Read<DateTime>(raw).ToString("O")
                    : default(DateTime).ToString("O"));
                return;
            case FieldDataType.Boolean:
                writer.WriteBooleanValue(raw.Length >= sizeof(bool) && MemoryMarshal.Read<bool>(raw));
                return;
            default:
                writer.WriteStringValue(Convert.ToBase64String(raw));
                return;
        }
    }
}
