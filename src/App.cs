// Pulso: monitoreo integral de la PC desde la bandeja (al lado del reloj): red, USB, rendimiento, errores, hardware y grabación de sesiones.
// Cada parte (Red y USB) es un "modulo" que se puede prender o apagar; apagado no gasta nada.
// La ventana solo existe mientras esta abierta: al cerrarla o minimizarla se destruye y se libera la memoria,
// y lo detectado queda guardado en el historial.
// Se compila para .NET Framework 4.8 (viene con Windows 10 y 11) y en C# 5, asi tambien compila con el csc de Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Pulso")]
[assembly: AssemblyProduct("Pulso")]
[assembly: AssemblyCompany("011Xuca")]
[assembly: AssemblyCopyright("011Xuca")]
[assembly: AssemblyVersion(Xuca.Pulso.Version + ".0")]
[assembly: AssemblyFileVersion(Xuca.Pulso.Version + ".0")]

namespace Xuca
{
    // ---------- Lo que comparten Red y USB ----------
    public abstract class Modulo
    {
        public string Clave, PestEs, PestEn, TipEs, TipEn, NombreEs, NombreEn, RegistroArch, HistArch, ContEs, ContEn, VacioEs, VacioEn, ApagadoEs, ApagadoEn;
        public Pulso App;
        public bool Activo;
        public DateTime Desde = DateTime.Now;

        public static string Idioma = "es";
        public static string T(string es, string en) { return Idioma == "en" ? en : es; }
        public static string Fecha(DateTime d) { return d.ToString(Idioma == "en" ? "MM/dd HH:mm:ss" : "dd/MM HH:mm:ss"); }

        protected readonly object Cerrojo = new object();
        internal readonly List<KeyValuePair<int, string>> Bloques = new List<KeyValuePair<int, string>>();
        internal int Cuenta;
        internal string LogF, HistF;

        protected abstract void Empezar();
        protected abstract void Terminar();

        // Un modulo puede mostrar un panel propio arriba del cuadro de texto (por ej. los sensores de PC).
        // Se pide una sola vez por ventana abierta; null = no hay nada extra que mostrar.
        public virtual Control PanelExtra() { return null; }

        public string Nombre { get { return T(NombreEs, NombreEn); } }

        public void Prender(bool si)
        {
            if (si == Activo) return;
            Activo = si;
            if (si)
            {
                Desde = DateTime.Now;
                Log("=== " + Nombre + T(" prendido", " turned on") + " ===");
                try { Empezar(); } catch (Exception ex) { Log("Error: " + ex.Message); }
            }
            else
            {
                try { Terminar(); } catch { }
                Log("=== " + Nombre + T(" apagado", " turned off") + " ===");
            }
        }

        public void Log(string t)
        {
            lock (Cerrojo)
            {
                try { File.AppendAllText(LogF, Fecha(DateTime.Now) + "  " + t + "\r\n", Encoding.UTF8); } catch { }
            }
        }

