<img src="XucaMonitor.ico" width="96" alt="Icono de Xuca Monitor">

# Xuca Monitor

App gratis para **Windows 10 y 11** que vigila tu PC desde la bandeja (al lado del reloj): **internet**, **USB**, **rendimiento en juegos** y **errores de Windows**. Cuando se corta internet, sube el ping, se desconecta algo, bajan los FPS, la pantalla se pone negra o hay un pantallazo azul, te dice **qué fue y qué probar**. Pesa unos 150 KB y casi no gasta memoria ni procesador.

## Descargar

**[Descargar XucaMonitor.exe](https://github.com/011Xuca/xuca-monitor/releases/latest/download/XucaMonitor.exe)**

No hace falta instalar nada: lo abrís y listo. Usa el .NET que ya trae Windows.

> La primera vez Windows puede mostrar un cartel azul (SmartScreen) porque el `.exe` no está firmado. Tocá **Más información** y después **Ejecutar de todas formas**.

## Qué hace

| Pestaña | Para qué sirve |
|---|---|
| **Red** | Avisa cuando se **corta internet** y te dice si fue la placa de red (cable o WiFi), el router o tu proveedor. También avisa cuando el **ping sube y se queda alto (lag)**: mide el ping al router, cuánto está bajando o subiendo la PC, qué programas suelen usar mucho internet y si algún driver está perdiendo memoria. |
| **USB** | Cuando suena el ruido de Windows de "se desconectó algo", te dice **qué aparato fue** (mouse, teclado, auriculares, monitor, Bluetooth...), si volvió a conectarse solo y qué conviene probar. |
| **Rendimiento** | Mientras jugás en pantalla completa, vigila procesador, RAM, disco y placa de video. Cuando algo **hace bajar los FPS** te dice el culpable: un programa de fondo (Windows Update, el antivirus, un launcher descargando...), el procesador que se frena por temperatura, la RAM llena o el disco al 100 %. No mide los FPS exactos (eso pide permisos de administrador): detecta lo que los hace bajar. |
| **Errores** | Te avisa cuando la **pantalla se pone negra** porque se colgó el driver de video, cuando hay un **pantallazo azul** (con el código explicado), cuando la PC **se apaga de golpe**, cuando hay errores de hardware, del disco o falta de memoria, y cuando un **juego o programa se cierra solo** (y en qué falló: driver de NVIDIA/AMD, un overlay...). |

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

- `registro_*.txt` (red, usb, rendimiento y errores): todo lo que detectó cada pestaña, con hora (botón **Abrir registro**).
- `historial_*.txt`: lo que ves en la ventana (los últimos 300 avisos de cada pestaña). **Limpiar** lo borra.
- `ajustes_monitores.txt`: tema, idioma, zoom y qué pestañas están prendidas.

## Compilarlo vos

El código está en `src` (C# para .NET Framework 4.8). Abrí `compilar.bat` y te arma `XucaMonitor.exe` con el compilador que ya trae Windows. No hace falta instalar nada.

## Créditos

Hecho por **011Xuca**. Redes: https://linktr.ee/011xuca
Si te sirvió, podés apoyarme en Ko-fi: https://ko-fi.com/011xuca
