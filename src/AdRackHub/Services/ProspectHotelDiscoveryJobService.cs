using System.Collections.Concurrent;

namespace AdRackHub.Services;

public class ProspectHotelDiscoveryJobService
{
    private readonly ConcurrentDictionary<Guid, ProspectHotelJob> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProspectHotelDiscoveryJobService> _logger;

    public ProspectHotelDiscoveryJobService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProspectHotelDiscoveryJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Guid Start(bool dryRun, int? maxSeeds, int? skipSeeds = null, int? routeId = null, string? cityContains = null)
    {
        var job = new ProspectHotelJob
        {
            Id = Guid.NewGuid(),
            DryRun = dryRun,
            MaxSeeds = maxSeeds,
            SkipSeeds = skipSeeds,
            RouteId = routeId,
            CityContains = cityContains,
            Status = ProspectHotelJobStatus.Running,
            StartedAt = DateTime.UtcNow,
            Progress = new ProspectHotelProgress { Phase = "Starting" }
        };

        if (!_jobs.TryAdd(job.Id, job))
            throw new InvalidOperationException("Could not start discovery job.");

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var discovery = scope.ServiceProvider.GetRequiredService<ProspectHotelDiscoveryService>();
                var progress = new Progress<ProspectHotelProgress>(p => job.Progress = p);
                job.Result = await discovery.DiscoverAsync(
                    dryRun,
                    maxSeeds,
                    skipSeeds,
                    routeId,
                    cityContains,
                    cancellationToken: job.Cancellation.Token,
                    progress: progress);
                job.Status = ProspectHotelJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
            }
            catch (OperationCanceledException)
            {
                job.Status = ProspectHotelJobStatus.Canceled;
                job.Error = "Canceled.";
                job.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Prospect hotel discovery job {JobId} failed", job.Id);
                job.Status = ProspectHotelJobStatus.Failed;
                job.Error = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
            }
        });

        return job.Id;
    }

    public ProspectHotelJob? Get(Guid id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public bool Cancel(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return false;
        job.Cancellation.Cancel();
        return true;
    }
}

public enum ProspectHotelJobStatus
{
    Running,
    Completed,
    Failed,
    Canceled
}

public class ProspectHotelJob
{
    public Guid Id { get; init; }
    public bool DryRun { get; init; }
    public int? MaxSeeds { get; init; }
    public int? SkipSeeds { get; init; }
    public int? RouteId { get; init; }
    public string? CityContains { get; init; }
    public ProspectHotelJobStatus Status { get; set; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public ProspectHotelProgress Progress { get; set; } = new();
    public ProspectHotelDiscoveryResult? Result { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
}
