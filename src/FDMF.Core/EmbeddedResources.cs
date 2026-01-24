namespace FDMF.Core;

public static class EmbeddedResources
{
    public static string BaseModel { get; } = ReadEmbeddedResource("Core.Dumps.BaseModel.json");

    private static string ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(EmbeddedResources).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Resource '{resourceName}' not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}