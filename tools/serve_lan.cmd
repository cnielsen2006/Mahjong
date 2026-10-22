@echo off
rem Serves the WebGL build to phones/tablets on the local network. Used by the "Mahjong LAN server" desktop shortcut.
title Mahjong LAN server
cd /d "%~dp0.."
python tools\serve_webgl.py 8765
echo.
pause
