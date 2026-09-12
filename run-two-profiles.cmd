@echo off
rem Launches two widgets, each with its own claude.ai login.
rem Rename the profiles to whatever you like ("work", "personal", ...).
set EXE=%~dp0bin\Release\net10.0-windows\ClaudeUsageWidget.exe
start "" "%EXE%" --profile work
start "" "%EXE%" --profile personal
