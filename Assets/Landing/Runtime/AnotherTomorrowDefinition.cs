using System;
using UnityEngine;

namespace LudumDare60.Landing
{
    [Serializable]
    public sealed class TheatreEndingCard
    {
        public string EnglishCue, ChineseCue;
        public string EnglishTitle, ChineseTitle;
        [TextArea(2, 5)] public string EnglishBody, ChineseBody;
        public string EnglishFootnote, ChineseFootnote;
        [Min(1)] public float HoldSeconds = 7f;
    }

    [CreateAssetMenu(menuName = "PIONEER-60/Another Tomorrow theatre ending")]
    public sealed class AnotherTomorrowDefinition : ScriptableObject
    {
        [Min(.1f)] public float BlackoutSeconds = 1.8f;
        [Min(.1f)] public float TextFadeSeconds = 1.2f;
        [Min(0)] public float BetweenCardsSeconds = .6f;
        public TheatreEndingCard[] Cards =
        {
            new TheatreEndingCard
            {
                EnglishCue = "EPILOGUE   /   I",
                ChineseCue = "尾声   /   第一幕",
                EnglishTitle = "The departure that never came",
                ChineseTitle = "未能启程",
                EnglishBody = "The appointed hour passes.\nNo engine breaks the silence.",
                ChineseBody = "约定的时刻过去了。\n没有引擎划破寂静。",
                EnglishFootnote = "13 SEPTEMBER 1981  ·  THE RETURN WINDOW CLOSES",
                ChineseFootnote = "1981 年 9 月 13 日   ·   返航窗口关闭"
            },
            new TheatreEndingCard
            {
                EnglishCue = "EPILOGUE   /   II",
                ChineseCue = "尾声   /   第二幕",
                EnglishTitle = "The long wait",
                ChineseTitle = "漫长的等候",
                EnglishBody = "The next return plan lies in November, 1983.\nThe food will not last that long.\nNo rescue has been promised.",
                ChineseBody = "下一次返航计划，在一九八三年十一月。\n口粮撑不到那时。\n也没有人能保证救援到来。",
                EnglishFootnote = "MORE THAN TWO YEARS FROM HOME",
                ChineseFootnote = "距离归途   ·   还有两年多",
                HoldSeconds = 9f
            },
            new TheatreEndingCard
            {
                EnglishCue = "EPILOGUE   /   III",
                ChineseCue = "尾声   /   第三幕",
                EnglishTitle = "The first seed",
                ChineseTitle = "播种之日",
                EnglishBody = "Outside, the colony is still empty.\nTomorrow: check the greenhouse water.\nIf it runs, prepare the first seed.",
                ChineseBody = "舱外，殖民地依然空无一人。\n明天，先去检查温室的供水。\n如果还能用，就准备播下第一颗种子。",
                EnglishFootnote = "14 SEPTEMBER 1981  ·  SOW",
                ChineseFootnote = "1981 年 9 月 14 日   ·   播种",
                HoldSeconds = 9f
            },
            new TheatreEndingCard
            {
                EnglishCue = "THE FINAL CURTAIN",
                ChineseCue = "终   幕",
                EnglishTitle = "Another Tomorrow",
                ChineseTitle = "另一个明天",
                EnglishBody = "The ship stays.\nThe work of living begins.",
                ChineseBody = "飞船留在原地。\n活下去，成了新的任务。",
                EnglishFootnote = "PIONEER–60   /   THE WINDOW CLOSED",
                ChineseFootnote = "先驱者六十号   /   错过归途"
            }
        };
    }
}
