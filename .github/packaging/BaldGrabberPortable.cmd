@echo off
setlocal
set "PORTABLE_ROOT=%~dp0"
set "BALDGRABBER_DATA=%PORTABLE_ROOT%Data"
start "" "%PORTABLE_ROOT%App\BaldGrabber\BaldGrabber.exe"