        // Historial: bloques "@@N" + texto (N = cuanto suma al contador). Se guardan los ultimos 300.
        internal void CargarHistorial()
        {
            try
            {
                if (!File.Exists(HistF)) return;
                string[] partes = System.Text.RegularExpressions.Regex.Split(File.ReadAllText(HistF, Encoding.UTF8), "(?m)^@@(\\d+)\\r?\\n");
                for (int i = 1; i + 1 < partes.Length; i += 2)
                {
                    string t = partes[i + 1].TrimEnd();
                    int n; int.TryParse(partes[i], out n);
                    if (t.Length > 0) { Bloques.Add(new KeyValuePair<int, string>(n, t)); Cuenta += n; }
                }
                if (Bloques.Count > 300)
                {
                    Bloques.RemoveRange(0, Bloques.Count - 300);
                    StringBuilder sb = new StringBuilder();
                    foreach (KeyValuePair<int, string> b in Bloques) sb.Append("@@" + b.Key + "\r\n" + b.Value + "\r\n");
                    File.WriteAllText(HistF, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        // Guarda un aviso y lo muestra si la ventana esta abierta (no la abre). Se puede llamar desde cualquier hilo.
        protected void Agregar(string texto, int suma)
        {
            lock (Cerrojo)
            {
                Bloques.Add(new KeyValuePair<int, string>(suma, texto));
                if (Bloques.Count > 300) Bloques.RemoveAt(0);
                Cuenta += suma;
                try { File.AppendAllText(HistF, "@@" + suma + "\r\n" + texto + "\r\n", Encoding.UTF8); } catch { }
            }
            App.Cambio(this);
        }

        // Para que otra parte de la app (la grabacion de sesion) deje un aviso en esta pestaña
        internal void AgregarAviso(string texto, int suma) { Agregar(texto, suma); }

        // La primera linea del ultimo aviso (su titulo), o null
        internal string UltimoTitulo()
        {
            lock (Cerrojo)
            {
                if (Bloques.Count == 0) return null;
                string t = Bloques[Bloques.Count - 1].Value;
                int i = t.IndexOf('\n');
                return (i >= 0 ? t.Substring(0, i) : t).Trim();
            }
        }

        // Aviso que se ve en la ventana pero no se guarda
        protected void Avisar(string texto) { App.Aviso(this, texto); }

        // Virtual: un modulo con sub-categorias (PC) la pisa para limpiar solo la categoria que se esta viendo.
        internal virtual void Limpiar()
        {
            lock (Cerrojo)
            {
                Bloques.Clear();
                Cuenta = 0;
                try { File.Delete(HistF); } catch { }
            }
        }

        internal virtual string Texto()
        {
            StringBuilder sb = new StringBuilder();
            lock (Cerrojo)
            {
                string sep = "\r\n\r\n" + new string('-', 70) + "\r\n\r\n";
                for (int i = Bloques.Count - 1; i >= 0; i--)
                {
                    if (sb.Length > 0) sb.Append(sep);
                    sb.Append(Bloques[i].Value);
                }
            }
            if (sb.Length > 0) return sb.ToString();
            return Activo ? T(VacioEs, VacioEn) : T(ApagadoEs, ApagadoEn);
        }

        internal virtual int Contador() { lock (Cerrojo) return Cuenta; }

        // Pasa trabajo al hilo de la ventana
        protected void EnUi(MethodInvoker m) { App.EnUi(m); }
    }

    // Pestaña que en este Windows no esta disponible: no vigila nada, solo explica por que
    public class ModuloNoDisponible : Modulo
    {
        readonly string queEs, queEn;
        public ModuloNoDisponible(string clave, string pestana, string queEs, string queEn)
        {
            Clave = clave; PestEs = PestEn = TipEs = TipEn = NombreEs = NombreEn = pestana;
            RegistroArch = "registro_" + clave + "_no_disponible.txt"; HistArch = "historial_" + clave + "_no_disponible.txt";
            ContEs = "Avisos"; ContEn = "Alerts"; VacioEs = ApagadoEs = VacioEn = ApagadoEn = "";
            this.queEs = queEs; this.queEn = queEn;
        }
        protected override void Empezar() { }
        protected override void Terminar() { }
        internal override string Texto() { return Pulso.SoloWin10(queEs, queEn); }
    }

    // Menu de Ajustes con la misma cara que la ventana: fondo de tarjeta, borde fino, seleccion verde suave con esquinas
    // redondeadas y la tilde en verde (sin el cuadrito con borde que dibuja Windows)
    class MenuAjustes : ToolStripProfessionalRenderer
    {
        readonly Color bg, linea, encima, acento, texto;
        public MenuAjustes(bool oscuro) : base(new ProfessionalColorTable())
        {
            bg = oscuro ? Color.FromArgb(28, 32, 39) : Color.White;
            linea = oscuro ? Color.FromArgb(46, 52, 64) : Color.FromArgb(209, 213, 219);
            encima = oscuro ? Color.FromArgb(32, 46, 40) : Color.FromArgb(236, 247, 240);
            acento = oscuro ? Color.FromArgb(74, 222, 128) : Color.FromArgb(21, 128, 61);
            texto = oscuro ? Color.White : Color.FromArgb(17, 24, 39);
            RoundedEdges = false;
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(linea)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed)) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(4, 1, e.Item.Width - 9, e.Item.Height - 3);
            using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
            using (SolidBrush b = new SolidBrush(encima))
            {
                int d = 8;
                gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                gp.CloseFigure();
                e.Graphics.FillPath(b, gp);
            }
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            int cx = r.X + r.Width / 2, cy = e.Item.Height / 2;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen p = new Pen(acento, 2f))
            {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                e.Graphics.DrawLines(p, new Point[] { new Point(cx - 4, cy), new Point(cx - 1, cy + 3), new Point(cx + 4, cy - 3) });
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen p = new Pen(linea)) e.Graphics.DrawLine(p, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = texto; base.OnRenderArrow(e); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? texto : Color.Gray;
            // El relleno vertical del item no mueve el texto (queda pegado arriba): se centra a mano
            Rectangle r = e.TextRectangle;
            e.TextRectangle = new Rectangle(r.X, (e.Item.Height - r.Height) / 2, r.Width, r.Height);
            base.OnRenderItemText(e);
        }
    }

    // ---------- La app ----------
    public partial class Pulso : ApplicationContext
    {
        public const string Nombre = "Pulso";
        // Lo unico que queda del nombre anterior (Xuca Monitor, hasta la 1.0.3): la carpeta de datos se muda sola a
        // Documentos\Pulso y la tarea "Iniciar con Windows" se cambia por la nueva (ver CarpetaDatos y Iniciar).
        const string NombreViejo = "Xuca Monitor";
        // Al sacar una version nueva: cambiar esto y subir Pulso.exe a un Release de GitHub con el tag "v" + Version
        public const string Version = "1.0.4";

        string Dir, Datos, CfgF, Exe;
        readonly List<Modulo> modulos = new List<Modulo>();
        Modulo actual;
        string tema = "sistema";
        bool salir;

        Control Ui;
        NotifyIcon ni;
        ToolStripMenuItem miAbrir, miSalir;
        readonly Dictionary<Modulo, ToolStripMenuItem> miMod = new Dictionary<Modulo, ToolStripMenuItem>();
        Icon icono, iconoChico;
        Mutex mutex;
        EventWaitHandle evMostrar;
        System.Windows.Forms.Timer timerTema;
        bool oscuroAhora;

        // Ventana (null cuando esta cerrada)
        Form fv;
        TextBox tb;
        Label estado;
        Panel panelExtra;
        CheckBox chkActivo;
        readonly List<Button> botones = new List<Button>();
        readonly Dictionary<Modulo, Button> pestanas = new Dictionary<Modulo, Button>();
        Button bReg, bLimp, bAj, bKofi, bOver, bNov;
        ContextMenuStrip menuAj;
        FlowLayoutPanel arriba, fila2;
        Panel medio, caja;

        static string T(string es, string en) { return Modulo.T(es, en); }

        // Version real de Windows. RtlGetVersion no depende del manifest (Environment.OSVersion si: sin la seccion de
        // compatibilidad, Windows 10/11 se hace pasar por 8 y se apagaria todo lo de abajo).
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OSVI { public int Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Csd; }
        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OSVI v);
        static Version LeerVersion()
        {
            try { OSVI v = new OSVI(); v.Size = Marshal.SizeOf(typeof(OSVI)); if (RtlGetVersion(ref v) == 0) return new Version(v.Major, v.Minor, v.Build); }
            catch { }
            return Environment.OSVersion.Version;
        }
        internal static readonly Version WinVer = LeerVersion();
        // En 7, 8 y 8.1 Pulso anda recortado: sin las pestañas de hardware (sensores con PawnIO) ni overlay de rendimiento / grabacion de sesion (ETW).
        internal static readonly bool Win10 = WinVer.Major >= 10;
        internal static string SoloWin10(string que_es, string que_en)
        {
            return T(que_es + ": solo para Windows 10 u 11. En tu Windows (" + WinVer.ToString(2) + ") no está disponible; el resto de Pulso anda igual.",
                     que_en + ": Windows 10 or 11 only. On your Windows (" + WinVer.ToString(2) + ") it is not available; the rest of Pulso works the same.");
        }

        // Aparte, sin inline: si falta una dll de "lib" el error de carga queda adentro de este metodo nomas.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Modulo CrearModuloPC()
        {
            try { return new MonitorPC(); }
            catch (Exception ex) { GuardarCrash(ex); return null; }
        }

        // Devuelve false si ya habia otro abierto (a ese se le pide que muestre su ventana)
        public bool Iniciar(bool oculto, bool actualizado)
        {
            bool creado;
            mutex = new Mutex(true, "Local\\Pulso", out creado);
            evMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Pulso_Mostrar");
            // Recien actualizada: la version vieja todavia se esta cerrando, se espera a que suelte el mutex
            if (!creado && actualizado)
            {
                try { creado = mutex.WaitOne(8000); } catch (AbandonedMutexException) { creado = true; }
            }
            if (!creado) { evMostrar.Set(); return false; }

            Exe = Application.ExecutablePath;
            Dir = Path.GetDirectoryName(Exe);
            // Registros, historial y ajustes van a Documentos\Pulso (asi el .exe puede estar en cualquier lado).
            // Se muda despues de tomar el mutex: ninguna otra copia puede estar escribiendo ahi.
            Datos = carpetaDatos = CarpetaDatos(true);
            try { Directory.CreateDirectory(Datos); } catch { Datos = Dir; }
            CfgF = Path.Combine(Datos, "ajustes_monitores.txt");

            Dictionary<string, string> aj = LeerAjustes();
            string v;
            if (aj.TryGetValue("idioma", out v) && (v == "es" || v == "en")) Modulo.Idioma = v;
            else Modulo.Idioma = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
            if (aj.TryGetValue("tema", out v) && (v == "sistema" || v == "claro" || v == "oscuro")) tema = v;
            float z;
            if (aj.TryGetValue("zoom", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z)) zoom = Math.Max(7f, Math.Min(24f, z));

            Ui = new Control();
            IntPtr h = Ui.Handle; // crea la ventana oculta en este hilo

            // Icono grande (ventana) y chico (bandeja) sacados del propio .exe
            try
            {
                IntPtr[] g = new IntPtr[1], c = new IntPtr[1];
                if (ExtractIconEx(Exe, 0, g, c, 1) > 0)
                {
                    if (g[0] != IntPtr.Zero) icono = Icon.FromHandle(g[0]);
                    if (c[0] != IntPtr.Zero) iconoChico = Icon.FromHandle(c[0]);
                }
            }
            catch { }
            if (icono == null) icono = SystemIcons.Application;
            if (iconoChico == null) iconoChico = icono;

            modulos.Add(new MonitorRed());
            modulos.Add(new MonitorUsb());
            modulos.Add(new MonitorRend());
            modulos.Add(new MonitorErrores());
            // Hardware: CPU, GPU, Memoria y Discos son 4 pestañas que leen de un mismo monitor (MonitorPC), que no se muestra.
            // Si falta alguna dll de la carpeta "lib" (sensores), que no se caiga toda la app: se guarda el error y
            // se sigue sin esas pestañas.
            // En Windows 7/8/8.1 queda una sola pestaña que explica por que no estan (el driver PawnIO es para Windows 10 u 11)
            if (Win10)
            {
                MonitorPC pc = (MonitorPC)CrearModuloPC();
                if (pc != null)
                {
                    pc.App = this; pc.LogF = Path.Combine(Datos, pc.RegistroArch); pc.HistF = Path.Combine(Datos, pc.HistArch);
                    modulos.AddRange(pc.Pestanas);
                }
            }
            else modulos.Add(new ModuloNoDisponible("pc", "Hardware",
                "Las pestañas CPU, GPU, Memoria y Discos (temperaturas, consumo, frecuencias y estado del hardware)", "The CPU, GPU, Memory and Drives tabs (temperatures, power, clocks and hardware health)"));
            modulos.Add(new MonitorSesion());
            foreach (Modulo m in modulos)
            {
                m.App = this;
                m.LogF = Path.Combine(Datos, m.RegistroArch);
                m.HistF = Path.Combine(Datos, m.HistArch);
                m.CargarHistorial();
            }
            actual = modulos[0];
            if (aj.TryGetValue("pagina", out v)) foreach (Modulo m in modulos) if (m.Clave == v) actual = m;

            ni = new NotifyIcon();
            ni.Icon = iconoChico;
            ContextMenuStrip menu = new ContextMenuStrip();
            miAbrir = new ToolStripMenuItem("", null, delegate { MostrarVentana(); });
            menu.Items.Add(miAbrir);
            menu.Items.Add(new ToolStripSeparator());
            foreach (Modulo m in modulos)
            {
                Modulo mm = m;
                ToolStripMenuItem it = new ToolStripMenuItem("", null, delegate { Prender(mm, !mm.Activo); });
                miMod[m] = it;
                menu.Items.Add(it);
            }
            menu.Items.Add(new ToolStripSeparator());
            miAct = new ToolStripMenuItem("", null, delegate { Actualizar(); });
            miAct.Visible = false;
            menu.Items.Add(miAct);
            miSalir = new ToolStripMenuItem("", null, delegate { Salir(); });
            menu.Items.Add(miSalir);
            ni.ContextMenuStrip = menu;
            ni.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MostrarVentana(); };
            ni.Visible = true;

            // Cuando se abre otra copia, esta muestra su ventana
            ThreadPool.RegisterWaitForSingleObject(evMostrar, delegate { EnUi(MostrarVentana); }, null, -1, false);

            // Con el tema "Sistema", cada 5 s se fija si Windows cambio entre claro y oscuro (solo con la ventana abierta)
            timerTema = new System.Windows.Forms.Timer();
            timerTema.Interval = 5000;
            timerTema.Tick += delegate { if (fv != null && tema == "sistema" && EsOscuro() != oscuroAhora) AplicarTema(); };

            // "Iniciar con Windows": la tarea de antes del cambio de nombre (XucaMonitor_Inicio) pasa a Pulso_Inicio. La vieja
            // se borra recien cuando la nueva quedo creada, asi nunca se pierde. Solo se reescribe si hace falta (la vieja
            // existe o el .exe cambio de carpeta): reescribirla en cada arranque es lo que Defender marcaba (ver CarpetaTareas).
            bool tareaVieja = TareaComando(TareaVieja) != null;
            string cmd = TareaComando(TareaNombre);
            if (tareaVieja || (cmd != null && !string.Equals(cmd, TareaExe, StringComparison.OrdinalIgnoreCase)))
            {
                TareaCrear();
                if (tareaVieja && TareaExiste()) TareaBorrar(TareaVieja);
            }

            foreach (Modulo m in modulos)
            {
                string on;
                // Las pestañas de hardware nuevas heredan el "Activado" de la pestaña PC que reemplazan (hasta la 1.0.3)
                if (!aj.TryGetValue(m.Clave, out on) && m is PcPestana) aj.TryGetValue("pc", out on);
                if (on != "0") m.Prender(true);
            }
            // El reporte de cada grabacion de sesion queda en la pestaña Sesión y avisa con un globito en la bandeja
            Bench.Listo = delegate (string largo, string corto)
            {
                foreach (Modulo m in modulos) if (m is MonitorSesion) { m.AgregarAviso(largo, 1); break; }
                if (corto != null) EnUi(delegate { try { if (ni != null && ni.Visible) ni.ShowBalloonTip(10000, Nombre + T(" · Sesión", " · Session"), corto, ToolTipIcon.Info); } catch { } });
            };
            // El overlay de rendimiento (y la grabacion de sesion) usa eventos de presentacion de Windows 10 u 11
            if (Win10) { try { OvMgr.Iniciar(Datos); } catch (Exception ex) { GuardarCrash(ex); } }
            ActualizarTextos();
            if (!oculto) MostrarVentana(); else Recortar();
            EmpezarBusquedaAct(); // al final: borra la version anterior solo si esta arranco entera
            return true;
        }

