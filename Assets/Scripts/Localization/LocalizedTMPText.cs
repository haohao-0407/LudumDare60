// PIONEER-60 本地化系统 · TextMeshPro 适配
// 用 TextMeshPro 的话挂这个；用自绘终端就用 LocalizedKeyBinder。
// 项目里没有 TMP 时，直接删掉本文件即可（其余脚本不依赖它）。
using TMPro;
using UnityEngine;

namespace Pioneer60.Localization
{
    [AddComponentMenu("PIONEER-60/Localization/Localized TMP Text")]
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedTMPText : MonoBehaviour
    {
        [Tooltip("词典 key")]
        [SerializeField] string key;
        [SerializeField] string[] argNames = new string[0];
        [SerializeField] string[] argValues = new string[0];
        [Tooltip("先显示 key 的原文再被覆盖？一般关掉")]
        [SerializeField] bool keepSceneTextAsPreview = true;

        TMP_Text _text;
        string _sceneOriginal;

        public string Key { get { return key; } }

        void Awake()
        {
            _text = GetComponent<TMP_Text>();
            if (keepSceneTextAsPreview) _sceneOriginal = _text.text;
        }

        void OnEnable()
        {
            Loc.LanguageChanged += HandleLanguageChanged;
            Refresh();
        }

        void OnDisable()
        {
            Loc.LanguageChanged -= HandleLanguageChanged;
        }

        void HandleLanguageChanged(string code)
        {
            Refresh();
        }

        public void SetKey(string newKey)
        {
            key = newKey;
            Refresh();
        }

        public void SetArgs(params string[] nameValuePairs)
        {
            int count = nameValuePairs == null ? 0 : nameValuePairs.Length / 2;
            argNames = new string[count];
            argValues = new string[count];
            for (int i = 0; i < count; i++)
            {
                argNames[i] = nameValuePairs[i * 2];
                argValues[i] = nameValuePairs[i * 2 + 1];
            }
            Refresh();
        }

        public void Refresh()
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_text == null) return;

            int count = Mathf.Min(argNames == null ? 0 : argNames.Length, argValues == null ? 0 : argValues.Length);
            var vars = new (string, object)[count];
            for (int i = 0; i < count; i++) vars[i] = (argNames[i], argValues[i]);

            if (string.IsNullOrEmpty(key))
            {
                if (keepSceneTextAsPreview) _text.text = _sceneOriginal;
                return;
            }

            string value;
            if (!Loc.TryGet(key, out value))
            {
                value = Loc.Get(key); // 走缺键标记，方便一眼看出漏翻
            }
            _text.text = Loc.ApplyVars(value, vars);
        }
    }
}
