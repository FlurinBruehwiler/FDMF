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
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        List<EntityDefinition> entities = [];
        foreach (var entityJson in Directory.EnumerateFiles(dir))
        {
            var entity = JsonSerializer.Deserialize<EntityDefinition>(File.ReadAllText(entityJson), options);
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
}

public class ReferenceFieldDefinition
{
    public Guid Id;
    public string Key = "";
    public TranslationText Name;
    public RefType RefType;
    public Guid OtherReferenceFielGuid;
    public bool IsIndexed;

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