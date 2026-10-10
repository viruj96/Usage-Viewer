using System;
using System.Diagnostics;
using System.Windows;

namespace UsageViewer;

public partial class App : System.Windows.Application
{
    private UsageViewerController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _controller = new UsageViewerController(
            AppServerClient.ConnectAsync,
            new SnapshotStore(),
            OpenBrowser);
        var window = new MainWindow(_controller);
        MainWindow = window;
        window.Show();
        window.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _controller = null;
        base.OnExit(e);
    }

    private static void OpenBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}
