// PIONEER-60 本地化系统 · 通用文本绑定
// 不依赖任何 UI 组件：拿一个 key，算出当前语言的字符串，通过 UnityEvent 交给任意渲染层
// （自绘终端文本、TextMeshPro、UI Toolkit、对话机……）。切换语言自动重算。
using UnityEngine;
using UnityEngine.Events;

namespace Pioneer60.Localization
{
    [AddComponentMenu("PIONEER-60/Localization/Localized Key Binder")]
    public class LocalizedKeyBinder : MonoBehaviour
    {
        [Tooltip("词典 key，例如 dlg.ai.damage_report")]
        [SerializeField] string key;

        [Tooltip("命名占位符，与 argValues 一一对应。例如 callsign / date")]
        [SerializeField] string[] argNames = new string[0];
        [SerializeField] string[] argValues = new string[0];

        [Tooltip("开启时会在 OnEnable 立刻算一次，避免首帧空白")]
        [SerializeField] bool refreshOnEnable = true;

        [Tooltip("算好的文本从这里输出，Inspector 里直接拖目标方法")]
        [SerializeField] UnityEvent<string> onTextChanged = new UnityEvent<string>();

        public string Key { get { return key; } }
        public string CurrentText { get; private set; }

        void OnEnable()
        {
            Loc.LanguageChanged += HandleLanguageChanged;
            if (refreshOnEnable) Refresh();
        }

        void OnDisable()
        {
            Loc.LanguageChanged -= HandleLanguageChanged;
        }

        void HandleLanguageChanged(string languageCode)
        {
            Refresh();
        }

        /// <summary>换 key 并立刻重算。对话系统按行推进时最常用。</summary>
        public void SetKey(string newKey)
        {
            key = newKey;
            Refresh();
        }

        /// <summary>传 "callsign", "Pioneer-60" 这样的成对参数。</summary>
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

        /// <summary>一次性设置 key + 参数，避免两次刷新。</summary>
        public void Show(string newKey, params string[] nameValuePairs)
        {
            key = newKey;
            SetArgs(nameValuePairs);
        }

        public void Refresh()
        {
            int count = Mathf.Min(argNames == null ? 0 : argNames.Length, argValues == null ? 0 : argValues.Length);
            var vars = new (string, object)[count];
            for (int i = 0; i < count; i++) vars[i] = (argNames[i], argValues[i]);

            CurrentText = Loc.Format(key, vars);
            if (onTextChanged != null) onTextChanged.Invoke(CurrentText);
        }

#if UNITY_EDITOR
        [ContextMenu("用当前语言预览")]
        void PreviewInEditor()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[Localization] 编辑期不解析（需要运行时才有词典实例）。进入 Play 模式再预览。");
                return;
            }
            Refresh();
            Debug.Log(key + " → " + CurrentText);
        }
#endif
    }
}
