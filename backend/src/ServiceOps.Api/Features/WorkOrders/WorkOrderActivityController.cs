using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Features.WorkOrders;

[ApiController]
[Route("api/v1/work-orders/{id:guid}/activity")]
[Authorize(Policy = "Operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkOrderActivityController(ServiceOpsDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ActivityPage>> Get(Guid id, CancellationToken cancellationToken,
        [FromQuery, MaxLength(512)] string? cursor = null, [FromQuery, Range(1, 100)] int pageSize = 25)
    {
        if (!await database.WorkOrders.AnyAsync(x => x.Id == id, cancellationToken)) return NotFound();
        var query = database.WorkOrderActivities.AsNoTracking().Where(x => x.WorkOrderId == id);
        if (cursor is not null)
        {
            try
            {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
                if (parts.Length != 2) throw new FormatException();
                var at = DateTimeOffset.ParseExact(parts[0], "O", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
                var activityId = Guid.Parse(parts[1]);
                query = query.Where(x => x.EffectiveAt > at || (x.EffectiveAt == at && x.Id.CompareTo(activityId) > 0));
            }
            catch (FormatException)
            {
                ModelState.AddModelError("cursor", "Use the next cursor returned by this activity endpoint.");
                return ValidationProblem(ModelState);
            }
        }
        var rows = await (from activity in query
            join user in database.Users on activity.ActorUserId equals user.Id
            orderby activity.EffectiveAt, activity.Id
            select new { Activity = activity, user.DisplayName }).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        var items = rows.Take(pageSize).Select(x => new ActivityItem(x.Activity.Id, x.Activity.EventType,
            x.Activity.ActorUserId, x.DisplayName, x.Activity.EffectiveAt, x.Activity.RecordedAt,
            x.Activity.Changes is null ? null : JsonSerializer.Deserialize<JsonElement>(x.Activity.Changes))).ToArray();
        var last = items.LastOrDefault();
        var next = rows.Length > pageSize && last is not null
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.EffectiveAt:O}|{last.Id}")) : null;
        return new ActivityPage(items, next);
    }
}

public sealed record ActivityItem(Guid Id, WorkOrderEventType EventType, Guid ActorUserId, string ActorName,
    DateTimeOffset EffectiveAt, DateTimeOffset RecordedAt, JsonElement? Changes);
public sealed record ActivityPage(ActivityItem[] Items, string? NextCursor);
