@echo off
echo ========================================
echo   Terraria Auto Fisher - Build Script
echo ========================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK is not installed!
    echo Please install .NET 6.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    pause
    exit /b 1
)

echo [1/3] Cleaning previous builds...
dotnet clean -c Release >nul 2>&1

echo [2/3] Building standard version (requires .NET 6.0 Runtime)...
dotnet build -c Release
if errorlevel 1 (
    echo Build failed! Check errors above.
    pause
    exit /b 1
)

echo.
echo [3/3] Building self-contained version (no dependencies)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo Publish failed! Check errors above.
    pause
    exit /b 1
)

echo.
echo ========================================
echo   BUILD SUCCESSFUL!
echo ========================================
echo.
echo Standard Build:
echo   Location: bin\Release\net6.0-windows\
echo   File: TerrariaAutoFisher.exe
echo   Requirements: .NET 6.0 Runtime
echo   Size: ~200 KB (+ dependencies)
echo.
echo Self-Contained Build:
echo   Location: bin\Release\net6.0-windows\win-x64\publish\
echo   File: TerrariaAutoFisher.exe
echo   Requirements: None - runs on any Windows 10/11
echo   Size: ~70-100 MB (includes everything)
echo.
echo ========================================

echo.
choice /C YN /M "Open output folders?"
if errorlevel 2 goto end
if errorlevel 1 (
    start "" "bin\Release\net6.0-windows\"
    start "" "bin\Release\net6.0-windows\win-x64\publish\"
)

:end
echo.
echo Press any key to exit...
pause >nul
