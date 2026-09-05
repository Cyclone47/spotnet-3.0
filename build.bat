@echo off
setlocal enabledelayedexpansion

title Spotnet 3.0 - Build and Test

echo ======================================================================
echo                  Spotnet 3.0 Build System
echo ======================================================================
echo.

set "SOLUTION_DIR=%~dp0src\Spotnet"

:: Check for dotnet CLI
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET SDK dotnet CLI was not found in PATH!
    echo Please install the .NET SDK from https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo [1/3] Restoring and building Spotnet 3.0 Solution Release x86...
echo.
dotnet build "%SOLUTION_DIR%\Spotnet.sln" -c Release -v minimal
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [BUILD FAILED] An error occurred while compiling Spotnet.sln.
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Running Automated Unit and Integration Tests...
echo.
dotnet test "%SOLUTION_DIR%\Spotnet.Tests\Spotnet.Tests.csproj" -c Release --no-build -v normal
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [WARNING] One or more tests failed.
) else (
    echo [TESTS PASSED] All test suites completed successfully.
)

echo.
echo [3/3] Build Verification
set "OUTPUT_EXE=%SOLUTION_DIR%\Spotnet\bin\Release\net472\Spotnet.exe"

if exist "%OUTPUT_EXE%" (
    echo ======================================================================
    echo [BUILD SUCCESSFUL] Spotnet 3.0 compiled successfully!
    echo.
    echo Binary location:
    echo "%OUTPUT_EXE%"
    echo ======================================================================
    echo.
) else (
    echo [ERROR] Expected output binary not found at:
    echo "%OUTPUT_EXE%"
    echo.
    pause
    exit /b 1
)

set /p RUN_APP="Would you like to launch Spotnet 3.0 now? (Y/N): "
if /i "%RUN_APP%"=="Y" (
    echo Launching Spotnet 3.0...
    start "" "%OUTPUT_EXE%"
)

echo.
echo Done.
