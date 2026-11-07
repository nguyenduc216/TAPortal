extern alias admin;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdminModels = admin::TAPortal.Web.Models;
using AdminSecurity = admin::TAPortal.Web.Security;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// HR-EMPLOYEES: Admin → Nhân sự (employees, login accounts, departments, teams, job titles). No SQL host or connection:
// the employee and catalogue stores are in-memory fakes applying the same EmployeePolicy / HrCatalogPolicy rules as the
// SQL stores. Real SQL (locks, RowVersion, filtered unique indexes) is NOT exercised here.
public sealed class SafeAdminEmployeeTests
{
    private const string TestScheme = "SafeEmployeeTest";
    private static readonly Guid Actor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid B1 = Guid.Parse("b1000000-0000-0000-0000-000000000001"), B2 = Guid.Parse("b2000000-0000-0000-0000-000000000002");
    private static readonly Guid D1 = Guid.Parse("d1000000-0000-0000-0000-000000000001"), D2 = Guid.Parse("d2000000-0000-0000-0000-000000000002");
    private static readonly Guid T1 = Guid.Parse("71000000-0000-0000-0000-000000000001"), J1 = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid StActive = Guid.Parse("5a000000-0000-0000-0000-000000000001"), StResigned = Guid.Parse("5a000000-0000-0000-0000-000000000005");
    private static readonly Guid FullTime = Guid.Parse("e7000000-0000-0000-0000-000000000001");
    private static readonly Guid E1 = Guid.Parse("e1000000-0000-0000-0000-000000000001"), E2 = Guid.Parse("e2000000-0000-0000-0000-000000000002");
    private static readonly Guid U1 = Guid.Parse("c1000000-0000-0000-0000-000000000001"), U2 = Guid.Parse("c2000000-0000-0000-0000-000000000002");
    private static readonly Guid SysAdminRole = Guid.Parse("cccccccc-0000-0000-0000-000000000001"), EditorRole = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private const string SecretIdentity = "079123456789";

    // ---- Catalogue and SQL guards --------------------------------------------------------------------------------
    [Fact]
    public void HrPermissionsAreCataloguedWithoutMigration()
    {
        // Rows that exist in production dbo.Permissions (HR core bootstrap); EXPORT, documents and specializations are not used.
        var used = new[] { "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.CREATE", "HR.EMPLOYEE.UPDATE", "HR.EMPLOYEE.DELETE", "HR.EMPLOYEE.CHANGE_STATUS", "HR.EMPLOYEE.CHANGE_ASSIGNMENT",
            "HR.EMPLOYEE.ACCOUNT_LINK", "HR.EMPLOYEE.VIEW_PRIVATE", "HR.EMPLOYEE.UPDATE_PRIVATE", "HR.DEPARTMENT.VIEW", "HR.DEPARTMENT.CREATE", "HR.DEPARTMENT.UPDATE",
            "HR.DEPARTMENT.DELETE", "HR.TEAM.VIEW", "HR.TEAM.CREATE", "HR.TEAM.UPDATE", "HR.TEAM.DELETE", "HR.JOB_TITLE.VIEW", "HR.JOB_TITLE.CREATE", "HR.JOB_TITLE.UPDATE", "HR.JOB_TITLE.DELETE" };
        var hr = AdminSecurity.AdminPermissions.Modules.Single(m => m.Key == "HR");
        Assert.Equal("Nhân sự", hr.Name);
        Assert.Equal(used.Order(StringComparer.Ordinal), hr.Permissions.Select(p => p.Code).Order(StringComparer.Ordinal));
        Assert.All(used, c => Assert.DoesNotContain(c, AdminSecurity.AdminPermissions.MigrationCodes));
        Assert.False(AdminSecurity.AdminPermissions.IsCatalogued("HR.EMPLOYEE.EXPORT"));
        Assert.False(AdminSecurity.AdminPermissions.IsCatalogued("HR.SPECIALIZATION.VIEW"));
    }

    [Fact]
    public void EmployeeSqlKeepsPrivateColumnsOutOfListsAndGuardsWrites()
    {
        foreach (var column in S.EmployeePrivateInput.FieldNames)
        {
            Assert.DoesNotContain(column, S.EmployeeSql.List);
            Assert.DoesNotContain(column, S.EmployeeSql.Employee);
            Assert.DoesNotContain(column, S.EmployeeSql.EmployeeForUpdate);
            Assert.Contains(column, S.EmployeeSql.Private);
        }
        Assert.Contains("ESCAPE '\\'", S.EmployeeSql.List);
        Assert.Contains("RowVersion=@rv", S.EmployeeSql.UpdateBasic);
        Assert.Contains("RowVersion=@rv", S.EmployeeSql.UpdateStatus);
        Assert.Contains("UserId IS NULL", S.EmployeeSql.SoftDelete);                   // never deletes a linked employee
        Assert.Contains("UserId IS NULL", S.EmployeeSql.Link);
        Assert.Contains("UserId=@u", S.EmployeeSql.Unlink);
        Assert.Contains("IsCurrent=0,ValidTo=@to", S.EmployeeSql.CloseAssignment);
        Assert.Contains("1,1", S.EmployeeSql.InsertAssignment);                         // IsPrimary=1, IsCurrent=1
        Assert.Contains("dbo.EmployeeStatusHistories", S.EmployeeSql.InsertStatusHistory);
        Assert.Contains("CurrentValue<@n", S.EmployeeSql.SequenceAdvance);              // the sequence never moves back
        var all = string.Join("\n", typeof(S.EmployeeSql).GetFields().Select(f => (string)f.GetValue(null)!))
            + string.Join("\n", typeof(S.HrCatalogSql).GetFields().Select(f => (string)f.GetValue(null)!));
        Assert.DoesNotContain("STRING_AGG", all);
        Assert.DoesNotContain("DELETE FROM", all);                                       // soft delete only
        Assert.DoesNotMatch(@"\bOFFSET\b|\bFETCH\b|CREATE OR ALTER|DROP\s", all);
        Assert.Contains("IsActive=1 AND a.DepartmentId=x.Id", S.HrCatalogSql.Departments);
        Assert.Contains("t.DepartmentId=x.Id AND t.IsDeleted=0", S.HrCatalogSql.Departments);
        Assert.DoesNotContain("BranchId", S.HrCatalogSql.DepartmentCodeTaken);       // company-wide unique code
        Assert.Contains("BranchId=@branch", S.HrCatalogSql.TeamCodeTaken);
        Assert.Contains("r.Code<>'SYS_ADMIN' OR EXISTS", S.PortalDb.CreateUserRoleSql);   // account creation reuses the SYS_ADMIN re-check
    }

