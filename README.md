<img src="Pulso.ico" width="96" alt="Icono de Pulso">

# Pulso

> Antes se llamaba **xuca-monitor (Xuca Monitor)**: si lo buscabas con ese nombre, es esta app.

App gratis para **Windows 10 y 11** que hace el **monitoreo integral de tu PC** desde la bandeja (al lado del reloj): **red y latencia**, **USB**, **rendimiento en juegos**, **errores de Windows** y el **estado del procesador, la placa de video, la memoria y los discos**. Cuando se corta internet, sube el ping, se desconecta algo, bajan los FPS, la pantalla se pone negra, hay un pantallazo azul o el hardware se calienta de más, te dice **qué fue y qué probar**. Además trae un **overlay de rendimiento** encima del juego y un **grabador de sesión** que mide unos minutos de uso real y te arma un reporte con el cuello de botella y qué conviene hacer. En Windows 7 SP1 y 8.1 anda recortada (ver [más abajo](#windows-7-8-y-81)).

## Descargar

**[Descargar Pulso.exe](https://github.com/011Xuca/pulso/releases/latest/download/Pulso.exe)**

No hace falta instalar nada: lo abrís y listo. Usa el .NET que ya trae Windows (si a tu Windows le falta, la app te avisa y te lleva a la descarga de .NET Framework 4.8).

> La primera vez Windows puede mostrar un cartel azul (SmartScreen) porque el `.exe` no está firmado. Tocá **Más información** y después **Ejecutar de todas formas**.

> **Pide permisos de administrador al abrir.** Es necesario para leer los sensores reales (temperaturas, vatios, discos). Si le decís que no a ese permiso, la app no abre.
>
> **Sin instalar nada más que Pulso** se ven datos reales de: la placa de video (temperatura, hot spot, memoria, frecuencias, uso, VRAM, potencia y ventiladores, con las DLL del driver de NVIDIA o AMD que ya tenés), los discos NVMe (temperatura, vida útil, escrito y horas, pedidos directo al disco), el estado SMART de los discos rígidos (Sano / Atención / Mal, con sectores reasignados, pendientes, incorregibles y errores CRC; un HDD no tiene % de vida y no se inventa), el procesador (uso, frecuencia real y % de rendimiento según Windows, **potencia** del paquete y de cada núcleo si Windows expone el contador `Energy Meter`, y un **indicio** de throttling si con carga alta Windows lo limita o la frecuencia cae), la RAM, la paginación y la batería (% y vatios en notebooks). Lo que no se puede leer sin driver se dice tal cual: **"Temperatura de CPU: requiere driver (PawnIO, opcional)"**, y lo mismo para el voltaje (y la potencia, en los equipos sin ese contador).
>
> **Temperatura del procesador:** sin drivers, Pulso usa la zona térmica que da Windows cuando es creíble (en notebooks suele andar; en muchas PCs de escritorio Windows no da un valor real y no se muestra nada). Para la temperatura y los vatios **reales** del procesador y los ventiladores de la placa está **PawnIO**, un driver gratis, firmado y de código abierto (el mismo que usan LibreHardwareMonitor y FanControl). Es **opcional** y **Pulso no lo baja ni lo instala**: el botón **Cómo instalar PawnIO** de la pestaña CPU te abre [pawnio.eu](https://pawnio.eu/) o te copia el comando `winget install --id namazso.PawnIO -e`. Es normal que el antivirus o SmartScreen avisen al instalar un driver así. Cuando termina de instalarse, Pulso lo detecta solo.
>
> **Antivirus:** hasta la 1.0.3 había dos cosas que hacían saltar a Defender: el botón "Instalar PawnIO" bajaba el instalador del driver y lo ejecutaba desde Pulso, y Pulso volvía a crear su tarea de "Iniciar con Windows" (con `schtasks.exe` y un XML temporal) **cada vez que se abría** (alertas `Behavior:Win32/Persistence.A!ml` y `Execution.A!ml`). Desde la 1.0.4 Pulso no descarga ni ejecuta ningún instalador, y la tarea se maneja con la API del Programador de tareas y solo se toca cuando la prendés o cuando moviste el `.exe`. Si Defender ya la había borrado, restaurala desde Seguridad de Windows > Protección contra virus y amenazas > Historial de protección.

## Qué hace

| Pestaña | Para qué sirve |
|---|---|
| **Red** | Arriba muestra la **latencia en vivo**: ping a internet, promedio de los últimos 10 s, variación (jitter), paquetes perdidos y ping al router. Avisa cuando se **corta internet** y te dice si fue la placa de red (cable o WiFi), el router o tu proveedor, y cuando el **ping sube y se queda alto (lag)**: cuánto está bajando o subiendo la PC, qué programas suelen usar mucho internet y si algún driver está perdiendo memoria. |
| **USB** | Cuando suena el ruido de Windows de "se desconectó algo", te dice **qué aparato fue** (mouse, teclado, auriculares, monitor, Bluetooth...), si volvió a conectarse solo y qué conviene probar. |
| **Rendimiento** | Mientras jugás en pantalla completa, vigila procesador, RAM, disco y placa de video. Cuando algo **hace bajar los FPS** te dice el culpable: un programa de fondo (Windows Update, el antivirus, un launcher descargando...), el procesador que se frena por temperatura, la RAM llena o el disco al 100 %. También avisa si **otro programa le saca el foco al juego** sin que toques nada (Discord, Steam, un aviso de Windows...). Con la pestaña CPU prendida sabe si el procesador se frena por temperatura o por energía. |
| **Errores** | Te avisa cuando la **pantalla se pone negra** porque se colgó el driver de video, cuando hay un **pantallazo azul** (con el código explicado), cuando la PC **se apaga de golpe**, cuando hay errores de hardware, del disco o falta de memoria, y cuando un **juego o programa se cierra solo** (y en qué falló: driver de NVIDIA/AMD, un overlay...). Con las pestañas de hardware prendidas también dice **cómo estaba la PC justo antes** y si fue por calor o no. |
| **CPU** | Temperatura (contra su límite TjMax), uso total y por núcleo, velocidad, potencia y voltaje **reales**, gráfico de los últimos 5 minutos, y abajo la **placa madre, la BIOS, el plan de energía, la batería y los ventiladores**. Avisa si el procesador llega a su límite y se frena (throttling por calor o por energía), si su ventilador se para, si tu Intel 13.ª/14.ª gen necesita una BIOS nueva y si jugás con el plan de ahorro o a batería. |
| **GPU** | Temperatura del núcleo, hot spot y memoria, uso, frecuencias, potencia, ventiladores, **VRAM** usada y driver. Avisa si se calienta de más, si el hot spot queda muy separado del núcleo (pasta térmica), si los ventiladores se paran, si la VRAM se llena o si el conector 12VHPWR se sobrecarga. En gráficos integrados (Radeon Vega, Intel UHD) muestra la memoria compartida y solo los datos que esa gráfica informa. |
| **Memoria** | RAM usada, módulos (marca, modelo, velocidad actual y de fábrica), si el **XMP/EXPO** está activo, si anda en **single o dual channel**, y la **paginación** (páginas por segundo leídas del disco y uso del archivo de paginación). Avisa si la RAM se llena (y qué la llena) o si los módulos se calientan. |
| **Discos** | Cada disco con su **tipo y bus (NVMe, SSD SATA, HDD, USB)**, su **salud SMART** según Windows y la **velocidad de lectura y escritura** en vivo; y con sensores, temperatura, vida útil, espacio usado, datos escritos y horas encendido. Avisa si un disco se frena por temperatura, si le queda poca vida o si Windows lo marca con fallas. |
| **Sesión** | **Grabador de sesión**: elegís 1, 5, 10, 15 o 30 minutos y usás la PC o jugás normal (no es un benchmark sintético). Al terminar deja un reporte con **FPS promedio, 1 % y 0.1 % low, frametimes**, tirones, temperaturas y uso de CPU y GPU, **VRAM, RAM y paginación**; el **cuello de botella** (CPU contra GPU, VRAM, RAM disponible, tipo de disco y resolución); un **diagnóstico del sistema** (edición de Windows completa o recortada, errores críticos recientes con `sfc /scannow` y `DISM`, procesos, servicios e inicio automático, días sin reiniciar, Windows 11 con 8 GB o menos, memoria reservada por la gráfica integrada, single channel y XMP, paginación sobre HDD) y **cuándo conviene usar [Brío](https://github.com/011Xuca/brio)**, con un botón para abrirlo o descargarlo. |
| **Overlay de rendimiento** | Encima del juego: FPS, promedio, mínimo y máximo de la sesión, 1 % y 0.1 % low, frametime con gráfico, y uso, temperatura, velocidad y consumo de CPU y GPU, VRAM y RAM. Se arma con una plantilla de texto y trae modelos listos (incluido uno para videos comparativos). No inyecta nada en el juego: los FPS salen de los eventos de Windows (DirectX 9 a 12), como PresentMon. OBS lo captura como la ventana "Pulso Overlay". En sus ajustes, **Qué categorías mostrar** prende o apaga cada parte sin tocar la plantilla: FPS y 1 % / 0.1 % low, Frametime y gráfico, CPU, GPU, VRAM, RAM, Temperaturas, Nombres de CPU y GPU, y Hora y juego (se recuerda en `overlay.txt`). |

- Cada pestaña se puede **prender o apagar** con la casilla **Activado** (o con clic derecho en el ícono de la bandeja). Apagada no gasta nada.
- Muestra las opciones de red del [Brío](https://github.com/011Xuca/brio) que tenés aplicadas, así sabés si alguna tiene que ver.
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

- `registro_*.txt` (red, usb, rendimiento, errores, sesion y `registro_pc_*` para CPU, GPU, Memoria y Discos; `registro_pc.txt` junta todo el hardware): todo lo que detectó cada pestaña, con hora (botón **Abrir registro**).
- `historial_*.txt`: lo que ves en la ventana (los últimos 300 avisos de cada pestaña). **Limpiar** lo borra.
- `pc_ultimos_minutos.txt`: cómo estuvo la PC en los últimos 5 minutos (sirve para saber si un apagón fue por temperatura).
- `ajustes_monitores.txt`: tema, idioma, zoom y qué pestañas están prendidas.
- `overlay.txt`: los ajustes del overlay de rendimiento.
- `benchmarks\`: un `.txt` por sesión grabada (el mismo reporte que ves en Sesión) y `benchmarks.csv`, con una fila por sesión (separado por `;`) para compararlas o abrirlas en una planilla.
- `crash.txt`: solo si la app tuvo un error.

No manda nada a ningún servidor. Lo único que sale a internet es el ping de la pestaña Red (al router, a 1.1.1.1 y a 8.8.8.8), la consulta de versión a GitHub y la actualización de Pulso cuando la pedís.

## Windows 7, 8 y 8.1

Pulso está hecha para **Windows 10 y 11**. En **Windows 7 SP1 y 8.1 anda recortada**:

- **Andan:** Red, USB, Errores y Rendimiento (este último sin los datos de la placa de video ni de la velocidad del procesador).
- **No están:** las pestañas **CPU, GPU, Memoria y Discos** (temperaturas, consumo, frecuencias y estado del hardware: el driver PawnIO es para Windows 10 y 11; en su lugar queda una pestaña **Hardware** que lo explica), el **overlay de rendimiento** y la **grabación de sesión** (usan eventos de Windows 10 y 11). La app lo avisa en esas pestañas y en el botón del overlay.
- Hace falta **.NET Framework 4.8**: esos Windows no lo traen, y si falta la app te lleva a la descarga.
- **Windows 8 (sin el .1) no es compatible**: no puede instalar el .NET que hace falta. La actualización a 8.1 es gratis.

## Compilarlo vos

El código está en `src` (C# 5 para .NET Framework 4.8). Abrí `compilar.bat` y te arma `Pulso.exe` con el compilador que ya trae Windows. No hace falta instalar nada. Las dll de la carpeta `lib` (sensores) se meten **adentro** del `.exe`: el resultado es un solo archivo que anda en cualquier carpeta.

### Publicar una versión

1. Cambiá `Version` en `src/App.cs` (y la de `app.manifest`) y subí el cambio.
2. Creá un Release con el tag `v` + versión (por ejemplo `v1.0.4`). El workflow `.github/workflows/publicar.yml` compila `Pulso.exe` y lo sube al Release (la app busca ese archivo para actualizarse sola), arma `Pulso.msix` para la Microsoft Store y los manifiestos de winget (artefacto `winget-manifests`).
3. **winget:** la primera vez enviá los manifiestos a mano con `wingetcreate submit <carpeta winget-manifests>`. Después, con el secreto `WINGET_TOKEN`, cada Release se envía solo.
4. **Microsoft Store:** la primera vez creá la app en Partner Center (nombre "Pulso"), cargá en las variables del repo `MSIX_PUBLISHER` (el `CN=...` que muestra Partner Center) y `STORE_PRODUCT_ID`, y en los secretos los datos de la app de Entra ID (`PARTNER_CENTER_TENANT_ID`, `PARTNER_CENTER_SELLER_ID`, `PARTNER_CENTER_CLIENT_ID`, `PARTNER_CENTER_CLIENT_SECRET`). La Store firma el paquete; instalada desde ahí, Pulso no se actualiza sola (lo hace la Store).

## Créditos

Hecho por **011Xuca**. Redes: https://linktr.ee/011xuca
Si te sirvió, podés apoyarme en Ko-fi: https://ko-fi.com/011xuca

Las temperaturas y sensores de hardware usan [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (licencia MPL-2.0).
