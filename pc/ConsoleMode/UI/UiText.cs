using System.Text;

namespace ConsoleMode.UI;

/// <summary>Text helpers for the window controls.</summary>
internal static class UiText
{
    /// <summary>
    /// Splits a long explanation into lines of at most <paramref name="maxLineLength"/> characters (a longer word stays
    /// whole), keeping the line breaks it already has. WinForms tooltips do not wrap on their own: without this they
    /// show the whole text on a single line as wide as the screen.
    /// </summary>
    public static string WrapForTooltip(string text, int maxLineLength = 70)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineLength, 1);

        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            line.Clear();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > maxLineLength)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0)
                {
                    line.Append(' ');
                }
                line.Append(word);
            }
            lines.Add(line.ToString());
        }
        return string.Join("\n", lines);
    }
}
