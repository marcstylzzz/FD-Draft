using System.Collections.Generic;
using System.Text;

namespace FdDraft.Core.Job
{
    /// <summary>
    /// The same minimal RFC-4180 reader FD-Pro writes with (PointsFormat.splitCsv):
    /// quoted fields, doubled quotes inside them, nothing else. Kept identical on
    /// purpose so a file FD-Pro can read back, FD-Draft reads the same way.
    /// </summary>
    public static class Csv
    {
        public static List<string> Split(string line)
        {
            var output = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted && ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = !quoted;
                }
                else if (ch == ',' && !quoted)
                {
                    output.Add(cell.ToString());
                    cell.Length = 0;
                }
                else
                {
                    cell.Append(ch);
                }
            }
            output.Add(cell.ToString());
            return output;
        }

        /// <summary>Column lookup by header name, case-insensitive; -1 when absent.</summary>
        public sealed class Header
        {
            private readonly List<string> _names;

            public Header(string headerLine)
            {
                _names = new List<string>();
                foreach (var name in Split(headerLine)) _names.Add(name.Trim());
            }

            public int IndexOf(string name)
            {
                for (int i = 0; i < _names.Count; i++)
                {
                    if (string.Equals(_names[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
                }
                return -1;
            }

            /// <summary>First of several accepted spellings that is present.</summary>
            public int IndexOfAny(params string[] names)
            {
                foreach (var n in names)
                {
                    int i = IndexOf(n);
                    if (i >= 0) return i;
                }
                return -1;
            }
        }

        public static string Cell(List<string> cells, int index) =>
            index >= 0 && index < cells.Count ? cells[index] : string.Empty;
    }
}
