@echo off
echo Compiling...
javac -cp "javalib/*;." *.java
if %errorlevel% neq 0 (
    echo Compilation Failed.
    pause
    exit /b %errorlevel%
)
echo Running...
java -cp "javalib/*;." Main
pause
