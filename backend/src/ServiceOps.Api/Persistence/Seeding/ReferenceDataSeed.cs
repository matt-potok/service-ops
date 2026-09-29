using Microsoft.EntityFrameworkCore;
using ServiceOps.Domain.Customers;
using ServiceOps.Domain.Technicians;

namespace ServiceOps.Api.Persistence.Seeding;

public static class ReferenceDataSeed
{
    public static async Task SeedAsync(ServiceOpsDbContext database)
    {
        var customers = new (string Code, string Name, string City, string[] Sites)[]
        {
            ("HARBOR", "Harborstone Logistics", "Newark, NJ", ["North Distribution Center", "Portside Freight Terminal", "Meadowlands Warehouse"]),
            ("CEDAR", "Cedar Vale Offices", "Princeton, NJ", ["Willow Park Campus", "Nassau Business Center", "Riverside Executive Suites"]),
            ("WESTHAVEN", "Westhaven Retail Group", "Philadelphia, PA", ["Chestnut Square", "Market Street Arcade", "Westgate Shopping Center"]),
            ("ALDER", "Alderbrook Medical Properties", "Trenton, NJ", ["Mercer Medical Plaza", "Oak Lane Clinic", "Lakeside Outpatient Center"]),
            ("SUMMIT", "Summit Ridge Manufacturing", "Allentown, PA", ["Lehigh Assembly Plant", "Cedar Creek Workshop", "South Yard Storage"]),
            ("PINE", "Pinecrest Hospitality", "New Brunswick, NJ", ["The Juniper Hotel", "Garden Court Inn", "Parkview Conference Center"]),
            ("MERIDIAN", "Meridian Learning Centers", "Edison, NJ", ["Oakwood Learning Center", "Raritan Training Campus", "Maple Avenue Academy"]),
            ("BROOKFIELD", "Brookfield Food Markets", "Cherry Hill, NJ", ["Haddonfield Market", "Kingsway Fresh Foods", "Eastgate Grocery"]),
            ("IRONWOOD", "Ironwood Industrial Partners", "Bethlehem, PA", ["Steelworks Business Park", "Foundry Road Complex", "Canal Street Depot"]),
            ("FAIRWAY", "Fairway Professional Properties", "Morristown, NJ", ["Morris Corporate Center", "Elm Street Offices", "Spring Brook Plaza"]),
            ("NORTHLINE", "Northline Packaging", "Reading, PA", ["Reading Production Center", "Schuylkill Warehouse"]),
            ("BAYVIEW", "Bayview Senior Living", "Toms River, NJ", ["Silver Bay Residence", "Seabrook Community Center"]),
            ("CLEARWATER", "Clearwater Fitness Clubs", "Doylestown, PA", ["Central Bucks Fitness", "Pine Run Athletic Club"]),
            ("GRANITE", "Granite Point Commercial", "Parsippany, NJ", ["Waterview Office Center", "Troy Hills Plaza"]),
            ("LANTERN", "Lantern House Hospitality", "Lancaster, PA", ["The Millstone Lodge", "Willow Creek Events"]),
            ("RED OAK", "Red Oak Laboratory Services", "Somerset, NJ", ["Franklin Research Campus", "Davidson Testing Center"]),
            ("WINDWARD", "Windward Distribution", "Wilmington, DE", ["Christina River Hub", "Brandywine Fulfillment Center"]),
            ("MEADOW", "Meadowlark Property Group", "Harrisburg, PA", ["Susquehanna Business Center", "Capitol Crossing Offices"]),
            ("FERN", "Fernwood Community Facilities", "Montclair, NJ", ["Glenridge Community Hall", "Crescent Recreation Center"]),
            ("ASHFORD", "Ashford Automotive Group", "West Chester, PA", ["Chester Valley Service Center", "Goshen Parts Depot"])
        };
        var technicians = new (string Name, string Email)[]
        {
            ("Daniel Ortiz", "daniel.ortiz"), ("Priya Shah", "priya.shah"), ("Owen Mitchell", "owen.mitchell"),
            ("Nadia Flores", "nadia.flores"), ("Caleb Foster", "caleb.foster"), ("Sofia Bennett", "sofia.bennett"),
            ("Julian Reed", "julian.reed"), ("Amara Collins", "amara.collins"), ("Ethan Park", "ethan.park"),
            ("Leah Morgan", "leah.morgan"), ("Isaac Turner", "isaac.turner"), ("Maya Sullivan", "maya.sullivan"),
            ("Noah Patel", "noah.patel"), ("Grace Kim", "grace.kim"), ("Adrian Wallace", "adrian.wallace")
        };
        var streets = new[] { "Commerce Drive", "Industrial Way", "Park Avenue" };
        var customerIds = (await database.Customers.Select(x => x.Id).ToListAsync()).ToHashSet();
        var locationIds = (await database.Locations.Select(x => x.Id).ToListAsync()).ToHashSet();
        var technicianIds = (await database.Technicians.Select(x => x.Id).ToListAsync()).ToHashSet();
        var locationIndex = 0;
        for (var i = 0; i < customers.Length; i++)
        {
            var item = customers[i];
            var customerId = SeedId(1, i);
            if (!customerIds.Contains(customerId)) database.Customers.Add(new Customer(customerId, item.Code, item.Name));
            for (var site = 0; site < item.Sites.Length; site++)
            {
                var locationId = SeedId(2, locationIndex++);
                if (!locationIds.Contains(locationId)) database.Locations.Add(new Location(locationId, customerId,
                    $"{item.Code}-{site + 1:D2}", item.Sites[site], $"{120 + i * 17 + site * 40} {streets[site]}, {item.City}"));
            }
        }
        for (var i = 0; i < technicians.Length; i++)
            if (!technicianIds.Contains(SeedId(3, i)))
                database.Technicians.Add(new Technician(SeedId(3, i), technicians[i].Name, $"{technicians[i].Email}@atlas.example"));

        // One save is transactional. Existing records are left untouched on reruns.
        await database.SaveChangesAsync();
    }

    private static Guid SeedId(int kind, int index) => Guid.Parse($"0000000{kind}-0000-0000-0000-{index + 1:D12}");
}
