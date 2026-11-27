# 0001. WinUI 3 for the PC app main window

- Status: Accepted
- Date: 2026-11-27
- Deciders: katarem (project owner)

## Context

The main window of the PC app (`pc/ConsoleMode/UI/MainForm.cs`, WinForms) is hard to use. Its layout relies on
fixed pixel widths (most texts are capped at 540 px), so moving the window to a monitor with another scale, or resizing
it, leaves icons and text clipped and wastes the available space. It also explains every option with long paragraphs.

The owner wants a redesign: a sidebar with the sections (Home, Modes, Phones, Power, Settings), the PC/Console switch
always visible, one short line per option with details in tooltips, and a window that adapts to its size and to the
monitor's DPI. The rest of the app (gamepad overlay, tray icon, pairing dialog, all the Win32 logic) works well and is
not part of the problem.

## Requirements

- No clipped icons or text at 100 %, 125 %, 150 % and 200 % scale, or when moving the window between monitors with
  different scales (per-monitor DPI v2). Checked by hand on the owner's PC.
- Layout adapts to the window size, from the minimum size to full screen, without fixed widths.
- Follows the Windows light/dark theme.
- Keeps shipping both an installer (Inno Setup) and a **single-file portable exe**, built by the existing
  `windows-latest` workflows, unpackaged (no MSIX, no Store).
- The gamepad overlay, tray icon and pairing dialog keep working unchanged in the same process.

## Options considered

### Option 1: Keep WinForms and redo the layout

- Pros: no new dependency or tooling; the portable exe stays at ~72 MB; the team already knows it; lowest risk.
- Cons: WinForms has no navigation, theming or modern controls: a sidebar, cards, dark mode and Windows 11 look have to
  be hand-drawn. Per-monitor DPI in WinForms keeps having rough edges (fonts and custom drawing need care in every
  control). It fixes the layout bugs but not the dated look the owner dislikes.

### Option 2: WPF

- Pros: part of .NET (no extra NuGet), vector layout that scales well, mature, single-file publish works.
- Cons: still needs a third-party theme (or .NET 9+ Fluent theme) for a Windows 11 look; a full rewrite of the window
  like option 3 but with a less native result.

### Option 3: WinUI 3 (Windows App SDK), unpackaged and self-contained

- Pros: native Windows 11 controls (`NavigationView`, `ToggleSwitch`, `InfoBar`, Mica backdrop), automatic light/dark
  theme and per-monitor DPI handled by the framework, adaptive layout out of the box. It is Microsoft's current UI
  stack for desktop apps.
- Cons: biggest change. New dependency (`Microsoft.WindowsAppSDK.WinUI`, MIT licence), XAML tooling that only builds on
  Windows, larger output and memory use, and two UI threads in the process (WinUI and WinForms).

## Proof of concept

A throwaway project (`poc/WinUiPoc`, removed before merging) was built and run on the `windows-latest` runner with
`Microsoft.WindowsAppSDK.WinUI` 2.6.5, .NET 8, unpackaged and self-contained:

| Check | Result |
|-------|--------|
| `dotnet publish` (no Visual Studio) | Builds, both folder and single-file. |
| Single-file portable exe | Works. 99 MB (current app: 72 MB). Needs `EnableMsixTooling` to embed `resources.pri`; the Windows App SDK extracts the files to `%TEMP%\.net` on first start (warning NETSDK1244). |
| WinUI 3 window next to WinForms in one process | Works: WinForms keeps the main thread, WinUI runs `Application.Start` on its own STA thread. Both windows are visible and no errors reach the Windows event log. |
| Calls between both UI threads | Work both ways: `Control.BeginInvoke` (WinUI → WinForms) and `DispatcherQueue.TryEnqueue` (WinForms → WinUI). |
| Start-up | WinUI window active ~0.4 s (folder) and ~1 s (single-file) after the process starts. |
| Memory | ~120 MB working set with both windows open. |
| Build quirk | `UseWindowsForms` imports the WPF XAML compiler, which overrides WinUI's and fails. WinForms is referenced with `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />` instead. |

## Decision

Option 3, WinUI 3, for the main window only. The owner chose it for the native, modern result, and the proof of concept
shows that the hard constraints (single-file portable exe, coexistence with the WinForms overlay and tray in one
process, CI build) hold.

The overlay, tray icon and pairing dialog stay in WinForms. The main window becomes a WinUI 3 window on its own UI
thread. It talks to the rest of the app through the existing services (`AppSettings`, `ModeSwitcher`, `QuickSwitch`…)
and marshals calls between threads with `DispatcherQueue` and `Control.BeginInvoke`.

## Consequences

- Positive: native Windows 11 look with Mica, light/dark theme and per-monitor DPI handled by the framework. Adaptive
  layout without hand-made sizing code. The main window's UI is declared in XAML, apart from its logic.
- Negative:
  - The portable exe grows from ~72 MB to ~100 MB and memory use rises while the window is open.
  - The PC project can only be fully built on Windows (the CI already does); the Linux build used for quick checks
    stops working.
  - Two UI threads: every call from the main window into WinForms-owned objects, and the other way round, must be
    marshalled.
  - New dependency to keep up to date (`Microsoft.WindowsAppSDK.WinUI`).
  - The project file changes target (`net8.0-windows10.0.19041.0`, x64, `EnableMsixTooling`) and stops using
    `UseWindowsForms`.
- Follow-up:
  - Remove the WinForms main window once the WinUI one covers all its features.
  - Pin the .NET SDK with `global.json`: the runner picked SDK 11 by default.
  - .NET 8 reached end of support on 2026-11-10: plan the move to .NET 10 LTS as a separate change.
