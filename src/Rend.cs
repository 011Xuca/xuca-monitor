// Monitor de rendimiento: mientras jugas (juego en pantalla completa adelante), vigila procesador, RAM, disco y
// placa de video, y cuando algo se dispara te dice el culpable (programa de fondo, temperatura, RAM llena, disco al 100 %).
// No mide FPS (eso pide permisos de administrador): detecta lo que suele causar los bajones.
// Fuera de los juegos solo mira cada 2 s que ventana esta adelante, y no gasta casi nada.
// Tambien avisa cuando otro programa le saca el foco al juego sin que toques nada (Discord, Steam, un aviso de Windows...):
// Windows le avisa a la app cada vez que cambia la ventana de adelante (SetWinEventHook), asi que no hay que estar mirando.
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
            hiloFoco = new Thread(VueltaFoco);
            hiloFoco.IsBackground = true;
            hiloFoco.Start();
        }

        protected override void Terminar()
        {
            parar = true;
            despertar.Set();
            if (tidFoco != 0) PostThreadMessage(tidFoco, 0x12, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            if (hiloFoco != null) hiloFoco.Join(2000);
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
        volatile int pidJuego;
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

        // ---------- Otro programa le saca el foco al juego ----------
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint hilo, uint ms);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint msg; public IntPtr w, l; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

        Thread hiloFoco;
        volatile uint tidFoco;
        WinEventProc alCambiar; // guardado para que no lo borre el recolector
        readonly object cerrojoFoco = new object();
        int ultimoAdelante;
        class Robo { public DateTime Desde; public string Proc = "", Titulo = ""; }
        Robo robo;
        DateTime roboFin; string roboQuien = "";
        readonly Dictionary<string, DateTime> roboAvisado = new Dictionary<string, DateTime>();

        // Ventanas que abris vos: el Alt+Tab, el menu inicio, la busqueda, el administrador de tareas
        static readonly Regex DeVos = new Regex("^(startmenuexperiencehost|searchhost|searchapp|searchui|shellexperiencehost|taskmgr|xucamonitor)$", RegexOptions.IgnoreCase);
        static readonly Regex ClaseSwitch = new Regex("^(MultitaskingViewFrame|XamlExplorerHostIslandWindow|ForegroundStaging|TaskSwitcherWnd|Shell_TrayWnd|NotifyIconOverflowWindow|TopLevelWindowForOverflowXamlIsland)$");

        void VueltaFoco()
        {
            tidFoco = GetCurrentThreadId();
            alCambiar = AlCambiar;
            IntPtr hook = SetWinEventHook(3, 3, IntPtr.Zero, alCambiar, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, fuera de proceso
            if (hook == IntPtr.Zero) return;
            try { MSG m; while (!parar && GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { } }
            finally { UnhookWinEvent(hook); tidFoco = 0; }
        }

        static bool Apretada(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        void AlCambiar(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint hiloV, uint ms)
        {
            try
            {
                int juego = pidJuego;
                int pid = 0;
                if (hwnd != IntPtr.Zero) GetWindowThreadProcessId(hwnd, out pid);
                int antes = ultimoAdelante;
                ultimoAdelante = pid;
                if (juego == 0 || pid == 0) return;
                if (pid == juego)
                {
                    // Volvio el juego: si se lo habian sacado, se avisa con cuanto duro
                    Robo r;
                    lock (cerrojoFoco) { r = robo; robo = null; }
                    if (r != null) AvisoFoco(r, DateTime.Now - r.Desde, false);
                    return;
                }
                if (antes != juego) return;
                lock (cerrojoFoco) { if (robo != null) return; }
                // Lo hiciste vos: Alt, Windows, Ctrl o un clic en el momento del cambio, o una ventana del sistema que abris vos
                if (Apretada(0x12) || Apretada(0x5B) || Apretada(0x5C) || Apretada(0x11) || Apretada(0x01) || Apretada(0x02)) return;
                StringBuilder cls = new StringBuilder(64);
                GetClassName(hwnd, cls, 64);
                if (ClaseSwitch.IsMatch(cls.ToString())) return;
                string nom = "";
                try { using (Process p = Process.GetProcessById(pid)) nom = p.ProcessName; } catch { }
                if (nom == "" || DeVos.IsMatch(nom)) return;
                StringBuilder tit = new StringBuilder(120);
                GetWindowText(hwnd, tit, 120);
                Robo n = new Robo(); n.Desde = DateTime.Now; n.Proc = nom; n.Titulo = tit.ToString().Trim();
                lock (cerrojoFoco) { robo = n; }
            }
            catch { }
        }

        // Lo llama la vuelta principal: si el juego sigue atras despues de 20 s, se avisa igual
        void RevisarFoco()
        {
            Robo r = null;
            lock (cerrojoFoco)
            {
                if (robo != null && (DateTime.Now - robo.Desde).TotalSeconds > 20) { r = robo; robo = null; }
            }
            if (r != null) AvisoFoco(r, DateTime.Now - r.Desde, true);
        }

        static string PistaFoco(string n)
        {
            string x = n.ToLowerInvariant();
            if (x == "discord" || x == "update") return T("Discord se puso adelante (suele pasar cuando se actualiza o con una llamada). Abrilo y dejalo actualizar antes de jugar.", "Discord came to the front (it usually happens when it updates or on a call). Open it and let it update before you play.");
            if (Regex.IsMatch(x, "^(steam|steamwebhelper)$")) return T("Steam abrió una ventana (amigos, una oferta o una actualización). En Steam > Configuración > Interfaz apagá que se abra al iniciar y las ofertas.", "Steam opened a window (friends, a sale or an update). In Steam > Settings > Interface, turn off opening at startup and sale popups.");
            if (Regex.IsMatch(x, "^(musnotification|musnotifyicon|mousocoreworker|wuauclt|usoclient)$")) return T("Windows Update te mostró un aviso de reinicio. Poné tus horas de juego en Configuración > Windows Update > Horas activas.", "Windows Update showed a restart notice. Set your gaming hours in Settings > Windows Update > Active hours.");
            if (x == "consent") return T("Un pedido de permiso de administrador (UAC): algún programa quiso instalarse o cambiar algo.", "An administrator permission prompt (UAC): some program wanted to install or change something.");
            if (x == "werfault") return T("Un aviso de error de Windows: algún programa se colgó o se cerró (mirá la pestaña Errores).", "A Windows error notice: some program hung or crashed (check the Errors tab).");
            if (Regex.IsMatch(x, "^(gamebar|gamebarftserver|xboxgamebarwidgets|gamebarpresencewriter)$")) return T("La Xbox Game Bar se abrió (Win+G o un widget). Si no la usás, apagala en Configuración > Juegos.", "The Xbox Game Bar opened (Win+G or a widget). If you do not use it, turn it off in Settings > Gaming.");
            if (Regex.IsMatch(x, "^(riotclientux|riotclientservices|epicgameslauncher|battle\\.net|eadesktop|upc|ubisoftconnect|galaxyclient)$")) return T("Un launcher de juegos abrió una ventana (un aviso o una actualización). Desactivá sus notificaciones o cerralo mientras jugás.", "A game launcher opened a window (a notice or an update). Turn off its notifications or close it while you play.");
            if (Regex.IsMatch(x, "^(nvidia overlay|nvidia app|nvcontainer|radeonsoftware|amdrsserv)$")) return T("El programa de la placa de video (overlay de NVIDIA o AMD) se puso adelante. Revisá sus avisos o apagá el overlay si no lo usás.", "The graphics card software (NVIDIA or AMD overlay) came to the front. Check its notices or turn off the overlay if you do not use it.");
            if (Regex.IsMatch(x, "^(chrome|msedge|firefox|opera|opera_gx|brave)$")) return T("El navegador abrió una ventana o pestaña solo (una página o una notificación). Cerralo mientras jugás.", "The browser opened a window or tab by itself (a page or a notification). Close it while you play.");
            if (x == "explorer") return T("El Explorador de Windows abrió una ventana sola (por ejemplo al conectar un USB). Podés apagar la Reproducción automática en Configuración > Bluetooth y dispositivos.", "Windows Explorer opened a window by itself (for example when you plug in a USB). You can turn off AutoPlay in Settings > Bluetooth & devices.");
            return null;
        }

        void AvisoFoco(Robo r, TimeSpan dur, bool sigueAtras)
        {
            string clave = r.Proc.ToLowerInvariant();
            DateTime t;
            lock (cerrojoFoco)
            {
                roboFin = DateTime.Now; roboQuien = r.Proc;
                if (roboAvisado.TryGetValue(clave, out t) && (DateTime.Now - t).TotalMinutes < 2) return;
                roboAvisado[clave] = DateTime.Now;
            }
            string cuanto = dur.TotalSeconds < 1 ? T("menos de 1 s", "less than 1 s") : ((int)Math.Round(dur.TotalSeconds)).ToString() + " s";
            string quien = r.Proc + ".exe" + (r.Titulo != "" ? " (\"" + r.Titulo + "\")" : "");
            List<string> L = new List<string>();
            L.Add(T("EL JUEGO PERDIÓ EL FOCO: ", "THE GAME LOST FOCUS: ") + Fecha(r.Desde) + "   (" + nomJuego + ")");
            L.Add("");
            L.Add(T("QUÉ PASÓ:", "WHAT HAPPENED:"));
            L.Add(T("  " + quien + " se puso adelante sin que toques nada, y el juego quedó atrás " + (sigueAtras ? "más de " + cuanto + " (sigue atrás)." : cuanto + "."),
                    "  " + quien + " came to the front without you touching anything, and the game was in the background " + (sigueAtras ? "for more than " + cuanto + " (still is)." : "for " + cuanto + ".")));
            L.Add(T("  Eso da un tirón, te saca del juego o lo minimiza (en pantalla completa exclusiva).", "  That causes a stutter, kicks you out of the game or minimizes it (in exclusive fullscreen)."));
            L.Add("");
            L.Add(T("QUÉ PROBAR:", "WHAT TO TRY:"));
            string pi = PistaFoco(r.Proc);
            L.Add("  - " + (pi ?? T("Si se repite, fijate si ese programa tiene avisos o ventanas que se abren solas y apagalos, o cerralo mientras jugás.", "If it happens again, check whether that program has popups or notices and turn them off, or close it while you play.")));
            L.Add(T("  - Jugar en \"Pantalla completa sin bordes\" hace que estos cambios no minimicen el juego.", "  - Playing in \"Borderless fullscreen\" stops these changes from minimizing the game."));
            foreach (string x in L) Log(x);
            Log("");
            Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), 1);
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
                RevisarFoco();
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
            RevisarFoco();

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
            // Si justo antes otro programa le saco el foco al juego, seguro tuvo que ver
            string rq = ""; DateTime rf;
            lock (cerrojoFoco) { rq = roboQuien; rf = roboFin; }
            if (rq != "" && (DateTime.Now - rf).TotalSeconds < 15)
                L.Add(T("  Justo antes, " + rq + ".exe le sacó el foco al juego: eso también da tirones.", "  Right before, " + rq + ".exe took focus away from the game: that also causes stutters."));
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
