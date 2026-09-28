@echo off
setlocal

set "PHOTOIDENTITY_START_ATTEMPT=0"
set "PHOTOIDENTITY_MAX_START_ATTEMPTS=23"
if defined LOCALAPPDATA (
    set "PHOTOIDENTITY_API_STDERR=%LOCALAPPDATA%\PhotoIdentity\launcher-logs\api.stderr.log"
) else (
    set "PHOTOIDENTITY_API_STDERR=%~dp0.photoidentity\launcher-logs\api.stderr.log"
)

:photoidentity_start
set /a PHOTOIDENTITY_START_ATTEMPT+=1 >nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-PhotoIdentity.ps1" -PublishPathOverride "%~dp0app" %*
set "PHOTOIDENTITY_EXIT_CODE=%ERRORLEVEL%"
if "%PHOTOIDENTITY_EXIT_CODE%"=="0" goto photoidentity_done
if %PHOTOIDENTITY_START_ATTEMPT% GEQ %PHOTOIDENTITY_MAX_START_ATTEMPTS% goto photoidentity_failed
if not exist "%PHOTOIDENTITY_API_STDERR%" goto photoidentity_failed
findstr /C:"SqlState: 57P03" "%PHOTOIDENTITY_API_STDERR%" >nul 2>&1
if errorlevel 1 goto photoidentity_failed

echo PostgreSQL is still starting ^(57P03^); retrying Photo Identity startup ^(attempt %PHOTOIDENTITY_START_ATTEMPT% of %PHOTOIDENTITY_MAX_START_ATTEMPTS%^)...
powershell.exe -NoLogo -NoProfile -Command "Start-Sleep -Seconds 2" >nul 2>&1
goto photoidentity_start

:photoidentity_failed
echo.
echo Photo Identity could not start. Review the launcher message above.
if not "%PHOTOIDENTITY_NONINTERACTIVE%"=="1" pause
exit /b %PHOTOIDENTITY_EXIT_CODE%

:photoidentity_done
exit /b 0
