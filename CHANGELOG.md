# Cambios

Todo lo que cambió en cada versión de **Xuca Monitor**. La más nueva va arriba.

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

### Cambios
- La app ahora **pide permisos de administrador** al abrir (los sensores lo necesitan).
- "Iniciar con Windows" usa una tarea programada (los accesos directos no se elevan solos). Además arranca aunque la notebook esté a batería y no se cierra a las 72 horas (dos trampas de las tareas que arma Windows por defecto).
- Si algo falla al abrir, en vez de cerrarse sin avisar muestra el error y lo guarda en Documentos\Xuca Monitor\crash.txt.
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

Primera versión: pestañas **Red** (cortes de internet y lag) y **USB** (qué se desconectó), en la bandeja, con tema claro/oscuro y español/inglés. Registros e historial en Documentos\Xuca Monitor.
