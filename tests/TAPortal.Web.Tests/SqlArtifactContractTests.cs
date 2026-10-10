using Xunit;

namespace TAPortal.Web.Tests;

public sealed class SqlArtifactContractTests
{
    [Fact]
    public void Migration_GuardsTAPortalSysBeforeMutation()
    {
        var sql = ReadRepositoryFile("database", "migrations", "053-core-dynamic-action-authorization.sql");
        var guard = sql.IndexOf("IF DB_NAME() <> N'TAPortalSys'", StringComparison.Ordinal);
        var transaction = sql.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal);
        var create = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);

        Assert.True(guard>=0);
        Assert.True(transaction>guard);
        Assert.True(create>guard);
        Assert.Contains("THROW 52120, 'This migration must run against TAPortalSys.'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_IsTextOnlyAndHasFailingSummaryContract()
    {
        var sql = ReadRepositoryFile("database", "scripts", "verify", "053-verify-core-dynamic-action-authorization.sql");

        Assert.Contains("TAPortalSys AUTHORIZATION VERIFY SUMMARY", sql, StringComparison.Ordinal);
        Assert.Contains("OVERALL STATUS:", sql, StringComparison.Ordinal);
        Assert.Contains("RAISERROR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TAPortal.sln")))
            directory=directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
