using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class BlazorFoundationTests
{
    [Fact]
    public void GlobalLoadingService_OverlappingScopesRemainBusyUntilAllComplete()
    {
        var loading = new GlobalLoadingService();
        using var first = loading.Begin("Đang lưu dữ liệu...");
        using var second = loading.Begin("Đang cập nhật dữ liệu...");

        Assert.True(loading.IsBusy);
        second.Dispose();
        Assert.True(loading.IsBusy);
        first.Dispose();
        Assert.False(loading.IsBusy);
    }

    [Fact]
    public void GlobalLoadingService_DisposingScopeTwiceDoesNotUnderflow()
    {
        var loading = new GlobalLoadingService();
        var scope = loading.Begin();

        scope.Dispose();
        scope.Dispose();

        Assert.False(loading.IsBusy);
    }

    [Fact]
    public void GlobalLoadingService_EmptyMessageUsesSafeDefault()
    {
        var loading = new GlobalLoadingService();
        using var scope = loading.Begin(" ");

        Assert.Equal("Đang xử lý...", loading.Message);
    }

    [Fact]
    public void GlobalLoadingService_ExceptionAndConcurrentDisposalCleanUp()
    {
        var loading=new GlobalLoadingService();
        Action failingOperation=()=>{ using var scope=loading.Begin("Failure"); throw new InvalidOperationException(); };
        Assert.Throws<InvalidOperationException>(failingOperation);
        Assert.False(loading.IsBusy);
        Parallel.For(0,100,_=>{ using var scope=loading.Begin("Concurrent"); });
        Assert.False(loading.IsBusy);
    }
}
