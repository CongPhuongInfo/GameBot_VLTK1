@echo off
title Build VLTKBotReader - .NET 9
echo ============================================
echo  Build VLTKBotReader - net9.0-windows x64
echo ============================================

dotnet --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [!!] Khong tim thay dotnet CLI.
    echo      Tai .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0
    pause & exit /b 1
)

echo [INFO] Restore packages...
dotnet restore VLTKBotReader.vbproj
if %ERRORLEVEL% neq 0 ( echo [!!] Restore that bai! & pause & exit /b 1 )

echo [INFO] Build...
dotnet build VLTKBotReader.vbproj -c Release --no-restore

if %ERRORLEVEL%==0 (
    echo.
    echo  [OK] BUILD THANH CONG!
    echo  Output: build\Release\VLTKBot.exe
    echo ============================================
    if exist "libs\tessdata" (
        if not exist "build\Release\tessdata" mkdir "build\Release\tessdata"
        xcopy /y /q "libs\tessdata\*.*" "build\Release\tessdata\" >nul 2>&1
        echo  [OK] Da copy tessdata\
    )
    echo.
    echo  Them anh mob/item vao:
    echo    build\Release\templates\mobs\   <- chup anh PNG ten mob tren dau
    echo    build\Release\templates\items\  <- icon item drop
    echo    build\Release\templates\npcs\   <- anh NPC
    echo.
    set /p RUN="Chay luon? (y/n): "
    if /i "%RUN%"=="y" start "" "build\Release\VLTKBot.exe"
) else (
    echo [!!] Build that bai!
)
pause
