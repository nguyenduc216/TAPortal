extern alias admin;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using AdminControllers = admin::TAPortal.Web.Controllers;
using M = admin::TAPortal.Web.Models;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Ảnh đại diện nhân viên (operator 2026-11-14).
public sealed class SafeEmployeePhotoTests
{
    [Fact]
    public void OnlySmallJpegPngOrWebpImagesAreAccepted()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0];
        byte[] webp = [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WEBP"u8.ToArray()];
        Assert.Equal("image/jpeg", S.EmployeePhotoPolicy.Sniff(jpeg));
        Assert.Equal("image/png", S.EmployeePhotoPolicy.Sniff(png));
        Assert.Equal("image/webp", S.EmployeePhotoPolicy.Sniff(webp));
        Assert.Null(S.EmployeePhotoPolicy.Validate(jpeg));
        Assert.NotNull(S.EmployeePhotoPolicy.Validate("<svg onload=alert(1)>"u8.ToArray()));
        Assert.NotNull(S.EmployeePhotoPolicy.Validate([]));
        var big = new byte[S.EmployeePhotoPolicy.MaxBytes + 1]; big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        Assert.NotNull(S.EmployeePhotoPolicy.Validate(big));
    }

    [Fact]
    public void TheAvatarIsThePhotoWhenThereIsOneElseTheInitials()
    {
        var employee = Guid.NewGuid(); var user = Guid.NewGuid();
        var index = new S.AvatarIndex(new Dictionary<Guid, long> { [employee] = 42 }, new Dictionary<Guid, (Guid, long)> { [user] = (employee, 42) });
        Assert.Equal($"/Account/Avatar/{employee}?v=42", M.Avatar.Url(index, employee, null));
        Assert.Equal($"/Account/Avatar/{employee}?v=42", M.Avatar.Url(index, null, user));
        Assert.Null(M.Avatar.Url(index, Guid.NewGuid(), Guid.NewGuid()));
        using var w = new StringWriter();
        M.Avatar.Render(S.AvatarIndex.Empty, "Phan Thái Hà").WriteTo(w, System.Text.Encodings.Web.HtmlEncoder.Default);
        Assert.Contains(">H</span>", w.ToString());
        using var p = new StringWriter();
        M.Avatar.Render(index, "Phan Thái Hà", employeeId: employee).WriteTo(p, System.Text.Encodings.Web.HtmlEncoder.Default);
        Assert.Contains("<img src=\"/Account/Avatar/", p.ToString());
    }

    [Fact]
    public void SettingSomeoneElsesPhotoNeedsEmployeeUpdate()
    {
        var set = typeof(AdminControllers.AvatarController).GetMethod(nameof(AdminControllers.AvatarController.SetEmployee))!;
        Assert.Contains(set.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "PERM:HR.EMPLOYEE.UPDATE");
        Assert.NotNull(typeof(AdminControllers.AvatarController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void TheMigrationBoundsSizeAndType()
    {
        var root = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "051-employee-photos.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
        Assert.Contains("ByteSize <= 307200", sql);
        Assert.Contains("ContentType IN ('image/jpeg', 'image/png', 'image/webp')", sql);
    }
}
