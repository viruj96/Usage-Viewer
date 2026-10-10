using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UsageViewer;

/// <summary>Account and rate-limit protocol facade over the app-server transport.</summary>
public sealed class AppServerClient : IAsyncDisposable
{
    private readonly AppServerTransport _transport;
    private readonly object _loginLock = new();
    private ChatGptLoginSession? _activeLogin;

    public event Action<JsonElement>? RateLimitsUpdated;
    public event Action<bool>? AuthenticationChanged;
    public bool IsRunning => _transport.IsRunning;

    private AppServerClient(AppServerTransport transport)
    {
        _transport = transport;
        // Subscribe before protocol initialization so early server notifications are not lost.
        _transport.NotificationReceived += HandleNotification;
    }

    public static async Task<AppServerClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var transport = await AppServerTransport.StartAsync().ConfigureAwait(false);
        var client = new AppServerClient(transport);
        try
        {
            await client.RequestAsync(AppServerProtocol.InitializeMethod, new Dictionary<string, object?>
            {
                [AppServerProtocol.ClientInfoField] = new Dictionary<string, string>
                {
                    [AppServerProtocol.ClientNameField] = AppServerProtocol.ClientName,
                    [AppServerProtocol.ClientTitleField] = AppServerProtocol.ClientTitle,
                    [AppServerProtocol.ClientVersionField] = AppServerProtocol.ClientVersion
                }
            }, AppServerProtocol.InitializationTimeout, cancellationToken).ConfigureAwait(false);
            await client._transport.SendNotificationAsync(AppServerProtocol.InitializedNotificationMethod, new { }, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<UsageSnapshot> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(AppServerProtocol.ReadRateLimitsMethod, null, AppServerProtocol.RequestTimeout, cancellationToken).ConfigureAwait(false);
        return RateLimitParser.Parse(result);
    }

    public async Task<bool> ReadIsChatGptAuthenticatedAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(AppServerProtocol.ReadAccountMethod,
            new Dictionary<string, object?> { [AppServerProtocol.RefreshTokenField] = false }, AppServerProtocol.RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(AppServerProtocol.AccountField, out var account))
            throw new AppServerException(ViewerText.InvalidAccountStatus);

        if (account.ValueKind == JsonValueKind.Null)
            return false;
        if (account.ValueKind != JsonValueKind.Object)
            throw new AppServerException(ViewerText.InvalidAccountStatus);

        if (account.TryGetProperty(AppServerProtocol.AuthModeField, out var accountAuthMode))
            return IsChatGptAuthMode(accountAuthMode);
        if (account.TryGetProperty(AppServerProtocol.AccountTypeField, out var accountType) && accountType.ValueKind == JsonValueKind.String)
            return IsChatGptAuthMode(accountType);
        throw new AppServerException(ViewerText.IncompleteAccountStatus);
    }

    private static bool IsChatGptAuthMode(JsonElement authMode) =>
        authMode.ValueKind == JsonValueKind.String &&
        (string.Equals(authMode.GetString(), AppServerProtocol.ChatGptAuthMode, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(authMode.GetString(), AppServerProtocol.ChatGptAuthTokensMode, StringComparison.OrdinalIgnoreCase));

    public async Task<ChatGptLoginSession> StartChatGptLoginAsync(CancellationToken cancellationToken)
    {
        var session = new ChatGptLoginSession();
        lock (_loginLock)
        {
            if (_activeLogin is not null)
                throw new AppServerException(ViewerText.SignInAlreadyInProgress);
            _activeLogin = session;
        }

        try
        {
            var result = await RequestAsync(AppServerProtocol.StartLoginMethod,
                new Dictionary<string, object?> { [AppServerProtocol.AccountTypeField] = AppServerProtocol.ChatGptAuthMode }, AppServerProtocol.RequestTimeout, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetProperty(AppServerProtocol.LoginIdField, out var loginIdElement) || loginIdElement.ValueKind != JsonValueKind.String ||
                !result.TryGetProperty(AppServerProtocol.AuthUrlField, out var authUrlElement) || authUrlElement.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(authUrlElement.GetString(), UriKind.Absolute, out var authUrl) ||
                authUrl.Scheme != Uri.UriSchemeHttps ||
                !(authUrl.Host.Equals(AppServerProtocol.ChatGptHost, StringComparison.OrdinalIgnoreCase) ||
                  authUrl.Host.Equals(AppServerProtocol.OpenAiAuthHost, StringComparison.OrdinalIgnoreCase)) ||
                !authUrl.IsDefaultPort || !string.IsNullOrEmpty(authUrl.UserInfo))
                throw new AppServerException(ViewerText.InvalidSignInLink);

            var loginId = loginIdElement.GetString();
            if (string.IsNullOrWhiteSpace(loginId))
                throw new AppServerException(ViewerText.InvalidSignInRequest);

            lock (_loginLock)
            {
                session.LoginId = loginId;
                session.AuthUrl = authUrl;
                if (session.EarlyCompletion is { } completion && completion.LoginId == loginId)
                    session.CompletionSource.TrySetResult(completion.Success);
            }
            return session;
        }
        catch
        {
            FinishChatGptLogin(session);
            throw;
        }
    }

    public void FinishChatGptLogin(ChatGptLoginSession session)
    {
        lock (_loginLock)
        {
            if (ReferenceEquals(_activeLogin, session))
                _activeLogin = null;
        }
    }

    private Task<JsonElement> RequestAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken) =>
        _transport.RequestAsync(method, parameters, timeout, cancellationToken);

    private void HandleNotification(string method, JsonElement parameters)
    {
        if (method == AppServerProtocol.RateLimitsUpdatedMethod)
            RateLimitsUpdated?.Invoke(parameters);
        else if (method == AppServerProtocol.AccountUpdatedMethod && parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(AppServerProtocol.AuthModeField, out var authMode))
        {
            var signedIn = authMode.ValueKind != JsonValueKind.Null && IsChatGptAuthMode(authMode);
            lock (_loginLock)
            {
                if (_activeLogin is { LoginId.Length: > 0 } login)
                    login.AuthenticationUpdatedSource.TrySetResult(signedIn);
            }
            AuthenticationChanged?.Invoke(signedIn);
        }
        else if (method == AppServerProtocol.LoginCompletedMethod)
            HandleLoginCompleted(parameters);
    }

    private void HandleLoginCompleted(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty(AppServerProtocol.LoginIdField, out var loginIdElement) || loginIdElement.ValueKind != JsonValueKind.String ||
            !parameters.TryGetProperty(AppServerProtocol.SuccessField, out var successElement) ||
            successElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return;

        var loginId = loginIdElement.GetString();
        if (string.IsNullOrWhiteSpace(loginId))
            return;
        lock (_loginLock)
        {
            if (_activeLogin is not { } login)
                return;
            var success = successElement.GetBoolean();
            if (login.LoginId.Length == 0)
                login.EarlyCompletion = new LoginCompletion(loginId, success);
            else if (login.LoginId == loginId)
                login.CompletionSource.TrySetResult(success);
        }
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
