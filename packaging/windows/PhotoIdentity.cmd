@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-PhotoIdentity.ps1" -PublishPathOverride "%~dp0app" %*
set "PHOTOIDENTITY_EXIT_CODE=%ERRORLEVEL%"
if not "%PHOTOIDENTITY_EXIT_CODE%"=="0" (
    echo.
    echo Photo Identity could not start. Review the launcher message above.
    pause
)
exit /b %PHOTOIDENTITY_EXIT_CODE%
