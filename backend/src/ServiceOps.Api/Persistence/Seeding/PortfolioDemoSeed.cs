using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Persistence.Seeding;

public static class PortfolioDemoSeed
{
    public const string Version = "portfolio-v1";
    public static readonly DateTimeOffset DefaultAnchor = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(ServiceOpsDbContext database, IConfiguration configuration)
    {
        var anchor = DefaultAnchor;
        var configured = configuration["DemoSeed:AnchorUtc"];
        if (!string.IsNullOrWhiteSpace(configured) &&
            !DateTimeOffset.TryParseExact(configured, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out anchor))
            throw new InvalidOperationException("Use DemoSeed__AnchorUtc in UTC, for example 2026-10-01T18:00:00Z.");

        await using var transaction = await database.Database.BeginTransactionAsync();
        var installed = await database.DemoSeedStates.SingleOrDefaultAsync();
        if (installed is not null)
        {
            if (installed.Version != Version || (!string.IsNullOrWhiteSpace(configured) && installed.AnchorUtc != anchor))
                throw new InvalidOperationException("A different demo dataset is already installed. Existing data was preserved; use a fresh database for another version or anchor.");
            Console.WriteLine($"{installed.Version} already installed at {installed.AnchorUtc:O}; all work orders and edits preserved.");
            return;
        }
        if (await database.WorkOrders.AnyAsync())
            throw new InvalidOperationException("--seed-demo requires an empty work-order database on its first run. Existing orders were preserved; use a fresh database.");

        var locations = await database.Locations.Include(x => x.Customer).OrderBy(x => x.Id).ToArrayAsync();
        var technicians = await database.Technicians.OrderBy(x => x.Id).ToArrayAsync();
        var operations = await database.Users.Where(x => x.Email == "elena.brooks@atlas.example").Select(x => x.Id).SingleAsync();
        var manager = await database.Users.Where(x => x.Email == "marcus.chen@atlas.example").Select(x => x.Id).SingleAsync();
        if (locations.Length != 50 || technicians.Length != 15 || locations.Any(x => !x.IsActive || !x.Customer.IsActive) || technicians.Any(x => !x.IsActive))
            throw new InvalidOperationException("The portfolio dataset requires the original active reference seed (50 locations and 15 technicians).");

        var random = new Random(20261001);
        var start = new DateTimeOffset(anchor.AddMonths(-6).UtcDateTime.Date, TimeSpan.Zero);
        var orders = new List<WorkOrder>();
        int[] technicianPool = [0, 0, 1, 1, 2, 2, 3, 3, 4, 5, 6, 6, 7, 8, 9, 10, 11, 12, 13, 14];
        string[] accessContext = ["Check in with the site supervisor before entering the service area.",
            "Reception can provide access; keep the adjacent occupied area clear while working.",
            "Coordinate the visit with the facilities contact to avoid interrupting scheduled deliveries.",
            "The duty manager has the service-area keys and can demonstrate the reported fault.",
            "Call the facilities contact on arrival and confirm the affected area before starting."];
        string[] holds = ["Waiting for the site contact to provide access to the affected area.",
            "Customer requested an access window after the occupied area closes.",
            "Replacement component is on order; the site contact has been advised of the delay.",
            "Awaiting approval of the proposed repair from the customer facilities manager."];
        string[] cancellations = ["Duplicate request; the original work order already covers this fault.",
            "Customer cancelled the request after reviewing the planned work.",
            "Site staff resolved the issue before technician dispatch.",
            "Request was entered for the wrong location; the facilities contact will submit a corrected request."];

        for (var i = 0; i < 450; i++)
        {
            var open = i >= 378;
            var openIndex = i - 378;
            var site = locations[random.NextDouble() < 0.35 ? random.Next(15) : random.Next(locations.Length)];
            var serviceRoll = random.Next(100);
            var service = serviceRoll < 34 ? ServiceType.HVAC : serviceRoll < 56 ? ServiceType.Electrical
                : serviceRoll < 76 ? ServiceType.Plumbing : serviceRoll < 90 ? ServiceType.Equipment : ServiceType.GeneralMaintenance;
            var priorityRoll = random.Next(100);
            var priority = priorityRoll < (open ? 7 : 3) ? Priority.Critical : priorityRoll < (open ? 35 : 25) ? Priority.High
                : priorityRoll < 83 ? Priority.Normal : Priority.Low;
            // Two durable risk examples for a review near the anchor, including a six-hour Low risk window.
            if (openIndex == 1) priority = Priority.Normal;
            if (openIndex == 3) priority = Priority.Low;
            var choices = DemoIssueCatalog.Issues.Where(x => x.Service == service && x.Priority == priority &&
                (x.CustomerCodes is null || x.CustomerCodes.Contains(site.Customer.Code))).ToArray();
            if (open)
            {
                var unused = choices.Where(x => !orders.Any(o => o.LocationId == site.Id && o.Title == x.Title &&
                    o.Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Cancelled))).ToArray();
                if (unused.Length > 0) choices = unused;
            }
            var issue = choices[random.Next(choices.Length)];
            var duration = WorkOrder.SlaDurationFor(priority);
            DateTimeOffset created;
            WorkOrderStatus status;
            if (open)
            {
                var statusRoll = random.Next(100);
                status = openIndex < 4 ? (WorkOrderStatus)openIndex : statusRoll < 25 ? WorkOrderStatus.New
                    : statusRoll < 51 ? WorkOrderStatus.Assigned : statusRoll < 84 ? WorkOrderStatus.InProgress : WorkOrderStatus.OnHold;
                var slaRoll = random.Next(100);
                var age = openIndex is 1 or 3 ? duration * 0.82 : slaRoll < 40 ? duration * (0.12 + random.NextDouble() * 0.45)
                    : slaRoll < 64 ? duration * (0.79 + random.NextDouble() * 0.12) : duration * (1.15 + random.NextDouble() * 3);
                if (openIndex == 4) age = 12 * 24 * 60; // Older backlog remains visible to future current-operations metrics.
                created = anchor.AddMinutes(-Math.Round(age));
            }
            else
            {
                created = start.AddDays(random.Next((anchor.AddDays(-1) - start).Days)).AddHours(random.Next(11, 22)).AddMinutes(random.Next(60));
                // Commercial work is busier on weekdays, but genuine weekend calls remain.
                if (created.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && random.Next(100) < 70)
                    created = created.AddDays(created.DayOfWeek == DayOfWeek.Saturday ? -1 : -2);
                if (created < start) created = start.AddHours(14);
                status = i < 360 ? WorkOrderStatus.Completed : WorkOrderStatus.Cancelled;
            }

            var time = new SeedTime(created);
            var creator = random.Next(100) < 12 ? manager : operations;
            var order = WorkOrder.Create(site.Customer, site, service, priority, issue.Title,
                $"{issue.Context} Reported at {site.Name}. {accessContext[random.Next(accessContext.Length)]}", creator, time);
            var technician = technicians[technicianPool[random.Next(technicianPool.Length)]];
            if (status == WorkOrderStatus.Cancelled)
            {
                var reason = random.Next(cancellations.Length);
                // Cancellation before dispatch, with a few already-assigned customer cancellations.
                if (reason == 1 && random.Next(2) == 0)
                {
                    time.Now = created.AddMinutes(12);
                    order.Assign(technician, operations, time);
                }
                time.Now = created.AddMinutes(random.Next(25, 160));
                order.Cancel(cancellations[reason], creator, time);
            }
            else if (status != WorkOrderStatus.New)
            {
                var missed = random.Next(100) < 24;
                var elapsed = open ? (anchor - created).TotalMinutes * (0.55 + random.NextDouble() * 0.3)
                    : Math.Min(duration * (missed ? 1.1 + random.NextDouble() * 2.4 : 0.3 + random.NextDouble() * 0.65), (anchor - created).TotalMinutes - 5);
                time.Now = created.AddMinutes(Math.Round(elapsed * 0.08));
                order.Assign(technician, operations, time);
                if (status != WorkOrderStatus.Assigned)
                {
                    time.Now = created.AddMinutes(Math.Round(elapsed * 0.22));
                    order.StartWork(operations, time);
                    if (status == WorkOrderStatus.OnHold || (status == WorkOrderStatus.Completed && random.Next(100) < (missed ? 55 : 12)))
                    {
                        time.Now = created.AddMinutes(Math.Round(elapsed * 0.42));
                        order.PlaceOnHold(holds[random.Next(holds.Length)], operations, time);
                        if (status == WorkOrderStatus.Completed)
                        {
                            time.Now = created.AddMinutes(Math.Round(elapsed * 0.78));
                            order.Resume(operations, time);
                        }
                    }
                    if (status == WorkOrderStatus.Completed)
                    {
                        time.Now = created.AddMinutes(Math.Round(elapsed));
                        order.Complete(issue.Resolution, random.Next(100) < 8 ? manager : operations, time);
                    }
                }
            }
            orders.Add(order);
        }

        // Save in chronological order so database-generated work-order numbers also repeat on a fresh DB.
        // All saves and the completion marker share this one transaction; a failure leaves no partial dataset.
        foreach (var order in orders.OrderBy(x => x.CreatedAt))
        {
            database.WorkOrders.Add(order);
            await database.SaveChangesAsync();
        }
        database.DemoSeedStates.Add(new DemoSeedState { Version = Version, AnchorUtc = anchor });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine($"Installed {Version}: 450 work orders, reference instant {anchor:O}. Live SLA states continue to age with server time.");
    }

    private sealed class SeedTime(DateTimeOffset instant) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = instant;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
