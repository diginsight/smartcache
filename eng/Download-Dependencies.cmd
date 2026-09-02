@echo off
REM Downloads every upstream Diginsight release listed in eng/upstream-releases.json into
REM artifacts/packages, the local-release package source. Run this before `dotnet restore`
REM when a pinned version has not reached the feed yet.
REM
REM Exists so the download is invokable from cmd and Windows PowerShell 5.1 as well: the
REM script itself declares #requires -Version 7.0, so it must be launched with pwsh.
REM
REM Any arguments are forwarded, so a single upstream can still be targeted:
REM   eng\Download-Dependencies.cmd https://github.com/diginsight/telemetry
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Download-PackageRelease.ps1" %*
exit /b %ERRORLEVEL%