    // ---- Policies ----------------------------------------------------------------------------------------------
    [Fact]
    public void AssignmentRulesAndPlan()
    {
        var l = Lookups();
        Assert.True(S.EmployeePolicy.AssignmentIsValid(l, B1, D1, T1, J1));
        Assert.True(S.EmployeePolicy.AssignmentIsValid(l, B2, D2, null, null));
        Assert.False(S.EmployeePolicy.AssignmentIsValid(l, null, null, null, null));          // branch required
        Assert.False(S.EmployeePolicy.AssignmentIsValid(l, B2, D1, null, null));              // department of another branch
        Assert.False(S.EmployeePolicy.AssignmentIsValid(l, B2, D2, T1, null));                // team of another branch
        Assert.False(S.EmployeePolicy.AssignmentIsValid(l, B1, null, null, Guid.NewGuid()));  // unknown job title

        var current = new S.EmployeeAssignmentView(Guid.NewGuid(), B1, D1, null, null, new DateTime(2026, 1, 1), null);
        S.EmployeeAssignmentInput Move(Guid b, Guid? d, DateTime? from) => new() { BranchId = b, DepartmentId = d, ValidFrom = from };
        Assert.Equal(S.AssignmentPlanKind.Insert, S.EmployeePolicy.PlanAssignment(null, Move(B1, D1, new DateTime(2026, 1, 1))));
        Assert.Equal(S.AssignmentPlanKind.NoChange, S.EmployeePolicy.PlanAssignment(current, Move(B1, D1, new DateTime(2026, 5, 1))));
        Assert.Equal(S.AssignmentPlanKind.CloseAndInsert, S.EmployeePolicy.PlanAssignment(current, Move(B2, D2, new DateTime(2026, 5, 1))));
        Assert.Equal(S.AssignmentPlanKind.UpdateInPlace, S.EmployeePolicy.PlanAssignment(current, Move(B2, D2, new DateTime(2026, 1, 1))));
        Assert.Equal(S.AssignmentPlanKind.InvalidDate, S.EmployeePolicy.PlanAssignment(current, Move(B2, D2, new DateTime(2025, 12, 31))));
        Assert.Equal(S.AssignmentPlanKind.InvalidDate, S.EmployeePolicy.PlanAssignment(current, Move(B2, D2, null)));
    }

    [Fact]
    public void CodeSequenceAndRoleRules()
    {
        Assert.Equal("NV000001", S.EmployeePolicy.NextCode("NV", null, 6, 0, []));
        Assert.Equal("NV000003", S.EmployeePolicy.NextCode("NV", null, 6, 0, ["NV000001", "nv000002"]));
        Assert.Equal(7, S.EmployeePolicy.SequenceNumber("NV000007", "NV", null));
        Assert.Null(S.EmployeePolicy.SequenceNumber("TA_E1", "NV", null));
        Assert.True(S.EmployeePolicy.IsValidCode("NV000001"));
        Assert.False(S.EmployeePolicy.IsValidCode("NV 01"));
        Assert.True(S.EmployeePolicy.IsLeaving("RESIGNED"));
        Assert.False(S.EmployeePolicy.IsLeaving("MATERNITY"));

        var roles = Roles();
        Assert.Equal(S.EmployeeOutcome.Saved, S.EmployeePolicy.EvaluateRoles(roles, [EditorRole], false));
        Assert.Equal(S.EmployeeOutcome.RoleForbidden, S.EmployeePolicy.EvaluateRoles(roles, [SysAdminRole], false));
        Assert.Equal(S.EmployeeOutcome.Saved, S.EmployeePolicy.EvaluateRoles(roles, [SysAdminRole, EditorRole], true));
        Assert.Equal(S.EmployeeOutcome.InvalidRole, S.EmployeePolicy.EvaluateRoles(roles, [Guid.NewGuid()], true));
    }

    // ---- List, create, edit ------------------------------------------------------------------------------------
    [Fact]
    public async Task ListSearchesAndFilters()
    {
        var store = new MemoryEmployees(false);
        using var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW");
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/employees"));
        Assert.Contains("TA_E1", html);
        Assert.Contains("Trần Thị Bình", html);
        Assert.Contains("an.nguyen", html);                                  // linked account
        Assert.DoesNotContain("data-ta-employee-create", html);              // no HR.EMPLOYEE.CREATE
        Assert.DoesNotContain(SecretIdentity, html);
        var search = WebUtility.HtmlDecode(await c.GetStringAsync("/employees?q=binh%40ta.vn"));
        Assert.DoesNotContain("data-ta-employee=\"TA_E1\"", search);
        Assert.Contains("data-ta-employee=\"TA_E2\"", search);
        var unlinked = WebUtility.HtmlDecode(await c.GetStringAsync("/employees?account=unlinked"));
        Assert.DoesNotContain("data-ta-employee=\"TA_E1\"", unlinked);
        Assert.Contains("data-ta-employee=\"TA_E2\"", unlinked);
        var branch2 = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees?branchId={B2}"));
        Assert.Contains("data-ta-employee=\"TA_E2\"", branch2);
        Assert.DoesNotContain("data-ta-employee=\"TA_E1\"", branch2);
    }

    [Fact]
    public async Task CreateEmployeeWithInitialAssignmentAndStatusHistory()
    {
        var store = new MemoryEmployees(false);
        using var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.CREATE");
        var c = Client(f);
        var form = WebUtility.HtmlDecode(await c.GetStringAsync("/employees/create"));
        Assert.Contains("value=\"NV000001\"", form);                         // suggested from NumberSequences EMPLOYEE_CODE
        Assert.DoesNotContain("data-ta-private", form);                     // no private permissions
        var token = Token(form);

        var bad = await c.PostAsync("/employees/create", Form(token, ("Input.Code", "NV000001"), ("Input.FullName", "Lê Văn Cường"), ("Input.StatusId", StActive.ToString()),
            ("Input.BranchId", B2.ToString()), ("Input.DepartmentId", D1.ToString())));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);              // department of another branch
        Assert.Equal(0, store.Writes);
        var dup = await c.PostAsync("/employees/create", Form(token, ("Input.Code", "ta_e1"), ("Input.FullName", "X"), ("Input.StatusId", StActive.ToString()), ("Input.BranchId", B1.ToString())));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("Mã nhân viên đã tồn tại.", WebUtility.HtmlDecode(await dup.Content.ReadAsStringAsync()));

