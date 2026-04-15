@echo off
echo Building DS1ParamHelper.dll...
echo.
echo This requires Visual Studio to be installed.
echo If build fails, open DS1ParamEditor.sln and build DS1ParamHelper project manually.
echo.

REM Try to find MSBuild
set MSBUILD=""
if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
)
if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
)
if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
)

if %MSBUILD%=="" (
    echo ERROR: MSBuild not found. Please install Visual Studio 2022.
    echo Or open DS1ParamEditor.sln and build DS1ParamHelper project manually.
    pause
    exit /b 1
)

echo Found MSBuild: %MSBUILD%
echo.

%MSBUILD% DS1ParamHelper.vcxproj /p:Configuration=Release /p:Platform=x64

if %ERRORLEVEL% == 0 (
    echo.
    echo Build successful!
    echo DLL location: ..\DS1ParamEditor\bin\Release\net8.0-windows\DS1ParamHelper.dll
) else (
    echo.
    echo Build failed. Try opening DS1ParamEditor.sln and building manually.
)

pause
