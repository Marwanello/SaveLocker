using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>
/// Pure computation of "when does this schedule next fire", shared by the poller (to decide when to
/// run) and the settings DTO (to show an admin "next fetch: ..." without duplicating the logic).
/// Deliberately takes <c>nowUtc</c> as a parameter rather than reading the clock itself, so it can be
/// tested without a background service or the system clock.
/// </summary>
public static class AutoFetchScheduler
{
    /// <summary>
    /// The next moment (UTC) this schedule should fire, strictly after <paramref name="nowUtc"/>.
    /// Null when disabled. Weekly/monthly time-of-day is server-local (see
    /// <see cref="AutoFetchSchedule"/>), so the wall-clock math happens in local time and only the
    /// final answer is converted back to UTC.
    /// </summary>
    public static DateTime? ComputeNextRun(AutoFetchSchedule schedule, DateTime nowUtc)
    {
        if (!IsEnabled(schedule)) return null;

        if (schedule.Mode == "hours")
            return nowUtc.AddHours(schedule.Hours);

        var time = TimeOnly.TryParse(schedule.TimeOfDay, out var t) ? t : new TimeOnly(3, 0);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);

        var candidateLocal = schedule.Mode == "weekly"
            ? NextWeeklyLocal(nowLocal, schedule.DayOfWeek, time)
            : NextMonthlyLocal(nowLocal, schedule.DayOfMonth, time);

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(candidateLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local);
    }

    public static bool IsEnabled(AutoFetchSchedule schedule) => schedule.Mode switch
    {
        "hours" => schedule.Hours > 0,
        "weekly" or "monthly" => true,
        _ => false, // "disabled" or anything unrecognized
    };

    /// <summary>Earliest date/time strictly after <paramref name="nowLocal"/> matching the target
    /// weekday and time. Bounded to 8 days — a week plus one, so "today, but the time already
    /// passed" correctly rolls to next week rather than looping forever on a bad input.</summary>
    private static DateTime NextWeeklyLocal(DateTime nowLocal, int targetDayOfWeek, TimeOnly time)
    {
        var target = (DayOfWeek)Math.Clamp(targetDayOfWeek, 0, 6);
        for (var offset = 0; offset <= 7; offset++)
        {
            var date = nowLocal.Date.AddDays(offset);
            if (date.DayOfWeek != target) continue;
            var candidate = date + time.ToTimeSpan();
            if (candidate > nowLocal) return candidate;
        }
        // Unreachable: some day in [today, today+7] always matches every weekday.
        return nowLocal.Date.AddDays(7) + time.ToTimeSpan();
    }

    /// <summary>Earliest date/time strictly after <paramref name="nowLocal"/> matching the target
    /// day-of-month, clamped to each month's actual length (31 in a 28/29/30-day month means "the
    /// last day", not "roll into the next month"). Bounded to 25 months as a safety backstop.</summary>
    private static DateTime NextMonthlyLocal(DateTime nowLocal, int targetDayOfMonth, TimeOnly time)
    {
        var target = Math.Clamp(targetDayOfMonth, 1, 31);
        var year = nowLocal.Year;
        var month = nowLocal.Month;
        for (var i = 0; i <= 24; i++)
        {
            var day = Math.Min(target, DateTime.DaysInMonth(year, month));
            var candidate = new DateTime(year, month, day) + time.ToTimeSpan();
            if (candidate > nowLocal) return candidate;
            month++;
            if (month > 12) { month = 1; year++; }
        }
        // Unreachable under any real-world input.
        return nowLocal.Date.AddMonths(1) + time.ToTimeSpan();
    }
}
