using Shared;

namespace Cli.Utils;

public static class ModelLookup
{
    public static EntityDefinition FindEntity(ProjectModel model, string typeKey)
    {
        var entity = model.EntityDefinitions.FirstOrDefault(e => string.Equals(e.Key, typeKey, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
            throw new Exception($"Unknown type '{typeKey}'. Run 'types' to see available types.");

        return entity;
    }

    public static string FormatType(ProjectModel model, Guid typId)
    {
        var e = model.EntityDefinitions.FirstOrDefault(x => x.Id == typId);
        return e?.Key ?? typId.ToString();
    }
}
