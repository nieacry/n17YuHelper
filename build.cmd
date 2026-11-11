@echo off
rem Build n17 YuHelper: publish\ and a copy in versions\vX.Y\
rem Usage: build.cmd 0.2
setlocal
chcp 65001 >nul
set ROOT=%~dp0
if "%~1"=="" (
  echo Specify the version, for example: build.cmd 0.2
  exit /b 1
)
set VER=%~1

dotnet publish "%ROOT%src\n17YuHelper\n17YuHelper.csproj" -c Release -o "%ROOT%publish" -p:Version=%VER%.0
if errorlevel 1 exit /b 1

if not exist "%ROOT%versions\v%VER%" mkdir "%ROOT%versions\v%VER%"
copy /Y "%ROOT%publish\n17YuHelper.exe" "%ROOT%versions\v%VER%\" >nul
echo Done: publish\ and versions\v%VER%\
