using System.Runtime.CompilerServices;

namespace FDMF.SourceGen;

public class Helper
{
    public static string GetRootDir([CallerFilePath] string str = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(str)!, "../"));
    }
}