        void Prender(Modulo m, bool si)
        {
            m.Prender(si);
            GuardarAjuste(m.Clave, si ? "1" : "0");
            ActualizarTextos();
            Recortar();
        }

        void Salir()
        {
            if (salir) return;
            salir = true;
            foreach (Modulo m in modulos) if (m.Activo) { try { m.Prender(false); } catch { } }
            try { OvMgr.Detener(); } catch { }
            ni.Visible = false;
            ni.Dispose();
            if (fv != null) { fv.Dispose(); fv = null; }
            if (timerAct != null) timerAct.Dispose();
            try { mutex.ReleaseMutex(); } catch { }
            try { mutex.Close(); } catch { }
            ExitThread();
        }

        public void EnUi(MethodInvoker m)
        {
            try { if (!salir) Ui.BeginInvoke(m); } catch { }
        }

        // Un modulo agrego algo: se actualiza la ventana (si esta abierta) y el texto de la bandeja
        public void Cambio(Modulo m)
        {
            // Un aviso de Rendimiento o de Red en medio de una grabacion de sesion: pudo ensuciar la medicion
            if (Bench.Corriendo && (m is MonitorRend || m is MonitorRed)) Bench.Evento(m.Nombre + ": " + m.UltimoTitulo());
            EnUi(delegate
            {
                if (fv != null && m == actual) LlenarTexto();
                ActualizarEstado();
            });
        }

