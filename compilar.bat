@echo off
rem Compila Xuca Monitor con el compilador que ya trae Windows (.NET Framework 4.8). No hace falta instalar nada.
rem El XucaMonitor.exe nuevo queda en esta misma carpeta.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /win32manifest:app.manifest /win32icon:XucaMonitor.ico /out:XucaMonitor.exe src\App.cs src\Eventos.cs src\Usb.cs src\Red.cs
pause
