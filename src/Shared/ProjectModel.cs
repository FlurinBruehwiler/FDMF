using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

public class ProjectModel
{
    public EntityDefinition[] EntityDefinitions = [];

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

        var dict = entities.SelectMany(x => x.ReferenceFields).ToDictionary(x => x.Id, x => x);
        foreach (var entityDefinition in entities)
        {
            foreach (var refField in entityDefinition.ReferenceFields)
            {
                refField.OwningEntity = entityDefinition;
                refField.OtherReferenceField = dict[refField.OtherReferenceFielGuid];
            }
        }

        return new ProjectModel
        {
            EntityDefinitions = entities.ToArray()
        };
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