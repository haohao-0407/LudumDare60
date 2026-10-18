using System;
using System.Globalization;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Earth-standard civil dates for the compressed mission, not Martian sols.</summary>
    public sealed class MissionSchedule
    {
        public const int DefaultDays = 20;
        public const string DefaultStart = "1981-08-25";
        public const string DefaultNextStart = "1983-11-09";
        public const string DefaultNextEnd = "1983-11-19";
        public static readonly DateTime ArrivalDate = new DateTime(1980, 9, 8);
        public static readonly DateTime ReturnWindowOpens = new DateTime(1981, 9, 3);
        public static readonly DateTime ReturnWindowCloses = new DateTime(1981, 9, 13);
        public DateTime StartDate { get; }
        public int DayCount { get; }
        public DateTime LastDate => StartDate.AddDays(DayCount - 1);
        public DateTime SowingDate => StartDate.AddDays(DayCount);
        public DateTime NextOpportunityStart { get; }
        public DateTime NextOpportunityEnd { get; }

        public MissionSchedule(string start = DefaultStart, int days = DefaultDays,
            string nextStart = DefaultNextStart, string nextEnd = DefaultNextEnd)
        {
            if (days < 1 || days > 99) throw new ArgumentOutOfRangeException(nameof(days));
            StartDate = Parse(start, DefaultStart); DayCount = days;
            NextOpportunityStart = Parse(nextStart, DefaultNextStart);
            NextOpportunityEnd = Parse(nextEnd, DefaultNextEnd);
            if (NextOpportunityEnd < NextOpportunityStart) NextOpportunityEnd = NextOpportunityStart;
        }

        public DateTime DateAt(int zeroBasedDay) => StartDate.AddDays(zeroBasedDay);
        public static string Label(DateTime date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        public static string CellLabel(DateTime date) => date.ToString("MM/dd", CultureInfo.InvariantCulture);
        public string NextOpportunityText => Label(NextOpportunityStart) + " - " + Label(NextOpportunityEnd);

        public string Resolve(string text, double durationSeconds = 1200) => text
            .Replace("{ARRIVAL_DATE}", Label(ArrivalDate))
            .Replace("{START_DATE}", Label(StartDate))
            .Replace("{LAST_DATE}", Label(LastDate))
            .Replace("{SOWING_DATE}", Label(SowingDate))
            .Replace("{DAY_COUNT}", DayCount.ToString(CultureInfo.InvariantCulture))
            .Replace("{ACTIVE_MINUTES}", (durationSeconds / 60).ToString("0.#", CultureInfo.InvariantCulture))
            .Replace("{NEXT_OPPORTUNITY}", NextOpportunityText);

        private static DateTime Parse(string text, string fallback) => DateTime.TryParseExact(text, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed :
            DateTime.ParseExact(fallback, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
