using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UsageViewer;

public sealed class UsageViewerController : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private const int ConcurrentRefreshLimit = 1;
    private const int ConcurrentRefreshWaiterLimit = 1;

    private readonly Func<CancellationToken, Task<AppServerClient>> _connect;
    private readonly SnapshotStore _store;
    private readonly Action<Uri> _openBrowser;
    private readonly SemaphoreSlim _refreshGate = new(ConcurrentRefreshLimit, ConcurrentRefreshWaiterLimit);
    private AppServerClient? _client;
    private UsageSnapshot? _snapshot;
    private long _snapshotVersion;
    private ViewerStatus _status;
    private bool _authenticationKnown;
    private bool _signedIn;
    private bool _signInInProgress;
    private bool _refreshing;
    private bool _expiryRefreshRequested;
    private bool _disposed;
    private long _rateLimitNotificationVersion;
    private UsageViewerViewState _viewState;

    public event Action<UsageViewerViewState>? ViewStateChanged;

    public UsageViewerViewState ViewState => _viewState;

    public UsageViewerController(
        Func<CancellationToken, Task<AppServerClient>> connect,
        SnapshotStore store,
        Action<Uri> openBrowser)
    {
        _connect = connect;
        _store = store;
        _openBrowser = openBrowser;
        _snapshot = _store.LoadSnapshot();
        _snapshotVersion = _snapshot is null ? 0 : 1;
        _status = _snapshot is null
            ? new ViewerStatus(ViewerStatusKind.Loading, ViewerText.Loading)
            : new ViewerStatus(ViewerStatusKind.Stale, ViewerText.StaleSavedLimits);
        _viewState = CreateViewState();
    }

    public async Task StartAsync()
    {
        try
        {
            await EnsureConnectedAsync().ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            SetErrorStatus(error);
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
            return;

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync().ConfigureAwait(false);
            _refreshing = true;
            SetStatus(ViewerStatusKind.Refreshing, ViewerText.RefreshingLimits);
            var client = _client ?? throw new AppServerException(ViewerText.CodexUnavailable);
            if (!_authenticationKnown)
            {
                try
                {
                    SetAuthenticationState(await client.ReadIsChatGptAuthenticatedAsync(CancellationToken.None).ConfigureAwait(false));
                }
                catch (Exception error) when (error is AppServerException or TimeoutException)
                {
                    SetAuthenticationState(null);
                }
            }

            var notificationVersion = Interlocked.Read(ref _rateLimitNotificationVersion);
            var snapshot = await client.ReadRateLimitsAsync(CancellationToken.None).ConfigureAwait(false);
            // Ignore a response when a pushed update arrived while the request was in flight.
            if (notificationVersion != Interlocked.Read(ref _rateLimitNotificationVersion))
                return;
            if (snapshot.Buckets.Count == 0 || snapshot.Buckets.All(bucket => bucket.Windows.Count == 0))
                throw new EmptyLimitsException();

            _snapshot = snapshot;
            _snapshotVersion++;
            var persistenceNote = string.Empty;
            try
            {
                await _store.SaveSnapshotAsync(snapshot).ConfigureAwait(false);
            }
            catch (IOException)
            {
                persistenceNote = ViewerText.LocalSaveUnavailable;
            }
            catch (UnauthorizedAccessException)
            {
                persistenceNote = ViewerText.LocalSaveUnavailable;
            }
            _expiryRefreshRequested = false;
            SetStatus(ViewerStatusKind.UpToDate, $"{ViewerText.UpToDate}{persistenceNote}", persistenceNote.Length > 0);
        }
        catch (Exception error)
        {
            SetErrorStatus(error);
        }
        finally
        {
            _refreshing = false;
            PublishViewState();
            _refreshGate.Release();
        }
    }

    public async Task SignInAsync()
    {
        if (_disposed || _signInInProgress)
            return;

        _signInInProgress = true;
        PublishViewState();
        try
        {
            SetStatus(ViewerStatusKind.SigningIn, ViewerText.StartingSignIn);
            await EnsureConnectedAsync().ConfigureAwait(false);
            var client = _client ?? throw new AppServerException(ViewerText.CodexUnavailable);
            var signedIn = await ChatGptSignInWorkflow.RunAsync(
                client,
                _openBrowser,
                (kind, text) => SetStatus(kind, text),
                RefreshAsync).ConfigureAwait(false);
            if (!signedIn)
                return;
        }
        catch (Exception error)
        {
            SetErrorStatus(error);
        }
        finally
        {
            _signInInProgress = false;
            PublishViewState();
        }
    }

    public void UpdateFreshness()
    {
        if (_snapshot is null || _disposed)
            return;

        var age = DateTimeOffset.UtcNow - _snapshot.FetchedAt;
        var now = DateTimeOffset.UtcNow;
        var expired = _snapshot.Buckets.SelectMany(bucket => bucket.Windows)
            .Any(window => window.ResetsAt is long reset && DateTimeOffset.FromUnixTimeSeconds(reset) <= now);
        if (expired && !_expiryRefreshRequested)
        {
            _expiryRefreshRequested = true;
            _ = RefreshAsync();
        }
        else if (!expired)
        {
            _expiryRefreshRequested = false;
        }

        if (age > PollInterval && !_refreshing)
        {
            if (_status.Kind is ViewerStatusKind.Unavailable or ViewerStatusKind.SignedOut)
                PublishViewState(_status with { Text = $"{_status.Text}{ViewerText.SavedLimitsOld(UsageDisplayFormatter.FormatDuration(age))}" });
            else
                SetStatus(ViewerStatusKind.Stale, ViewerText.StaleLastUpdate(UsageDisplayFormatter.FormatDuration(age)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_client is not null)
        {
            _client.RateLimitsUpdated -= OnRateLimitsUpdated;
            _client.AuthenticationChanged -= OnAuthenticationChanged;
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
        // Wait for an in-flight refresh to release the gate before shutdown completes.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Release();
    }

    private async Task EnsureConnectedAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UsageViewerController));
        if (_client?.IsRunning == true)
            return;
        if (_client is not null)
        {
            _client.RateLimitsUpdated -= OnRateLimitsUpdated;
            _client.AuthenticationChanged -= OnAuthenticationChanged;
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        _client = await _connect(CancellationToken.None).ConfigureAwait(false);
        SetAuthenticationState(null);
        _client.RateLimitsUpdated += OnRateLimitsUpdated;
        _client.AuthenticationChanged += OnAuthenticationChanged;
    }

    private void OnRateLimitsUpdated(JsonElement parameters)
    {
        // Advance the version before applying the pushed data so an older async response cannot win.
        Interlocked.Increment(ref _rateLimitNotificationVersion);
        if (!RateLimitSnapshotMerger.TryApply(_snapshot, parameters, out var merged))
        {
            _ = RefreshAsync();
            return;
        }

        _snapshot = merged;
        _snapshotVersion++;
        _expiryRefreshRequested = false;
        SetStatus(ViewerStatusKind.UpToDate, ViewerText.UpToDate, false);
        _ = PersistNotificationSnapshotAsync(merged);
    }

    private async Task PersistNotificationSnapshotAsync(UsageSnapshot snapshot)
    {
        try
        {
            await _store.SaveSnapshotAsync(snapshot).ConfigureAwait(false);
        }
        catch (IOException)
        {
            SetStatus(ViewerStatusKind.UpToDate, $"{ViewerText.UpToDate}{ViewerText.LocalSaveUnavailable}", true);
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus(ViewerStatusKind.UpToDate, $"{ViewerText.UpToDate}{ViewerText.LocalSaveUnavailable}", true);
        }
    }

    private void OnAuthenticationChanged(bool signedIn)
    {
        SetAuthenticationState(signedIn);
        if (signedIn && !_signInInProgress)
            _ = RefreshAsync();
        else if (!signedIn)
            SetStatus(ViewerStatusKind.SignedOut, ViewerText.SignedOut);
    }

    private void SetAuthenticationState(bool? signedIn)
    {
        _authenticationKnown = signedIn.HasValue;
        if (signedIn.HasValue)
            _signedIn = signedIn.Value;
        PublishViewState();
    }

    private void SetErrorStatus(Exception error)
    {
        var signedOut = error is AppServerException { Kind: AppServerErrorKind.AuthenticationRequired };
        var text = error switch
        {
            EmptyLimitsException => ViewerText.NoUsageLimits,
            TimeoutException timeout => ViewerText.UnavailableWithDetail(timeout.Message),
            AppServerException { Kind: AppServerErrorKind.AuthenticationRequired } => ViewerText.SignedOut,
            AppServerException appError => ViewerText.UnavailableWithDetail(appError.Message),
            _ => ViewerText.UnavailableFallback
        };
        SetStatus(signedOut ? ViewerStatusKind.SignedOut : ViewerStatusKind.Unavailable, text);
    }

    private void SetStatus(ViewerStatusKind kind, string text, bool isVisible = true)
    {
        _status = new ViewerStatus(kind, text, isVisible);
        PublishViewState();
    }

    private void PublishViewState()
    {
        PublishViewState(_status);
    }

    private void PublishViewState(ViewerStatus status)
    {
        _viewState = CreateViewState(status);
        ViewStateChanged?.Invoke(_viewState);
    }

    private UsageViewerViewState CreateViewState(ViewerStatus? status = null) => new(
        _snapshot,
        _snapshotVersion,
        status ?? _status,
        !_refreshing,
        _authenticationKnown && !_signedIn,
        !_signInInProgress);

    private sealed class EmptyLimitsException : Exception
    {
    }
}
