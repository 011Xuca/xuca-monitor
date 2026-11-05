// Overlay de rendimiento personalizable (como el OSD de MSI Afterburner + RivaTuner, pero sin inyectar nada en el juego).
//  - Los FPS se miden leyendo los eventos de Windows (ETW, DXGI y D3D9), igual que PresentMon: el juego ni se entera,
//    asi que anda con Vanguard (Valorant) y EasyAntiCheat (Fortnite).
//  - El texto se dibuja en una ventana transparente (con transparencia por pixel) que no recibe clics, y se puede capturar
//    con OBS como "Captura de ventana". Se ve con el juego en pantalla completa sin bordes o en ventana.
//  - Lo que se muestra lo decide una PLANTILLA de texto que edita el usuario: lineas con {variables}, colores, tamanos...
//  - Sin juego configurado abierto no hay sesion ETW ni sensores: solo un timer de 1 s que mira la lista de procesos.
// C# 5 (compila con el csc de Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Xuca
{
    // ---------- Variables que se pueden usar en la plantilla ----------
    internal sealed class OvVar
    {
        public string Key, Es, En, Fmt;
        public OvVar(string k, string es, string en, string fmt) { Key = k; Es = es; En = en; Fmt = fmt; }
        public override string ToString() { return Key + "  —  " + (Modulo.Idioma == "en" ? En : Es); }
    }

    internal static class OvCat
    {
        public static readonly OvVar[] Vars = new OvVar[]
        {
            new OvVar("fps", "FPS actuales", "Current FPS", "0"),
            new OvVar("fps.avg", "FPS promedio de la sesión", "Session average FPS", "0"),
            new OvVar("fps.min", "FPS mínimo de la sesión", "Session minimum FPS", "0"),
            new OvVar("fps.max", "FPS máximo de la sesión", "Session maximum FPS", "0"),
            new OvVar("low1", "1% low (los peores cuadros, en FPS)", "1% low (worst frames, in FPS)", "0"),
            new OvVar("ft", "Frametime (ms)", "Frametime (ms)", "0.0"),
            new OvVar("proc", "Nombre del proceso del juego", "Game process name", ""),
            new OvVar("time", "Hora actual", "Current time", ""),
            new OvVar("cpu.name", "Nombre del procesador", "CPU name", ""),
            new OvVar("cpu.load", "CPU: uso (%)", "CPU: usage (%)", "0"),
            new OvVar("cpu.temp", "CPU: temperatura (°C)", "CPU: temperature (°C)", "0"),
            new OvVar("cpu.power", "CPU: consumo (W)", "CPU: power (W)", "0"),
            new OvVar("cpu.clock", "CPU: velocidad (MHz)", "CPU: clock (MHz)", "0"),
            new OvVar("cpu.volt", "CPU: voltaje (V)", "CPU: voltage (V)", "0.00"),
            new OvVar("gpu.name", "Nombre de la placa de video", "Graphics card name", ""),
            new OvVar("gpu.load", "GPU: uso (%)", "GPU: usage (%)", "0"),
            new OvVar("gpu.temp", "GPU: temperatura del núcleo (°C)", "GPU: core temperature (°C)", "0"),
            new OvVar("gpu.hot", "GPU: temperatura hot spot (°C)", "GPU: hot spot temperature (°C)", "0"),
            new OvVar("gpu.memtemp", "GPU: temperatura de la memoria (°C)", "GPU: memory temperature (°C)", "0"),
            new OvVar("gpu.power", "GPU: consumo (W)", "GPU: power (W)", "0"),
            new OvVar("gpu.clock", "GPU: velocidad del núcleo (MHz)", "GPU: core clock (MHz)", "0"),
            new OvVar("gpu.memclock", "GPU: velocidad de la memoria (MHz)", "GPU: memory clock (MHz)", "0"),
            new OvVar("gpu.volt", "GPU: voltaje (V)", "GPU: voltage (V)", "0.00"),
            new OvVar("gpu.vram", "GPU: VRAM usada (MB)", "GPU: VRAM used (MB)", "0"),
            new OvVar("gpu.vramtotal", "GPU: VRAM total (MB)", "GPU: VRAM total (MB)", "0"),
            new OvVar("gpu.vrampct", "GPU: VRAM usada (%)", "GPU: VRAM used (%)", "0"),
            new OvVar("gpu.fan", "GPU: ventilador (RPM)", "GPU: fan (RPM)", "0"),
            new OvVar("gpu.fanpct", "GPU: ventilador (%)", "GPU: fan (%)", "0"),
            new OvVar("ram.load", "RAM: uso (%)", "RAM: usage (%)", "0"),
            new OvVar("ram.used", "RAM: usada (GB)", "RAM: used (GB)", "0.0"),
            new OvVar("ram.total", "RAM: total (GB)", "RAM: total (GB)", "0.0"),
            new OvVar("graph", "Gráfico de frametime (ej. {graph:260x40})", "Frametime graph (e.g. {graph:260x40})", "")
        };

        public static string FmtDe(string key)
        {
            foreach (OvVar v in Vars) if (v.Key == key) return v.Fmt;
            return "0";
        }

        public const string Defecto =
            "<c=FFC83D>{gpu.name}</c>\n" +
            "<b><s=150>{fps!<60,30}</s></b> FPS   1% low {low1!<60,30}\n" +
            "Frametime {ft:0.0} ms\n" +
            "{graph:240x40}\n" +
            "GPU  {gpu.load!90,98}%  {gpu.temp!75,85}°C  {gpu.clock} MHz  {gpu.power} W\n" +
            "VRAM {gpu.vram} / {gpu.vramtotal} MB\n" +
            "CPU  {cpu.load!90,98}%  {cpu.temp!80,90}°C  {cpu.clock} MHz\n" +
            "RAM  {ram.used} / {ram.total} GB";

        public static readonly string[] PresetNombresEs = { "Completo", "Estilo Afterburner", "Solo FPS", "Mínimo" };
        public static readonly string[] PresetNombresEn = { "Full", "Afterburner style", "FPS only", "Minimal" };
        public static readonly string[] Presets = new string[]
        {
            Defecto,
            "<c=FFC83D>{gpu.name}</c>\nGPU usage    {gpu.load}%\nGPU temp     {gpu.temp}°C\nGPU clock    {gpu.clock} MHz\nMemory usage {gpu.vram} MB\nCPU usage    {cpu.load}%\nCPU temp     {cpu.temp}°C\nRAM usage    {ram.used:0.0} GB\nFramerate    {fps} FPS\nFrametime    {ft:0.0} ms\n{graph:260x40}",
            "<b><s=180>{fps!<60,30}</s></b> FPS",
            "FPS {fps}   1% {low1}\nGPU {gpu.load}%  {gpu.temp}°C"
        };
    }

    // ---------- Ajustes (Documentos\Xuca Monitor\overlay.txt, formato clave=valor) ----------
    internal sealed class OvCfg
    {
        public bool Activo = true;
        public bool SoloFrente = true;      // false: se queda visible mientras el juego este abierto (para capturarlo con OBS)
        public List<string> Procs = new List<string>();
        public string Fuente = "Consolas";
        public float Tam = 11;
        public bool Negrita = false;
        public int ColorTexto = Color.FromArgb(255, 255, 255).ToArgb();
        public int ColorFondo = Color.FromArgb(0, 0, 0).ToArgb();
        public int OpTexto = 100;           // 10..100
        public int OpFondo = 60;            // 0..100
        public string Esquina = "tl";       // tl, tr, bl, br
        public int X = 12, Y = 12;
        public string Tecla = "F10";        // "" = sin atajo
        public string Plantilla = OvCat.Defecto;

        public static OvCfg Cargar(string arch)
        {
            OvCfg c = new OvCfg();
            try { if (File.Exists(arch)) c.Leer(File.ReadAllLines(arch), true); } catch { }
            return c;
        }

        // conProcs=false: solo el estilo (al importar un estilo no se pisa la lista de juegos)
        public void Leer(string[] lineas, bool conProcs)
        {
            foreach (string l in lineas)
            {
                int i = l.IndexOf('=');
                if (i < 1) continue;
                string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1);
                int n; float f;
                switch (k)
                {
                    case "activo": Activo = v.Trim() != "0"; break;
                    case "solofrente": SoloFrente = v.Trim() != "0"; break;
                    case "procs": if (conProcs) { Procs.Clear(); foreach (string p in v.Split('|')) if (p.Trim().Length > 0) Procs.Add(p.Trim()); } break;
                    case "fuente": if (v.Trim().Length > 0) Fuente = v.Trim(); break;
                    case "tam": if (float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) && f >= 6 && f <= 72) Tam = f; break;
                    case "negrita": Negrita = v.Trim() != "0"; break;
                    case "colortexto": if (int.TryParse(v.Trim(), out n)) ColorTexto = n; break;
                    case "colorfondo": if (int.TryParse(v.Trim(), out n)) ColorFondo = n; break;
                    case "optexto": if (int.TryParse(v.Trim(), out n)) OpTexto = Math.Max(10, Math.Min(100, n)); break;
                    case "opfondo": if (int.TryParse(v.Trim(), out n)) OpFondo = Math.Max(0, Math.Min(100, n)); break;
                    case "esquina": v = v.Trim(); if (v == "tl" || v == "tr" || v == "bl" || v == "br") Esquina = v; break;
                    case "x": if (int.TryParse(v.Trim(), out n)) X = Math.Max(0, Math.Min(4000, n)); break;
                    case "y": if (int.TryParse(v.Trim(), out n)) Y = Math.Max(0, Math.Min(4000, n)); break;
                    case "tecla": Tecla = v.Trim(); break;
                    case "plantilla": Plantilla = v.Replace('↵', '\n'); break;
                }
            }
        }

        public string[] Texto(bool conProcs)
        {
            List<string> l = new List<string>();
            l.Add("activo=" + (Activo ? "1" : "0"));
            l.Add("solofrente=" + (SoloFrente ? "1" : "0"));
            if (conProcs) l.Add("procs=" + string.Join("|", Procs.ToArray()));
            l.Add("fuente=" + Fuente);
            l.Add("tam=" + Tam.ToString(CultureInfo.InvariantCulture));
            l.Add("negrita=" + (Negrita ? "1" : "0"));
            l.Add("colortexto=" + ColorTexto);
            l.Add("colorfondo=" + ColorFondo);
            l.Add("optexto=" + OpTexto);
            l.Add("opfondo=" + OpFondo);
            l.Add("esquina=" + Esquina);
            l.Add("x=" + X);
            l.Add("y=" + Y);
            l.Add("tecla=" + Tecla);
            l.Add("plantilla=" + Plantilla.Replace("\r\n", "\n").Replace('\n', '↵'));
            return l.ToArray();
        }

        public OvCfg Copia()
        {
            OvCfg c = new OvCfg();
            c.Leer(Texto(true), true);
            return c;
        }
    }

    // Valores que llegan a la plantilla: numeros (se les da formato) y textos
    internal sealed class OvDatos
    {
        public readonly Dictionary<string, double> N = new Dictionary<string, double>();
        public readonly Dictionary<string, string> S = new Dictionary<string, string>();
        public float[] Graf = new float[0];
        public bool HayFps;
        public void Limpiar() { N.Clear(); S.Clear(); Graf = new float[0]; HayFps = false; }
    }

    // ---------- FPS: sesion ETW en tiempo real (Present de DXGI y D3D9) ----------
    internal sealed unsafe class OvEtw
    {
        const string NOMBRE = "XucaMonitorFps";
        static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        static readonly Guid D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");

        [StructLayout(LayoutKind.Sequential)]
        struct HDR { public ushort Size, Flags; public uint Version, ThreadId, ProcessId; public long Ts; public Guid G; public uint K, U; }
        [StructLayout(LayoutKind.Sequential)]
        struct ETR { public HDR H; public uint Inst, ParentInst; public Guid PG; public IntPtr Mof; public uint MofLen, Ctx; }
        [StructLayout(LayoutKind.Sequential)]
        unsafe struct LH
        {
            public uint BufSize, Ver, ProvVer, NProc; public long End; public uint TimerRes, MaxFile, Mode, BufWritten;
            public Guid Inst; public IntPtr LoggerName, LogFileName; public fixed byte TZ[172];
            public long Boot, Freq, Start; public uint Reserved, BufLost;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct LOGFILE
        {
            public IntPtr FileName, LoggerName; public long CurTime; public uint BufRead, Mode; public ETR Cur; public LH Hdr;
            public IntPtr BufCb; public uint BufSize, Filled, Lost; public IntPtr RecCb; public uint Kernel; public IntPtr Ctx;
        }
        delegate void RecCb(IntPtr rec);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint StartTraceW(out ulong h, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint ControlTraceW(ulong h, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern uint EnableTraceEx2(ulong h, ref Guid g, uint ctl, byte level, ulong any, ulong all, uint timeout, IntPtr p);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern ulong OpenTraceW(ref LOGFILE lf);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ref ulong h, uint n, IntPtr s, IntPtr e);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong h);

        readonly object lk = new object();
        readonly HashSet<uint> pids = new HashSet<uint>();
        readonly Dictionary<uint, List<double>> frames = new Dictionary<uint, List<double>>();
        ulong sesion, traza;
        Thread hilo;
        RecCb cb;
        IntPtr nombrePtr;
        readonly double aMs = 1000.0 / Stopwatch.Frequency;   // Stopwatch usa el mismo reloj (QPC) que las marcas de tiempo crudas

        public bool Andando;

        public static bool Posible { get { return Environment.Is64BitProcess; } }

        // Los desplazamientos de EVENT_TRACE_PROPERTIES de abajo son los de 64 bits
        static IntPtr Props()
        {
            int total = 120 + (NOMBRE.Length + 1) * 2;
            IntPtr p = Marshal.AllocHGlobal(total + 8);
            for (int i = 0; i < total + 8; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, total);            // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);               // Wnode.ClientContext = 1 (QPC)
            Marshal.WriteInt32(p, 44, 0x20000);         // Wnode.Flags = WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);              // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 2);               // MinimumBuffers
            Marshal.WriteInt32(p, 56, 16);              // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x100);           // LogFileMode = EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);               // FlushTimer = 1 s
            Marshal.WriteInt32(p, 116, 120);            // LoggerNameOffset
            for (int i = 0; i < NOMBRE.Length; i++) Marshal.WriteInt16(p, 120 + i * 2, NOMBRE[i]);
            return p;
        }

        public bool Iniciar()
        {
            if (Andando) return true;
            if (!Posible) return false;
            IntPtr p = Props();
            try
            {
                uint r = StartTraceW(out sesion, NOMBRE, p);
                if (r == 183)   // ya existe: quedo de un cierre anterior
                {
                    ControlTraceW(0, NOMBRE, p, 1);
                    Marshal.FreeHGlobal(p); p = Props();
                    r = StartTraceW(out sesion, NOMBRE, p);
                }
                if (r != 0) { sesion = 0; return false; }
            }
            finally { Marshal.FreeHGlobal(p); }

            Guid g1 = Dxgi, g2 = D3d9;
            EnableTraceEx2(sesion, ref g1, 1, 4, 0, 0, 0, IntPtr.Zero);
            EnableTraceEx2(sesion, ref g2, 1, 4, 0, 0, 0, IntPtr.Zero);

            cb = new RecCb(Evento);
            nombrePtr = Marshal.StringToHGlobalUni(NOMBRE);
            LOGFILE lf = new LOGFILE();
            lf.LoggerName = nombrePtr;
            lf.Mode = 0x100 | 0x10000000 | 0x01000000;   // tiempo real | EVENT_RECORD | marcas de tiempo crudas (QPC)
            lf.RecCb = Marshal.GetFunctionPointerForDelegate(cb);
            traza = OpenTraceW(ref lf);
            if (traza == ulong.MaxValue) { traza = 0; Parar(); return false; }
            Andando = true;
            hilo = new Thread(delegate () { try { ulong t = traza; ProcessTrace(ref t, 1, IntPtr.Zero, IntPtr.Zero); } catch { } });
            hilo.IsBackground = true;
            hilo.Start();
            return true;
        }

        public void Parar()
        {
            Andando = false;
            if (sesion != 0)
            {
                IntPtr p = Props();
                try { ControlTraceW(sesion, NOMBRE, p, 1); } catch { }
                Marshal.FreeHGlobal(p);
                sesion = 0;
            }
            if (traza != 0) { try { CloseTrace(traza); } catch { } }
            traza = 0;
            if (hilo != null) { hilo.Join(1500); hilo = null; }
            if (nombrePtr != IntPtr.Zero) { Marshal.FreeHGlobal(nombrePtr); nombrePtr = IntPtr.Zero; }
            lock (lk) { frames.Clear(); }
        }

        public void Procesos(HashSet<uint> nuevos)
        {
            lock (lk)
            {
                pids.Clear();
                foreach (uint x in nuevos) pids.Add(x);
                List<uint> viejos = new List<uint>();
                foreach (uint k in frames.Keys) if (!pids.Contains(k)) viejos.Add(k);
                foreach (uint k in viejos) frames.Remove(k);
            }
        }

        // Se llama desde el hilo de ETW por cada evento: lo mas barato posible
        void Evento(IntPtr rec)
        {
            try
            {
                byte* p = (byte*)rec;
                ushort id = *(ushort*)(p + 40);
                if (id != 42 && id != 1) return;
                Guid g = *(Guid*)(p + 24);
                if (!((id == 42 && g == Dxgi) || (id == 1 && g == D3d9))) return;
                uint pid = *(uint*)(p + 12);
                double ms = (*(long*)(p + 16)) * aMs;
                lock (lk)
                {
                    if (!pids.Contains(pid)) return;
                    List<double> l;
                    if (!frames.TryGetValue(pid, out l)) { l = new List<double>(); frames[pid] = l; }
                    l.Add(ms);
                    if (l[0] < ms - 6000)
                    {
                        int k = 0;
                        while (k < l.Count && l[k] < ms - 6000) k++;
                        l.RemoveRange(0, k);
                    }
                }
            }
            catch { }
        }

        // Llena fps, low1 y ft (y el grafico) en d. Si todavia no llego ningun cuadro, no pone nada.
        public void Medir(OvDatos d)
        {
            double ahora = Stopwatch.GetTimestamp() * aMs;
            double[] t = null;
            lock (lk)
            {
                double mejor = -1;
                foreach (List<double> l in frames.Values)
                    if (l.Count > 2 && l[l.Count - 1] > mejor) { mejor = l[l.Count - 1]; t = l.ToArray(); }
            }
            if (t == null) return;
            d.HayFps = true;
            if (ahora - t[t.Length - 1] > 2000) { d.N["fps"] = 0; d.N["ft"] = 0; d.N["low1"] = 0; return; }   // el juego esta en pausa o minimizado
            int n1 = 0;
            for (int i = t.Length - 1; i >= 0 && t[i] > ahora - 1000; i--) n1++;
            d.N["fps"] = n1;
            d.N["ft"] = t[t.Length - 1] - t[t.Length - 2];
            List<double> dt = new List<double>();
            for (int i = t.Length - 1; i > 0 && t[i] > ahora - 5000; i--) dt.Add(t[i] - t[i - 1]);
            if (dt.Count > 0)
            {
                dt.Sort();
                dt.Reverse();
                int k = Math.Max(1, dt.Count / 100);
                double s = 0;
                for (int i = 0; i < k; i++) s += dt[i];
                d.N["low1"] = s > 0 ? 1000.0 / (s / k) : 0;
            }
            int m = Math.Min(120, t.Length - 1);
            float[] g = new float[m];
            for (int i = 0; i < m; i++) g[i] = (float)(t[t.Length - m + i] - t[t.Length - m + i - 1]);
            d.Graf = g;
        }
    }

    // ---------- La plantilla: se convierte en lineas de "pedazos" con su color, tamano y negrita ----------
    internal sealed class OvRun
    {
        public string T = "";
        public Color C;
        public int Esc = 100;
        public bool B;
        public bool Graf;
        public int GW, GH;      // GW = 0: ocupa todo el ancho
    }

    internal static class OvPlantilla
    {
        static readonly Color Naranja = Color.FromArgb(255, 176, 0), Rojo = Color.FromArgb(255, 64, 64);

        public static List<List<OvRun>> Armar(string plantilla, OvDatos d, Color baseColor, bool baseNegrita)
        {
            List<List<OvRun>> res = new List<List<OvRun>>();
            string[] lineas = plantilla.Replace("\r\n", "\n").Split('\n');
            List<Color> pc = new List<Color>(); List<int> pe = new List<int>(); int neg = 0;
            foreach (string linea in lineas)
            {
                List<OvRun> runs = new List<OvRun>();
                System.Text.StringBuilder buf = new System.Text.StringBuilder();
                int i = 0;
                while (i < linea.Length)
                {
                    char ch = linea[i];
                    if (ch == '{')
                    {
                        int j = linea.IndexOf('}', i + 1);
                        if (j > i)
                        {
                            Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita);
                            Variable(linea.Substring(i + 1, j - i - 1), d, runs, pc, pe, neg, baseColor, baseNegrita);
                            i = j + 1;
                            continue;
                        }
                    }
                    else if (ch == '<')
                    {
                        int j = linea.IndexOf('>', i + 1);
                        if (j > i)
                        {
                            string tag = linea.Substring(i + 1, j - i - 1).ToLowerInvariant();
                            bool ok = true;
                            if (tag.StartsWith("c=") && tag.Length >= 8)
                            {
                                Color col;
                                if (Hex(tag.Substring(2), out col)) { Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita); pc.Add(col); }
                                else ok = false;
                            }
                            else if (tag == "/c") { Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita); if (pc.Count > 0) pc.RemoveAt(pc.Count - 1); }
                            else if (tag.StartsWith("s="))
                            {
                                int s;
                                if (int.TryParse(tag.Substring(2), out s) && s >= 30 && s <= 500) { Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita); pe.Add(s); }
                                else ok = false;
                            }
                            else if (tag == "/s") { Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita); if (pe.Count > 0) pe.RemoveAt(pe.Count - 1); }
                            else if (tag == "b") { Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita); neg++; }
                            else if (tag == "/b") { Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita); if (neg > 0) neg--; }
                            else ok = false;
                            if (ok) { i = j + 1; continue; }
                        }
                    }
                    buf.Append(ch);
                    i++;
                }
                Vaciar(runs, buf, pc, pe, neg, baseColor, baseNegrita);
                res.Add(runs);
            }
            return res;
        }

        static void Vaciar(List<OvRun> runs, System.Text.StringBuilder buf, List<Color> pc, List<int> pe, int neg, Color baseColor, bool baseNeg)
        {
            if (buf.Length == 0) return;
            runs.Add(Nuevo(buf.ToString(), pc, pe, neg, baseColor, baseNeg));
            buf.Length = 0;
        }

        static OvRun Nuevo(string t, List<Color> pc, List<int> pe, int neg, Color baseColor, bool baseNeg)
        {
            OvRun r = new OvRun();
            r.T = t;
            r.C = pc.Count > 0 ? pc[pc.Count - 1] : baseColor;
            r.Esc = pe.Count > 0 ? pe[pe.Count - 1] : 100;
            r.B = baseNeg || neg > 0;
            return r;
        }

        static bool Hex(string h, out Color c)
        {
            c = Color.White;
            try
            {
                if (h.Length != 6) return false;
                c = Color.FromArgb(255, Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
                return true;
            }
            catch { return false; }
        }

        // {clave}  {clave:formato}  {clave!aviso,critico}  {clave!<aviso,critico} (al reves, para FPS)  {graph:ANCHOxALTO}
        static void Variable(string spec, OvDatos d, List<OvRun> runs, List<Color> pc, List<int> pe, int neg, Color baseColor, bool baseNeg)
        {
            string thr = null;
            int bang = spec.IndexOf('!');
            if (bang >= 0) { thr = spec.Substring(bang + 1); spec = spec.Substring(0, bang); }
            string fmt = null;
            int dp = spec.IndexOf(':');
            if (dp >= 0) { fmt = spec.Substring(dp + 1); spec = spec.Substring(0, dp); }
            string key = spec.Trim().ToLowerInvariant();

            OvRun r = Nuevo("", pc, pe, neg, baseColor, baseNeg);
            if (key == "graph")
            {
                r.Graf = true;
                if (fmt != null)
                {
                    string[] wh = fmt.ToLowerInvariant().Split('x');
                    int w, h;
                    if (wh.Length == 2 && int.TryParse(wh[0], out w) && int.TryParse(wh[1], out h) && w >= 40 && w <= 1500 && h >= 10 && h <= 400) { r.GW = w; r.GH = h; }
                }
                if (r.GH == 0) r.GH = 40;
                runs.Add(r);
                return;
            }
            string txt;
            string s;
            double v;
            if (d.S.TryGetValue(key, out s)) txt = s;
            else if (d.N.TryGetValue(key, out v))
            {
                string f = string.IsNullOrEmpty(fmt) ? OvCat.FmtDe(key) : fmt;
                try { txt = v.ToString(f, CultureInfo.InvariantCulture); } catch { txt = v.ToString("0", CultureInfo.InvariantCulture); }
                if (thr != null) Alerta(thr, v, r);
            }
            else txt = "--";
            r.T = txt;
            runs.Add(r);
        }

        static void Alerta(string thr, double v, OvRun r)
        {
            bool alReves = thr.StartsWith("<");
            if (alReves) thr = thr.Substring(1);
            string[] p = thr.Split(',');
            double a, b;
            if (p.Length != 2 || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a) || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return;
            if (alReves) { if (v <= b) r.C = Rojo; else if (v <= a) r.C = Naranja; }
            else { if (v >= b) r.C = Rojo; else if (v >= a) r.C = Naranja; }
        }
    }

    // ---------- La ventana transparente que se dibuja encima del juego (transparencia por pixel) ----------
    internal sealed class OvForm : Form
    {
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref PT dst, ref SZ size, IntPtr hdcSrc, ref PT src, int key, ref BLEND bf, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential)] struct BLEND { public byte Op, Flags, Alpha, Format; }

        public OvForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Xuca Overlay";   // titulo fijo: asi OBS lo lista en "Captura de ventana"
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // NOACTIVATE (no roba el foco) | LAYERED | TRANSPARENT (los clics pasan al juego).
                // Sin TOOLWINDOW a proposito: OBS ignora las ventanas de herramientas en "Captura de ventana".
                cp.ExStyle |= 0x08000000 | 0x80000 | 0x20;
                return cp;
            }
        }

        public void Poner(OvCfg cfg, OvDatos d, Rectangle pantalla)
        {
            Color baseCol = Color.FromArgb(cfg.ColorTexto);
            List<List<OvRun>> lineas = OvPlantilla.Armar(cfg.Plantilla, d, Color.FromArgb(255, baseCol), cfg.Negrita);

            Dictionary<string, Font> fuentes = new Dictionary<string, Font>();
            using (Bitmap medir = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(medir))
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                try
                {
                    sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    // Medidas de cada linea
                    int n = lineas.Count;
                    float[] ancho = new float[n], alto = new float[n];
                    float[][] aRun = new float[n][], hRun = new float[n][];
                    float maxAncho = 0;
                    for (int li = 0; li < n; li++)
                    {
                        List<OvRun> rs = lineas[li];
                        aRun[li] = new float[rs.Count]; hRun[li] = new float[rs.Count];
                        float baseH = Fuente(fuentes, cfg, 100, cfg.Negrita).GetHeight(g);
                        float w = 0, h = rs.Count == 0 ? baseH : 0;
                        for (int ri = 0; ri < rs.Count; ri++)
                        {
                            OvRun r = rs[ri];
                            if (r.Graf) { aRun[li][ri] = r.GW; hRun[li][ri] = r.GH; }
                            else
                            {
                                Font f = Fuente(fuentes, cfg, r.Esc, r.B);
                                aRun[li][ri] = g.MeasureString(r.T, f, 10000, sf).Width;
                                hRun[li][ri] = f.GetHeight(g);
                            }
                            w += aRun[li][ri]; h = Math.Max(h, hRun[li][ri]);
                        }
                        ancho[li] = w; alto[li] = h;
                        maxAncho = Math.Max(maxAncho, w);
                    }
                    // Los graficos sin ancho ocupan todo el ancho (minimo 160)
                    float anchoTotal = Math.Max(maxAncho, 1);
                    for (int li = 0; li < n; li++)
                        for (int ri = 0; ri < lineas[li].Count; ri++)
                            if (lineas[li][ri].Graf && lineas[li][ri].GW == 0)
                            {
                                float otros = ancho[li];
                                aRun[li][ri] = Math.Max(160, anchoTotal - otros);
                                ancho[li] = otros + aRun[li][ri];
                                anchoTotal = Math.Max(anchoTotal, ancho[li]);
                            }

                    int pad = 6;
                    float total = 0;
                    foreach (float h in alto) total += h;
                    int W = (int)Math.Ceiling(anchoTotal) + pad * 2, H = (int)Math.Ceiling(total) + pad * 2;
                    if (W < 2 || H < 2 || (n == 1 && lineas[0].Count == 0)) { if (Visible) Hide(); return; }

                    // Dibujo sobre un bitmap con alpha: el fondo y el texto tienen opacidad propia
                    using (Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppPArgb))
                    using (Graphics gd = Graphics.FromImage(bmp))
                    {
                        gd.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        gd.SmoothingMode = SmoothingMode.AntiAlias;
                        Color fondo = Color.FromArgb(cfg.ColorFondo);
                        gd.Clear(Color.FromArgb(cfg.OpFondo * 255 / 100, fondo.R, fondo.G, fondo.B));
                        int aT = cfg.OpTexto * 255 / 100;
                        float y = pad;
                        for (int li = 0; li < n; li++)
                        {
                            float x = pad;
                            for (int ri = 0; ri < lineas[li].Count; ri++)
                            {
                                OvRun r = lineas[li][ri];
                                if (r.Graf) DibujarGrafico(gd, d.Graf, r.C, aT, x, y + (alto[li] - hRun[li][ri]), aRun[li][ri], hRun[li][ri]);
                                else
                                {
                                    using (SolidBrush br = new SolidBrush(Color.FromArgb(aT, r.C)))
                                        gd.DrawString(r.T, Fuente(fuentes, cfg, r.Esc, r.B), br, x, y + (alto[li] - hRun[li][ri]), sf);
                                }
                                x += aRun[li][ri];
                            }
                            y += alto[li];
                        }

                        int px = cfg.Esquina.EndsWith("r") ? pantalla.Right - W - cfg.X : pantalla.Left + cfg.X;
                        int py = cfg.Esquina.StartsWith("b") ? pantalla.Bottom - H - cfg.Y : pantalla.Top + cfg.Y;
                        if (!Visible) Show();
                        Aplicar(bmp, px, py);
                    }
                }
                finally { foreach (Font f in fuentes.Values) f.Dispose(); }
            }
        }

        static Font Fuente(Dictionary<string, Font> cache, OvCfg cfg, int esc, bool negrita)
        {
            string k = esc + (negrita ? "b" : "n");
            Font f;
            if (cache.TryGetValue(k, out f)) return f;
            float tam = Math.Max(4f, cfg.Tam * esc / 100f);
            try { f = new Font(cfg.Fuente, tam, negrita ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
            catch { f = new Font("Consolas", tam, negrita ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
            cache[k] = f;
            return f;
        }

        static void DibujarGrafico(Graphics g, float[] graf, Color col, int alfa, float x, float y, float w, float h)
        {
            using (Pen marco = new Pen(Color.FromArgb(alfa / 3, col), 1f))
                g.DrawRectangle(marco, x, y, w - 1, h - 1);
            if (graf.Length < 2) return;
            float max = 33.3f;
            foreach (float v in graf) if (v > max) max = Math.Min(v, 100f);
            PointF[] pts = new PointF[graf.Length];
            for (int i = 0; i < graf.Length; i++)
                pts[i] = new PointF(x + (w - 1) * i / (float)(graf.Length - 1), y + (h - 1) - Math.Min(graf[i], max) / max * (h - 1));
            using (Pen p = new Pen(Color.FromArgb(alfa, col), 1f)) g.DrawLines(p, pts);
        }

        void Aplicar(Bitmap bmp, int x, int y)
        {
            IntPtr dcPantalla = GetDC(IntPtr.Zero);
            IntPtr dcMem = CreateCompatibleDC(dcPantalla);
            IntPtr hbm = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr viejo = SelectObject(dcMem, hbm);
            try
            {
                PT dst = new PT(); dst.X = x; dst.Y = y;
                SZ sz = new SZ(); sz.W = bmp.Width; sz.H = bmp.Height;
                PT src = new PT();
                BLEND bf = new BLEND(); bf.Op = 0; bf.Flags = 0; bf.Alpha = 255; bf.Format = 1;   // AC_SRC_OVER, AC_SRC_ALPHA
                UpdateLayeredWindow(Handle, dcPantalla, ref dst, ref sz, dcMem, ref src, 0, ref bf, 2);
            }
            finally
            {
                SelectObject(dcMem, viejo);
                DeleteObject(hbm);
                DeleteDC(dcMem);
                ReleaseDC(IntPtr.Zero, dcPantalla);
            }
        }
    }

    // ---------- Atajo de teclado global (recibe WM_HOTKEY) ----------
    internal sealed class OvTecla : NativeWindow
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        public event Action Pulsada;

        public OvTecla() { CreateHandle(new CreateParams()); }

        public void Poner(string tecla)
        {
            UnregisterHotKey(Handle, 1);
            if (tecla != null && tecla.Length >= 2 && tecla[0] == 'F')
            {
                int n;
                if (int.TryParse(tecla.Substring(1), out n) && n >= 1 && n <= 12) RegisterHotKey(Handle, 1, 0x4000, (uint)(0x70 + n - 1));
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && Pulsada != null) Pulsada();
            base.WndProc(ref m);
        }

        public void Soltar() { UnregisterHotKey(Handle, 1); DestroyHandle(); }
    }

    // ---------- El que maneja todo ----------
    internal static class OvMgr
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static OvCfg cfg = new OvCfg();
        static string arch;
        static System.Windows.Forms.Timer tDetectar, tRefrescar;
        static OvEtw etw;
        static OvForm form;
        static OvTecla tecla;
        static bool oculto, prueba, sensoresOn;
        static Thread hiloSens;
        static readonly object lkSens = new object();
        static Lectura ultima;
        static readonly OvDatos datos = new OvDatos();
        static HashSet<uint> pidsJuego = new HashSet<uint>();
        static readonly Dictionary<uint, string> pidNombre = new Dictionary<uint, string>();
        static Rectangle ultPantalla = Screen.PrimaryScreen.Bounds;
        static double fpsMin, fpsMax, fpsSuma;
        static long fpsMuestras;

        public static OvCfg Config { get { return cfg.Copia(); } }

        public static void Iniciar(string carpeta)
        {
            arch = Path.Combine(carpeta, "overlay.txt");
            cfg = OvCfg.Cargar(arch);
            tDetectar = new System.Windows.Forms.Timer(); tDetectar.Interval = 1000; tDetectar.Tick += delegate { Detectar(); };
            tRefrescar = new System.Windows.Forms.Timer(); tRefrescar.Interval = 500; tRefrescar.Tick += delegate { Refrescar(); };
            try { tecla = new OvTecla(); tecla.Pulsada += delegate { oculto = !oculto; if (oculto && form != null) form.Hide(); }; }
            catch { tecla = null; }
            Aplicar(cfg, false);
        }

        public static void Aplicar(OvCfg nueva, bool guardar)
        {
            cfg = nueva.Copia();
            if (guardar) { try { File.WriteAllLines(arch, cfg.Texto(true)); } catch { } }
            if (tecla != null) tecla.Poner(cfg.Tecla);
            bool vigilar = cfg.Activo && cfg.Procs.Count > 0;
            tDetectar.Enabled = vigilar;
            if (!vigilar) Parar();
            else Detectar();
            if (prueba || (form != null && form.Visible)) Refrescar();
        }

        public static void Prueba(bool si)
        {
            prueba = si;
            if (si) { tRefrescar.Enabled = true; Refrescar(); }
            else if (pidsJuego.Count == 0) { tRefrescar.Enabled = false; if (form != null) form.Hide(); }
        }

        static HashSet<uint> BuscarPids()
        {
            HashSet<uint> r = new HashSet<uint>();
            pidNombre.Clear();
            foreach (string n in cfg.Procs)
            {
                string nom = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
                Process[] ps;
                try { ps = Process.GetProcessesByName(nom); } catch { continue; }
                foreach (Process p in ps) { r.Add((uint)p.Id); pidNombre[(uint)p.Id] = nom; p.Dispose(); }
            }
            return r;
        }

        static void Detectar()
        {
            HashSet<uint> pids = BuscarPids();
            pidsJuego = pids;
            if (pids.Count == 0) { Parar(); return; }
            if (etw == null) etw = new OvEtw();
            if (!etw.Andando) etw.Iniciar();
            etw.Procesos(pids);
            if (!sensoresOn) IniciarSensores();
            tRefrescar.Enabled = true;
        }

        static void Parar()
        {
            tRefrescar.Enabled = prueba;
            if (etw != null && etw.Andando) etw.Parar();
            PararSensores();
            fpsMin = fpsMax = fpsSuma = 0; fpsMuestras = 0;
            if (form != null && !prueba) form.Hide();
        }

        static void IniciarSensores()
        {
            sensoresOn = true;
            hiloSens = new Thread(delegate ()
            {
                Sensores s = null;
                try { s = new Sensores(); s.Abrir(); } catch { s = null; }
                while (sensoresOn && s != null)
                {
                    try { Lectura l = new Lectura(); s.Leer(l); MonitorPC.LeerRam(l);
                        // Sin PawnIO la libreria no da MHz ni uso de CPU: se toman los que calcula Windows en el monitor de PC (si esta andando)
                        Lectura pcL = MonitorPC.Instancia != null ? MonitorPC.Instancia.Ultima : null;
                        if (pcL != null) { if (!l.CpuClockProm.HasValue) l.CpuClockProm = pcL.CpuClockProm; if (!l.CpuLoad.HasValue) l.CpuLoad = pcL.CpuLoad; }
                        lock (lkSens) { ultima = l; } } catch { }
                    for (int i = 0; i < 10 && sensoresOn; i++) Thread.Sleep(100);
                }
                if (s != null) { try { s.Cerrar(); } catch { } }
            });
            hiloSens.IsBackground = true;
            hiloSens.Start();
        }

        static void PararSensores()
        {
            sensoresOn = false;
            if (hiloSens != null) { hiloSens.Join(3000); hiloSens = null; }
            lock (lkSens) { ultima = null; }
        }

        static void Num(OvDatos d, string k, float? v) { if (v.HasValue) d.N[k] = v.Value; }

        static void Refrescar()
        {
            IntPtr fg = GetForegroundWindow();
            uint fpid = 0;
            if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out fpid);

            if (prueba)
            {
                // Vista previa con datos de ejemplo
                OvDatos d = new OvDatos();
                d.HayFps = true;
                d.N["fps"] = 144; d.N["fps.avg"] = 141; d.N["fps.min"] = 96; d.N["fps.max"] = 188; d.N["low1"] = 118; d.N["ft"] = 6.9;
                d.S["proc"] = cfg.Procs.Count > 0 ? cfg.Procs[0] : "Juego"; d.S["time"] = DateTime.Now.ToString("HH:mm");
                d.S["cpu.name"] = "AMD Ryzen 7 5800X3D"; d.N["cpu.load"] = 38; d.N["cpu.temp"] = 61; d.N["cpu.power"] = 72; d.N["cpu.clock"] = 4450; d.N["cpu.volt"] = 1.25;
                d.S["gpu.name"] = "NVIDIA GeForce RTX 4070"; d.N["gpu.load"] = 97; d.N["gpu.temp"] = 66; d.N["gpu.hot"] = 78; d.N["gpu.memtemp"] = 70;
                d.N["gpu.power"] = 185; d.N["gpu.clock"] = 2745; d.N["gpu.memclock"] = 10500; d.N["gpu.volt"] = 1.05;
                d.N["gpu.vram"] = 5120; d.N["gpu.vramtotal"] = 12288; d.N["gpu.vrampct"] = 42; d.N["gpu.fan"] = 1450; d.N["gpu.fanpct"] = 45;
                d.N["ram.load"] = 48; d.N["ram.used"] = 15.4; d.N["ram.total"] = 32.0;
                d.Graf = new float[120];
                for (int i = 0; i < 120; i++) d.Graf[i] = 6.9f + (float)Math.Abs(Math.Sin(i / 7.0)) * 5f;
                Mostrar(d, Screen.PrimaryScreen.Bounds);
                return;
            }
            if (!cfg.Activo || oculto) { if (form != null && form.Visible) form.Hide(); return; }

            bool enJuego;
            if (pidsJuego.Contains(fpid)) { ultPantalla = Screen.FromHandle(fg).Bounds; enJuego = true; }
            else enJuego = !cfg.SoloFrente && pidsJuego.Count > 0;
            if (!enJuego) { if (form != null && form.Visible) form.Hide(); return; }

            datos.Limpiar();
            if (etw != null && etw.Andando) etw.Medir(datos);
            double fps;
            if (datos.N.TryGetValue("fps", out fps) && fps > 0)
            {
                if (fpsMuestras == 0 || fps < fpsMin) fpsMin = fps;
                if (fps > fpsMax) fpsMax = fps;
                fpsSuma += fps; fpsMuestras++;
                datos.N["fps.min"] = fpsMin; datos.N["fps.max"] = fpsMax; datos.N["fps.avg"] = fpsSuma / fpsMuestras;
            }
            string nom;
            if (pidNombre.TryGetValue(fpid, out nom)) datos.S["proc"] = nom;
            else if (cfg.Procs.Count > 0) datos.S["proc"] = cfg.Procs[0];
            datos.S["time"] = DateTime.Now.ToString("HH:mm");

            Lectura l;
            lock (lkSens) { l = ultima; }
            if (l != null)
            {
                if (l.CpuNombre.Length > 0) datos.S["cpu.name"] = l.CpuNombre;
                if (l.GpuNombre.Length > 0) datos.S["gpu.name"] = l.GpuNombre;
                Num(datos, "cpu.load", l.CpuLoad); Num(datos, "cpu.temp", l.CpuTemp); Num(datos, "cpu.power", l.CpuPower);
                Num(datos, "cpu.clock", l.CpuClockProm); Num(datos, "cpu.volt", l.CpuVolt);
                Num(datos, "gpu.load", l.GpuLoad); Num(datos, "gpu.temp", l.GpuTemp); Num(datos, "gpu.hot", l.GpuHot); Num(datos, "gpu.memtemp", l.GpuMemTemp);
                Num(datos, "gpu.power", l.GpuPower); Num(datos, "gpu.clock", l.GpuClock); Num(datos, "gpu.memclock", l.GpuMemClock); Num(datos, "gpu.volt", l.GpuVolt);
                Num(datos, "gpu.vram", l.GpuVramUsada); Num(datos, "gpu.vramtotal", l.GpuVramTotal);
                if (l.GpuVramUsada.HasValue && l.GpuVramTotal.HasValue && l.GpuVramTotal.Value > 0) datos.N["gpu.vrampct"] = l.GpuVramUsada.Value * 100.0 / l.GpuVramTotal.Value;
                Num(datos, "gpu.fan", l.GpuFanRpm); Num(datos, "gpu.fanpct", l.GpuFanPct);
                if (l.RamUso >= 0) datos.N["ram.load"] = l.RamUso;
                if (l.RamTotalGB > 0) { datos.N["ram.used"] = l.RamUsadaGB; datos.N["ram.total"] = l.RamTotalGB; }
            }
            Mostrar(datos, ultPantalla);
        }

        static void Mostrar(OvDatos d, Rectangle pantalla)
        {
            if (form == null || form.IsDisposed) form = new OvForm();
            form.Poner(cfg, d, pantalla);
        }

        public static void Detener()
        {
            if (tDetectar != null) { tDetectar.Dispose(); tDetectar = null; }
            if (tRefrescar != null) { tRefrescar.Dispose(); tRefrescar = null; }
            if (etw != null) { etw.Parar(); etw = null; }
            PararSensores();
            if (tecla != null) { tecla.Soltar(); tecla = null; }
            if (form != null) { form.Dispose(); form = null; }
        }
    }

    // ---------- Ventana de ajustes ----------
    internal sealed class OvAjustes : Form
    {
        static OvAjustes abierta;
        static string T(string es, string en) { return Modulo.T(es, en); }

        OvCfg c;
        bool cargando;
        CheckBox chkActivo, chkNegrita, chkFrente;
        ListBox lista;
        ComboBox cmbProc, cmbEsq, cmbTecla, cmbVar, cmbPre;
        TextBox txt;
        Button bFuente, bTexto, bFondo;
        TrackBar trTexto, trFondo;
        NumericUpDown nX, nY;
        System.Windows.Forms.Timer debounce;

        public static void Mostrar()
        {
            if (abierta != null && !abierta.IsDisposed) { abierta.Activate(); return; }
            abierta = new OvAjustes();
            abierta.Show();
        }

        OvAjustes()
        {
            c = OvMgr.Config;
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = T("Overlay de rendimiento", "Performance overlay");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 700);
            MinimumSize = new Size(480, 400);
            Font = new Font("Segoe UI", 9f);

            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill; f.FlowDirection = FlowDirection.TopDown; f.WrapContents = false; f.AutoScroll = true;
            f.Padding = new Padding(10);
            Controls.Add(f);

            chkActivo = new CheckBox(); chkActivo.AutoSize = true;
            chkActivo.Text = T("Overlay activado (solo aparece cuando uno de estos juegos está abierto)", "Overlay enabled (only shows while one of these games is open)");
            chkActivo.CheckedChanged += Cambio; f.Controls.Add(chkActivo);
            chkFrente = new CheckBox(); chkFrente.AutoSize = true; chkFrente.MaximumSize = new Size(540, 0);
            chkFrente.Text = T("Ocultarlo cuando el juego no está al frente (destildalo para capturarlo con OBS: Captura de ventana > \"Xuca Overlay\")", "Hide it when the game isn't in front (untick it to capture it with OBS: Window Capture > \"Xuca Overlay\")");
            chkFrente.CheckedChanged += Cambio; f.Controls.Add(chkFrente);

            // Juegos
            GroupBox gp = Grupo(T("Juegos (nombre del proceso, sin .exe)", "Games (process name, without .exe)"), 150, f);
            lista = new ListBox(); lista.SetBounds(10, 22, 250, 118); gp.Controls.Add(lista);
            cmbProc = new ComboBox(); cmbProc.DropDownStyle = ComboBoxStyle.DropDown; cmbProc.SetBounds(270, 22, 250, 24);
            cmbProc.DropDown += delegate { LlenarProcesos(); };
            gp.Controls.Add(cmbProc);
            Button ba = new Button(); ba.Text = T("Agregar", "Add"); ba.SetBounds(270, 52, 120, 26);
            ba.Click += delegate
            {
                string n = cmbProc.Text.Trim();
                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
                if (n.Length > 0 && !lista.Items.Contains(n)) { lista.Items.Add(n); Cambio(null, null); }
                cmbProc.Text = "";
            };
            gp.Controls.Add(ba);
            Button bq = new Button(); bq.Text = T("Quitar", "Remove"); bq.SetBounds(400, 52, 120, 26);
            bq.Click += delegate { if (lista.SelectedIndex >= 0) { lista.Items.RemoveAt(lista.SelectedIndex); Cambio(null, null); } };
            gp.Controls.Add(bq);
            Label ej = new Label(); ej.AutoSize = false; ej.SetBounds(270, 86, 250, 54);
            ej.Text = T("Abrí el juego y elegilo de la lista, o escribilo. Ej.: FortniteClient-Win64-Shipping, VALORANT-Win64-Shipping.", "Open the game and pick it from the list, or type it. E.g. FortniteClient-Win64-Shipping, VALORANT-Win64-Shipping.");
            gp.Controls.Add(ej);

            // Plantilla
            GroupBox gt = Grupo(T("Qué mostrar (plantilla: cada línea del texto es una línea del overlay)", "What to show (template: each line of text is a line of the overlay)"), 330, f);
            txt = new TextBox(); txt.Multiline = true; txt.AcceptsReturn = true; txt.WordWrap = false; txt.ScrollBars = ScrollBars.Both;
            txt.Font = new Font("Consolas", 9.5f); txt.SetBounds(10, 22, 510, 160);
            txt.TextChanged += delegate { if (cargando) return; debounce.Stop(); debounce.Start(); };
            gt.Controls.Add(txt);
            debounce = new System.Windows.Forms.Timer(); debounce.Interval = 350;
            debounce.Tick += delegate { debounce.Stop(); Cambio(null, null); };

            cmbVar = new ComboBox(); cmbVar.DropDownStyle = ComboBoxStyle.DropDownList; cmbVar.SetBounds(10, 190, 400, 24);
            foreach (OvVar v in OvCat.Vars) cmbVar.Items.Add(v);
            cmbVar.SelectedIndex = 0;
            gt.Controls.Add(cmbVar);
            Button bin = new Button(); bin.Text = T("Insertar variable", "Insert variable"); bin.SetBounds(420, 189, 100, 26);
            bin.Click += delegate
            {
                OvVar v = cmbVar.SelectedItem as OvVar;
                if (v == null) return;
                txt.SelectedText = v.Key == "graph" ? "{graph:240x40}" : "{" + v.Key + "}";
                txt.Focus();
            };
            gt.Controls.Add(bin);

            cmbPre = new ComboBox(); cmbPre.DropDownStyle = ComboBoxStyle.DropDownList; cmbPre.SetBounds(10, 222, 400, 24);
            cmbPre.Items.AddRange(Modulo.Idioma == "en" ? OvCat.PresetNombresEn : OvCat.PresetNombresEs);
            cmbPre.SelectedIndex = 0;
            gt.Controls.Add(cmbPre);
            Button bpre = new Button(); bpre.Text = T("Cargar modelo", "Load preset"); bpre.SetBounds(420, 221, 100, 26);
            bpre.Click += delegate { txt.Text = OvCat.Presets[Math.Max(0, cmbPre.SelectedIndex)].Replace("\n", "\r\n"); };
            gt.Controls.Add(bpre);

            Label ayuda = new Label(); ayuda.AutoSize = false; ayuda.SetBounds(10, 254, 510, 70);
            ayuda.Text = T(
                "Escribí lo que quieras y poné las variables entre llaves: {gpu.name}, {fps}, {gpu.temp}...  Color: <c=FF0000>texto</c>   Tamaño: <s=150>texto</s> (150 %)   Negrita: <b>texto</b>   Formato: {gpu.power:0.0}   Alerta por valor: {gpu.temp!75,85} (naranja desde 75, rojo desde 85); en FPS al revés: {fps!<60,30}   Gráfico: {graph:260x40}",
                "Write whatever you want and put variables in braces: {gpu.name}, {fps}, {gpu.temp}...  Color: <c=FF0000>text</c>   Size: <s=150>text</s> (150 %)   Bold: <b>text</b>   Format: {gpu.power:0.0}   Alert by value: {gpu.temp!75,85} (orange from 75, red from 85); for FPS reversed: {fps!<60,30}   Graph: {graph:260x40}");
            gt.Controls.Add(ayuda);

            // Estilo
            GroupBox ge = Grupo(T("Estilo y posición", "Style and position"), 215, f);
            bFuente = new Button(); bFuente.SetBounds(10, 22, 250, 26);
            bFuente.Click += delegate
            {
                using (FontDialog fd = new FontDialog())
                {
                    fd.FontMustExist = true; fd.ShowColor = false;
                    try { fd.Font = new Font(c.Fuente, c.Tam, c.Negrita ? FontStyle.Bold : FontStyle.Regular); } catch { }
                    if (fd.ShowDialog(this) == DialogResult.OK)
                    {
                        c.Fuente = fd.Font.Name; c.Tam = fd.Font.SizeInPoints;
                        Cambio(null, null);
                    }
                }
            };
            ge.Controls.Add(bFuente);
            chkNegrita = new CheckBox(); chkNegrita.AutoSize = true; chkNegrita.Text = T("Todo en negrita", "All bold"); chkNegrita.Location = new Point(270, 26);
            chkNegrita.CheckedChanged += Cambio; ge.Controls.Add(chkNegrita);
            bTexto = new Button(); bTexto.SetBounds(10, 54, 250, 26);
            bTexto.Click += delegate { Color n; if (Elegir(c.ColorTexto, out n)) { c.ColorTexto = n.ToArgb(); Cambio(null, null); } };
            ge.Controls.Add(bTexto);
            bFondo = new Button(); bFondo.SetBounds(270, 54, 250, 26);
            bFondo.Click += delegate { Color n; if (Elegir(c.ColorFondo, out n)) { c.ColorFondo = n.ToArgb(); Cambio(null, null); } };
            ge.Controls.Add(bFondo);

            Label lot = new Label(); lot.AutoSize = true; lot.Text = T("Opacidad del texto", "Text opacity"); lot.Location = new Point(10, 92); ge.Controls.Add(lot);
            trTexto = new TrackBar(); trTexto.Minimum = 10; trTexto.Maximum = 100; trTexto.TickFrequency = 10; trTexto.SetBounds(130, 84, 130, 30);
            trTexto.ValueChanged += Cambio; ge.Controls.Add(trTexto);
            Label lof = new Label(); lof.AutoSize = true; lof.Text = T("Opacidad del fondo", "Background opacity"); lof.Location = new Point(270, 92); ge.Controls.Add(lof);
            trFondo = new TrackBar(); trFondo.Minimum = 0; trFondo.Maximum = 100; trFondo.TickFrequency = 10; trFondo.SetBounds(400, 84, 120, 30);
            trFondo.ValueChanged += Cambio; ge.Controls.Add(trFondo);

            Label le = new Label(); le.AutoSize = true; le.Text = T("Esquina", "Corner"); le.Location = new Point(10, 130); ge.Controls.Add(le);
            cmbEsq = new ComboBox(); cmbEsq.DropDownStyle = ComboBoxStyle.DropDownList; cmbEsq.SetBounds(70, 126, 150, 24);
            cmbEsq.Items.AddRange(new object[] { T("Arriba izquierda", "Top left"), T("Arriba derecha", "Top right"), T("Abajo izquierda", "Bottom left"), T("Abajo derecha", "Bottom right") });
            cmbEsq.SelectedIndexChanged += Cambio; ge.Controls.Add(cmbEsq);
            Label lx = new Label(); lx.AutoSize = true; lx.Text = "X"; lx.Location = new Point(235, 130); ge.Controls.Add(lx);
            nX = new NumericUpDown(); nX.Maximum = 4000; nX.SetBounds(255, 126, 70, 24); nX.ValueChanged += Cambio; ge.Controls.Add(nX);
            Label ly = new Label(); ly.AutoSize = true; ly.Text = "Y"; ly.Location = new Point(335, 130); ge.Controls.Add(ly);
            nY = new NumericUpDown(); nY.Maximum = 4000; nY.SetBounds(355, 126, 70, 24); nY.ValueChanged += Cambio; ge.Controls.Add(nY);

            Label lt = new Label(); lt.AutoSize = true; lt.Text = T("Atajo para ocultar/mostrar", "Hide/show hotkey"); lt.Location = new Point(10, 168); ge.Controls.Add(lt);
            cmbTecla = new ComboBox(); cmbTecla.DropDownStyle = ComboBoxStyle.DropDownList; cmbTecla.SetBounds(170, 164, 90, 24);
            cmbTecla.Items.Add(T("Ninguno", "None"));
            for (int i = 1; i <= 12; i++) cmbTecla.Items.Add("F" + i);
            cmbTecla.SelectedIndexChanged += Cambio; ge.Controls.Add(cmbTecla);

            // Botones
            FlowLayoutPanel fb = new FlowLayoutPanel(); fb.AutoSize = true; fb.Margin = new Padding(0, 8, 0, 0); f.Controls.Add(fb);
            CheckBox prev = new CheckBox(); prev.Appearance = Appearance.Button; prev.AutoSize = true; prev.Text = T("Vista previa", "Preview");
            prev.CheckedChanged += delegate { OvMgr.Prueba(prev.Checked); };
            fb.Controls.Add(prev);
            Button bex = new Button(); bex.AutoSize = true; bex.Text = T("Exportar estilo", "Export style");
            bex.Click += delegate
            {
                using (SaveFileDialog sd = new SaveFileDialog())
                {
                    sd.Filter = "Xuca overlay (*.txt)|*.txt"; sd.FileName = "estilo-overlay.txt";
                    if (sd.ShowDialog(this) == DialogResult.OK) { try { File.WriteAllLines(sd.FileName, c.Texto(false)); } catch { } }
                }
            };
            fb.Controls.Add(bex);
            Button bim = new Button(); bim.AutoSize = true; bim.Text = T("Importar estilo", "Import style");
            bim.Click += delegate
            {
                using (OpenFileDialog od = new OpenFileDialog())
                {
                    od.Filter = "Xuca overlay (*.txt)|*.txt";
                    if (od.ShowDialog(this) == DialogResult.OK)
                    {
                        try { c.Leer(File.ReadAllLines(od.FileName), false); Cargar(); Cambio(null, null); } catch { }
                    }
                }
            };
            fb.Controls.Add(bim);

            Cargar();
            FormClosed += delegate { OvMgr.Prueba(false); if (debounce != null) debounce.Dispose(); abierta = null; };
        }

        GroupBox Grupo(string titulo, int alto, FlowLayoutPanel donde)
        {
            GroupBox g = new GroupBox(); g.Text = titulo; g.Size = new Size(530, alto); g.Margin = new Padding(0, 6, 0, 0);
            donde.Controls.Add(g);
            return g;
        }

        bool Elegir(int actual, out Color nuevo)
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true; cd.Color = Color.FromArgb(actual);
                if (cd.ShowDialog(this) == DialogResult.OK) { nuevo = cd.Color; return true; }
            }
            nuevo = Color.Empty;
            return false;
        }

        void LlenarProcesos()
        {
            string t = cmbProc.Text;
            List<string> nombres = new List<string>();
            foreach (Process p in Process.GetProcesses())
            {
                try { if (p.MainWindowHandle != IntPtr.Zero && !nombres.Contains(p.ProcessName)) nombres.Add(p.ProcessName); } catch { }
                p.Dispose();
            }
            nombres.Sort(StringComparer.OrdinalIgnoreCase);
            cmbProc.Items.Clear();
            cmbProc.Items.AddRange(nombres.ToArray());
            cmbProc.Text = t;
        }

        // Ajustes -> controles
        void Cargar()
        {
            cargando = true;
            chkActivo.Checked = c.Activo;
            chkFrente.Checked = c.SoloFrente;
            lista.Items.Clear();
            foreach (string p in c.Procs) lista.Items.Add(p);
            txt.Text = c.Plantilla.Replace("\r\n", "\n").Replace("\n", "\r\n");
            chkNegrita.Checked = c.Negrita;
            trTexto.Value = Math.Max(10, Math.Min(100, c.OpTexto));
            trFondo.Value = Math.Max(0, Math.Min(100, c.OpFondo));
            cmbEsq.SelectedIndex = Math.Max(0, Array.IndexOf(new string[] { "tl", "tr", "bl", "br" }, c.Esquina));
            nX.Value = c.X; nY.Value = c.Y;
            int t = 0;
            if (c.Tecla.Length > 1 && c.Tecla[0] == 'F') int.TryParse(c.Tecla.Substring(1), out t);
            cmbTecla.SelectedIndex = t >= 1 && t <= 12 ? t : 0;
            cargando = false;
            Textos();
        }

        void Textos()
        {
            bFuente.Text = T("Fuente: ", "Font: ") + c.Fuente + " " + c.Tam.ToString("0.#", CultureInfo.InvariantCulture);
            bTexto.Text = T("Color del texto", "Text color");
            bFondo.Text = T("Color del fondo", "Background color");
            bTexto.BackColor = Color.FromArgb(255, Color.FromArgb(c.ColorTexto));
            bTexto.ForeColor = bTexto.BackColor.GetBrightness() > 0.5 ? Color.Black : Color.White;
            bFondo.BackColor = Color.FromArgb(255, Color.FromArgb(c.ColorFondo));
            bFondo.ForeColor = bFondo.BackColor.GetBrightness() > 0.5 ? Color.Black : Color.White;
        }

        // Controles -> ajustes (y se aplica y guarda al instante)
        void Cambio(object o, EventArgs e)
        {
            if (cargando) return;
            c.Activo = chkActivo.Checked;
            c.SoloFrente = chkFrente.Checked;
            c.Procs.Clear();
            foreach (object p in lista.Items) c.Procs.Add((string)p);
            c.Plantilla = txt.Text.Replace("\r\n", "\n");
            c.Negrita = chkNegrita.Checked;
            c.OpTexto = trTexto.Value;
            c.OpFondo = trFondo.Value;
            c.Esquina = new string[] { "tl", "tr", "bl", "br" }[Math.Max(0, cmbEsq.SelectedIndex)];
            c.X = (int)nX.Value; c.Y = (int)nY.Value;
            c.Tecla = cmbTecla.SelectedIndex > 0 ? "F" + cmbTecla.SelectedIndex : "";
            Textos();
            OvMgr.Aplicar(c, true);
        }
    }
}
