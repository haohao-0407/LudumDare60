// PIONEER-60 本地化系统 · 语言切换入口
// 设置菜单的「语言」行、下拉框、按钮都挂这个，不需要写代码。
using System;
using UnityEngine;
using UnityEngine.Events;

namespace Pioneer60.Localization
{
    [AddComponentMenu("PIONEER-60/Localization/Language Switcher")]
    public class LanguageSwitcher : MonoBehaviour
    {
        [Tooltip("留空 = 用 PlayerPrefs 里上次选的；填 en / zh-Hans 可强制初始语言")]
        [SerializeField] string forceInitialLanguage = "";

        [Tooltip("激活时是否强制应用一次初始语言")]
        [SerializeField] bool applyOnEnable = false;

        [SerializeField] UnityEvent<int> onLanguageIndexChanged = new UnityEvent<int>();
        [SerializeField] UnityEvent<string> onLanguageCodeChanged = new UnityEvent<string>();

        public int CurrentIndex
        {
            get
            {
                var m = LocalizationManager.Instance;
                return m == null ? -1 : m.IndexOfLanguage(m.CurrentLanguage);
            }
        }

        public int LanguageCount
        {
            get
            {
                var m = LocalizationManager.Instance;
                return (m == null || m.Database.Languages == null) ? 0 : m.Database.Languages.Count;
            }
        }

        /// <summary>给下拉框填选项用：按词典顺序返回显示名。</summary>
        public string[] GetDisplayNames()
        {
            var m = LocalizationManager.Instance;
            if (m == null) return new string[0];
            var list = m.Database.Languages;
            var names = new string[list.Count];
            for (int i = 0; i < list.Count; i++) names[i] = list[i].displayName;
            return names;
        }

        void OnEnable()
        {
            Loc.LanguageChanged += HandleLanguageChanged;
            if (applyOnEnable && !string.IsNullOrEmpty(forceInitialLanguage))
                Loc.SetLanguage(forceInitialLanguage);
            PushCurrent();
        }

        void OnDisable()
        {
            Loc.LanguageChanged -= HandleLanguageChanged;
        }

        void HandleLanguageChanged(string code)
        {
            PushCurrent();
        }

        void PushCurrent()
        {
            int index = CurrentIndex;
            if (onLanguageIndexChanged != null) onLanguageIndexChanged.Invoke(index);
            if (onLanguageCodeChanged != null) onLanguageCodeChanged.Invoke(Loc.Language);
        }

        // 挂到按钮 / 下拉框的 OnValueChanged
        public void Next()
        {
            int count = LanguageCount;
            if (count <= 0) return;
            LocalizationManager.Instance.SetLanguageByIndex((CurrentIndex + 1) % count);
        }

        public void Previous()
        {
            int count = LanguageCount;
            if (count <= 0) return;
            LocalizationManager.Instance.SetLanguageByIndex((CurrentIndex - 1 + count) % count);
        }

        public void SetByIndex(int index)
        {
            if (LocalizationManager.Instance != null)
                LocalizationManager.Instance.SetLanguageByIndex(index);
        }

        public void SetByCode(string code)
        {
            Loc.SetLanguage(code);
        }

        /// <summary>下拉框直接连：Dropdown.onValueChanged → SetByIndex(int)。</summary>
        public void SetByDropdownValue(int value)
        {
            SetByIndex(value);
        }
    }
}
