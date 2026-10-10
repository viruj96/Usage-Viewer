# Contributor instructions

## Project scope

This repository builds **Usage Viewer**, a Windows-only WPF utility. It starts `codex app-server`, reads account rate limits over the App Server's JSON-line protocol, and presents a cached snapshot in a small window and notification-area icon.

## Build and run

Use the .NET 10 SDK on Windows. From the repository root:

```powershell
dotnet build UsageViewer.sln
dotnet run --project UsageViewer.csproj
```

The app needs the Codex CLI on `PATH`. Set `CODEX_CLI_PATH` to the CLI executable when it is installed in a nonstandard location. A successful build does not confirm that the local Codex App Server can start or that the account is signed in.

There is no test project in this repository. Do not describe build success as test coverage. For a behavior change, run the build and any focused checks that already exist for the affected path; do not add a test framework just for a documentation or small maintenance change.

## Code map

- `Controllers/` owns refresh orchestration and maps application state for the UI.
- `Models/` contains usage, settings, view-state, status, and protocol result types.
- `Helpers/` contains rate-limit parsing, snapshot merging, usage formatting, and rendering.
- `Constants/` contains App Server protocol and JSON keys, defaults, UI text, and resource keys.
- `Services/` contains App Server communication, snapshot persistence, and tray integration.
- `Workflows/` coordinates ChatGPT sign-in.
- `Views/` contains the WPF window and its code-behind.
- `App.xaml.cs` wires the controller, snapshot store, browser callback, and window, then shuts them down.

Prefer changes in the layer that owns the behavior. Keep protocol parsing out of the view, formatting out of the transport, and persistence out of UI event handlers.

## Important behavior to preserve

- The app talks to the Codex App Server process through standard input and output using JSON-line messages. Keep request IDs, response matching, timeouts, cancellation, and notification handling coherent when changing this transport.
- The account and rate-limit protocol behavior is centralized in `Constants/AppServerProtocol.cs` and `Constants/RateLimitJson.cs`, then consumed by `Services/AppServerClient.cs`. Keep method names and JSON field names in those constants rather than scattering protocol strings.
- Rate-limit notifications can contain partial updates. Preserve fields that a notification does not include when updating the current snapshot.
- Persist the latest snapshot so startup can display cached data before a successful refresh. Treat the cache as stale until the server provides current data.
- Refreshes are serialized by the controller. Avoid introducing overlapping refreshes or duplicate App Server processes.
- Closing the window hides it to the tray. The tray **Exit** action is the explicit app shutdown path; preserve disposal of the controller and tray icon.
- The window starts topmost and is placed near the lower-right of the usable screen area. Preserve usable behavior across display sizes and do not assume a fixed screen resolution.
- Keep user-facing text in `Constants/ViewerText.cs` and usage display formatting in `Helpers/UsageDisplayFormatter.cs`.
- The app launches ChatGPT sign-in in the user's browser. Do not log authentication tokens, raw sign-in responses, or other account secrets.

## Change workflow

1. Read the owning code and its callers before changing behavior. In particular, trace App Server changes through `AppServerClient`, `UsageViewerController`, and the view state.
2. Make the smallest change that preserves the layer boundaries and existing UI behavior.
3. Build with `dotnet build UsageViewer.sln` on Windows. If a change depends on a running Codex App Server or signed-in account, state that the build alone does not verify that integration.
4. Update this file or `README.md` only when the contributor workflow, app behavior, setup, or repository structure has actually changed.

## Documentation boundaries

Keep `README.md` user-facing: explain the utility, requirements, setup, behavior, data location, and practical troubleshooting. Keep this file focused on repository-specific guidance for people and coding agents making changes. Do not claim features, settings, commands, tests, or supported platforms that are not present in the project.
