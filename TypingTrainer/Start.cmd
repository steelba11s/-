@echo off
cd /d "%~dp0"
dotnet run --project "TypingTrainer.csproj" -- %*
if errorlevel 1 pause
