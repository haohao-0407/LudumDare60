// PIONEER-60 本地化系统 · 静态门面
// 业务代码只写 Loc.Get("item.relay.name") / Loc.Format("sys.current_date", ("date", d))，不碰管理器。
using System;
using System.Text;

namespace Pioneer60.Localization
{
    public static class Loc
    {
        /// <summary>语言变化回调，参数是新语言 code。</summary>
        public static event Action<string> LanguageChanged
        {
            add { LocalizationManager.LanguageChanged += value; }
            remove { LocalizationManager.LanguageChanged -= value; }
        }

        public static string Language
        {
            get
            {
                var m = LocalizationManager.Instance;
                return m == null ? string.Empty : m.CurrentLanguage;
            }
        }

        /// <summary>当前是不是词典里的默认语言（默认语言下不需要做任何替换）。</summary>
        public static bool IsDefaultLanguage
        {
            get
            {
                var m = LocalizationManager.Instance;
                if (m == null) return true;
                return string.Equals(m.CurrentLanguage, m.Database.File.defaultLanguage, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string Get(string key)
        {
            var m = LocalizationManager.Instance;
            if (m == null) return key ?? string.Empty;
            return m.GetText(key);
        }

        /// <summary>取不到就返回 fallback，不显示 ⟦key⟧。适合按钮、可缺省的装饰文本。</summary>
        public static string GetOr(string key, string fallback)
        {
            var m = LocalizationManager.Instance;
            if (m == null) return fallback;
            string value;
            return m.TryGetText(key, out value) ? value : fallback;
        }

        /// <summary>缺键时返回 false，不打印警告。适合批量校验。</summary>
        public static bool TryGet(string key, out string value)
        {
            value = null;
            var m = LocalizationManager.Instance;
            return m != null && m.TryGetText(key, out value);
        }

        /// <summary>
        /// 命名占位符替换：Loc.Format("sys.current_date", ("date", "25 AUG 1981"))
        /// 模板里写 {date}；变量值不翻译，只按当前语言排版。
        /// </summary>
        public static string Format(string key, params (string name, object value)[] vars)
        {
            return ApplyVars(Get(key), vars);
        }

        /// <summary>
        /// 原文直译：Loc.Say("START ASSEMBLY")。烤在预制体/数据资产里的文案用它，
        /// 查不到就原样返回（英文兜底，不会破坏排版）。
        /// </summary>
        public static string Say(string englishSource)
        {
            var m = LocalizationManager.Instance;
            return m == null ? englishSource : m.TranslateSource(englishSource);
        }

        /// <summary>
        /// 模板直译 + 填参：Loc.T("CATCH {0} - {1}", a, b)。
        /// 给每帧刷新的数值文本用（TARGET / PROGRESS / CATCH 这类）。
        /// </summary>
        public static string T(string englishTemplate, params object[] args)
        {
            string template = Say(englishTemplate);
            if (args == null || args.Length == 0) return template;
            try
            {
                var m = LocalizationManager.Instance;
                return string.Format(m != null ? m.Culture : System.Globalization.CultureInfo.InvariantCulture,
                    template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>索引占位符：模板里写 {0} {1}，走 string.Format。</summary>
        public static string FormatIndexed(string key, params object[] args)
        {
            string template = Get(key);
            if (args == null || args.Length == 0) return template;
            try
            {
                return string.Format(LocalizationManager.Instance.Culture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        public static string Speaker(string speakerId)
        {
            var m = LocalizationManager.Instance;
            return m == null ? string.Empty : m.GetSpeaker(speakerId);
        }

        public static void SetLanguage(string code)
        {
            var m = LocalizationManager.Instance;
            if (m != null) m.SetLanguage(code);
        }

        public static string ApplyVars(string template, params (string name, object value)[] vars)
        {
            if (string.IsNullOrEmpty(template) || vars == null || vars.Length == 0) return template;
            var sb = new StringBuilder(template);
            for (int i = 0; i < vars.Length; i++)
            {
                string token = "{" + vars[i].name + "}";
                string value = vars[i].value == null ? string.Empty : vars[i].value.ToString();
                sb.Replace(token, value);
            }
            return sb.ToString();
        }
    }
}
