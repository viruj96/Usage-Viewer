namespace UsageViewer;

public readonly record struct ViewerStatus(ViewerStatusKind Kind, string Text, bool IsVisible = true);
