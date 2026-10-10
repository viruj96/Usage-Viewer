using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace UsageViewer;

public partial class App : System.Windows.Application
{
#if DEBUG
    private const string InstanceMutexName = @"Local\UsageViewer.SingleInstance.Debug";
#else
    private const string InstanceMutexName = @"Local\UsageViewer.SingleInstance";
#endif

    private Mutex? _instanceMutex;
    private UsageViewerController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _controller = new UsageViewerController(
            AppServerClient.ConnectAsync,
            new SnapshotStore(),
            OpenBrowser);
        var window = new MainWindow(_controller);
        MainWindow = window;
        ProcessMemoryTrimmer.TrimCurrentProcessWorkingSet();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _controller = null;
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }

    private static void OpenBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}
