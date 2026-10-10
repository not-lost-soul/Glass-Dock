@echo off
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
cd /d "%~dp0"

echo Building GlassDock...
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:GlassDock.exe ^
  /r:"%FW%\WPF\PresentationCore.dll" ^
  /r:"%FW%\WPF\PresentationFramework.dll" ^
  /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" ^
  /r:"%FW%\System.Drawing.dll" ^
  GlassDock.cs

if errorlevel 1 (
  echo.
  echo BUILD FAILED. Copy the error lines above and send them to Claude.
  pause
  exit /b 1
)
echo.
echo Done! Double-click GlassDock.exe to start it.
pause
