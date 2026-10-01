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
public sealed class WorkOrderMutationsController(ServiceOpsDbContext database, TimeProvider clock, IAntiforgery antiforgery) : ControllerBase
{
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<WorkOrderDetail>(200)]
    [ProducesResponseType<ProblemDetails>(409)]
    public Task<ActionResult<WorkOrderDetail>> CorrectDetails(Guid id, CorrectDetailsRequest request, CancellationToken cancellationToken) =>
        Change(id, request.ExpectedRevision, (order, actor) => order.UpdateDetails(request.Title, request.Description, actor, clock), cancellationToken);

    [HttpPut("{id:guid}/assignment")]
    [ProducesResponseType<WorkOrderDetail>(200)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<WorkOrderDetail>> Assign(Guid id, AssignmentRequest request, CancellationToken cancellationToken)
    {
        var technician = request.TechnicianId.HasValue
            ? await database.Technicians.SingleOrDefaultAsync(x => x.Id == request.TechnicianId, cancellationToken) : null;
        if (request.TechnicianId.HasValue && technician is null)
        {
            ModelState.AddModelError("technicianId", "Choose an existing active technician.");
            return ValidationProblem(ModelState);
        }
        return await Change(id, request.ExpectedRevision, (order, actor) =>
        {
            if (technician is null) order.Unassign(actor, clock);
            else order.Assign(technician, actor, clock);
        }, cancellationToken);
    }

    [HttpPost("{id:guid}/status-transitions")]
    [ProducesResponseType<WorkOrderDetail>(200)]
    [ProducesResponseType<ProblemDetails>(409)]
    public Task<ActionResult<WorkOrderDetail>> Transition(Guid id, StatusTransitionRequest request, CancellationToken cancellationToken) =>
        Change(id, request.ExpectedRevision, (order, actor) =>
        {
            // Transport dispatch only: each operation independently enforces its domain preconditions.
            switch (request.TargetStatus)
            {
                case WorkOrderStatus.InProgress:
                    if (order.Status == WorkOrderStatus.OnHold) order.Resume(actor, clock);
                    else order.StartWork(actor, clock);
                    break;
                case WorkOrderStatus.OnHold: order.PlaceOnHold(request.Reason ?? "", actor, clock); break;
                case WorkOrderStatus.Completed: order.Complete(request.Summary ?? "", actor, clock); break;
                case WorkOrderStatus.Cancelled: order.Cancel(request.Reason ?? "", actor, clock); break;
                default: throw new InvalidOperationException("Use assignment or unassignment to enter Assigned or New status.");
            }
        }, cancellationToken);

    // Shared persistence/error handling for these three endpoints, not a command or service framework.
    private async Task<ActionResult<WorkOrderDetail>> Change(Guid id, int? expectedRevision,
        Action<WorkOrder, Guid> operation, CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return Problem(statusCode: 400, title: "Invalid request token", detail: "Refresh the page and try again.");
        var order = await database.WorkOrders.Include(x => x.Technician).Include(x => x.Location).ThenInclude(x => x.Customer)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (order is null) return NotFound();
        if (expectedRevision.HasValue && expectedRevision != order.Revision) return StaleRevision();
        try
        {
            operation(order, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        }
        catch (ArgumentException error)
        {
            ModelState.AddModelError(error.ParamName ?? "", error.Message.Split(" (Parameter")[0]);
            return ValidationProblem(ModelState);
        }
        catch (InvalidOperationException error)
        {
            return ConflictProblem("Action not permitted", error.Message, "invalid_transition");
        }
        // Revision's tracked original value is part of EF's UPDATE predicate. One SaveChanges
        // transaction commits the update and new activity, or rolls both back on a race/failure.
        // History is deliberately not loaded. These are only the new domain-created entries;
        // mark their preassigned UUIDs as inserts rather than letting EF infer existing rows.
        database.WorkOrderActivities.AddRange(order.Activities);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return StaleRevision(); }
        return WorkOrderDetail.From(order, clock.GetUtcNow());
    }

    private ObjectResult StaleRevision() => ConflictProblem("Work order changed",
        "This work order changed since you loaded it. Reload and review the latest information before trying again.", "stale_revision");

    private ObjectResult ConflictProblem(string title, string detail, string code)
    {
        // MVC's factory has already supplied common extensions; replace its default error code.
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, 409, title, detail: detail);
        problem.Extensions["errorCode"] = code;
        return Conflict(problem);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectDetailsRequest([Required, MaxLength(200)] string Title,
    [Required, MaxLength(10000)] string Description, [Range(1, int.MaxValue)] int? ExpectedRevision = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignmentRequest([property: JsonRequired] Guid? TechnicianId, [Range(1, int.MaxValue)] int? ExpectedRevision = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StatusTransitionRequest([Required] WorkOrderStatus? TargetStatus,
    [MaxLength(5000)] string? Reason = null, [MaxLength(5000)] string? Summary = null,
    [Range(1, int.MaxValue)] int? ExpectedRevision = null);
