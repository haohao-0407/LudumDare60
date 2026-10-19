using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LudumDare60.UI
{
    /// <summary>
    /// 设置页的「选项行」。左右方向键（或鼠标点击 / 回车）循环切换界面语言。
    /// 与音量滑条分别控制语言和音量。
    /// 上下方向键仍然交给 Selectable 做默认导航，所以不会破坏整页的焦点移动。
    /// </summary>
    [AddComponentMenu("PIONEER-60/UI/Language Option")]
    public sealed class EscLanguageOption : Selectable, ISubmitHandler
    {
        [SerializeField, Tooltip("行首的固定标签（LANGUAGE），由 EscMenuController 统一本地化，可留空")]
        private TMP_Text rowLabel;

        [SerializeField, Tooltip("显示当前语言的文本")]
        private TMP_Text valueLabel;

        [SerializeField, Tooltip("{0} = 当前语言的显示名")]
        private string valueFormat = "< {0} >";

        [SerializeField, Tooltip("关闭后只能靠鼠标点击 / 回车切换，左右键仍按导航走")]
        private bool enableKeyboardArrows = true;

        [SerializeField] private UnityEvent<string> languageChanged = new UnityEvent<string>();

        public TMP_Text ValueLabel { get { return valueLabel; } }
        public TMP_Text RowLabel { get { return rowLabel; } }

        /// <summary>给编辑器构建脚本用，避免反射写私有序列化字段。</summary>
        public void Configure(TMP_Text row, TMP_Text value)
        {
            rowLabel = row;
            valueLabel = value;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            // 编辑模式下不解析：此时没有词典实例，硬刷新会把预制体里的占位文本清空
            if (!Application.isPlaying) return;
            Pioneer60.Localization.Loc.LanguageChanged += HandleLanguageChanged;
            RefreshValue();
        }

        protected override void OnDisable()
        {
            if (Application.isPlaying)
                Pioneer60.Localization.Loc.LanguageChanged -= HandleLanguageChanged;
            base.OnDisable();
        }

        private void HandleLanguageChanged(string code)
        {
            RefreshValue();
        }

        /// <summary>按词典顺序刷新显示名，并确保中文字形可用。</summary>
        public void RefreshValue()
        {
            var mgr = Pioneer60.Localization.LocalizationManager.Instance;
            if (valueLabel == null) return;
            if (mgr == null || mgr.Database.Languages == null || mgr.Database.Languages.Count == 0)
            {
                valueLabel.text = string.Empty;
                return;
            }

            int index = mgr.IndexOfLanguage(mgr.CurrentLanguage);
            if (index < 0) index = 0;
            string name = mgr.Database.Languages[index].displayName;

            Pioneer60.Localization.LocFontFallback.EnsureFor(valueLabel);
            valueLabel.text = string.Format(valueFormat, name);
        }

        public void Next() { Cycle(1); }
        public void Previous() { Cycle(-1); }

        private void Cycle(int delta)
        {
            var mgr = Pioneer60.Localization.LocalizationManager.Instance;
            if (mgr == null) return;
            int count = mgr.Database.Languages == null ? 0 : mgr.Database.Languages.Count;
            if (count < 2) return;

            int index = mgr.IndexOfLanguage(mgr.CurrentLanguage);
            if (index < 0) index = 0;
            int next = ((index + delta) % count + count) % count;
            mgr.SetLanguageByIndex(next);
            if (languageChanged != null) languageChanged.Invoke(mgr.CurrentLanguage);
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (enableKeyboardArrows && IsActive() && IsInteractable())
            {
                if (eventData.moveDir == MoveDirection.Left) { Cycle(-1); eventData.Use(); return; }
                if (eventData.moveDir == MoveDirection.Right) { Cycle(1); eventData.Use(); return; }
            }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsActive() || !IsInteractable()) return;
            Cycle(1);
            if (eventData != null) eventData.Use();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            if (!IsActive() || !IsInteractable()) return;
            var es = EventSystem.current;
            if (es != null) es.SetSelectedGameObject(gameObject);
            Cycle(1);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (Application.isPlaying) RefreshValue();
        }
#endif
    }
}
