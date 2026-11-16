@echo off
set "SHARESAFE_EXE=%~dp0WindowsApp\bin\ShareSafe.Windows.exe"
if exist "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe" (
  copy /Y "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe" "%SHARESAFE_EXE%" >nul 2>&1
  if errorlevel 1 (
    echo ShareSafe is still running. Quit it from the tray menu, then open this launcher again to apply the update.
    pause
    exit /b 1
  )
  if exist "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe.config" copy /Y "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe.config" "%SHARESAFE_EXE%.config" >nul
  del "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe"
  if exist "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe.config" del "%~dp0WindowsApp\bin\ShareSafe.Windows.Pending.exe.config"
)
if not exist "%SHARESAFE_EXE%" (
  echo Build ShareSafe first: powershell -File scripts\build-windows.ps1
  pause
  exit /b 1
)
start "" "%SHARESAFE_EXE%" %*
