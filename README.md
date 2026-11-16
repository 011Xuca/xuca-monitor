<img src="Pulso.ico" width="96" alt="Icono de Pulso">

# Pulso

App gratis para **Windows 10 y 11** que vigila tu PC desde la bandeja (al lado del reloj): **internet**, **USB**, **rendimiento en juegos**, **errores de Windows** y **temperaturas/estado del hardware**. Cuando se corta internet, sube el ping, se desconecta algo, bajan los FPS, la pantalla se pone negra, hay un pantallazo azul o el procesador/placa de video se calienta de más, te dice **qué fue y qué probar**. Además trae un **overlay de FPS** y un **modo benchmark** que registra cada partida, te dice qué problemas tuvo y la compara con la anterior. En Windows 7 SP1 y 8.1 anda recortada (ver [más abajo](#windows-7-8-y-81)).

## Descargar

**[Descargar Pulso.exe](https://github.com/011Xuca/pulse/releases/latest/download/Pulso.exe)**

No hace falta instalar nada: lo abrís y listo. Usa el .NET que ya trae Windows (si a tu Windows le falta, la app te avisa y te lleva a la descarga de .NET Framework 4.8).

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
| **PC** | Temperatura, uso, velocidad y potencia **reales** del procesador y la placa de video (núcleo, hot spot y memoria), RAM (y si el XMP/EXPO está activo o anda en single channel), discos (temperatura y vida útil), ventiladores, placa madre y BIOS. Todo en verde, amarillo o rojo. Avisa cosas concretas: procesador frenándose por calor, hot spot muy separado del núcleo (pasta térmica), ventiladores parados, VRAM o RAM llena (y qué la llena), conector 12VHPWR sobrecargado, disco gastado. Tiene 5 categorías (General, CPU, GPU, RAM, Discos), cada una con su registro y su casilla "Guardar avisos". La temperatura del procesador y de la placa de video se pinta amarilla desde 85 °C (sin aviso: jugando es normal) y roja, con aviso, desde 90 °C. En procesadores con un límite más bajo (como los Ryzen X3D, que llegan a 89 °C) el rojo arranca 3 °C antes de ese límite. |
| **Overlay de FPS** | Encima del juego: FPS, promedio, mínimo y máximo de la sesión, 1 % y 0.1 % low, frametime con gráfico, y uso, temperatura, velocidad y consumo de CPU y GPU, VRAM y RAM. Se arma con una plantilla de texto y trae modelos listos (incluido uno para videos comparativos). No inyecta nada en el juego: los FPS salen de los eventos de Windows (DirectX 9 a 12), como PresentMon. OBS lo captura como la ventana "Pulso Overlay". |
| **Modo benchmark** | Se prende en los ajustes del overlay. Registra cada partida de los juegos de la lista (desde que se abre hasta que se cierra) y al terminar deja en **Rendimiento** un resumen: FPS promedio, 1 % y 0.1 % low de **toda** la corrida, frametime, tirones, y promedio y máximo de temperatura, uso, frecuencia y consumo de CPU y GPU, y de la RAM. Te dice los **problemas que encontró en los datos**: throttling térmico o de energía, tirones, FPS desparejos, cuello de botella del procesador, RAM casi llena y avisos de Rendimiento o de Red que pasaron durante la prueba. Cada corrida se guarda y se compara con la anterior del mismo juego. |

- Cada pestaña se puede **prender o apagar** con la casilla **Activado** (o con clic derecho en el ícono de la bandeja). Apagada no gasta nada.
- Muestra las opciones de red del [Xuca OPtimizer](https://github.com/011Xuca/xuca-optimizer) que tenés aplicadas, así sabés si alguna tiene que ver.
- Lee los errores de Windows de ese momento y te los muestra junto al aviso.
- **Se actualiza sola:** cada vez que la abrís se fija una vez en GitHub si hay una versión nueva. Si hay, aparece un cartel amarillo (y una opción en el menú de la bandeja); lo tocás, se baja de GitHub y se vuelve a abrir. Antes de reemplazarse verifica que la descarga esté completa, y si la versión nueva no llega a abrir vuelve sola a la anterior.

## Cómo se usa

1. Abrí `Pulso.exe`. Queda en la bandeja, al lado del reloj.
2. Marcá **Iniciar con Windows** para que arranque solo (arranca escondido en la bandeja).
3. Usá la PC normal. Cuando pase algo, abrí la app desde la bandeja y vas a ver qué pasó.

- Minimizar o cerrar con la X la deja en la bandeja. Para cerrarla del todo: clic derecho en el ícono > **Salir**.
- La ventana nunca se abre sola: los avisos se guardan y los ves cuando quieras.
- **Tema** claro, oscuro o el del sistema, e **idioma** español o inglés (la primera vez usa el de Windows).
- **Zoom:** Ctrl + rueda del mouse (o Ctrl + / Ctrl -) agranda o achica el texto. Ctrl + 0 lo vuelve al tamaño normal.

## Dónde guarda las cosas

Todo queda en **Documentos\Pulso**:

- `registro_*.txt` (red, usb, rendimiento, errores y pc con sus categorías): todo lo que detectó cada pestaña, con hora (botón **Abrir registro**).
- `historial_*.txt`: lo que ves en la ventana (los últimos 300 avisos de cada pestaña). **Limpiar** lo borra.
- `pc_ultimos_minutos.txt`: cómo estuvo la PC en los últimos 5 minutos (sirve para saber si un apagón fue por temperatura).
- `ajustes_monitores.txt`: tema, idioma, zoom y qué pestañas están prendidas.
- `overlay.txt`: los ajustes del overlay de FPS.
- `benchmarks\`: un `.txt` por corrida del modo benchmark (el mismo resumen que ves en Rendimiento) y `benchmarks.csv`, con una fila por corrida (separado por `;`) para compararlas o abrirlas en una planilla.
- `crash.txt`: solo si la app tuvo un error.

No manda nada a ningún servidor. Lo único que sale a internet es el ping de la pestaña Red (al router, a 1.1.1.1 y a 8.8.8.8), la consulta de versión a GitHub y las descargas que vos pedís (la actualización o PawnIO).

## Windows 7, 8 y 8.1

Pulso está hecha para **Windows 10 y 11**. En **Windows 7 SP1 y 8.1 anda recortada**:

- **Andan:** Red, USB, Errores y Rendimiento (este último sin los datos de la placa de video ni de la velocidad del procesador).
- **No están:** la pestaña **PC** (temperaturas, consumo, frecuencias y estado del hardware: el driver PawnIO es para Windows 10 y 11), el **overlay de FPS** y el **modo benchmark** (usan eventos de Windows 10 y 11). La app lo avisa en esa pestaña y en el botón del overlay.
- Hace falta **.NET Framework 4.8**: esos Windows no lo traen, y si falta la app te lleva a la descarga.
- **Windows 8 (sin el .1) no es compatible**: no puede instalar el .NET que hace falta. La actualización a 8.1 es gratis.

## Compilarlo vos

El código está en `src` (C# 5 para .NET Framework 4.8). Abrí `compilar.bat` y te arma `Pulso.exe` con el compilador que ya trae Windows. No hace falta instalar nada. Las dll de la carpeta `lib` (sensores) se meten **adentro** del `.exe`: el resultado es un solo archivo que anda en cualquier carpeta.

### Publicar una versión

1. Cambiá `Version` en `src/App.cs` y corré `compilar.bat`.
2. Creá un Release con el tag `v` + versión (por ejemplo `v1.0.4`) y subí `Pulso.exe`. La app busca ese archivo en el último Release para actualizarse sola.

## Créditos

Hecho por **011Xuca**. Redes: https://linktr.ee/011xuca
Si te sirvió, podés apoyarme en Ko-fi: https://ko-fi.com/011xuca

Las temperaturas y sensores de hardware usan [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (licencia MPL-2.0).
