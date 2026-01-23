using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Shared.Database;

//A Database has a "Model" (this model can then reference other models)
//Lets create a metadata table, for the guid of the model is stored


public static class JsonDump
{
    public static string GetJsonDump(Environment env, DbSession dbSession)
    {
        var model = dbSession.GetObjFromGuid<Model.Generated.Model>(env.ModelGuid);
        var entityById = model!.Value.GetAllEntityDefinitions().ToDictionary(x => Guid.Parse(x.Id), x => x);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WriteString("modelGuid", env.ModelGuid);
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
                        var raw = dbSession.GetFldValue(objId, Guid.Parse(field.Id));
                        if (raw.Length == 0)
                            continue;

                        writer.WritePropertyName(field.Key);
                        WriteScalarFieldValue(writer,  Enum.Parse<FieldDataType>(field.DataType) , raw);
                    }

                    foreach (var refField in entity.ReferenceFieldDefinitions)
                    {
                        ArrayBufferWriter<Guid>? collected = null;
                        foreach (var aso in dbSession.EnumerateAso(objId, Guid.Parse(refField.Id)))
                        {
                            collected ??= new ArrayBufferWriter<Guid>();
                            collected.GetSpan(1)[0] = aso.ObjId;
                            collected.Advance(1);

                            if (refField.RefType != nameof(RefType.Multiple))
                                break;
                        }

                        if (collected == null || collected.WrittenCount == 0)
                            continue;

                        writer.WritePropertyName(refField.Key);

                        if (refField.RefType == nameof(RefType.Multiple))
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

    public static void FromJson(string json, DbSession dbSession)
    {
        if (dbSession.IsReadOnly)
            throw new InvalidOperationException("DbSession is read-only");

        using var doc = JsonDocument.Parse(json);

        var modelGuid = doc.RootElement.GetProperty("modelGuid").GetGuid();
        dbSession.Environment.ModelGuid = modelGuid; //todo, is this good?

        if (!doc.RootElement.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Object)
            return;

        // Pass 1: ensure all objects exist, and types match.
        foreach (var entityProp in entities.EnumerateObject())
        {
            if (!Guid.TryParse(entityProp.Name, out var objId))
                continue;

            var entityJson = entityProp.Value;
            if (entityJson.ValueKind != JsonValueKind.Object)
                continue;

            if (!entityJson.TryGetProperty("$type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                continue;

            if (!Guid.TryParse(typeProp.GetString(), out var typId))
                continue;

            var existingTyp = dbSession.GetTypId(objId);
            if (existingTyp == Guid.Empty)
            {
                CreateObjWithId(dbSession, objId, typId);
            }
            else if (existingTyp != typId)
            {
                Logging.Log(LogFlags.Error, $"FromJson: object {objId} exists with type {existingTyp}, expected {typId}; skipping.");
            }
        }


        var model = dbSession.GetObjFromGuid<Model.Generated.Model>(dbSession.Environment.ModelGuid);
        var entityById = model!.Value.GetAllEntityDefinitions().ToDictionary(x => Guid.Parse(x.Id), x => x);


        // Pass 2: set fields and associations.
        foreach (var entityProp in entities.EnumerateObject())
        {
            if (!Guid.TryParse(entityProp.Name, out var objId))
                continue;

            var entityJson = entityProp.Value;
            if (entityJson.ValueKind != JsonValueKind.Object)
                continue;

            if (!entityJson.TryGetProperty("$type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                continue;

            if (!Guid.TryParse(typeProp.GetString(), out var typId))
                continue;

            if (!entityById.TryGetValue(typId, out var entity))
                continue;

            // If the object exists with a different type, we skip it (already logged in pass 1).
            if (dbSession.GetTypId(objId) != typId)
                continue;

            foreach (var field in entity.FieldDefinitions)
            {
                if (entityJson.TryGetProperty(field.Key, out var value))
                {
                    SetScalarFieldFromJson(dbSession, objId, Guid.Parse(field.Id), Enum.Parse<FieldDataType>(field.DataType), value);
                }
                else
                {
                    // Match dump semantics: missing means "unset".
                    dbSession.SetFldValue(objId, Guid.Parse(field.Id), ReadOnlySpan<byte>.Empty);
                }
            }

            foreach (var refField in entity.ReferenceFieldDefinitions)
            {
                var fldIdA = refField.Id;
                var fldIdB = refField.OtherReferenceFields;

                if (!entityJson.TryGetProperty(refField.Key, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    dbSession.RemoveAllAso(objId, Guid.Parse(fldIdA));
                    continue;
                }

                // Always clear existing connections first so the DB matches the json.
                dbSession.RemoveAllAso(objId, Guid.Parse(fldIdA));

                if (refField.RefType == nameof(RefType.Multiple))
                {
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String)
                                continue;

                            if (Guid.TryParse(item.GetString(), out var otherObjId))
                                dbSession.CreateAso(objId, Guid.Parse(fldIdA), otherObjId, Guid.Parse(fldIdB.Id));
                        }
                    }
                    else if (value.ValueKind == JsonValueKind.String)
                    {
                        if (Guid.TryParse(value.GetString(), out var otherObjId))
                            dbSession.CreateAso(objId, Guid.Parse(fldIdA), otherObjId, Guid.Parse(fldIdB.Id));
                    }

                    continue;
                }

                // SingleOptional / SingleMandatory
                if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var singleId))
                {
                    if (singleId != Guid.Empty)
                        dbSession.CreateAso(objId, Guid.Parse(fldIdA), singleId, Guid.Parse(fldIdB.Id));
                }
            }
        }
    }

    private static void CreateObjWithId(DbSession dbSession, Guid objId, Guid typId)
    {
        var val = new ObjValue
        {
            TypId = typId,
            ValueTyp = ValueTyp.Obj
        };

        var keyBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref objId, 1));
        var valueBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1));

        var result = dbSession.Store.Put(keyBuf, valueBuf);
        if (result != ResultCode.Success)
            throw new InvalidOperationException($"Failed creating object {objId} ({typId}).");
    }

    private static void SetScalarFieldFromJson(DbSession dbSession, Guid objId, Guid fldId, FieldDataType type, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
            return;
        }

        switch (type)
        {
            case FieldDataType.String:
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
                    return;
                }

                var s = value.GetString() ?? string.Empty;
                var bytes = Encoding.Unicode.GetBytes(s);
                dbSession.SetFldValue(objId, fldId, bytes);
                return;
            }
            case FieldDataType.Integer:
            {
                if (value.ValueKind != JsonValueKind.Number)
                {
                    dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
                    return;
                }

                long l = value.GetInt64();
                dbSession.SetFldValue(objId, fldId, l.AsSpan());
                return;
            }
            case FieldDataType.Decimal:
            {
                if (value.ValueKind != JsonValueKind.Number)
                {
                    dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
                    return;
                }

                decimal d = value.GetDecimal();
                dbSession.SetFldValue(objId, fldId, d.AsSpan());
                return;
            }
            case FieldDataType.DateTime:
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
                    return;
                }

                var s = value.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
                    return;
                }

                var dt = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                dbSession.SetFldValue(objId, fldId, dt.AsSpan());
                return;
            }
            case FieldDataType.Boolean:
            {
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                {
                    dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
                    return;
                }

                bool b = value.GetBoolean();
                dbSession.SetFldValue(objId, fldId, b.AsSpan());
                return;
            }
            default:
                dbSession.SetFldValue(objId, fldId, ReadOnlySpan<byte>.Empty);
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

public enum RefType
{
    SingleOptional,
    SingleMandatory,
    Multiple
}

public enum FieldDataType
{
    Integer,
    Decimal,
    String,
    DateTime,
    Boolean
}