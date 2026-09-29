using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
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
public sealed class WorkOrdersController(ServiceOpsDbContext database, TimeProvider clock, IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<WorkOrderDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkOrderDetail>> Create(CreateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return Problem(statusCode: 400, title: "Invalid request token", detail: "Refresh the page and try again.");
        var customer = await database.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId, cancellationToken);
        var location = await database.Locations.SingleOrDefaultAsync(x => x.Id == request.LocationId, cancellationToken);
        if (customer is null) ModelState.AddModelError("customerId", "Choose an existing customer.");
        if (location is null) ModelState.AddModelError("locationId", "Choose an existing location.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        WorkOrder order;
        try
        {
            order = WorkOrder.Create(customer!, location!, request.ServiceType!.Value, request.Priority!.Value,
                request.Title, request.Description, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), clock);
        }
        catch (ArgumentException error)
        {
            ModelState.AddModelError(error.ParamName ?? "", error.Message.Split(" (Parameter")[0]);
            return ValidationProblem(ModelState);
        }

        database.WorkOrders.Add(order);
        // EF saves the order and its Created activity in one transaction.
        await database.SaveChangesAsync(cancellationToken);
        var detail = WorkOrderDetail.From(order, clock.GetUtcNow());
        return CreatedAtAction(nameof(Get), new { id = order.Id }, detail);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<WorkOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkOrderDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var order = await database.WorkOrders.AsNoTracking().Include(x => x.Location).ThenInclude(x => x.Customer)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return order is null ? NotFound() : WorkOrderDetail.From(order, clock.GetUtcNow());
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateWorkOrderRequest(
    [Required] Guid? CustomerId, [Required] Guid? LocationId,
    [Required] ServiceType? ServiceType, [Required] Priority? Priority,
    [Required, MaxLength(200)] string Title, [Required, MaxLength(10000)] string Description);

public sealed record WorkOrderDetail(Guid Id, string Number, string Title, string Description,
    Guid CustomerId, string CustomerName, Guid LocationId, string LocationName, string LocationAddress,
    ServiceType ServiceType, Priority Priority, WorkOrderStatus Status, DateTimeOffset CreatedAt,
    Guid CreatedByUserId, int SlaDurationMinutes, DateTimeOffset SlaAtRiskAt, DateTimeOffset SlaDeadlineAt,
    SlaState SlaState, int Revision, DateTimeOffset EvaluatedAt)
{
    public static WorkOrderDetail From(WorkOrder order, DateTimeOffset evaluatedAt) => new(
        order.Id, order.Number, order.Title, order.Description, order.Location.CustomerId, order.Location.Customer.Name,
        order.LocationId, order.Location.Name, order.Location.Address, order.ServiceType, order.Priority, order.Status,
        order.CreatedAt, order.CreatedByUserId, order.SlaDurationMinutes, order.SlaAtRiskAt, order.SlaDeadlineAt,
        order.GetSlaState(evaluatedAt), order.Revision, evaluatedAt);
}
