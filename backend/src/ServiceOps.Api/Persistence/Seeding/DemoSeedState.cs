namespace ServiceOps.Api.Persistence.Seeding;

// One marker for the installed portfolio dataset; never used by normal business operations.
public sealed class DemoSeedState
{
    public string Version { get; set; } = "";
    public DateTimeOffset AnchorUtc { get; set; }
}
