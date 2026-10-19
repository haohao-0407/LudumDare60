// PIONEER-60 本地化系统 · 编辑器工具
// 放在任意 Editor 文件夹下即可。四个菜单：
//   1 从 CSV 重建词典 JSON   —— 日常改完文案跑这个
//   2 校验词典              —— 重复 key / 空翻译 / 占位符不一致 / 说话人缺标签
//   3 从 JSON 反向导出 CSV   —— 别人改过 JSON 时回写
//   4 列出未被代码引用的 key  —— 清理死词条用，仅提示
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Pioneer60.Localization.EditorTools
{
    public static class LocalizationTools
    {
        // ── 路径：按你们的工程结构改这两行 ─────────────────────
        const string CsvPath = "Assets/Localization/pioneer60_dict.csv";
        const string JsonPath = "Assets/Resources/Localization/pioneer60_dict.json";

        // 语言表：加语言时这里加一项，同时在 CSV 里加一列（列名 = code）
        static readonly LocLanguage[] Languages =
        {
            new LocLanguage { code = "en", displayName = "English", shortLabel = "EN" },
            new LocLanguage { code = "zh-Hans", displayName = "简体中文", shortLabel = "中文" },
        };
        const string DefaultLanguage = "en";
        const string FallbackLanguage = "en";
        const string MissingKeyFormat = "\u27E6{key}\u27E7";

        static readonly string[] KnownCategories =
        {
            "speaker", "log", "dialogue", "system", "update", "engine_test",
            "departure", "ending", "ui", "item", "material", "repair", "command", "misc"
        };

        // ── 指令别名（与 CSV 无关，改这里） ─────────────────────
        static readonly LocCommand[] Commands =
        {
            Cmd("collect", "collect", "collect", "采集", "caiji"),
            Cmd("inventory", "inventory", "inventory", "inv", "仓库", "cangku"),
            Cmd("assemble", "assemble", "assemble", "asm", "组装", "zuzhuang"),
            Cmd("help", "help", "help", "?", "帮助", "bangzhu"),
        };

        static LocCommand Cmd(string id, string canonical, params string[] aliases)
        {
            var c = new LocCommand { id = id, canonical = canonical };
            c.aliases = new List<string>(aliases);
            return c;
        }

        // ══════════════════════════ 菜单 ══════════════════════════
        [MenuItem("Tools/PIONEER-60/本地化/1 从 CSV 重建词典 JSON", false, 10)]
        public static void RebuildJsonFromCsv()
        {
            if (!File.Exists(CsvPath))
            {
                EditorUtility.DisplayDialog("找不到 CSV", "缺少文件：\n" + CsvPath +
                    "\n\n请把 pioneer60_dict.csv 放到该路径，或在 LocalizationTools.cs 顶部改 CsvPath。", "好");
                return;
            }

            string[] header;
            var rows = ReadCsv(CsvPath, out header);
            if (header == null || header.Length < 4)
            {
                EditorUtility.DisplayDialog("CSV 表头不对", "表头至少要有：key,category,speaker,<语言列>...", "好");
                return;
            }

            var langColumns = new List<(string code, int index)>();
            int noteIndex = -1;
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i].Trim();
                if (h == "note") { noteIndex = i; continue; }
                if (h == "key" || h == "category" || h == "speaker") continue;
                langColumns.Add((h, i));
            }
            if (langColumns.Count == 0)
            {
                EditorUtility.DisplayDialog("CSV 里没有语言列", "表头里除 key/category/speaker/note 外，至少还要有一列语言（列名 = 语言 code，如 en、zh-Hans）。", "好");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schemaVersion\": 1,\n");
            sb.Append("  \"defaultLanguage\": ").Append(JStr(DefaultLanguage)).Append(",\n");
            sb.Append("  \"fallbackLanguage\": ").Append(JStr(FallbackLanguage)).Append(",\n");
            sb.Append("  \"missingKeyFormat\": ").Append(JStr(MissingKeyFormat)).Append(",\n");

            sb.Append("  \"languages\": [\n");
            for (int i = 0; i < Languages.Length; i++)
            {
                var l = Languages[i];
                sb.Append("    { \"code\": ").Append(JStr(l.code))
                  .Append(", \"displayName\": ").Append(JStr(l.displayName))
                  .Append(", \"shortLabel\": ").Append(JStr(l.shortLabel)).Append(" }")
                  .Append(i == Languages.Length - 1 ? "\n" : ",\n");
            }
            sb.Append("  ],\n");

            sb.Append("  \"entries\": [\n");
            int written = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                string key = Cell(row, 0);
                if (string.IsNullOrEmpty(key) || key.StartsWith("#")) continue;

                if (written > 0) sb.Append(",\n");
                written++;
                sb.Append("    {\n");
                sb.Append("      \"key\": ").Append(JStr(key)).Append(",\n");
                sb.Append("      \"category\": ").Append(JStr(Cell(row, 1))).Append(",\n");
                sb.Append("      \"speaker\": ").Append(JStr(Cell(row, 2))).Append(",\n");
                sb.Append("      \"note\": ").Append(JStr(noteIndex >= 0 ? Cell(row, noteIndex) : "")).Append(",\n");
                sb.Append("      \"texts\": [\n");
                for (int i = 0; i < langColumns.Count; i++)
                {
                    sb.Append("        { \"lang\": ").Append(JStr(langColumns[i].code))
                      .Append(", \"text\": ").Append(JStr(Cell(row, langColumns[i].index))).Append(" }")
                      .Append(i == langColumns.Count - 1 ? "\n" : ",\n");
                }
                sb.Append("      ]\n");
                sb.Append("    }");
            }
            sb.Append("\n  ],\n");

            sb.Append("  \"commands\": [\n");
            for (int i = 0; i < Commands.Length; i++)
            {
                sb.Append("    { \"id\": ").Append(JStr(Commands[i].id))
                  .Append(", \"canonical\": ").Append(JStr(Commands[i].canonical))
                  .Append(", \"aliases\": [")
                  .Append(string.Join(", ", Commands[i].aliases.Select(JStr)))
                  .Append("] }")
                  .Append(i == Commands.Length - 1 ? "\n" : ",\n");
            }
            sb.Append("  ],\n");

            // 数字模板不在 CSV 里，重建时从旧 JSON 原样搬过来，别丢
            sb.Append("  \"patterns\": [\n");
            var previous = File.Exists(JsonPath)
                ? JsonUtility.FromJson<LocFile>(File.ReadAllText(JsonPath))
                : null;
            int patternCount = 0;
            if (previous != null && previous.patterns != null)
            {
                for (int i = 0; i < previous.patterns.Count; i++)
                {
                    var p = previous.patterns[i];
                    if (p == null || string.IsNullOrEmpty(p.regex)) continue;
                    if (patternCount > 0) sb.Append(",\n");
                    patternCount++;
                    sb.Append("    { \"regex\": ").Append(JStr(p.regex)).Append(", \"texts\": [");
                    var texts = p.texts ?? new List<LocText>();
                    for (int t = 0; t < texts.Count; t++)
                    {
                        sb.Append(t == 0 ? "" : ", ")
                          .Append("{ \"lang\": ").Append(JStr(texts[t].lang))
                          .Append(", \"text\": ").Append(JStr(texts[t].text)).Append(" }");
                    }
                    sb.Append("] }");
                }
                if (patternCount > 0) sb.Append("\n");
            }
            sb.Append("  ]\n}\n");

            Directory.CreateDirectory(Path.GetDirectoryName(JsonPath) ?? "Assets");
            File.WriteAllText(JsonPath, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceUpdate);

            Debug.Log("[Localization] 已重建 " + JsonPath + "，词条 " + written + " 条。");
            EditorUtility.DisplayDialog("词典已重建",
                "词条：" + written + " 条\n语言：" + string.Join(" / ", langColumns.Select(c => c.code)) +
                "\n\n输出：" + JsonPath, "好");
        }

        [MenuItem("Tools/PIONEER-60/本地化/2 校验词典", false, 11)]
        public static void Validate()
        {
            var problems = new List<string>();
            var infos = new List<string>();

            string[] header;
            var rows = ReadCsv(CsvPath, out header);
            if (rows == null)
            {
                EditorUtility.DisplayDialog("找不到 CSV", "缺少文件：\n" + CsvPath, "好");
                return;
            }

            var langIdx = new List<(string code, int index)>();
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i].Trim();
                if (h != "key" && h != "category" && h != "speaker" && h != "note") langIdx.Add((h, i));
            }

            var keys = new Dictionary<string, int>();
            var speakers = new HashSet<string>();
            var placeholdersByKey = new Dictionary<string, string>();
            int emptyCells = 0;

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                string key = Cell(row, 0);
                if (string.IsNullOrEmpty(key) || key.StartsWith("#")) continue;

                if (keys.ContainsKey(key)) problems.Add("重复 key：" + key + "（第 " + (r + 2) + " 行，首次在 " + keys[key] + " 行）");
                else keys[key] = r + 2;

                if (!Regex.IsMatch(key, @"^[a-z0-9]+(\.[a-z0-9_]+)+$"))
                    problems.Add("key 命名不合规（应全小写、点分层）：" + key);

                string cat = Cell(row, 1);
                if (Array.IndexOf(KnownCategories, cat) < 0)
                    problems.Add("未知 category「" + cat + "」：" + key);

                string spk = Cell(row, 2);
                if (!string.IsNullOrEmpty(spk)) speakers.Add(spk);

                foreach (var lc in langIdx)
                {
                    string text = Cell(row, lc.index);
                    if (string.IsNullOrEmpty(text))
                    {
                        emptyCells++;
                        problems.Add("空翻译 [" + lc.code + "]：" + key);
                    }
                }

                // 占位符一致性
                string refSet = null;
                string refLang = null;
                foreach (var lc in langIdx)
                {
                    string set = string.Join(",", ExtractVars(Cell(row, lc.index)).OrderBy(x => x));
                    if (refSet == null) { refSet = set; refLang = lc.code; }
                    else if (set != refSet)
                        problems.Add("占位符不一致：" + key + "（" + refLang + "={" + refSet + "} / " + lc.code + "={" + set + "}）");
                }
                if (!string.IsNullOrEmpty(refSet) && refSet.Length > 0) placeholdersByKey[key] = refSet;
            }

            foreach (var s in speakers)
                if (!keys.ContainsKey("speaker." + s))
                    problems.Add("说话人没有标签词条：speaker." + s + "（被以下词条引用）");

            infos.Add("词条 " + keys.Count + " 条；语言 " + langIdx.Count + " 种；说话人 " + speakers.Count + " 个");
            infos.Add("含占位符的词条 " + placeholdersByKey.Count + " 条");
            infos.Add("空翻译 " + emptyCells + " 处");

            var sb = new StringBuilder();
            sb.AppendLine("【词典校验】");
            foreach (var i in infos) sb.AppendLine("· " + i);
            sb.AppendLine();
            if (problems.Count == 0)
            {
                sb.AppendLine("没有发现问题。");
            }
            else
            {
                sb.AppendLine("问题 " + problems.Count + " 处：");
                for (int i = 0; i < Math.Min(problems.Count, 80); i++) sb.AppendLine((i + 1) + ". " + problems[i]);
                if (problems.Count > 80) sb.AppendLine("……其余 " + (problems.Count - 80) + " 处省略，见 Console。");
            }

            string report = sb.ToString();
            if (problems.Count == 0) Debug.Log(report); else Debug.LogWarning(report);
            EditorUtility.DisplayDialog("词典校验", report.Length > 1200 ? report.Substring(0, 1200) + "\n……" : report, "好");
        }

        [MenuItem("Tools/PIONEER-60/本地化/3 从 JSON 反向导出 CSV", false, 12)]
        public static void ExportCsvFromJson()
        {
            if (!File.Exists(JsonPath)) { EditorUtility.DisplayDialog("找不到 JSON", JsonPath, "好"); return; }
            var file = JsonUtility.FromJson<LocFile>(File.ReadAllText(JsonPath));
            if (file == null || file.entries == null) { EditorUtility.DisplayDialog("解析失败", "JSON 结构不符合 LocFile。", "好"); return; }

            var codes = file.languages.Select(l => l.code).ToList();
            var sb = new StringBuilder();
            sb.Append("key,category,speaker");
            foreach (var c in codes) sb.Append(',').Append(Esc(c));
            sb.Append(",note\n");

            foreach (var e in file.entries)
            {
                sb.Append(Esc(e.key)).Append(',').Append(Esc(e.category)).Append(',').Append(Esc(e.speaker));
                foreach (var c in codes)
                {
                    var t = e.texts == null ? null : e.texts.FirstOrDefault(x => x.lang == c);
                    sb.Append(',').Append(Esc(t == null ? string.Empty : t.text));
                }
                sb.Append(',').Append(Esc(e.note)).Append('\n');
            }

            File.WriteAllText(CsvPath, sb.ToString(), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(CsvPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[Localization] 已导出 " + CsvPath);
            EditorUtility.DisplayDialog("已导出", "从 JSON 反向导出 " + file.entries.Count + " 条到\n" + CsvPath, "好");
        }

        [MenuItem("Tools/PIONEER-60/本地化/4 列出未被代码引用的 key", false, 13)]
        public static void FindUnusedKeys()
        {
            if (!File.Exists(JsonPath)) { EditorUtility.DisplayDialog("找不到 JSON", JsonPath, "好"); return; }
            var file = JsonUtility.FromJson<LocFile>(File.ReadAllText(JsonPath));
            if (file == null) return;

            var csFiles = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories);
            var blob = new StringBuilder();
            foreach (var f in csFiles) blob.Append(File.ReadAllText(f)).Append('\n');
            string all = blob.ToString();

            var unused = file.entries.Where(e => all.IndexOf("\"" + e.key + "\"", StringComparison.Ordinal) < 0
                                              && all.IndexOf(e.key, StringComparison.Ordinal) < 0)
                                     .Select(e => e.key).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("词典词条：" + file.entries.Count + "，代码里没找到引用的：" + unused.Count);
            sb.AppendLine("（对话按行号从表里取词条的工程，这里会全量列出，属正常）");
            sb.AppendLine();
            foreach (var k in unused.Take(120)) sb.AppendLine("· " + k);
            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("未引用 key",
                "共 " + unused.Count + " 条未被代码直接引用，明细见 Console。\n\n如果你们是「对话表存 key」的做法，可以忽略这份清单。", "好");
        }

        // ══════════════════════════ CSV ══════════════════════════
        static List<string[]> ReadCsv(string path, out string[] header)
        {
            header = null;
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
            var rows = ParseCsv(text);
            if (rows.Count == 0) return null;
            header = rows[0];
            rows.RemoveAt(0);
            return rows;
        }

        static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Length = 0; break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Length = 0;
                        rows.Add(row.ToArray()); row.Clear();
                        break;
                    default: field.Append(c); break;
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }

        static string Cell(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length) return string.Empty;
            return (row[index] ?? string.Empty).Trim();
        }

        static string Esc(string s)
        {
            if (s == null) s = string.Empty;
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static string JStr(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static IEnumerable<string> ExtractVars(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (Match m in Regex.Matches(s, @"\{([A-Za-z0-9_]+)\}"))
                yield return m.Groups[1].Value;
        }
    }
}
