@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   SynToolkit Build and Installer Script
echo ============================================
echo.

:: Set paths
set "PROJECT_DIR=%~dp0SynToolkit"
set "SOLUTION_DIR=%~dp0"
set "PUBLISH_DIR=%~dp0artifacts\publish"
set "OUTPUT_DIR=%~dp0Installer\Output"
set "INSTALLER_DIR=%~dp0Installer"

:: Check if dotnet is available
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: .NET SDK not found. Please install .NET 8 SDK.
    echo Download from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: Display .NET version
echo Found .NET SDK:
dotnet --version
echo.

:: Clean previous builds
echo [1/4] Cleaning previous builds...
if exist "%PROJECT_DIR%\bin\x64\Release" rd /s /q "%PROJECT_DIR%\bin\x64\Release"
if exist "%PROJECT_DIR%\obj\x64\Release" rd /s /q "%PROJECT_DIR%\obj\x64\Release"
if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"
if exist "%OUTPUT_DIR%" rd /s /q "%OUTPUT_DIR%"
mkdir "%PUBLISH_DIR%"
mkdir "%OUTPUT_DIR%"
echo Done.
echo.

:: Restore NuGet packages
echo [2/4] Restoring NuGet packages...
dotnet restore "%PROJECT_DIR%\SynToolkit.csproj" -r win-x64
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to restore packages.
    pause
    exit /b 1
)
echo Done.
echo.

:: Build and publish the application
echo [3/4] Building and publishing SynToolkit (Release, x64)...
dotnet publish "%PROJECT_DIR%\SynToolkit.csproj" ^
    -c Release ^
    -r win-x64 ^
    -p:Platform=x64 ^
    -p:PublishSingleFile=false ^
    -p:SelfContained=true ^
    -p:PublishReadyToRun=true ^
    --output "%PUBLISH_DIR%"

if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed.
    pause
    exit /b 1
)
echo Done.
echo.

:: Check if Inno Setup is installed
echo [4/4] Creating installer...
set "INNO_PATH="

:: Check common Inno Setup installation paths
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
) else if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
)

if defined INNO_PATH (
    echo Found Inno Setup at: !INNO_PATH!
    "!INNO_PATH!" "%INSTALLER_DIR%\setup.iss"
    if %ERRORLEVEL% neq 0 (
        echo WARNING: Installer creation failed.
    ) else (
        echo.
        echo ============================================
        echo   BUILD COMPLETE!
        echo ============================================
        echo.
        echo Installer created at:
        echo   %OUTPUT_DIR%\SynToolkit-Setup-1.7.0.exe
        echo.
        echo Published files at:
        echo   %PUBLISH_DIR%
    )
) else (
    echo.
    echo WARNING: Inno Setup not found.
    echo.
    echo To create the installer:
    echo   1. Download Inno Setup from: https://jrsoftware.org/isdl.php
    echo   2. Install it
    echo   3. Run this script again, OR
    echo   4. Open Installer\setup.iss in Inno Setup and compile
    echo.
    echo ============================================
    echo   BUILD COMPLETE (without installer)
    echo ============================================
    echo.
    echo Published files are at:
    echo   %PUBLISH_DIR%
    echo.
    echo You can run SynToolkit.exe directly from that folder.
)

echo.
pause
