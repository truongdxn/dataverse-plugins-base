@echo off
rem Entry point for the dv CLI. Forwards to dv.ps1, which builds the tool when it is stale.
rem Kept as a thin wrapper so there is only one copy of the real logic.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dv.ps1" %*
exit /b %ERRORLEVEL%
