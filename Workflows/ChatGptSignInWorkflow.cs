using System;
using System.Threading.Tasks;

namespace UsageViewer;

internal static class ChatGptSignInWorkflow
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AuthenticationConfirmationTimeout = TimeSpan.FromSeconds(20);

    internal static async Task<bool> RunAsync(
        AppServerClient client,
        Action<Uri> openBrowser,
        Action<ViewerStatusKind, string> reportProgress,
        Func<Task> refreshAfterSignIn)
    {
        var session = await client.StartChatGptLoginAsync(default).ConfigureAwait(false);
        try
        {
            try
            {
                openBrowser(session.AuthUrl);
            }
            catch (Exception)
            {
                reportProgress(ViewerStatusKind.SigningIn, ViewerText.BrowserOpenFailed);
                return false;
            }

            reportProgress(ViewerStatusKind.SigningIn, ViewerText.BrowserSignInPrompt);
            bool completed;
            try
            {
                completed = await session.Completion.WaitAsync(CompletionTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                reportProgress(ViewerStatusKind.SigningIn, ViewerText.SignInPending);
                return false;
            }
            if (!completed)
            {
                reportProgress(ViewerStatusKind.SigningIn, ViewerText.SignInIncomplete);
                return false;
            }

            reportProgress(ViewerStatusKind.SigningIn, ViewerText.SignInCompleteConfirming);
            bool signedIn;
            try
            {
                signedIn = await session.AuthenticationUpdated
                    .WaitAsync(AuthenticationConfirmationTimeout)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                reportProgress(ViewerStatusKind.SigningIn, ViewerText.SignInConfirmationFailed);
                return false;
            }
            if (!signedIn)
            {
                reportProgress(ViewerStatusKind.SigningIn, ViewerText.SignInConfirmationFailed);
                return false;
            }

            reportProgress(ViewerStatusKind.SigningIn, ViewerText.SignedInRefreshing);
            await refreshAfterSignIn().ConfigureAwait(false);
            return true;
        }
        finally
        {
            client.FinishChatGptLogin(session);
        }
    }
}
