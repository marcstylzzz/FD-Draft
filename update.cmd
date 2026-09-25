@echo off
rem Double-click to update FD-Draft: pulls the latest code, builds it, and starts the app.
rem Works from wherever this file is, so it doesn't matter which folder a Command Prompt opens in.
cd /d "%~dp0"

tasklist /fi "imagename eq FD-Draft.exe" | find /i "FD-Draft.exe" >nul
if not errorlevel 1 (
  echo FD-Draft is still open. Save your work and close it, then press a key to carry on.
  pause >nul
)

echo Getting the latest FD-Draft...
git pull --recurse-submodules
if errorlevel 1 goto failed

echo Building...
dotnet build FD-Draft.sln -v q -nologo
if errorlevel 1 goto failed

echo.
echo Up to date. Starting FD-Draft...
start "" "src\FdDraft.App\bin\Debug\net8.0-windows\FD-Draft.exe"
exit /b 0

:failed
echo.
echo Something went wrong above - send Claude a screenshot of this window.
pause
exit /b 1