        public void Aviso(Modulo m, string texto)
        {
            EnUi(delegate { if (tb != null && m == actual) tb.Text = texto + "\r\n\r\n" + tb.Text; });
        }

        // ---------- Ajustes ----------
        readonly object cerrojoAj = new object();

        public string Ajuste(string k)
        {
            string v;
            return LeerAjustes().TryGetValue(k, out v) ? v : null;
        }

        Dictionary<string, string> LeerAjustes()
        {
            Dictionary<string, string> c = new Dictionary<string, string>();
            lock (cerrojoAj)
            {
                try
                {
                    foreach (string l in File.ReadAllLines(CfgF))
                    {
                        int i = l.IndexOf('=');
                        if (i > 0) c[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                    }
                }
                catch { }
            }
            return c;
        }

        // Se puede llamar desde cualquier hilo
        public void GuardarAjuste(string k, string v)
        {
            lock (cerrojoAj)
            {
                Dictionary<string, string> c = LeerAjustes();
                c[k] = v;
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, string> p in c) l.Add(p.Key + "=" + p.Value);
                try { File.WriteAllLines(CfgF, l.ToArray()); } catch { }
            }
        }

        void LlenarTexto()
        {
            if (tb == null) return;
            tb.Text = actual.Texto();
            tb.SelectionStart = 0;
            tb.SelectionLength = 0;
        }

        // ---------- Accesos directos ----------
        void Acceso(string lnk, string args)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            try
            {
                object s = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                Type ts = s.GetType();
                ts.InvokeMember("TargetPath", BindingFlags.SetProperty, null, s, new object[] { Exe });
                ts.InvokeMember("Arguments", BindingFlags.SetProperty, null, s, new object[] { args });
                ts.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, s, new object[] { Dir });
                ts.InvokeMember("IconLocation", BindingFlags.SetProperty, null, s, new object[] { Exe + ",0" });
                ts.InvokeMember("Description", BindingFlags.SetProperty, null, s, new object[] { Nombre });
                ts.InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
                Marshal.FinalReleaseComObject(s);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }

        // ---------- Iniciar con Windows (tarea programada) ----------
        // Como la app pide administrador, un acceso directo en la carpeta de Inicio no la eleva sola:
        // Windows no la arranca (o la arranca sin permisos). Por eso "Iniciar con Windows" usa una tarea
        // programada con "Ejecutar con los privilegios más altos", que SI se eleva solita al iniciar sesión.
        const string TareaNombre = "Pulso_Inicio";
        const string TareaVieja = "XucaMonitor_Inicio";   // la de hasta la 1.0.3: Iniciar la cambia por la nueva

        string UltimoError = "";

        // Se maneja con la API del Programador de tareas (COM "Schedule.Service"), sin abrir schtasks.exe ni escribir un XML
        // en %TEMP%. Hasta la 1.0.3 se hacia con schtasks y se recreaba en CADA arranque: Defender lo marcaba como
        // Behavior:Win32/Persistence.A!ml y Execution.A!ml (un .exe sin firma que se reinstala al inicio una y otra vez)
        // y borraba Pulso. Ahora solo se escribe cuando el usuario la prende o cuando el .exe cambio de carpeta.
        static object Com(object o, string metodo, params object[] args) { return o.GetType().InvokeMember(metodo, BindingFlags.InvokeMethod, null, o, args); }
        static object CarpetaTareas()
        {
            object svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            Com(svc, "Connect");
            return Com(svc, "GetFolder", "\\");
        }

        // El comando que tiene la tarea guardada, o null si no existe
        static string TareaComando(string nombre)
        {
            try
            {
                object t = Com(CarpetaTareas(), "GetTask", nombre);
                string xml = (string)t.GetType().InvokeMember("Xml", BindingFlags.GetProperty, null, t, null);
                System.Xml.XmlDocument d = new System.Xml.XmlDocument();
                d.LoadXml(xml);
                System.Xml.XmlNodeList c = d.GetElementsByTagName("Command");
                return c.Count > 0 ? Environment.ExpandEnvironmentVariables(c[0].InnerText.Trim('"')) : "";
            }
            catch { return null; }
        }

        bool TareaExiste() { return TareaComando(TareaNombre) != null; }

