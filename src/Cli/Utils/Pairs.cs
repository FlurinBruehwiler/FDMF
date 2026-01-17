namespace Cli.Utils;

public static class Pairs
{
    public static (string key, string value) Split(string s)
    {
        var idx = s.IndexOf('=');
        if (idx <= 0)
            throw new Exception($"Invalid pair '{s}'. Expected Key=Value.");

        // Allow empty value ("Key=") to mean "clear".
        return (s.Substring(0, idx), idx == s.Length - 1 ? string.Empty : s.Substring(idx + 1));
    }
}
