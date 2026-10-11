namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-002
// Scoped, reference-counted circuit UI state prevents overlapping operations from hiding loading feedback early.
public sealed class GlobalLoadingService
{
    private readonly object _gate = new();
    private int _operations;
    public bool IsBusy { get { lock (_gate) return _operations > 0; } }
    public string Message { get { lock (_gate) return _message; } }
    private string _message = "Đang tải...";
    public event Action? Changed;

    public IDisposable Begin(string message = "Đang xử lý...")
    {
        // FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
        // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-002
        // Increment only this scoped circuit's counter and publish the latest operation message.
        lock (_gate) { _operations++; _message = string.IsNullOrWhiteSpace(message) ? "Đang xử lý..." : message; }
        Changed?.Invoke();
        return new Scope(this);
    }

    private void End()
    {
        // FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
        // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-002
        // Idempotent scopes cannot drive the reference count below zero.
        lock (_gate) { if (_operations > 0) _operations--; }
        Changed?.Invoke();
    }

    private sealed class Scope(GlobalLoadingService owner) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.End(); }
    }
}