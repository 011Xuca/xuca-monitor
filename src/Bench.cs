// Pulso: modo benchmark. Registra una corrida (desde que se abre un juego de la lista del overlay hasta que se cierra),
// y al terminar arma un resumen con los problemas que encontro en los datos. Cada corrida queda en Documentos\Pulso\benchmarks
// (un .txt legible por corrida y una fila en benchmarks.csv para comparar), y el resumen aparece en la pestaña Rendimiento.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Xuca
{
    internal static class Bench
    {
        // ---------- Umbrales (todo lo que decide si algo es un "problema") ----------
        public const int MinSegundos = 20;          // corridas mas cortas no se resumen (abrir y cerrar el juego no es una prueba)
        const double PausaMs = 1000;                // un cuadro de mas de 1 s es pausa, carga o juego minimizado: no cuenta
        const double TironFactor = 2.5;             // tiron (stutter): un cuadro que tarda mas de 2.5 veces la mediana...
        const double TironMinMs = 25;               // ...y al menos 25 ms (a 300 FPS, 2.5 veces la mediana son 8 ms: no se nota)
        const double TironesPorMinuto = 6;          // mas de 6 tirones por minuto de juego se marca como problema
        const double LowVsPromedio = 0.5;           // 1 % low por debajo de la mitad del promedio: FPS muy desparejos
        public const float TempPeligro = MonitorPC.TempRojo;   // CPU y GPU: desde 90 °C es zona de peligro (en CPU, o 3 °C antes de su TjMax si es menor)
        const double CaidaReloj = 0.85;             // throttling: la frecuencia cae debajo del 85 % de la que logra cargado y fresco
        const int MinMuestrasThrottle = 3;          // ...durante al menos 3 muestras (segundos)
        const float CargaAlta = 80f;                // "cargado": uso de 80 % o mas
        const float GpuTope = 95f;                  // GPU al 95 % o mas en promedio: la GPU es el limite (lo normal en juegos)
        const float GpuEsperando = 80f;             // GPU por debajo del 80 % en promedio...
        const float NucleoTope = 90f;               // ...con el nucleo mas cargado del CPU al 90 % o mas: el CPU limita
        const float RamLlena = 90f;                 // RAM al 90 % o mas en algun momento: puede empezar a usar el disco como memoria

        class Punto { public float? CpuT, CpuU, CpuMhz, CpuW, GpuT, GpuU, GpuMhz, GpuW, NucleoMax; public float Ram = -1; public int Tj; public bool Frente; }

        static readonly object lk = new object();
        static bool corriendo;
        static string juego = "";
        static DateTime inicio;
        // pid -> swapchain -> frametimes (ms); y el ultimo tiempo de cada una
        static Dictionary<uint, Dictionary<ulong, List<float>>> cuadros;
        static Dictionary<uint, Dictionary<ulong, double>> ultimos;
        static List<Punto> muestras;
        static List<string> eventos;
        static string cpuNombre = "", gpuNombre = "";

        public static bool Corriendo { get { return corriendo; } }
        public static string Motivo = "";             // por que no hay FPS (si no se pudo medir), para el resumen
        public static Action<string, string> Listo;   // (resumen completo, resumen corto): lo pone la app

        static string T(string es, string en) { return Modulo.T(es, en); }

        public static void Empezar(string nombreJuego)
        {
            lock (lk)
            {
                corriendo = true; juego = nombreJuego ?? ""; inicio = DateTime.Now;
                cuadros = new Dictionary<uint, Dictionary<ulong, List<float>>>();
                ultimos = new Dictionary<uint, Dictionary<ulong, double>>();
                muestras = new List<Punto>(); eventos = new List<string>();
                cpuNombre = gpuNombre = "";
            }
        }

        // Desde el hilo de ETW, por cada cuadro presentado: lo mas barato posible
        public static void Cuadro(uint pid, ulong sc, double ms)
        {
            if (!corriendo) return;
            lock (lk)
            {
                if (!corriendo) return;
                Dictionary<ulong, double> u; Dictionary<ulong, List<float>> c;
                if (!ultimos.TryGetValue(pid, out u)) { u = new Dictionary<ulong, double>(); ultimos[pid] = u; cuadros[pid] = new Dictionary<ulong, List<float>>(); }
                c = cuadros[pid];
                double antes;
                if (u.TryGetValue(sc, out antes))
                {
                    List<float> l;
                    if (!c.TryGetValue(sc, out l)) { l = new List<float>(); c[sc] = l; }
                    l.Add((float)(ms - antes));
                }
                u[sc] = ms;
            }
        }

        // Una vez por segundo mientras el juego esta abierto
        public static void Muestra(Lectura l, bool juegoAlFrente)
        {
            if (!corriendo || l == null) return;
            Punto m = new Punto();
            m.CpuT = l.CpuTemp; m.CpuU = l.CpuLoad; m.CpuMhz = l.CpuClockProm; m.CpuW = l.CpuPower; m.Tj = l.CpuTjMax;
            m.GpuT = l.GpuTemp; m.GpuU = l.GpuLoad; m.GpuMhz = l.GpuClock; m.GpuW = l.GpuPower; m.Ram = l.RamUso; m.Frente = juegoAlFrente;
            float max = -1; foreach (float x in l.CpuNucleos) if (x > max) max = x;
            if (max >= 0) m.NucleoMax = max;
            lock (lk)
            {
                if (!corriendo) return;
                muestras.Add(m);
                if (l.CpuNombre.Length > 0) cpuNombre = l.CpuNombre;
                if (l.GpuNombre.Length > 0) gpuNombre = l.GpuNombre;
            }
        }

        // Un aviso de otra pestaña (Rendimiento, Red) durante la corrida: algo pudo ensuciar la prueba
        public static void Evento(string titulo)
        {
            if (!corriendo || string.IsNullOrEmpty(titulo)) return;
            lock (lk) { if (corriendo && eventos.Count < 20) eventos.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + titulo); }
        }

        // El juego se cerro (o se apago el overlay): resume, guarda y avisa. Nunca tira error.
        public static void Terminar(string carpeta)
        {
            List<float> ft = null; List<Punto> ms; List<string> ev; string jg, cpu, gpu; DateTime ini;
            lock (lk)
            {
                if (!corriendo) return;
                corriendo = false;
                foreach (Dictionary<ulong, List<float>> porSc in cuadros.Values)
                    foreach (List<float> l in porSc.Values) if (ft == null || l.Count > ft.Count) ft = l;   // la swapchain del juego: la que mas cuadros tuvo
                ms = muestras; ev = eventos; jg = juego; cpu = cpuNombre; gpu = gpuNombre; ini = inicio;
                cuadros = null; ultimos = null; muestras = null; eventos = null;
            }
            try
            {
                double seg = (DateTime.Now - ini).TotalSeconds;
                if (seg < MinSegundos) return;
                string[] res = Resumir(jg, ini, seg, ft, ms, ev, cpu, gpu, carpeta);
                if (Listo != null) Listo(res[0], res[1]);
            }
            catch (Exception ex)
            {
                if (Listo != null) Listo(T("BENCHMARK: no se pudo armar el resumen (", "BENCHMARK: could not build the summary (") + ex.Message + ")", null);
            }
        }

        static double Prom(List<float> l) { double s = 0; foreach (float x in l) s += x; return l.Count > 0 ? s / l.Count : double.NaN; }
        static float Max(List<float> l) { float m = float.NaN; foreach (float x in l) if (float.IsNaN(m) || x > m) m = x; return m; }
        static List<float> Col(List<Punto> ms, Func<Punto, float?> f) { List<float> r = new List<float>(); foreach (Punto m in ms) { float? v = f(m); if (v.HasValue) r.Add(v.Value); } return r; }
        static string F0(double v) { return double.IsNaN(v) ? "-" : Math.Round(v).ToString(CultureInfo.InvariantCulture); }
        static string F1(double v) { return double.IsNaN(v) ? "-" : v.ToString("0.0", CultureInfo.InvariantCulture); }
        static string Ghz(double mhz) { return double.IsNaN(mhz) ? "-" : (mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " GHz"; }

        // Low de los peores cuadros: promedio del peor 1 % (o 0.1 %) de los frametimes, pasado a FPS (como el overlay)
        static double Low(List<float> desc, double fraccion)
        {
            int k = Math.Max(1, (int)(desc.Count * fraccion));
            double s = 0; for (int i = 0; i < k; i++) s += desc[i];
            return s > 0 ? 1000.0 / (s / k) : double.NaN;
        }

        // Throttling: muestras cargadas donde la frecuencia cayo debajo del 85 % de la que logra cargado y lejos del limite
        static int Frenadas(List<Punto> ms, Func<Punto, float?> mhz, Func<Punto, float?> uso, Func<Punto, float?> temp, float limite, bool caliente)
        {
            float refMhz = 0;
            foreach (Punto m in ms) { float? f = mhz(m), u = uso(m), t = temp(m); if (f.HasValue && u.HasValue && u.Value >= CargaAlta && (!t.HasValue || t.Value < limite - 10) && f.Value > refMhz) refMhz = f.Value; }
            if (refMhz <= 0) return 0;
            int n = 0;
            foreach (Punto m in ms)
            {
                float? f = mhz(m), u = uso(m), t = temp(m);
                if (!f.HasValue || !u.HasValue || u.Value < CargaAlta || f.Value >= refMhz * CaidaReloj) continue;
                bool hot = t.HasValue && t.Value >= limite;
                if (hot == caliente && (caliente || (t.HasValue && t.Value < limite - 10))) n++;
            }
            return n;
        }

        static string[] Resumir(string jg, DateTime ini, double seg, List<float> todos, List<Punto> todas, List<string> ev, string cpu, string gpu, string carpeta)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            List<string> L = new List<string>(), problemas = new List<string>(), info = new List<string>();
            L.Add(T("BENCHMARK: ", "BENCHMARK: ") + jg + " · " + Modulo.Fecha(ini) + " · " + ((int)(seg / 60)) + " min " + ((int)(seg % 60)) + " s");
            L.Add("");

            // ---- FPS (toda la corrida, sin pausas) ----
            double fpsProm = double.NaN, low1 = double.NaN, low01 = double.NaN, ftProm = double.NaN, ftMax = double.NaN;
            int tirones = 0; double minutosJuego = 0;
            List<float> ft = new List<float>();
            if (todos != null) foreach (float x in todos) if (x > 0 && x < PausaMs) ft.Add(x);
            if (ft.Count >= 100)
            {
                double tot = 0; foreach (float x in ft) tot += x;
                minutosJuego = tot / 60000.0;
                fpsProm = ft.Count * 1000.0 / tot; ftProm = tot / ft.Count;
                List<float> desc = new List<float>(ft); desc.Sort(); float mediana = desc[desc.Count / 2]; desc.Reverse();
                ftMax = desc[0]; low1 = Low(desc, 0.01); low01 = Low(desc, 0.001);
                double corte = Math.Max(mediana * TironFactor, TironMinMs);
                foreach (float x in ft) if (x > corte) tirones++;
                L.Add(T("FPS (toda la corrida, sin pausas ni pantallas de carga de más de 1 s):", "FPS (whole run, without pauses or loading screens over 1 s):"));
                L.Add(T("  Promedio ", "  Average ") + F0(fpsProm) + "   ·   1 % low " + F0(low1) + "   ·   0.1 % low " + F0(low01)
                    + T("   ·   frametime promedio ", "   ·   average frametime ") + F1(ftProm) + T(" ms (el más largo: ", " ms (longest: ") + F0(ftMax) + " ms)");
                L.Add(T("  Cuadros medidos: ", "  Frames measured: ") + ft.Count.ToString(inv) + T("   ·   tirones (cuadros de más de ", "   ·   stutters (frames over ") + F0(corte) + " ms): " + tirones);
                double porMin = minutosJuego > 0 ? tirones / minutosJuego : 0;
                if (porMin > TironesPorMinuto)
                    problemas.Add(T("Tirones: " + tirones + " cuadros tardaron más de " + F0(corte) + " ms (" + F1(porMin) + " por minuto). Se sienten como saltos aunque el promedio sea alto. Suelen ser compilación de shaders, el disco, la RAM llena o algo de fondo.",
                                    "Stutter: " + tirones + " frames took more than " + F0(corte) + " ms (" + F1(porMin) + " per minute). They feel like hitches even if the average is high. Usually shader compilation, the drive, full RAM or something in the background."));
                if (low1 < fpsProm * LowVsPromedio)
                    problemas.Add(T("FPS muy desparejos: el 1 % low (" + F0(low1) + ") es menos de la mitad del promedio (" + F0(fpsProm) + ").",
                                    "Very uneven FPS: the 1% low (" + F0(low1) + ") is less than half the average (" + F0(fpsProm) + ")."));
            }
            else
            {
                L.Add(T("FPS: no se pudieron medir. ", "FPS: could not be measured. ") + (Motivo != "" ? Motivo
                    : T("No llegaron cuadros del juego: puede usar una API que el overlay no mide (Vulkan u OpenGL) o pantalla completa exclusiva.",
                        "No frames arrived from the game: it may use an API the overlay does not measure (Vulkan or OpenGL) or exclusive full screen.")));
            }

            // ---- Sensores (solo con el juego al frente, si las hay) ----
            List<Punto> ms = new List<Punto>();
            foreach (Punto m in todas) if (m.Frente) ms.Add(m);
            if (ms.Count < 5) ms = todas;
            int tj = 100; foreach (Punto m in ms) if (m.Tj > 0) tj = m.Tj;
            float limCpu = MonitorPC.RojoCpu(tj);

            List<float> cT = Col(ms, delegate (Punto m) { return m.CpuT; }), cU = Col(ms, delegate (Punto m) { return m.CpuU; }), cF = Col(ms, delegate (Punto m) { return m.CpuMhz; }), cW = Col(ms, delegate (Punto m) { return m.CpuW; });
            List<float> gT = Col(ms, delegate (Punto m) { return m.GpuT; }), gU = Col(ms, delegate (Punto m) { return m.GpuU; }), gF = Col(ms, delegate (Punto m) { return m.GpuMhz; }), gW = Col(ms, delegate (Punto m) { return m.GpuW; });
            List<float> nMax = Col(ms, delegate (Punto m) { return m.NucleoMax; });
            List<float> ram = new List<float>(); foreach (Punto m in ms) if (m.Ram >= 0) ram.Add(m.Ram);

            L.Add("");
            L.Add(T("SENSORES (promedio / máximo):", "SENSORS (average / max):"));
            if (cT.Count + cU.Count + cF.Count + cW.Count > 0)
                L.Add("  CPU" + (cpu != "" ? " (" + cpu + ")" : "") + ": " + F0(Prom(cT)) + " / " + F0(Max(cT)) + " °C   ·   " + T("uso ", "usage ") + F0(Prom(cU)) + " / " + F0(Max(cU)) + " %   ·   "
                    + Ghz(Prom(cF)) + "   ·   " + F0(Prom(cW)) + " / " + F0(Max(cW)) + " W" + (nMax.Count > 0 ? T("   ·   núcleo más cargado ", "   ·   busiest core ") + F0(Prom(nMax)) + " %" : ""));
            else L.Add(T("  CPU: sin datos de sensores.", "  CPU: no sensor data."));
            if (gT.Count + gU.Count + gF.Count + gW.Count > 0)
                L.Add("  GPU" + (gpu != "" ? " (" + gpu + ")" : "") + ": " + F0(Prom(gT)) + " / " + F0(Max(gT)) + " °C   ·   " + T("uso ", "usage ") + F0(Prom(gU)) + " / " + F0(Max(gU)) + " %   ·   "
                    + F0(Prom(gF)) + " MHz   ·   " + F0(Prom(gW)) + " / " + F0(Max(gW)) + " W");
            else L.Add(T("  GPU: sin datos de sensores.", "  GPU: no sensor data."));
            if (ram.Count > 0) L.Add("  RAM: " + F0(Prom(ram)) + " / " + F0(Max(ram)) + " %");
            if (cT.Count == 0) info.Add(T("Sin temperatura del procesador: hace falta PawnIO (pestaña PC).", "No CPU temperature: PawnIO is needed (PC tab)."));

            // ---- Problemas a partir de los sensores ----
            int cpuTerm = Frenadas(ms, delegate (Punto m) { return m.CpuMhz; }, delegate (Punto m) { return m.CpuU; }, delegate (Punto m) { return m.CpuT; }, limCpu, true);
            int cpuEnergia = Frenadas(ms, delegate (Punto m) { return m.CpuMhz; }, delegate (Punto m) { return m.CpuU; }, delegate (Punto m) { return m.CpuT; }, limCpu, false);
            int gpuTerm = Frenadas(ms, delegate (Punto m) { return m.GpuMhz; }, delegate (Punto m) { return m.GpuU; }, delegate (Punto m) { return m.GpuT; }, TempPeligro, true);
            int gpuPot = Frenadas(ms, delegate (Punto m) { return m.GpuMhz; }, delegate (Punto m) { return m.GpuU; }, delegate (Punto m) { return m.GpuT; }, TempPeligro, false);
            if (cpuTerm >= MinMuestrasThrottle)
                problemas.Add(T("Throttling térmico del procesador: durante " + cpuTerm + " s estuvo a " + F0(limCpu) + " °C o más y bajó su frecuencia más de un 15 %. Limpiá el cooler, revisá la pasta térmica y el flujo de aire.",
                                "CPU thermal throttling: for " + cpuTerm + " s it was at " + F0(limCpu) + " °C or more and dropped its clock by over 15 %. Clean the cooler, check the thermal paste and airflow."));
            else if (Max(cT) >= limCpu)
                problemas.Add(T("El procesador llegó a " + F0(Max(cT)) + " °C (zona de peligro desde " + F0(limCpu) + " °C), aunque no llegó a frenarse.",
                                "The CPU reached " + F0(Max(cT)) + " °C (danger zone from " + F0(limCpu) + " °C), although it did not throttle."));
            if (cpuEnergia >= MinMuestrasThrottle)
                problemas.Add(T("El procesador bajó su frecuencia más de un 15 % durante " + cpuEnergia + " s sin estar caliente: límite de energía (plan de energía, BIOS o notebook a batería).",
                                "The CPU dropped its clock by over 15 % for " + cpuEnergia + " s without being hot: a power limit (power plan, BIOS or laptop on battery)."));
            if (gpuTerm >= MinMuestrasThrottle)
                problemas.Add(T("Throttling térmico de la placa de video: durante " + gpuTerm + " s estuvo a " + F0(TempPeligro) + " °C o más y bajó su frecuencia más de un 15 %.",
                                "Graphics card thermal throttling: for " + gpuTerm + " s it was at " + F0(TempPeligro) + " °C or more and dropped its clock by over 15 %."));
            else if (Max(gT) >= TempPeligro)
                problemas.Add(T("La placa de video llegó a " + F0(Max(gT)) + " °C (zona de peligro desde " + F0(TempPeligro) + " °C).",
                                "The graphics card reached " + F0(Max(gT)) + " °C (danger zone from " + F0(TempPeligro) + " °C)."));
            if (gpuPot >= MinMuestrasThrottle)
                info.Add(T("La placa de video bajó su frecuencia durante " + gpuPot + " s estando fresca y al tope de uso: es su límite de potencia (normal en muchas placas).",
                           "The graphics card lowered its clock for " + gpuPot + " s while cool and fully loaded: that is its power limit (normal on many cards)."));
            double gProm = Prom(gU), nProm = Prom(nMax);
            if (!double.IsNaN(gProm))
            {
                if (gProm >= GpuTope) info.Add(T("La placa de video fue el límite (" + F0(gProm) + " % de uso promedio): es lo normal en un juego exigente.", "The graphics card was the limit (" + F0(gProm) + " % average usage): normal in a demanding game."));
                else if (gProm < GpuEsperando && !double.IsNaN(nProm) && nProm >= NucleoTope)
                    problemas.Add(T("Cuello de botella del procesador: la placa de video estuvo al " + F0(gProm) + " % en promedio mientras el núcleo más cargado del procesador estaba al " + F0(nProm) + " %. El procesador no le da abasto.",
                                    "CPU bottleneck: the graphics card averaged " + F0(gProm) + " % while the busiest CPU core was at " + F0(nProm) + " %. The CPU cannot keep up."));
                else if (gProm < GpuEsperando)
                    info.Add(T("La placa de video estuvo al " + F0(gProm) + " % en promedio sin que el procesador estuviera al tope: probablemente hay un límite de FPS (VSync o tope de FPS).",
                               "The graphics card averaged " + F0(gProm) + " % without the CPU being maxed out: there is probably an FPS limit (VSync or an FPS cap)."));
            }
            if (Max(ram) >= RamLlena)
                problemas.Add(T("La RAM llegó al " + F0(Max(ram)) + " %: con la memoria casi llena Windows empieza a usar el disco, y eso da tirones.",
                                "RAM reached " + F0(Max(ram)) + " %: with memory almost full, Windows starts using the drive, which causes stutter."));
            if (ev.Count > 0)
            {
                problemas.Add(T("Durante la prueba hubo avisos que pudieron ensuciarla:", "During the run there were alerts that may have spoiled it:"));
                foreach (string e in ev) problemas.Add("    " + e);
            }

            L.Add("");
            L.Add(T("PROBLEMAS ENCONTRADOS:", "PROBLEMS FOUND:"));
            if (problemas.Count == 0) L.Add(T("  Ninguno con los datos que hay.", "  None with the available data."));
            foreach (string p in problemas) L.Add(p.StartsWith("    ") ? p : "  - " + p);
            if (info.Count > 0) { L.Add(""); L.Add(T("PARA TENER EN CUENTA:", "GOOD TO KNOW:")); foreach (string p in info) L.Add("  - " + p); }

            // ---- Guardar y comparar con la corrida anterior del mismo juego ----
            string dir = Path.Combine(carpeta, "benchmarks"), csv = Path.Combine(dir, "benchmarks.csv"), comp = null, txt = null;
            try
            {
                Directory.CreateDirectory(dir);
                comp = Comparar(csv, jg, fpsProm, low1);
                string[] fila = { ini.ToString("yyyy-MM-dd HH:mm:ss", inv), jg.Replace(";", ","), F0(seg), F1(fpsProm), F1(low1), F1(low01), F1(ftProm), F0(ftMax), tirones.ToString(inv),
                    F0(Prom(cT)), F0(Max(cT)), F0(Prom(cU)), F0(Prom(cF)), F0(Prom(cW)), F0(Prom(gT)), F0(Max(gT)), F0(Prom(gU)), F0(Prom(gF)), F0(Prom(gW)), F0(Prom(ram)), F0(Max(ram)), problemas.Count.ToString(inv) };
                if (!File.Exists(csv))
                    File.WriteAllText(csv, "fecha;juego;segundos;fps_prom;low1;low01;ft_prom_ms;ft_max_ms;tirones;cpu_temp_prom;cpu_temp_max;cpu_uso_prom;cpu_mhz_prom;cpu_w_prom;gpu_temp_prom;gpu_temp_max;gpu_uso_prom;gpu_mhz_prom;gpu_w_prom;ram_prom;ram_max;problemas\r\n", Encoding.UTF8);
                File.AppendAllText(csv, string.Join(";", fila) + "\r\n", Encoding.UTF8);
                string limpio = jg; foreach (char ch in Path.GetInvalidFileNameChars()) limpio = limpio.Replace(ch, '_');
                txt = Path.Combine(dir, ini.ToString("yyyy-MM-dd_HH-mm-ss", inv) + "_" + limpio + ".txt");
            }
            catch { txt = null; }
            if (comp != null) { L.Add(""); L.Add(comp); }
            if (txt != null) { L.Add(""); L.Add(T("Guardado en ", "Saved in ") + txt + T(" (y una fila en benchmarks.csv para comparar)", " (and a row in benchmarks.csv to compare)")); }
            string texto = string.Join("\r\n", L.ToArray());
            if (txt != null) { try { File.WriteAllText(txt, texto + "\r\n", Encoding.UTF8); } catch { } }

            string corto = jg + ": " + (double.IsNaN(fpsProm) ? T("sin FPS", "no FPS") : F0(fpsProm) + T(" FPS promedio, 1 % low ", " FPS average, 1% low ") + F0(low1))
                + " · " + (problemas.Count == 0 ? T("sin problemas", "no problems") : problemas.Count + T(" problema(s): mirá Rendimiento", " problem(s): see Performance"));
            return new string[] { texto, corto };
        }

        // Compara con la ultima corrida del mismo juego en el CSV (si hay)
        static string Comparar(string csv, string jg, double fps, double low1)
        {
            if (!File.Exists(csv) || double.IsNaN(fps)) return null;
            string[] lineas = File.ReadAllLines(csv, Encoding.UTF8);
            for (int i = lineas.Length - 1; i >= 1; i--)
            {
                string[] c = lineas[i].Split(';');
                if (c.Length < 5 || c[1] != jg.Replace(";", ",")) continue;
                double f0, l0;
                if (!double.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out f0) || f0 <= 0) return null;
                string r = T("COMPARADO CON LA CORRIDA ANTERIOR (", "COMPARED WITH THE PREVIOUS RUN (") + c[0] + "):  FPS " + Delta(fps, f0);
                if (double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out l0) && l0 > 0 && !double.IsNaN(low1)) r += "   ·   1 % low " + Delta(low1, l0);
                return r;
            }
            return null;
        }

        static string Delta(double ahora, double antes)
        {
            double p = (ahora - antes) * 100 / antes;
            return F0(antes) + " → " + F0(ahora) + " (" + (p >= 0 ? "+" : "") + F1(p) + " %)";
        }
    }
}
