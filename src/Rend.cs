// Monitor de rendimiento: mientras jugas (juego en pantalla completa adelante), vigila procesador, RAM, disco y
// placa de video, y cuando algo se dispara te dice el culpable (programa de fondo, temperatura, RAM llena, disco al 100 %).
// No mide FPS (eso pide permisos de administrador): detecta lo que suele causar los bajones.
// Fuera de los juegos solo mira cada 2 s que ventana esta adelante, y no gasta casi nada.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Xuca
{
    public class MonitorRend : Modulo
    {
        public MonitorRend()
        {
            Clave = "rend"; PestEs = "Rendimiento"; PestEn = "Performance"; TipEs = "Rend"; TipEn = "Perf";
            NombreEs = "Monitor de rendimiento"; NombreEn = "Performance Monitor";
            RegistroArch = "registro_rendimiento.txt"; HistArch = "historial_rendimiento.txt";
            ContEs = "Bajones"; ContEn = "Drops";
            VacioEs = "Todavía no hubo bajones. Mientras jugás (en pantalla completa), si algo hace bajar los FPS, acá vas a ver el culpable.";
            VacioEn = "No drops yet. While you play (fullscreen), if something makes your FPS drop, you will see the culprit here.";
            ApagadoEs = "El monitor de rendimiento está apagado. Prendelo con la casilla \"Activado\" para saber qué hace bajar los FPS mientras jugás.";
            ApagadoEn = "The performance monitor is off. Turn it on with the \"Enabled\" box to find out what makes your FPS drop while you play.";
        }

        Thread hilo;
        volatile bool parar;
        readonly ManualResetEvent despertar = new ManualResetEvent(false);

        protected override void Empezar()
        {
            parar = false; despertar.Reset();
            hilo = new Thread(Vuelta);
            hilo.IsBackground = true;
            hilo.Priority = ThreadPriority.BelowNormal;
            hilo.Start();
        }

        protected override void Terminar()
        {
            parar = true;
            despertar.Set();
            if (hilo != null) hilo.Join(3000);
        }

        bool Dormir(int ms) { return despertar.WaitOne(ms) || parar; }

        // ---------- Que juego esta adelante ----------
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int f);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

        // Programas en pantalla completa que no son juegos
        static readonly Regex NoJuego = new Regex("^(explorer|chrome|msedge|firefox|opera|opera_gx|brave|vivaldi|vlc|mpc-hc64|mpc-hc|mpc-be64|potplayer|potplayermini64|wmplayer|applicationframehost|lockapp|searchhost|startmenuexperiencehost|shellexperiencehost|textinputhost|powerpnt|xucamonitor|obs64|discord|spotify|steamwebhelper)$", RegexOptions.IgnoreCase);

        static readonly int miPid = Process.GetCurrentProcess().Id;
        int pidJuego;
        string nomJuego = "";

        // Devuelve el pid del juego en pantalla completa que esta adelante (0 si no hay)
        int JuegoAdelante(out string nombre)
        {
            nombre = "";
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return 0;
            StringBuilder cls = new StringBuilder(64);
            GetClassName(h, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return 0;
            RECT r;
            if (!GetWindowRect(h, out r)) return 0;
            MONITORINFO mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return 0;
            if (r.L > mi.rcMonitor.L || r.T > mi.rcMonitor.T || r.R < mi.rcMonitor.R || r.B < mi.rcMonitor.B) return 0;
            int pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0 || pid == miPid) return 0;
            if (pid == pidJuego) { nombre = nomJuego; return pid; }
            try { using (Process p = Process.GetProcessById(pid)) nombre = p.ProcessName; } catch { return 0; }
            if (NoJuego.IsMatch(nombre)) return 0;
            return pid;
        }

        // ---------- Mediciones (sin WMI: contadores de Windows y llamadas directas) ----------
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public int dwLength; public int dwMemoryLoad; public ulong total, avail, totPage, availPage, totVirt, availVirt, availExt; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

        [StructLayout(LayoutKind.Explicit)] struct PDH_VAL { [FieldOffset(0)] public int CStatus; [FieldOffset(8)] public double Valor; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr c, int fmt, IntPtr tipo, out PDH_VAL v);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr c, int fmt, ref int tam, out int cant, IntPtr buf);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr q);
        const int PDH_DOUBLE = 0x200 | 0x8000; // double, sin limite de 100

        IntPtr q, cPerf, cDisco, cGpu;
        long idle0, ker0, usr0;

        void AbrirContadores()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out q) != 0) { q = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(q, "\\Processor Information(_Total)\\% Processor Performance", IntPtr.Zero, out cPerf) != 0) cPerf = IntPtr.Zero;
            if (PdhAddEnglishCounter(q, "\\PhysicalDisk(*)\\% Idle Time", IntPtr.Zero, out cDisco) != 0) cDisco = IntPtr.Zero;
            if (PdhAddEnglishCounter(q, "\\GPU Engine(*engtype_3D)\\Utilization Percentage", IntPtr.Zero, out cGpu) != 0) cGpu = IntPtr.Zero;
            PdhCollectQueryData(q);
            GetSystemTimes(out idle0, out ker0, out usr0);
        }

        void CerrarContadores()
        {
            if (q != IntPtr.Zero) PdhCloseQuery(q);
            q = cPerf = cDisco = cGpu = IntPtr.Zero;
        }

        // Lee todas las instancias de un contador (nombre, valor)
        static List<KeyValuePair<string, double>> Arreglo(IntPtr c)
        {
            List<KeyValuePair<string, double>> r = new List<KeyValuePair<string, double>>();
            if (c == IntPtr.Zero) return r;
            int tam = 0, cant;
            PdhGetFormattedCounterArray(c, PDH_DOUBLE, ref tam, out cant, IntPtr.Zero);
            if (tam <= 0) return r;
            IntPtr buf = Marshal.AllocHGlobal(tam);
            try
            {
                if (PdhGetFormattedCounterArray(c, PDH_DOUBLE, ref tam, out cant, buf) != 0) return r;
                // Cada item: puntero al nombre y el valor, alineado a 8 bytes (24 bytes en 32 y 64 bits)
                const int paso = 24;
                for (int i = 0; i < cant; i++)
                {
                    IntPtr item = new IntPtr(buf.ToInt64() + (long)i * paso);
                    string nom = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    int st = Marshal.ReadInt32(item, 8);
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    if (st == 0 || st == 1) r.Add(new KeyValuePair<string, double>(nom ?? "", v));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return r;
        }

        class Muestra { public double Cpu = -1, Perf = -1, DiscoOcupado = -1, Gpu = -1; public int Ram = -1; }

        Muestra Medir()
        {
            Muestra m = new Muestra();
            long i1, k1, u1;
            if (GetSystemTimes(out i1, out k1, out u1))
            {
                long di = i1 - idle0, dt = (k1 - ker0) + (u1 - usr0);
                if (dt > 0) m.Cpu = Math.Max(0, Math.Min(100, 100.0 * (dt - di) / dt));
                idle0 = i1; ker0 = k1; usr0 = u1;
            }
            MEMSTAT ms = new MEMSTAT(); ms.dwLength = Marshal.SizeOf(typeof(MEMSTAT));
            if (GlobalMemoryStatusEx(ref ms)) m.Ram = ms.dwMemoryLoad;
            if (q != IntPtr.Zero && PdhCollectQueryData(q) == 0)
            {
                PDH_VAL v;
                if (cPerf != IntPtr.Zero && PdhGetFormattedCounterValue(cPerf, PDH_DOUBLE, IntPtr.Zero, out v) == 0 && (v.CStatus == 0 || v.CStatus == 1)) m.Perf = v.Valor;
                // Disco: el mas ocupado (100 - % inactivo), sin contar el total
                foreach (KeyValuePair<string, double> d in Arreglo(cDisco))
                    if (d.Key != "_Total") m.DiscoOcupado = Math.Max(m.DiscoOcupado, Math.Max(0, Math.Min(100, 100 - d.Value)));
                // Placa de video: lo que usa cada programa en 3D, se suma por programa y se toma el mayor
                Dictionary<string, double> porPid = new Dictionary<string, double>();
                foreach (KeyValuePair<string, double> g in Arreglo(cGpu))
                {
                    Match mt = Regex.Match(g.Key, "pid_(\\d+)_");
                    string k = mt.Success ? mt.Groups[1].Value : g.Key;
                    double s; porPid.TryGetValue(k, out s); porPid[k] = s + g.Value;
                }
                foreach (double s in porPid.Values) m.Gpu = Math.Max(m.Gpu, Math.Min(100, s));
            }
            return m;
        }

        // ---------- Quien esta usando la PC (solo cuando algo se dispara) ----------
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int acc, bool inh, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr h, out long c, out long e, out long k, out long u);
        [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS io);
        [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr h, out PMC pmc, int cb);
        [StructLayout(LayoutKind.Sequential)] struct PMC { public int cb, PageFaultCount; public IntPtr Peak, WorkingSet, QPeakPaged, QPaged, QPeakNonPaged, QNonPaged, Pagefile, PeakPagefile, Private; }

        class Proc { public int Pid; public string Nombre; public long Cpu; public ulong Io; public long Mem; }

        static Dictionary<int, Proc> Foto()
        {
            Dictionary<int, Proc> r = new Dictionary<int, Proc>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    Proc x = new Proc(); x.Pid = p.Id; x.Nombre = p.ProcessName;
                    IntPtr h = OpenProcess(0x1000, false, p.Id); // solo consulta limitada
                    if (h != IntPtr.Zero)
                    {
                        long c, e, k, u;
                        if (GetProcessTimes(h, out c, out e, out k, out u)) x.Cpu = k + u;
                        IO_COUNTERS io;
                        if (GetProcessIoCounters(h, out io)) x.Io = io.ReadBytes + io.WriteBytes;
                        PMC pm; pm.cb = 0;
                        if (GetProcessMemoryInfo(h, out pm, Marshal.SizeOf(typeof(PMC)))) x.Mem = pm.Private.ToInt64();
                        CloseHandle(h);
                    }
                    r[x.Pid] = x;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return r;
        }

        class Uso { public string Nombre; public double Valor; }

        // Los que mas usaron procesador y disco en ~1 s, y los que mas RAM ocupan (sin contar el juego)
        void Culpables(out List<Uso> cpu, out List<Uso> disco, out List<Uso> ram)
        {
            Dictionary<int, Proc> a = Foto();
            DateTime t0 = DateTime.UtcNow;
            Dormir(1000);
            Dictionary<int, Proc> b = Foto();
            double seg = Math.Max(0.2, (DateTime.UtcNow - t0).TotalSeconds);
            int nucleos = Environment.ProcessorCount;
            Dictionary<string, Uso> pc = new Dictionary<string, Uso>(), pd = new Dictionary<string, Uso>(), pr = new Dictionary<string, Uso>();
            foreach (Proc y in b.Values)
            {
                if (y.Pid == pidJuego || y.Pid == 0 || y.Nombre == "Idle") continue;
                Proc x;
                if (a.TryGetValue(y.Pid, out x))
                {
                    double c = (y.Cpu - x.Cpu) / 1e7 / seg / nucleos * 100;
                    double d = (double)(y.Io - x.Io) / seg / 1048576;
                    Sumar(pc, y.Nombre, c); Sumar(pd, y.Nombre, d);
                }
                Sumar(pr, y.Nombre, y.Mem / 1048576.0);
            }
            cpu = Top(pc, 1); disco = Top(pd, 1); ram = Top(pr, 300);
        }

        static void Sumar(Dictionary<string, Uso> d, string n, double v)
        {
            Uso u;
            if (!d.TryGetValue(n, out u)) { u = new Uso(); u.Nombre = n; d[n] = u; }
            u.Valor += Math.Max(0, v);
        }

        static List<Uso> Top(Dictionary<string, Uso> d, double minimo)
        {
            List<Uso> l = new List<Uso>(d.Values);
            l.Sort(delegate (Uso x, Uso y) { return y.Valor.CompareTo(x.Valor); });
            List<Uso> r = new List<Uso>();
            foreach (Uso u in l) { if (u.Valor < minimo || r.Count >= 4) break; r.Add(u); }
            return r;
        }

        // Que suele ser cada programa que aparece de culpable
        static string Pista(string n)
        {
            string x = n.ToLowerInvariant();
            if (x == "msmpeng" || x == "mpdefendercoreservice") return T("el antivirus de Windows está analizando. Agregá la carpeta de tus juegos a las exclusiones de Defender.", "Windows antivirus is scanning. Add your games folder to Defender exclusions.");
            if (Regex.IsMatch(x, "^(tiworker|trustedinstaller|mousocoreworker|wuauclt|usoclient|setuphost)$")) return T("Windows Update está instalando algo. Dejalo terminar o pausá las actualizaciones mientras jugás.", "Windows Update is installing something. Let it finish or pause updates while you play.");
            if (Regex.IsMatch(x, "^(searchindexer|searchprotocolhost|searchfilterhost)$")) return T("la búsqueda de Windows está indexando archivos.", "Windows search is indexing files.");
            if (x == "system") return T("son drivers (Windows los cuenta como System). Actualizá los drivers de la placa madre, red y video.", "these are drivers (Windows counts them as System). Update your motherboard, network and video drivers.");
            if (Regex.IsMatch(x, "^(steam|steamwebhelper|epicgameslauncher|epicwebhelper|battle\\.net|agent|eadesktop|riotclientservices|ubisoftconnect|upc|galaxyclient)$")) return T("un launcher de juegos está descargando o actualizando. Pausá las descargas mientras jugás.", "a game launcher is downloading or updating. Pause downloads while you play.");
            if (Regex.IsMatch(x, "^(chrome|msedge|firefox|opera|opera_gx|brave)$")) return T("el navegador (pestañas o videos abiertos). Cerralo o pausá los videos.", "the browser (open tabs or videos). Close it or pause the videos.");
            if (x == "discord") return T("Discord (si estás transmitiendo o con muchas animaciones).", "Discord (if you are streaming or have lots of animations).");
            if (Regex.IsMatch(x, "^(obs64|obs32|streamlabs|nvcontainer|nvidia share|medal|outplayed)$")) return T("un programa de grabación o clips.", "a recording or clips program.");
            if (Regex.IsMatch(x, "^(onedrive|dropbox|googledrivefs)$")) return T("la nube está sincronizando archivos. Pausala mientras jugás.", "cloud storage is syncing files. Pause it while you play.");
            if (x == "dwm") return T("el escritorio de Windows (dwm). Suele subir con varios monitores, HDR o grabación.", "the Windows desktop (dwm). It usually goes up with several monitors, HDR or recording.");
            return null;
        }

        static string P(double v) { return ((int)Math.Round(v)).ToString() + " %"; }

        // ---------- Vuelta principal ----------
        void Vuelta()
        {
            while (!parar)
            {
                try { Paso(); }
                catch (Exception ex) { Log("Error: " + ex.Message); }
                if (Dormir(pidJuego != 0 ? 1000 : 2000)) break;
            }
            CerrarContadores();
        }

        DateTime fueraDesde;
        readonly Queue<Muestra> ultimas = new Queue<Muestra>();
        double perfMax;
        readonly Dictionary<string, DateTime> avisado = new Dictionary<string, DateTime>();
        bool gpuAvisado;

        void Paso()
        {
            string nom;
            int pid = JuegoAdelante(out nom);
            if (pid != 0 && pid != pidJuego)
            {
                // Empezo un juego (o se cambio de juego)
                CerrarContadores();
                pidJuego = pid; nomJuego = nom;
                ultimas.Clear(); perfMax = 0; avisado.Clear(); gpuAvisado = false;
                AbrirContadores();
                Log(T("Jugando: ", "Playing: ") + nom);
                return;
            }
            if (pidJuego == 0) return;
            if (pid == 0)
            {
                // Si el juego queda atras mas de 60 s (o se cerro), se deja de medir
                if (fueraDesde == DateTime.MinValue) fueraDesde = DateTime.Now;
                bool vivo = false;
                try { using (Process p = Process.GetProcessById(pidJuego)) vivo = !p.HasExited; } catch { }
                if (!vivo || (DateTime.Now - fueraDesde).TotalSeconds > 60)
                {
                    Log(T("Dejó de jugar: ", "Stopped playing: ") + nomJuego);
                    pidJuego = 0; nomJuego = ""; CerrarContadores(); ultimas.Clear();
                }
                return;
            }
            fueraDesde = DateTime.MinValue;

            Muestra m = Medir();
            ultimas.Enqueue(m);
            while (ultimas.Count > 6) ultimas.Dequeue();
            if (m.Perf > perfMax) perfMax = m.Perf;
            if (ultimas.Count < 4) return;

            // Algo tiene que pasar 3 segundos seguidos para contar (un pico suelto no baja los FPS)
            Muestra[] u = ultimas.ToArray();
            int n = u.Length;
            bool cpuAlta = true, ramLlena = true, discoLleno = true, lento = true, gpuTope = true;
            for (int i = n - 3; i < n; i++)
            {
                cpuAlta &= u[i].Cpu >= 95;
                ramLlena &= u[i].Ram >= 92;
                discoLleno &= u[i].DiscoOcupado >= 90;
                lento &= perfMax >= 80 && u[i].Perf >= 0 && u[i].Perf < perfMax * 0.65 && u[i].Cpu >= 30;
                gpuTope &= u[i].Gpu >= 98;
            }
            string tipo = cpuAlta ? "cpu" : ramLlena ? "ram" : discoLleno ? "disco" : lento ? "lento" : null;
            if (tipo == null)
            {
                if (gpuTope && !gpuAvisado) { gpuAvisado = true; AvisoGpu(m); }
                return;
            }
            DateTime t;
            if (avisado.TryGetValue(tipo, out t) && (DateTime.Now - t).TotalMinutes < 3) return;
            avisado[tipo] = DateTime.Now;
            Reporte(tipo, m);
        }

        void AvisoGpu(Muestra m)
        {
            List<string> L = new List<string>();
            L.Add(T("PLACA DE VIDEO AL MÁXIMO: ", "GRAPHICS CARD MAXED OUT: ") + Fecha(DateTime.Now) + "   (" + nomJuego + ")");
            L.Add("");
            L.Add(T("  La placa de video está al 100 %: el límite de FPS lo pone ella. Es normal en juegos exigentes.", "  The graphics card is at 100 %: it sets the FPS limit. It is normal in demanding games."));
            L.Add(T("  Si querés más FPS o que no varíen tanto: bajá la calidad gráfica, la resolución, o limitá los FPS un poco por debajo de lo que te da.", "  If you want more FPS or steadier FPS: lower graphics quality, resolution, or cap FPS a bit below what you get."));
            foreach (string x in L) Log(x);
            Log("");
            Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), 0);
        }

        void Reporte(string tipo, Muestra m)
        {
            List<Uso> cpu, disco, ram;
            Culpables(out cpu, out disco, out ram);
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> L = new List<string>();
            L.Add(T("BAJÓN EN ", "DROP IN ") + nomJuego + ": " + Fecha(DateTime.Now));
            L.Add("");
            L.Add(T("QUÉ PASÓ:", "WHAT HAPPENED:"));
            List<Uso> culp = null;
            string unidad = " %";
            if (tipo == "cpu")
            {
                L.Add(T("  El procesador estuvo al 100 % varios segundos: eso baja los FPS y da tirones.", "  The CPU was at 100 % for several seconds: that lowers FPS and causes stutters."));
                culp = cpu;
            }
            else if (tipo == "ram")
            {
                L.Add(T("  La memoria RAM está casi llena (" + P(m.Ram) + "): Windows empieza a usar el disco y el juego da tirones.", "  RAM is almost full (" + P(m.Ram) + "): Windows starts using the drive and the game stutters."));
                culp = ram; unidad = " MB";
            }
            else if (tipo == "disco")
            {
                L.Add(T("  Un disco estuvo al 100 % varios segundos: el juego espera para cargar cosas y da tirones.", "  A drive was at 100 % for several seconds: the game waits to load things and stutters."));
                culp = disco; unidad = " MB/s";
            }
            else
            {
                L.Add(T("  El procesador bajó mucho su velocidad (" + P(m.Perf) + " de su máximo de " + P(perfMax) + ") mientras jugabas.", "  The CPU lowered its speed a lot (" + P(m.Perf) + " of its " + P(perfMax) + " max) while you were playing."));
                L.Add(T("  Casi siempre es temperatura (se calienta y se frena solo) o energía (notebook a batería, plan de ahorro).", "  It is almost always temperature (it heats up and slows itself) or power (laptop on battery, power saver plan)."));
            }
            List<string> pistas = new List<string>();
            if (culp != null && culp.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Uso x in culp)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(x.Nombre + " (" + (unidad == " %" ? P(x.Valor) : ((int)Math.Round(x.Valor)).ToString() + unidad) + ")");
                    string pi = Pista(x.Nombre);
                    if (pi != null && pistas.Count < 3) pistas.Add(x.Nombre + ": " + pi);
                }
                L.Add(T("  Lo que más lo usó, sin contar el juego: ", "  What used it the most, not counting the game: ") + sb.ToString());
            }
            else if (tipo == "cpu")
                L.Add(T("  Lo usó casi todo el juego: tu procesador es el límite en este juego.", "  The game used almost all of it: your CPU is the limit in this game."));
            L.Add("");
            L.Add(T("QUÉ PROBAR:", "WHAT TO TRY:"));
            foreach (string pi in pistas) L.Add("  - " + pi);
            if (tipo == "cpu") L.Add(T("  - Cerrá lo que no uses mientras jugás. Si el culpable es el juego, bajá las opciones que usan procesador (distancia de visión, multitudes, físicas).", "  - Close what you do not use while gaming. If the culprit is the game itself, lower CPU-heavy settings (view distance, crowds, physics)."));
            if (tipo == "ram") L.Add(T("  - Cerrá el navegador y programas pesados mientras jugás. Si pasa seguido, te conviene más RAM.", "  - Close the browser and heavy programs while gaming. If it happens often, more RAM would help."));
            if (tipo == "disco") L.Add(T("  - Si el juego está en un disco HDD, pasalo a un SSD. Revisá la salud del disco con CrystalDiskInfo.", "  - If the game is on an HDD, move it to an SSD. Check the drive health with CrystalDiskInfo."));
            if (tipo == "lento")
            {
                L.Add(T("  - Mirá la temperatura del procesador con HWiNFO mientras jugás. Si pasa de 90 °C, revisá el cooler, la pasta térmica y el polvo.", "  - Check the CPU temperature with HWiNFO while gaming. If it goes over 90 °C, check the cooler, thermal paste and dust."));
                L.Add(T("  - En notebook: jugá enchufada y con el plan de energía en rendimiento.", "  - On a laptop: play plugged in and on a performance power plan."));
            }
            L.Add("");
            L.Add(T("DATOS:", "DATA:"));
            L.Add(T("  Procesador: ", "  CPU: ") + P(m.Cpu) + (m.Perf >= 0 ? T("   ·   velocidad: ", "   ·   speed: ") + P(m.Perf) : "")
                + "   ·   RAM: " + P(m.Ram)
                + (m.DiscoOcupado >= 0 ? T("   ·   disco más ocupado: ", "   ·   busiest drive: ") + P(m.DiscoOcupado) : "")
                + (m.Gpu >= 0 ? T("   ·   placa de video: ", "   ·   graphics card: ") + P(m.Gpu) : ""));

            foreach (string x in L) Log(x);
            Log("");
            Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), 1);
        }
    }
}
