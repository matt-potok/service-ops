using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Features.ReferenceData;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = "Operations")]
public sealed class ReferenceDataController(ServiceOpsDbContext database) : ControllerBase
{
    [HttpGet("technicians")]
    public Task<TechnicianOption[]> Technicians(CancellationToken cancellationToken) => database.Technicians.AsNoTracking()
        .OrderBy(x => x.DisplayName).Select(x => new TechnicianOption(x.Id, x.DisplayName, x.IsActive)).ToArrayAsync(cancellationToken);
    [HttpGet("customers")]
    public Task<CustomerOption[]> Customers(CancellationToken cancellationToken) => database.Customers.AsNoTracking()
        .Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new CustomerOption(x.Id, x.Name)).ToArrayAsync(cancellationToken);

    [HttpGet("customers/{id:guid}/locations")]
    public Task<LocationOption[]> Locations(Guid id, CancellationToken cancellationToken) => database.Locations.AsNoTracking()
        .Where(x => x.CustomerId == id && x.IsActive && x.Customer.IsActive).OrderBy(x => x.Name)
        .Select(x => new LocationOption(x.Id, x.Name, x.Address)).ToArrayAsync(cancellationToken);

    [HttpGet("reference-data")]
    public CreationOptions Options() => new(
        Enum.GetValues<ServiceType>().Select(x => new ServiceOption(x, x == ServiceType.GeneralMaintenance ? "General Maintenance" : x.ToString())).ToArray(),
        Enum.GetValues<Priority>().Select(x => new PriorityOption(x, x.ToString(), WorkOrder.SlaDurationFor(x))).ToArray());
}

public sealed record CustomerOption(Guid Id, string Name);
public sealed record TechnicianOption(Guid Id, string DisplayName, bool IsActive);
public sealed record LocationOption(Guid Id, string Name, string Address);
public sealed record ServiceOption(ServiceType Code, string Label);
public sealed record PriorityOption(Priority Code, string Label, int SlaDurationMinutes);
public sealed record CreationOptions(ServiceOption[] ServiceTypes, PriorityOption[] Priorities);
