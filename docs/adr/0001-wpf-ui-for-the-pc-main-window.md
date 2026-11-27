# 0001. WPF with WPF-UI for the PC main window

- Status: Accepted
- Date: 2026-11-27
- Deciders: project owner

## Context

The main window of the PC app (`pc/ConsoleMode`, .NET 8) is built with standard WinForms controls. Even after making
it DPI-aware and lighter on text (1.9.0), it looks dated, and the owner wants a full redesign: a left navigation bar,
cards, modern toggles and drop-downs, following the Windows light or dark theme.

The rest of the app must keep working as it does today: the tray icon, the background process started with Windows,
the custom-drawn gamepad overlay (WinForms, `OverlayForm`), the single-file portable `.exe` with its self-updater and
the Inno Setup installer built from that same `.exe`.

## Requirements

- Modern Windows 11 look (Fluent controls, left navigation, light/dark theme that follows Windows): reviewed by the
  owner on Windows.
- Builds in the development environment (Linux, `dotnet build` with `EnableWindowsTargeting`) and in the Windows CI
  job: `dotnet build pc/ConsoleMode/ConsoleMode.csproj -c Release` with 0 errors.
- Single-file portable `.exe` still produced by `release.yml` (`PublishSingleFile`, self-contained) and still
  replaceable by the updater.
- Tray icon, background start and the WinForms gamepad overlay keep working in the same process.
- No noticeable regression in startup time or memory while the app runs in the background.

## Options considered

### Option 1: WinForms with custom-drawn controls (current stack)

- Pros: no new dependency or framework; lowest memory; the overlay already shows the approach works.
- Cons: every control (navigation, toggles, drop-downs, cards, theming, accessibility, keyboard focus) has to be
  written and maintained by hand; hard to make it look and behave like native Windows 11.

### Option 2: WPF with WPF-UI

- Pros: Fluent look close to Windows 11 (Mica, `NavigationView`, `FluentWindow`, theme that follows the system);
  WPF-UI is MIT-licensed and actively maintained (4.3.0); WPF and WinForms coexist in one process, so the tray icon
  and the overlay stay as they are; a probe project with WPF-UI 4.3.0 and `UseWindowsForms` builds on Linux with
  0 warnings and 0 errors; single-file publish works as today.
- Cons: a new UI framework and dependency in the project; the main window is rewritten in XAML; somewhat more memory
  than WinForms while the window is open.

### Option 3: WinUI 3 (Windows App SDK)

- Pros: fully native Windows 11 look and controls.
- Cons: the XAML compiler (`XamlCompiler.exe`, .NET Framework) only runs on Windows: a probe project fails on Linux
  with "Exec format error", so every change could only be checked in the Windows CI; no built-in tray icon, and
  mixing its dispatcher with the WinForms message loop of the tray and the overlay is delicate; single-file publish
  is only supported unpackaged and self-contained from Windows App SDK 1.5, and the `.exe` extracts the runtime to a
  temporary folder on first start, with a larger download; highest memory of the three.

## Decision

Option 2: WPF with WPF-UI. It gives practically the same visual result as WinUI 3 while keeping the build and the
verification in the development environment, and it keeps the tray icon, the overlay, the portable `.exe` and the
installer working without changes. The owner first preferred WinUI 3 and chose WPF with WPF-UI after reviewing the
trade-offs above.

## Consequences

- Positive: modern, theme-aware main window built from maintained controls instead of hand-drawn ones; changes can be
  compiled and checked locally.
- Negative: one more framework and a new dependency (`WPF-UI`) to keep up to date; the main window code is rewritten;
  WPF and WinForms now share the process, so startup and the message loop must be handled with care.
- Follow-up: rewrite `MainForm` as a WPF window with WPF-UI (navigation bar, cards, theme following Windows), keep
  `TrayApplicationContext`, `OverlayForm` and `PairRequestForm` in WinForms for now, check single-file publish and
  the installer in CI, and track the WPF-UI version with the rest of the dependencies.