        var ok = await c.PostAsync("/employees/create", Form(token, ("Input.Code", "nv000001"), ("Input.FullName", "Lê Văn Cường"), ("Input.Gender", "MALE"),
            ("Input.StatusId", StActive.ToString()), ("Input.BranchId", B1.ToString()), ("Input.DepartmentId", D1.ToString()), ("Input.TeamId", T1.ToString()),
            ("Input.JobTitleId", J1.ToString()), ("Input.StartDate", "2026-11-02"), ("Input.CompanyEmail", "cuong@ta.vn"), ("Input.Private.IdentityNumber", "FORGED")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var created = store.ByCode("NV000001")!;
        Assert.EndsWith($"/Employees/Details/{created.Id}", ok.Headers.Location!.OriginalString);
        Assert.Equal((B1, (Guid?)D1, (Guid?)T1, (Guid?)J1, new DateTime(2026, 11, 2)), (created.Assignment!.BranchId, created.Assignment.DepartmentId, created.Assignment.TeamId, created.Assignment.JobTitleId, created.Assignment.ValidFrom));
        Assert.Single(created.StatusHistory);
        Assert.Null(store.PrivateOf(created.Id).IdentityNumber);              // private field dropped without permission
        Assert.False(store.LastCreatePrivate);
    }

    [Fact]
    public async Task EditKeepsCodeAndPrivateFieldsWithoutPrivatePermission()
    {
        var store = new MemoryEmployees(false);
        using var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.UPDATE");
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/edit/{E2}"));
        Assert.DoesNotContain(SecretIdentity, page);
        Assert.DoesNotContain("Input.Private", page);
        Assert.False(store.PrivateReads.Last());                             // private columns not even read
        var stamp = Regex.Match(page, "name=\"Input.Stamp\" value=\"([^\"]+)\"").Groups[1].Value;
        var saved = await c.PostAsync($"/employees/edit/{E2}", Form(Token(page), ("Input.Stamp", stamp), ("Input.Code", "HACKED"), ("Input.FullName", "Trần Thị Bình An"),
            ("Input.MobilePhone", "0909000000"), ("Input.Private.IdentityNumber", "HACKED"), ("Input.StatusId", StResigned.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var e = store.Get(E2)!;
        Assert.Equal("TA_E2", e.Code);
        Assert.Equal("Trần Thị Bình An", e.FullName);
        Assert.Equal(StActive, e.StatusId);                                  // status only changes through ChangeStatus
        Assert.Equal(SecretIdentity, store.PrivateOf(E2).IdentityNumber);

        var stale = await c.PostAsync($"/employees/edit/{E2}", Form(Token(page), ("Input.Stamp", stamp), ("Input.FullName", "Khác")));
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);           // RowVersion conflict
        Assert.Equal("Trần Thị Bình An", store.Get(E2)!.FullName);
    }

    [Fact]
    public async Task PrivateFieldsNeedTheirOwnPermissions()
    {
        var store = new MemoryEmployees(false);
        using (var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW"))
        {
            var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/employees/details/{E2}"));
            Assert.DoesNotContain(SecretIdentity, html);
            Assert.DoesNotContain("data-ta-private", html);
            Assert.False(store.PrivateReads.Last());
        }
        using (var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.VIEW_PRIVATE"))
        {
            var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/employees/details/{E2}"));
            Assert.Contains(SecretIdentity, html);
            Assert.Contains("data-ta-private", html);
        }
        using (var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.UPDATE", "HR.EMPLOYEE.VIEW_PRIVATE", "HR.EMPLOYEE.UPDATE_PRIVATE"))
        {
            var c = Client(f);
            var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/edit/{E2}"));
            Assert.Contains("name=\"Input.Private.IdentityNumber\" value=\"" + SecretIdentity + "\"", page);
            var stamp = Regex.Match(page, "name=\"Input.Stamp\" value=\"([^\"]+)\"").Groups[1].Value;
            var saved = await c.PostAsync($"/employees/edit/{E2}", Form(Token(page), ("Input.Stamp", stamp), ("Input.FullName", "Trần Thị Bình"), ("Input.Private.IdentityNumber", "079000000001"),
                ("Input.Private.PersonalTaxCode", "8000000001")));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            Assert.Equal("079000000001", store.PrivateOf(E2).IdentityNumber);
            Assert.Equal("8000000001", store.PrivateOf(E2).PersonalTaxCode);
        }
    }

    // ---- Assignment and status ---------------------------------------------------------------------------------
    [Fact]
    public async Task AssignmentChangeClosesTheCurrentRowAndStatusChangeWritesHistory()
    {
        var store = new MemoryEmployees(false);
        using var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.CHANGE_ASSIGNMENT", "HR.EMPLOYEE.CHANGE_STATUS");
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E1}"));
        Assert.Contains("data-ta-assignment-form", page);
        Assert.Contains("data-ta-status-form", page);
        Assert.DoesNotContain("data-ta-account-create", page);              // no account permissions
        var stamp = Stamp(page);
        var moved = await c.PostAsync($"/employees/changeassignment/{E1}", Form(Token(page), ("Stamp", stamp), ("BranchId", B2.ToString()), ("DepartmentId", D2.ToString()), ("ValidFrom", "2026-11-10")));
        Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);
        var e = store.Get(E1)!;
        Assert.Equal((B2, (Guid?)D2, new DateTime(2026, 11, 10)), (e.Assignment!.BranchId, e.Assignment.DepartmentId, e.Assignment.ValidFrom));
        Assert.Contains(e.AssignmentHistory, h => !h.IsCurrent && h.ValidTo == new DateTime(2026, 11, 9));

        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E1}"));
        var badTeam = await c.PostAsync($"/employees/changeassignment/{E1}", Form(Token(page), ("Stamp", Stamp(page)), ("BranchId", B2.ToString()), ("TeamId", T1.ToString()), ("ValidFrom", "2026-11-12")));
        Assert.Equal(HttpStatusCode.Redirect, badTeam.StatusCode);
        Assert.Null(store.Get(E1)!.Assignment!.TeamId);                       // team of another branch refused

        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E1}"));
        var resigned = await c.PostAsync($"/employees/changestatus/{E1}", Form(Token(page), ("Stamp", Stamp(page)), ("StatusId", StResigned.ToString()), ("EffectiveDate", "2026-11-30"), ("Reason", "Nghỉ việc")));
        Assert.Equal(HttpStatusCode.Redirect, resigned.StatusCode);
        e = store.Get(E1)!;
        Assert.Equal(StResigned, e.StatusId);
        Assert.False(e.IsActive);
        Assert.Equal(2, e.StatusHistory.Count);
        var after = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E1}"));
        Assert.Contains("vẫn đang hoạt động", after);                         // warns that the linked login account is still active
    }

    // ---- Login account ----------------------------------------------------------------------------------------
    [Fact]
    public async Task CreateLinkAndUnlinkAnAccount()
    {
        var store = new MemoryEmployees(false);
        using var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.ACCOUNT_LINK", "AUTH.USERS.CREATE", "HR.EMPLOYEE.DELETE");
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        Assert.Contains("data-ta-account-create", page);
        Assert.Contains("value=\"binh@ta.vn\"", page);                       // e-mail defaults to the company e-mail
        Assert.DoesNotContain($"name=\"RoleIds\" value=\"{SysAdminRole}\"", page); // SYS_ADMIN not offered to a non-SYS_ADMIN
        Assert.Contains($"name=\"RoleIds\" value=\"{EditorRole}\"", page);

        var weak = await c.PostAsync($"/employees/createaccount/{E2}", Form(Token(page), ("Stamp", Stamp(page)), ("Username", "binh.tran2"), ("Password", "short")));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("ít nhất 8 ký tự", WebUtility.HtmlDecode(await weak.Content.ReadAsStringAsync()));

        var forged = await c.PostAsync($"/employees/createaccount/{E2}", Form(Token(page), ("Stamp", Stamp(page)), ("Username", "binh.tran2"), ("Password", "Secret#2026"), ("RoleIds", SysAdminRole.ToString())));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Contains("Chỉ quản trị hệ thống (SYS_ADMIN) mới được gán vai trò Quản trị hệ thống.", WebUtility.HtmlDecode(await forged.Content.ReadAsStringAsync()));
        Assert.Null(store.Get(E2)!.Account);

        var created = await c.PostAsync($"/employees/createaccount/{E2}", Form(Token(page), ("Stamp", Stamp(page)), ("Username", "binh.tran2"), ("Email", "binh@ta.vn"),
            ("Password", "Secret#2026"), ("RoleIds", EditorRole.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var account = store.Get(E2)!.Account!;
        Assert.Equal("binh.tran2", account.Username);
        Assert.Equal("Trần Thị Bình", account.DisplayName);                  // display name = full name
        Assert.Equal(new[] { "Kế toán" }, account.RoleNames);
        Assert.True(store.LastPasswordHash!.Length > 20 && !store.LastPasswordHash.Contains("Secret#2026"));

        // Deleting is refused while linked; unlinking keeps the account; then deletion works.
        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        Assert.Contains("data-ta-linked-username", page);
        var refused = await c.PostAsync($"/employees/delete/{E2}", Form(Token(page), ("stamp", Stamp(page))));
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.NotNull(store.Get(E2));
        var unlinked = await c.PostAsync($"/employees/unlinkaccount/{E2}", Form(Token(page), ("stamp", Stamp(page))));
        Assert.Equal(HttpStatusCode.Redirect, unlinked.StatusCode);
        Assert.Null(store.Get(E2)!.Account);
        Assert.Contains(account.Id, store.Users.Keys);                        // the account itself still exists

        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        Assert.Contains($"value=\"{U2}\"", page);                             // unlinked accounts are offered
        Assert.DoesNotContain($"value=\"{U1}\"", page);                       // linked to E1
        var linked = await c.PostAsync($"/employees/linkaccount/{E2}", Form(Token(page), ("stamp", Stamp(page)), ("userId", U2.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, linked.StatusCode);
        Assert.Equal(U2, store.Get(E2)!.Account!.Id);
        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        var taken = await c.PostAsync($"/employees/linkaccount/{E1}", Form(Token(page), ("stamp", store.Get(E1)!.Stamp), ("userId", U2.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, taken.StatusCode);
        Assert.Equal(U1, store.Get(E1)!.Account!.Id);                          // E1 already linked: unchanged

        await c.PostAsync($"/employees/unlinkaccount/{E2}", Form(Token(page), ("stamp", Stamp(page))));
        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        var deleted = await c.PostAsync($"/employees/delete/{E2}", Form(Token(page), ("stamp", Stamp(page))));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.EndsWith("/Employees", deleted.Headers.Location!.OriginalString);
        Assert.Null(store.Get(E2));
    }

    [Fact]
    public async Task OnlySystemAdminGrantsSystemAdminToANewEmployeeAccount()
    {
        var store = new MemoryEmployees(true);
        using var f = Factory("SYS_ADMIN", store, new MemoryCatalog());
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        Assert.Contains($"name=\"RoleIds\" value=\"{SysAdminRole}\"", page);
        var created = await c.PostAsync($"/employees/createaccount/{E2}", Form(Token(page), ("Stamp", Stamp(page)), ("Username", "binh.admin"), ("Password", "Secret#2026"), ("RoleIds", SysAdminRole.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(new[] { "Quản trị hệ thống" }, store.Get(E2)!.Account!.RoleNames);
    }

    // ---- Catalogues -------------------------------------------------------------------------------------------
    [Fact]
    public async Task CatalogueCrudAndDeleteRefusal()
    {
        var catalog = new MemoryCatalog();
        using var f = Factory("STAFF", new MemoryEmployees(false), catalog, "HR.DEPARTMENT.VIEW", "HR.DEPARTMENT.CREATE", "HR.DEPARTMENT.UPDATE", "HR.DEPARTMENT.DELETE",
            "HR.TEAM.VIEW", "HR.TEAM.CREATE", "HR.JOB_TITLE.VIEW", "HR.JOB_TITLE.CREATE");
        var c = Client(f);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/departments"));
        Assert.Contains("data-ta-catalog=\"KT\"", list);
        var form = WebUtility.HtmlDecode(await c.GetStringAsync("/departments/create"));
        var created = await c.PostAsync("/departments/create", Form(Token(form), ("Input.BranchId", B2.ToString()), ("Input.Code", "hcns"), ("Input.Name", "Hành chính nhân sự"), ("Input.IsActive", "true")));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var hcns = catalog.ByCode(S.HrCatalogKind.Department, "HCNS")!;
        Assert.Equal(B2, hcns.BranchId);
        var dup = await c.PostAsync("/departments/create", Form(Token(form), ("Input.BranchId", B2.ToString()), ("Input.Code", "HCNS"), ("Input.Name", "x")));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var otherBranch = await c.PostAsync("/departments/create", Form(Token(form), ("Input.BranchId", B2.ToString()), ("Input.Code", "KT"), ("Input.Name", "x")));
        Assert.Equal(HttpStatusCode.BadRequest, otherBranch.StatusCode);        // department codes are unique company-wide (UX_Departments_Code_NotDeleted)
        Assert.Contains("Mã phòng ban đã tồn tại.", WebUtility.HtmlDecode(await otherBranch.Content.ReadAsStringAsync()));

        var edit = WebUtility.HtmlDecode(await c.GetStringAsync($"/departments/edit/{hcns.Id}"));
        var saved = await c.PostAsync($"/departments/edit/{hcns.Id}", Form(Token(edit), ("Input.Stamp", hcns.Stamp), ("Input.Code", "HACK"), ("Input.BranchId", B1.ToString()),
            ("Input.Name", "Hành chính"), ("Input.IsActive", "true")));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        hcns = catalog.ByCode(S.HrCatalogKind.Department, "HCNS")!;           // code and branch are fixed
        Assert.Equal(("Hành chính", B2), (hcns.Name, hcns.BranchId!.Value));

        // D1 has an active employee and an active team: soft delete refused; the new empty department can be deleted.
        var d1 = catalog.Get(S.HrCatalogKind.Department, D1)!;
        var refused = await c.PostAsync($"/departments/delete/{D1}", Form(Token(edit), ("stamp", d1.Stamp)));
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.NotNull(catalog.Get(S.HrCatalogKind.Department, D1));
        var deleted = await c.PostAsync($"/departments/delete/{hcns.Id}", Form(Token(edit), ("stamp", hcns.Stamp)));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Null(catalog.Get(S.HrCatalogKind.Department, hcns.Id));

        form = WebUtility.HtmlDecode(await c.GetStringAsync("/teams/create"));
        var wrongBranch = await c.PostAsync("/teams/create", Form(Token(form), ("Input.BranchId", B2.ToString()), ("Input.DepartmentId", D1.ToString()), ("Input.Code", "T9"), ("Input.Name", "Nhóm 9")));
        Assert.Equal(HttpStatusCode.BadRequest, wrongBranch.StatusCode);
        var team = await c.PostAsync("/teams/create", Form(Token(form), ("Input.BranchId", B1.ToString()), ("Input.DepartmentId", D1.ToString()), ("Input.Code", "T9"), ("Input.Name", "Nhóm 9")));
        Assert.Equal(HttpStatusCode.Redirect, team.StatusCode);
        form = WebUtility.HtmlDecode(await c.GetStringAsync("/jobtitles/create"));
        var job = await c.PostAsync("/jobtitles/create", Form(Token(form), ("Input.Code", "TRUONG_PHONG"), ("Input.Name", "Trưởng phòng"), ("Input.Level", "3"), ("Input.IsManagement", "true")));
        Assert.Equal(HttpStatusCode.Redirect, job.StatusCode);
        Assert.True(catalog.ByCode(S.HrCatalogKind.JobTitle, "TRUONG_PHONG")!.IsManagement);

        // Without HR.TEAM.UPDATE the team page is read-only.
        var teamPage = WebUtility.HtmlDecode(await c.GetStringAsync($"/teams/edit/{T1}"));
        Assert.DoesNotContain("type=\"submit\"", teamPage[teamPage.IndexOf("data-ta-catalog-form", StringComparison.Ordinal)..]);
    }

    // ---- Authorization and menu -------------------------------------------------------------------------------
    [Theory]
    [InlineData("GET", "/employees/create")]
    [InlineData("GET", "/employees/edit/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/create")]
    [InlineData("POST", "/employees/edit/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/changestatus/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/changeassignment/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/createaccount/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/linkaccount/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/unlinkaccount/e2000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/employees/delete/e2000000-0000-0000-0000-000000000002")]
    [InlineData("GET", "/departments")]
    [InlineData("GET", "/teams")]
    [InlineData("GET", "/jobtitles")]
    [InlineData("POST", "/departments/create")]
    [InlineData("POST", "/teams/edit/71000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/jobtitles/delete/a1000000-0000-0000-0000-000000000001")]
    public async Task UngrantedHrActionsAreForbidden(string method, string path)
    {
        var store = new MemoryEmployees(false);
        var catalog = new MemoryCatalog();
        using var f = Factory("STAFF", store, catalog, "HR.EMPLOYEE.VIEW");
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? new FormUrlEncodedContent([new("Input.Name", "x")]) : null };
        var response = await Client(f).SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, store.Writes + catalog.Writes);
    }

    [Fact]
    public async Task AccountCreationAlsoNeedsUserCreatePermission()
    {
        var store = new MemoryEmployees(false);
        using var f = Factory("STAFF", store, new MemoryCatalog(), "HR.EMPLOYEE.VIEW", "HR.EMPLOYEE.ACCOUNT_LINK");
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/employees/details/{E2}"));
        Assert.DoesNotContain("data-ta-account-create", page);
        Assert.Contains("data-ta-account-link", page);
        var response = await c.PostAsync($"/employees/createaccount/{E2}", Form(Token(page), ("Stamp", Stamp(page)), ("Username", "x.y"), ("Password", "Secret#2026")));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, store.Writes);
        var noView = Factory("STAFF", store, new MemoryCatalog(), "AUTH.ROLES.VIEW");
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(noView).GetAsync("/employees")).StatusCode);
        noView.Dispose();
    }

    [Fact]
    public async Task SidebarShowsOnlyGrantedHrItems()
    {
        using (var f = Factory("STAFF", new MemoryEmployees(false), new MemoryCatalog(), "HR.EMPLOYEE.VIEW"))
        {
            var nav = Nav(await Client(f).GetStringAsync("/employees"));
            Assert.Contains("Nhân sự", nav);
            Assert.Matches("aria-current=\"page\"[^>]*href=\"/Employees\"", nav);
            foreach (var hidden in new[] { "/Departments", "/Teams", "/JobTitles", "/Users", "/Roles" }) Assert.DoesNotContain(hidden, nav);
        }
        using (var f = Factory("STAFF", new MemoryEmployees(false), new MemoryCatalog(), "HR.TEAM.VIEW"))
        {
            var nav = Nav(await Client(f).GetStringAsync("/teams"));
            Assert.Contains("href=\"/Teams\"", nav);
            Assert.DoesNotContain("href=\"/Employees\"", nav);
        }
        using (var f = Factory("STAFF", new MemoryEmployees(false), new MemoryCatalog(), "AUTH.ROLES.VIEW"))
        {
            var nav = Nav(await Client(f).GetStringAsync("/account/accessdenied"));
            Assert.DoesNotContain("Nhân sự", nav);                            // empty group heading is not rendered
        }
        using (var f = Factory("SYS_ADMIN", new MemoryEmployees(true), new MemoryCatalog()))
        {
            var nav = Nav(await Client(f).GetStringAsync("/jobtitles"));
            foreach (var link in new[] { "href=\"/Employees\"", "href=\"/Departments\"", "href=\"/Teams\"", "href=\"/JobTitles\"" }) Assert.Contains(link, nav);
        }
        Assert.Equal("Employees", AdminSecurity.AdminNavigation.FirstAllowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "HR.EMPLOYEE.VIEW")], "t")))!.Controller);
    }

    [Fact]
    public void UsersListShowsTheLinkedEmployee()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TAPortal.sln"))) root = root.Parent!;
        var view = File.ReadAllText(Path.Combine(root.FullName, "src", "TAPortal.Web", "Views", "Users", "Index.cshtml"));
        Assert.Contains("ViewData[\"EmployeeMap\"]", view);
        Assert.Contains("asp-controller=\"Employees\" asp-action=\"Details\"", view);
        Assert.Contains("AdminPermissions.EmployeesView", view);              // the link only for HR.EMPLOYEE.VIEW
    }

