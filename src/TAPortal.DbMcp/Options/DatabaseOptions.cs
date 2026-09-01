namespace TAPortal.DbMcp.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? ConnectionString { get; init; }
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int MaxRows { get; init; } = 500;
}
