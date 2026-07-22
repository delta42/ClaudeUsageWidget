# Claude Usage Widget

A small, always-on-top Windows desktop widget that shows your current
Claude.ai session usage and reset time at a glance.

<img src="Assets/screenshot.png" alt="screenshot" width="280">

## Features

- Frameless, draggable, always-on-top widget showing usage % and reset time
- Refreshes automatically on a configurable interval (default: every 60 seconds)
- Right-click menu: Log in / show browser, Refresh now, Settings, Exit
- Session/cookies persisted in a dedicated WebView2 profile, so you only log in once
- Remembers its window position between runs

## How it works

The widget keeps a hidden WebView2 instance pointed at your configured
claude.ai usage page, periodically re-navigates it, and reads the rendered
page for the usage percentage and "Resets in ..." text. No API keys are
used and nothing is scraped from network traffic — it's the same page you'd
see in a normal browser tab.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/) (to build)
- Microsoft Edge WebView2 Runtime (already installed on most modern Windows systems)

## Build & run

```
dotnet build ClaudeUsageWidget.csproj -c Release
bin\Release\net10.0-windows\ClaudeUsageWidget.exe
```

Or open `ClaudeUsageWidget.slnx` in Visual Studio and press F5.

## First-time setup

On first run the widget shows `?` until you're logged in. Right-click it →
**Log in / show browser**, sign in to Claude in the window that appears,
then click **Done**. Your session is cached locally, so you shouldn't need
to log in again.

## Settings

Right-click → **Settings...** lets you change:

- **Page URL** — which claude.ai page to read from (defaults to `https://claude.ai/new`)
- **Refresh rate (seconds)** — how often to refresh (default: 60)
- **Launch at Windows startup** — adds/removes the app from your per-user startup entries (default: off)

Settings and the WebView2 session data are stored under
`%AppData%\ClaudeUsageWidget\`, outside the repo.

## Privacy

The widget doesn't send your data anywhere — it only talks to claude.ai
through a browser instance embedded in the app. No credentials, tokens, or
usage data leave your machine or get logged.

## License

[MIT](LICENSE)
