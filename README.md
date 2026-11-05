<img src="XucaMonitor.ico" width="96" alt="Icono de Xuca Monitor">

# Xuca Monitor

App gratis para **Windows 10 y 11** que vigila tu PC desde la bandeja (al lado del reloj): **internet**, **USB**, **rendimiento en juegos**, **errores de Windows** y **temperaturas/estado del hardware**. Cuando se corta internet, sube el ping, se desconecta algo, bajan los FPS, la pantalla se pone negra, hay un pantallazo azul o el procesador/placa de video se calienta de más, te dice **qué fue y qué probar**.

## Descargar

**[Descargar XucaMonitor.exe](https://github.com/011Xuca/xuca-monitor/releases/latest/download/XucaMonitor.exe)**

No hace falta instalar nada: lo abrís y listo. Usa el .NET que ya trae Windows.

> La primera vez Windows puede mostrar un cartel azul (SmartScreen) porque el `.exe` no está firmado. Tocá **Más información** y después **Ejecutar de todas formas**.

> **Pide permisos de administrador al abrir.** Es necesario para leer los sensores reales (temperaturas, vatios, discos). Si le decís que no a ese permiso, la app no abre.
>
> **Temperatura del procesador:** además hace falta **PawnIO**, un driver gratis y firmado (el mismo que usan LibreHardwareMonitor y FanControl). La pestaña PC tiene un botón para instalarlo; sin él se ve todo lo demás (placa de video, RAM, discos).

## Qué hace

| Pestaña | Para qué sirve |
|---|---|
| **Red** | Avisa cuando se **corta internet** y te dice si fue la placa de red (cable o WiFi), el router o tu proveedor. También avisa cuando el **ping sube y se queda alto (lag)**: mide el ping al router, cuánto está bajando o subiendo la PC, qué programas suelen usar mucho internet y si algún driver está perdiendo memoria. |
| **USB** | Cuando suena el ruido de Windows de "se desconectó algo", te dice **qué aparato fue** (mouse, teclado, auriculares, monitor, Bluetooth...), si volvió a conectarse solo y qué conviene probar. |
| **Rendimiento** | Mientras jugás en pantalla completa, vigila procesador, RAM, disco y placa de video. Cuando algo **hace bajar los FPS** te dice el culpable: un programa de fondo (Windows Update, el antivirus, un launcher descargando...), el procesador que se frena por temperatura, la RAM llena o el disco al 100 %. También avisa si **otro programa le saca el foco al juego** sin que toques nada (Discord, Steam, un aviso de Windows...). No mide los FPS exactos: detecta lo que los hace bajar. Con la pestaña PC prendida sabe si el procesador se frena por temperatura o por energía. |
| **Errores** | Te avisa cuando la **pantalla se pone negra** porque se colgó el driver de video, cuando hay un **pantallazo azul** (con el código explicado), cuando la PC **se apaga de golpe**, cuando hay errores de hardware, del disco o falta de memoria, y cuando un **juego o programa se cierra solo** (y en qué falló: driver de NVIDIA/AMD, un overlay...). Con la pestaña PC prendida también dice **cómo estaba la PC justo antes** y si fue por calor o no. |
| **PC** | Temperatura, uso, velocidad y potencia **reales** del procesador y la placa de video (núcleo, hot spot y memoria), RAM (y si el XMP/EXPO está activo o anda en single channel), discos (temperatura y vida útil), ventiladores, placa madre y BIOS. Todo en verde, amarillo o rojo. Avisa cosas concretas: procesador frenándose por calor, hot spot muy separado del núcleo (pasta térmica), ventiladores parados, VRAM o RAM llena (y qué la llena), conector 12VHPWR sobrecargado, disco gastado. Tiene 5 categorías (General, CPU, GPU, RAM, Discos), cada una con su registro y su casilla "Guardar avisos". |

- Cada pestaña se puede **prender o apagar** con la casilla **Activado** (o con clic derecho en el ícono de la bandeja). Apagada no gasta nada.
- Muestra las opciones de red del [Xuca OPtimizer](https://github.com/011Xuca/xuca-optimizer) que tenés aplicadas, así sabés si alguna tiene que ver.
- Lee los errores de Windows de ese momento y te los muestra junto al aviso.
- **Se actualiza sola:** cuando hay una versión nueva aparece un cartel amarillo (y una opción en el menú de la bandeja). Lo tocás, se baja de GitHub y se vuelve a abrir.

## Cómo se usa

1. Abrí `XucaMonitor.exe`. Queda en la bandeja, al lado del reloj.
2. Marcá **Iniciar con Windows** para que arranque solo (arranca escondido en la bandeja).
3. Usá la PC normal. Cuando pase algo, abrí la app desde la bandeja y vas a ver qué pasó.

- Minimizar o cerrar con la X la deja en la bandeja. Para cerrarla del todo: clic derecho en el ícono > **Salir**.
- La ventana nunca se abre sola: los avisos se guardan y los ves cuando quieras.
- **Tema** claro, oscuro o el del sistema, e **idioma** español o inglés (la primera vez usa el de Windows).
- **Zoom:** Ctrl + rueda del mouse (o Ctrl + / Ctrl -) agranda o achica el texto. Ctrl + 0 lo vuelve al tamaño normal.

## Dónde guarda las cosas

Todo queda en **Documentos\Xuca Monitor**:

- `registro_*.txt` (red, usb, rendimiento, errores y pc con sus categorías): todo lo que detectó cada pestaña, con hora (botón **Abrir registro**).
- `historial_*.txt`: lo que ves en la ventana (los últimos 300 avisos de cada pestaña). **Limpiar** lo borra.
- `pc_ultimos_minutos.txt`: cómo estuvo la PC en los últimos 5 minutos (sirve para saber si un apagón fue por temperatura).
- `ajustes_monitores.txt`: tema, idioma, zoom y qué pestañas están prendidas.

## Compilarlo vos

El código está en `src` (C# 5 para .NET Framework 4.8). Abrí `compilar.bat` y te arma `XucaMonitor.exe` con el compilador que ya trae Windows. No hace falta instalar nada. Las dll de la carpeta `lib` (sensores) se meten **adentro** del `.exe`: el resultado es un solo archivo que anda en cualquier carpeta.

## Créditos

Hecho por **011Xuca**. Redes: https://linktr.ee/011xuca
Si te sirvió, podés apoyarme en Ko-fi: https://ko-fi.com/011xuca

Las temperaturas y sensores de hardware usan [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (licencia MPL-2.0).
