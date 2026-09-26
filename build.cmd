@echo off
setlocal
cd /d "%~dp0"
echo Building DJL_6's 工具箱 single-file executable...
dotnet publish DJL_6sToolbox.Desktop\DJL_6sToolbox.Desktop.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if errorlevel 1 exit /b 1
echo.
echo Build complete: dist\DJL_6sToolbox.exe
endlocal
