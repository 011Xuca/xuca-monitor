// Monitor de PC: temperaturas, uso, potencia y velocidades REALES de CPU, GPU, RAM y discos, con colores segun que
// tan comprometido esta cada cosa (verde / amarillo / rojo), datos de la placa madre y la BIOS, y avisos concretos:
// procesador al limite y frenandose (throttling), placa de video caliente o con el hot spot muy por encima del nucleo,
// ventiladores parados, conector 12VHPWR con un pin sobrecargado, VRAM o RAM llena (con que programas la llenan),
// RAM sin XMP/EXPO o en single channel, discos calientes o gastados.
// Se ve en 4 pestañas propias (CPU, GPU, Memoria, Discos: ver PcPestana), cada una con su registro, su contador y su
// "Activado". Todas leen de este mismo monitor (un solo hilo y una sola conexion a los sensores), que anda mientras
// haya al menos una prendida. registro_pc.txt guarda una copia de todo.
// Los sensores salen de LibreHardwareMonitorLib (ver Sensores.cs). Para la temperatura del procesador hace falta el
// driver PawnIO (firmado, gratis, opcional: lo instala el usuario, ver GuiaPawn); sin el se usa la zona termica de Windows si es creible.
// Lo que Windows da sin drivers (frecuencia y limite del CPU, NVMe directo, bateria en W) esta en SinDriver.cs.
// La RAM y la BIOS salen de WMI y de la tabla SMBIOS.
// Ademas guarda una "foto" liviana cada 2 s: sirve para el grafico, los maximos, y para que las pestañas Errores y
// Rendimiento digan como estaba la PC justo cuando paso algo (tambien lo ultimo antes de un apagon o pantallazo).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Xuca
{
    // ---------- Datos simples (sin nada de LibreHardwareMonitor: los puede usar cualquier parte de la app) ----------
    public enum Nivel { Gris, Verde, Amarillo, Rojo }

    public class Dato { public string Grupo, Nombre, Valor, Pico; }
    public class Ventilador { public string Nombre; public float Rpm, MaxRpm; }
    public class Disco
    {
        public string Nombre; public float? Temp, TempMax, TempAviso, TempCritica, Vida, Lleno, EscritoGB, Horas, Actividad;
        // Sin % de vida (HDD): estado calculado con atributos SMART reales (SmartAta) y su detalle
        public string Estado = "", EstadoDetalle = ""; public Nivel NivelEstado = Nivel.Gris;
    }
    // Disco fisico segun Windows (sin drivers): tipo y bus (NVMe, SSD SATA, HDD, USB), salud SMART y velocidad en vivo
    public class DiscoFisico { public int Numero; public string Nombre = "", Tipo = "", Salud = "", Letras = ""; public Nivel NivelSalud = Nivel.Gris; public bool Lento; public double LeeMBs = -1, EscribeMBs = -1; }

    public class Lectura
    {
        public DateTime T = DateTime.Now;
        public bool HaySensores;
        public string CpuNombre = "", CpuTempFuente = "";
        public float? CpuTemp, CpuTempMaxLib, CpuLoad, CpuPower, CpuClockProm, CpuClockMax, CpuVolt;
        public bool CpuVoltVid;   // CpuVolt es el VID (lo que pide el procesador), no un voltaje medido
        public int CpuTjMax;
        // Sin driver (Windows): % de rendimiento (frecuencia / base) y % de limite que impone el firmware (100 = sin limite)
        public float? CpuPerfPct, CpuLimitePct;
        // CpuBajoNivel: la libreria pudo leer temperatura o vatios reales (hay PawnIO). CpuClockWindows: los MHz son de Windows
        public bool CpuBajoNivel, CpuClockWindows, CpuPowerWindows;
        public readonly List<float> CpuNucleos = new List<float>();
        public string GpuNombre = "", GpuPinNombre = "";
        public readonly List<string> GpuOtras = new List<string>();
        public bool GpuNvidia, GpuAmd, GpuIntegrada;
        public float? GpuTemp, GpuHot, GpuMemTemp, GpuLoad, GpuPower, GpuClock, GpuMemClock, GpuVolt, GpuVramUsada, GpuVramTotal, GpuCompUsada, GpuCompTotal, GpuFanRpm, GpuFanPct, GpuPinMaxA;
        public float RamUso = -1;
        public double RamUsadaGB, RamTotalGB;
        public float? DimmTemp;
        public readonly List<Disco> Discos = new List<Disco>();
        public readonly List<Ventilador> Fans = new List<Ventilador>();
        public readonly List<Dato> CpuDatos = new List<Dato>(), GpuDatos = new List<Dato>(), PlacaDatos = new List<Dato>();
        public string Juego = "";
        public int Bateria = -1; public bool Enchufada = true;
        public float? BateriaW;   // + cargando, - descargando (notebooks; Windows)
        // Paginacion (contadores de Windows): paginas leidas del disco por segundo (fallos de pagina duros) y % del archivo de paginacion en uso
        public float? PaginasSeg, PaginacionUso;
        public readonly List<DiscoFisico> Fisicos = new List<DiscoFisico>();
    }

    public class ModuloRam
    {
        public string Ranura = "", Marca = "", Modelo = "";
        public double GB;
        public int Velocidad, Configurada, Voltaje;
        public string Canal = "";
    }

    public class InfoFija
    {
        public string BiosMarca = "", BiosVersion = "", PlacaFab = "", PlacaModelo = "";
        public DateTime? BiosFecha;
        public string CpuNombre = "";
        public int Nucleos, Hilos, CpuBaseMHz;
        public string GpuDriver = "", GpuNombreWmi = "";
        public DateTime? GpuDriverFecha;
        public readonly List<ModuloRam> Ram = new List<ModuloRam>();
        public string RamTipo = "";
        public int RamTotalGB;
        // Analisis de la RAM
        public Nivel RamXmpNivel = Nivel.Gris; public string RamXmpTexto = "";
        public Nivel RamCanalNivel = Nivel.Gris; public string RamCanalTexto = "";
        public string RamMezclaTexto = "";
        public int RamVelModulos;
        public volatile string Plan = "", PlanGuid = "";
        public volatile List<DiscoFisico> Fisicos = new List<DiscoFisico>();   // se relee cada 2 min (la salud SMART puede cambiar)
    }

    // Una "foto" liviana cada 2 s (para el grafico, los maximos, y para contarle a Errores/Rendimiento como estaba la PC)
    public class Foto
    {
        public DateTime T;
        public float CpuT = float.NaN, CpuU = float.NaN, CpuMhz = float.NaN, GpuT = float.NaN, GpuH = float.NaN, GpuU = float.NaN, GpuW = float.NaN, Ram = float.NaN;
        public string Juego = "";
        public static float N(float? v) { return v.HasValue ? v.Value : float.NaN; }
        public static bool Hay(float v) { return !float.IsNaN(v); }
    }

    public class MonitorPC : Modulo
    {
        public static MonitorPC Instancia;

        public MonitorPC()
        {
            // No es una pestaña: la app no lo muestra, solo lo usan las 4 pestañas de hardware (PcPestana)
            Clave = "pc"; PestEs = PestEn = TipEs = TipEn = "PC";
            NombreEs = "Monitor de hardware"; NombreEn = "Hardware monitor";
            RegistroArch = "registro_pc.txt"; HistArch = "historial_pc.txt";
            ContEs = "Avisos"; ContEn = "Alerts";
            VacioEs = VacioEn = ApagadoEs = ApagadoEn = "";
            Instancia = this;
            Pestanas = new PcPestana[] {
                new PcPestana(this, CAT_CPU, "cpu", "CPU", "CPU", "Procesador y placa madre", "CPU and motherboard",
                    "Sin avisos del procesador. Acá aparecen el límite de temperatura, el throttling por calor o energía, el ventilador parado, la BIOS que necesita actualizarse y el plan de energía o la batería que frenan al jugar.",
                    "No CPU alerts. This is where temperature limit, heat or power throttling, a stopped fan, a BIOS that needs updating and a power plan or battery holding back games show up."),
                new PcPestana(this, CAT_GPU, "gpu", "GPU", "GPU", "Placa de video", "Graphics card",
                    "Sin avisos de la placa de video. Acá aparecen la temperatura del núcleo, hot spot y memoria fuera de rango, ventiladores parados, VRAM llena y el conector 12VHPWR sobrecargado.",
                    "No graphics card alerts. This is where core, hot spot and memory temperatures out of range, stopped fans, full VRAM and an overloaded 12VHPWR connector show up."),
                new PcPestana(this, CAT_RAM, "memoria", "Memoria", "Memory", "Memoria RAM", "Memory",
                    "Sin avisos de la memoria. Acá aparecen la RAM llena (con qué programas la llenan), los módulos calientes, el XMP/EXPO apagado y el single channel.",
                    "No memory alerts. This is where full RAM (and which programs fill it), hot modules, XMP/EXPO turned off and single channel show up."),
                new PcPestana(this, CAT_DISCOS, "discos", "Discos", "Drives", "Discos", "Drives",
                    "Sin avisos de los discos. Acá aparecen el disco que se frena por temperatura, el que tiene poca vida útil y el que Windows marca con fallas (SMART).",
                    "No drive alerts. This is where a drive throttling from heat, one with little life left and one Windows flags as failing (SMART) show up."),
            };
        }

        public const int CAT_CPU = 0, CAT_GPU = 1, CAT_RAM = 2, CAT_DISCOS = 3, CATS = 4;
        public readonly PcPestana[] Pestanas;
        string carpeta;

        // Prende el monitor si hay alguna pestaña de hardware prendida, y lo apaga si no queda ninguna
        internal void Recalcular()
        {
            bool alguna = false;
            foreach (PcPestana p in Pestanas) if (p.Activo) alguna = true;
            Prender(alguna);
        }

        void LogGeneral(string t) { Log(t); }

        // Los errores tecnicos (de lectura de sensores) se anotan como mucho una vez cada 30 min, asi no llenan el registro
        readonly Dictionary<string, DateTime> errAnotado = new Dictionary<string, DateTime>();
        void LogErr(string clave, string texto)
        {
            lock (errAnotado)
            {
                DateTime u;
                if (errAnotado.TryGetValue(clave, out u) && (DateTime.Now - u).TotalMinutes < 30) return;
                errAnotado[clave] = DateTime.Now;
            }
            LogGeneral(texto);
        }

        // =====================================================================================================
        // Arranque / parada
        // =====================================================================================================
        Thread hilo;
        volatile bool parar;
        volatile bool reabrir;
        readonly AutoResetEvent despertar = new AutoResetEvent(false);

        public volatile Lectura Ultima;
        public volatile InfoFija Fija;
        public volatile string ErrorSensores;

        protected override void Empezar()
        {
            carpeta = Path.GetDirectoryName(LogF);
            // Antes de escribir el archivo de los ultimos minutos, se guarda en memoria lo que dejo la sesion anterior
            SesionAnterior(carpeta);
            LeerPawn();
            parar = false;
            hilo = new Thread(Vuelta);
            hilo.IsBackground = true;
            hilo.Priority = ThreadPriority.BelowNormal;
            hilo.Name = "Pulso PC";
            hilo.Start();
        }

        protected override void Terminar()
        {
            parar = true;
            despertar.Set();
            // Los sensores los cierra el mismo hilo al salir (cerrarlos desde aca mientras lee los romperia)
            if (hilo != null) hilo.Join(6000);
            hilo = null;
            Ultima = null;
        }

        // Todo lo que toca la libreria de sensores pasa por estos metodos aparte: si la libreria no carga,
        // el error salta adentro del try de quien los llama (y no rompe el hilo entero).
        [MethodImpl(MethodImplOptions.NoInlining)] static object AbrirSens() { Sensores s = new Sensores(); s.Abrir(); return s; }
        [MethodImpl(MethodImplOptions.NoInlining)] static void CerrarSens(object s) { ((Sensores)s).Cerrar(); }
        [MethodImpl(MethodImplOptions.NoInlining)] static void LeerSens(object s, Lectura L) { ((Sensores)s).Leer(L); }
        [MethodImpl(MethodImplOptions.NoInlining)] static void LeerFijaSens(object s, InfoFija f) { ((Sensores)s).LeerFija(f); }
        [MethodImpl(MethodImplOptions.NoInlining)] static string DiagSens(object s) { return ((Sensores)s).Diagnostico(); }

        void Vuelta()
        {
            object sens = null;
            DateTime proximoIntento = DateTime.MinValue, proximoPlan = DateTime.MinValue;
            int n = 0;
            // Autocuracion: PawnIO instalado y con administrador pero la libreria lee ceros (p. ej. abrio antes de que el driver estuviera listo)
            int sinBajo = 0, intentos = 0; long aperturaMs = 0; bool diagPend = false;
            try
            {
                InfoFija f = new InfoFija();
                try { LeerWmi(f); AnalizarRam(f); } catch (Exception ex) { LogErr("wmi", T("No se pudo leer la RAM/BIOS por WMI: ", "Could not read RAM/BIOS through WMI: ") + ex.Message); }
                Fija = f;
                AbrirPdh();
                while (!parar)
                {
                    if (reabrir && sens != null) { try { CerrarSens(sens); } catch { } sens = null; proximoIntento = DateTime.MinValue; }
                    reabrir = false;
                    if (sens == null && DateTime.Now >= proximoIntento)
                    {
                        try
                        {
                            Stopwatch sw = Stopwatch.StartNew();
                            sens = AbrirSens();
                            aperturaMs = sw.ElapsedMilliseconds; diagPend = true;
                            ErrorSensores = null;
                            try { LeerFijaSens(sens, f); } catch { }
                        }
                        catch (Exception ex)
                        {
                            sens = null;
                            Exception b = ex.GetBaseException();
                            ErrorSensores = b.Message;
                            LogErr("abrir", T("No se pudieron abrir los sensores: ", "Could not open the sensors: ") + b.GetType().Name + ": " + b.Message);
                            proximoIntento = DateTime.Now.AddMinutes(1);
                        }
                    }
                    if (DateTime.Now >= proximoPlan)
                    {
                        LeerPlan(f); LeerFisicos(f); smart = SmartAta.Leer(f.Fisicos); proximoPlan = DateTime.Now.AddMinutes(2);
                        // El usuario instalo (o actualizo) PawnIO por su cuenta: se reabren los sensores para que la libreria lo use
                        Version antes = PawnVersion; LeerPawn();
                        if (PawnOk && PawnVersion != antes) { reabrir = true; LogGeneral(T("PawnIO detectado (v", "PawnIO detected (v") + PawnVersion + ")"); }
                    }

                    Lectura L = new Lectura();
                    if (sens != null)
                    {
                        try { LeerSens(sens, L); }
                        catch (Exception ex) { LogErr("leer:" + ex.GetType().Name, T("Error leyendo sensores: ", "Error reading sensors: ") + ex.Message); }
                    }
                    if (sens != null && L.HaySensores)
                    {
                        // Constancia en el registro, una vez por apertura: para diagnosticar sin adivinar
                        if (diagPend)
                        {
                            diagPend = false;
                            string d = ""; try { d = DiagSens(sens); } catch (Exception ex) { d = ex.Message; }
                            LogGeneral(T("Sensores abiertos en " + aperturaMs + " ms. Administrador: " + (EsAdmin ? "sí" : "NO") + ". PawnIO: " + (PawnVersion != null ? "v" + PawnVer : "no detectado") + ". " + d,
                                         "Sensors opened in " + aperturaMs + " ms. Administrator: " + (EsAdmin ? "yes" : "NO") + ". PawnIO: " + (PawnVersion != null ? "v" + PawnVer : "not detected") + ". " + d));
                        }
                        bool sinLectura = PawnOk && EsAdmin && !L.CpuBajoNivel;
                        if (!sinLectura) { sinBajo = 0; PawnSinDatos = false; if (L.CpuBajoNivel) intentos = 0; }
                        else
                        {
                            sinBajo++;
                            if (sinBajo >= 2) PawnSinDatos = true;
                            // Vueltas de 2 s: reintenta a los 10 s, 20 s y 40 s; despues cada 2 min
                            int umbral = intentos == 0 ? 5 : intentos == 1 ? 10 : intentos == 2 ? 20 : 60;
                            if (sinBajo >= umbral)
                            {
                                sinBajo = 0; intentos++; reabrir = true;
                                LogErr("pawnsd" + Math.Min(intentos, 4), T("PawnIO v" + PawnVer + " instalado pero la librería no leyó el CPU: reabriendo (intento " + intentos + ")",
                                                                          "PawnIO v" + PawnVer + " is installed but the library did not read the CPU: reopening (attempt " + intentos + ")"));
                            }
                        }
                    }
                    Propias(L, f);
                    Ultima = L;
                    Anotar(L, ++n % 5 == 0);
                    try { Evaluar(L, f); }
                    catch (Exception ex) { LogErr("evaluar:" + ex.GetType().Name, "Error: " + ex.Message); }
                    despertar.WaitOne(2000);
                }
            }
            catch (Exception ex) { LogErr("vuelta", "Error: " + ex.Message); }
            finally
            {
                if (sens != null) { try { CerrarSens(sens); } catch { } }
                CerrarPdh();
            }
        }

        // ---------- Lo que se lee sin la libreria: RAM usada, velocidad del procesador (Windows), bateria, juego ----------
        [StructLayout(LayoutKind.Sequential)]
        struct MEMSTAT { public int dwLength; public int dwMemoryLoad; public ulong total, avail, totPage, availPage, totVirt, availVirt, availExt; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        [StructLayout(LayoutKind.Explicit)] struct PDH_VAL { [FieldOffset(0)] public int CStatus; [FieldOffset(8)] public double Valor; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] internal static extern int PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr c, int fmt, IntPtr tipo, out PDH_VAL v);
        [DllImport("pdh.dll")] internal static extern int PdhCloseQuery(IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArrayW(IntPtr c, int fmt, ref int bytes, out int n, IntPtr buf);
        const int PDH_DOUBLE = 0x200 | 0x8000;
        IntPtr pdhQ, pdhPag, pdhPagUso, pdhLee, pdhEsc;
        // Procesador sin driver (frecuencia, % de rendimiento y de limite, zona termica ACPI): ver SinDriver.cs
        readonly CpuWindows cpuWin = new CpuWindows();
        // Temperatura y SMART de los NVMe pedidos directo al disco (sin driver ni admin), cuando la libreria no los da
        List<Disco> directos = new List<Disco>();
        DateTime proximoDirecto = DateTime.MinValue;
        // SMART de los SATA por WMI (se relee cada 2 min, con la lista de discos)
        List<SmartAta> smart = new List<SmartAta>();

        void AbrirPdh()
        {
            cpuWin.Abrir();
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out pdhQ) != 0) { pdhQ = IntPtr.Zero; return; }
                pdhPag = Contador("\\Memory\\Pages Input/sec");
                pdhPagUso = Contador("\\Paging File(_Total)\\% Usage");
                pdhLee = Contador("\\PhysicalDisk(*)\\Disk Read Bytes/sec");
                pdhEsc = Contador("\\PhysicalDisk(*)\\Disk Write Bytes/sec");
                PdhCollectQueryData(pdhQ);
            }
            catch { pdhQ = IntPtr.Zero; }
        }

        IntPtr Contador(string ruta)
        {
            IntPtr c;
            return PdhAddEnglishCounter(pdhQ, ruta, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero;
        }

        // Un contador con comodin (*): nombre de instancia -> valor. PDH_FMT_COUNTERVALUE_ITEM mide 24 bytes en 32 y 64 bits
        // (puntero al nombre, y el valor alineado a 8: estado en +8, double en +16).
        internal static Dictionary<string, double> PdhVarios(IntPtr c)
        {
            Dictionary<string, double> r = new Dictionary<string, double>();
            if (c == IntPtr.Zero) return r;
            int bytes = 0, n;
            PdhGetFormattedCounterArrayW(c, PDH_DOUBLE, ref bytes, out n, IntPtr.Zero);
            if (bytes <= 0) return r;
            IntPtr buf = Marshal.AllocHGlobal(bytes);
            try
            {
                if (PdhGetFormattedCounterArrayW(c, PDH_DOUBLE, ref bytes, out n, buf) != 0) return r;
                for (int i = 0; i < n; i++)
                {
                    IntPtr it = new IntPtr(buf.ToInt64() + i * 24);
                    if (Marshal.ReadInt32(it, 8) != 0) continue;
                    string nom = Marshal.PtrToStringUni(Marshal.ReadIntPtr(it));
                    if (nom != null) r[nom] = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(it, 16));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return r;
        }

        void CerrarPdh()
        {
            try { if (pdhQ != IntPtr.Zero) PdhCloseQuery(pdhQ); } catch { }
            pdhQ = IntPtr.Zero;
            cpuWin.Cerrar();
        }

        internal static float? Pdh(IntPtr c)
        {
            if (c == IntPtr.Zero) return null;
            PDH_VAL v;
            if (PdhGetFormattedCounterValue(c, PDH_DOUBLE, IntPtr.Zero, out v) != 0 || v.CStatus != 0) return null;
            return (float)v.Valor;
        }

        public static void LeerRam(Lectura L)
        {
            MEMSTAT ms = new MEMSTAT(); ms.dwLength = Marshal.SizeOf(typeof(MEMSTAT));
            if (GlobalMemoryStatusEx(ref ms))
            {
                L.RamUso = ms.dwMemoryLoad;
                L.RamTotalGB = ms.total / 1073741824.0;
                L.RamUsadaGB = (ms.total - ms.avail) / 1073741824.0;
            }
        }

        void Propias(Lectura L, InfoFija f)
        {
            LeerRam(L);
            // Sin PawnIO la libreria no puede medir los MHz de los nucleos ni la temperatura: se usa lo que da Windows
            cpuWin.Leer(L, f.CpuBaseMHz);
            // NVMe que la libreria no leyo (sin admin, o si no cargo): se le pide directo al disco cada 10 s
            if (DateTime.Now >= proximoDirecto)
            {
                List<Disco> nuevos = new List<Disco>();
                foreach (DiscoFisico fijo in f.Fisicos)
                {
                    if (TempDeLib(L, fijo.Nombre)) continue;
                    Disco d = DiscoDirecto.Leer(fijo.Numero, fijo.Nombre, fijo.Tipo == "NVMe");
                    if (d != null) nuevos.Add(d);
                }
                directos = nuevos; proximoDirecto = DateTime.Now.AddSeconds(10);
            }
            foreach (Disco d in directos)
            {
                if (TempDeLib(L, d.Nombre)) continue;
                Disco lib = L.Discos.Find(delegate (Disco x) { return MismoDisco(x.Nombre, d.Nombre); });
                if (lib == null) { L.Discos.Add(d); continue; }
                // La libreria lo vio pero sin temperatura: se completa (el espacio usado sigue saliendo de ella)
                lib.Temp = d.Temp; lib.TempAviso = d.TempAviso; lib.TempCritica = d.TempCritica;
                if (!lib.Vida.HasValue) lib.Vida = d.Vida;
                if (!lib.EscritoGB.HasValue) lib.EscritoGB = d.EscritoGB;
                if (!lib.Horas.HasValue) lib.Horas = d.Horas;
            }
            // Discos sin % de vida (HDD): estado SMART real; y respaldo de temperatura (194/190) y escrito (241) si faltan
            foreach (SmartAta s in smart)
            {
                Disco d = L.Discos.Find(delegate (Disco x) { return MismoDisco(x.Nombre, s.Nombre); });
                if (d == null) { d = new Disco(); d.Nombre = s.Nombre; L.Discos.Add(d); }
                if (d.Vida.HasValue) continue;   // SSD con su propio % de vida: alcanza con eso
                d.Estado = s.Estado; d.NivelEstado = s.Nivel; d.EstadoDetalle = s.Detalle;
                if (!d.Temp.HasValue) d.Temp = s.Temp;
                if (!d.EscritoGB.HasValue) d.EscritoGB = s.EscritoGB;
            }
            L.BateriaW = BateriaWindows.Vatios();
            if (pdhQ != IntPtr.Zero)
            {
                try
                {
                    PdhCollectQueryData(pdhQ);
                    L.PaginasSeg = Pdh(pdhPag);
                    L.PaginacionUso = Pdh(pdhPagUso);
                    // Velocidad de cada disco fisico: las instancias son "0 C:", "1 D: E:"... (el numero es el del disco)
                    Dictionary<string, double> lee = PdhVarios(pdhLee), esc = PdhVarios(pdhEsc);
                    foreach (DiscoFisico fijo in f.Fisicos)
                    {
                        DiscoFisico d = new DiscoFisico();
                        d.Numero = fijo.Numero; d.Nombre = fijo.Nombre; d.Tipo = fijo.Tipo; d.Salud = fijo.Salud; d.NivelSalud = fijo.NivelSalud; d.Lento = fijo.Lento;
                        foreach (KeyValuePair<string, double> kv in lee)
                        {
                            int sp = kv.Key.IndexOf(' '); int num;
                            if (sp <= 0 || !int.TryParse(kv.Key.Substring(0, sp), out num) || num != d.Numero) continue;
                            d.Letras = kv.Key.Substring(sp + 1).Trim();
                            d.LeeMBs = kv.Value / 1048576.0;
                            double e; if (esc.TryGetValue(kv.Key, out e)) d.EscribeMBs = e / 1048576.0;
                        }
                        L.Fisicos.Add(d);
                    }
                }
                catch { }
            }
            if (L.CpuNombre == "") L.CpuNombre = f.CpuNombre;
            if (L.CpuTjMax <= 0) L.CpuTjMax = TjMaxPorNombre(L.CpuNombre);
            tjMax = L.CpuTjMax;
            L.Juego = MonitorRend.JuegoAhora ?? "";
            try
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown)
                {
                    L.Bateria = (int)Math.Round(ps.BatteryLifePercent * 100);
                    L.Enchufada = ps.PowerLineStatus != PowerLineStatus.Offline;
                }
            }
            catch { }
        }

        // La libreria ya da la temperatura de ese disco (mismo modelo segun Windows)
        static bool TempDeLib(Lectura L, string nombre)
        {
            foreach (Disco x in L.Discos) if (x.Temp.HasValue && MismoDisco(x.Nombre, nombre)) return true;
            return false;
        }
        static bool MismoDisco(string a, string b)
        {
            a = (a ?? "").Trim(); b = (b ?? "").Trim();
            return a != "" && b != "" && (a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Temperatura a la que el procesador se frena solo. Intel la informa (sale del sensor); AMD no, se usa la de fabrica.
        static int TjMaxPorNombre(string n)
        {
            if (string.IsNullOrEmpty(n)) return 100;
            if (n.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match m = Regex.Match(n, @"Ryzen\s+(?:\d+\s+)?(\d)(\d)\d{2}", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string serie = m.Groups[1].Value;
                    if (n.IndexOf("X3D", StringComparison.OrdinalIgnoreCase) >= 0 && (serie == "5" || serie == "7")) return 89;
                    if (serie == "5") return 90;
                }
                return 95;
            }
            return 100;
        }

        // ---------- Datos fijos por WMI: procesador, placa de video (driver), modulos de RAM ----------
        static string W(ManagementBaseObject o, string p)
        {
            try { object v = o[p]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
        }
        static int WI(ManagementBaseObject o, string p)
        {
            try { object v = o[p]; return v == null ? 0 : Convert.ToInt32(v); } catch { return 0; }
        }

        void LeerWmi(InfoFija f)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject o in col)
                    {
                        if (f.CpuNombre == "") { f.CpuNombre = Regex.Replace(W(o, "Name"), "\\s+", " "); f.Nucleos = WI(o, "NumberOfCores"); f.Hilos = WI(o, "NumberOfLogicalProcessors"); f.CpuBaseMHz = WI(o, "MaxClockSpeed"); }
                        o.Dispose();
                    }
            }
            catch { }
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                using (ManagementObjectCollection col = s.Get())
                {
                    foreach (ManagementBaseObject o in col)
                    {
                        string nom = W(o, "Name");
                        // La dedicada antes que la integrada
                        bool dedicada = Regex.IsMatch(nom, "NVIDIA|GeForce|RTX|GTX|Radeon RX|Radeon Pro|Arc", RegexOptions.IgnoreCase);
                        if (f.GpuNombreWmi == "" || dedicada)
                        {
                            f.GpuNombreWmi = nom;
                            f.GpuDriver = VersionDriver(W(o, "DriverVersion"), nom);
                            string fd = W(o, "DriverDate");
                            if (fd.Length >= 8) { try { f.GpuDriverFecha = ManagementDateTimeConverter.ToDateTime(fd); } catch { } }
                        }
                        o.Dispose();
                    }
                }
            }
            catch { }
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                using (ManagementObjectCollection col = s.Get())
                {
                    foreach (ManagementBaseObject o in col)
                    {
                        ModuloRam m = new ModuloRam();
                        try { m.GB = Convert.ToDouble(o["Capacity"]) / 1073741824.0; } catch { }
                        m.Velocidad = WI(o, "Speed");
                        m.Configurada = WI(o, "ConfiguredClockSpeed");
                        m.Voltaje = WI(o, "ConfiguredVoltage");
                        m.Marca = W(o, "Manufacturer");
                        m.Modelo = W(o, "PartNumber");
                        string loc = W(o, "DeviceLocator"), banco = W(o, "BankLabel");
                        m.Ranura = loc != "" ? loc : banco;
                        Match ch = Regex.Match(loc + " " + banco, @"CHANNEL\s*-?\s*([A-H])\b|Channel([A-H])-", RegexOptions.IgnoreCase);
                        if (ch.Success) m.Canal = (ch.Groups[1].Success && ch.Groups[1].Value != "" ? ch.Groups[1].Value : ch.Groups[2].Value).ToUpperInvariant();
                        if (f.RamTipo == "") f.RamTipo = TipoRam(WI(o, "SMBIOSMemoryType"));
                        if (m.GB > 0) f.Ram.Add(m);
                        o.Dispose();
                    }
                }
            }
            catch { }
            double tot = 0; foreach (ModuloRam m in f.Ram) tot += m.GB;
            f.RamTotalGB = (int)Math.Round(tot);
        }

        // NVIDIA: "32.0.15.6094" -> "560.94" (como lo muestra NVIDIA)
        static string VersionDriver(string v, string nom)
        {
            if (Regex.IsMatch(nom, "NVIDIA|GeForce", RegexOptions.IgnoreCase))
            {
                string d = v.Replace(".", "");
                if (d.Length >= 5) { string u = d.Substring(d.Length - 5); return u.Substring(0, 3) + "." + u.Substring(3); }
            }
            return v;
        }

        static string TipoRam(int c)
        {
            switch (c) { case 20: return "DDR"; case 21: return "DDR2"; case 24: return "DDR3"; case 26: return "DDR4"; case 34: return "DDR5"; case 35: return "LPDDR5"; case 30: return "LPDDR4"; default: return ""; }
        }

        // Discos fisicos segun el administrador de almacenamiento de Windows (sin drivers): tipo, bus y salud SMART
        static void LeerFisicos(InfoFija f)
        {
            List<DiscoFisico> l = new List<DiscoFisico>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject o in s.Get())
                    {
                        DiscoFisico d = new DiscoFisico();
                        if (!int.TryParse(W(o, "DeviceId"), out d.Numero)) continue;
                        d.Nombre = W(o, "FriendlyName");
                        int media = WI(o, "MediaType"), bus = WI(o, "BusType"), salud = W(o, "HealthStatus") == "" ? -1 : WI(o, "HealthStatus");
                        d.Tipo = TipoDisco(media, bus);
                        d.Lento = media == 3 || bus == 7;   // HDD o por USB: paginar ahi da tirones
                        d.NivelSalud = salud == 0 ? Nivel.Verde : salud == 1 ? Nivel.Amarillo : salud == 2 ? Nivel.Rojo : Nivel.Gris;
                        d.Salud = salud == 0 ? T("Sana", "Healthy") : salud == 1 ? T("Con advertencias", "Warning") : salud == 2 ? T("Con fallas", "Failing") : T("Sin dato", "Unknown");
                        l.Add(d);
                        o.Dispose();
                    }
            }
            catch { }
            l.Sort(delegate (DiscoFisico a, DiscoFisico b) { return a.Numero.CompareTo(b.Numero); });
            f.Fisicos = l;
        }

        // MediaType 3 = HDD, 4 = SSD, 5 = SCM; BusType 17 = NVMe, 11 = SATA, 7 = USB, 8 = RAID, 10 = SAS, 3 = ATA
        public static string TipoDisco(int media, int bus)
        {
            if (bus == 17) return "NVMe";
            string m = media == 3 ? "HDD" : media == 4 ? "SSD" : media == 5 ? "SCM" : "";
            string b = bus == 11 ? "SATA" : bus == 7 ? "USB" : bus == 8 ? "RAID" : bus == 10 ? "SAS" : bus == 3 ? "ATA" : "";
            return (m + " " + b).Trim();
        }

        // Plan de energia activo (en "Ahorro de energia" el procesador va mas lento)
        public const string PlanAhorro = "a1841308-3541-4fab-bc81-f71556f20b4a";
        void LeerPlan(InfoFija f)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", "/getactivescheme");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    Match m = Regex.Match(o, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((.+)\)");
                    if (m.Success) { f.PlanGuid = m.Groups[1].Value.ToLowerInvariant(); f.Plan = m.Groups[2].Value.Trim(); }
                }
            }
            catch { }
        }

        // ---------- Analisis de la RAM: XMP/EXPO, canales, modulos distintos ----------
        static readonly Dictionary<int, int> Codigo2 = new Dictionary<int, int>
        {
            { 18, 1866 }, { 21, 2133 }, { 24, 2400 }, { 26, 2666 }, { 29, 2933 }, { 30, 3000 }, { 32, 3200 }, { 33, 3333 }, { 34, 3466 }, { 36, 3600 },
            { 37, 3733 }, { 38, 3866 }, { 40, 4000 }, { 42, 4266 }, { 44, 4400 }, { 46, 4600 }, { 48, 4800 }, { 52, 5200 }, { 56, 5600 }, { 60, 6000 },
            { 62, 6200 }, { 64, 6400 }, { 66, 6600 }, { 68, 6800 }, { 72, 7200 }, { 76, 7600 }, { 80, 8000 }
        };

        // La velocidad de fabrica de los modulos, sacada del modelo (la mayoria de las marcas la ponen en el nombre)
        public static int VelocidadDeModelo(string pn)
        {
            if (string.IsNullOrEmpty(pn)) return 0;
            pn = pn.Trim().ToUpperInvariant();
            Match m;
            int v = 0;
            if ((m = Regex.Match(pn, @"^CM[A-Z]+\d+GX[45]M\d[A-Z](\d{4})C\d")).Success) v = int.Parse(m.Groups[1].Value);           // Corsair: CMK16GX4M2B3200C16
            else if ((m = Regex.Match(pn, @"^F[45]-(\d{4})[A-Z]\d")).Success) v = int.Parse(m.Groups[1].Value);                    // G.Skill: F4-3600C16-8GVKC
            else if ((m = Regex.Match(pn, @"^(?:KF|HX)[45](\d{2})[A-Z]")).Success) { int c = int.Parse(m.Groups[1].Value); v = Codigo2.ContainsKey(c) ? Codigo2[c] : c * 100; } // Kingston: KF432C16BB, KF556C40
            else if ((m = Regex.Match(pn, @"^BLS?\d+G(\d{2})C\d")).Success) { int c = int.Parse(m.Groups[1].Value); v = Codigo2.ContainsKey(c) ? Codigo2[c] : c * 100; }     // Ballistix: BL8G32C16U4B
            else if ((m = Regex.Match(pn, @"^CP\d+G(\d{2})C\d")).Success) { int c = int.Parse(m.Groups[1].Value); v = Codigo2.ContainsKey(c) ? Codigo2[c] : c * 100; }       // Crucial Pro DDR5: CP16G56C46U5
            else if ((m = Regex.Match(pn, @"^PV[A-Z]*\d+G(\d{3})C")).Success) v = int.Parse(m.Groups[1].Value) * 10;               // Patriot: PVS416G320C6K
            else if ((m = Regex.Match(pn, @"^AX[45]U(\d{4})")).Success) v = int.Parse(m.Groups[1].Value);                          // ADATA XPG: AX4U32008G16A
            else if ((m = Regex.Match(pn, @"(\d{4})(?:HC|CL|C)\d{2}")).Success) v = int.Parse(m.Groups[1].Value);                  // TeamGroup y otros: TF3D416G3200HC16F
            return v >= 1600 && v <= 10000 ? v : 0;
        }

        // Memoria comun de fabrica (Samsung, SK Hynix, Micron, Crucial basica): no trae perfil XMP/EXPO
        static bool EsEstandar(string pn)
        {
            if (string.IsNullOrEmpty(pn)) return false;
            return Regex.IsMatch(pn.Trim().ToUpperInvariant(), @"^(M3\d{2}|M4\d{2}|HMA|HMAA|HMCG|HMCT|HMT|MTA|MTC|CT\d+G[45]|ACR|RMSA|TSB|HMAG)");
        }

        static void AnalizarRam(InfoFija f)
        {
            if (f.Ram.Count == 0)
            {
                f.RamXmpTexto = T("No se pudieron leer los módulos de RAM.", "Could not read the RAM modules.");
                return;
            }
            int conf = 0, volt = 0, velMod = 0; string pn = "";
            foreach (ModuloRam m in f.Ram)
            {
                int c = m.Configurada > 0 ? m.Configurada : m.Velocidad;
                if (c > conf) conf = c;
                if (m.Voltaje > volt) volt = m.Voltaje;
                int vm = VelocidadDeModelo(m.Modelo);
                if (vm > velMod) { velMod = vm; }
                if (pn == "" && m.Modelo != "") pn = m.Modelo;
            }
            bool ddr4 = f.RamTipo == "DDR4", ddr5 = f.RamTipo == "DDR5";
            string sV = volt > 0 ? (volt / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V" : "";
            f.RamVelModulos = velMod;
            if (conf <= 0)
            {
                f.RamXmpTexto = T("No se pudo leer a qué velocidad corre la RAM.", "Could not read the RAM speed.");
            }
            else if (velMod > 0)
            {
                if (conf >= velMod - 60)
                {
                    f.RamXmpNivel = Nivel.Verde;
                    f.RamXmpTexto = T("XMP/EXPO activo: corre a " + conf + " MT/s, la velocidad de tus módulos.", "XMP/EXPO on: running at " + conf + " MT/s, your modules' speed.");
                }
                else
                {
                    f.RamXmpNivel = Nivel.Amarillo;
                    f.RamXmpTexto = T("XMP/EXPO apagado: tus módulos son de " + velMod + " MT/s pero corren a " + conf + " MT/s. Activá el perfil en la BIOS (XMP en Intel, EXPO o DOCP en AMD) y ganás rendimiento gratis.",
                        "XMP/EXPO off: your modules are " + velMod + " MT/s but run at " + conf + " MT/s. Enable the profile in the BIOS (XMP on Intel, EXPO or DOCP on AMD) for free performance.");
                }
            }
            else if (EsEstandar(pn))
            {
                f.RamXmpNivel = Nivel.Verde;
                f.RamXmpTexto = T("Memoria estándar (sin perfil XMP/EXPO): corre a " + conf + " MT/s, que es su velocidad normal.", "Standard memory (no XMP/EXPO profile): running at " + conf + " MT/s, its normal speed.");
            }
            else if ((ddr4 && (volt >= 1300 || conf > 3200)) || (ddr5 && (volt >= 1200 || conf > 6400)))
            {
                f.RamXmpNivel = Nivel.Verde;
                f.RamXmpTexto = T("XMP/EXPO activo: corre a " + conf + " MT/s" + (sV != "" ? " con " + sV : "") + ".", "XMP/EXPO on: running at " + conf + " MT/s" + (sV != "" ? " at " + sV : "") + ".");
            }
            else if ((ddr4 && conf <= 2666) || (ddr5 && conf <= 4800))
            {
                f.RamXmpNivel = Nivel.Amarillo;
                f.RamXmpTexto = T("Probablemente sin XMP/EXPO: corre a " + conf + " MT/s" + (sV != "" ? " con " + sV : "") + ", la velocidad básica. Si tus módulos son de más velocidad (fijate en la etiqueta o la caja), activá el perfil en la BIOS.",
                    "Probably no XMP/EXPO: running at " + conf + " MT/s" + (sV != "" ? " at " + sV : "") + ", the basic speed. If your modules are faster (check the label or box), enable the profile in the BIOS.");
            }
            else
            {
                f.RamXmpTexto = T("Corre a " + conf + " MT/s" + (sV != "" ? " con " + sV : "") + ". No se puede saber con seguridad si el XMP/EXPO está activo: compará con la velocidad de la etiqueta de tus módulos.",
                    "Running at " + conf + " MT/s" + (sV != "" ? " at " + sV : "") + ". It is not possible to tell for sure if XMP/EXPO is on: compare with the speed on your modules' label.");
            }

            // Canales
            int n = f.Ram.Count;
            List<string> canales = new List<string>();
            bool todos = true;
            foreach (ModuloRam m in f.Ram) { if (m.Canal == "") todos = false; else if (!canales.Contains(m.Canal)) canales.Add(m.Canal); }
            if (n == 1)
            {
                f.RamCanalNivel = Nivel.Amarillo;
                f.RamCanalTexto = T("Single channel (1 solo módulo): la RAM trabaja a la mitad de ancho. En juegos eso suele costar entre 10 y 30 % de FPS. Con 2 módulos iguales pasa a dual channel.",
                    "Single channel (only 1 module): RAM works at half width. In games that usually costs 10 to 30 % FPS. With 2 matching modules it becomes dual channel.");
            }
            else if (todos && canales.Count == 1)
            {
                f.RamCanalNivel = Nivel.Amarillo;
                f.RamCanalTexto = T("Single channel: los " + n + " módulos están en el mismo canal (" + canales[0] + "). Pasá uno a la ranura del otro canal (normalmente se usan la 2 y la 4, mirá el manual de la placa).",
                    "Single channel: all " + n + " modules are on the same channel (" + canales[0] + "). Move one to the other channel's slot (usually slots 2 and 4, check the motherboard manual).");
            }
            else if (canales.Count >= 2)
            {
                f.RamCanalNivel = Nivel.Verde;
                f.RamCanalTexto = T("Dual channel: " + n + " módulos en los canales " + string.Join(" y ", canales.ToArray()) + ".", "Dual channel: " + n + " modules on channels " + string.Join(" and ", canales.ToArray()) + ".");
            }
            else
            {
                f.RamCanalTexto = T(n + " módulos: van en dual channel si están en las ranuras que recomienda la placa (normalmente la 2 y la 4).", n + " modules: they run in dual channel if they are in the slots the motherboard recommends (usually 2 and 4).");
            }

            // Modulos distintos
            bool distintos = false;
            foreach (ModuloRam m in f.Ram)
                if (Math.Abs(m.GB - f.Ram[0].GB) > 0.5 || (m.Modelo != "" && f.Ram[0].Modelo != "" && m.Modelo != f.Ram[0].Modelo)) distintos = true;
            if (distintos)
                f.RamMezclaTexto = T("Tenés módulos distintos (tamaño o modelo): pueden funcionar, pero es una causa común de inestabilidad con XMP/EXPO y de que no ande el dual channel completo.",
                    "You have different modules (size or model): they can work, but it is a common cause of instability with XMP/EXPO and of partial dual channel.");
        }

        // =====================================================================================================
        // Historia (fotos cada 2 s), maximos, y lo que se le cuenta a las otras pestañas
        // =====================================================================================================
        readonly List<Foto> historia = new List<Foto>();
        const int MaxHistoria = 300; // 10 minutos
        public class Picos { public float CpuT = float.NaN, GpuT = float.NaN, GpuH = float.NaN, GpuMem = float.NaN, Ram = float.NaN, CpuW = float.NaN, GpuW = float.NaN; public string Juego = ""; }
        public readonly Picos PicoSesion = new Picos();
        public volatile Picos PicoJuego = new Picos();
        static volatile int tjMax = 100;
        public static int TjMax { get { return tjMax; } }

        static float Mx(float a, float? b) { if (!b.HasValue) return a; return float.IsNaN(a) || b.Value > a ? b.Value : a; }

        void Anotar(Lectura L, bool guardar)
        {
            Foto f = new Foto();
            f.T = L.T; f.CpuT = Foto.N(L.CpuTemp); f.CpuU = Foto.N(L.CpuLoad); f.CpuMhz = Foto.N(L.CpuClockProm);
            f.GpuT = Foto.N(L.GpuTemp); f.GpuH = Foto.N(L.GpuHot); f.GpuU = Foto.N(L.GpuLoad); f.GpuW = Foto.N(L.GpuPower);
            f.Ram = L.RamUso >= 0 ? L.RamUso : float.NaN; f.Juego = L.Juego;
            lock (historia)
            {
                historia.Add(f);
                if (historia.Count > MaxHistoria) historia.RemoveAt(0);
            }
            Picos[] ps;
            if (L.Juego != "")
            {
                if (PicoJuego.Juego != L.Juego) { Picos nj = new Picos(); nj.Juego = L.Juego; PicoJuego = nj; }
                ps = new Picos[] { PicoSesion, PicoJuego };
            }
            else ps = new Picos[] { PicoSesion };
            foreach (Picos p in ps)
            {
                p.CpuT = Mx(p.CpuT, L.CpuTemp); p.GpuT = Mx(p.GpuT, L.GpuTemp); p.GpuH = Mx(p.GpuH, L.GpuHot); p.GpuMem = Mx(p.GpuMem, L.GpuMemTemp);
                p.Ram = Mx(p.Ram, L.RamUso >= 0 ? L.RamUso : (float?)null); p.CpuW = Mx(p.CpuW, L.CpuPower); p.GpuW = Mx(p.GpuW, L.GpuPower);
            }
            if (guardar) GuardarUltimos();
        }

        public List<Foto> Historia(int segundos)
        {
            DateTime desde = DateTime.Now.AddSeconds(-segundos);
            List<Foto> r = new List<Foto>();
            lock (historia) foreach (Foto f in historia) if (f.T >= desde) r.Add(f);
            return r;
        }

        // Los picos entre dos momentos (NaN = no hay dato). Sirve para "como estaba la PC cuando paso X".
        public static Foto PicoEntre(DateTime desde, DateTime hasta)
        {
            MonitorPC pc = Instancia;
            if (pc == null || !pc.Activo) return null;
            List<Foto> l = new List<Foto>();
            lock (pc.historia) foreach (Foto f in pc.historia) if (f.T >= desde && f.T <= hasta) l.Add(f);
            return Juntar(l);
        }

        static Foto Juntar(List<Foto> l)
        {
            if (l.Count == 0) return null;
            Foto r = new Foto();
            r.T = l[l.Count - 1].T;
            foreach (Foto f in l)
            {
                r.CpuT = M(r.CpuT, f.CpuT); r.CpuU = M(r.CpuU, f.CpuU); r.CpuMhz = M(r.CpuMhz, f.CpuMhz); r.GpuT = M(r.GpuT, f.GpuT); r.GpuH = M(r.GpuH, f.GpuH);
                r.GpuU = M(r.GpuU, f.GpuU); r.GpuW = M(r.GpuW, f.GpuW); r.Ram = M(r.Ram, f.Ram);
                if (f.Juego != "") r.Juego = f.Juego;
            }
            return r;
        }
        static float M(float a, float b) { if (float.IsNaN(b)) return a; return float.IsNaN(a) || b > a ? b : a; }

        // Una linea con como esta (o estaba) la PC: "CPU 92 °C · 98 % · 4.20 GHz  |  GPU 84 °C (hot spot 101 °C) · 99 % · 310 W  |  RAM 71 %"
        public static string Resumen(Foto f)
        {
            if (f == null) return "";
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> p = new List<string>();
            List<string> c = new List<string>();
            if (Foto.Hay(f.CpuT)) c.Add(f.CpuT.ToString("0", ci) + " °C");
            if (Foto.Hay(f.CpuU)) c.Add(f.CpuU.ToString("0", ci) + " %");
            if (Foto.Hay(f.CpuMhz)) c.Add((f.CpuMhz / 1000f).ToString("0.00", ci) + " GHz");
            if (c.Count > 0) p.Add("CPU " + string.Join(" · ", c.ToArray()));
            List<string> g = new List<string>();
            if (Foto.Hay(f.GpuT)) g.Add(f.GpuT.ToString("0", ci) + " °C" + (Foto.Hay(f.GpuH) ? " (hot spot " + f.GpuH.ToString("0", ci) + " °C)" : ""));
            if (Foto.Hay(f.GpuU)) g.Add(f.GpuU.ToString("0", ci) + " %");
            if (Foto.Hay(f.GpuW)) g.Add(f.GpuW.ToString("0", ci) + " W");
            if (g.Count > 0) p.Add("GPU " + string.Join(" · ", g.ToArray()));
            if (Foto.Hay(f.Ram)) p.Add("RAM " + f.Ram.ToString("0", ci) + " %");
            if (f.Juego != "") p.Add(T("jugando ", "playing ") + f.Juego);
            return string.Join("   |   ", p.ToArray());
        }

        // Lo de los ultimos 10 s (vacio si las pestañas de hardware estan apagadas)
        public static string ResumenAhora()
        {
            return Resumen(PicoEntre(DateTime.Now.AddSeconds(-10), DateTime.Now.AddSeconds(1)));
        }

        // ---------- Los ultimos minutos guardados en disco (para saber como estaba la PC antes de un apagon) ----------
        const string ArchUltimos = "pc_ultimos_minutos.txt";
        static readonly object cerrojoAnt = new object();
        static List<Foto> anterior;

        static string Linea(Foto f)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            return f.T.ToString("yyyyMMddHHmmss", ci) + ";" + f.CpuT.ToString(ci) + ";" + f.CpuU.ToString(ci) + ";" + f.CpuMhz.ToString(ci) + ";" + f.GpuT.ToString(ci) + ";"
                + f.GpuH.ToString(ci) + ";" + f.GpuU.ToString(ci) + ";" + f.GpuW.ToString(ci) + ";" + f.Ram.ToString(ci) + ";" + (f.Juego ?? "").Replace(";", ",");
        }

        static Foto DeLinea(string l)
        {
            try
            {
                string[] p = l.Split(';');
                if (p.Length < 10) return null;
                CultureInfo ci = CultureInfo.InvariantCulture;
                Foto f = new Foto();
                f.T = DateTime.ParseExact(p[0], "yyyyMMddHHmmss", ci);
                f.CpuT = float.Parse(p[1], ci); f.CpuU = float.Parse(p[2], ci); f.CpuMhz = float.Parse(p[3], ci); f.GpuT = float.Parse(p[4], ci);
                f.GpuH = float.Parse(p[5], ci); f.GpuU = float.Parse(p[6], ci); f.GpuW = float.Parse(p[7], ci); f.Ram = float.Parse(p[8], ci); f.Juego = p[9];
                return f;
            }
            catch { return null; }
        }

        void GuardarUltimos()
        {
            if (carpeta == null) return;
            StringBuilder sb = new StringBuilder();
            lock (historia)
            {
                // Los ultimos 5 minutos, una cada 10 s
                for (int i = Math.Max(0, historia.Count - 150); i < historia.Count; i += 5) sb.Append(Linea(historia[i])).Append("\r\n");
                if (historia.Count > 0) sb.Append(Linea(historia[historia.Count - 1])).Append("\r\n");
            }
            try { File.WriteAllText(Path.Combine(carpeta, ArchUltimos), sb.ToString(), Encoding.UTF8); } catch { }
        }

        // Lo que quedo guardado de ANTES del ultimo arranque de Windows (se lee una sola vez, antes de que se pise)
        public static List<Foto> SesionAnterior(string dir)
        {
            lock (cerrojoAnt)
            {
                if (anterior != null) return anterior;
                anterior = new List<Foto>();
                try
                {
                    string f = Path.Combine(dir, ArchUltimos);
                    if (File.Exists(f))
                    {
                        DateTime arranque = DateTime.Now.AddMilliseconds(-(double)GetTickCount64());
                        foreach (string l in File.ReadAllLines(f, Encoding.UTF8))
                        {
                            Foto x = DeLinea(l);
                            if (x != null && x.T < arranque) anterior.Add(x);
                        }
                    }
                }
                catch { }
                return anterior;
            }
        }

        // Picos de los ultimos 2 minutos antes de que se cortara la sesion anterior (null si no hay datos)
        public static Foto PicoAntesDelCorte(string dir, out DateTime ultimo)
        {
            List<Foto> a = SesionAnterior(dir);
            ultimo = DateTime.MinValue;
            if (a.Count == 0) return null;
            ultimo = a[a.Count - 1].T;
            List<Foto> l = new List<Foto>();
            foreach (Foto f in a) if (f.T >= ultimo.AddMinutes(-2)) l.Add(f);
            Foto r = Juntar(l);
            if (r != null) r.Juego = a[a.Count - 1].Juego;
            return r;
        }

        // =====================================================================================================
        // Avisos
        // =====================================================================================================
        // Cada regla tiene que cumplirse varias lecturas seguidas para entrar (y para salir), asi un pico suelto no avisa
        class Regla
        {
            public bool Activa;
            public DateTime Desde, UltimoAviso = DateTime.MinValue;
            public float Pico = float.NaN;
            public bool Avisada;
            int racha;
            // +1 recien entro, -1 recien salio, 0 sin cambio
            public int Paso(bool cond, int entrar, int salir)
            {
                if (cond == Activa) { racha = 0; return 0; }
                racha++;
                if (racha < (cond ? entrar : salir)) return 0;
                racha = 0; Activa = cond;
                if (cond) { Desde = DateTime.Now; Pico = float.NaN; Avisada = false; }
                return cond ? 1 : -1;
            }
            public bool PuedeAvisar(int minutos) { return (DateTime.Now - UltimoAviso).TotalMinutes >= minutos; }
        }

        readonly Dictionary<string, Regla> reglas = new Dictionary<string, Regla>();
        Regla R(string k) { Regla r; if (!reglas.TryGetValue(k, out r)) { r = new Regla(); reglas[k] = r; } return r; }

        float cpuRefMhz;            // la velocidad que alcanza el procesador cargado y frio (para saber si se esta frenando)
        float cpuRefSinTemp;        // lo mismo sin temperatura: la que logra cargado y sin limite del firmware
        float fanCpuMax;
        bool ramConfigAvisada, planAvisado, bateriaAvisada;
        string juegoAnterior = "";

        // Temperatura de CPU y GPU: hasta 85 °C es normal jugando (80 °C no es nada), de 85 a 89 °C se pinta amarillo (solo
        // visual, sin aviso) y desde 90 °C es rojo y avisa. En un CPU con TjMax bajo (Ryzen X3D: 89 °C) todo se corre a su limite.
        public const float TempAmarillo = 85f, TempRojo = 90f;
        public static float RojoCpu(int tj) { return Math.Min(TempRojo, (tj <= 0 ? 100 : tj) - 3); }
        public static Nivel NivelCpu(float? t, int tj)
        {
            float rojo = RojoCpu(tj);
            return Umbral(t, Math.Min(TempAmarillo, rojo - 4), rojo);
        }
        public static float RojoGpu(bool nvidia) { return TempRojo; }
        public static Nivel NivelGpu(float? t, bool nvidia) { return Umbral(t, TempAmarillo, RojoGpu(nvidia)); }
        public static Nivel NivelHot(float? t) { return Umbral(t, 95f, 105f); }
        public static Nivel NivelMemGpu(float? t) { return Umbral(t, 95f, 104f); }
        public static Nivel NivelRam(float uso) { return uso < 0 ? Nivel.Gris : Umbral(uso, 85f, 93f); }
        public static Nivel NivelDimm(float? t) { return Umbral(t, 70f, 82f); }
        public static float AvisoDisco(Disco d) { return d.TempAviso.HasValue ? d.TempAviso.Value : 70f; }
        public static Nivel NivelDisco(Disco d) { if (!d.Temp.HasValue) return Nivel.Gris; float a = AvisoDisco(d); return d.Temp.Value >= a ? Nivel.Rojo : d.Temp.Value >= a - 10 ? Nivel.Amarillo : Nivel.Verde; }
        public static Nivel NivelVida(float? v) { if (!v.HasValue) return Nivel.Gris; return v.Value <= 10 ? Nivel.Rojo : v.Value <= 30 ? Nivel.Amarillo : Nivel.Verde; }
        public static Nivel Umbral(float? v, float amarillo, float rojo)
        {
            if (!v.HasValue) return Nivel.Gris;
            if (v.Value >= rojo) return Nivel.Rojo;
            if (v.Value >= amarillo) return Nivel.Amarillo;
            return Nivel.Verde;
        }

        static string Gc(float? v) { return v.HasValue ? ((int)Math.Round(v.Value)).ToString() + " °C" : "-"; }
        static string Ghz(float? v) { return v.HasValue ? (v.Value / 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " GHz" : "-"; }

        void Evaluar(Lectura L, InfoFija f)
        {
            int tj = L.CpuTjMax > 0 ? L.CpuTjMax : 100;
            Foto ahora = new Foto();
            ahora.T = L.T; ahora.CpuT = Foto.N(L.CpuTemp); ahora.CpuU = Foto.N(L.CpuLoad); ahora.CpuMhz = Foto.N(L.CpuClockProm);
            ahora.GpuT = Foto.N(L.GpuTemp); ahora.GpuH = Foto.N(L.GpuHot); ahora.GpuU = Foto.N(L.GpuLoad); ahora.GpuW = Foto.N(L.GpuPower);
            ahora.Ram = L.RamUso >= 0 ? L.RamUso : float.NaN; ahora.Juego = L.Juego;
            string ctx = Resumen(ahora);

            // ---------------- CPU ----------------
            if (L.CpuTemp.HasValue)
            {
                float t = L.CpuTemp.Value;
                // Velocidad de referencia: la que logra cargado (mas de 40 %) y lejos del limite de temperatura
                if (L.CpuClockProm.HasValue && L.CpuLoad.HasValue && L.CpuLoad.Value >= 40 && t < tj - 12 && L.CpuClockProm.Value > cpuRefMhz) cpuRefMhz = L.CpuClockProm.Value;

                Regla r = R("cpu_limite");
                int c = r.Paso(r.Activa ? t >= tj - 7 : t >= tj - 3, 3, 5);
                if (r.Activa) r.Pico = M(r.Pico, t);
                if (c == 1 && r.PuedeAvisar(10))
                {
                    bool frena = cpuRefMhz > 0 && L.CpuClockProm.HasValue && L.CpuLoad.HasValue && L.CpuLoad.Value >= 30 && L.CpuClockProm.Value < cpuRefMhz * 0.85f;
                    List<string> que = new List<string>();
                    que.Add(T("El procesador llegó a " + Gc(t) + " y su límite (TjMax) es " + tj + " °C: a esa temperatura se frena solo para no dañarse.",
                              "The CPU reached " + Gc(t) + " and its limit (TjMax) is " + tj + " °C: at that temperature it throttles itself to avoid damage."));
                    if (frena)
                        que.Add(T("Y se está frenando: bajó de " + Ghz(cpuRefMhz) + " a " + Ghz(L.CpuClockProm) + " (throttling térmico). Eso baja los FPS y da tirones.",
                                  "And it is throttling: it dropped from " + Ghz(cpuRefMhz) + " to " + Ghz(L.CpuClockProm) + " (thermal throttling). That lowers FPS and causes stutters."));
                    string top = TopProcesos(false, 3);
                    if (top != "") que.Add(T("Lo que más lo estaba usando: ", "What was using it the most: ") + top);
                    List<string> probar = new List<string>();
                    probar.Add(T("Limpiá el polvo del cooler y del gabinete, y fijate que el ventilador del procesador gire.", "Clean the dust from the cooler and case, and check that the CPU fan spins."));
                    probar.Add(T("Si la pasta térmica tiene más de 3-4 años, cambiala.", "If the thermal paste is more than 3-4 years old, replace it."));
                    probar.Add(T("Si es un cooler de stock (el que viene en la caja), en juegos pesados suele quedarse corto.", "If it is a stock cooler (the one in the box), it often falls short in heavy games."));
                    if (L.CpuNombre.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0)
                        probar.Add(T("En Ryzen, un undervolt con Curve Optimizer (en la BIOS, empezando por -10) baja mucho la temperatura sin perder rendimiento.", "On Ryzen, an undervolt with Curve Optimizer (in the BIOS, starting at -10) lowers temperature a lot without losing performance."));
                    Aviso(CAT_CPU, T("CPU: AL LÍMITE DE TEMPERATURA", "CPU: AT ITS TEMPERATURE LIMIT"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now; r.Avisada = true;
                }
                else if (c == -1 && r.Avisada) Volvio(CAT_CPU, T("CPU: volvió a una temperatura normal", "CPU: temperature back to normal"), r, Gc(t));

                // Caliente mucho rato: 90 °C o mas durante 3 minutos seguidos (sin llegar a su limite, que avisa aparte)
                Regla rc = R("cpu_caliente");
                float caliente = RojoCpu(tj);
                int cc = rc.Paso(rc.Activa ? t >= caliente - 3 : t >= caliente, 90, 15);
                if (rc.Activa) rc.Pico = M(rc.Pico, t);
                if (cc == 1 && !R("cpu_limite").Activa && rc.PuedeAvisar(30))
                {
                    List<string> que = new List<string>();
                    que.Add(T("El procesador estuvo más de 3 minutos seguidos a " + (int)caliente + " °C o más (llegó a " + Gc(rc.Pico) + "). Todavía no es su límite (" + tj + " °C), pero está trabajando muy caliente.",
                              "The CPU was at " + (int)caliente + " °C or more for over 3 minutes straight (it reached " + Gc(rc.Pico) + "). It is not its limit yet (" + tj + " °C), but it is running very hot."));
                    string top = TopProcesos(false, 3);
                    if (top != "") que.Add(T("Lo que más lo usaba: ", "What used it the most: ") + top);
                    List<string> probar = new List<string>();
                    probar.Add(T("Revisá que el gabinete tenga buena entrada y salida de aire, y que el cooler no esté lleno de polvo.", "Check the case has good air intake and exhaust, and the cooler is not full of dust."));
                    probar.Add(T("Si no estás jugando y pasa igual, fijate qué programa lo usa (arriba).", "If you are not gaming and it still happens, check which program uses it (above)."));
                    Aviso(CAT_CPU, T("CPU: MUY CALIENTE HACE RATO", "CPU: VERY HOT FOR A WHILE"), que, probar, ctx, 1);
                    rc.UltimoAviso = DateTime.Now;
                }

                // Ventilador del procesador parado (solo si la placa lo informa con nombre de CPU y alguna vez giro)
                foreach (Ventilador v in L.Fans)
                {
                    if (v.Nombre.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (v.Rpm > fanCpuMax) fanCpuMax = v.Rpm;
                    Regla rf = R("cpu_fan");
                    int cf = rf.Paso(fanCpuMax > 300 && v.Rpm < 100 && t > 55, 3, 3);
                    if (cf == 1 && rf.PuedeAvisar(15))
                    {
                        List<string> que = new List<string>();
                        que.Add(T("El ventilador del procesador (" + v.Nombre + ") marca " + (int)v.Rpm + " RPM con el procesador a " + Gc(t) + ". Antes giraba a " + (int)fanCpuMax + " RPM.",
                                  "The CPU fan (" + v.Nombre + ") reads " + (int)v.Rpm + " RPM with the CPU at " + Gc(t) + ". It was spinning at " + (int)fanCpuMax + " RPM before."));
                        List<string> probar = new List<string>();
                        probar.Add(T("Apagá la PC y fijate que el cable del ventilador esté bien enchufado en CPU_FAN y que nada lo trabe.", "Turn off the PC and check the fan cable is properly plugged into CPU_FAN and nothing blocks it."));
                        probar.Add(T("Si usás refrigeración líquida, fijate que la bomba funcione.", "If you use liquid cooling, check the pump works."));
                        Aviso(CAT_CPU, T("CPU: VENTILADOR PARADO", "CPU: FAN STOPPED"), que, probar, ctx, 1);
                        rf.UltimoAviso = DateTime.Now;
                    }
                    break;
                }
            }

            // Sin temperatura real (sin PawnIO): INDICIO de que se frena, con lo que dice Windows. No es una temperatura:
            // el firmware le recorta el rendimiento (% Performance Limit) o los MHz caen con carga alta. Puede ser calor o energia.
            if (!L.CpuBajoNivel)
            {
                bool cargado = L.CpuLoad.HasValue && L.CpuLoad.Value >= 50;
                bool sinLimite = !L.CpuLimitePct.HasValue || L.CpuLimitePct.Value >= 100;
                if (L.CpuClockProm.HasValue && L.CpuLoad.HasValue && L.CpuLoad.Value >= 40 && sinLimite && L.CpuClockProm.Value > cpuRefSinTemp) cpuRefSinTemp = L.CpuClockProm.Value;
                bool limitado = L.CpuLimitePct.HasValue && L.CpuLimitePct.Value < 90;
                bool cae = cpuRefSinTemp > 0 && L.CpuClockProm.HasValue && L.CpuClockProm.Value < cpuRefSinTemp * 0.85f;
                Regla ri = R("cpu_indicio");
                int ci = ri.Paso(cargado && (limitado || cae), 8, 5);
                if (ci == 1 && ri.PuedeAvisar(30))
                {
                    List<string> que = new List<string>();
                    if (limitado)
                        que.Add(T("Windows informa que el procesador está limitado al " + (int)L.CpuLimitePct.Value + " % de su rendimiento máximo (lo recorta el propio procesador o la BIOS) con un uso del " + (int)L.CpuLoad.Value + " %.",
                                  "Windows reports the CPU is limited to " + (int)L.CpuLimitePct.Value + " % of its maximum performance (cut by the CPU itself or the BIOS) at " + (int)L.CpuLoad.Value + " % usage."));
                    if (cae)
                        que.Add(T("Con carga alta bajó de " + Ghz(cpuRefSinTemp) + " a " + Ghz(L.CpuClockProm) + " (frecuencia según Windows).",
                                  "Under high load it dropped from " + Ghz(cpuRefSinTemp) + " to " + Ghz(L.CpuClockProm) + " (clock according to Windows)."));
                    que.Add(T("Es un indicio, no una temperatura: puede ser calor o un límite de energía (plan de energía, BIOS, notebook a batería). La temperatura del procesador " + SinDriverTexto() + ".",
                              "This is a hint, not a temperature: it may be heat or a power limit (power plan, BIOS, laptop on battery). The CPU temperature " + SinDriverTexto() + "."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Revisá el plan de energía (abajo) y que la notebook esté enchufada.", "Check the power plan (below) and that the laptop is plugged in."));
                    probar.Add(T("Limpiá el polvo del cooler y fijate que el ventilador del procesador gire.", "Clean the dust from the cooler and check that the CPU fan spins."));
                    Aviso(CAT_CPU, T("CPU: INDICIO DE QUE SE ESTÁ FRENANDO", "CPU: HINT THAT IT IS THROTTLING"), que, probar, ctx, 1);
                    ri.UltimoAviso = DateTime.Now;
                }
            }

            // ---------------- GPU ----------------
            if (L.GpuTemp.HasValue)
            {
                float t = L.GpuTemp.Value, rojo = RojoGpu(L.GpuNvidia);
                Regla r = R("gpu_temp");
                int c = r.Paso(r.Activa ? t >= rojo - 4 : t >= rojo, 3, 5);
                if (r.Activa) r.Pico = M(r.Pico, t);
                if (c == 1 && r.PuedeAvisar(10))
                {
                    List<string> que = new List<string>();
                    que.Add(T("La placa de video llegó a " + Gc(t) + (L.GpuHot.HasValue ? " (hot spot " + Gc(L.GpuHot) + ")" : "") + ".", "The graphics card reached " + Gc(t) + (L.GpuHot.HasValue ? " (hot spot " + Gc(L.GpuHot) + ")" : "") + "."));
                    if (L.GpuNvidia) que.Add(T("En NVIDIA, desde unos 83 °C la placa ya baja sola su velocidad para no calentarse más: perdés FPS.", "On NVIDIA, from about 83 °C the card already lowers its own speed to avoid heating more: you lose FPS."));
                    if (L.GpuFanPct.HasValue) que.Add(T("Sus ventiladores estaban al " + (int)L.GpuFanPct.Value + " %.", "Its fans were at " + (int)L.GpuFanPct.Value + " %."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Limpiá el polvo de la placa (aire comprimido, con los ventiladores trabados para que no giren).", "Clean the dust off the card (compressed air, holding the fans so they do not spin)."));
                    probar.Add(T("Fijate que el gabinete tenga ventiladores que metan aire frío y saquen el caliente.", "Check the case has fans bringing cold air in and pushing hot air out."));
                    if (L.GpuFanPct.HasValue && L.GpuFanPct.Value < 60) probar.Add(T("Los ventiladores no estaban al máximo: una curva más agresiva (MSI Afterburner) la enfría.", "The fans were not at maximum: a more aggressive curve (MSI Afterburner) cools it down."));
                    probar.Add(T("Limitar los FPS (por ejemplo a la frecuencia de tu monitor) baja mucho la temperatura.", "Capping FPS (for example to your monitor's refresh rate) lowers temperature a lot."));
                    Aviso(CAT_GPU, T("GPU: TEMPERATURA ALTA", "GPU: HIGH TEMPERATURE"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now; r.Avisada = true;
                }
                else if (c == -1 && r.Avisada) Volvio(CAT_GPU, T("GPU: volvió a una temperatura normal", "GPU: temperature back to normal"), r, Gc(t));

                // Ventiladores parados con la placa caliente (con poca carga es normal que paren: modo 0 RPM)
                if (L.GpuFanRpm.HasValue)
                {
                    Regla rf = R("gpu_fan");
                    int cf = rf.Paso(L.GpuFanRpm.Value < 100 && t >= 75, 5, 3);
                    if (cf == 1 && rf.PuedeAvisar(15))
                    {
                        List<string> que = new List<string>();
                        que.Add(T("Los ventiladores de la placa de video marcan " + (int)L.GpuFanRpm.Value + " RPM con la placa a " + Gc(t) + ". A esa temperatura tendrían que estar girando.",
                                  "The graphics card fans read " + (int)L.GpuFanRpm.Value + " RPM with the card at " + Gc(t) + ". At that temperature they should be spinning."));
                        List<string> probar = new List<string>();
                        probar.Add(T("Si tenés una curva de ventiladores (MSI Afterburner u otro), revisala o sacala.", "If you have a fan curve (MSI Afterburner or other), check it or remove it."));
                        probar.Add(T("Con la PC apagada, girá los ventiladores con el dedo: si están duros o hacen ruido, están gastados.", "With the PC off, spin the fans with your finger: if they are stiff or noisy, they are worn out."));
                        Aviso(CAT_GPU, T("GPU: VENTILADORES PARADOS", "GPU: FANS STOPPED"), que, probar, ctx, 1);
                        rf.UltimoAviso = DateTime.Now;
                    }
                }
            }
            if (L.GpuHot.HasValue)
            {
                Regla r = R("gpu_hot");
                int c = r.Paso(r.Activa ? L.GpuHot.Value >= 101 : L.GpuHot.Value >= 105, 3, 5);
                if (c == 1 && r.PuedeAvisar(10))
                {
                    List<string> que = new List<string>();
                    que.Add(T("El punto más caliente de la placa (hot spot) llegó a " + Gc(L.GpuHot) + ". Cerca de 110 °C la placa se frena para protegerse.", "The card's hottest point (hot spot) reached " + Gc(L.GpuHot) + ". Near 110 °C the card throttles to protect itself."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Limpiá el polvo y mejorá la ventilación del gabinete.", "Clean the dust and improve case airflow."));
                    probar.Add(T("Si el hot spot está más de 25 °C por encima del núcleo, la pasta térmica o las almohadillas están gastadas: conviene cambiarlas (o llevarla a un técnico).", "If the hot spot is more than 25 °C above the core, the thermal paste or pads are worn out: replace them (or take it to a technician)."));
                    Aviso(CAT_GPU, T("GPU: HOT SPOT MUY ALTO", "GPU: HOT SPOT TOO HIGH"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now;
                }
                // Diferencia hot spot - nucleo (lo normal es 10-20 °C): si es muy grande, la pasta o el montaje estan mal
                if (L.GpuTemp.HasValue && L.GpuLoad.HasValue)
                {
                    float d = L.GpuHot.Value - L.GpuTemp.Value;
                    Regla rd = R("gpu_delta");
                    int cd = rd.Paso(d >= 25 && L.GpuLoad.Value >= 80, 15, 10);
                    if (cd == 1 && rd.PuedeAvisar(60))
                    {
                        List<string> que = new List<string>();
                        que.Add(T("El hot spot está " + (int)Math.Round(d) + " °C por encima del núcleo (" + Gc(L.GpuHot) + " contra " + Gc(L.GpuTemp) + ") con la placa trabajando al " + (int)L.GpuLoad.Value + " %. Lo normal es entre 10 y 20 °C de diferencia.",
                                  "The hot spot is " + (int)Math.Round(d) + " °C above the core (" + Gc(L.GpuHot) + " vs " + Gc(L.GpuTemp) + ") with the card at " + (int)L.GpuLoad.Value + " % load. Normal is a 10 to 20 °C difference."));
                        que.Add(T("Una diferencia así casi siempre es pasta térmica seca o mal repartida, o el disipador que no apoya parejo.", "A gap like that is almost always dried or badly spread thermal paste, or the heatsink not sitting evenly."));
                        List<string> probar = new List<string>();
                        probar.Add(T("Cambiar la pasta térmica de la placa (si no sabés cómo, un técnico lo hace rápido). Si está en garantía, reclamala.", "Replace the card's thermal paste (if you do not know how, a technician does it quickly). If under warranty, claim it."));
                        Aviso(CAT_GPU, T("GPU: HOT SPOT MUY SEPARADO DEL NÚCLEO", "GPU: HOT SPOT FAR ABOVE CORE"), que, probar, ctx, 1);
                        rd.UltimoAviso = DateTime.Now;
                    }
                }
            }
            if (L.GpuMemTemp.HasValue)
            {
                Regla r = R("gpu_mem");
                int c = r.Paso(r.Activa ? L.GpuMemTemp.Value >= 100 : L.GpuMemTemp.Value >= 104, 3, 5);
                if (c == 1 && r.PuedeAvisar(15))
                {
                    List<string> que = new List<string>();
                    que.Add(T("La memoria de la placa de video (VRAM) llegó a " + Gc(L.GpuMemTemp) + ". Cerca de 110 °C se frena y puede dar artefactos o cuelgues.", "The graphics card memory (VRAM) reached " + Gc(L.GpuMemTemp) + ". Near 110 °C it throttles and can cause artifacts or crashes."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Mejorá la ventilación. Si pasa seguido, las almohadillas térmicas de la memoria están gastadas (es común en placas con GDDR6X).", "Improve airflow. If it happens often, the memory thermal pads are worn (common on GDDR6X cards)."));
                    Aviso(CAT_GPU, T("GPU: MEMORIA MUY CALIENTE", "GPU: MEMORY TOO HOT"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now;
                }
            }
            // Conector 12VHPWR / 12V-2x6: cada pin aguanta 9.5 A. Si uno lleva mas, el conector se puede derretir.
            if (L.GpuPinMaxA.HasValue)
            {
                Regla r = R("gpu_conector");
                int c = r.Paso(L.GpuPinMaxA.Value >= 9.5f, 2, 5);
                if (c == 1 && r.PuedeAvisar(10))
                {
                    List<string> que = new List<string>();
                    que.Add(T("Un pin del conector de energía de la placa (" + L.GpuPinNombre + ") lleva " + L.GpuPinMaxA.Value.ToString("0.0", CultureInfo.InvariantCulture) + " A. El máximo seguro es 9.5 A por pin: un conector mal enchufado o dañado reparte mal la corriente y se puede derretir.",
                              "One pin of the card's power connector (" + L.GpuPinNombre + ") carries " + L.GpuPinMaxA.Value.ToString("0.0", CultureInfo.InvariantCulture) + " A. The safe maximum is 9.5 A per pin: a badly seated or damaged connector spreads current unevenly and can melt."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Apagá la PC y desenchufala. Sacá y volvé a meter el conector hasta el fondo (que haga clic), sin doblar el cable cerca del conector.", "Turn off and unplug the PC. Remove and re-insert the connector fully (until it clicks), without bending the cable near the connector."));
                    probar.Add(T("Fijate que el conector no esté marrón o derretido. Si lo está, no la uses y reclamá garantía.", "Check the connector is not brown or melted. If it is, do not use it and claim warranty."));
                    Aviso(CAT_GPU, T("GPU: CONECTOR DE ENERGÍA SOBRECARGADO", "GPU: POWER CONNECTOR OVERLOADED"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now;
                }
            }
            // VRAM llena con la placa trabajando: tirones aunque los FPS promedio den bien
            if (!L.GpuIntegrada && L.GpuVramUsada.HasValue && L.GpuVramTotal.HasValue && L.GpuLoad.HasValue) // una integrada usa la RAM del sistema a proposito: no es "llena"
            {
                Regla r = R("gpu_vram");
                int c = r.Paso(L.GpuVramUsada.Value >= L.GpuVramTotal.Value * 0.97f && L.GpuLoad.Value >= 50, 10, 10);
                if (c == 1 && r.PuedeAvisar(30))
                {
                    List<string> que = new List<string>();
                    que.Add(T("La memoria de la placa de video está llena: " + GB(L.GpuVramUsada.Value) + " de " + GB(L.GpuVramTotal.Value) + ". Cuando se llena, el juego tiene que usar la RAM (mucho más lenta) y aparecen tirones y texturas que cargan tarde.",
                              "The graphics card memory is full: " + GB(L.GpuVramUsada.Value) + " of " + GB(L.GpuVramTotal.Value) + ". When it fills up, the game has to use system RAM (much slower) and you get stutters and late-loading textures."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Bajá la calidad de texturas un nivel (es lo que más VRAM usa). También ayuda bajar la resolución o apagar el trazado de rayos.", "Lower texture quality one step (it uses the most VRAM). Lowering resolution or turning off ray tracing also helps."));
                    probar.Add(T("Cerrá el navegador y Discord mientras jugás: también usan VRAM.", "Close the browser and Discord while gaming: they also use VRAM."));
                    Aviso(CAT_GPU, T("GPU: VRAM LLENA", "GPU: VRAM FULL"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now;
                }
            }

            // ---------------- RAM ----------------
            if (L.RamUso >= 0)
            {
                Regla r = R("ram_llena");
                int c = r.Paso(r.Activa ? L.RamUso >= 88 : L.RamUso >= 93, 3, 5);
                if (r.Activa) r.Pico = M(r.Pico, L.RamUso);
                if (c == 1 && r.PuedeAvisar(10))
                {
                    List<string> que = new List<string>();
                    que.Add(T("La RAM está usada al " + (int)L.RamUso + " % (" + L.RamUsadaGB.ToString("0.0", CultureInfo.InvariantCulture) + " de " + L.RamTotalGB.ToString("0", CultureInfo.InvariantCulture) + " GB). Windows empieza a usar el disco como memoria y todo se pone más lento, con tirones en juegos.",
                              "RAM is " + (int)L.RamUso + " % used (" + L.RamUsadaGB.ToString("0.0", CultureInfo.InvariantCulture) + " of " + L.RamTotalGB.ToString("0", CultureInfo.InvariantCulture) + " GB). Windows starts using the drive as memory and everything slows down, with stutters in games."));
                    string top = TopProcesos(true, 4);
                    if (top != "") que.Add(T("Lo que más ocupa: ", "What uses the most: ") + top);
                    List<string> probar = new List<string>();
                    probar.Add(T("Cerrá lo que no uses (sobre todo pestañas del navegador). Si uno solo ocupa muchísimo, puede tener una pérdida de memoria: reinicialo.", "Close what you do not use (especially browser tabs). If one alone uses a huge amount, it may have a memory leak: restart it."));
                    if (L.RamTotalGB < 15) probar.Add(T("Con " + L.RamTotalGB.ToString("0", CultureInfo.InvariantCulture) + " GB hoy se queda corta para jugar: 16 GB es lo mínimo recomendado, 32 GB lo cómodo.", "With " + L.RamTotalGB.ToString("0", CultureInfo.InvariantCulture) + " GB it falls short for gaming today: 16 GB is the recommended minimum, 32 GB is comfortable."));
                    Aviso(CAT_RAM, T("RAM: CASI LLENA", "RAM: ALMOST FULL"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now; r.Avisada = true;
                }
                else if (c == -1 && r.Avisada) Volvio(CAT_RAM, T("RAM: volvió a lo normal", "RAM: back to normal"), r, (int)L.RamUso + " %");
            }
            if (L.DimmTemp.HasValue)
            {
                Regla r = R("ram_temp");
                int c = r.Paso(L.DimmTemp.Value >= 82, 5, 5);
                if (c == 1 && r.PuedeAvisar(30))
                {
                    List<string> que = new List<string>();
                    que.Add(T("Los módulos de RAM llegaron a " + Gc(L.DimmTemp) + ". Por encima de 85 °C empiezan los errores (cuelgues, juegos que se cierran).", "The RAM modules reached " + Gc(L.DimmTemp) + ". Above 85 °C errors start (freezes, games closing)."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Que les llegue aire: un ventilador frontal o superior ayuda. Con XMP/EXPO muy exigente, bajar un poco el voltaje o la velocidad también.", "Give them airflow: a front or top fan helps. With a very demanding XMP/EXPO, lowering voltage or speed a bit helps too."));
                    Aviso(CAT_RAM, T("RAM: MÓDULOS MUY CALIENTES", "RAM: MODULES TOO HOT"), que, probar, ctx, 1);
                    r.UltimoAviso = DateTime.Now;
                }
            }
            // Configuracion de la RAM (XMP apagado, single channel): se avisa una vez, y otra vez solo si cambia
            if (!ramConfigAvisada && f.Ram.Count > 0)
            {
                ramConfigAvisada = true;
                string estado = f.RamXmpNivel + "|" + f.RamCanalNivel + "|" + f.Ram.Count;
                if (App.Ajuste("pc_ram_estado") != estado)
                {
                    App.GuardarAjuste("pc_ram_estado", estado);
                    List<string> que = new List<string>();
                    if (f.RamXmpNivel == Nivel.Amarillo) que.Add(f.RamXmpTexto);
                    if (f.RamCanalNivel == Nivel.Amarillo) que.Add(f.RamCanalTexto);
                    if (que.Count > 0)
                    {
                        List<string> probar = new List<string>();
                        if (f.RamXmpNivel == Nivel.Amarillo) probar.Add(T("XMP/EXPO: entrá a la BIOS (Supr o F2 al prender), buscá XMP, EXPO o DOCP, ponelo en \"Profile 1\" y guardá. Si después hay pantallazos, volvé a apagarlo.", "XMP/EXPO: enter the BIOS (Del or F2 at boot), find XMP, EXPO or DOCP, set it to \"Profile 1\" and save. If you get blue screens afterwards, turn it off again."));
                        if (f.RamCanalNivel == Nivel.Amarillo) probar.Add(T("Dual channel: usá 2 módulos iguales en las ranuras que dice el manual de la placa (casi siempre la 2 y la 4 contando desde el procesador).", "Dual channel: use 2 matching modules in the slots the motherboard manual says (almost always 2 and 4 counting from the CPU)."));
                        Aviso(CAT_RAM, T("RAM: CONFIGURACIÓN QUE TE QUITA RENDIMIENTO", "RAM: SETUP THAT COSTS PERFORMANCE"), que, probar, "", 1);
                    }
                }
            }

            // ---------------- Discos ----------------
            foreach (Disco d in L.Discos)
            {
                if (d.Temp.HasValue)
                {
                    float aviso = AvisoDisco(d);
                    Regla r = R("disco_t|" + d.Nombre);
                    int c = r.Paso(r.Activa ? d.Temp.Value >= aviso - 3 : d.Temp.Value >= aviso, 6, 10);
                    if (c == 1 && r.PuedeAvisar(30))
                    {
                        List<string> que = new List<string>();
                        que.Add(T(d.Nombre + " llegó a " + Gc(d.Temp) + (d.TempAviso.HasValue ? " y su fabricante avisa desde " + Gc(d.TempAviso) : "") + ". A esa temperatura el disco se frena solo (lee y escribe más lento: cargas largas y tirones).",
                                  d.Nombre + " reached " + Gc(d.Temp) + (d.TempAviso.HasValue ? " and its maker warns from " + Gc(d.TempAviso) : "") + ". At that temperature the drive throttles itself (slower reads and writes: long loads and stutters)."));
                        List<string> probar = new List<string>();
                        probar.Add(T("Si es un SSD M.2, ponele un disipador (muchas placas traen uno) y que no quede justo abajo de la placa de video.", "If it is an M.2 SSD, add a heatsink (many motherboards include one) and avoid having it right under the graphics card."));
                        Aviso(CAT_DISCOS, T("DISCO: MUY CALIENTE", "DRIVE: TOO HOT"), que, probar, "", 1);
                        r.UltimoAviso = DateTime.Now;
                    }
                }
                if (d.Vida.HasValue)
                {
                    Nivel nv = NivelVida(d.Vida);
                    string k = "pc_vida_" + Regex.Replace(d.Nombre, "[^A-Za-z0-9]", "");
                    string antes = App.Ajuste(k);
                    if (nv != Nivel.Verde && antes != nv.ToString())
                    {
                        App.GuardarAjuste(k, nv.ToString());
                        List<string> que = new List<string>();
                        que.Add(T("A " + d.Nombre + " le queda " + (int)d.Vida.Value + " % de vida útil" + (d.EscritoGB.HasValue ? " (lleva " + Tb(d.EscritoGB.Value) + " escritos)" : "") + ".",
                                  d.Nombre + " has " + (int)d.Vida.Value + " % of its life left" + (d.EscritoGB.HasValue ? " (" + Tb(d.EscritoGB.Value) + " written so far)" : "") + "."));
                        List<string> probar = new List<string>();
                        probar.Add(nv == Nivel.Rojo ? T("Hacé una copia de tus archivos importantes ya y pensá en reemplazarlo pronto.", "Back up your important files now and plan to replace it soon.")
                                                    : T("Todavía anda, pero tené copia de lo importante.", "It still works, but keep a backup of what matters."));
                        Aviso(CAT_DISCOS, T("DISCO: POCA VIDA ÚTIL", "DRIVE: LOW REMAINING LIFE"), que, probar, "", 1);
                    }
                    else if (nv == Nivel.Verde && antes != null && antes != "Verde") App.GuardarAjuste(k, "Verde");
                }
            }
            // Salud que informa Windows (SMART): se avisa una vez por cada cambio, igual que la vida util
            foreach (DiscoFisico d in f.Fisicos)
            {
                if (d.NivelSalud == Nivel.Gris) continue;
                string k = "pc_salud_" + Regex.Replace(d.Nombre, "[^A-Za-z0-9]", "");
                string antes = App.Ajuste(k);
                if (d.NivelSalud == Nivel.Verde) { if (antes != null && antes != "Verde") App.GuardarAjuste(k, "Verde"); continue; }
                if (antes == d.NivelSalud.ToString()) continue;
                App.GuardarAjuste(k, d.NivelSalud.ToString());
                List<string> que = new List<string>();
                que.Add(T("Windows marca a " + d.Nombre + " (" + d.Tipo + ") como \"" + d.Salud + "\": el propio disco (SMART) informa errores o desgaste que anticipan una falla.",
                          "Windows flags " + d.Nombre + " (" + d.Tipo + ") as \"" + d.Salud + "\": the drive itself (SMART) reports errors or wear that anticipate a failure."));
                List<string> probar = new List<string>();
                probar.Add(T("Hacé una copia de tus archivos importantes ya.", "Back up your important files now."));
                probar.Add(T("Configuración > Sistema > Almacenamiento > Discos y volúmenes > Propiedades muestra el detalle.", "Settings > System > Storage > Disks & volumes > Properties shows the details."));
                Aviso(CAT_DISCOS, T("DISCO: WINDOWS DETECTA FALLAS (SMART)", "DRIVE: WINDOWS DETECTS FAILURES (SMART)"), que, probar, "", 1);
            }

            // ---------------- General: plan de ahorro de energia o notebook a bateria mientras jugas ----------------
            if (L.Juego != "" && L.Juego != juegoAnterior)
            {
                if (!planAvisado && f.PlanGuid == PlanAhorro)
                {
                    planAvisado = true;
                    List<string> que = new List<string>();
                    que.Add(T("Estás jugando con el plan de energía \"" + f.Plan + "\" (ahorro): el procesador no llega a su velocidad máxima y perdés FPS.", "You are gaming with the \"" + f.Plan + "\" (power saver) power plan: the CPU does not reach its full speed and you lose FPS."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Panel de control > Opciones de energía > Equilibrado o Alto rendimiento.", "Control Panel > Power Options > Balanced or High performance."));
                    Aviso(CAT_CPU, T("CPU: PLAN DE ENERGÍA EN AHORRO", "CPU: POWER SAVER PLAN"), que, probar, "", 1);
                }
                if (!bateriaAvisada && L.Bateria >= 0 && !L.Enchufada)
                {
                    bateriaAvisada = true;
                    List<string> que = new List<string>();
                    que.Add(T("Estás jugando a batería (" + L.Bateria + " %): la notebook limita mucho el procesador y la placa de video para durar más.", "You are gaming on battery (" + L.Bateria + " %): the laptop heavily limits the CPU and graphics card to last longer."));
                    List<string> probar = new List<string>();
                    probar.Add(T("Enchufala para jugar.", "Plug it in to play."));
                    Aviso(CAT_CPU, T("CPU: JUGANDO A BATERÍA", "CPU: GAMING ON BATTERY"), que, probar, "", 1);
                }
            }
            juegoAnterior = L.Juego;
            RevisarBios(f);
        }

        // Intel Core de 13.ª/14.ª gen de escritorio (i5-13500 en adelante, i7, i9) con BIOS anterior al microcodigo 0x12B
        // (septiembre 2024): reciben demasiado voltaje y se degradan con el tiempo. Es un problema conocido y real.
        public static bool IntelConBiosVieja(InfoFija f)
        {
            if (f == null || !f.BiosFecha.HasValue || string.IsNullOrEmpty(f.CpuNombre)) return false;
            if (!Regex.IsMatch(f.CpuNombre, @"\bi(?:[79]-1[34]\d{3}|5-1[34][56]\d{2})", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(f.CpuNombre, @"-1[34]\d{3}(?:H|HX|U|P)\b", RegexOptions.IgnoreCase)) return false; // notebooks: no les pasa
            return f.BiosFecha.Value < new DateTime(2024, 9, 15);
        }

        bool biosRevisada;
        void RevisarBios(InfoFija f)
        {
            if (biosRevisada || !f.BiosFecha.HasValue) return;
            biosRevisada = true;
            if (!IntelConBiosVieja(f) || App.Ajuste("pc_intel_bios") == f.BiosVersion) return;
            App.GuardarAjuste("pc_intel_bios", f.BiosVersion);
            string fecha = f.BiosFecha.Value.ToString(Idioma == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy");
            List<string> que = new List<string>();
            que.Add(T("Tu procesador (" + f.CpuNombre + ") es de los Intel de 13.ª/14.ª generación que tienen un problema conocido: con BIOS viejas reciben más voltaje del que deben y se van dañando con el tiempo (cuelgues, juegos que se cierran solos, pantallazos azules).",
                      "Your CPU (" + f.CpuNombre + ") is one of the 13th/14th gen Intel chips with a known problem: with old BIOSes they get more voltage than they should and degrade over time (freezes, games closing by themselves, blue screens)."));
            que.Add(T("Intel lo arregló con el microcódigo 0x12B, que viene en las BIOS de fines de 2024 en adelante. Tu BIOS (" + f.BiosVersion + ") es del " + fecha + ".",
                      "Intel fixed it with microcode 0x12B, included in BIOSes from late 2024 onwards. Your BIOS (" + f.BiosVersion + ") is from " + fecha + "."));
            List<string> probar = new List<string>();
            probar.Add(T("Actualizá la BIOS desde la página de soporte de tu placa madre" + (f.PlacaModelo != "" ? " (" + (f.PlacaFab + " " + f.PlacaModelo).Trim() + ")" : "") + ". Es lo más importante que podés hacer por ese procesador.",
                         "Update the BIOS from your motherboard's support page" + (f.PlacaModelo != "" ? " (" + (f.PlacaFab + " " + f.PlacaModelo).Trim() + ")" : "") + ". It is the most important thing you can do for that CPU."));
            probar.Add(T("Si ya tenés cuelgues seguidos aun con la BIOS nueva, Intel extendió la garantía de estos procesadores: reclamala.", "If you already get frequent crashes even with the new BIOS, Intel extended the warranty for these CPUs: claim it."));
            Aviso(CAT_CPU, T("CPU: TU INTEL NECESITA UNA BIOS MÁS NUEVA", "CPU: YOUR INTEL NEEDS A NEWER BIOS"), que, probar, "", 1);
        }

        static string GB(float mb) { return (mb / 1024f).ToString("0.0", CultureInfo.InvariantCulture) + " GB"; }
        public static string Tb(float gb) { return gb >= 1000 ? (gb / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " TB" : gb.ToString("0", CultureInfo.InvariantCulture) + " GB"; }

        // Cuando algo vuelve a lo normal se anota (cuanto duro y el pico), pero no suma al contador
        void Volvio(int cat, string titulo, Regla r, string ahora)
        {
            int seg = (int)(DateTime.Now - r.Desde).TotalSeconds;
            string dur = seg < 90 ? seg + " s" : (seg / 60) + " min";
            List<string> que = new List<string>();
            que.Add(T("Ahora está en " + ahora + ". Estuvo en rojo " + dur + (Foto.Hay(r.Pico) ? ", con un pico de " + ((int)Math.Round(r.Pico)) + (titulo.StartsWith("RAM") ? " %" : " °C") : "") + ".",
                      "It is now at " + ahora + ". It was in red for " + dur + (Foto.Hay(r.Pico) ? ", peaking at " + ((int)Math.Round(r.Pico)) + (titulo.StartsWith("RAM") ? " %" : " °C") : "") + "."));
            Aviso(cat, titulo, que, null, "", 0);
        }

        // Arma el aviso, lo guarda en su pestaña (si esta prendida) y una copia en registro_pc.txt
        void Aviso(int cat, string titulo, List<string> que, List<string> probar, string ctx, int suma)
        {
            PcPestana p = Pestanas[cat];
            if (!p.Activo) return;
            List<string> L = new List<string>();
            L.Add(titulo + ": " + Fecha(DateTime.Now));
            L.Add("");
            if (que != null && que.Count > 0)
            {
                if (suma > 0) L.Add(T("QUÉ PASA:", "WHAT IS HAPPENING:"));
                foreach (string x in que) L.Add("  " + x);
            }
            if (probar != null && probar.Count > 0)
            {
                L.Add("");
                L.Add(T("QUÉ PROBAR:", "WHAT TO TRY:"));
                foreach (string x in probar) L.Add("  - " + x);
            }
            if (!string.IsNullOrEmpty(ctx))
            {
                L.Add("");
                L.Add(T("EN ESE MOMENTO:", "AT THAT MOMENT:"));
                L.Add("  " + ctx);
            }
            string texto = string.Join("\r\n", L.ToArray()).TrimEnd();
            foreach (string x in L) { p.Log(x); Log(x); }
            p.Log(""); Log("");
            p.AgregarAviso(texto, suma);
        }

        // Los programas que mas usan el procesador (midiendo 1 s) o la RAM, sumando los que tienen el mismo nombre
        static string TopProcesos(bool ram, int n)
        {
            Dictionary<string, double> uso = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int yo = Process.GetCurrentProcess().Id;
            try
            {
                if (ram)
                {
                    foreach (Process p in Process.GetProcesses())
                    {
                        try { if (p.Id > 4 && p.Id != yo) Sumar(uso, p.ProcessName, p.WorkingSet64 / 1048576.0); } catch { }
                        finally { p.Dispose(); }
                    }
                }
                else
                {
                    Dictionary<int, KeyValuePair<string, TimeSpan>> antes = new Dictionary<int, KeyValuePair<string, TimeSpan>>();
                    foreach (Process p in Process.GetProcesses())
                    {
                        try { if (p.Id > 4 && p.Id != yo) antes[p.Id] = new KeyValuePair<string, TimeSpan>(p.ProcessName, p.TotalProcessorTime); } catch { }
                        finally { p.Dispose(); }
                    }
                    Thread.Sleep(1000);
                    foreach (Process p in Process.GetProcesses())
                    {
                        try
                        {
                            KeyValuePair<string, TimeSpan> a;
                            if (antes.TryGetValue(p.Id, out a) && a.Key == p.ProcessName)
                                Sumar(uso, a.Key, (p.TotalProcessorTime - a.Value).TotalMilliseconds / 10.0 / Environment.ProcessorCount);
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
            }
            catch { return ""; }
            List<KeyValuePair<string, double>> l = new List<KeyValuePair<string, double>>(uso);
            l.Sort(delegate (KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            List<string> r = new List<string>();
            foreach (KeyValuePair<string, double> x in l)
            {
                if (r.Count >= n) break;
                if (x.Key == "Idle" || x.Key == "System" || x.Key == "Memory Compression" || x.Key == "Registry") continue;
                if (ram) { if (x.Value < 150) break; r.Add(x.Key + " (" + (x.Value >= 1024 ? (x.Value / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : ((int)x.Value) + " MB") + ")"); }
                else { if (x.Value < 3) break; r.Add(x.Key + " (" + (int)Math.Round(x.Value) + " %)"); }
            }
            return string.Join(", ", r.ToArray());
        }

        static void Sumar(Dictionary<string, double> d, string k, double v)
        {
            double a; d.TryGetValue(k, out a); d[k] = a + v;
        }

        // =====================================================================================================
        // PawnIO: el driver que hace falta para la temperatura del procesador y los ventiladores de la placa
        // =====================================================================================================
        public static volatile Version PawnVersion;
        // Pulso necesita administrador para que la libreria lea el CPU (sin admin da todo 0). El manifiesto lo pide, pero se verifica de verdad.
        public static readonly bool EsAdmin = LeerAdmin();
        static bool LeerAdmin() { try { using (WindowsIdentity id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { return true; } }
        // PawnIO esta instalado y con administrador, pero la libreria sigue sin leer el CPU (lo ve Vuelta, que reabre los sensores)
        public static volatile bool PawnSinDatos;
        public static string PawnVer { get { Version v = PawnVersion; return v == null ? "" : v.Revision > 0 ? v.ToString() : v.ToString(3); } }   // 2.2.0.0 -> 2.2.0
        // Por que no hay temperatura ni vatios del CPU: lo verdadero segun el caso (no siempre es "falta PawnIO")
        public static string SinDriverTexto()
        {
            if (!EsAdmin) return T("requiere administrador (Pulso no está elevado)", "requires administrator (Pulso is not elevated)");
            if (PawnOk && PawnSinDatos) return T("PawnIO v" + PawnVer + " instalado, pero no entregó datos del CPU (reintentando)", "PawnIO v" + PawnVer + " is installed but gave no CPU data (retrying)");
            return T("requiere driver (PawnIO, opcional)", "requires a driver (PawnIO, optional)");
        }
        public static string SinDriverCorto()
        {
            if (!EsAdmin) return T("requiere administrador", "needs administrator");
            if (PawnOk && PawnSinDatos) return T("sin datos (PawnIO)", "no data (PawnIO)");
            return T("requiere driver", "needs a driver");
        }
        public static bool PawnOk { get { Version v = PawnVersion; return v != null && v >= new Version(2, 0); } }
        public static bool PawnViejo { get { Version v = PawnVersion; return v != null && v < new Version(2, 0); } }
        // PawnIO lo instala el usuario desde su origen oficial: Pulso NO baja ni ejecuta el instalador. Hasta la 1.0.3 lo bajaba
        // de GitHub y lo abria (un .exe sin firma, con administrador, que descarga y lanza el instalador de un driver de kernel):
        // Defender y SmartScreen saltaban siempre en ese momento. Correr "winget install" desde Pulso tampoco sirve: seria el
        // mismo patron (Pulso lanzando el instalador de un driver). El usuario lo instala y Pulso lo detecta solo (Vuelta, cada 2 min).
        public const string WebPawn = "https://pawnio.eu/";
        public const string WingetPawn = "winget install --id namazso.PawnIO -e";

        // Solo lee el registro (version instalada); nunca instala nada
        public static void LeerPawn()
        {
            Version v = null;
            foreach (RegistryView rv in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, rv))
                    using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                    {
                        if (k == null) continue;
                        Version x;
                        if (Version.TryParse(k.GetValue("DisplayVersion") as string, out x)) { v = x; break; }
                    }
                }
                catch { }
            }
            PawnVersion = v;
        }

        // Boton "Cómo instalar PawnIO" / "Cómo actualizar PawnIO" de la pestaña CPU: solo explica y abre la pagina oficial
        public void GuiaPawn()
        {
            DialogResult r = MessageBox.Show(
                T("PawnIO es opcional. Sin él, Pulso muestra todo lo demás y, donde Windows la da sin drivers, una temperatura aproximada del procesador. Con él se ven la temperatura y los vatios reales del procesador y los ventiladores de la placa madre.\r\n\r\n" +
                  "Pulso no lo baja ni lo instala por su cuenta: lo instalás vos desde su origen oficial, de una de estas dos formas:\r\n" +
                  "   1. Sitio oficial: pawnio.eu (instalador firmado por su autor, namazso).\r\n" +
                  "   2. En una terminal: " + WingetPawn + "\r\n\r\n" +
                  "Es normal que el antivirus o SmartScreen avisen al instalarlo: PawnIO es un driver que lee los sensores del procesador a bajo nivel y ese tipo de programa dispara alertas genéricas. Es de código abierto, está firmado y es el mismo que usan LibreHardwareMonitor y FanControl. Se desinstala desde Configuración > Aplicaciones.\r\n\r\n" +
                  "Sí: abrir el sitio oficial.   No: copiar el comando de winget.   Cancelar: nada.\r\n" +
                  "Cuando termine de instalarse, Pulso lo detecta solo (en 2 minutos como mucho).",
                  "PawnIO is optional. Without it, Pulso shows everything else and, where Windows provides it without drivers, an approximate CPU temperature. With it you get the real CPU temperature and watts and the motherboard fans.\r\n\r\n" +
                  "Pulso does not download or install it on its own: you install it from its official source, in one of these two ways:\r\n" +
                  "   1. Official site: pawnio.eu (installer signed by its author, namazso).\r\n" +
                  "   2. In a terminal: " + WingetPawn + "\r\n\r\n" +
                  "It is normal for your antivirus or SmartScreen to warn while installing it: PawnIO is a driver that reads CPU sensors at a low level, and that kind of program triggers generic alerts. It is open source, signed, and the same one LibreHardwareMonitor and FanControl use. It can be uninstalled from Settings > Apps.\r\n\r\n" +
                  "Yes: open the official site.   No: copy the winget command.   Cancel: nothing.\r\n" +
                  "Once it is installed, Pulso detects it by itself (within 2 minutes)."),
                Pulso.Nombre, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) { try { Process.Start(WebPawn); } catch { } }
            else if (r == DialogResult.No) { try { Clipboard.SetText(WingetPawn); } catch { } }
        }
    }

    // ---------- Una pestaña de hardware (CPU, GPU, Memoria o Discos) ----------
    // Tiene su propio historial, registro y contador (los de la clase base). Lo que mide sale de MonitorPC.
    public class PcPestana : Modulo
    {
        public readonly MonitorPC Pc;
        public readonly int Cat;

        public PcPestana(MonitorPC pc, int cat, string clave, string es, string en, string nombreEs, string nombreEn, string vacioEs, string vacioEn)
        {
            Pc = pc; Cat = cat;
            Clave = clave; PestEs = es; PestEn = en; TipEs = es; TipEn = en; NombreEs = nombreEs; NombreEn = nombreEn;
            // Los mismos archivos que tenian las categorias de la pestaña PC (hasta la 1.0.3): el historial se conserva
            string arch = clave == "memoria" ? "ram" : clave;
            RegistroArch = "registro_pc_" + arch + ".txt"; HistArch = "historial_pc_" + arch + ".txt";
            ContEs = "Avisos"; ContEn = "Alerts";
            VacioEs = vacioEs; VacioEn = vacioEn;
            ApagadoEs = "Esta pestaña está apagada: no mide ni avisa nada. Prendela con la casilla \"Activado\".";
            ApagadoEn = "This tab is off: it does not measure or alert anything. Turn it on with the \"Enabled\" box.";
        }

        protected override void Empezar() { Pc.Recalcular(); }
        protected override void Terminar() { Pc.Recalcular(); }
        public override Control PanelExtra() { return new PcVista(Pc, this); }
    }
}
