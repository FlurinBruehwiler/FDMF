using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

public class ProjectModel
{
    public required EntityDefinition[] EntityDefinitions;
    public Dictionary<Guid, FieldDefinition> FieldsById = [];
    public Dictionary<Guid, ReferenceFieldDefinition> AsoFieldsById = [];

    public static ProjectModel CreateFromDirectory(string dir)
    {
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new GuidConverterWithEmptyString());

        List<EntityDefinition> entities = [];
        foreach (var entityJson in Directory.EnumerateFiles(dir))
        {
            var entity = JsonSerializer.Deserialize<EntityDefinition>(File.ReadAllText(entityJson), options);

            File.WriteAllText(entityJson, JsonSerializer.Serialize(entity, options));

            entities.Add(entity!);
        }

        var model = new ProjectModel
        {
            EntityDefinitions = entities.ToArray(),
        };
        model.Resolve();

        return model;
    }

    public ProjectModel Resolve()
    {
        var dict = EntityDefinitions.SelectMany(x => x.ReferenceFields).ToDictionary(x => x.Id, x => x);
        foreach (var entityDefinition in EntityDefinitions)
        {
            foreach (var fld in entityDefinition.Fields)
            {
                fld.OwningEntity = entityDefinition;
            }

            foreach (var refField in entityDefinition.ReferenceFields)
            {
                refField.OwningEntity = entityDefinition;
                refField.OtherReferenceField = dict[refField.OtherReferenceFielGuid];
            }
        }

        FieldsById = EntityDefinitions.SelectMany(x => x.Fields).ToDictionary(x => x.Id, x => x);
        AsoFieldsById = EntityDefinitions.SelectMany(x => x.ReferenceFields).ToDictionary(x => x.Id, x => x);

        return this;
    }
}

public class GuidConverterWithEmptyString : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
                return Guid.NewGuid(); // generate a new Guid for empty strings
            return Guid.Parse(s);
        }
        throw new JsonException($"Unexpected token {reader.TokenType} when parsing a Guid.");
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public class EntityDefinition
{
    public Guid Id;
    public string Key = "";
    public TranslationText Name;
    public FieldDefinition[] Fields = [];
    public ReferenceFieldDefinition[] ReferenceFields = [];
}

public class FieldDefinition
{
    public Guid Id;
    public string Key = "";
    public TranslationText Name;
    public FieldDataType DataType;
    public bool IsIndexed;

    [JsonIgnore]
    public EntityDefinition OwningEntity;
}

public class ReferenceFieldDefinition
{
    public Guid Id;
    public string Key = "";
    public TranslationText Name;
    public RefType RefType;
    public Guid OtherReferenceFielGuid;

    [JsonIgnore]
    public EntityDefinition OwningEntity;

    [JsonIgnore] public ReferenceFieldDefinition OtherReferenceField;
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

public struct TranslationText
{
    public string Default;
}