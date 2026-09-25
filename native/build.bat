@echo off
rem Builds SoundMaster.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
  /win32icon:"%~dp0app.ico" /resource:"%~dp0app.ico",SoundMaster.app.ico ^
  /out:"%~dp0..\SoundMaster.exe" ^
  "%~dp0src\ComInterop.cs" "%~dp0src\AudioManager.cs" "%~dp0src\AudioPolicyConfig.cs" "%~dp0src\Theme.cs" ^
  "%~dp0src\AppIcons.cs" "%~dp0src\Views.cs" "%~dp0src\FlowView.cs" "%~dp0src\MainForm.cs" ^
  "%~dp0src\SelfTest.cs" "%~dp0src\Program.cs" "%~dp0src\AssemblyInfo.cs"
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Built ..\SoundMaster.exe
