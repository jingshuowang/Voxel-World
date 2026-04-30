@echo off
cd Boot
dotnet run -c Release
if %errorlevel% neq 0 pause
