@echo off
cd /d "%~dp0"
dotnet run --project SimpleMap.csproj
if errorlevel 1 pause
