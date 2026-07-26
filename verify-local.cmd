@echo off
setlocal enableextensions

set CONFIGURATION=Release
set ROOT=%~dp0
set ROOT=%ROOT:~0,-1%
set INSTALL_MODELS=0
set SKIP_MODELS=0

:parse_args
if /i "%1"=="--install-models" ( set INSTALL_MODELS=1 & shift & goto :parse_args )
if /i "%1"=="--skip-models"    ( set SKIP_MODELS=1   & shift & goto :parse_args )
if /i "%1"=="--debug"          ( set CONFIGURATION=Debug & shift & goto :parse_args )

echo.
echo == .NET SDK ==
dotnet --version
if %ERRORLEVEL% neq 0 goto :fail

echo.
echo == Restore and build ==
dotnet restore "%ROOT%\PhotoIdentity.slnx"
if %ERRORLEVEL% neq 0 goto :fail
dotnet build "%ROOT%\PhotoIdentity.slnx" --configuration %CONFIGURATION% --no-restore
if %ERRORLEVEL% neq 0 goto :fail

echo.
echo == Automated tests ==
dotnet test "%ROOT%\PhotoIdentity.slnx" --configuration %CONFIGURATION% --no-restore
if %ERRORLEVEL% neq 0 goto :fail

echo.
echo == Living-document validation ==
dotnet run --project "%ROOT%\tools\PhotoIdentity.Docs" --configuration %CONFIGURATION% --no-build -- validate
if %ERRORLEVEL% neq 0 goto :fail

echo.
echo == Generated-document consistency ==
dotnet run --project "%ROOT%\tools\PhotoIdentity.Docs" --configuration %CONFIGURATION% --no-build -- generate --check
if %ERRORLEVEL% neq 0 goto :fail

if "%SKIP_MODELS%"=="1" goto :done

if "%INSTALL_MODELS%"=="1" (
    echo.
    echo == Model installation ==
    dotnet run --project "%ROOT%\tools\PhotoIdentity.Models" --configuration %CONFIGURATION% --no-build -- install --root "%ROOT%"
    if %ERRORLEVEL% neq 0 goto :fail
)

echo.
echo == Model verification ==
dotnet run --project "%ROOT%\tools\PhotoIdentity.Models" --configuration %CONFIGURATION% --no-build -- verify --root "%ROOT%"
if %ERRORLEVEL% neq 0 goto :fail

:done
echo.
echo == All checks passed ==
exit /b 0

:fail
echo.
echo == Verification failed ==
exit /b 1