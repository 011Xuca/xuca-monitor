// Monitores de Xuca: lo que comparten el de USB y el de red.
// Corre en la bandeja (al lado del reloj). La ventana solo existe mientras esta abierta: al cerrarla o minimizarla
// se destruye y se libera la memoria, y lo detectado queda guardado en el historial.
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
    public abstract class MonitorBase : ApplicationContext
    {
        // ---------- Lo que cambia en cada monitor ----------
        protected string NombreEs, NombreEn, Archivo, RegistroArch, HistArch, ContEs, ContEn, VacioEs, VacioEn;

        // ---------- Idioma ----------
        public static string Idioma = "es";
        public static string T(string es, string en) { return Idioma == "en" ? en : es; }
        public static string Fecha(DateTime d) { return d.ToString(Idioma == "en" ? "MM/dd HH:mm:ss" : "dd/MM HH:mm:ss"); }

        // ---------- Rutas ----------
        protected string Dir, LogF, HistF, CfgF, Exe, InicioLnk;

        // ---------- Estado ----------
        protected readonly object Cerrojo = new object();
        readonly List<KeyValuePair<int, string>> bloques = new List<KeyValuePair<int, string>>();
        int cuenta;
        DateTime desde = DateTime.Now;
        string tema = "sistema";
        bool salir;

        // Control oculto para pasar trabajo de los otros hilos a la ventana
        protected Control Ui;
        NotifyIcon ni;
        ToolStripMenuItem miAbrir, miSalir;
        Icon icono, iconoChico;
        Mutex mutex;
        EventWaitHandle evMostrar;
        System.Windows.Forms.Timer timerTema;
        bool oscuroAhora;

        // Ventana (null cuando esta cerrada)
        Form fv;
        TextBox tb;
        Label estado;
        CheckBox chkInicio;
        List<Button> botones = new List<Button>();
        Button bEsc, bReg, bLimp, bTema, bIdioma;
        FlowLayoutPanel arriba;
        Panel medio, caja;

        protected abstract void Empezar();
        protected abstract void Terminar();

        // Devuelve false si ya habia otro abierto (a ese se le pide que muestre su ventana)
        public bool Iniciar(bool oculto)
        {
            bool creado;
            mutex = new Mutex(true, "Local\\Xuca" + Archivo, out creado);
            evMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Xuca" + Archivo + "_Mostrar");
            if (!creado) { evMostrar.Set(); return false; }

            Exe = Application.ExecutablePath;
            Dir = Path.GetDirectoryName(Exe);
            LogF = Path.Combine(Dir, RegistroArch);
            HistF = Path.Combine(Dir, HistArch);
            CfgF = Path.Combine(Dir, "ajustes_monitores.txt");
            InicioLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), NombreEs + ".lnk");

            Dictionary<string, string> aj = LeerAjustes();
            string v;
            if (aj.TryGetValue("idioma", out v) && (v == "es" || v == "en")) Idioma = v;
            else Idioma = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
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

            ni = new NotifyIcon();
            ni.Icon = iconoChico;
            ContextMenuStrip menu = new ContextMenuStrip();
            miAbrir = new ToolStripMenuItem("", null, delegate { MostrarVentana(); });
            miSalir = new ToolStripMenuItem("", null, delegate { Salir(); });
            menu.Items.Add(miAbrir);
            menu.Items.Add(miSalir);
            ni.ContextMenuStrip = menu;
            ni.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MostrarVentana(); };
            ni.Visible = true;

            // Cuando se abre otra copia, esta muestra su ventana
            ThreadPool.RegisterWaitForSingleObject(evMostrar, delegate { EnUi(MostrarVentana); }, null, -1, false);

            // Con el tema "Sistema", cada 5 s se fija si Windows cambio entre claro y oscuro
            timerTema = new System.Windows.Forms.Timer();
            timerTema.Interval = 5000;
            timerTema.Tick += delegate { if (fv != null && tema == "sistema" && EsOscuro() != oscuroAhora) AplicarTema(); };
            timerTema.Start();

            // Si "Iniciar con Windows" estaba puesto con la version vieja (.vbs), el acceso pasa a apuntar a este .exe
            if (File.Exists(InicioLnk)) { try { Acceso(InicioLnk, "/inicio"); } catch { } }

            CargarHistorial();
            ActualizarTextos();
            Log("=== " + T(NombreEs, NombreEn) + T(" iniciado", " started") + " ===");
            Empezar();
            if (!oculto) MostrarVentana(); else Recortar();
            return true;
        }

        void Salir()
        {
            if (salir) return;
            salir = true;
            try { Terminar(); } catch { }
            Log("=== " + T(NombreEs, NombreEn) + T(" cerrado", " closed") + " ===");
            ni.Visible = false;
            ni.Dispose();
            if (fv != null) { fv.Dispose(); fv = null; }
            try { mutex.ReleaseMutex(); } catch { }
            ExitThread();
        }

        protected void EnUi(MethodInvoker m)
        {
            try { if (!salir) Ui.BeginInvoke(m); } catch { }
        }

        // ---------- Archivos ----------
        public void Log(string t)
        {
            lock (Cerrojo)
            {
                try { File.AppendAllText(LogF, DateTime.Now.ToString(Idioma == "en" ? "MM/dd HH:mm:ss" : "dd/MM HH:mm:ss") + "  " + t + "\r\n", Encoding.UTF8); } catch { }
            }
        }

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

        // Historial: bloques "@@N" + texto (N = cuanto suma al contador). Se guardan los ultimos 300.
        void CargarHistorial()
        {
            try
            {
                if (!File.Exists(HistF)) return;
                string[] partes = System.Text.RegularExpressions.Regex.Split(File.ReadAllText(HistF, Encoding.UTF8), "(?m)^@@(\\d+)\\r?\\n");
                for (int i = 1; i + 1 < partes.Length; i += 2)
                {
                    string t = partes[i + 1].TrimEnd();
                    int n; int.TryParse(partes[i], out n);
                    if (t.Length > 0) { bloques.Add(new KeyValuePair<int, string>(n, t)); cuenta += n; }
                }
                if (bloques.Count > 300)
                {
                    bloques.RemoveRange(0, bloques.Count - 300);
                    StringBuilder sb = new StringBuilder();
                    foreach (KeyValuePair<int, string> b in bloques) sb.Append("@@" + b.Key + "\r\n" + b.Value + "\r\n");
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
                bloques.Add(new KeyValuePair<int, string>(suma, texto));
                if (bloques.Count > 300) bloques.RemoveAt(0);
                cuenta += suma;
                try { File.AppendAllText(HistF, "@@" + suma + "\r\n" + texto + "\r\n", Encoding.UTF8); } catch { }
            }
            EnUi(delegate { LlenarTexto(); ActualizarEstado(); });
        }

        // Aviso que se ve en la ventana pero no se guarda
        protected void Avisar(string texto)
        {
            EnUi(delegate { if (tb != null) tb.Text = texto + "\r\n\r\n" + tb.Text; });
        }

        void Limpiar()
        {
            lock (Cerrojo)
            {
                bloques.Clear();
                cuenta = 0;
                try { File.Delete(HistF); } catch { }
            }
            LlenarTexto();
            ActualizarEstado();
        }

        void LlenarTexto()
        {
            if (tb == null) return;
            StringBuilder sb = new StringBuilder();
            lock (Cerrojo)
            {
                string sep = "\r\n\r\n" + new string('-', 70) + "\r\n\r\n";
                for (int i = bloques.Count - 1; i >= 0; i--)
                {
                    if (sb.Length > 0) sb.Append(sep);
                    sb.Append(bloques[i].Value);
                }
            }
            tb.Text = sb.Length > 0 ? sb.ToString() : T(VacioEs, VacioEn);
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
                ts.InvokeMember("Description", BindingFlags.SetProperty, null, s, new object[] { NombreEs });
                ts.InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
                Marshal.FinalReleaseComObject(s);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int ExtractIconEx(string f, int i, IntPtr[] g, IntPtr[] c, int n);

        // ---------- Tema ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

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
            fv.BackColor = fondo; fv.ForeColor = texto;
            arriba.BackColor = fondo; medio.BackColor = fondo;
            caja.BackColor = tarj; tb.BackColor = tarj; tb.ForeColor = texto;
            estado.BackColor = fondo; estado.ForeColor = gris;
            chkInicio.ForeColor = texto;
            foreach (Button b in botones)
            {
                b.BackColor = tarj; b.ForeColor = texto;
                b.FlatAppearance.BorderColor = linea; b.FlatAppearance.MouseOverBackColor = linea;
            }
            // Barra de titulo oscura o clara (Windows 10 20H1 o mas nuevo)
            try
            {
                int v = osc ? 1 : 0;
                DwmSetWindowAttribute(fv.Handle, 20, ref v, 4);
                DwmSetWindowAttribute(fv.Handle, 19, ref v, 4);
                if (fv.Visible) { fv.Width += 1; fv.Width -= 1; }
            }
            catch { }
        }

        // ---------- Textos (para cambiar de idioma sin cerrar) ----------
        void ActualizarTextos()
        {
            miAbrir.Text = T("Abrir", "Open");
            miSalir.Text = T("Salir", "Exit");
            if (fv != null)
            {
                fv.Text = T(NombreEs, NombreEn);
                chkInicio.Text = T("Iniciar con Windows", "Start with Windows");
                bEsc.Text = T("Acceso directo en el escritorio", "Desktop shortcut");
                bReg.Text = T("Abrir registro", "Open log");
                bLimp.Text = T("Limpiar", "Clear");
                bTema.Text = TextoTema();
                bIdioma.Text = T("English", "Español");
                LlenarTexto();
            }
            ActualizarEstado();
        }

        void ActualizarEstado()
        {
            string cont = T(ContEs, ContEn);
            int c;
            lock (Cerrojo) c = cuenta;
            if (estado != null)
            {
                estado.Text = Idioma == "en"
                    ? "Watching since " + desde.ToString("HH:mm") + " on " + desde.ToString("MM/dd") + "   ·   " + cont + ": " + c + "   ·   Minimizing or closing keeps it in the tray (next to the clock)."
                    : "Vigilando desde las " + desde.ToString("HH:mm") + " del " + desde.ToString("dd/MM") + "   ·   " + cont + ": " + c + "   ·   Minimizar o cerrar la deja en la bandeja (al lado del reloj).";
            }
            string tip = T(NombreEs, NombreEn) + " - " + cont + ": " + c;
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            ni.Text = tip;
        }

        // ---------- Ventana ----------
        Button Boton(EventHandler click)
        {
            Button b = new Button();
            b.AutoSize = true;
            b.Height = (int)(28 * b.DeviceDpi / 96f);
            b.FlatStyle = FlatStyle.Flat;
            b.Margin = new Padding(0, 0, 8, 0);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.Click += click;
            arriba.Controls.Add(b);
            botones.Add(b);
            return b;
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
            fv.Size = new Size((int)(900 * k), (int)(580 * k));
            fv.MinimumSize = new Size((int)(560 * k), (int)(320 * k));
            fv.StartPosition = FormStartPosition.CenterScreen;
            fv.Font = new Font("Segoe UI", 9.5f);

            arriba = new FlowLayoutPanel();
            arriba.Dock = DockStyle.Top;
            arriba.Height = (int)(46 * k);
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

            bEsc = Boton(delegate
            {
                try
                {
                    Acceso(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), NombreEs + ".lnk"), "");
                    bEsc.Text = T("Listo, ya está en el escritorio", "Done, it is on your desktop");
                }
                catch { bEsc.Text = T("No se pudo crear", "Could not create it"); }
            });
            bReg = Boton(delegate { if (File.Exists(LogF)) Process.Start("notepad.exe", "\"" + LogF + "\""); });
            bLimp = Boton(delegate { Limpiar(); });
            // Tema: Sistema -> Claro -> Oscuro -> Sistema
            bTema = Boton(delegate
            {
                tema = tema == "sistema" ? "claro" : (tema == "claro" ? "oscuro" : "sistema");
                GuardarAjuste("tema", tema);
                bTema.Text = TextoTema();
                AplicarTema();
            });
            // El boton muestra el otro idioma
            bIdioma = Boton(delegate
            {
                Idioma = Idioma == "en" ? "es" : "en";
                GuardarAjuste("idioma", Idioma);
                ActualizarTextos();
            });

            tb = new TextBox();
            tb.Multiline = true;
            tb.ReadOnly = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.Dock = DockStyle.Fill;
            tb.BorderStyle = BorderStyle.None;
            tb.Font = new Font("Consolas", 10f);

            medio = new Panel();
            medio.Dock = DockStyle.Fill;
            medio.Padding = new Padding(10, 4, 10, 4);
            caja = new Panel();
            caja.Dock = DockStyle.Fill;
            caja.Padding = new Padding(10);
            caja.Controls.Add(tb);
            medio.Controls.Add(caja);

            estado = new Label();
            estado.Dock = DockStyle.Bottom;
            estado.Height = (int)(28 * k);
            estado.Padding = new Padding(12, 0, 0, 4);
            estado.TextAlign = ContentAlignment.MiddleLeft;

            fv.Controls.Add(medio);
            fv.Controls.Add(estado);
            fv.Controls.Add(arriba);

            // Minimizar o cerrar con la X la manda a la bandeja: la ventana se destruye y se libera la memoria
            fv.Resize += delegate { if (fv != null && fv.WindowState == FormWindowState.Minimized) CerrarVentana(); };
            fv.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (!salir && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; CerrarVentana(); }
            };

            ActualizarTextos();
            AplicarTema();
            fv.Show();
            fv.Activate();
        }

        void CerrarVentana()
        {
            Form f = fv;
            if (f == null) return;
            fv = null; tb = null; estado = null; chkInicio = null; botones.Clear();
            f.Hide();
            Ui.BeginInvoke((MethodInvoker)delegate { f.Dispose(); Recortar(); });
        }

        // ---------- Memoria ----------
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);

        // Devuelve a Windows la memoria que no se esta usando (cuando no hay ventana abierta)
        protected void Recortar()
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
    }
}
