using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Pioneer60.Localization
{
    /// <summary>
    /// 中文字形兜底。
    /// 背景：Glass_TTY_VT220 SDF 是静态图集，只含 ASCII。切到中文时 TMP 找不到字形，整行会空白。
    /// 两条路，任一即可：
    ///   1) 把中文字体做成 TMP Font Asset，放到 Assets/Resources/Localization/CJK Fallback SDF.asset
    ///   2) 运行时给 <see cref="Override"/> 赋值（自己的字体加载流程用）
    /// 也完全可以不启用本类：直接把 Glass_TTY 的 fallback 列表在 Inspector 里挂上中文字体，
    /// 那样全工程的 TMP 文本都吃得到，这里就不用管了。
    /// </summary>
    public static class LocFontFallback
    {
        /// <summary>手工指定的中文字体资产，优先于 Resources 路径。</summary>
        public static TMP_FontAsset Override;

        private const string BakedResourcePath = "Localization/CJK Fallback SDF";

        private static TMP_FontAsset cjk;
        private static bool attempted;
        private static bool warned;

        public static TMP_FontAsset CjkFont { get { return cjk; } }

        /// <summary>把一个 TMP 文本所用的字体资产装上中文兜底。重复调用无副作用。</summary>
        public static bool EnsureFor(TMP_Text sample)
        {
            return sample != null && EnsureFor(sample.font);
        }

        public static bool EnsureFor(TMP_FontAsset baseFont)
        {
            if (baseFont == null) return false;

            // 主字体（含它自己挂的 fallback）已经能排中文，就什么都不用做
            if (baseFont.HasCharacter('\u6682', true)) return true;

            if (cjk == null && !attempted)
            {
                attempted = true;
                cjk = Override != null ? Override : Resources.Load<TMP_FontAsset>(BakedResourcePath);
            }

            if (cjk == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[Localization] 换到中文前先准备一个中文字体资产：\n" +
                                     "  · 放到 Assets/Resources/" + BakedResourcePath + ".asset，或\n" +
                                     "  · 给 LocFontFallback.Override 赋值，或\n" +
                                     "  · 直接在 Glass_TTY_VT220 SDF 的 Fallback Font Assets 里挂上中文字体。\n" +
                                     "否则中文会显示为空白。");
                }
                return false;
            }
            return Attach(baseFont, cjk);
        }

        private static bool Attach(TMP_FontAsset baseFont, TMP_FontAsset fallback)
        {
            if (baseFont == null || fallback == null || baseFont == fallback) return false;
            if (baseFont.fallbackFontAssetTable == null)
                baseFont.fallbackFontAssetTable = new List<TMP_FontAsset>(1);
            if (baseFont.fallbackFontAssetTable.Contains(fallback)) return true;
            baseFont.fallbackFontAssetTable.Add(fallback);
            return true;
        }
    }
}
