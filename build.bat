@echo off
echo --- Voxel C# Build Script ---
"C:\Program Files\dotnet\dotnet.exe" --version >nul 2>&1
if %errorlevel% neq 0 (
    echo .NET SDK not found! Please install it from: https://dotnet.microsoft.com/download
    pause
    exit /b
)
echo Restoring libraries (Silk.NET)...
"C:\Program Files\dotnet\dotnet.exe" restore
echo Building project...
"C:\Program Files\dotnet\dotnet.exe" build -c Release
echo.
echo Success! Your EXE is in: bin/Release/net8.0/Voxel.exe
pause
