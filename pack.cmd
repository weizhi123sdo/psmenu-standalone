@echo off
rem ===========================================================================
rem pack.cmd —— PSMenu 独立版一键构建与生成分发资产包
rem ===========================================================================
cd /d %~dp0
echo [*] 正在编译源码...
test\roslyn\tools\csc.exe @core.rsp
if errorlevel 1 (
    echo [!] 编译失败
    exit /b 1
)
echo [+] 编译成功 -> out\PSMenu.exe

if not exist release mkdir release
copy /y out\PSMenu.exe release\ >nul
copy /y lib\Newtonsoft.Json.dll release\ >nul
copy /y lib\FontAwesomeIconsWpf.dll release\ >nul
if exist 使用说明.md copy /y 使用说明.md release\ >nul

echo [*] 打包 release\psmenu_standalone.zip 与生成 latest.json ...
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\generate_pack.ps1
if errorlevel 1 (
    echo [!] 打包生成失败
    exit /b 1
)
echo [+] 全部打包与清单生成完成！
