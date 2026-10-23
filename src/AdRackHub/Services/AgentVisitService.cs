using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public interface IAgentVisitApi
{
    Task<AgentCallResult<AgentStopListDto>> SearchStopsAsync(
        string? search,
        int? routeId,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);

    Task<AgentCallResult<AgentVisitListDto>> ListVisitsAsync(
        int? stopId,
        int? routeId,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);

    Task<AgentCallResult<AgentVisitDto>> LogAsync(
        AgentVisitUpload upload,
        string hubBase,
        string? userId,
        string? userName,
        CancellationToken cancellationToken);
}

public class AgentVisitService : IAgentVisitApi
{
    private readonly ApplicationDbContext _context;
    private readonly StopVisitService _visits;

    public AgentVisitService(ApplicationDbContext context, StopVisitService visits)
    {
        _context = context;
        _visits = visits;
    }

    public async Task<AgentCallResult<AgentStopListDto>> SearchStopsAsync(
        string? search,
        int? routeId,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = Page(page, pageSize);
        var query = ActiveStops(tracking: false);
        if (routeId is int id)
            query = query.Where(s => s.RouteId == id);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.StopName.ToLower().Contains(term)
                || s.Route!.RouteName.ToLower().Contains(term)
                || (s.City != null && s.City.ToLower().Contains(term))
                || (s.HighwayExit != null && s.HighwayExit.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var stops = await query
            .OrderBy(s => s.Route!.RouteName)
            .ThenBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return AgentCallResult<AgentStopListDto>.Ok(new AgentStopListDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = Pages(total, pageSize),
            HasMore = page < Pages(total, pageSize),
            Items = stops.Select(stop => MapStop(stop, hubBase)).ToList()
        });
    }

    public async Task<AgentCallResult<AgentVisitListDto>> ListVisitsAsync(
        int? stopId,
        int? routeId,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = Page(page, pageSize);
        var query = _context.StopVisits.AsNoTracking()
            .Include(v => v.Stop).ThenInclude(s => s.Route)
            .Include(v => v.Photos)
            .AsQueryable();
        if (stopId is int id)
            query = query.Where(v => v.StopId == id);
        if (routeId is int route)
            query = query.Where(v => v.Stop.RouteId == route);

        var total = await query.CountAsync(cancellationToken);
        var visits = await query
            .OrderByDescending(v => v.VisitedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return AgentCallResult<AgentVisitListDto>.Ok(new AgentVisitListDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = Pages(total, pageSize),
            HasMore = page < Pages(total, pageSize),
            Items = visits.Select(visit => MapVisit(visit, hubBase)).ToList()
        });
    }

    public async Task<AgentCallResult<AgentVisitDto>> LogAsync(
        AgentVisitUpload upload,
        string hubBase,
        string? userId,
        string? userName,
        CancellationToken cancellationToken)
    {
        StoredVisitImage before;
        StoredVisitImage after;
        DateTime when;
        try
        {
            before = VisitImageRules.FromBase64(upload.BeforeImageBase64, upload.BeforeFileName, "Before photo");
            after = VisitImageRules.FromBase64(upload.AfterImageBase64, upload.AfterFileName, "After photo");
            when = VisitImageRules.ParseVisitedAt(upload.VisitedAt);
        }
        catch (InvalidOperationException ex)
        {
            return AgentCallResult<AgentVisitDto>.Invalid([ex.Message]);
        }

        var resolved = await ResolveStopAsync(upload, cancellationToken);
        if (resolved.Error != null)
            return AgentCallResult<AgentVisitDto>.Fail(resolved.Status, resolved.Error);
        var stop = resolved.Stop!;

        try
        {
            var visit = await _visits.LogWithPhotosAsync(
                stop,
                userId,
                string.IsNullOrWhiteSpace(userName) ? "Agent API" : userName,
                upload.DriverName,
                upload.Notes,
                when,
                before,
                after,
                cancellationToken);
            visit.Stop = stop;
            return AgentCallResult<AgentVisitDto>.Created(MapVisit(visit, hubBase), $"{hubBase.TrimEnd('/')}/api/agent/visits?stopId={visit.StopId}");
        }
        catch (InvalidOperationException ex)
        {
            return AgentCallResult<AgentVisitDto>.Invalid([ex.Message]);
        }
    }

