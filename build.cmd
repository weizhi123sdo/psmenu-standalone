@echo off
rem PS便捷菜单 独立版 —— 一键构建（Roslyn csc，.NET Framework 4.8 目标）
cd /d %~dp0
test\roslyn\tools\csc.exe @core.rsp
if errorlevel 1 (
  echo 构建失败
  exit /b 1
)
copy /y lib\Newtonsoft.Json.dll out\ >nul
copy /y lib\FontAwesomeIconsWpf.dll out\ >nul
echo 构建完成: out\PSMenu.exe
