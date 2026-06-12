@echo off
title Ambilight Controller Launcher

:: Build the project if needed
dotnet build "%~dp0AmbilightControllerForm.csproj"

:: Run the executable directly detached from console
start "" "%~dp0bin\Debug\net9.0-windows\AmbilightControllerForm.exe"