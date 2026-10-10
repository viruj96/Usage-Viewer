using System;
using System.Threading.Tasks;

namespace UsageViewer;

public sealed class ChatGptLoginSession
{
    internal string LoginId { get; set; } = "";
    internal LoginCompletion? EarlyCompletion { get; set; }
    internal TaskCompletionSource<bool> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> AuthenticationUpdatedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Uri AuthUrl { get; internal set; } = new(AppServerProtocol.DefaultChatGptAuthUrl);
    public Task<bool> Completion => CompletionSource.Task;
    public Task<bool> AuthenticationUpdated => AuthenticationUpdatedSource.Task;
}
