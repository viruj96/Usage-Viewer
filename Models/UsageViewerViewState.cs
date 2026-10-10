namespace UsageViewer;

public sealed record UsageViewerViewState(
    UsageSnapshot? Snapshot,
    long SnapshotVersion,
    ViewerStatus Status,
    bool RefreshEnabled,
    bool SignInVisible,
    bool SignInEnabled);
