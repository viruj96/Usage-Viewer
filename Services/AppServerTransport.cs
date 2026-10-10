using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UsageViewer;

/// <summary>Owns the app-server process and the JSON-line request/response transport.</summary>
internal sealed class AppServerTransport : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _input;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Task _reader;
    private long _nextId;
    private bool _disposed;

    internal event Action<string, JsonElement>? NotificationReceived;

    internal bool IsRunning => !_disposed && !_process.HasExited;

    private AppServerTransport(Process process)
    {
        _process = process;
        _input = process.StandardInput;
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
        _reader = ReadLoopAsync();
    }

    internal static Task<AppServerTransport> StartAsync()
    {
        var executable = Environment.GetEnvironmentVariable(AppServerProtocol.ExecutablePathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            executable = AppServerProtocol.DefaultExecutableName;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        startInfo.ArgumentList.Add(AppServerProtocol.AppServerArgument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                throw new AppServerException(ViewerText.AppServerStartFailed);
        }
        catch (Exception ex) when (ex is not AppServerException)
        {
            process.Dispose();
            throw new AppServerException(ViewerText.AppServerStartFailed);
        }
        return Task.FromResult(new AppServerTransport(process));
    }

    internal async Task<JsonElement> RequestAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new AppServerException(ViewerText.RequestTrackingFailed);

        try
        {
            var request = new Dictionary<string, object?>
            {
                [AppServerProtocol.MethodField] = method,
                [AppServerProtocol.IdField] = id
            };
            if (parameters is not null)
                request[AppServerProtocol.ParametersField] = parameters;
            await WriteMessageAsync(request, cancellationToken).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeoutSource.CancelAfter(timeout);
            try
            {
                return await completion.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                throw new TimeoutException(ViewerText.AppServerMethodTimedOut(method));
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    internal Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(new Dictionary<string, object?>
        {
            [AppServerProtocol.MethodField] = method,
            [AppServerProtocol.ParametersField] = parameters
        }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _input.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new AppServerException(ViewerText.AppServerStoppedSendingRequest);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null)
                    break;

                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty(AppServerProtocol.IdField, out var idElement) && TryReadId(idElement, out var id))
                {
                    if (!_pending.TryGetValue(id, out var completion))
                        continue;

                    if (message.TryGetProperty(AppServerProtocol.ErrorField, out var error))
                    {
                        var code = error.TryGetProperty(AppServerProtocol.ErrorCodeField, out var codeElement) && codeElement.TryGetInt32(out var parsedCode) ? parsedCode : 0;
                        var detail = error.TryGetProperty(AppServerProtocol.ErrorMessageField, out var messageElement) ? messageElement.GetString() : null;
                        var safeError = ToSafeError(code, detail);
                        completion.TrySetException(new AppServerException(safeError.Message, safeError.Kind));
                    }
                    else if (message.TryGetProperty(AppServerProtocol.ResultField, out var result))
                        completion.TrySetResult(result.Clone());
                    else
                        completion.TrySetException(new AppServerException(ViewerText.InvalidAppServerResponse));
                }
                else if (message.TryGetProperty(AppServerProtocol.MethodField, out var methodElement))
                {
                    var method = methodElement.GetString();
                    if (method is not null)
                    {
                        var parameters = message.TryGetProperty(AppServerProtocol.ParametersField, out var paramsElement) ? paramsElement.Clone() : default;
                        NotificationReceived?.Invoke(method, parameters);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (JsonException) { FailPending(ViewerText.InvalidAppServerMessage); }
        catch (IOException) { FailPending(ViewerText.AppServerStoppedUnexpectedly); }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                FailPending(ViewerText.AppServerStoppedUnexpectedly);
        }
    }

    private static bool TryReadId(JsonElement value, out long id)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out id))
            return true;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out id))
            return true;
        id = 0;
        return false;
    }

    private static (string Message, AppServerErrorKind Kind) ToSafeError(int code, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail) &&
            (detail.Contains(AppServerProtocol.UnauthorizedNeedle, StringComparison.OrdinalIgnoreCase) ||
             detail.Contains(AppServerProtocol.NotLoggedInNeedle, StringComparison.OrdinalIgnoreCase) ||
             detail.Contains(AppServerProtocol.UnauthorizedStatusNeedle, StringComparison.OrdinalIgnoreCase) ||
             detail.Contains(AppServerProtocol.AuthenticationRequiredNeedle, StringComparison.OrdinalIgnoreCase)))
            return (ViewerText.SignInToReadLimits, AppServerErrorKind.AuthenticationRequired);
        return (code == 0 ? ViewerText.AppServerReturnedError : ViewerText.AppServerReturnedErrorCode(code), AppServerErrorKind.General);
    }

    private void FailPending(string message)
    {
        foreach (var item in _pending.Values)
            item.TrySetException(new AppServerException(message));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        try { _input.Close(); } catch (IOException) { }
        try
        {
            if (!_process.HasExited)
                _process.Kill(true);
        }
        catch (InvalidOperationException) { }
        try { await _reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _process.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
    }
}
