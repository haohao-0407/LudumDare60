// PIONEER-60 本地化系统 · 管理器
// 单一职责：装词典、切语言、给文本。所有界面只跟 Loc 静态门面打交道。
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Pioneer60.Localization
{
    [AddComponentMenu("PIONEER-60/Localization/Localization Manager")]
    [DefaultExecutionOrder(-900)]
    public class LocalizationManager : MonoBehaviour
    {
        public const string PrefsKey = "pioneer60.language";

        [Tooltip("留空则从 Resources 下按 resourcePath 加载")]
        [SerializeField] TextAsset dictionaryAsset;
        [SerializeField] string resourcePath = "Localization/pioneer60_dict";

        [Tooltip("缺键时是否在界面上显示 ⟦key⟧（开发期建议开，打包前关）")]
        [SerializeField] bool showMissingKeyMarkers = true;
        [Tooltip("缺键时是否打一次警告")]
        [SerializeField] bool logMissingKeys = true;

        static LocalizationManager _instance;
        static bool _shuttingDown;

        LocalizationDatabase _db;
        string _language;
        CultureInfo _culture;
        readonly HashSet<string> _warnedKeys = new HashSet<string>();

        public static event Action<string> LanguageChanged;

        /// <summary>
        /// 关闭「进入播放模式时重载域」时静态字段不会自动复位，
        /// 上一轮的 _shuttingDown 会把单例永久锁死。每次进播放前手动清一遍。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _shuttingDown = false;
            // 静态事件也不会被域重载清掉：上一轮播放的残留订阅（对象已销毁）会在 Invoke 时抛异常，
            // 把后面的订阅者整条打断，表现就是「切语言只有一部分界面跟着变」。
            LanguageChanged = null;
        }

        /// <summary>
        /// 本工程关闭了域重载（Enter Play Mode Options），任何 Awake 里懒创建都会撞时序。
        /// 进场景前先把词典实例建好，其余脚本在 Awake 里就能直接取文本。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootBeforeSceneLoad()
        {
            if (!Application.isPlaying) return;
            var unused = Instance;
        }

        public LocalizationDatabase Database { get { EnsureLoaded(); return _db; } }
        public string CurrentLanguage { get { EnsureLoaded(); return _language; } }
        public CultureInfo Culture { get { EnsureLoaded(); return _culture; } }
        public bool ShowMissingKeyMarkers { get { return showMissingKeyMarkers; } }

        public static LocalizationManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                if (_shuttingDown) return null;
                _instance = FindAnyObjectByType<LocalizationManager>();
                if (_instance != null) return _instance;
                if (!Application.isPlaying) return null; // 编辑期不凭空造对象
                var go = new GameObject("[Localization]");
                _instance = go.AddComponent<LocalizationManager>();
                return _instance;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 场景里重复放置时，保留已在用的那个
                if (_instance.dictionaryAsset == null && dictionaryAsset != null)
                {
                    _instance.dictionaryAsset = dictionaryAsset;
                    _instance.ShowMissingKeyMarkersOverride(showMissingKeyMarkers);
                    _instance.Reload();
                }
                Destroy(this);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureLoaded();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnApplicationQuit()
        {
            _shuttingDown = true;
        }

        void ShowMissingKeyMarkersOverride(bool value)
        {
            showMissingKeyMarkers = value;
        }

        /// <summary>改完 JSON 想在运行时热重载时调它。</summary>
        public void Reload()
        {
            _db = null;
            EnsureLoaded();
            LanguageChanged?.Invoke(_language);
        }

        void EnsureLoaded()
        {
            if (_db != null) return;

            TextAsset asset = dictionaryAsset;
            if (asset == null && !string.IsNullOrEmpty(resourcePath))
                asset = Resources.Load<TextAsset>(resourcePath);

            if (asset == null)
            {
                Debug.LogError("[Localization] 找不到词典。把 pioneer60_dict.json 放到 " +
                               "Assets/Resources/" + (resourcePath ?? "") + ".json，或在 Inspector 里直接指定 TextAsset。");
                _db = new LocalizationDatabase(new LocFile());
            }
            else
            {
                _db = LocalizationDatabase.FromJson(asset.text);
            }

            string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
            string initial = ResolveLanguageCode(saved);
            if (string.IsNullOrEmpty(initial)) initial = ResolveLanguageCode(_db.File.defaultLanguage);
            if (string.IsNullOrEmpty(initial) && _db.Languages != null && _db.Languages.Count > 0)
                initial = _db.Languages[0].code;
            if (string.IsNullOrEmpty(initial)) initial = "en";

            SetLanguageInternal(initial, false);
            if (_db.EntryCount == 0)
                Debug.LogWarning("[Localization] 词典已加载但没有任何词条，请检查 JSON。");
        }

        /// <summary>把任意写法归一化到词典里注册的 code：zh / zh-CN / cn → zh-Hans，en-US → en。</summary>
        public string ResolveLanguageCode(string code)
        {
            if (_db == null || _db.Languages == null || _db.Languages.Count == 0)
                return string.IsNullOrEmpty(code) ? _db?.File?.fallbackLanguage : code;

            if (string.IsNullOrEmpty(code)) return null;

            for (int i = 0; i < _db.Languages.Count; i++)
                if (string.Equals(_db.Languages[i].code, code, StringComparison.OrdinalIgnoreCase))
                    return _db.Languages[i].code;

            string head = code.Split('-', '_')[0].Trim().ToLowerInvariant();
            for (int i = 0; i < _db.Languages.Count; i++)
            {
                string c = _db.Languages[i].code;
                if (c.ToLowerInvariant() == head) return c;
            }
            switch (head)
            {
                case "zh":
                case "cn":
                case "chs":
                case "zhcn":
                    for (int i = 0; i < _db.Languages.Count; i++)
                        if (_db.Languages[i].code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                            return _db.Languages[i].code;
                    break;
            }
            return null;
        }

        public void SetLanguage(string code, bool save = true)
        {
            EnsureLoaded();
            string resolved = ResolveLanguageCode(code);
            if (string.IsNullOrEmpty(resolved))
            {
                Debug.LogWarning("[Localization] 词典里没有语言 " + code + "，忽略本次切换。");
                return;
            }
            SetLanguageInternal(resolved, save);
        }

        public void SetLanguageByIndex(int index, bool save = true)
        {
            EnsureLoaded();
            if (_db.Languages == null || index < 0 || index >= _db.Languages.Count) return;
            SetLanguageInternal(_db.Languages[index].code, save);
        }

        public int IndexOfLanguage(string code)
        {
            EnsureLoaded();
            string resolved = ResolveLanguageCode(code);
            for (int i = 0; i < _db.Languages.Count; i++)
                if (_db.Languages[i].code == resolved) return i;
            return -1;
        }

        void SetLanguageInternal(string code, bool save)
        {
            bool changed = !string.Equals(_language, code, StringComparison.Ordinal);
            _language = code;
            _culture = BuildCulture(code);
            if (save)
            {
                PlayerPrefs.SetString(PrefsKey, code);
                PlayerPrefs.Save();
            }
            if (changed) LanguageChanged?.Invoke(code);
        }

        static CultureInfo BuildCulture(string code)
        {
            try
            {
                switch (code)
                {
                    case "zh-Hans": return new CultureInfo("zh-CN");
                    case "en": return CultureInfo.InvariantCulture;
                    default: return new CultureInfo(code);
                }
            }
            catch
            {
                return CultureInfo.InvariantCulture;
            }
        }

        // ── 查询 ──────────────────────────────────────────────
        public string GetText(string key)
        {
            EnsureLoaded();
            string value;
            if (_db.TryGetText(key, _language, _db.File.fallbackLanguage, out value)) return value;
            WarnMissing(key);
            return showMissingKeyMarkers
                ? (_db.MissingKeyFormat ?? "\u27E6{key}\u27E7").Replace("{key}", key)
                : (key ?? string.Empty);
        }

        /// <summary>
        /// 英文原文 → 当前语言。烤死在预制体/场景/对话资产里的文案走这条路：
        /// 英文模式原样返回，其余语言按原文反查词典，查不到就保留原文（不会出现 ⟦key⟧）。
        /// </summary>
        public string TranslateSource(string source)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(source)) return source;

            // ① 已经是词典里的某条译文（任意语言）→ 按 key 直接换成当前语言。
            //    这样中文界面切回英文也能靠查表还原，不依赖「记住原文」。
            string key;
            if (_db.TryFindKeyByAnyText(source, out key))
            {
                string text;
                if (_db.TryGetText(key, _language, _db.File.fallbackLanguage, out text) && !string.IsNullOrEmpty(text))
                    return text;
            }

            // ② 原文（英文）→ 当前语言；英文模式下这步同时负责「中文整段 → 英文」的逐行反转，
            //    所以不能因为「当前就是英文」就提前返回原文。
            string value;
            return _db.TryTranslateSource(source, _language, _db.File.fallbackLanguage, out value) ? value : source;
        }

        public bool TryGetText(string key, out string value)
        {
            EnsureLoaded();
            return _db.TryGetText(key, _language, _db.File.fallbackLanguage, out value);
        }

        /// <summary>取说话人标签，例如 speaker.player；缺键时返回空字符串而不是报错。</summary>
        public string GetSpeaker(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId)) return string.Empty;
            EnsureLoaded();
            string value;
            return _db.TryGetText("speaker." + speakerId, _language, _db.File.fallbackLanguage, out value)
                ? value
                : string.Empty;
        }

        void WarnMissing(string key)
        {
            if (!logMissingKeys || string.IsNullOrEmpty(key) || !_warnedKeys.Add(key)) return;
            Debug.LogWarning("[Localization] 缺键：" + key + "（语言 " + _language + "）");
        }
    }
}
