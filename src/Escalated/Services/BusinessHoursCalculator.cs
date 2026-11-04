using System.Globalization;
using System.Text.Json;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class BusinessHoursCalculator
{
    private readonly EscalatedDbContext _db;

    public BusinessHoursCalculator(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Check if a given datetime falls within business hours for a schedule.
    /// </summary>
    public bool IsWithinBusinessHours(DateTime dateTimeUtc, BusinessSchedule schedule)
    {
        var tz = ZoneOf(schedule);
        var local = TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtc, tz);

        if (IsHoliday(local, schedule)) return false;

        var daySchedule = GetDaySchedule(local, schedule);
        if (daySchedule == null) return false;

        var time = local.ToString("HH:mm");
        return string.Compare(time, daySchedule.Value.Start, StringComparison.Ordinal) >= 0
            && string.Compare(time, daySchedule.Value.End, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// Add business hours to a start time and return the resulting UTC datetime.
    /// </summary>
    public DateTime AddBusinessHours(DateTime startUtc, double hours, BusinessSchedule schedule)
    {
        // A schedule with no open day counts calendar time, as escalated-laravel's calculator.
        if (!HasOpenDay(schedule)) return startUtc.AddHours(hours);

        var tz = ZoneOf(schedule);
        var current = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
        var remainingMinutes = hours * 60;
        var maxIterations = 365;

        while (remainingMinutes > 0 && maxIterations-- > 0)
        {
            if (IsHoliday(current, schedule))
            {
                current = current.AddDays(1).Date;
                continue;
            }

            var daySchedule = GetDaySchedule(current, schedule);
            if (daySchedule == null)
            {
                current = current.AddDays(1).Date;
                continue;
            }

            var dayStart = current.Date.Add(Clock(daySchedule.Value.Start));
            var dayEnd = current.Date.Add(Clock(daySchedule.Value.End));

            if (current < dayStart)
                current = dayStart;

            if (current >= dayEnd)
            {
                current = current.AddDays(1).Date;
                continue;
            }

            var availableMinutes = (dayEnd - current).TotalMinutes;
            if (remainingMinutes <= availableMinutes)
            {
                current = current.AddMinutes(remainingMinutes);
                remainingMinutes = 0;
            }
            else
            {
                remainingMinutes -= availableMinutes;
                current = current.AddDays(1).Date;
            }
        }

        return TimeZoneInfo.ConvertTimeToUtc(current, tz);
    }

    /// <summary>The schedule's zone; UTC when the zone is unknown, as escalated-laravel.</summary>
    private static TimeZoneInfo ZoneOf(BusinessSchedule schedule)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>A wall-clock time of day; <c>24:00</c> is the next midnight.</summary>
    private static TimeSpan Clock(string value) =>
        value == "24:00" ? TimeSpan.FromDays(1) : TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time) ? time : TimeSpan.Zero;

    private static bool HasOpenDay(BusinessSchedule schedule) =>
        Enum.GetValues<DayOfWeek>().Any(day => GetDaySchedule(day, schedule) is not null);

    private static (string Start, string End)? GetDaySchedule(DateTime local, BusinessSchedule schedule) =>
        GetDaySchedule(local.DayOfWeek, schedule);

    /// <summary>
    /// A day's hours, or null when it is closed: left out, <c>enabled: false</c>, a missing
    /// start or end, or a start equal to its end.
    /// </summary>
    private static (string Start, string End)? GetDaySchedule(DayOfWeek day, BusinessSchedule schedule)
    {
        if (string.IsNullOrEmpty(schedule.Schedule)) return null;

        var dayName = day.ToString().ToLowerInvariant();
        try
        {
            using var document = JsonDocument.Parse(schedule.Schedule);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(dayName, out var hours)
                || hours.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (hours.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return null;
            var start = hours.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var end = hours.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end) || start == end) return null;
            return (start, end);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool IsHoliday(DateTime local, BusinessSchedule schedule)
    {
        foreach (var holiday in schedule.Holidays)
        {
            if (holiday.Recurring)
            {
                if (local.Month == holiday.Date.Month && local.Day == holiday.Date.Day)
                    return true;
            }
            else
            {
                if (local.Date == holiday.Date.Date)
                    return true;
            }
        }
        return false;
    }
}
