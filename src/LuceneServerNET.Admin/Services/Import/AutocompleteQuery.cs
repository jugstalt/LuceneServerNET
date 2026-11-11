using System.Text;

namespace LuceneServerNET.Admin.Services.Import;

/// <summary>
/// Builds a search-as-you-type query from user input: every word is required and a prefix.
/// "graz haupt" => "+graz* +haupt*" (finds "Graz-Umgebung Hauptplatz"). Lucene special characters are escaped.
/// </summary>
public static class AutocompleteQuery
{
    private const string SpecialCharacters = "+-&|!(){}[]^\"~*?:\\/";

    public static string Build(string? input)
    {
        var words = (input ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(Escape)
                                 .Where(w => w.Length > 0)
                                 .ToArray();
        if (words.Length == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('+').Append(words[i].ToLowerInvariant()).Append('*');
        }
        return sb.ToString();
    }

    private static string Escape(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            if (SpecialCharacters.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
