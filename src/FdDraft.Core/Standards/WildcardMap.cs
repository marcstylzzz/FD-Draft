using System;
using System.Collections.Generic;

namespace FdDraft.Core.Standards
{
    /// <summary>
    /// An ordered code -> value map whose keys may use * and ? wildcards.
    /// An exact key always wins; otherwise the FIRST matching pattern in file
    /// order wins, so a firm can put specific patterns above general ones
    /// (FDSIB before FD*).
    /// </summary>
    public sealed class WildcardMap
    {
        private readonly Dictionary<string, string> _exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, string>> _patterns = new List<KeyValuePair<string, string>>();

        public int Count => _exact.Count + _patterns.Count;

        public void Add(string key, string value)
        {
            key = key.Trim();
            if (key.Length == 0) return;
            if (key.IndexOf('*') >= 0 || key.IndexOf('?') >= 0) _patterns.Add(new KeyValuePair<string, string>(key, value));
            else _exact[key] = value;
        }

        public bool TryGetValue(string key, out string value)
        {
            key = (key ?? "").Trim();
            if (_exact.TryGetValue(key, out value!)) return true;
            foreach (var p in _patterns)
            {
                if (FirmStandards.WildcardMatch(p.Key, key)) { value = p.Value; return true; }
            }
            value = "";
            return false;
        }

        public string Get(string key, string fallback) => TryGetValue(key, out var v) ? v : fallback;
    }
}
