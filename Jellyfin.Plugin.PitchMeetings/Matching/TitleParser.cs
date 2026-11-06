using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PitchMeetings.Matching;

/// <summary>What a Pitch Meeting episode title says about the movie or show it is about.</summary>
public sealed record ParsedTitle(string Cleaned, int? Year, int? Season, IReadOnlyList<string> Keys);

/// <summary>
/// Turns titles into comparable keys. Both sides (episode titles and movie/series titles) go through
/// <see cref="Keys"/>, so differences in case, punctuation, accents, "&amp;"/"and", roman numerals,
/// hyphens/colons and a leading article do not matter ("The Fantastic Four - First Steps" ==
/// "The Fantastic Four: First Steps" == "Fantastic Four First Steps").
/// </summary>
public static partial class TitleParser
{
    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.Ordinal)
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9",
        ["volume"] = "vol", ["pt"] = "part", ["and"] = "&"
    };

    private static readonly string[] Articles = { "the", "a", "an" };

    public static ParsedTitle Parse(string? title)
    {
        var s = (title ?? string.Empty).Trim();

        // channel decoration: "| Ryan George", "- Screen Rant", "Pitch Meeting", "Classic Pitch Meeting"
        s = PipeSuffix().Replace(s, string.Empty);
        s = ChannelNames().Replace(s, " ");
        s = PitchWords().Replace(s, " ");

        int? year = null;
        var ym = YearInParens().Match(s);
        if (ym.Success)
        {
            year = int.Parse(ym.Groups[1].Value, CultureInfo.InvariantCulture);
            s = s.Remove(ym.Index, ym.Length);
        }

        int? season = null;
        var sm = SeasonPart().Match(s);
        if (sm.Success)
        {
            season = int.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture);
            s = s[..sm.Index];
        }

        s = TrailingSeparators().Replace(LeadingSeparators().Replace(s, string.Empty), string.Empty).Trim();
        s = MultiSpace().Replace(s, " ");

        var keys = new List<string>();
        AddKeys(keys, s);

        // "Lee Cronin's The Mummy" may be listed as plain "The Mummy"
        var pm = PossessivePrefix().Match(s);
        if (pm.Success && pm.Length < s.Length)
        {
            AddKeys(keys, s[pm.Length..]);
        }

        return new ParsedTitle(s, year, season, keys);
    }

    /// <summary>Keys a movie/series title is indexed under (exact form first).</summary>
    public static IEnumerable<string> Keys(string? title)
    {
        var list = new List<string>();
        AddKeys(list, title);
        return list;
    }

    private static void AddKeys(List<string> keys, string? title)
    {
        var tokens = Tokens(title);
        if (tokens.Count == 0)
        {
            return;
        }

        Add(keys, string.Concat(tokens));
        if (tokens.Count > 1 && Array.IndexOf(Articles, tokens[0]) >= 0)
        {
            Add(keys, string.Concat(tokens.Skip(1)));
        }

        static void Add(List<string> keys, string key)
        {
            if (key.Length > 0 && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }
    }

    private static List<string> Tokens(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new List<string>();
        }

        var decomposed = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is '\'' or '’' or '‘' or '`' or '´')
            {
                continue; // "Cronin's" -> "cronins"
            }

            if (c is '&' or '+')
            {
                sb.Append(" and ");
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(' ');
            }
        }

        var result = new List<string>();
        foreach (var t in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result.Add(TokenMap.TryGetValue(t, out var mapped) ? mapped : t);
        }

        return result;
    }

    [GeneratedRegex(@"\s*[|｜].*$")]
    private static partial Regex PipeSuffix();

    [GeneratedRegex(@"(?i)(?:[-–—:]\s*)?\b(?:ryan\s+george|screen\s*rant)\b")]
    private static partial Regex ChannelNames();

    [GeneratedRegex(@"(?i)(?:[-–—:]\s*)?\b(?:classic\s+|super\s+easy\s+)?pitch\s+meetings?\b(?:\s*[-–—:])?")]
    private static partial Regex PitchWords();

    [GeneratedRegex(@"\(\s*((?:19|20)\d{2})\s*\)")]
    private static partial Regex YearInParens();

    [GeneratedRegex(@"(?i)\s*[-–—:,]?\s*\b(?:seasons?|staffel|series)\s+(\d{1,2})\b.*$")]
    private static partial Regex SeasonPart();

    [GeneratedRegex(@"^[\s\-–—:|,]+")]
    private static partial Regex LeadingSeparators();

    [GeneratedRegex(@"[\s\-–—:|,]+$")]
    private static partial Regex TrailingSeparators();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"^(?:[\p{L}.\-]+\s){0,3}[\p{L}.\-]+['’]s\s+")]
    private static partial Regex PossessivePrefix();
}
