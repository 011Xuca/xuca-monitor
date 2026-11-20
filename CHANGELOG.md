# Cambios

Todo lo que cambió en cada versión de **Pulso**. La más nueva va arriba.

## v1.0.4

### Nuevo
- **Grabación de sesión** (pestaña nueva **Sesión**): elegís 1, 5, 10, 15 o 30 minutos y usás la PC o jugás normal; no es un benchmark sintético. Al terminar deja un reporte (se guarda en `Documentos\Pulso\benchmarks`, con una fila en `benchmarks.csv`, y se compara con la sesión anterior de la misma app) con FPS promedio, 1 % y 0.1 % low, frametimes, tirones, temperaturas y uso de CPU y GPU, VRAM, RAM y paginación, y:
  - **Cuello de botella**: cruza la carga del procesador contra la de la placa de video, la VRAM, la RAM disponible, el tipo de disco y la resolución activa.
  - **Diagnóstico del sistema**: edición de Windows (completa o recortada), errores críticos recientes del visor de eventos (con `sfc /scannow` y `DISM` para reparar), procesos, servicios y programas de inicio de más (OneDrive, Widgets, telemetría), días sin reiniciar, Windows 11 completo con 8 GB o menos, memoria que se reserva la gráfica integrada (bajarla a 512 MB en la BIOS, distinto si es notebook o PC de escritorio), single channel y XMP, y paginación sobre un HDD.
  - **Cómo seguir**: cuándo conviene usar **Brío** según lo que encontró (o te felicita si vas estable y con margen), con un botón para abrirlo o descargarlo.
- **Red**: latencia en vivo arriba de la pestaña: ping a internet, promedio de 10 s, variación (jitter), paquetes perdidos y ping al router.
- **Discos**: tipo y bus de cada disco (NVMe, SSD SATA, HDD, USB), salud SMART según Windows y velocidad de lectura y escritura en vivo. Avisa si Windows marca un disco con fallas.
- **Memoria**: paginación en vivo (páginas por segundo leídas del disco y uso del archivo de paginación).
- Preparado para la **Microsoft Store** (MSIX) y **winget**, con un workflow de GitHub Actions que publica cada versión.
- La app se renombró de Xuca Monitor a **Pulso** (`Pulso.exe`, repo `github.com/011Xuca/pulso`); los datos y el "Iniciar con Windows" se mudan solos la primera vez que abre.
- **Busca actualizaciones al abrir**: una sola vez por cada arranque, sin chequeos en segundo plano. Si hay una versión nueva aparece el cartel amarillo para actualizar.
- **Más datos reales sin instalar nada** (sin PawnIO; nuevo `src/SinDriver.cs`):
  - **Discos NVMe**: temperatura, temperatura de aviso y crítica, vida útil, datos escritos y horas encendido se le piden **directo al disco** (`IOCTL_STORAGE_QUERY_PROPERTY`: registro SMART/Health del NVMe, Identify del controlador y la temperatura que expone Windows). No usa driver propio ni necesita administrador. Solo se usa si la librería de sensores no dio la temperatura de ese disco. Los SATA siguen saliendo de la librería (SMART por comandos ATA, con administrador).
  - **Procesador**: la frecuencia real (base × **% de rendimiento** de Windows, lo mismo que el Administrador de tareas) y el **% de límite de rendimiento** que impone el firmware, de los contadores `Processor Information`. El overlay ya no depende de que la pestaña CPU esté prendida para mostrar la velocidad, y sigue mostrando lo que da Windows aunque la librería de sensores no cargue.
  - **Indicio de throttling sin temperatura**: si con carga alta Windows informa un límite de rendimiento o la frecuencia cae más de un 15 %, la pestaña CPU avisa (**CPU: indicio de que se está frenando**) y el reporte de Sesión lo cuenta, aclarando que es un indicio y no una temperatura.
  - **Potencia del procesador sin driver**: si Windows expone el contador `Energy Meter` (RAPL del procesador, por ejemplo en Ryzen 9000), la pestaña CPU, el overlay y el reporte de Sesión muestran los vatios del paquete ("Potencia (Windows, RAPL)"), y "Ver todos" los de cada núcleo. Si el equipo no tiene ese contador, sigue diciendo "requiere driver".
  - **Discos rígidos (HDD) y SATA sin % de vida**: en "Vida útil" muestran un estado calculado con atributos SMART reales (WMI `MSStorageDriver_ATAPISmartData` y `MSStorageDriver_FailurePredictStatus`, con administrador): **Sano (HDD)**, **Atención** (algún sector reasignado o pendiente, reintentos de arranque o errores CRC) o **Mal** (Windows predice falla, sectores incorregibles, 100+ reasignados o 10+ pendientes). No se inventa un porcentaje. Debajo de la tabla se explica el estado y se listan los contadores de cada disco. "Escrito" sale del atributo 241 si el HDD lo informa (si no, "—", y la leyenda dice por qué); la temperatura SMART (194/190) se usa si la librería no la dio.
  - **Batería** (notebooks): cuánto consume o con cuánto carga, en vatios (`CallNtPowerInformation`).
  - Placa de video: temperatura, hot spot, memoria, frecuencias, uso, VRAM, potencia y ventiladores ya salían sin PawnIO (la librería usa las DLL del driver de NVIDIA o AMD que ya está instalado).
  - **Gráficos integrados** (Radeon Vega / Radeon Graphics de los Ryzen y Athlon, Intel UHD / Iris): la tarjeta de GPU muestra solo lo que la gráfica informa (uso, frecuencia y, si existen, potencia y voltaje) y la **memoria compartida** con la RAM, en vez de dejar casilleros vacíos; avisa que no tiene VRAM propia, ventilador ni hot spot, y la pastilla dice "Integrada" en lugar de "Sin dato". El aviso "tiene más de un año, conviene actualizarlo" del driver ya no sale en integradas (varias dejaron de recibir drivers nuevos y Windows no dice si existe uno) y la regla "VRAM llena" tampoco. El overlay suma `{gpu.vramshared}`.