        // Se crea con un XML propio porque la tarea que arma "schtasks /sc onlogon" trae dos trampas de Windows:
        // no arranca si la notebook esta a bateria, y MATA el programa a las 72 horas de andar.
        void TareaCrear()
        {
            string usuario = Environment.UserDomainName + "\\" + Environment.UserName;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Abre Pulso al iniciar sesion</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Xml(usuario) + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + Xml(usuario) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + Xml(TareaExe) + "</Command><Arguments>/inicio</Arguments><WorkingDirectory>" + Xml(Path.GetDirectoryName(TareaExe)) + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
            // 6 = crear o actualizar; 3 = con el token de la sesion interactiva (el usuario y el nivel salen del XML)
            try { Com(CarpetaTareas(), "RegisterTask", TareaNombre, xml, 6, null, null, 3, null); UltimoError = ""; }
            catch (Exception ex) { UltimoError = (ex.InnerException ?? ex).Message; }
        }

        static string Xml(string s) { return System.Security.SecurityElement.Escape(s ?? ""); }

        static void TareaBorrar(string nombre) { try { Com(CarpetaTareas(), "DeleteTask", nombre, 0); } catch { } }
        void TareaBorrar() { TareaBorrar(TareaVieja); TareaBorrar(TareaNombre); }

