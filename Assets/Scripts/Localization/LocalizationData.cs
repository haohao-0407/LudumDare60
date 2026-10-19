// PIONEER-60 本地化系统 · 数据结构
// 与 Assets/Resources/Localization/pioneer60_dict.json 一一对应，字段名不要改。
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Pioneer60.Localization
{
    [Serializable]
    public class LocLanguage
    {
        public string code;
        public string displayName;
        public string shortLabel;
    }

    [Serializable]
    public class LocText
    {
        public string lang;
        public string text;
    }

    [Serializable]
    public class LocEntry
    {
        public string key;
        public string category;
        public string speaker;
        public string note;
        public List<LocText> texts = new List<LocText>();
    }

    [Serializable]
    public class LocCommand
    {
        public string id;
        public string canonical;
        public List<string> aliases = new List<string>();
    }

    [Serializable]
    public class LocPattern
    {
        /// <summary>整段/整行的正则，捕获组按 {0} {1} … 填进译文模板。</summary>
        public string regex;
        public List<LocText> texts = new List<LocText>();
    }

    [Serializable]
    public class LocFile
    {
        public int schemaVersion = 1;
        public string defaultLanguage = "en";
        public string fallbackLanguage = "en";
        public string missingKeyFormat = "\u27E6{key}\u27E7";
        public List<LocLanguage> languages = new List<LocLanguage>();
        public List<LocEntry> entries = new List<LocEntry>();
        public List<LocCommand> commands = new List<LocCommand>();
        /// <summary>数字/日期这类每次都不一样的文本走这里，例如 "OWNED 3 / REQUIRED 6"。</summary>
        public List<LocPattern> patterns = new List<LocPattern>();
    }

    /// <summary>词典的运行时索引：key 查询、语言回退、指令别名解析。</summary>
    public class LocalizationDatabase
    {
        readonly LocFile _file;
        readonly Dictionary<string, LocEntry> _byKey = new Dictionary<string, LocEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, LocCommand> _byAlias = new Dictionary<string, LocCommand>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 英文原文 → key。工程里的文案烤在预制体/场景/对话资产里，没有 key，
        // 所以用「英文原文即 key」反查：改文案只加词条，不动代码。
        readonly Dictionary<string, string> _bySource = new Dictionary<string, string>(StringComparer.Ordinal);
        // 任意语言的已知文本 → key。让「切回英文」也能靠查表还原，不依赖记忆。
        readonly Dictionary<string, string> _byAnyText = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<KeyValuePair<Regex, LocPattern>> _patterns = new List<KeyValuePair<Regex, LocPattern>>();
        // 「轧制机电合金板 x7」这类清单行：名字要翻，数量原样带回去
        static readonly Regex CountSuffix = new Regex(@"^(?<name>.+?)\s*(?:x|×|X)\s*(?<count>\d+)$",
            RegexOptions.CultureInvariant);

        public LocFile File { get { return _file; } }
        public IList<LocLanguage> Languages { get { return _file.languages; } }
        public IList<LocCommand> Commands { get { return _file.commands; } }
        public int EntryCount { get { return _file.entries.Count; } }
        public string MissingKeyFormat { get { return _file.missingKeyFormat; } }

        public LocalizationDatabase(LocFile file)
        {
            _file = file ?? new LocFile();
            for (int i = 0; i < _file.entries.Count; i++)
            {
                LocEntry e = _file.entries[i];
                if (e == null || string.IsNullOrEmpty(e.key)) continue;
                _byKey[e.key] = e;

                if (e.texts == null) continue;
                for (int j = 0; j < e.texts.Count; j++)
                {
                    LocText t = e.texts[j];
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    string src = NormalizeSource(t.text);
                    if (src.Length == 0) continue;

                    // 所有语言都进「任意语言」索引：中文界面切回英文时靠它还原
                    if (!_byAnyText.ContainsKey(src)) _byAnyText[src] = e.key;
                    // 只有默认语言（英文）进「原文」索引
                    if (string.Equals(t.lang, _file.defaultLanguage, StringComparison.OrdinalIgnoreCase)
                        && !_bySource.ContainsKey(src))
                        _bySource[src] = e.key;
                }
            }
            for (int i = 0; i < _file.commands.Count; i++)
            {
                LocCommand c = _file.commands[i];
                if (c == null) continue;
                if (!string.IsNullOrEmpty(c.id)) _canonical[c.id] = c.canonical;
                if (c.aliases == null) continue;
                for (int j = 0; j < c.aliases.Count; j++)
                {
                    string a = Normalize(c.aliases[j]);
                    if (!string.IsNullOrEmpty(a)) _byAlias[a] = c;
                }
                string canon = Normalize(c.canonical);
                if (!string.IsNullOrEmpty(canon) && !_byAlias.ContainsKey(canon)) _byAlias[canon] = c;
            }

            if (_file.patterns != null)
            {
                for (int i = 0; i < _file.patterns.Count; i++)
                {
                    LocPattern p = _file.patterns[i];
                    if (p == null || string.IsNullOrEmpty(p.regex)) continue;
                    try
                    {
                        _patterns.Add(new KeyValuePair<Regex, LocPattern>(
                            new Regex(p.regex, RegexOptions.CultureInvariant), p));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Localization] 正则写错了，已跳过：" + p.regex + "\n" + e.Message);
                    }
                }
            }
        }

        public static LocalizationDatabase FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return new LocalizationDatabase(new LocFile());
            return new LocalizationDatabase(JsonUtility.FromJson<LocFile>(json));
        }

        public bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && _byKey.ContainsKey(key);
        }

        public LocEntry GetEntry(string key)
        {
            LocEntry e;
            return (!string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out e)) ? e : null;
        }

        /// <summary>按 language → fallback → 任意非空 的顺序取文本。空字符串也算缺。</summary>
        public bool TryGetText(string key, string language, string fallbackLanguage, out string value)
        {
            value = null;
            LocEntry e = GetEntry(key);
            if (e == null || e.texts == null) return false;

            if (TryPick(e, language, out value)) return true;
            if (!string.Equals(language, fallbackLanguage) && TryPick(e, fallbackLanguage, out value)) return true;
            for (int i = 0; i < e.texts.Count; i++)
            {
                if (e.texts[i] != null && !string.IsNullOrEmpty(e.texts[i].text))
                {
                    value = e.texts[i].text;
                    return true;
                }
            }
            return false;
        }

        static bool TryPick(LocEntry e, string lang, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(lang) || e.texts == null) return false;
            for (int i = 0; i < e.texts.Count; i++)
            {
                LocText t = e.texts[i];
                if (t != null && string.Equals(t.lang, lang, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(t.text))
                {
                    value = t.text;
                    return true;
                }
            }
            return false;
        }

        public LocCommand ResolveCommand(string rawInput)
        {
            string a = Normalize(rawInput);
            LocCommand c;
            return (!string.IsNullOrEmpty(a) && _byAlias.TryGetValue(a, out c)) ? c : null;
        }

        public IList<string> AliasesOf(string commandId)
        {
            for (int i = 0; i < _file.commands.Count; i++)
                if (_file.commands[i] != null && string.Equals(_file.commands[i].id, commandId, StringComparison.OrdinalIgnoreCase))
                    return _file.commands[i].aliases;
            return new List<string>();
        }

        /// <summary>这段文字是不是词典里某条译文（任意语言）。是则给出 key。</summary>
        public bool TryFindKeyByAnyText(string text, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(text)) return false;
            return _byAnyText.TryGetValue(NormalizeSource(text), out key);
        }

        /// <summary>把一段英文原文翻成目标语言。找不到就返回 false，调用方保留原文。</summary>
        public bool TryTranslateSource(string source, string language, string fallbackLanguage, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(source)) return false;

            // 1) 单行：整行匹配，<color> 之类的标签只翻标签之间的文字
            if (source.IndexOf('\n') < 0)
                return TryTranslateSegment(source, language, fallbackLanguage, out value);

            // 2) 多行整段精确匹配（多行台词块、状态块）
            if (TryLine(source, language, fallbackLanguage, out value)) return true;

            // 3) 多行块（材料清单、状态面板）逐行翻，保留缩进；一行都没命中就放弃，别输出半英半中的碎片
            if (source.IndexOf('\n') >= 0)
            {
                string[] lines = source.Split('\n');
                var sb = new StringBuilder(source.Length + 32);
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) sb.Append('\n');
                    string line = lines[i];
                    string body = line.Trim();
                    if (body.Length == 0) { sb.Append(line); continue; }
                    string translated;
                    if (TryTranslateSegment(body, language, fallbackLanguage, out translated))
                    {
                        sb.Append(line.Substring(0, line.Length - line.TrimStart().Length)).Append(translated);
                        changed = true;
                    }
                    else sb.Append(line);
                }
                if (changed) { value = sb.ToString(); return true; }
            }
            return false;
        }

        /// <summary>整行匹配；带 &lt;color&gt; 这类富文本标签时，只翻标签之间的文字。</summary>
        private bool TryTranslateSegment(string text, string language, string fallbackLanguage, out string value)
        {
            if (TryLine(text, language, fallbackLanguage, out value)) return true;

            value = null;
            if (text.IndexOf('<') < 0 || text.IndexOf('>', text.IndexOf('<')) < 0) return false;

            var sb = new StringBuilder(text.Length + 16);
            bool changed = false;
            int i = 0;
            while (i < text.Length)
            {
                int lt = text.IndexOf('<', i);
                int gt = lt < 0 ? -1 : text.IndexOf('>', lt + 1);
                if (lt < 0 || gt < 0)
                {
                    string tail = text.Substring(i);
                    string tailValue;
                    if (tail.Trim().Length > 0 && TryLine(tail.Trim(), language, fallbackLanguage, out tailValue))
                    {
                        sb.Append(tail.Substring(0, tail.Length - tail.TrimStart().Length)).Append(tailValue);
                        changed = true;
                    }
                    else sb.Append(tail);
                    break;
                }

                string before = text.Substring(i, lt - i);
                if (before.Length > 0)
                {
                    string beforeValue;
                    if (TryLine(before.Trim(), language, fallbackLanguage, out beforeValue))
                    {
                        sb.Append(before.Substring(0, before.Length - before.TrimStart().Length)).Append(beforeValue);
                        changed = true;
                    }
                    else sb.Append(before);
                }
                sb.Append(text, lt, gt - lt + 1); // 标签原样保留
                i = gt + 1;
            }
            if (!changed) return false;
            value = sb.ToString();
            return true;
        }

        /// <summary>单行：先整行精确匹配，再走数字模板。</summary>
        private bool TryLine(string text, string language, string fallbackLanguage, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(text)) return false;

            // 用「任意语言」索引：英文原文能查，中文译文也能查回英文
            string normalized = NormalizeSource(text);
            string key;
            if (_byAnyText.TryGetValue(normalized, out key) ||
                _byAnyText.TryGetValue(normalized.Replace("pioneer-60", "{callsign}"), out key))
            {
                if (TryGetText(key, language, fallbackLanguage, out value) && !string.IsNullOrEmpty(value))
                    return true;
            }
            Match counted = CountSuffix.Match(text.Trim());
            if (counted.Success)
            {
                string name;
                if (TryLine(counted.Groups["name"].Value.Trim(), language, fallbackLanguage, out name))
                {
                    value = name + " x" + counted.Groups["count"].Value;
                    return true;
                }
            }

            return TryPattern(text, language, fallbackLanguage, out value);
        }

        private bool TryPattern(string text, string language, string fallbackLanguage, out string value)
        {
            value = null;
            if (_patterns.Count == 0) return false;
            string probe = text.Trim();

            for (int i = 0; i < _patterns.Count; i++)
            {
                Match match = _patterns[i].Key.Match(probe);
                if (!match.Success) continue;

                string template = PickText(_patterns[i].Value.texts, language, fallbackLanguage);
                if (string.IsNullOrEmpty(template)) continue;

                for (int g = 1; g < match.Groups.Count; g++)
                    template = template.Replace("{" + (g - 1) + "}", match.Groups[g].Value);
                value = template;
                return true;
            }
            return false;
        }

        private string PickText(List<LocText> texts, string language, string fallbackLanguage)
        {
            if (texts == null) return null;
            string fallback = null;
            for (int i = 0; i < texts.Count; i++)
            {
                LocText t = texts[i];
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                if (string.Equals(t.lang, language, StringComparison.OrdinalIgnoreCase)) return t.text;
                if (fallback == null) fallback = t.text;
            }
            return fallback;
        }

        /// <summary>原文规范化：折叠所有空白为单个空格 + 转小写，用于「英文原文即 key」的反查。</summary>
        public static string NormalizeSource(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            bool pendingSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        public static string Normalize(string s)        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            bool lastSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastSpace && sb.Length > 0) sb.Append(' ');
                    lastSpace = true;
                    continue;
                }
                sb.Append(char.ToLowerInvariant(ch));
                lastSpace = false;
            }
            return sb.ToString().Trim();
        }
    }
}