    // ---- Host and helpers --------------------------------------------------------------------------------------
    private static string Nav(string html)
    {
        html = WebUtility.HtmlDecode(html);
        var start = html.IndexOf("id=\"taNav\"", StringComparison.Ordinal);
        return html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static string Stamp(string html) => Regex.Match(html, "name=\"[Ss]tamp\" value=\"([^\"]+)\"").Groups[1].Value;

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new([new("__RequestVerificationToken", token), .. fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))]);

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string role, MemoryEmployees employees, MemoryCatalog catalog, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.IEmployeeStore>();
                s.AddSingleton<S.IEmployeeStore>(employees);
                s.RemoveAll<S.IHrCatalogStore>();
                s.AddSingleton<S.IHrCatalogStore>(catalog);
                s.AddSingleton(new TestPrincipal(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private static S.EmployeeLookups Lookups() => new(
        [new(B1, "HN", "Chi nhánh Hà Nội", "T.A"), new(B2, "HCM", "Chi nhánh Hồ Chí Minh", "T.A")],
        [new(D1, B1, "KT", "Kế toán"), new(D2, B2, "KD", "Kinh doanh")],
        [new(T1, B1, D1, "T1", "Nhóm thuế")],
        [new(J1, "NV", "Nhân viên")],
        [new(StActive, "ACTIVE", "Đang làm việc"), new(Guid.Parse("5a000000-0000-0000-0000-000000000002"), "PROBATION", "Thử việc"), new(StResigned, "RESIGNED", "Đã nghỉ việc")],
        [new(FullTime, "FULL_TIME", "Chính thức")]);

    private static List<AdminModels.PortalRole> Roles() => [new(SysAdminRole, "SYS_ADMIN", "Quản trị hệ thống", null, true, true), new(EditorRole, "EDITOR", "Kế toán", null, false, true)];

    // In-memory employees applying EmployeePolicy like SqlEmployeeStore. The actor is treated as holding the permission
    // of each call (the SQL store re-checks it in the transaction); actorIsSystemAdmin stands for ActorPermissionSql.IsSystemAdmin.
    private sealed class MemoryEmployees(bool actorIsSystemAdmin) : S.IEmployeeStore
    {
        private readonly object gate = new();
        private readonly S.EmployeeLookups lookups = Lookups();
        private readonly Dictionary<Guid, S.EmployeeDetail> rows = new();
        private readonly Dictionary<Guid, S.EmployeePrivateInput> privates = new();
        public Dictionary<Guid, (string Username, string DisplayName, string? Email, List<Guid> Roles)> Users { get; } = new()
        {
            [U1] = ("an.nguyen", "Nguyễn Văn An", "an@ta.vn", [EditorRole]),
            [U2] = ("binh.tran", "Trần Thị Bình", "binh.personal@ta.vn", [])
        };
        public int Writes { get; private set; }
        public List<bool> PrivateReads { get; } = new();
        public bool LastCreatePrivate { get; private set; }
        public string? LastPasswordHash { get; private set; }

        private bool seeded;
        private void Seed()
        {
            if (seeded) return;
            seeded = true;
            rows[E1] = Detail(E1, "TA_E1", "Nguyễn Văn An", "an@ta.vn", new(Guid.NewGuid(), B1, D1, T1, J1, new DateTime(2026, 1, 1), null), U1);
            rows[E2] = Detail(E2, "TA_E2", "Trần Thị Bình", "binh@ta.vn", new(Guid.NewGuid(), B2, D2, null, null, new DateTime(2026, 2, 1), null), null);
            privates[E1] = new();
            privates[E2] = new() { IdentityNumber = SecretIdentity, PermanentAddress = "Số 1 Đường A" };
        }

        private S.EmployeeDetail Detail(Guid id, string code, string name, string email, S.EmployeeAssignmentView a, Guid? user) =>
            new(id, code, name, "FEMALE", null, null, null, null, email, a.ValidFrom, null, FullTime, StActive, "ACTIVE", "Đang làm việc", true, null, a, null,
                user is Guid u ? Account(u) : null, NewStamp(), [new(a.ValidFrom, null, "Đang làm việc", "Tạo hồ sơ nhân viên", null, "Admin", DateTime.UtcNow)],
                [new(a.ValidFrom, null, true, lookups.BranchName(a.BranchId), lookups.DepartmentName(a.DepartmentId), lookups.TeamName(a.TeamId), lookups.JobTitleName(a.JobTitleId))]);

        private S.LinkedAccount Account(Guid u) => new(u, Users[u].Username, Users[u].DisplayName, Users[u].Email, true,
            Users[u].Roles.Select(r => Roles().First(x => x.Id == r).Name).ToList());

        private static string NewStamp() => Guid.NewGuid().ToString("N").ToUpperInvariant();

        public S.EmployeeDetail? Get(Guid id) { lock (gate) { Seed(); return rows.GetValueOrDefault(id); } }
        public S.EmployeeDetail? ByCode(string code) { lock (gate) { Seed(); return rows.Values.FirstOrDefault(r => r.Code == code); } }
        public S.EmployeePrivateInput PrivateOf(Guid id) { lock (gate) { Seed(); return privates[id]; } }

        public Task<S.EmployeeLookups> LookupsAsync() => Task.FromResult(lookups);

        public Task<IReadOnlyList<S.EmployeeListItem>> ListAsync(S.EmployeeFilter filter)
        {
            lock (gate)
            {
                Seed();
                IReadOnlyList<S.EmployeeListItem> list = rows.Values.Select(e => new S.EmployeeListItem(e.Id, e.Code, e.FullName, e.Assignment?.BranchId, lookups.BranchName(e.Assignment?.BranchId),
                    e.Assignment?.DepartmentId, lookups.DepartmentName(e.Assignment?.DepartmentId), lookups.TeamName(e.Assignment?.TeamId), lookups.JobTitleName(e.Assignment?.JobTitleId),
                    e.MobilePhone, e.CompanyPhone, e.CompanyEmail, e.StatusId, e.StatusCode, e.StatusName, e.Account?.Id, e.Account?.Username, e.Account?.LoginEligible == true, e.PersonalEmail))
                    .Where(filter.Matches).OrderBy(e => e.FullName).ToList();
                return Task.FromResult(list);
            }
        }

        public Task<S.EmployeeDetail?> GetAsync(Guid id, bool includePrivate)
        {
            lock (gate)
            {
                Seed();
                PrivateReads.Add(includePrivate);
                return Task.FromResult(rows.TryGetValue(id, out var e) ? e with { Private = includePrivate ? privates[id] : null } : null);
            }
        }

        public Task<string?> SuggestCodeAsync() { lock (gate) { Seed(); return Task.FromResult(S.EmployeePolicy.NextCode("NV", null, 6, 0, rows.Values.Select(r => r.Code))); } }

        public Task<S.EmployeeResult> CreateAsync(Guid actorId, S.EmployeeInput input, string? ip)
        {
            lock (gate)
            {
                Seed();
                LastCreatePrivate = input.Private is not null;
                var code = S.EmployeePolicy.NormalizeCode(input.Code);
                var status = lookups.Statuses.FirstOrDefault(s => s.Id == input.StatusId);
                if (status is null || !S.EmployeePolicy.AssignmentIsValid(lookups, input.BranchId, input.DepartmentId, input.TeamId, input.JobTitleId)) return R(S.EmployeeOutcome.InvalidReference);
                if (rows.Values.Any(r => r.Code == code)) return R(S.EmployeeOutcome.DuplicateCode);
                var id = Guid.NewGuid();
                var from = (input.StartDate ?? DateTime.Today).Date;
                var a = new S.EmployeeAssignmentView(Guid.NewGuid(), input.BranchId!.Value, input.DepartmentId, input.TeamId, input.JobTitleId, from, null);
                rows[id] = Detail(id, code, input.FullName.Trim(), input.CompanyEmail ?? "", a, null) with { StatusId = status.Id, StatusCode = status.Code, StatusName = status.Name };
                privates[id] = input.Private?.Normalized() ?? new();
                Writes++;
                return R(S.EmployeeOutcome.Saved, id);
            }
        }

        public Task<S.EmployeeResult> UpdateAsync(Guid actorId, Guid id, S.EmployeeInput input, string? ip) => Change(id, input.Stamp, e =>
        {
            if (input.Private is { } p) privates[id] = p.Normalized();
            return e with { FullName = input.FullName.Trim(), Gender = S.EmployeePolicy.Clean(input.Gender), MobilePhone = S.EmployeePolicy.Clean(input.MobilePhone), CompanyEmail = S.EmployeePolicy.Clean(input.CompanyEmail) };
        });

        public Task<S.EmployeeResult> ChangeAssignmentAsync(Guid actorId, Guid id, S.EmployeeAssignmentInput input, string? ip) => Change(id, input.Stamp, e =>
        {
            if (!S.EmployeePolicy.AssignmentIsValid(lookups, input.BranchId, input.DepartmentId, input.TeamId, input.JobTitleId, e.Assignment)) throw new Refused(S.EmployeeOutcome.InvalidReference);
            var plan = S.EmployeePolicy.PlanAssignment(e.Assignment, input);
            if (plan == S.AssignmentPlanKind.NoChange) throw new Refused(S.EmployeeOutcome.NoChange);
            if (plan == S.AssignmentPlanKind.InvalidDate) throw new Refused(S.EmployeeOutcome.InvalidDate);
            var from = input.ValidFrom!.Value.Date;
            var history = e.AssignmentHistory.Select(h => h.IsCurrent && plan == S.AssignmentPlanKind.CloseAndInsert ? h with { IsCurrent = false, ValidTo = from.AddDays(-1) } : h)
                .Where(h => !(h.IsCurrent && plan == S.AssignmentPlanKind.UpdateInPlace)).ToList();
            history.Insert(0, new(from, null, true, lookups.BranchName(input.BranchId), lookups.DepartmentName(input.DepartmentId), lookups.TeamName(input.TeamId), lookups.JobTitleName(input.JobTitleId)));
            return e with { Assignment = new(Guid.NewGuid(), input.BranchId!.Value, input.DepartmentId, input.TeamId, input.JobTitleId, from, input.Note), AssignmentHistory = history };
        });

        public Task<S.EmployeeResult> ChangeStatusAsync(Guid actorId, Guid id, S.EmployeeStatusInput input, string? ip) => Change(id, input.Stamp, e =>
        {
            var status = lookups.Statuses.FirstOrDefault(s => s.Id == input.StatusId) ?? throw new Refused(S.EmployeeOutcome.InvalidReference);
            if (status.Id == e.StatusId) throw new Refused(S.EmployeeOutcome.NoChange);
            var history = e.StatusHistory.Prepend(new(input.EffectiveDate!.Value, e.StatusName, status.Name, input.Reason, input.Note, "Tester", DateTime.UtcNow)).ToList();
            return e with { StatusId = status.Id, StatusCode = status.Code, StatusName = status.Name, IsActive = !S.EmployeePolicy.IsLeaving(status.Code), StatusHistory = history };
        });

        public Task<S.EmployeeResult> DeleteAsync(Guid actorId, Guid id, string? stamp, string? ip)
        {
            lock (gate)
            {
                Seed();
                if (!rows.TryGetValue(id, out var e)) return R(S.EmployeeOutcome.NotFound);
                var outcome = S.EmployeePolicy.EvaluateDelete(e, stamp);
                if (outcome != S.EmployeeOutcome.Saved) return R(outcome, id);
                rows.Remove(id);
                Writes++;
                return R(outcome, id);
            }
        }

        public Task<S.EmployeeAccountOptions> AccountOptionsAsync()
        {
            lock (gate)
            {
                Seed();
                var linked = rows.Values.Where(r => r.Account is not null).Select(r => r.Account!.Id).ToHashSet();
                return Task.FromResult(new S.EmployeeAccountOptions(Roles(), Users.Where(u => !linked.Contains(u.Key)).Select(u => new S.EmployeeAccountCandidate(u.Key, u.Value.Username, u.Value.DisplayName, u.Value.Email, true)).ToList()));
            }
        }

        public Task<S.EmployeeResult> CreateAccountAsync(Guid actorId, Guid id, S.EmployeeAccountInput input, string passwordHash, string? ip) => Change(id, input.Stamp, e =>
        {
            if (e.Account is not null) throw new Refused(S.EmployeeOutcome.AlreadyLinked);
            if (Users.Values.Any(u => string.Equals(u.Username, input.Username?.Trim(), StringComparison.OrdinalIgnoreCase))) throw new Refused(S.EmployeeOutcome.UsernameTaken);
            var roles = (input.RoleIds ?? []).Distinct().ToList();
            var outcome = S.EmployeePolicy.EvaluateRoles(Roles(), roles, actorIsSystemAdmin);
            if (outcome != S.EmployeeOutcome.Saved) throw new Refused(outcome);
            var userId = Guid.NewGuid();
            Users[userId] = (input.Username!.Trim(), e.FullName, input.Email, roles);
            LastPasswordHash = passwordHash;
            return e with { Account = Account(userId) };
        });

        public Task<S.EmployeeResult> LinkAccountAsync(Guid actorId, Guid id, Guid userId, string? stamp, string? ip) => Change(id, stamp, e =>
        {
            if (e.Account is not null) throw new Refused(S.EmployeeOutcome.AlreadyLinked);
            if (!Users.ContainsKey(userId)) throw new Refused(S.EmployeeOutcome.InvalidReference);
            if (rows.Values.Any(r => r.Account?.Id == userId)) throw new Refused(S.EmployeeOutcome.AccountTaken);
            return e with { Account = Account(userId) };
        });

        public Task<S.EmployeeResult> UnlinkAccountAsync(Guid actorId, Guid id, string? stamp, string? ip) => Change(id, stamp, e =>
            e.Account is null ? throw new Refused(S.EmployeeOutcome.NotLinked) : e with { Account = null });

        public Task<IReadOnlyDictionary<Guid, S.LinkedEmployee>> LinkedEmployeesAsync()
        {
            lock (gate)
            {
                Seed();
                IReadOnlyDictionary<Guid, S.LinkedEmployee> map = rows.Values.Where(r => r.Account is not null).ToDictionary(r => r.Account!.Id, r => new S.LinkedEmployee(r.Id, r.Code, r.FullName));
                return Task.FromResult(map);
            }
        }

        private Task<S.EmployeeResult> Change(Guid id, string? stamp, Func<S.EmployeeDetail, S.EmployeeDetail> apply)
        {
            lock (gate)
            {
                Seed();
                if (!rows.TryGetValue(id, out var e)) return R(S.EmployeeOutcome.NotFound);
                if (e.Stamp != stamp) return R(S.EmployeeOutcome.Conflict, id);
                try { rows[id] = apply(e) with { Stamp = NewStamp() }; }
                catch (Refused r) { return R(r.Outcome, id); }
                Writes++;
                return R(S.EmployeeOutcome.Saved, id);
            }
        }

        private static Task<S.EmployeeResult> R(S.EmployeeOutcome outcome, Guid? id = null) => Task.FromResult(new S.EmployeeResult(outcome, id));

        private sealed class Refused(S.EmployeeOutcome outcome) : Exception
        {
            public S.EmployeeOutcome Outcome { get; } = outcome;
        }
    }

    // In-memory catalogue applying HrCatalogPolicy like SqlHrCatalogStore. D1 has one active employee and one active team (T1).
    private sealed class MemoryCatalog : S.IHrCatalogStore
    {
        private readonly object gate = new();
        private readonly S.EmployeeLookups lookups = Lookups();
        private readonly List<S.HrCatalogItem> items;
        public int Writes { get; private set; }

        public MemoryCatalog()
        {
            items =
            [
                new(D1, S.HrCatalogKind.Department, "KT", "Kế toán", null, 0, true, B1, "Chi nhánh Hà Nội", null, null, null, false, 1, 1, "S1"),
                new(D2, S.HrCatalogKind.Department, "KD", "Kinh doanh", null, 0, true, B2, "Chi nhánh Hồ Chí Minh", null, null, null, false, 1, 0, "S2"),
                new(T1, S.HrCatalogKind.Team, "T1", "Nhóm thuế", null, 0, true, B1, "Chi nhánh Hà Nội", D1, "Kế toán", null, false, 1, 0, "S3"),
                new(J1, S.HrCatalogKind.JobTitle, "NV", "Nhân viên", null, 0, true, null, null, null, null, 1, false, 1, 0, "S4")
            ];
        }

        public S.HrCatalogItem? Get(S.HrCatalogKind kind, Guid id) { lock (gate) return items.FirstOrDefault(i => i.Kind == kind && i.Id == id); }
        public S.HrCatalogItem? ByCode(S.HrCatalogKind kind, string code) { lock (gate) return items.FirstOrDefault(i => i.Kind == kind && i.Code == code); }

        public Task<IReadOnlyList<S.HrCatalogItem>> ListAsync(S.HrCatalogKind kind) { lock (gate) return Task.FromResult<IReadOnlyList<S.HrCatalogItem>>(items.Where(i => i.Kind == kind).ToList()); }
        public Task<S.HrCatalogItem?> GetAsync(S.HrCatalogKind kind, Guid id) => Task.FromResult(Get(kind, id));
        public Task<IReadOnlyList<S.BranchOption>> BranchesAsync() => Task.FromResult(lookups.Branches);
        public Task<IReadOnlyList<S.DepartmentOption>> DepartmentsAsync()
        {
            lock (gate) return Task.FromResult<IReadOnlyList<S.DepartmentOption>>(items.Where(i => i.Kind == S.HrCatalogKind.Department).Select(i => new S.DepartmentOption(i.Id, i.BranchId!.Value, i.Code, i.Name, i.IsActive)).ToList());
        }

        public async Task<S.HrCatalogResult> CreateAsync(S.HrCatalogKind kind, Guid actorId, S.HrCatalogInput input, string? ip)
        {
            var departments = await DepartmentsAsync();
            lock (gate)
            {
                var code = S.HrCatalogPolicy.NormalizeCode(input.Code);
                if (!S.HrCatalogPolicy.IsValidCode(kind, code)) return new(S.HrCatalogOutcome.InvalidCode);
                if (!S.HrCatalogPolicy.ReferencesAreValid(kind, input, lookups.Branches, departments)) return new(S.HrCatalogOutcome.InvalidReference);
                if (items.Any(i => i.Kind == kind && i.Code == code && (kind != S.HrCatalogKind.Team || i.BranchId == input.BranchId))) return new(S.HrCatalogOutcome.DuplicateCode);
                var id = Guid.NewGuid();
                items.Add(new(id, kind, code, input.Name.Trim(), input.Description, input.SortOrder, input.IsActive, kind == S.HrCatalogKind.JobTitle ? null : input.BranchId,
                    lookups.BranchName(input.BranchId), input.DepartmentId, lookups.DepartmentName(input.DepartmentId), input.Level, input.IsManagement, 0, 0, Guid.NewGuid().ToString("N")));
                Writes++;
                return new(S.HrCatalogOutcome.Saved, id);
            }
        }

        public Task<S.HrCatalogResult> UpdateAsync(S.HrCatalogKind kind, Guid actorId, Guid id, S.HrCatalogInput input, string? ip)
        {
            lock (gate)
            {
                var current = items.FirstOrDefault(i => i.Kind == kind && i.Id == id);
                if (current is null) return Task.FromResult(new S.HrCatalogResult(S.HrCatalogOutcome.NotFound));
                if (current.Stamp != input.Stamp) return Task.FromResult(new S.HrCatalogResult(S.HrCatalogOutcome.Conflict, id));
                input.BranchId = current.BranchId;
                if (S.HrCatalogPolicy.Unchanged(current, input)) return Task.FromResult(new S.HrCatalogResult(S.HrCatalogOutcome.NoChange, id));
                items[items.IndexOf(current)] = current with { Name = input.Name.Trim(), Description = input.Description, SortOrder = input.SortOrder, IsActive = input.IsActive,
                    DepartmentId = kind == S.HrCatalogKind.Team ? input.DepartmentId : current.DepartmentId, Level = input.Level, IsManagement = input.IsManagement, Stamp = Guid.NewGuid().ToString("N") };
                Writes++;
                return Task.FromResult(new S.HrCatalogResult(S.HrCatalogOutcome.Saved, id));
            }
        }

        public Task<S.HrCatalogResult> DeleteAsync(S.HrCatalogKind kind, Guid actorId, Guid id, string? stamp, string? ip)
        {
            lock (gate)
            {
                var current = items.FirstOrDefault(i => i.Kind == kind && i.Id == id);
                if (current is null) return Task.FromResult(new S.HrCatalogResult(S.HrCatalogOutcome.NotFound));
                var outcome = S.HrCatalogPolicy.EvaluateDelete(current, stamp);
                if (outcome == S.HrCatalogOutcome.Saved) { items.Remove(current); Writes++; }
                return Task.FromResult(new S.HrCatalogResult(outcome, id));
            }
        }
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-employee-test"), new("display_name", "Nhân sự"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