- **Novedades** dentro de la app: el botón **Novedades** (arriba, junto a Overlay de rendimiento) abre una ventana con las novedades de la última versión en el idioma de Pulso (español o inglés), sin internet: si son muy largas (más de 2000 caracteres) se corta en el límite de una viñeta, y un botón lleva al resto y al historial completo en GitHub. El historial en inglés está en `CHANGELOG.en.md`; los dos archivos van embebidos en el `.exe`.

### Cambios
- La pestaña **PC** se reparte en cuatro pestañas propias: **CPU** (con placa madre, BIOS, plan de energía, batería y ventiladores al pie), **GPU**, **Memoria** y **Discos**. Cada una tiene su casilla Activado, su contador y su registro; el historial de antes se conserva.
- El botón "Overlay de FPS" pasa a llamarse **Overlay de rendimiento**.
- **Overlay: Qué categorías mostrar**: casillas para prender o apagar cada parte sin tocar la plantilla (FPS y 1 % / 0.1 % low, Frametime y gráfico, CPU, GPU, VRAM, RAM, Temperaturas, Nombres de CPU y GPU, Hora y juego). Se recuerda entre usos en `overlay.txt`. Apagar Temperaturas saca solo los °C de cada línea.
- Las pestañas pasan a una segunda línea si la ventana es angosta.
- **Temperatura del procesador sin drivers**: si Windows da una zona térmica creíble (lo normal en notebooks), se muestra como "aproximada, sin driver". PawnIO queda como mejora opcional.
- **PawnIO ya no se instala desde Pulso**: el botón pasa a ser **Cómo instalar PawnIO** (o **Cómo actualizar PawnIO**) y solo explica, abre [pawnio.eu](https://pawnio.eu/) o copia `winget install --id namazso.PawnIO -e`, avisando que el antivirus puede saltar y por qué. Cuando lo instalás, Pulso lo detecta solo en 2 minutos como mucho.
- Pulso tiene **color propio: verde**. Cambian el ícono (borde y dibujo; el fondo sigue gris oscuro) y la ventana: pestaña elegida, botones, casillas y selección. Los gráficos y los colores de estado (verde, amarillo, rojo) de la pestaña PC no cambian.
- Si la app actualizada encuentra otra copia del mismo `.exe` todavía abierta (otra sesión de Windows, o una versión anterior colgada), la cierra para poder reemplazar el archivo.
- **Overlay de rendimiento**: ahora arranca **sin fondo** (opacidad del fondo en 0). Si ya lo tenías configurado, se mantiene lo tuyo.
- **Actualización más segura**: antes de reemplazar el `.exe`, verifica que la descarga esté completa (mismo SHA-256 que publica GitHub) y que sea de verdad la versión anunciada. Si el `.exe` publicado dijera una versión menor, ya no se instala, porque si no la app volvería a ofrecer la misma actualización para siempre.
- Si la versión recién bajada **no logra abrir**, vuelve sola a la anterior en vez de dejar la app rota. La versión anterior se borra recién cuando la nueva terminó de arrancar.
- Se limpian los restos de una actualización cortada a la mitad (`.nuevo`, `.roto`).
- **Temperaturas**: 80 °C ya no es "caliente". La temperatura del procesador y de la placa de video se pinta amarilla desde 85 °C (sin aviso) y roja, con aviso, desde 90 °C. En procesadores con un límite más bajo (Ryzen X3D: 89 °C), el rojo arranca 3 °C antes de ese límite. Los colores del overlay pasan a 85/90 °C (si tenías un modelo de fábrica sin tocar, se actualiza solo).
- **Windows 7 SP1 y 8.1**: Pulso anda recortado. Funcionan Red, USB, Errores y Rendimiento (sin datos de la placa de video ni de velocidad del procesador). Las pestañas de hardware (CPU, GPU, Memoria y Discos), el overlay de rendimiento y la grabación de sesión son solo para Windows 10 y 11, y la app lo avisa en vez de fallar.
- Si falta **.NET Framework 4.7.2 o más nuevo** (Windows 7 SP1 y 8.1 no lo traen de fábrica), avisa y ofrece abrir la descarga del 4.8, en vez de cerrarse con un error confuso. En Windows 8 (sin el .1), que no puede instalarlo, avisa que hace falta actualizar a 8.1.

### Arreglos
- **Sin PawnIO se mostraban números falsos del procesador**: potencia en 0 W, frecuencias en 0 MHz y voltaje fijo de 1.55 V (la librería crea esos sensores aunque no los pueda leer). Ahora la pestaña CPU dice **"Temperatura de CPU: requiere driver (PawnIO, opcional)"** y "requiere driver" en voltaje (y en potencia si Windows no la da), "Ver todos" muestra solo el uso, el overlay deja `--` (y marca con `~` la temperatura aproximada de la zona térmica), y el reporte de Sesión dice "temperatura / potencia: requiere driver" en vez de "- / -" y ya no dice "muy estable de temperaturas" si la del procesador no se midió.
- **El antivirus saltaba al tocar "Instalar PawnIO"**: Pulso bajaba el instalador del driver desde GitHub y lo ejecutaba. Ya no descarga ni ejecuta ningún instalador.
- **Defender borraba Pulso** (`Behavior:Win32/Persistence.A!ml` y `Execution.A!ml`): Pulso recreaba la tarea de "Iniciar con Windows" con `schtasks.exe` y un XML temporal en cada arranque. Ahora usa la API del Programador de tareas y solo la escribe cuando la prendés o cuando el `.exe` cambió de carpeta.
- **"Requiere driver (PawnIO, opcional)" aunque PawnIO estuviera instalado**: si la librería de sensores abría antes de que el driver estuviera listo, leía ceros y la pestaña CPU quedaba sin temperatura, vatios ni voltaje (y mostraba la potencia de Windows como si fuera lo único). Ahora, si PawnIO 2.0 o más nuevo está instalado y Pulso tiene administrador pero el procesador sigue sin dar datos, **reabre los sensores solo** (a los 10, 20 y 40 segundos y después cada 2 minutos) y, mientras tanto, dice la verdad: "PawnIO instalado, pero no entregó datos del CPU (reintentando)" con qué probar (cerrar HWiNFO o FanControl, reiniciar la PC). Si Pulso no está corriendo como administrador, lo dice ("requiere administrador") en vez de culpar a PawnIO. El registro suma, una vez por apertura, si hay administrador, la versión de PawnIO, cuánto tardó en abrir, cuántos sensores dio el procesador y los valores de temperatura y vatios.
- **Voltaje del procesador vacío en Ryzen 7000 y 9000**: esos procesadores no tienen un sensor de voltaje real, solo el VID de cada núcleo. Ahora la pestaña CPU muestra el mayor VID como **Voltaje (VID)**, aclarando que es lo que el procesador solicita y no lo medido en la placa; si hay un voltaje real se sigue mostrando como "Voltaje".

## v1.0.3

### Nuevo
- Pestaña **PC**: temperatura, uso, velocidad y potencia **reales** del procesador y de la placa de video (núcleo, hot spot y memoria), uso y velocidad de la RAM, temperatura y vida útil de los discos, ventiladores, placa madre, BIOS, plan de energía y batería. Todo en verde, amarillo o rojo según qué tan comprometido está.
- Adentro de "PC" hay 5 categorías: **General**, **CPU**, **GPU**, **RAM** y **Discos**. Cada una tiene su propio registro, su contador y su casilla "Guardar avisos". General junta todo.
- Gráfico de los últimos 5 minutos, máximo de la sesión y máximo jugando, uso por núcleo y la lista completa de sensores.
- **Avisos concretos**, con qué probar y cómo estaba la PC en ese momento:
  - Procesador al límite de temperatura y **frenándose** (dice de cuántos GHz a cuántos bajó), o muy caliente mucho rato (con qué programa lo usaba).
  - Placa de video caliente, **hot spot muy separado del núcleo** (pasta térmica gastada), memoria de video caliente, ventiladores parados, **VRAM llena** y **conector 12VHPWR sobrecargado**.
  - **RAM llena** (con qué programas la llenan), **XMP/EXPO apagado** (compara con la velocidad de tus módulos) y **single channel**.
  - Discos que se frenan por temperatura o con poca vida útil.
  - Intel 13.ª/14.ª gen con BIOS vieja (el problema de degradación por voltaje).
  - Jugando con el plan de energía en ahorro o la notebook a batería.
- **Errores** ahora dice cómo estaba la PC justo antes de la pantalla negra, el pantallazo, el apagón o el juego cerrado, y si fue calor o no (por ejemplo: "la placa estaba a 70 °C: no fue por calor, mirá el driver o la fuente").
- **Rendimiento**: cuando el procesador se frena, ahora dice si es temperatura o energía (antes solo podía adivinar).
- Para la temperatura del procesador hace falta **PawnIO** (driver gratis y firmado, el mismo de LibreHardwareMonitor y FanControl): la pestaña PC tiene un botón para instalarlo y verifica su firma antes de abrirlo.

- **Overlay de FPS** (botón "Overlay de FPS" arriba): muestra encima del juego FPS, 1 % low, frametime, gráfico de frametime, uso y temperatura de CPU y GPU, y RAM, como el de MSI Afterburner con RivaTuner. Elegís en qué procesos aparece, un atajo F1-F12 para ocultarlo y la esquina y posición. Los cambios se aplican al instante, tiene vista previa y podés exportar e importar el estilo.
  - **100 % personalizable con una plantilla de texto**: cada línea que escribís es una línea del overlay. Usás variables entre llaves (`{gpu.name}`, `{fps}`, `{low1}`, `{ft}`, `{gpu.temp}`, `{gpu.vram}`, `{cpu.clock}`, `{ram.used}`...; hay una lista para insertar con un clic), con **color** (`<c=FF0000>`), **tamaño** (`<s=150>`), **negrita** (`<b>`), **formato** (`{gpu.power:0.0}`), **alerta por valor** (`{gpu.temp!75,85}`: naranja y rojo; `{fps!<60,30}` al revés para FPS) y **gráfico de frametime** con el tamaño que quieras (`{graph:260x40}`). Trae modelos listos, incluido uno estilo Afterburner.
  - Fuente, tamaño, negrita, color base, y opacidad del texto y del fondo **por separado** (transparencia por pixel).
  - **Seguro con anti-cheats**: no inyecta nada en el juego. Los FPS salen de los eventos de Windows (ETW, DXGI y D3D9), como PresentMon, y el texto va en una ventana transparente por la que pasan los clics. Se ve con el juego en pantalla completa sin bordes o en ventana, no en pantalla completa exclusiva.
  - **Capturable con OBS**: es una ventana aparte, así que se agrega como fuente de **Captura de ventana**. Destildando "Ocultarlo cuando el juego no está al frente" se queda visible mientras el juego esté abierto, aunque estés mirando OBS.
  - **Sin gasto cuando no jugás**: sin un juego de la lista abierto no hay sesión ETW ni lectura de sensores, solo un chequeo de procesos por segundo.
  - **Reborde del texto**: negro por defecto y de 0 a 6 px, con color a elección, para que se lea sobre cualquier fondo.
  - **Promedio de FPS** (`{fps.avg}`) y **0.1 % low** (`{low01}`), y la plantilla por defecto ahora trae colores, el nombre de la CPU y de la GPU y el promedio. Quien tenía la plantilla vieja sin tocar pasa a la nueva solo.
  - Modelo nuevo **Comparativa (videos)**: nombres de los componentes, FPS grandes, promedio, 1 % y 0.1 % low, y consumo de GPU y CPU.

### Cambios
- Arreglado: el overlay mostraba **FPS de más** (por ejemplo 5000 jugando a 1000) porque los tiempos de los eventos de Windows tienen otro origen de reloj que el programa. Ahora FPS, 1 % low, frametime y gráfico se miden contra el último cuadro recibido. También se cuenta una sola swapchain, para no sumar de más si el juego o un overlay presentan en varias.
- **Rendimiento**: se sacó el aviso "Placa de video al máximo". La GPU al 100 % jugando es lo normal (no hay cuello de botella) y el aviso parecía un problema sin serlo. Siguen los avisos de bajones reales: procesador, RAM, disco, tirones y pérdida de foco.
- La app ahora **pide permisos de administrador** al abrir (los sensores lo necesitan).
- "Iniciar con Windows" usa una tarea programada (los accesos directos no se elevan solos). Además arranca aunque la notebook esté a batería y no se cierra a las 72 horas (dos trampas de las tareas que arma Windows por defecto).
- Si algo falla al abrir, en vez de cerrarse sin avisar muestra el error y lo guarda en crash.txt, en la carpeta de datos.
- Arreglado: el Snipping Tool (recorte de pantalla) ya no se detecta como juego, lo que daba avisos falsos de "perdió el foco" en Rendimiento.

## v1.0.2

### Nuevo
- **Rendimiento** ahora avisa cuando **otro programa le saca el foco al juego** sin que toques nada (Discord, Steam, un aviso de Windows Update, un pedido de permiso...): qué programa fue, qué ventana abrió y cuánto tiempo quedó el juego atrás, con qué probar para que no pase más. Si eso pasó justo antes de un bajón de FPS, el aviso del bajón también lo dice.
- No cuenta lo que hacés vos (Alt+Tab, la tecla Windows, el menú inicio o un clic en otra ventana).

## v1.0.1

### Nuevo
- Pestaña **Rendimiento**: mientras jugás en pantalla completa, te dice qué hizo bajar los FPS. Puede ser un programa de fondo, el procesador frenado por temperatura, la RAM llena o el disco al 100 %. También avisa cuando la placa de video es el límite.
- Pestaña **Errores**: pantalla negra por el driver de video, pantallazo azul con el código explicado, apagones de golpe, errores de hardware, del disco y de memoria, y juegos o programas que se cierran solos (con dónde fallaron).
- **Aviso de versión nueva**: un cartel amarillo y una opción en la bandeja. Se actualiza sola desde GitHub.
- **Zoom** del texto: Ctrl + rueda del mouse, o Ctrl + / Ctrl - (Ctrl + 0 lo vuelve al normal).
- La versión aparece en el título de la ventana.

## v1.0.0

Primera versión: pestañas **Red** (cortes de internet y lag) y **USB** (qué se desconectó), en la bandeja, con tema claro/oscuro y español/inglés. Registros e historial en la carpeta Documentos.
