using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Features.WorkOrders;

[ApiController]
[Route("api/v1/work-orders")]
[Authorize(Policy = "Operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkOrderListController(ServiceOpsDbContext database, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WorkOrderPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkOrderPage>> List([FromQuery] WorkOrderListRequest request, CancellationToken cancellationToken)
    {
        // Do not silently pretend that assignment/completion fields already exist.
        string[] supported = ["search", "customerId", "locationId", "serviceType", "status", "slaStatus",
            "createdFrom", "createdTo", "openOnly", "sort", "page", "pageSize"];
        foreach (var key in Request.Query.Keys)
            if (!supported.Contains(key, StringComparer.OrdinalIgnoreCase))
                ModelState.AddModelError(key, "This filter is not supported by the current work-order model.");
        if (request.PageSize is not (25 or 50 or 100)) ModelState.AddModelError("pageSize", "Choose 25, 50, or 100 rows.");
        if ((long)(request.Page - 1) * request.PageSize > int.MaxValue) ModelState.AddModelError("page", "Page is too large.");
        if (request.CreatedFrom >= request.CreatedTo) ModelState.AddModelError("createdTo", "End must be later than start (end is exclusive).");
        foreach (var key in new[] { "createdFrom", "createdTo" })
            if (Request.Query.TryGetValue(key, out var value) && !Regex.IsMatch(value.ToString(), @"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase))
                ModelState.AddModelError(key, "Use an ISO timestamp with Z or an explicit UTC offset.");
        if (request.CustomerId == Guid.Empty || request.LocationId == Guid.Empty) ModelState.AddModelError("locationId", "Use a valid customer or location ID.");
        if (request.ServiceType.HasValue && !Enum.IsDefined(request.ServiceType.Value)) ModelState.AddModelError("serviceType", "Choose a valid service type.");
        if (request.Status.Any(status => !Enum.IsDefined(status))) ModelState.AddModelError("status", "Choose a currently supported status (New).");
        if (request.SlaStatus.HasValue && !Enum.IsDefined(request.SlaStatus.Value)) ModelState.AddModelError("slaStatus", "Choose Good, AtRisk, or Breached.");
        var sort = request.Sort ?? "-createdAt";
        string[] sorts = ["number", "createdAt", "deadline", "priority", "status", "customerName"];
        if (!sorts.Contains(sort.TrimStart('-')) || sort.StartsWith("--")) ModelState.AddModelError("sort", "Choose number, createdAt, deadline, priority, status, or customerName; prefix - for descending.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var evaluatedAt = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var orders = database.WorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Literal substring matching: user-entered % and _ are not wildcard operators.
            var search = request.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            orders = orders.Where(x => EF.Functions.ILike(x.Number, $"%{search}%", "\\") || EF.Functions.ILike(x.Title, $"%{search}%", "\\"));
        }
        if (request.CustomerId.HasValue) orders = orders.Where(x => x.Location.CustomerId == request.CustomerId);
        if (request.LocationId.HasValue) orders = orders.Where(x => x.LocationId == request.LocationId);
        if (request.ServiceType.HasValue) orders = orders.Where(x => x.ServiceType == request.ServiceType);
        if (request.Status.Length > 0) orders = orders.Where(x => request.Status.Contains(x.Status));
        // New is the only open status implemented so far. Expand with the workflow phase.
        if (request.OpenOnly == true) orders = orders.Where(x => x.Status == WorkOrderStatus.New);
        if (request.CreatedFrom.HasValue) { var from = request.CreatedFrom.Value.ToUniversalTime(); orders = orders.Where(x => x.CreatedAt >= from); }
        if (request.CreatedTo.HasValue) { var to = request.CreatedTo.Value.ToUniversalTime(); orders = orders.Where(x => x.CreatedAt < to); }

        // SQL equivalent of WorkOrder.GetSlaState, verified against the domain at exact boundaries.
        // Project before filtering so the SLA predicate has one definition in this query.
        var rows = orders.Select(x => new WorkOrderSummary
        {
            Id = x.Id, Number = x.Number, Title = x.Title, CustomerId = x.Location.CustomerId,
            CustomerName = x.Location.Customer.Name, LocationId = x.LocationId, LocationName = x.Location.Name,
            ServiceType = x.ServiceType, Priority = x.Priority, Status = x.Status,
            CreatedAt = x.CreatedAt, Deadline = x.SlaDeadlineAt,
            SlaState = evaluatedAt >= x.SlaDeadlineAt ? SlaState.Breached : evaluatedAt >= x.SlaAtRiskAt ? SlaState.AtRisk : SlaState.Good
        });
        if (request.SlaStatus.HasValue) rows = rows.Where(x => x.SlaState == request.SlaStatus);
        var total = await rows.CountAsync(cancellationToken);
        var sorted = sort switch
        {
            "number" => rows.OrderBy(x => x.Number), "-number" => rows.OrderByDescending(x => x.Number),
            "createdAt" => rows.OrderBy(x => x.CreatedAt), "-createdAt" => rows.OrderByDescending(x => x.CreatedAt),
            "deadline" => rows.OrderBy(x => x.Deadline), "-deadline" => rows.OrderByDescending(x => x.Deadline),
            "priority" => rows.OrderBy(x => x.Priority == Priority.Critical ? 0 : x.Priority == Priority.High ? 1 : x.Priority == Priority.Normal ? 2 : 3),
            "-priority" => rows.OrderByDescending(x => x.Priority == Priority.Critical ? 0 : x.Priority == Priority.High ? 1 : x.Priority == Priority.Normal ? 2 : 3),
            "status" => rows.OrderBy(x => x.Status), "-status" => rows.OrderByDescending(x => x.Status),
            "customerName" => rows.OrderBy(x => x.CustomerName), "-customerName" => rows.OrderByDescending(x => x.CustomerName),
            _ => throw new InvalidOperationException("Sort was not validated.")
        };
        var items = await sorted.ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WorkOrderPage(items, request.Page, request.PageSize, total, evaluatedAt);
    }
}

public sealed class WorkOrderListRequest
{
    [MaxLength(200)] public string? Search { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? LocationId { get; init; }
    public ServiceType? ServiceType { get; init; }
    public WorkOrderStatus[] Status { get; init; } = [];
    public SlaState? SlaStatus { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public bool? OpenOnly { get; init; }
    public string? Sort { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record WorkOrderPage(WorkOrderSummary[] Items, int Page, int PageSize, int TotalCount, DateTimeOffset EvaluatedAt);
public sealed record WorkOrderSummary
{
    public required Guid Id { get; init; }
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required Guid LocationId { get; init; }
    public required string LocationName { get; init; }
    public required ServiceType ServiceType { get; init; }
    public required Priority Priority { get; init; }
    public required WorkOrderStatus Status { get; init; }
    public required SlaState SlaState { get; init; }
    public required DateTimeOffset Deadline { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