    private async Task<(Stop? Stop, int Status, string? Error)> ResolveStopAsync(AgentVisitUpload upload, CancellationToken cancellationToken)
    {
        if (upload.StopId is int id)
        {
            var byId = await ActiveStops().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            return byId == null
                ? (null, 404, "Stop was not found.")
                : (byId, 200, null);
        }

        var name = upload.StopName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (null, 400, "Send stopId, or stopName. Use search_stops when the email does not include the stop id.");

        var term = name.ToLower();
        var query = ActiveStops().Where(s => s.StopName.ToLower().Contains(term));
        if (!string.IsNullOrWhiteSpace(upload.RouteName))
        {
            var route = upload.RouteName.Trim().ToLower();
            query = query.Where(s => s.Route!.RouteName.ToLower().Contains(route));
        }

        var matches = await query.OrderBy(s => s.StopName).Take(9).ToListAsync(cancellationToken);
        if (matches.Count == 0)
            return (null, 404, "No stop matches that name.");
        if (matches.Count > 1)
        {
            var listed = string.Join("; ", matches.Take(8).Select(s => $"{s.Id} {s.StopName} ({s.Route!.RouteName})"));
            return (null, 409, "More than one stop matches. Use search_stops and send stopId. Matches: " + listed);
        }

        return (matches[0], 200, null);
    }

    private IQueryable<Stop> ActiveStops(bool tracking = true)
    {
        var query = tracking
            ? _context.Stops.Include(s => s.Route)
            : _context.Stops.AsNoTracking().Include(s => s.Route);
        return query.Where(s => s.Status == StopStatus.Active);
    }

    private static string? Iso(DateTime? value) => value.HasValue ? Iso(value.Value) : null;

    private static string Iso(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToString("o");
    }

    private static AgentStopDto MapStop(Stop stop, string hubBase) => new()
    {
        Id = stop.Id,
        StopName = stop.StopName,
        RouteId = stop.RouteId,
        RouteName = stop.Route!.RouteName,
        City = stop.City,
        State = stop.State,
        HighwayExit = stop.HighwayExit,
        LastVisitedAt = Iso(stop.LastVisitedAt),
        HubUrl = $"{hubBase.TrimEnd('/')}/Stops/Details/{stop.Id}"
    };

    private static AgentVisitDto MapVisit(StopVisit visit, string hubBase)
    {
        var hub = hubBase.TrimEnd('/');
        return new AgentVisitDto
        {
            Id = visit.Id,
            StopId = visit.StopId,
            StopName = visit.Stop?.StopName ?? "",
            RouteId = visit.Stop?.RouteId ?? 0,
            RouteName = visit.Stop?.Route?.RouteName ?? "",
            VisitedAt = Iso(visit.VisitedAt) ?? "",
            DriverName = visit.DriverName,
            VisitedByName = visit.VisitedByName,
            Notes = visit.Notes,
            HubUrl = $"{hub}/Stops/Details/{visit.StopId}",
            Photos = visit.Photos.OrderBy(p => p.Kind).Select(photo => new AgentVisitPhotoDto
            {
                Id = photo.Id,
                Kind = photo.Kind.ToString(),
                FileName = photo.OriginalFileName,
                ContentType = photo.ContentType,
                HubUrl = $"{hub}/Stops/VisitPhoto/{photo.Id}"
            }).ToList()
        };
    }

    private static (int Page, int PageSize) Page(int page, int pageSize)
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = AgentCustomerApiService.DefaultPageSize;
        if (pageSize > AgentCustomerApiService.MaxPageSize)
            pageSize = AgentCustomerApiService.MaxPageSize;
        return (page, pageSize);
    }

    private static int Pages(int total, int pageSize) =>
        total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
}
