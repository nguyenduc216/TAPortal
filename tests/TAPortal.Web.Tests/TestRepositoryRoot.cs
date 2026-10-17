namespace TAPortal.Web.Tests;

internal static class TestRepositoryRoot
{
    internal static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("TAPORTAL_REPOSITORY_ROOT");
        var directory = new DirectoryInfo(configured ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TAPortal.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "TAPortal.Web", "TAPortal.Web.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException($"TAPortal repository root not found from {(configured is null ? "test output directory" : "TAPORTAL_REPOSITORY_ROOT")}. Set TAPORTAL_REPOSITORY_ROOT when using an external BaseOutputPath.");
    }

    internal static string FilePath(params string[] parts) => Path.Combine(new[] { Resolve() }.Concat(parts).ToArray());
}