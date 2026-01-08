@echo off
echo Building MediaManager for Windows...
echo.

cd /d "%~dp0"

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false -p:PublishReadyToRunComposite=false -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:InvariantGlobalization=true -p:TrimMode=full -p:TrimAnalysis=false -p:EventSourceSupport=false -p:UseSystemResourceKeys=true

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed!
    exit /b %ERRORLEVEL%
)

echo.
echo Build successful!
echo.

set PUBLISH_PATH=bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\MediaManager.exe
set TARGET_DIR=%~dp0..\..\..\ru.valentderah.media-manager.sdPlugin\bin
set TARGET_PATH=%TARGET_DIR%\MediaManager.exe

if not exist "%PUBLISH_PATH%" (
    echo ERROR: Built file not found at: %PUBLISH_PATH%
    echo Please check the build output above for errors.
    exit /b 1
)

if not exist "%TARGET_DIR%" (
    mkdir "%TARGET_DIR%"
)

echo Copying MediaManager.exe to plugin bin folder...
copy /Y "%PUBLISH_PATH%" "%TARGET_PATH%"

if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to copy file to: %TARGET_PATH%
    exit /b %ERRORLEVEL%
)

if not exist "%TARGET_PATH%" (
    echo ERROR: File was not copied successfully!
    exit /b 1
)

echo.
echo Done! MediaManager.exe copied to plugin bin folder.
echo File location: %TARGET_PATH%

