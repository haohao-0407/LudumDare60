using System;
using System.Collections.Generic;
using LudumDare60.ReturnWindow;

internal static class MissionClockTests
{
    private static int passed, failed;
    private static int Main()
    {
        Run("ready state never consumes time and starts only once", ReadyAndStart);
        Run("20 minutes map to twenty 24-hour dated workdays", DefaultsAndDayMapping);
        Run("calendar rolls from August into September and identifies the final date", CalendarDates);
        Run("twenty dates cross once, and only expiry completes the final date", TwentyDateCrosses);
        Run("story date substitutions follow configuration without mutating historical arrival", ScheduleTemplates);
        Run("crossing several days emits every cross exactly once", CatchUpDays);
        Run("exact seventh boundary closes window once", Deadline);
        Run("reading pause drops wall time and resumes without a jump", PauseAndResume);
        Run("pause transition charges pre-reading time exactly once", PauseTransition);
        Run("repeated pause synchronization does not advance the deadline before input", RedundantPauseDoesNotTick);
        Run("launch just before deadline freezes departure day and time", JustInTimeLaunch);
        Run("launch at zero and late launch enter stay ending", AtAndAfterDeadline);
        Run("incomplete repair cannot launch and does not reset deadline", IncompleteRepair);
        Run("duplicate launch and start cannot change an ending", EndingIsLocked);
        Run("cancelled departure retains budget, calendar and published warnings", CancelDeparturePreservesDeadline);
        Run("cancelled departure preserves pause and cannot rewind timestamps", CancelDeparturePreservesPause);
        Run("departure retry still requires a strictly positive deadline", CancelDepartureRetryDeadline);
        Run("departure cancellation cannot restart ready, running or expired missions", CancelDepartureStateGuards);
        Run("all crossed warning thresholds publish once in descending order", Warnings);
        Run("warning config is copied, sorted and deduplicated", WarningConfiguration);
        Run("stale timestamps neither rewind nor double count", MonotonicTimestamp);
        Run("invalid durations, days, warnings and timestamps are rejected", InvalidInput);
        Run("expiry state is committed before observers run", ReentrantExpiry);
        Run("blocking scene load gap preserves time without resetting run", SceneLoadGap);
        Console.WriteLine("Passed " + passed + "; failed " + failed + " return-window tests.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(double actual, double expected) => Check(Math.Abs(actual - expected) < 0.000001, actual + " != " + expected);
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Expected ArgumentOutOfRangeException");
    }
    private static MissionClock Running(double duration = 1200, int days = 7, double timestamp = 0)
    {
        var clock = new MissionClock(duration, days);
        clock.Start(timestamp);
        return clock;
    }

    private static void ReadyAndStart()
    {
        var clock = new MissionClock();
        clock.AdvanceTo(5000);
        Check(clock.State == MissionState.Ready && !clock.TryLaunch(true, 5001), "ready must not launch");
        Equal(clock.RemainingSeconds, 1200);
        Check(clock.Start(6000) && !clock.Start(6001), "start must be one-shot");
        clock.AdvanceTo(6010);
        Equal(clock.ElapsedSeconds, 10);
    }
    private static void DefaultsAndDayMapping()
    {
        var clock = new MissionClock(); clock.Start(0);
        Equal(clock.RemainingMissionSeconds, 20 * 24 * 3600);
        Check(clock.MissionDays == 20 && clock.CurrentDay == 1, "wrong defaults");
        clock.AdvanceTo(600);
        Equal(clock.RemainingMissionSeconds, 10 * 24 * 3600);
        Check(clock.CompletedDays == 10 && clock.CurrentDay == 11, "wrong task day");
    }
    private static void CalendarDates()
    {
        var schedule = new MissionSchedule();
        Check(MissionSchedule.CellLabel(schedule.DateAt(0)) == "08/25", "wrong first date");
        Check(MissionSchedule.CellLabel(schedule.DateAt(6)) == "08/31", "August ended early");
        Check(MissionSchedule.CellLabel(schedule.DateAt(7)) == "09/01", "month rollover failed");
        Check(schedule.LastDate == new DateTime(1981, 9, 13) && schedule.SowingDate == new DateTime(1981, 9, 14), "deadline or following day off by one");
        Check(new MissionSchedule("1984-02-28", 3).LastDate == new DateTime(1984, 3, 1), "leap date rollover failed");
        Check(schedule.NextOpportunityStart == new DateTime(1983, 11, 9) && schedule.NextOpportunityEnd == new DateTime(1983, 11, 19), "archive dates changed");
    }
    private static void TwentyDateCrosses()
    {
        var clock = Running(days: 20); var days = new List<int>(); clock.DayCompleted += days.Add;
        clock.AdvanceTo(60); Check(days.Count == 1 && clock.CurrentDay == 2, "first completed date not crossed");
        clock.AdvanceTo(1199.9); Check(days.Count == 19 && clock.CurrentDay == 20, "deadline date crossed before it ended");
        clock.AdvanceTo(1200); clock.AdvanceTo(1400);
        Check(days.Count == 20 && days[19] == 20 && clock.State == MissionState.Stayed, "crosses skipped or repeated at expiry");
    }
    private static void ScheduleTemplates()
    {
        var schedule = new MissionSchedule("1981-08-26", 19);
        var text = schedule.Resolve("{ARRIVAL_DATE}|{START_DATE}|{LAST_DATE}|{SOWING_DATE}|{DAY_COUNT}|{ACTIVE_MINUTES}", 900);
        Check(text == "08 SEP 1980|26 AUG 1981|13 SEP 1981|14 SEP 1981|19|15", "configured story dates diverged");
        Check(new MissionSchedule("bad-date").StartDate == new DateTime(1981, 8, 25), "invalid inspector date not handled");
    }
    private static void CatchUpDays()
    {
        var clock = Running(140);
        var days = new List<int>();
        clock.DayCompleted += days.Add;
        clock.AdvanceTo(65);
        Check(string.Join(",", days) == "1,2,3", "multi-day tick skipped a cross");
        clock.AdvanceTo(65); clock.AdvanceTo(66);
        Check(days.Count == 3 && clock.CurrentDay == 4, "duplicate crosses");
        clock.AdvanceTo(139.9);
        Check(days.Count == 6 && clock.State == MissionState.Running, "closed before end of seventh day");
    }
    private static void Deadline()
    {
        var clock = Running(140);
        var crosses = 0; var closures = 0; var endings = 0;
        clock.DayCompleted += day => crosses++;
        clock.WindowClosed += () => closures++;
        clock.StateChanged += state => endings++;
        clock.AdvanceTo(140); clock.AdvanceTo(900);
        Check(crosses == 7 && closures == 1 && endings == 1, "expiry must be single-shot");
        Check(clock.State == MissionState.Stayed && clock.CompletedDays == 7, "incorrect ending");
        Equal(clock.RemainingSeconds, 0);
    }
    private static void PauseAndResume()
    {
        var clock = Running();
        clock.SetPaused(true, 20);
        clock.AdvanceTo(5000);
        Equal(clock.ElapsedSeconds, 20);
        clock.SetPaused(false, 7000);
        clock.AdvanceTo(7010);
        Equal(clock.ElapsedSeconds, 30);
    }
    private static void PauseTransition()
    {
        var clock = Running(timestamp: 100);
        clock.AdvanceTo(105);
        clock.SetPaused(true, 110);
        clock.SetPaused(true, 200);
        clock.SetPaused(false, 300);
        clock.SetPaused(false, 305);
        clock.AdvanceTo(310);
        Equal(clock.ElapsedSeconds, 20);
    }
    private static void RedundantPauseDoesNotTick()
    {
        var clock = Running(10);
        clock.SetPaused(false, 10);
        Check(clock.State == MissionState.Running, "routine pause check expired before UI click was processed");
        Check(clock.TryLaunch(true, 9.999), "pre-deadline click must win before later frame tick");
    }
    private static void JustInTimeLaunch()
    {
        var clock = Running(140);
        Check(clock.TryLaunch(true, 139.999), "last instant launch rejected");
        var remaining = clock.RemainingSeconds;
        clock.AdvanceTo(10000);
        Check(clock.State == MissionState.Departed && clock.CompletedDays == 6, "departure did not preserve calendar");
        Equal(clock.RemainingSeconds, remaining);
    }
    private static void AtAndAfterDeadline()
    {
        foreach (var time in new[] { 1200.0, 1200.001 })
        {
            var clock = Running();
            Check(!clock.TryLaunch(true, time) && clock.State == MissionState.Stayed, "zero or late click departed");
        }
    }
    private static void IncompleteRepair()
    {
        var clock = Running();
        Check(!clock.TryLaunch(false, 500), "incomplete repair departed");
        Equal(clock.RemainingSeconds, 700);
        Check(clock.TryLaunch(true, 600), "finished repair did not unlock departure");
    }
    private static void EndingIsLocked()
    {
        var clock = Running();
        var states = 0; clock.StateChanged += state => states++;
        Check(clock.TryLaunch(true, 100), "launch failed");
        Check(!clock.TryLaunch(true, 101) && !clock.Start(102), "duplicate ending/start accepted");
        clock.SetPaused(false, 9000); clock.AdvanceTo(10000);
        Check(states == 1 && clock.State == MissionState.Departed, "ending changed");
    }
    private static void Warnings()
    {
        var clock = Running(100);
        var warnings = new List<double>(); clock.WarningReached += warnings.Add;
        clock.AdvanceTo(49.99);
        Check(warnings.Count == 0, "early warning");
        clock.AdvanceTo(50); clock.AdvanceTo(99); clock.AdvanceTo(1000);
        Check(warnings.Count == 3, "warning skipped or duplicated");
        Equal(warnings[0], 0.5); Equal(warnings[1], 0.25); Equal(warnings[2], 0.1);
    }
    private static void CancelDeparturePreservesDeadline()
    {
        var clock = Running(100, 10);
        var days = new List<int>(); clock.DayCompleted += days.Add;
        var warnings = new List<double>(); clock.WarningReached += warnings.Add;
        Check(clock.TryLaunch(true, 60), "initial departure failed");
        var stateChanges = 0; var changes = 0;
        clock.StateChanged += state =>
        {
            Check(state == MissionState.Running && !clock.HasEnded, "cancellation state not committed before observers");
            stateChanges++;
        };
        clock.Changed += () => changes++;
        Check(clock.CancelDeparture(1000), "departure cancellation failed");
        Equal(clock.ElapsedSeconds, 60); Equal(clock.RemainingSeconds, 40);
        Check(clock.CompletedDays == 6 && clock.CurrentDay == 7, "cancellation reset calendar");
        Check(stateChanges == 1 && changes == 1, "cancellation notifications were missing or duplicated");
        clock.AdvanceTo(1001);
        Equal(clock.ElapsedSeconds, 61);
        Check(days.Count == 6 && warnings.Count == 1, "cancellation replayed calendar or warning events");
        clock.AdvanceTo(1015);
        Equal(clock.RemainingSeconds, 25);
        Check(days.Count == 7 && warnings.Count == 2, "remaining calendar or warning progress was lost");
    }
    private static void CancelDeparturePreservesPause()
    {
        var clock = Running(100);
        clock.SetPaused(true, 20);
        Check(clock.TryLaunch(true, 30), "paused launch failed");
        clock.AdvanceTo(1000);
        Check(clock.CancelDeparture(900) && clock.IsPaused, "cancellation lost the pause");
        clock.SetPaused(false, 950);
        clock.AdvanceTo(1001);
        Equal(clock.ElapsedSeconds, 21);
    }
    private static void CancelDepartureRetryDeadline()
    {
        foreach (var remainingAfterRetry in new[] { 0.001, 0.0, -0.001 })
        {
            var clock = Running(100);
            Check(clock.TryLaunch(true, 99), "initial last-second launch failed");
            Check(clock.CancelDeparture(1000), "last-second cancellation failed");
            var accepted = clock.TryLaunch(true, 1001 - remainingAfterRetry);
            Check(accepted == (remainingAfterRetry > 0), "retry did not enforce strict cutoff");
            Check(clock.State == (accepted ? MissionState.Departed : MissionState.Stayed), "retry entered the wrong state");
        }
    }
    private static void CancelDepartureStateGuards()
    {
        var clock = new MissionClock(10);
        Check(!clock.CancelDeparture(1) && clock.State == MissionState.Ready, "cancellation started a ready mission");
        clock.Start(2);
        Check(!clock.CancelDeparture(500), "running mission accepted cancellation");
        clock.AdvanceTo(12);
        Check(clock.State == MissionState.Stayed && !clock.CancelDeparture(1000), "expired mission resumed");
        Equal(clock.RemainingSeconds, 0);
        Check(!clock.TryLaunch(true, 1001), "expired mission departed after cancellation");
    }
    private static void WarningConfiguration()
    {
        var thresholds = new[] { 0.2, 0.8, 0.2 };
        var clock = new MissionClock(100, 7, thresholds);
        thresholds[0] = 0.9;
        var warnings = new List<double>(); clock.WarningReached += warnings.Add;
        clock.Start(0); clock.AdvanceTo(100);
        Check(warnings.Count == 2, "duplicate or caller-mutated warning config");
        Equal(warnings[0], 0.8); Equal(warnings[1], 0.2);
    }
    private static void MonotonicTimestamp()
    {
        var clock = Running();
        clock.AdvanceTo(50); clock.AdvanceTo(30); clock.AdvanceTo(60);
        Equal(clock.ElapsedSeconds, 60);
    }
    private static void InvalidInput()
    {
        Throws(() => new MissionClock(0)); Throws(() => new MissionClock(double.NaN));
        Throws(() => new MissionClock(1, 0)); Throws(() => new MissionClock(1, 1, new[] { 1.0 }));
        Throws(() => new MissionClock(1, 1, new[] { double.PositiveInfinity }));
        var clock = Running(); Throws(() => clock.AdvanceTo(-1));
        Throws(() => clock.TryLaunch(true, double.NaN)); Throws(() => clock.SetPaused(false, double.PositiveInfinity));
        Throws(() => clock.CancelDeparture(double.NaN)); Throws(() => clock.CancelDeparture(-1));
    }
    private static void ReentrantExpiry()
    {
        var clock = Running(7);
        clock.DayCompleted += day => { if (day == 7) Check(!clock.TryLaunch(true, 7), "expiry callback resurrected launch"); };
        clock.AdvanceTo(7);
        Check(clock.State == MissionState.Stayed, "state not committed before events");
    }
    private static void SceneLoadGap()
    {
        var clock = Running();
        clock.AdvanceTo(20);
        clock.DiscardSuspendedTime(10000);
        clock.AdvanceTo(10010);
        Equal(clock.ElapsedSeconds, 30);
        Check(!clock.Start(10011) && clock.State == MissionState.Running, "scene transition reset run");
    }
}
