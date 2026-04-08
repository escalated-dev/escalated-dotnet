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
        var tz = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
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
        var tz = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
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

            var dayStart = current.Date.Add(TimeSpan.Parse(daySchedule.Value.Start));
            var dayEnd = current.Date.Add(TimeSpan.Parse(daySchedule.Value.End));

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

    private (string Start, string End)? GetDaySchedule(DateTime local, BusinessSchedule schedule)
    {
        if (string.IsNullOrEmpty(schedule.Schedule)) return null;

        var dayName = local.DayOfWeek.ToString().ToLower();
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(schedule.Schedule);
            if (dict != null && dict.TryGetValue(dayName, out var dayData))
            {
                if (dayData.TryGetValue("start", out var start) && dayData.TryGetValue("end", out var end))
                    return (start, end);
            }
        }
        catch { }

        return null;
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
