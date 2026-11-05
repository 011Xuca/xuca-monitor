@echo off
rem Compila Xuca Monitor con el compilador que ya trae Windows (.NET Framework 4.8). No hace falta instalar nada.
rem El XucaMonitor.exe nuevo queda en esta misma carpeta. Las dll de la carpeta "lib" (sensores de CPU/GPU/RAM/discos)
rem van ADENTRO del .exe: el .exe anda solo, sin la carpeta "lib" al lado.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set LIBS=/resource:lib\LibreHardwareMonitorLib.dll,lib.LibreHardwareMonitorLib.dll /resource:lib\System.Memory.dll,lib.System.Memory.dll /resource:lib\RAMSPDToolkit-NDD.dll,lib.RAMSPDToolkit-NDD.dll /resource:lib\HidSharp.dll,lib.HidSharp.dll /resource:lib\DiskInfoToolkit.dll,lib.DiskInfoToolkit.dll /resource:lib\System.Runtime.CompilerServices.Unsafe.dll,lib.System.Runtime.CompilerServices.Unsafe.dll /resource:lib\BlackSharp.Core.dll,lib.BlackSharp.Core.dll /resource:lib\System.Numerics.Vectors.dll,lib.System.Numerics.Vectors.dll /resource:lib\System.Buffers.dll,lib.System.Buffers.dll
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /unsafe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Management.dll /r:lib\LibreHardwareMonitorLib.dll %LIBS% /win32manifest:app.manifest /win32icon:XucaMonitor.ico /out:XucaMonitor.exe src\App.cs src\Actualizar.cs src\Eventos.cs src\Usb.cs src\Red.cs src\Rend.cs src\Errores.cs src\Pc.cs src\PcVista.cs src\Sensores.cs src\Overlay.cs
pause
