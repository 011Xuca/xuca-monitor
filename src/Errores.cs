// Monitor de errores: avisa cuando Windows anota algo grave (driver de video colgado, pantallazo azul, apagon,
// error de hardware, disco con fallas, sin memoria, juego que se cerro) y explica que probar.
// No revisa nada cada tanto: Windows le pasa cada evento nuevo apenas lo escribe (EventLogWatcher).
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Threading;

namespace Xuca
{
    public class MonitorErrores : Modulo
    {
        public MonitorErrores()
        {
            Clave = "errores"; PestEs = "Errores"; PestEn = "Errors"; TipEs = "Err"; TipEn = "Err";
            NombreEs = "Monitor de errores"; NombreEn = "Error Monitor";
            RegistroArch = "registro_errores.txt"; HistArch = "historial_errores.txt";
            ContEs = "Errores"; ContEn = "Errors";
            VacioEs = "Todo bien por ahora. Si se cuelga el driver de video, hay un pantallazo azul, la PC se apaga de golpe o un juego se cierra solo, acá vas a ver qué pasó y qué probar.";
            VacioEn = "All good so far. If the video driver hangs, there is a blue screen, the PC shuts down suddenly or a game closes by itself, you will see here what happened and what to try.";
            ApagadoEs = "El monitor de errores está apagado. Prendelo con la casilla \"Activado\" para saber qué pasó cuando la pantalla se pone negra, hay un pantallazo azul o un juego se cierra.";
            ApagadoEn = "The error monitor is off. Turn it on with the \"Enabled\" box to find out what happened when the screen goes black, there is a blue screen or a game closes.";
        }

        // Solo errores y avisos del registro Sistema, y los programas que se cierran o se cuelgan
        const string QSistema = "*[System[(Level=1 or Level=2 or Level=3)]]";
        const string QApps = "*[System[(EventID=1000 or EventID=1002) and (Provider[@Name='Application Error'] or Provider[@Name='Application Hang'])]]";

        EventLogWatcher wSis, wApp;
        Timer espera;
        readonly List<EventRecord> lote = new List<EventRecord>();
        readonly object cerrojoLote = new object();
        readonly object cerrojoProc = new object();
        // Para no repetir el mismo aviso muchas veces seguidas
        readonly Dictionary<string, DateTime> ultimo = new Dictionary<string, DateTime>();

