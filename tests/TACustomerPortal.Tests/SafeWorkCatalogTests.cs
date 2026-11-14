extern alias admin;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using AdminControllers = admin::TAPortal.Web.Controllers;
using AdminSecurity = admin::TAPortal.Web.Security;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Danh mục công việc (operator 2026-11-14): task names come from a managed list so the same work has the same name.
public sealed class SafeWorkCatalogTests
{
    [Fact]
    public void NamesAreTrimmedAndBounded()
    {
        Assert.Equal("Kéo hóa đơn", S.WorkCatalogPolicy.Clean("  Kéo   hóa\tđơn "));
        Assert.NotNull(S.WorkCatalogPolicy.Validate(new S.WorkCatalogInput { Name = "   " }));
        Assert.NotNull(S.WorkCatalogPolicy.Validate(new S.WorkCatalogInput { Name = new string('a', 201) }));
        Assert.NotNull(S.WorkCatalogPolicy.Validate(new S.WorkCatalogInput { Name = "Kéo hóa đơn", Description = new string('a', 501) }));
        Assert.Null(S.WorkCatalogPolicy.Validate(new S.WorkCatalogInput { Name = "Kéo hóa đơn" }));
    }

    [Fact]
    public void ANameOutsideTheCatalogueIsRefusedOnceTheCatalogueHasEntries()
    {
        // '' = catalogue empty or not installed (free names); the catalogue spelling when found; NULL otherwise.
        Assert.Contains("IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL SELECT CAST(N'' AS nvarchar(200))", S.WorkCatalogSql.Resolve);
        Assert.Contains("ELSE IF NOT EXISTS (SELECT 1 FROM dbo.WorkTaskCatalog WHERE IsDeleted=0 AND IsActive=1) SELECT CAST(N'' AS nvarchar(200))", S.WorkCatalogSql.Resolve);
        Assert.Contains("SELECT TOP 1 Name FROM dbo.WorkTaskCatalog WHERE IsDeleted=0 AND IsActive=1 AND Name=@title", S.WorkCatalogSql.Resolve);
        Assert.Contains(S.WorkCatalogSql.Active, S.WorkTaskSql.Lookups);
        Assert.Contains("Danh mục công việc", S.WorkCatalogPolicy.NotInCatalog);
    }

    [Fact]
    public void RenamingAnEntryRenamesItsTasks()
    {
        Assert.Contains("UPDATE dbo.WorkTasks SET Title=@name", S.WorkCatalogSql.Rename);
        Assert.Contains("UPDATE dbo.WorkTaskTemplates SET Title=@name", S.WorkCatalogSql.Rename);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK) WHERE IsDeleted=0 AND Name=@name", S.WorkCatalogSql.Taken);
    }

    [Fact]
    public void OnlyCatalogueManagersEditTheList()
    {
        Assert.Contains("TASKS.CATALOG.MANAGE", AdminSecurity.AdminPermissions.Codes);
        Assert.Equal(["TASKS.CATALOG.MANAGE"], AdminSecurity.AdminPermissions.Migration043Codes);
        Assert.DoesNotContain("TASKS.CATALOG.MANAGE", AdminSecurity.AdminPermissions.Migration031Codes);
        Assert.Contains(typeof(AdminControllers.WorkCatalogController).GetCustomAttributes<AuthorizeAttribute>(true), a => a.Policy == "PERM:TASKS.CATALOG.MANAGE");
    }

    [Fact]
    public void TheMigrationCreatesTheCatalogueWithUniqueNames()
    {
        var root = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "043-work-task-catalog.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_WorkTaskCatalog_Name ON dbo.WorkTaskCatalog(Name) WHERE IsDeleted = 0", sql);
        Assert.Contains("'TASKS.CATALOG.MANAGE'", sql);
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "075-verify-work-task-catalog.sql")));
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "076-rollback-work-task-catalog.sql")));
    }
}
