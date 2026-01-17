namespace Cli.Utils;

public static class TablePrinter
{
    public static void Print(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        if (headers.Count == 0)
            return;

        var widths = new int[headers.Count];
        for (int i = 0; i < headers.Count; i++)
            widths[i] = Math.Min(40, headers[i].Length);

        foreach (var r in rows)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                var cell = i < r.Length ? (r[i] ?? string.Empty) : string.Empty;
                widths[i] = Math.Min(60, Math.Max(widths[i], cell.Length));
            }
        }

        static string Fit(string s, int w)
        {
            s ??= string.Empty;
            if (s.Length <= w) return s;
            return s.Substring(0, Math.Max(0, w - 3)) + "...";
        }

        var headerLine = string.Join(" | ", headers.Select((h, i) => Fit(h, widths[i]).PadRight(widths[i])));
        Console.WriteLine(headerLine);
        Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));

        foreach (var r in rows)
        {
            var line = string.Join(" | ", headers.Select((_, i) => Fit(i < r.Length ? r[i] ?? string.Empty : string.Empty, widths[i]).PadRight(widths[i])));
            Console.WriteLine(line);
        }
    }
}
