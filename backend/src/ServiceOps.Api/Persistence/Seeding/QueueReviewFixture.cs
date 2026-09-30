using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Persistence.Seeding;

// Explicit, small development fixture. Never part of normal --seed or the future historical dataset.
public static class QueueReviewFixture
{
    public static async Task SeedAsync(ServiceOpsDbContext database, IConfiguration configuration)
    {
        if (!DateTimeOffset.TryParse(configuration["QueueFixture:AnchorUtc"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var anchor))
            throw new InvalidOperationException("Set QueueFixture__AnchorUtc to a fixed ISO timestamp for this review dataset.");
        if (await database.WorkOrders.AnyAsync())
            throw new InvalidOperationException("The queue review fixture requires an empty work-order database. Existing data was left unchanged.");
        var actor = await database.Users.Where(x => x.Email == "elena.brooks@atlas.example").Select(x => x.Id).SingleAsync();
        var locations = await database.Locations.Include(x => x.Customer).OrderBy(x => x.Id).Take(6).ToArrayAsync();
        if (locations.Length != 6) throw new InvalidOperationException("Run the reference/user seed first.");
        string[] issues = ["Cooling unit losing temperature", "Air handling unit rattling", "Heating unit not responding", "Emergency light flickering", "Washroom supply pipe leaking"];
        for (var i = 0; i < 60; i++)
        {
            var site = locations[i < 45 ? i % 3 : 3 + i % 3];
            var service = i % 5 < 3 ? ServiceType.HVAC : i % 5 == 3 ? ServiceType.Electrical : ServiceType.Plumbing;
            var order = WorkOrder.Create(site.Customer, site, service, (Priority)(i % 4),
                $"{issues[i % 5]} — zone {i + 1:D2}",
                $"Site staff at {site.Name} reported this issue during the routine inspection. Check in at reception before accessing the affected area.",
                actor, new FixtureTime(anchor.AddMinutes(-30 * i)));
            database.WorkOrders.Add(order);
        }
        await database.SaveChangesAsync();
    }

    private sealed class FixtureTime(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
