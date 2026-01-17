using Shared;

namespace Cli.Utils;

public static class ModelLoader
{
    private static readonly Lazy<ProjectModel> Cached = new(() =>
    {
        var modelDir = FindModelDirectory();
        return ProjectModel.CreateFromDirectory(modelDir);
    });

    public static ProjectModel Load() => Cached.Value;

    private static string FindModelDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                // repo-root layout
                var candidate = Path.Combine(dir.FullName, "src", "Shared", "Model");
                if (Directory.Exists(candidate))
                    return candidate;

                // running from within src/ already
                candidate = Path.Combine(dir.FullName, "Shared", "Model");
                if (Directory.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }
        }

        throw new Exception("Could not locate model directory. Expected 'src/Shared/Model' relative to the repo root.");
    }
}