        protected override void Empezar()
        {
            espera = new Timer(delegate { Procesar(); }, null, Timeout.Infinite, Timeout.Infinite);
            // Lo que paso desde que se prendio la PC (pantallazo azul o apagon antes de reiniciar) y no se aviso todavia
            ThreadPool.QueueUserWorkItem(delegate { try { Pendientes(); } catch (Exception ex) { Log("Error: " + ex.Message); } });
            try
            {
                wSis = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, QSistema));
                wSis.EventRecordWritten += Llego;
                wSis.Enabled = true;
            }
            catch (Exception ex) { Log("Error System: " + ex.Message); }
            try
            {
                wApp = new EventLogWatcher(new EventLogQuery("Application", PathType.LogName, QApps));
                wApp.EventRecordWritten += Llego;
                wApp.Enabled = true;
            }
            catch (Exception ex) { Log("Error Application: " + ex.Message); }
        }

        protected override void Terminar()
        {
            foreach (EventLogWatcher w in new EventLogWatcher[] { wSis, wApp })
            {
                if (w == null) continue;
                try { w.Enabled = false; w.Dispose(); } catch { }
            }
            wSis = null; wApp = null;
            if (espera != null) { espera.Dispose(); espera = null; }
            lock (cerrojoLote) lote.Clear();
        }

        void Llego(object s, EventRecordWrittenEventArgs e)
        {
            if (e.EventRecord == null) return;
            lock (cerrojoLote) lote.Add(e.EventRecord);
            // Varios eventos seguidos (por ejemplo, el driver de video que se cuelga y vuelve) se muestran juntos
            Timer t = espera;
            if (t != null) try { t.Change(3000, Timeout.Infinite); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        // Pantallazos azules y apagones de golpe que Windows anoto al volver a prender (desde el ultimo arranque)
        void Pendientes()
        {
            DateTime arranque = DateTime.Now.AddMilliseconds(-(double)GetTickCount64());
            long vistoHasta = 0;
            long.TryParse(App.Ajuste("errores_visto") ?? "0", out vistoHasta);
            List<EventRecord> ev = new List<EventRecord>();
            string q = "*[System[((Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001) or (Provider[@Name='Microsoft-Windows-WHEA-Logger'])) and TimeCreated[timediff(@SystemTime) <= " + (long)(DateTime.Now - arranque.AddMinutes(-2)).TotalMilliseconds + "]]]";
            try
            {
                using (EventLogReader rd = new EventLogReader(new EventLogQuery("System", PathType.LogName, q)))
                {
                    EventRecord e;
                    while (ev.Count < 50 && (e = rd.ReadEvent()) != null)
                    {
                        if (e.RecordId.HasValue && e.RecordId.Value > vistoHasta) ev.Add(e); else e.Dispose();
                    }
                }
            }
            catch { }
            if (ev.Count == 0) return;
            lock (cerrojoLote) lote.AddRange(ev);
            Procesar();
        }

        void Procesar()
        {
            List<EventRecord> evs;
            lock (cerrojoLote) { evs = new List<EventRecord>(lote); lote.Clear(); }
            if (evs.Count == 0) return;
            lock (cerrojoProc)
            {
                try { Armar(evs); }
                catch (Exception ex) { Log("Error: " + ex.Message); }
                finally { foreach (EventRecord e in evs) try { e.Dispose(); } catch { } }
            }
        }

        // ---------- Que es cada evento ----------
        class Aviso { public string Tipo, Titulo; public List<string> Que = new List<string>(), Probar = new List<string>(); public DateTime T; public List<string> Crudo = new List<string>(); }

        static int Prop(EventRecord e, int i, out string s)
        {
            s = "";
            try { if (e.Properties != null && e.Properties.Count > i && e.Properties[i].Value != null) s = e.Properties[i].Value.ToString(); } catch { }
            return s.Length;
        }

        void Armar(List<EventRecord> evs)
        {
            evs.Sort(delegate (EventRecord a, EventRecord b) { return Nullable.Compare(a.TimeCreated, b.TimeCreated); });
            Dictionary<string, Aviso> grupos = new Dictionary<string, Aviso>();
            List<string> orden = new List<string>();
            bool hayPantallazo = false;
            foreach (EventRecord e in evs) if (e.ProviderName == "Microsoft-Windows-WER-SystemErrorReporting" && e.Id == 1001) hayPantallazo = true;
            long maxRec = 0;

            foreach (EventRecord e in evs)
            {
                if (e.LogName == "System" && e.RecordId.HasValue && e.RecordId.Value > maxRec) maxRec = e.RecordId.Value;
                string prov = e.ProviderName ?? "";
                int id = e.Id;
                string tipo = null, clave = null;
                Aviso a;

                if ((prov == "Display" && id == 4101) || (prov == "nvlddmkm" && (id == 13 || id == 14 || id == 153)) || ((prov == "amdkmdag" || prov == "amdwddmg" || prov == "igfx" || prov == "igfxn") && e.Level.HasValue && e.Level.Value <= 2))
                    tipo = "video";
                else if (prov == "Microsoft-Windows-WER-SystemErrorReporting" && id == 1001) tipo = "pantallazo";
                else if (prov == "Microsoft-Windows-Kernel-Power" && id == 41) { string bc; Prop(e, 0, out bc); if (hayPantallazo || (bc != "" && bc != "0")) continue; tipo = "apagon"; }
                else if (prov == "Microsoft-Windows-WHEA-Logger") tipo = "hardware";
                else if ((prov == "disk" && (id == 7 || id == 11 || id == 51 || id == 153)) || (prov == "Microsoft-Windows-Ntfs" && (id == 55 || id == 98)) || ((prov == "stornvme" || prov == "storahci" || prov == "iaStorAC" || prov == "iaStorA") && (id == 11 || id == 129))) tipo = "disco";
                else if (prov == "Microsoft-Windows-Resource-Exhaustion-Detector" && id == 2004) tipo = "memoria";
                else if (prov == "Microsoft-Windows-Kernel-Processor-Power" && id == 37) tipo = "cpu";
                else if ((prov == "Application Error" && id == 1000) || (prov == "Application Hang" && id == 1002)) tipo = "app";
                if (tipo == null) continue;

                clave = tipo;
                string appNom = "";
                if (tipo == "app") { Prop(e, 0, out appNom); clave = "app|" + appNom.ToLowerInvariant() + "|" + id; }
                if (!grupos.TryGetValue(clave, out a))
                {
                    a = new Aviso(); a.Tipo = tipo; a.T = e.TimeCreated ?? DateTime.Now;
                    grupos[clave] = a; orden.Add(clave);
                    Explicar(a, e, appNom);
                }
                if (a.Crudo.Count < 4) a.Crudo.Add(Eventos.Hora(e) + "  " + prov + " (" + id + "): " + Eventos.PrimeraLinea(e, 140));
            }
            if (maxRec > 0) App.GuardarAjuste("errores_visto", maxRec.ToString());

            foreach (string k in orden)
            {
                Aviso a = grupos[k];
                // El mismo tipo de aviso (o el mismo programa) se muestra como mucho cada 10 minutos
                DateTime u;
                if (ultimo.TryGetValue(k, out u) && (a.T - u).TotalMinutes < 10 && a.Tipo != "pantallazo" && a.Tipo != "apagon") continue;
                ultimo[k] = a.T;
                List<string> L = new List<string>();
                L.Add(a.Titulo + ": " + Fecha(a.T));
                L.Add("");
                L.Add(T("QUÉ PASÓ:", "WHAT HAPPENED:"));
                foreach (string x in a.Que) L.Add("  " + x);
                if (a.Probar.Count > 0)
                {
                    L.Add("");
                    L.Add(T("QUÉ PROBAR:", "WHAT TO TRY:"));
                    foreach (string x in a.Probar) L.Add("  - " + x);
                }
                L.Add("");
                L.Add(T("LO QUE ANOTÓ WINDOWS:", "WHAT WINDOWS LOGGED:"));
                foreach (string x in a.Crudo) L.Add("  " + x);
                foreach (string x in L) Log(x);
                Log("");
                Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), 1);
            }
        }

        // Codigos de pantallazo azul mas comunes: nombre y que suele ser
        static string[] Pantallazo(string cod)
        {
            switch (cod)
            {
                case "133": return new string[] { "DPC_WATCHDOG_VIOLATION", T("Un driver tardó demasiado en responder. Muy seguido es el del SSD, el de red o el de video.", "A driver took too long to respond. Very often it is the SSD, network or video driver.") };
                case "1A": return new string[] { "MEMORY_MANAGEMENT", T("Problema con la memoria RAM. Suele ser el perfil EXPO/XMP o un módulo de RAM con fallas.", "Problem with the RAM. It is usually the EXPO/XMP profile or a faulty RAM stick.") };
                case "124": return new string[] { "WHEA_UNCORRECTABLE_ERROR", T("Error de hardware: procesador, RAM o placa madre. Muy común con overclock, undervolt (Curve Optimizer) o EXPO/XMP inestables.", "Hardware error: CPU, RAM or motherboard. Very common with unstable overclock, undervolt (Curve Optimizer) or EXPO/XMP.") };
                case "101": return new string[] { "CLOCK_WATCHDOG_TIMEOUT", T("Un núcleo del procesador dejó de responder. Casi siempre es overclock o undervolt inestable.", "A CPU core stopped responding. It is almost always unstable overclock or undervolt.") };
                case "3B": return new string[] { "SYSTEM_SERVICE_EXCEPTION", T("Un driver hizo algo que no debía. Suele ser el de video, un antitrampas o un antivirus.", "A driver did something it should not. It is usually the video driver, an anti-cheat or an antivirus.") };
                case "D1": return new string[] { "DRIVER_IRQL_NOT_LESS_OR_EQUAL", T("Un driver usó memoria que no debía. Muy seguido es el de red (cable o WiFi).", "A driver used memory it should not. Very often it is the network driver (cable or WiFi).") };
                case "A": return new string[] { "IRQL_NOT_LESS_OR_EQUAL", T("Un driver o la RAM. Si pasa seguido, probá sin EXPO/XMP y actualizá los drivers.", "A driver or the RAM. If it happens often, try without EXPO/XMP and update your drivers.") };
                case "50": return new string[] { "PAGE_FAULT_IN_NONPAGED_AREA", T("La RAM o un driver. También puede ser un antivirus o un antitrampas.", "The RAM or a driver. It can also be an antivirus or an anti-cheat.") };
                case "7E": return new string[] { "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", T("Un driver falló. Suele ser el de video.", "A driver failed. It is usually the video driver.") };
                case "1E": return new string[] { "KMODE_EXCEPTION_NOT_HANDLED", T("Un driver falló.", "A driver failed.") };
                case "139": return new string[] { "KERNEL_SECURITY_CHECK_FAILURE", T("Un driver o la RAM. Actualizá drivers y probá sin EXPO/XMP.", "A driver or the RAM. Update drivers and try without EXPO/XMP.") };
                case "116": return new string[] { "VIDEO_TDR_FAILURE", T("La placa de video dejó de responder y Windows no pudo recuperarla.", "The graphics card stopped responding and Windows could not recover it.") };
                case "119": return new string[] { "VIDEO_SCHEDULER_INTERNAL_ERROR", T("Problema con la placa de video o su driver.", "Problem with the graphics card or its driver.") };
                case "9F": return new string[] { "DRIVER_POWER_STATE_FAILURE", T("Un driver no supo entrar o salir del reposo. Suele pasar al suspender o al despertar la PC.", "A driver could not go into or out of sleep. It usually happens when the PC sleeps or wakes up.") };
                case "EF": return new string[] { "CRITICAL_PROCESS_DIED", T("Se cerró una parte vital de Windows. Puede ser el disco o archivos de Windows dañados.", "A vital part of Windows closed. It can be the drive or damaged Windows files.") };
                case "C2": return new string[] { "BAD_POOL_CALLER", T("Un driver usó mal la memoria.", "A driver misused memory.") };
                case "19": return new string[] { "BAD_POOL_HEADER", T("Un driver o la RAM.", "A driver or the RAM.") };
                case "7A": return new string[] { "KERNEL_DATA_INPAGE_ERROR", T("Windows no pudo leer del disco. Revisá la salud del disco y su cable.", "Windows could not read from the drive. Check the drive health and its cable.") };
                case "F4": return new string[] { "CRITICAL_OBJECT_TERMINATION", T("Se cerró algo vital. Suele ser el disco (cable, SSD con fallas).", "Something vital closed. It is usually the drive (cable, failing SSD).") };
            }
            return null;
        }

        void Explicar(Aviso a, EventRecord e, string appNom)
        {
            string mod, codigo;
            switch (a.Tipo)
            {
                case "video":
                    a.Titulo = T("PANTALLA NEGRA / DRIVER DE VIDEO", "BLACK SCREEN / VIDEO DRIVER");
                    a.Que.Add(T("El driver de la placa de video dejó de responder y Windows lo reinició.", "The graphics card driver stopped responding and Windows restarted it."));
                    a.Que.Add(T("Por eso la pantalla se pone negra unos segundos o el juego se cierra.", "That is why the screen goes black for a few seconds or the game closes."));
                    a.Probar.Add(T("Si tenés overclock o undervolt en la placa de video (MSI Afterburner, etc.), sacalo.", "If you have an overclock or undervolt on the graphics card (MSI Afterburner, etc.), remove it."));
                    a.Probar.Add(T("Reinstalá el driver de video limpio: DDU y después el driver nuevo (DDU está en Apps recomendadas del Xuca OPtimizer).", "Reinstall the video driver cleanly: DDU and then the new driver (DDU is in Xuca OPtimizer's Recommended apps)."));
                    a.Probar.Add(T("Mirá la temperatura de la placa con HWiNFO. Si pasa de 85 °C, limpiá el polvo.", "Check the card temperature with HWiNFO. If it goes over 85 °C, clean the dust."));
                    a.Probar.Add(T("Si se repite con juegos pesados, puede ser la fuente (poca potencia o vieja).", "If it keeps happening with heavy games, it can be the power supply (too weak or old)."));
                    break;
                case "pantallazo":
                    Prop(e, 0, out codigo);
                    string cod = "";
                    Match m = Regex.Match(codigo ?? "", "0x0*([0-9a-fA-F]+)");
                    if (m.Success) cod = m.Groups[1].Value.ToUpperInvariant();
                    string[] info = Pantallazo(cod);
                    a.Titulo = T("PANTALLAZO AZUL", "BLUE SCREEN");
                    a.Que.Add(T("La PC tuvo un pantallazo azul y se reinició", "The PC had a blue screen and restarted") + (cod != "" ? T(" (código ", " (code ") + "0x" + cod + (info != null ? " " + info[0] : "") + ")." : "."));
                    if (info != null) a.Que.Add(info[1]);
                    a.Probar.Add(T("Si tenés overclock, undervolt o EXPO/XMP, probá unos días sin eso. Si no vuelve a pasar, era eso.", "If you have overclock, undervolt or EXPO/XMP, try a few days without it. If it does not happen again, that was it."));
                    a.Probar.Add(T("Actualizá los drivers desde la página de tu placa madre (Buscar drivers del Xuca OPtimizer) y el de video.", "Update the drivers from your motherboard page (Find drivers in Xuca OPtimizer) and the video driver."));
                    a.Probar.Add(T("Si se repite, el archivo del pantallazo está en C:\\Windows\\Minidump: se puede abrir con WhoCrashed o BlueScreenView para ver qué driver fue.", "If it keeps happening, the crash file is in C:\\Windows\\Minidump: open it with WhoCrashed or BlueScreenView to see which driver it was."));
                    break;
                case "apagon":
                    a.Titulo = T("APAGÓN / REINICIO DE GOLPE", "SUDDEN SHUTDOWN / RESTART");
                    a.Que.Add(T("La PC se apagó o se reinició de golpe, sin pantallazo azul y sin apagarse bien.", "The PC turned off or restarted suddenly, without a blue screen and without shutting down properly."));
                    a.Que.Add(T("Si no fue un corte de luz ni el botón, casi siempre es la fuente o la temperatura.", "If it was not a power cut or the button, it is almost always the power supply or the temperature."));
                    a.Probar.Add(T("Mirá las temperaturas con HWiNFO jugando: procesador por debajo de 90 °C, placa de video por debajo de 85 °C.", "Check temperatures with HWiNFO while gaming: CPU below 90 °C, graphics card below 85 °C."));
                    a.Probar.Add(T("Si pasa con juegos pesados, la fuente puede quedarse corta. Probá con OCCT (prueba de fuente) para confirmarlo.", "If it happens with heavy games, the power supply may not be enough. Test with OCCT (power test) to confirm it."));
                    a.Probar.Add(T("Si tenés overclock o undervolt, sacalo.", "If you have overclock or undervolt, remove it."));
                    break;
                case "hardware":
                    bool fatal = e.Level.HasValue && e.Level.Value <= 2;
                    a.Titulo = T("ERROR DE HARDWARE", "HARDWARE ERROR");
                    a.Que.Add(fatal ? T("Windows encontró un error de hardware grave.", "Windows found a serious hardware error.")
                                    : T("Windows encontró un error de hardware y lo corrigió solo. Uno suelto no pasa nada; si se repite, algo está inestable.", "Windows found a hardware error and fixed it by itself. A single one is fine; if it keeps happening, something is unstable."));
                    string[] l = Eventos.Lineas(e);
                    foreach (string x in l) if (Regex.IsMatch(x, "Component|Componente|Error Source|Origen del error")) { a.Que.Add("  " + x); }
                    a.Probar.Add(T("Si dice Processor/Procesador: overclock o undervolt (Curve Optimizer) inestable. Sacalo o bajalo.", "If it says Processor: unstable overclock or undervolt (Curve Optimizer). Remove it or lower it."));
                    a.Probar.Add(T("Si dice Memory/Memoria: probá sin EXPO/XMP.", "If it says Memory: try without EXPO/XMP."));
                    a.Probar.Add(T("Si dice PCI Express: probá la placa de video o el SSD en otra ranura, o poné esa ranura en Gen 4 o Gen 3 en la BIOS.", "If it says PCI Express: try the graphics card or SSD in another slot, or set that slot to Gen 4 or Gen 3 in the BIOS."));
                    break;
                case "disco":
                    a.Titulo = T("ERROR DEL DISCO", "DRIVE ERROR");
                    a.Que.Add(T("Windows tuvo problemas para leer o escribir en un disco. Puede causar tirones, juegos que tardan o archivos dañados.", "Windows had trouble reading or writing a drive. It can cause stutters, slow game loading or damaged files."));
                    a.Probar.Add(T("Mirá la salud del disco con CrystalDiskInfo (está en Apps recomendadas del Xuca OPtimizer). Si no dice \"Bueno\", hacé una copia de tus archivos.", "Check the drive health with CrystalDiskInfo (in Xuca OPtimizer's Recommended apps). If it does not say \"Good\", back up your files."));
                    a.Probar.Add(T("Si es un disco SATA, cambiá el cable de datos o el puerto de la placa madre.", "If it is a SATA drive, change the data cable or the motherboard port."));
                    a.Probar.Add(T("Si es un SSD NVMe, actualizá su firmware desde la página de la marca.", "If it is an NVMe SSD, update its firmware from the brand's page."));
                    break;
                case "memoria":
                    a.Titulo = T("SIN MEMORIA", "OUT OF MEMORY");
                    a.Que.Add(T("Windows se quedó sin memoria y tuvo que cerrar o frenar programas.", "Windows ran out of memory and had to close or slow down programs."));
                    foreach (string x in Eventos.Lineas(e)) if (Regex.IsMatch(x, @"\(\d+\)\s*(consumed|consumi)")) a.Que.Add("  " + x);
                    a.Probar.Add(T("Cerrá lo que no uses mientras jugás (pestañas del navegador, programas de fondo).", "Close what you do not use while gaming (browser tabs, background programs)."));
                    a.Probar.Add(T("Dejá el archivo de paginación en automático (no lo desactives).", "Leave the page file on automatic (do not disable it)."));
                    a.Probar.Add(T("Si un programa usa muchísima memoria, puede tener una pérdida: actualizalo o reinstalalo.", "If one program uses a huge amount of memory, it may have a leak: update or reinstall it."));
                    break;
                case "cpu":
                    a.Titulo = T("PROCESADOR LIMITADO", "CPU LIMITED");
                    a.Que.Add(T("La placa madre bajó la velocidad del procesador (por temperatura o por energía). Eso baja los FPS.", "The motherboard lowered the CPU speed (because of temperature or power). That lowers FPS."));
                    a.Probar.Add(T("Mirá la temperatura del procesador con HWiNFO. Si pasa de 90 °C, revisá el cooler y la pasta térmica.", "Check the CPU temperature with HWiNFO. If it goes over 90 °C, check the cooler and thermal paste."));
                    a.Probar.Add(T("En notebook: usala enchufada y con el plan de energía en rendimiento.", "On a laptop: keep it plugged in and on a performance power plan."));
                    break;
                case "app":
                    bool colgado = e.Id == 1002;
                    Prop(e, 3, out mod);
                    a.Titulo = colgado ? T("PROGRAMA COLGADO", "PROGRAM HUNG") : T("PROGRAMA CERRADO", "PROGRAM CRASHED");
                    a.Que.Add(colgado ? T(appNom + " dejó de responder y Windows lo cerró.", appNom + " stopped responding and Windows closed it.")
                                      : T(appNom + " se cerró solo por un error.", appNom + " closed by itself because of an error."));
                    if (!colgado && mod != "")
                    {
                        a.Que.Add(T("Falló en: ", "It failed in: ") + mod);
                        string ml = mod.ToLowerInvariant();
                        if (Regex.IsMatch(ml, "^(nvwgf2um|nvd3dum|nvoglv|nvlddmkm|nvgpucomp|nvapi)"))
                            a.Probar.Add(T("Falló en el driver de NVIDIA: reinstalalo limpio con DDU, o probá la versión anterior.", "It failed in the NVIDIA driver: reinstall it cleanly with DDU, or try the previous version."));
                        else if (Regex.IsMatch(ml, "^(atidxx|atiumd|amdxx|amdvlk|amdihk|aticfx)"))
                            a.Probar.Add(T("Falló en el driver de AMD: reinstalalo limpio con DDU, o probá la versión anterior.", "It failed in the AMD driver: reinstall it cleanly with DDU, or try the previous version."));
                        else if (Regex.IsMatch(ml, "^(igd|igc|igxe|ig\\d)"))
                            a.Probar.Add(T("Falló en el driver de video de Intel: actualizalo.", "It failed in the Intel video driver: update it."));
                        else if (Regex.IsMatch(ml, "overlay|discord|rtss|gameoverlayrenderer|obs|nvspcap|medal|overwolf"))
                            a.Probar.Add(T("Falló en un overlay (Discord, Steam, RTSS, OBS, NVIDIA, etc.): apagá ese overlay y probá de nuevo.", "It failed in an overlay (Discord, Steam, RTSS, OBS, NVIDIA, etc.): turn that overlay off and try again."));
                        else if (Regex.IsMatch(ml, "^(d3d|dxgi|dxcore|vulkan)"))
                            a.Probar.Add(T("Falló en DirectX/Vulkan: actualizá el driver de video.", "It failed in DirectX/Vulkan: update the video driver."));
                    }
                    if (Regex.IsMatch((appNom ?? "").ToLowerInvariant(), "win64|shipping|game|\\.exe"))
                        a.Probar.Add(T("Si es un juego: verificá los archivos (Steam: Propiedades > Archivos instalados > Verificar).", "If it is a game: verify the files (Steam: Properties > Installed files > Verify)."));
                    a.Probar.Add(T("Si tenés overclock, undervolt o EXPO/XMP y los juegos se cierran seguido, probá sin eso.", "If you have overclock, undervolt or EXPO/XMP and games close often, try without it."));
                    break;
            }
        }
    }
}
