// Xuca Monitor: vigila la red y los USB desde la bandeja (al lado del reloj).
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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Xuca
{
    // ---------- Lo que comparten Red y USB ----------
    public abstract class Modulo
    {
        public string Clave, NombreEs, NombreEn, RegistroArch, HistArch, ContEs, ContEn, VacioEs, VacioEn, ApagadoEs, ApagadoEn;
        public XucaMonitor App;
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

        // Aviso que se ve en la ventana pero no se guarda
        protected void Avisar(string texto) { App.Aviso(this, texto); }

        internal void Limpiar()
        {
            lock (Cerrojo)
            {
                Bloques.Clear();
                Cuenta = 0;
                try { File.Delete(HistF); } catch { }
            }
        }

        internal string Texto()
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

        internal int Contador() { lock (Cerrojo) return Cuenta; }

        // Pasa trabajo al hilo de la ventana
        protected void EnUi(MethodInvoker m) { App.EnUi(m); }
    }

    // ---------- La app ----------
    public class XucaMonitor : ApplicationContext
    {
        public const string Nombre = "Xuca Monitor";

        string Dir, Datos, CfgF, Exe, InicioLnk;
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
        CheckBox chkInicio, chkActivo;
        readonly List<Button> botones = new List<Button>();
        readonly Dictionary<Modulo, Button> pestanas = new Dictionary<Modulo, Button>();
        Button bEsc, bReg, bLimp, bTema, bIdioma, bKofi;
        FlowLayoutPanel arriba, fila2;
        Panel medio, caja;

        static string T(string es, string en) { return Modulo.T(es, en); }

        // Devuelve false si ya habia otro abierto (a ese se le pide que muestre su ventana)
        public bool Iniciar(bool oculto)
        {
            bool creado;
            mutex = new Mutex(true, "Local\\XucaMonitor", out creado);
            evMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\XucaMonitor_Mostrar");
            if (!creado) { evMostrar.Set(); return false; }

            Exe = Application.ExecutablePath;
            Dir = Path.GetDirectoryName(Exe);
            // Registros, historial y ajustes van a Documentos\Xuca Monitor (asi el .exe puede estar en cualquier lado)
            Datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Nombre);
            try { Directory.CreateDirectory(Datos); } catch { Datos = Dir; }
            // Lo que haya quedado al lado del .exe (versiones anteriores) se mueve a la carpeta nueva
            if (Datos != Dir)
            {
                foreach (string f in new string[] { "ajustes_monitores.txt", "registro_red.txt", "historial_red.txt", "registro_usb.txt", "historial_usb.txt" })
                {
                    string viejo = Path.Combine(Dir, f), nuevo = Path.Combine(Datos, f);
                    try { if (File.Exists(viejo) && !File.Exists(nuevo)) File.Move(viejo, nuevo); } catch { }
                }
            }
            CfgF = Path.Combine(Datos, "ajustes_monitores.txt");
            string inicio = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            InicioLnk = Path.Combine(inicio, Nombre + ".lnk");

            Dictionary<string, string> aj = LeerAjustes();
            string v;
            if (aj.TryGetValue("idioma", out v) && (v == "es" || v == "en")) Modulo.Idioma = v;
            else Modulo.Idioma = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
            if (aj.TryGetValue("tema", out v) && (v == "sistema" || v == "claro" || v == "oscuro")) tema = v;

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

            // Los accesos de inicio de los monitores viejos (Monitor USB y Monitor de red) se cambian por uno solo
            bool habiaViejo = false;
            foreach (string viejo in new string[] { "Monitor USB.lnk", "Monitor de red.lnk" })
            {
                string f = Path.Combine(inicio, viejo);
                if (File.Exists(f)) { habiaViejo = true; try { File.Delete(f); } catch { } }
            }
            if (habiaViejo || File.Exists(InicioLnk)) { try { Acceso(InicioLnk, "/inicio"); } catch { } }

            foreach (Modulo m in modulos)
            {
                string on;
                bool prender = !aj.TryGetValue(m.Clave, out on) || on != "0";
                if (prender) m.Prender(true);
            }
            ActualizarTextos();
            if (!oculto) MostrarVentana(); else Recortar();
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
            ni.Visible = false;
            ni.Dispose();
            if (fv != null) { fv.Dispose(); fv = null; }
            try { mutex.ReleaseMutex(); } catch { }
            ExitThread();
        }

        public void EnUi(MethodInvoker m)
        {
            try { if (!salir) Ui.BeginInvoke(m); } catch { }
        }

        // Un modulo agrego algo: se actualiza la ventana (si esta abierta) y el texto de la bandeja
        public void Cambio(Modulo m)
        {
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
        Dictionary<string, string> LeerAjustes()
        {
            Dictionary<string, string> c = new Dictionary<string, string>();
            try
            {
                foreach (string l in File.ReadAllLines(CfgF))
                {
                    int i = l.IndexOf('=');
                    if (i > 0) c[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                }
            }
            catch { }
            return c;
        }

        void GuardarAjuste(string k, string v)
        {
            Dictionary<string, string> c = LeerAjustes();
            c[k] = v;
            List<string> l = new List<string>();
            foreach (KeyValuePair<string, string> p in c) l.Add(p.Key + "=" + p.Value);
            try { File.WriteAllLines(CfgF, l.ToArray()); } catch { }
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

        string TextoTema()
        {
            if (tema == "claro") return T("Tema: Claro", "Theme: Light");
            if (tema == "oscuro") return T("Tema: Oscuro", "Theme: Dark");
            return T("Tema: Sistema", "Theme: System");
        }

        static readonly Color Celeste = Color.FromArgb(34, 211, 238);

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
            Color elegida = osc ? Color.FromArgb(14, 52, 60) : Color.FromArgb(207, 250, 254);
            fv.BackColor = fondo; fv.ForeColor = texto;
            arriba.BackColor = fondo; fila2.BackColor = fondo; medio.BackColor = fondo;
            caja.BackColor = tarj; tb.BackColor = tarj; tb.ForeColor = texto;
            estado.BackColor = fondo; estado.ForeColor = gris;
            chkInicio.ForeColor = texto; chkActivo.ForeColor = texto;
            foreach (Button b in botones)
            {
                bool pest = false, sel = false;
                foreach (KeyValuePair<Modulo, Button> p in pestanas) if (p.Value == b) { pest = true; sel = p.Key == actual; }
                b.BackColor = sel ? elegida : tarj;
                b.ForeColor = texto;
                b.FlatAppearance.BorderColor = sel ? Celeste : linea;
                b.FlatAppearance.BorderSize = pest && sel ? 2 : 1;
                b.FlatAppearance.MouseOverBackColor = sel ? elegida : linea;
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
            miAbrir.Text = T("Abrir", "Open");
            miSalir.Text = T("Salir", "Exit");
            foreach (Modulo m in modulos) { miMod[m].Text = m.Nombre; miMod[m].Checked = m.Activo; }
            if (fv != null)
            {
                fv.Text = Nombre;
                chkInicio.Text = T("Iniciar con Windows", "Start with Windows");
                bEsc.Text = T("Acceso directo en el escritorio", "Desktop shortcut");
                bTema.Text = TextoTema();
                bIdioma.Text = T("English", "Español");
                bKofi.Text = T("♥ Apoyar en Ko-fi", "♥ Support on Ko-fi");
                chkActivo.Text = T("Activado", "Enabled");
                chkActivo.Checked = actual.Activo;
                bReg.Text = T("Abrir registro", "Open log");
                bLimp.Text = T("Limpiar", "Clear");
                LlenarTexto();
            }
            ActualizarEstado();
        }

        void ActualizarEstado()
        {
            if (fv != null)
            {
                foreach (KeyValuePair<Modulo, Button> p in pestanas)
                {
                    Modulo m = p.Key;
                    p.Value.Text = (m.Clave == "red" ? T("Red", "Network") : "USB") + "  " + (m.Activo ? "(" + m.Contador() + ")" : T("(apagado)", "(off)"));
                }
                string cont = T(actual.ContEs, actual.ContEn);
                if (!actual.Activo)
                    estado.Text = T("Apagado: no está vigilando. Prendelo con la casilla \"Activado\".", "Off: not watching. Turn it on with the \"Enabled\" box.") + "   ·   " + cont + ": " + actual.Contador();
                else
                    estado.Text = Modulo.Idioma == "en"
                        ? "Watching since " + actual.Desde.ToString("HH:mm") + " on " + actual.Desde.ToString("MM/dd") + "   ·   " + cont + ": " + actual.Contador() + "   ·   Minimizing or closing keeps it in the tray (next to the clock)."
                        : "Vigilando desde las " + actual.Desde.ToString("HH:mm") + " del " + actual.Desde.ToString("dd/MM") + "   ·   " + cont + ": " + actual.Contador() + "   ·   Minimizar o cerrar la deja en la bandeja (al lado del reloj).";
            }
            // Texto de la bandeja: "Xuca Monitor - Red 2 · USB 5"
            StringBuilder tip = new StringBuilder(Nombre + " -");
            foreach (Modulo m in modulos)
            {
                tip.Append(" " + (m.Clave == "red" ? T("Red", "Net") : "USB") + " " + (m.Activo ? m.Contador().ToString() : "off"));
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
            fv.Size = new Size((int)(940 * k), (int)(600 * k));
            fv.MinimumSize = new Size((int)(600 * k), (int)(340 * k));
            fv.StartPosition = FormStartPosition.CenterScreen;
            fv.Font = new Font("Segoe UI", 9.5f);

            // Fila de arriba: lo de toda la app
            arriba = new FlowLayoutPanel();
            arriba.Dock = DockStyle.Top;
            arriba.Height = (int)(44 * k);
            arriba.Padding = new Padding((int)(10 * k), (int)(9 * k), (int)(10 * k), 0);

            chkInicio = new CheckBox();
            chkInicio.AutoSize = true;
            chkInicio.Margin = new Padding(0, 5, 14, 0);
            chkInicio.Checked = File.Exists(InicioLnk);
            chkInicio.CheckedChanged += delegate
            {
                if (chkInicio.Checked) { try { Acceso(InicioLnk, "/inicio"); } catch { } }
                else { try { File.Delete(InicioLnk); } catch { } }
                if (chkInicio.Checked != File.Exists(InicioLnk)) chkInicio.Checked = File.Exists(InicioLnk);
            };
            arriba.Controls.Add(chkInicio);

            bEsc = Boton(arriba, delegate
            {
                try
                {
                    Acceso(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Nombre + ".lnk"), "");
                    bEsc.Text = T("Listo, ya está en el escritorio", "Done, it is on your desktop");
                }
                catch { bEsc.Text = T("No se pudo crear", "Could not create it"); }
            });
            // Tema: Sistema -> Claro -> Oscuro -> Sistema
            bTema = Boton(arriba, delegate
            {
                tema = tema == "sistema" ? "claro" : (tema == "claro" ? "oscuro" : "sistema");
                GuardarAjuste("tema", tema);
                bTema.Text = TextoTema();
                AplicarTema();
            });
            // El boton muestra el otro idioma
            bIdioma = Boton(arriba, delegate
            {
                Modulo.Idioma = Modulo.Idioma == "en" ? "es" : "en";
                GuardarAjuste("idioma", Modulo.Idioma);
                ActualizarTextos();
            });

            // Ko-fi: celeste fijo, igual que en el Xuca OPtimizer (no cambia con el tema)
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
            fila2.Height = (int)(40 * k);
            fila2.Padding = new Padding((int)(10 * k), (int)(4 * k), (int)(10 * k), 0);
            pestanas.Clear();
            foreach (Modulo m in modulos)
            {
                Modulo mm = m;
                Button p = Boton(fila2, delegate { Elegir(mm); });
                p.Font = new Font("Segoe UI Semibold", 9.5f);
                p.Padding = new Padding((int)(8 * k), 0, (int)(8 * k), 0);
                pestanas[m] = p;
            }
            Label sep = new Label();
            sep.Width = (int)(18 * k);
            sep.Height = 1;
            fila2.Controls.Add(sep);
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
            tb.Font = new Font("Consolas", 10f);

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

            fv.Controls.Add(medio);
            fv.Controls.Add(estado);
            fv.Controls.Add(fila2);
            fv.Controls.Add(arriba);

            // Minimizar o cerrar con la X la manda a la bandeja: la ventana se destruye y se libera la memoria
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
            fv = null; tb = null; estado = null; chkInicio = null; chkActivo = null; botones.Clear(); pestanas.Clear();
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

        [STAThread]
        static void Main(string[] args)
        {
            bool oculto = false;
            foreach (string a in args) if (a.Equals("/inicio", StringComparison.OrdinalIgnoreCase)) oculto = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            XucaMonitor app = new XucaMonitor();
            if (app.Iniciar(oculto)) Application.Run(app);
        }
    }
}
