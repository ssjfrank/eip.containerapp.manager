using ContainerApp.Manager.Config;
using Cronos;

namespace ContainerApp.Manager.Scheduling;

public interface IScheduleEvaluator
{
    bool IsInActiveWindow(AppMapping mapping, DateTimeOffset nowUtc, out int desiredReplicas, out ScheduleWindow? activeWindow);
}

public sealed class ScheduleEvaluator : IScheduleEvaluator
{
    public bool IsInActiveWindow(AppMapping mapping, DateTimeOffset nowUtc, out int desiredReplicas, out ScheduleWindow? activeWindow)
    {
        desiredReplicas = mapping.DesiredReplicas;
        activeWindow = null;
        foreach (var window in mapping.Schedules)
        {
            if (string.IsNullOrWhiteSpace(window.Cron)) continue;
            var expr = CronExpression.Parse(window.Cron, CronFormat.IncludeSeconds);
            var from = nowUtc.AddMinutes(-1 * Math.Max(1, window.DurationMinutes)).UtcDateTime;
            var to = nowUtc.UtcDateTime;
            // Find the last occurrence between from and to by iterating backwards a small number of steps
            var next = expr.GetNextOccurrence(from, TimeZoneInfo.Utc);
            DateTime? last = null;
            while (next.HasValue && next.Value <= to)
            {
                last = next;
                next = expr.GetNextOccurrence(next.Value, TimeZoneInfo.Utc);
            }
            if (last.HasValue)
            {
                var start = new DateTimeOffset(last.Value, TimeSpan.Zero);
                var end = start.AddMinutes(Math.Max(1, window.DurationMinutes));
                if (nowUtc >= start && nowUtc <= end)
                {
                    desiredReplicas = window.DesiredReplicas;
                    activeWindow = window;
                    return true;
                }
            }
        }
        return false;
    }
}


