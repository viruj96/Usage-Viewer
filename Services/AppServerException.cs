using System;

namespace UsageViewer;

public sealed class AppServerException(string message, AppServerErrorKind kind = AppServerErrorKind.General) : Exception(message)
{
    public AppServerErrorKind Kind { get; } = kind;
}
