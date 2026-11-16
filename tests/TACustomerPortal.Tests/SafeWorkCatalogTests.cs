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

    [Fact]
    public void EachEntryCarriesAChecklistTemplate()
    {
        // Operator 2026-11-16: an entry's checklist fills a new task's checklist; a running task can add to it.
        Assert.Equal("Thu thập hóa đơn\nLập tờ khai", S.WorkCatalogPolicy.NormalizeChecklist("- Thu thập hóa đơn\n\n• Lập tờ khai\nthu thập hóa đơn"));
        Assert.Null(S.WorkCatalogPolicy.NormalizeChecklist("  \n "));
        var (steps, added) = S.WorkCatalogPolicy.Append([new("Thu thập hóa đơn", "Lấy trên cổng thuế")], ["THU THẬP HÓA ĐƠN", "Nộp tờ khai", " "]);
        Assert.Equal(1, added);
        Assert.Equal("Thu thập hóa đơn\nNộp tờ khai", S.WorkCatalogPolicy.ToText(steps));
        Assert.Equal("Lấy trên cổng thuế", steps[0].Guide);                                // existing explanation kept
        Assert.Contains("ChecklistText", S.WorkCatalogSql.Active);
        Assert.Contains("ChecklistText=@check", S.WorkCatalogSql.Update);
        Assert.Contains(typeof(AdminControllers.WorkCatalogController).GetMethod("FromTask")!.GetCustomAttributes<AuthorizeAttribute>(true), a => a.Policy == "PERM:TASKS.CATALOG.MANAGE");
        var root = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "056-work-catalog-checklist.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
        Assert.Contains("ADD ChecklistText nvarchar(4000) NULL", sql);
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "099-verify-work-catalog-checklist.sql")));
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "100-rollback-work-catalog-checklist.sql")));
    }

    [Fact]
    public void EachTemplateItemMayCarryAnExplanation()
    {
        // Operator 2026-11-16 ("gắn từng mục"): procedure / formula per checklist item, shown with that item on the tasks.
        var steps = S.WorkCatalogPolicy.CleanSteps([("Tính thuế GTGT", "Thuế phải nộp = đầu ra - đầu vào\r\nĐối chiếu tờ khai"), ("  ", null), ("tính thuế gtgt", "x"), ("Lưu hồ sơ", "  ")]);
        Assert.Equal(2, steps.Count);
        Assert.Equal("Thuế phải nộp = đầu ra - đầu vào\nĐối chiếu tờ khai", steps[0].Guide);
        Assert.Null(steps[1].Guide);
        var json = S.WorkCatalogPolicy.ToJson(steps)!;
        Assert.Contains("Tính thuế GTGT", json);                                                // readable, not \u-escaped
        Assert.Equal(steps, S.WorkCatalogPolicy.Steps(json, "ignored"));
        Assert.Equal([new S.WorkChecklistStep("A", null), new S.WorkChecklistStep("B", null)], S.WorkCatalogPolicy.Steps(null, "A\nB"));   // 056 rows
        Assert.Equal([new S.WorkChecklistStep("A", null)], S.WorkCatalogPolicy.Steps("not json", "A"));
        Assert.NotNull(S.WorkCatalogPolicy.Validate(new S.WorkCatalogInput { Name = "X", Items = [new() { Title = " ", Guide = "orphan" }] }));

        var index = new S.WorkGuideIndex(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kê khai thuế"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tính thuế GTGT"] = "công thức" }
        });
        Assert.Equal("công thức", index.For("Kê khai thuế", "tính thuế  GTGT"));
        Assert.Equal("công thức", index.For("Kê khai thuế — kỳ T10/2026", "Tính thuế GTGT"));      // recurring task name
        Assert.Null(index.For("Kê khai thuế", "Nộp tờ khai"));
        Assert.Contains("ChecklistJson=@json", S.WorkCatalogSql.Update);
        Assert.Contains("JOIN dbo.WorkTaskChecklistItems i ON i.TaskId=t.Id AND i.IsDeleted=0", S.WorkCatalogSql.List);   // items used by its tasks
        var entry = new S.WorkCatalogItem(Guid.NewGuid(), "Kê khai thuế", null, 0, true, 2, "Tính thuế GTGT") { Used = [new("tính thuế gtgt", 2), new("Nộp tờ khai", 1)] };
        Assert.Equal(["Nộp tờ khai"], entry.UsedNotInTemplate.Select(u => u.Title));

        var root = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "057-work-catalog-checklist-guides.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
        Assert.Contains("ADD ChecklistJson nvarchar(max) NULL", sql);
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "101-verify-work-catalog-checklist-guides.sql")));
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "102-rollback-work-catalog-checklist-guides.sql")));
    }
}