        // Instalada desde la Microsoft Store (MSIX) la carpeta del .exe cambia con cada actualizacion: la tarea apunta al
        // alias "Pulso.exe" que Windows deja fijo en %LOCALAPPDATA%\Microsoft\WindowsApps.
        string TareaExe { get { return Empaquetada ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WindowsApps\\Pulso.exe") : Exe; } }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref int len, StringBuilder nombre);
        // true si corre instalada como paquete MSIX (Microsoft Store). En Windows 7 la funcion no existe: false.
        internal static readonly bool Empaquetada = EsPaquete();
        static bool EsPaquete()
        {
            try { int n = 0; return GetCurrentPackageFullName(ref n, null) != 15700; }   // 15700 = APPMODEL_ERROR_NO_PACKAGE
            catch { return false; }
        }

        // ---------- Carpeta de datos ----------
        static string carpetaDatos;   // la que eligio Iniciar (GuardarCrash la usa)

        // Documentos\Pulso. Si todavia esta la de antes del cambio de nombre (Documentos\Xuca Monitor) y no hay una
        // nueva, se renombra entera (mismo disco: no se copia nada, no se puede perder a medias). Si no se puede mudar
        // (algun archivo abierto por otro programa), se sigue usando la vieja y se reintenta en el proximo arranque.
        static string CarpetaDatos(bool mudar)
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string nueva = Path.Combine(docs, Nombre), vieja = Path.Combine(docs, NombreViejo);
            if (!Directory.Exists(nueva) && Directory.Exists(vieja))
            {
                if (!mudar || ViejaAbierta()) return vieja;
                try { Directory.Move(vieja, nueva); }
                catch { return vieja; }
            }
            return nueva;
        }

        // Si una Xuca Monitor (hasta la 1.0.3) sigue abierta, sigue escribiendo en la carpeta vieja: no se muda hasta
        // que se cierre, asi los datos no quedan repartidos entre las dos carpetas.
        static bool ViejaAbierta()
        {
            try
            {
                Mutex m;
                if (!Mutex.TryOpenExisting("Local\\XucaMonitor", out m)) return false;
                m.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException) { return true; }   // existe pero es de otro proceso con mas permisos
            catch { return false; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int ExtractIconEx(string f, int i, IntPtr[] g, IntPtr[] c, int n);

        // ---------- Tema ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

        bool EsOscuro()
        {
            if (tema == "oscuro") return true;
            if (tema == "claro") return false;
            try
            {
                object v = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return true; }
        }

        ToolStripMenuItem Opc(string txt, bool marcada, EventHandler click)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(txt);
            m.Checked = marcada;
            m.Click += click;
            return m;
        }
        void ArmarMenuAjustes()
        {
            menuAj.Items.Clear();
            ToolStripMenuItem idi = new ToolStripMenuItem(T("Idioma", "Language"));
            idi.DropDownItems.Add(Opc("Español", Modulo.Idioma == "es", delegate { Modulo.Idioma = "es"; GuardarAjuste("idioma", "es"); ActualizarTextos(); }));
            idi.DropDownItems.Add(Opc("English", Modulo.Idioma == "en", delegate { Modulo.Idioma = "en"; GuardarAjuste("idioma", "en"); ActualizarTextos(); }));
            ToolStripMenuItem tem = new ToolStripMenuItem(T("Tema", "Theme"));
            tem.DropDownItems.Add(Opc(T("Sistema", "System"), tema == "sistema", delegate { tema = "sistema"; GuardarAjuste("tema", tema); AplicarTema(); }));
            tem.DropDownItems.Add(Opc(T("Claro", "Light"), tema == "claro", delegate { tema = "claro"; GuardarAjuste("tema", tema); AplicarTema(); }));
            tem.DropDownItems.Add(Opc(T("Oscuro", "Dark"), tema == "oscuro", delegate { tema = "oscuro"; GuardarAjuste("tema", tema); AplicarTema(); }));
            menuAj.Items.Add(idi);
            menuAj.Items.Add(tem);
            menuAj.Items.Add(Opc(T("Iniciar con Windows", "Start with Windows"), TareaExiste(), delegate
            {
                bool quiere = !TareaExiste();
                if (quiere) { try { TareaCrear(); } catch { } }
                else { try { TareaBorrar(); } catch { } }
                if (quiere && !TareaExiste()) MessageBox.Show(T("No se pudo activar el inicio con Windows:\n", "Could not enable start with Windows:\n") + UltimoError, Nombre);
            }));
            if (Win10)
            {
                menuAj.Items.Add(Opc(T("Overlay: exportar estilo...", "Overlay: export style..."), false, delegate { OvMgr.ExportarEstilo(fv); }));
                menuAj.Items.Add(Opc(T("Overlay: importar estilo...", "Overlay: import style..."), false, delegate { OvMgr.ImportarEstilo(fv); }));
            }
            // Mismos colores que la ventana (el menu no hereda el tema solo)
            Color bg = oscuroAhora ? Color.FromArgb(28, 32, 39) : Color.White;
            Color fg = oscuroAhora ? Color.White : Color.FromArgb(17, 24, 39);
            menuAj.BackColor = bg; menuAj.ForeColor = fg;
            MenuAjustes dibujo = new MenuAjustes(oscuroAhora);
            menuAj.Renderer = dibujo; menuAj.ShowImageMargin = false; menuAj.ShowCheckMargin = true;
            foreach (ToolStripMenuItem m in menuAj.Items)
            {
                m.BackColor = bg; m.ForeColor = fg; m.Padding = new Padding(4, 5, 4, 5);
                ToolStripDropDownMenu dd = m.DropDown as ToolStripDropDownMenu;
                m.DropDown.BackColor = bg; m.DropDown.Renderer = dibujo;
                if (dd != null) { dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
                foreach (ToolStripItem d in m.DropDownItems) { d.BackColor = bg; d.ForeColor = fg; d.Padding = new Padding(4, 5, 4, 5); }
            }
        }

        // Para que los paneles propios de cada modulo (PC) usen los mismos colores
        public bool Oscuro { get { return fv != null ? oscuroAhora : EsOscuro(); } }

        void AplicarTema()
        {
            if (fv == null) return;
            bool osc = EsOscuro();
            oscuroAhora = osc;
            Color fondo = osc ? Color.FromArgb(20, 22, 27) : Color.FromArgb(243, 244, 246);
            Color tarj = osc ? Color.FromArgb(28, 32, 39) : Color.White;
            Color linea = osc ? Color.FromArgb(46, 52, 64) : Color.FromArgb(209, 213, 219);
            Color texto = osc ? Color.White : Color.FromArgb(17, 24, 39);
            Color gris = osc ? Color.FromArgb(150, 160, 175) : Color.FromArgb(107, 114, 128);
            // Verde propio de la app (borde y dibujo del icono; el fondo #14161B del icono es el de la ventana).
            // Solo en controles y seleccion: los colores de estado (bien/atencion/mal) van siempre con texto.
            Color acento = osc ? Color.FromArgb(74, 222, 128) : Color.FromArgb(21, 128, 61);
            Color elegida = osc ? Color.FromArgb(22, 50, 35) : Color.FromArgb(220, 252, 231);
            Color encima = osc ? Color.FromArgb(32, 46, 40) : Color.FromArgb(236, 247, 240);   // hover: gris con un toque de verde
            fv.BackColor = fondo; fv.ForeColor = texto;
            arriba.BackColor = fondo; fila2.BackColor = fondo; medio.BackColor = fondo;
            if (panelExtra != null)
            {
                panelExtra.BackColor = fondo;
                foreach (Control c in panelExtra.Controls) c.Invalidate();
            }
            caja.BackColor = tarj; tb.BackColor = tarj; tb.ForeColor = texto;
            estado.BackColor = fondo; estado.ForeColor = gris;
            chkActivo.ForeColor = texto;
            foreach (Button b in botones)
            {
                bool pest = false, sel = false;
                foreach (KeyValuePair<Modulo, Button> p in pestanas) if (p.Value == b) { pest = true; sel = p.Key == actual; }
                b.BackColor = sel ? elegida : tarj;
                b.ForeColor = texto;
                b.FlatAppearance.BorderColor = sel ? acento : linea;
                b.FlatAppearance.BorderSize = pest && sel ? 2 : 1;
                b.FlatAppearance.MouseOverBackColor = sel ? elegida : encima;
            }
            // Barra de titulo oscura o clara (Windows 10 20H1 o mas nuevo)
            try
            {
                int v = osc ? 1 : 0;
                DwmSetWindowAttribute(fv.Handle, 20, ref v, 4);
                DwmSetWindowAttribute(fv.Handle, 19, ref v, 4);
                // Barra de desplazamiento oscura en el texto (Windows 10 1809 o mas nuevo)
                SetWindowTheme(tb.Handle, osc ? "DarkMode_Explorer" : "Explorer", null);
                if (fv.Visible) { fv.Width += 1; fv.Width -= 1; }
            }
            catch { }
        }

        // ---------- Textos (para cambiar de idioma sin cerrar) ----------
        void ActualizarTextos()
        {
            TextoActualizar();
            miAbrir.Text = T("Abrir", "Open");
            miSalir.Text = T("Salir", "Exit");
            foreach (Modulo m in modulos) { miMod[m].Text = m.Nombre; miMod[m].Checked = m.Activo; }
            if (fv != null)
            {
                fv.Text = Nombre + " v" + Version;
                bAj.Text = T("Ajustes", "Settings");
                bOver.Text = T("Overlay de rendimiento", "Performance overlay");
                bNov.Text = T("Novedades", "What's new");
                bKofi.Text = T("♥ Apoyar en Ko-fi", "♥ Support on Ko-fi");
                chkActivo.Text = T("Activado", "Enabled");
                chkActivo.Checked = actual.Activo;
                bReg.Text = T("Abrir registro", "Open log");
                bLimp.Text = T("Limpiar", "Clear");
                ActualizarPanelExtra();
                LlenarTexto();
            }
            ActualizarEstado();
        }

        Modulo panelExtraDe;

        // Pide el panel propio del modulo elegido (sensores de PC, etc.) solo cuando cambio de modulo:
        // asi no se reconstruye (y pierde sus timers) cada vez que se actualizan los textos.
        void ActualizarPanelExtra()
        {
            if (panelExtraDe == actual) return;
            panelExtraDe = actual;
            // El panel anterior se libera de verdad (Clear solo lo saca: su timer seguia andando para siempre)
            List<Control> viejos = new List<Control>();
            foreach (Control c in panelExtra.Controls) viejos.Add(c);
            panelExtra.Controls.Clear();
            foreach (Control c in viejos) c.Dispose();
            Control ex = actual.PanelExtra();
            if (ex != null)
            {
                int h = ex.Height > 0 ? ex.Height : 140;
                ex.Dock = DockStyle.Fill;
                panelExtra.Controls.Add(ex);
                panelExtra.Height = h;
                panelExtra.Visible = true;
            }
            else { panelExtra.Visible = false; panelExtra.Height = 0; }
        }

        void ActualizarEstado()
        {
            if (fv != null)
            {
                foreach (KeyValuePair<Modulo, Button> p in pestanas)
                {
                    Modulo m = p.Key;
                    p.Value.Text = T(m.PestEs, m.PestEn) + "  " + (m.Activo ? "(" + m.Contador() + ")" : T("(apagado)", "(off)"));
                }
                string cont = T(actual.ContEs, actual.ContEn);
                if (!actual.Activo)
                    estado.Text = T("Apagado: no está vigilando. Prendelo con la casilla \"Activado\".", "Off: not watching. Turn it on with the \"Enabled\" box.") + "   ·   " + cont + ": " + actual.Contador();
                else
                    estado.Text = Modulo.Idioma == "en"
                        ? "Watching since " + actual.Desde.ToString("HH:mm") + " on " + actual.Desde.ToString("MM/dd") + "   ·   " + cont + ": " + actual.Contador() + "   ·   Minimizing or closing keeps it in the tray (next to the clock)."
                        : "Vigilando desde las " + actual.Desde.ToString("HH:mm") + " del " + actual.Desde.ToString("dd/MM") + "   ·   " + cont + ": " + actual.Contador() + "   ·   Minimizar o cerrar la deja en la bandeja (al lado del reloj).";
            }
            // Texto de la bandeja: "Pulso - Red 2 · USB 5"
            StringBuilder tip = new StringBuilder(Nombre + " -");
            foreach (Modulo m in modulos)
            {
                tip.Append(" " + T(m.TipEs, m.TipEn) + " " + (m.Activo ? m.Contador().ToString() : "off"));
                if (m != modulos[modulos.Count - 1]) tip.Append(" ·");
            }
            string t = tip.ToString();
            ni.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        // ---------- Ventana ----------
        Button Boton(FlowLayoutPanel donde, EventHandler click)
        {
            Button b = new Button();
            b.AutoSize = true;
            b.Height = (int)(28 * b.DeviceDpi / 96f);
            b.FlatStyle = FlatStyle.Flat;
            b.Margin = new Padding(0, 0, 8, 0);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.Click += click;
            donde.Controls.Add(b);
            botones.Add(b);
            return b;
        }

        void Elegir(Modulo m)
        {
            actual = m;
            GuardarAjuste("pagina", m.Clave);
            ActualizarTextos();
            AplicarTema();
        }

        void MostrarVentana()
        {
            if (salir) return;
            if (fv != null)
            {
                fv.Show();
                fv.WindowState = FormWindowState.Normal;
                fv.Activate();
                return;
            }
            fv = new Form();
            fv.Icon = icono;
            // Tamaños pensados para 100 %: se agrandan con la escala de Windows (125 %, 150 %...)
            float k = fv.DeviceDpi / 96f;
            fv.Size = new Size((int)(1000 * k), (int)(720 * k));
            fv.MinimumSize = new Size((int)(600 * k), (int)(340 * k));
            fv.StartPosition = FormStartPosition.CenterScreen;
            fv.Font = new Font("Segoe UI", 9.5f);

            // Fila de arriba: lo de toda la app
            arriba = new FlowLayoutPanel();
            arriba.Dock = DockStyle.Top;
            arriba.Height = (int)(44 * k);
            arriba.Padding = new Padding((int)(10 * k), (int)(9 * k), (int)(10 * k), 0);

            // Ajustes: idioma, tema, inicio con Windows y copia del estilo del overlay (el menu se arma al abrirlo, asi siempre muestra lo que esta elegido)
            menuAj = new ContextMenuStrip();
            bAj = Boton(arriba, delegate { ArmarMenuAjustes(); menuAj.Show(bAj, new Point(0, bAj.Height)); });
            bOver = Boton(arriba, delegate
            {
                if (Win10) OvAjustes.Mostrar();
                else MessageBox.Show(SoloWin10("El overlay de rendimiento y la grabación de sesión", "The performance overlay and session recording"), Nombre, MessageBoxButtons.OK, MessageBoxIcon.Information);
            });

            // Novedades: la ultima version del changelog (embebido) en el idioma de la app; el resto, en GitHub
            bNov = Boton(arriba, delegate { Novedades.Mostrar(oscuroAhora, icono); });

            // Ko-fi: celeste fijo, igual que en Brio (no cambia con el tema)
            bKofi = Boton(arriba, delegate { try { Process.Start("https://ko-fi.com/011xuca"); } catch { } });
            botones.Remove(bKofi);
            bKofi.BackColor = Color.FromArgb(143, 211, 244);
            bKofi.ForeColor = Color.FromArgb(11, 37, 51);
            bKofi.Font = new Font("Segoe UI Semibold", 9.5f);
            bKofi.FlatAppearance.BorderColor = Color.FromArgb(143, 211, 244);
            bKofi.FlatAppearance.MouseOverBackColor = Color.FromArgb(186, 230, 250);

            // Segunda fila: pestañas Red / USB y lo de la pestaña elegida
            fila2 = new FlowLayoutPanel();
            fila2.Dock = DockStyle.Top;
            // Son 9 pestañas: en una ventana angosta pasan a una segunda linea en vez de quedar cortadas
            fila2.AutoSize = true;
            fila2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fila2.Padding = new Padding((int)(10 * k), (int)(4 * k), (int)(10 * k), (int)(6 * k));
            pestanas.Clear();
            foreach (Modulo m in modulos)
            {
                Modulo mm = m;
                Button p = Boton(fila2, delegate { Elegir(mm); });
                p.Font = new Font("Segoe UI Semibold", 9.5f);
                p.Padding = new Padding((int)(8 * k), 0, (int)(8 * k), 0);
                p.Margin = new Padding(0, 0, (int)(6 * k), (int)(4 * k));
                pestanas[m] = p;
            }
            // Lo de la pestaña elegida empieza siempre en su propia linea, debajo de las pestañas
            fila2.SetFlowBreak(pestanas[modulos[modulos.Count - 1]], true);
            chkActivo = new CheckBox();
            chkActivo.AutoSize = true;
            chkActivo.Margin = new Padding(0, 5, 14, 0);
            chkActivo.Checked = actual.Activo;
            chkActivo.CheckedChanged += delegate { if (chkActivo.Checked != actual.Activo) Prender(actual, chkActivo.Checked); };
            fila2.Controls.Add(chkActivo);
            bReg = Boton(fila2, delegate { if (File.Exists(actual.LogF)) Process.Start("notepad.exe", "\"" + actual.LogF + "\""); });
            bLimp = Boton(fila2, delegate { actual.Limpiar(); LlenarTexto(); ActualizarEstado(); });

            tb = new TextBox();
            tb.Multiline = true;
            tb.ReadOnly = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.Dock = DockStyle.Fill;
            tb.BorderStyle = BorderStyle.None;
            tb.Font = new Font("Consolas", zoom);
            PrepararZoom();

            medio = new Panel();
            medio.Dock = DockStyle.Fill;
            medio.Padding = new Padding((int)(10 * k), (int)(4 * k), (int)(10 * k), (int)(4 * k));
            caja = new Panel();
            caja.Dock = DockStyle.Fill;
            caja.Padding = new Padding((int)(10 * k));
            caja.Controls.Add(tb);
            medio.Controls.Add(caja);

            estado = new Label();
            estado.Dock = DockStyle.Bottom;
            estado.Height = (int)(28 * k);
            estado.Padding = new Padding((int)(12 * k), 0, 0, (int)(4 * k));
            estado.TextAlign = ContentAlignment.MiddleLeft;

            panelExtra = new Panel();
            panelExtra.Dock = DockStyle.Top;
            panelExtra.Height = 0;
            panelExtra.Visible = false;

            fv.Controls.Add(medio);
            fv.Controls.Add(panelExtra);
            fv.Controls.Add(estado);
            fv.Controls.Add(fila2);
            fv.Controls.Add(arriba);
            CrearCartelAct();

            // Minimizar o cerrar con la X la manda a la bandeja: la ventana se destruye y se libera la memoria
            fv.KeyPreview = true;
            fv.KeyDown += TeclaZoom;
            fv.Resize += delegate { if (fv != null && fv.WindowState == FormWindowState.Minimized) CerrarVentana(); };
            fv.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (!salir && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; CerrarVentana(); }
            };

            ActualizarTextos();
            AplicarTema();
            timerTema.Start();
            fv.Show();
            fv.Activate();
        }

        void CerrarVentana()
        {
            Form f = fv;
            if (f == null) return;
            timerTema.Stop();
            fv = null; tb = null; estado = null; panelExtra = null; panelExtraDe = null; chkActivo = null; botones.Clear(); pestanas.Clear();
            f.Hide();
            Ui.BeginInvoke((MethodInvoker)delegate { f.Dispose(); Recortar(); });
        }

        // ---------- Memoria ----------
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);

        // Devuelve a Windows la memoria que no se esta usando (cuando no hay ventana abierta)
        void Recortar()
        {
            if (fv != null) return;
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                EmptyWorkingSet(GetCurrentProcess());
            }
            catch { }
        }

        // Si algo se rompe antes de mostrar la ventana (o en cualquier momento), en vez de cerrarse sin avisar
        // nada, deja un registro en Documentos\Pulso\crash.txt y muestra el error.
        static void GuardarCrash(object ex)
        {
            string msg = ex == null ? "(error desconocido)" : ex.ToString();
            // Sin mudar la carpeta: esto puede correr antes de tomar el mutex
            string arch = Path.Combine(carpetaDatos ?? CarpetaDatos(false), "crash.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(arch));
                File.AppendAllText(arch, DateTime.Now + "\r\n" + msg + "\r\n\r\n");
            }
            catch { }
            try
            {
                MessageBox.Show(T("Pulso tuvo un error:\r\n\r\n", "Pulso had an error:\r\n\r\n") + msg +
                    T("\r\n\r\nQuedó guardado en ", "\r\n\r\nIt was saved in ") + arch,
                    Nombre, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        // Las dll de los sensores (LibreHardwareMonitor y las que usa) van ADENTRO del .exe (compilar.bat las mete
        // como recursos "lib.Nombre.dll"): asi el .exe anda solo, se puede mover a cualquier carpeta y el
        // actualizador baja un unico archivo. .NET no las encuentra por su cuenta, entonces cuando pide una se la
        // damos desde adentro del .exe (o, si no estuviera, desde una carpeta "lib" al lado).
        static readonly Dictionary<string, Assembly> cargadas = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        static Assembly ResolverDeLib(object s, ResolveEventArgs e)
        {
            string corto;
            try { corto = new AssemblyName(e.Name).Name; } catch { return null; }
            lock (cargadas)
            {
                Assembly a;
                if (cargadas.TryGetValue(corto, out a)) return a;
                try
                {
                    using (Stream st = typeof(Pulso).Assembly.GetManifestResourceStream("lib." + corto + ".dll"))
                    {
                        if (st != null)
                        {
                            byte[] b = new byte[st.Length];
                            int leido = 0;
                            while (leido < b.Length) { int n = st.Read(b, leido, b.Length - leido); if (n <= 0) break; leido += n; }
                            a = Assembly.Load(b);
                        }
                    }
                    if (a == null)
                    {
                        string ruta = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib"), corto + ".dll");
                        if (File.Exists(ruta)) a = Assembly.LoadFrom(ruta);
                    }
                }
                catch { a = null; }
                if (a != null) cargadas[corto] = a;
                return a;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolverDeLib;
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { GuardarCrash(e.ExceptionObject); };
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { GuardarCrash(e.Exception); };
            if (!NetAlcanza()) return;
            bool actualizado = false, oculto = false;
            try
            {
                foreach (string a in args)
                {
                    if (a.Equals("/inicio", StringComparison.OrdinalIgnoreCase)) oculto = true;
                    if (a.Equals("/actualizado", StringComparison.OrdinalIgnoreCase)) actualizado = true;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Pulso app = new Pulso();
                if (app.Iniciar(oculto, actualizado)) Application.Run(app);
            }
            catch (Exception ex)
            {
                GuardarCrash(ex);
                if (actualizado) VolverAVersionAnterior(oculto);
            }
        }

        // Las dll de los sensores piden .NET Framework 4.7.2 o mas nuevo. Windows 10 (1803+) y 11 ya lo traen;
        // Windows 7 SP1 y 8.1 necesitan instalar el 4.8. Sin esto la app se caeria con un error confuso.
        static bool NetAlcanza()
        {
            object rel = null;
            try { rel = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", "Release", null); }
            catch { return true; } // si no se puede leer, se intenta igual
            if (rel is int && (int)rel >= 461808) return true; // 461808 = 4.7.2
            // Windows 8 (no 8.1) no puede instalar .NET 4.7.2 ni 4.8: no sirve mandarlo a la descarga
            Version os = WinVer;
            if (os.Major == 6 && os.Minor == 2)
            {
                bool es8 = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "es";
                MessageBox.Show(es8 ? "Pulso necesita Windows 7 SP1, 8.1, 10 u 11. Windows 8 no puede instalar el .NET Framework que hace falta: actualizá a Windows 8.1 (es gratis)."
                    : "Pulso needs Windows 7 SP1, 8.1, 10 or 11. Windows 8 cannot install the required .NET Framework: update to Windows 8.1 (it is free).", Nombre, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            bool es = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "es";
            if (MessageBox.Show(es ? "Pulso necesita .NET Framework 4.8 (gratis, de Microsoft).\r\n\r\n¿Abrir la página de descarga?"
                    : "Pulso needs .NET Framework 4.8 (free, from Microsoft).\r\n\r\nOpen the download page?",
                    Nombre, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start("https://dotnet.microsoft.com/download/dotnet-framework/net48"); } catch { }
            }
            return false;
        }
    }
}
