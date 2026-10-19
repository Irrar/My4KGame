@echo off
chcp 936 >nul
setlocal

REM ============================================================
REM  一键打开 My4KGame 工程（双击本文件即可）
REM
REM  为什么需要它：Unity Hub 有时认不出命令行装的编辑器，
REM  直接双击 .unity 场景文件也可能因为路径记录失效而打不开。
REM ============================================================

set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe"
set "PROJECT=%~dp0"
if "%PROJECT:~-1%"=="\" set "PROJECT=%PROJECT:~0,-1%"

echo ============================================================
echo   My4KGame  4K 下落式音游
echo ------------------------------------------------------------
echo   编辑器 : %UNITY_EXE%
echo   工程   : %PROJECT%
echo ============================================================
echo.

if not exist "%UNITY_EXE%" (
    echo [错误] 找不到 Unity 编辑器：
    echo        %UNITY_EXE%
    echo.
    echo 请确认编辑器装了、或者改一下本文件第 13 行的 UNITY_EXE 路径。
    echo 找不到的话可以在 PowerShell 里搜一下：
    echo   Get-ChildItem "C:\Program Files" -Recurse -Depth 6 -Filter Unity.exe -ErrorAction SilentlyContinue
    echo.
    pause
    exit /b 1
)

if not exist "%PROJECT%\Assets" (
    echo [错误] 当前目录不是 Unity 工程（没有 Assets 文件夹）。
    echo        请把本文件放在 My4KGame 工程根目录（有 Assets 文件夹的那层）下再双击。
    echo.
    pause
    exit /b 1
)

echo 正在启动 Unity，第一次打开需要 2~5 分钟（要导入资源、编译脚本）...
echo 打开之后：
echo   1. 等左下角进度条转完
echo   2. 如果 Game 视图是一片蓝天灰地（标题写着 Untitled），说明场景没打开：
echo      菜单 My4KGame - 0. 打开游戏场景；或者 Project 面板进 Assets/Scenes 双击 My4KGame
echo   3. 按顶部的播放键，进入选曲界面，点任意难度开始玩
echo.
echo 场景没生成过的话，先点菜单 My4KGame - 1. 一键生成全部。
echo.
echo 窗口已经打开了吗？如果 Unity 没弹出来，请看桌面任务栏，它可能被挡住了。
echo.

start "" "%UNITY_EXE%" -projectPath "%PROJECT%"

timeout /t 8 >nul
