@echo off
setlocal
call "%~dp0build.bat"
if errorlevel 1 exit /b 1
start "" "%~dp0GlassDock.exe"
