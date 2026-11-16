@echo off
set "SHARESAFE_EXE=%~dp0WindowsApp\bin\ShareSafe.Windows.exe"
if not exist "%SHARESAFE_EXE%" (
  echo Build ShareSafe first: powershell -File scripts\build-windows.ps1
  pause
  exit /b 1
)
start "" "%SHARESAFE_EXE%" %*
