@echo off

cd ..\ConsoleMonthlyCsvCombiner\

dotnet publish -p:PublishProfile=FolderProfile.pubxml

:: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o ./../Resources/dist

echo.
set "SCRIPT_DIR=%~dp0"
set "DIST_DIR=%SCRIPT_DIR%dist"
echo Published to %DIST_DIR%

powershell -Command "Get-FileHash -Path '.\..\Resources\dist\ConsoleMonthlyCsvCombiner.exe' -Algorithm SHA256"

pause