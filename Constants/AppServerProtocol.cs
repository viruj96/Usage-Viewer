using System;

namespace UsageViewer;

internal static class AppServerProtocol
{
    internal const string InitializeMethod = "initialize";
    internal const string InitializedNotificationMethod = "initialized";
    internal const string ReadRateLimitsMethod = "account/rateLimits/read";
    internal const string ReadAccountMethod = "account/read";
    internal const string StartLoginMethod = "account/login/start";
    internal const string RateLimitsUpdatedMethod = "account/rateLimits/updated";
    internal const string AccountUpdatedMethod = "account/updated";
    internal const string LoginCompletedMethod = "account/login/completed";
    internal const string MethodField = "method";
    internal const string IdField = "id";
    internal const string ParametersField = "params";
    internal const string ErrorField = "error";
    internal const string ResultField = "result";
    internal const string ErrorCodeField = "code";
    internal const string ErrorMessageField = "message";
    internal const string AccountField = "account";
    internal const string AuthModeField = "authMode";
    internal const string AccountTypeField = "type";
    internal const string RefreshTokenField = "refreshToken";
    internal const string LoginIdField = "loginId";
    internal const string AuthUrlField = "authUrl";
    internal const string SuccessField = "success";
    internal const string ClientInfoField = "clientInfo";
    internal const string ClientNameField = "name";
    internal const string ClientTitleField = "title";
    internal const string ClientVersionField = "version";
    internal const string ChatGptAuthMode = "chatgpt";
    internal const string ChatGptAuthTokensMode = "chatgptAuthTokens";
    internal const string ClientName = "usage_viewer";
    internal const string ClientTitle = "Usage Viewer";
    internal const string ClientVersion = "1.0.0";
    internal const string ExecutablePathEnvironmentVariable = "CODEX_CLI_PATH";
    internal const string DefaultExecutableName = "codex";
    internal const string AppServerArgument = "app-server";
    internal const string ChatGptHost = "chatgpt.com";
    internal const string OpenAiAuthHost = "auth.openai.com";
    internal const string DefaultChatGptAuthUrl = "https://chatgpt.com/";
    internal const string UnauthorizedNeedle = "unauthorized";
    internal const string NotLoggedInNeedle = "not logged in";
    internal const string UnauthorizedStatusNeedle = "401";
    internal const string AuthenticationRequiredNeedle = "authentication required";
    internal static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
}
