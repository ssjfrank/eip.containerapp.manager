using Quartz;

namespace ContainerApp.Manager.Scheduling;

public sealed class SchedulerService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private IScheduler? _scheduler;

    public SchedulerService(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        await _scheduler.Start(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is not null)
        {
            await _scheduler.Shutdown(waitForJobsToComplete: false, cancellationToken);
        }
    }
}


