@echo off
cd /d "%~dp0.."
echo Compiling...
javac -cp "lib/*;." system/*.java rendering/*.java
if %errorlevel% neq 0 (
    echo Compilation Failed.
    pause
    exit /b %errorlevel%
)
echo Running...
java -cp "lib/*;." system.Main
pause
