using System.Text;
using System.Text.RegularExpressions;

namespace FdDraft.Core.Layout
{
    /// <summary>
    /// The title-block text pattern language, deliberately small enough to run in
    /// AutoLISP inside MSCAD as well as here:
    ///   #   a number: digits, with decimal parts ("300", "7.5") - a trailing period
    ///       is left alone, so "1:300." keeps its full stop
    ///   *   the rest of the text
    ///   anything else matches itself, case-insensitively.
    /// A pattern matches anywhere in the text; every match is replaced.
    /// </summary>
    public static class SimplePattern
    {
        public static Regex ToRegex(string pattern)
        {
            var sb = new StringBuilder();
            foreach (char c in pattern)
            {
                if (c == '#') sb.Append(@"[0-9]+(?:\.[0-9]+)*");
                else if (c == '*') sb.Append(".*$");
                else sb.Append(Regex.Escape(c.ToString()));
            }
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static bool IsMatch(string pattern, string text) => ToRegex(pattern).IsMatch(text);

        public static string Replace(string pattern, string text, string replacement) =>
            ToRegex(pattern).Replace(text, _ => replacement);
    }
}
