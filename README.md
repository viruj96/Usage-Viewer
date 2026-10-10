# Usage Viewer

Usage Viewer is a small Windows desktop utility that keeps Codex account usage limits visible in a compact window. It starts the Codex App Server, reads the signed-in account's rate-limit data, and shows usage percentages and reset times. The window can stay above other windows and can be hidden to the Windows notification area.

## What it does

- Shows each usage-limit bucket returned by Codex, including its usage windows, reset countdowns, and available reset credits when present.
- Refreshes automatically every five minutes. You can also refresh from the window or the tray menu.
- Starts ChatGPT sign-in in the default browser when Codex needs authentication.
- Saves the last usage snapshot locally so it can show stale data while a new refresh is in progress or Codex is unavailable.
- Keeps running when the window is hidden. Use **Exit** in the tray menu to close the app.

This app reads the rate-limit data provided by the Codex App Server. It does not calculate usage from prompts or provide an account-wide billing report.

## Requirements

- Windows, because the UI uses WPF and the tray icon uses Windows Forms.
- The .NET 10 SDK.
- The Codex CLI available as `codex` on `PATH`, or an explicit executable path in `CODEX_CLI_PATH`.
- A ChatGPT sign-in in Codex to view account limits.

## Build and run

From the repository root in PowerShell:

```powershell
dotnet build UsageViewer.sln
dotnet run --project UsageViewer.csproj
```

## Build the MSI installer

Build the per-user MSI from the repository root in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File Installer\Build-Msi.ps1
```

The MSI is written to `artifacts\msi\UsageViewer-1.0.0.msi`. Increase `ProductVersion` when building a release, for example:

```powershell
powershell -ExecutionPolicy Bypass -File Installer\Build-Msi.ps1 -ProductVersion 1.0.1
```

When Codex is installed somewhere outside `PATH`, set the executable path before starting the app:

```powershell
$env:CODEX_CLI_PATH = 'C:\path\to\codex.exe'
dotnet run --project UsageViewer.csproj
```

The app opens a small window near the lower-right of the usable desktop area. Drag its header to move it. The **×** button hides it to the tray; the tray menu can show, hide, refresh, or exit.

## Where data is stored

The app writes its settings and most recent usage snapshot under:

```text
%LOCALAPPDATA%\UsageViewer\
```

The files are `settings.json` and `snapshot.json`. The snapshot is a local cache of the last data returned by Codex. Deleting `snapshot.json` removes that cached view; the app can fetch a new snapshot after it connects successfully.

## Repository map

| Path | Responsibility |
| --- | --- |
| `App.xaml`, `App.xaml.cs` | WPF startup, controller construction, browser launch, and shutdown. |
| `Controllers/` | Coordinates usage refresh and maps application state for the window. |
| `Models/` | Usage snapshots, limit buckets and windows, settings, view state, and status. |
| `Helpers/` | Parses and merges rate-limit data, formats usage, and renders the snapshot. |
| `Constants/` | App Server protocol and JSON keys, defaults, UI text, and resource keys. |
| `Services/` | App Server communication, local snapshot storage, and tray integration. |
| `Workflows/` | Coordinates ChatGPT sign-in. |
| `Views/` | The WPF window and its code-behind. |
| `Assets/` | Embedded Windows tray icon. |

## Troubleshooting

- **Codex App Server could not start:** Confirm `codex` runs from PowerShell, or set `CODEX_CLI_PATH` to the CLI executable. The environment variable must be set in the process environment used to launch this app.
- **Sign in to read limits:** Choose **Sign in**, complete ChatGPT sign-in in the browser, then wait for the app to refresh.
- **Saved limits are stale:** The app could not get a current snapshot. Check that Codex can start and is signed in, then use **Refresh** from the window or tray menu.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for the terms.
