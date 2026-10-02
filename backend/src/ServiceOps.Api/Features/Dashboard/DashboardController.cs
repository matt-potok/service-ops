using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Features.Dashboard;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = "Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(ServiceOpsDbContext database, TimeProvider clock) : ControllerBase
{
    private const string ReportingZone = "America/New_York";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ReportingZone);

    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DashboardResponse>> Get([FromQuery] DashboardRequest request, CancellationToken cancellationToken)
    {
        foreach (var (key, value) in Request.Query)
        {
            if (!new[] { "startDate", "endDateExclusive" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                ModelState.AddModelError(key, "This dashboard filter is not supported.");
            else if (value.Count != 1 || !DateOnly.TryParseExact(value.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                ModelState.AddModelError(key, "Use one calendar date in yyyy-MM-dd format.");
        }
        if (request.StartDate.HasValue != request.EndDateExclusive.HasValue)
            ModelState.AddModelError("endDateExclusive", "Provide both dates, or omit both for the last 30 calendar days.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var evaluatedAt = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(evaluatedAt, Zone).DateTime);
        var start = request.StartDate ?? today.AddDays(-29);
        var end = request.EndDateExclusive ?? today.AddDays(1);
        var days = end.DayNumber - start.DayNumber;
        if (days is < 1 or > 366)
            ModelState.AddModelError("endDateExclusive", "Choose a period of 1 to 366 calendar days; the end is exclusive.");
        if (start.Year < 1900 || end.Year > 2100)
            ModelState.AddModelError("startDate", "Choose dates between 1900 and 2100.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start.ToDateTime(TimeOnly.MinValue), Zone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end.ToDateTime(TimeOnly.MinValue), Zone));
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var open = database.WorkOrders.AsNoTracking().Where(x => x.Status != WorkOrderStatus.Completed && x.Status != WorkOrderStatus.Cancelled);
        // SQL equivalents of the accepted domain SLA boundaries; terminal work never enters this cohort.
        var current = await open.GroupBy(x => 1).Select(g => new CurrentOperations(
            g.Count(), g.Count(x => x.Status == WorkOrderStatus.New), g.Count(x => x.Status == WorkOrderStatus.Assigned),
            g.Count(x => x.Status == WorkOrderStatus.InProgress), g.Count(x => x.Status == WorkOrderStatus.OnHold),
            g.Count(x => evaluatedAt < x.SlaAtRiskAt),
            g.Count(x => evaluatedAt >= x.SlaAtRiskAt && evaluatedAt < x.SlaDeadlineAt),
            g.Count(x => evaluatedAt >= x.SlaDeadlineAt))).SingleOrDefaultAsync(cancellationToken)
            ?? new CurrentOperations(0, 0, 0, 0, 0, 0, 0, 0);
        var workload = await open.GroupBy(x => new { x.TechnicianId, Name = x.Technician == null ? "Unassigned" : x.Technician.DisplayName })
            .Select(g => new TechnicianWorkload(g.Key.TechnicianId, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken);
        if (workload.All(x => x.TechnicianId != null)) workload.Add(new TechnicianWorkload(null, "Unassigned", 0));
        var completed = await database.WorkOrders.AsNoTracking()
            .Where(x => x.Status == WorkOrderStatus.Completed && x.CompletedAt >= from && x.CompletedAt < to)
            .GroupBy(x => 1).Select(g => new { Total = g.Count(), Met = g.Count(x => x.CompletedAt <= x.SlaDeadlineAt) })
            .SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var total = completed?.Total ?? 0;
        var met = completed?.Met ?? 0;
        return new DashboardResponse(evaluatedAt, ReportingZone, current,
            workload.OrderBy(x => x.TechnicianId != null).ThenByDescending(x => x.OpenCount).ThenBy(x => x.TechnicianName).ThenBy(x => x.TechnicianId).ToArray(),
            new PeriodPerformance(start, end, from, to, total, met, total - met,
                total == 0 ? null : Math.Round(100m * met / total, 1, MidpointRounding.AwayFromZero)));
    }
}

public sealed class DashboardRequest
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDateExclusive { get; init; }
}

public sealed record DashboardResponse(DateTimeOffset EvaluatedAt, string TimeZone, CurrentOperations Current,
    TechnicianWorkload[] Workload, PeriodPerformance Period);
public sealed record CurrentOperations(int Open, int New, int Assigned, int InProgress, int OnHold, int Good, int AtRisk, int Breached);
public sealed record TechnicianWorkload(Guid? TechnicianId, string TechnicianName, int OpenCount);
public sealed record PeriodPerformance(DateOnly StartDate, DateOnly EndDateExclusive, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    int Completed, int Met, int Missed, decimal? CompliancePercent);
