@echo off
title SoundMaster
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server.ps1"
if errorlevel 1 (
  echo.
  echo SoundMaster could not start. See the message above.
  pause
)
