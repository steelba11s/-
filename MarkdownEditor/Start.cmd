@echo off
cd /d "%~dp0"
dotnet run --project "MarkdownEditor.App\MarkdownEditor.App.csproj" -- %*
if errorlevel 1 pause
