using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pioneer60.Localization
{
    /// <summary>
    /// 界面文本本地化。
    ///
    /// 为什么需要它：界面文案烤在预制体和场景里（由几套 Editor 构建脚本生成），没有 key。
    /// 逐个改成 Loc.Get 既动几十个文件、又会在下次重跑构建脚本时被覆盖。
    /// 所以按「原文」反查词典：Loc.Say 双向可用，英文原文能翻成中文，中文也能查回英文。
    ///
    /// 触发时机只有三个，不做定时轮询：
    ///   1. 场景加载完成（含未激活的界面，一次扫干净）
    ///   2. 手动切换语言（ESC → SETTINGS → LANGUAGE）
    ///   3. 面板自己 Refresh 完调 <see cref="Refresh(GameObject)"/>，或代码调 <see cref="Refresh()"/>
    ///
    /// 为什么不轮询：轮询会和「每帧重设 text」的代码抢——代码写英文、扫描写中文，屏幕上就是来回闪。
    /// 要动态变化的文本，一律在源头走 Loc.Say / Loc.T。
    /// </summary>
    [AddComponentMenu("PIONEER-60/Localization/Loc Auto Translate")]
    [DefaultExecutionOrder(600)]
    public sealed class LocAutoTranslate : MonoBehaviour
    {
        private static LocAutoTranslate runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Application.isPlaying || runner != null) return;
            var go = new GameObject("[Loc Auto Translate]");
            DontDestroyOnLoad(go);
            runner = go.AddComponent<LocAutoTranslate>();
        }

        /// <summary>整场景刷新一次。只给「运行时新建了界面」这类场景用，别每帧调。</summary>
        public static void Refresh()
        {
            if (runner != null) runner.ScanAll();
        }

        /// <summary>
        /// 只刷一棵子树。给「事件驱动重写文本」的面板在自己 Refresh 末尾调用：
        /// 面板刚写完英文，这里立刻按原文换成当前语言，不需要定时器。
        /// </summary>
        public static void Refresh(GameObject root)
        {
            if (runner == null || root == null) return;
            runner.ScanSubtree(root.transform);
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnEnable()
        {
            Loc.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
        }

        private void Start()
        {
            StartCoroutine(ScanNextFrame());
        }

        private System.Collections.IEnumerator ScanNextFrame()
        {
            // 等一帧：让各界面在自己的 Start / OnEnable 里把初始文本写完
            yield return null;
            ScanAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(ScanNextFrame());
        }

        private void OnLanguageChanged(string code)
        {
            ScanAll();
        }

        private void ScanAll()
        {
            if (LocalizationManager.Instance == null) return;
            var texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            for (int i = 0; i < texts.Length; i++) TranslateOne(texts[i]);
        }

        private void ScanSubtree(Transform root)
        {
            if (LocalizationManager.Instance == null || root == null) return;
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++) TranslateOne(texts[i]);
        }

        /// <summary>双向：原文→当前语言，译文→当前语言（切回英文也走这里）。</summary>
        private void TranslateOne(TMP_Text text)
        {
            if (text == null) return;
            string current = text.text;
            if (string.IsNullOrEmpty(current)) return;

            string translated = Loc.Say(current);
            if (string.IsNullOrEmpty(translated) || translated == current) return;
            text.text = translated;
        }
    }
}
