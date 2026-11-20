// Pulso: grabacion de sesion. El usuario elige cuanto tiempo (1 a 30 min) y usa la PC o juega normal: Pulso registra el
// rendimiento real de lo que tenga al frente (no es un benchmark sintetico) y al terminar deja en esta pestaña un reporte
// con FPS, 1 % y 0.1 % low, frametimes, temperaturas, VRAM, RAM y paginacion (Bench.cs), el cuello de botella, el estado
// de Windows y del hardware, y que conviene hacer (Diagnostico, abajo). La medicion la hace OvMgr (Overlay.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Xuca
{
    public class MonitorSesion : Modulo
    {
        static readonly int[] Minutos = { 1, 5, 10, 15, 30 };

        public MonitorSesion()
        {
            Clave = "sesion"; PestEs = "Sesión"; PestEn = "Session"; TipEs = "Ses"; TipEn = "Ses";
            NombreEs = "Grabación de sesión"; NombreEn = "Session recording";
            RegistroArch = "registro_sesion.txt"; HistArch = "historial_sesion.txt";
            ContEs = "Sesiones"; ContEn = "Sessions";
            VacioEs = "Todavía no grabaste ninguna sesión. Elegí la duración, tocá \"Grabar sesión\" y usá la PC o jugá como siempre: Pulso mide lo que tengas al frente. Al terminar aparece acá el reporte: FPS promedio, 1 % y 0.1 % low, frametimes, temperaturas y uso, VRAM, RAM, paginación, qué componente limita el rendimiento y qué conviene hacer.";
            VacioEn = "You have not recorded a session yet. Pick the length, press \"Record session\" and use the PC or play as usual: Pulso measures whatever is in front. When it ends, the report shows up here: average FPS, 1% and 0.1% low, frametimes, temperatures and load, VRAM, RAM, paging, which part limits performance and what to do about it.";
            ApagadoEs = "La grabación de sesión está apagada. Prendela con la casilla \"Activado\".";
            ApagadoEn = "Session recording is off. Turn it on with the \"Enabled\" box.";
        }

        protected override void Empezar() { }
        protected override void Terminar() { if (OvMgr.Grabando) OvMgr.TerminarSesion(); }

        // Brío (la app hermana que optimiza Windows) se instala en %LOCALAPPDATA%\Brio
        internal static string BrioRuta()
        {
            string p = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Brio"), "Brio.bat");
            return File.Exists(p) ? p : null;
        }

        static void AbrirBrio()
        {
            try
            {
                string r = BrioRuta();
                if (r != null) { ProcessStartInfo p = new ProcessStartInfo(r); p.WorkingDirectory = Path.GetDirectoryName(r); Process.Start(p); }
                else Process.Start("https://github.com/011Xuca/brio#instalar");
            }
            catch { }
        }

        public override Control PanelExtra()
        {
            float k = 1;
            FlowLayoutPanel f = new FlowLayoutPanel();
            try { k = f.DeviceDpi / 96f; } catch { }
            f.Height = (int)(78 * k);
            f.Padding = new Padding((int)(10 * k), (int)(6 * k), (int)(10 * k), 0);
            f.WrapContents = true;

            Label dur = new Label(); dur.AutoSize = true; dur.Margin = new Padding(0, (int)(7 * k), (int)(6 * k), 0);
            dur.Text = T("Duración:", "Length:");
            f.Controls.Add(dur);
            int elegido; if (!int.TryParse(App.Ajuste("sesion_min") ?? "", out elegido)) elegido = 5;
            List<RadioButton> radios = new List<RadioButton>();
            foreach (int m in Minutos)
            {
                RadioButton r = new RadioButton(); r.AutoSize = true; r.Text = m + " min"; r.Tag = m; r.Checked = m == elegido;
                r.Margin = new Padding(0, (int)(5 * k), (int)(4 * k), 0);
                r.CheckedChanged += delegate { if (r.Checked) App.GuardarAjuste("sesion_min", r.Tag.ToString()); };
                radios.Add(r); f.Controls.Add(r);
            }
            if (!radios.Exists(delegate (RadioButton r) { return r.Checked; })) radios[1].Checked = true;

            Button grabar = Boton(f, k);
            Button brio = Boton(f, k);
            Label est = new Label(); est.AutoSize = true; est.Margin = new Padding(0, (int)(4 * k), 0, 0);
            est.MaximumSize = new Size((int)(900 * k), 0);
            f.Controls.Add(est);
            f.SetFlowBreak(brio, true);

            grabar.Click += delegate
            {
                if (OvMgr.Grabando) { OvMgr.TerminarSesion(); return; }
                if (!Pulso.Win10) { MessageBox.Show(Pulso.SoloWin10("La grabación de sesión", "Session recording"), Pulso.Nombre); return; }
                if (!Activo) return;
                int min = 5; foreach (RadioButton r in radios) if (r.Checked) min = (int)r.Tag;
                string err = OvMgr.GrabarSesion(min * 60);
                if (err != null) MessageBox.Show(err, Pulso.Nombre, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            brio.Click += delegate { AbrirBrio(); };

            Timer t = new Timer(); t.Interval = 500;
            MethodInvoker poner = delegate
            {
                bool g = OvMgr.Grabando;
                grabar.Text = g ? T("■  Detener y armar el reporte", "■  Stop and build the report") : T("●  Grabar sesión", "●  Record session");
                grabar.Enabled = g || (Activo && Pulso.Win10);
                foreach (RadioButton r in radios) r.Enabled = !g;
                brio.Text = BrioRuta() != null ? T("Abrir Brío", "Open Brío") : T("Descargar Brío", "Get Brío");
                if (!Pulso.Win10) est.Text = Pulso.SoloWin10("La grabación de sesión", "Session recording");
                else if (!Activo) est.Text = T(ApagadoEs, ApagadoEn);
                else if (g)
                {
                    TimeSpan q = OvMgr.Falta;
                    est.Text = T("Grabando: quedan ", "Recording: ") + ((int)q.TotalMinutes) + ":" + q.Seconds.ToString("00") + T(". Usá la PC o jugá normal; al terminar el reporte aparece abajo.", " left. Use the PC or play as usual; the report shows up below when it ends.");
                }
                else est.Text = T("Mide lo que tengas al frente (un juego, un editor de video, el navegador). Brío es la app hermana de Pulso que optimiza Windows: el reporte te dice cuándo conviene usarla.",
                                  "It measures whatever is in front (a game, a video editor, the browser). Brío is Pulso's sister app that optimizes Windows: the report tells you when it is worth using.");
            };
            t.Tick += delegate { poner(); };
            poner();
            t.Start();
            f.Disposed += delegate { t.Stop(); t.Dispose(); };
            return f;
        }

        static Button Boton(FlowLayoutPanel f, float k)
        {
            Button b = new Button(); b.AutoSize = true; b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand;
            b.FlatAppearance.BorderColor = Color.FromArgb(120, 128, 140);
            b.Margin = new Padding((int)(10 * k), 0, 0, (int)(4 * k));
            b.Padding = new Padding((int)(6 * k), 0, (int)(6 * k), 0);
            f.Controls.Add(b);
            return b;
        }
    }

    // ---------- Cuello de botella, estado de Windows y del hardware, y que hacer (solo en el reporte de una sesion) ----------
    internal static class Diagnostico
    {
        internal class Datos
        {
            public double Fps = double.NaN, Low1 = double.NaN, Minutos, CpuUProm = double.NaN, GpuUProm = double.NaN, NucleoProm = double.NaN, PagProm = double.NaN, RamTotalGB, RamGbMax;
            public float CpuTMax = float.NaN, GpuTMax = float.NaN, LimCpu, RamMax = float.NaN, VramMax = float.NaN, VramTotal = float.NaN, PagMax = float.NaN;
            public int Tirones, ThrottleCpu, ThrottleGpu, Problemas;
            public string Ruta, GpuNombre = "";
        }

        static string T(string es, string en) { return Modulo.T(es, en); }
        static string F0(double v) { return double.IsNaN(v) ? "-" : Math.Round(v).ToString(CultureInfo.InvariantCulture); }
        static string F1(double v) { return double.IsNaN(v) ? "-" : v.ToString("0.0", CultureInfo.InvariantCulture); }
        static bool Hay(double v) { return !double.IsNaN(v); }

        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        // Notebooks segun el tipo de gabinete de la tabla SMBIOS (Win32_SystemEnclosure.ChassisTypes)
        static readonly int[] ChasisNotebook = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
        static readonly Regex Integrada = new Regex(@"UHD|Iris|HD Graphics|Radeon\(TM\) Graphics|Radeon Graphics|Vega \d+|Radeon \d{3}M|\b[67]\d0M\b", RegexOptions.IgnoreCase);
        static readonly Regex Recortado = new Regex(@"tiny\s*1[01]|atlas|revi|ghost\s*spectre|superlite|xlite|\blite\b|debloat", RegexOptions.IgnoreCase);
        static readonly Regex InicioPesado = new Regex(@"OneDrive|Teams|Discord|Steam|EpicGames|Spotify|Battle\.net|EADesktop|Origin|Ubisoft|Dropbox|GoogleDrive|Skype|Opera|CCleaner|Razer|iCUE|Armoury|Widgets|PhoneExperience|Copilot", RegexOptions.IgnoreCase);
        static readonly Regex FondoPesado = new Regex(@"^(OneDrive|Widgets|WidgetService|PhoneExperienceHost|Teams|ms-teams|SearchApp|Copilot)$", RegexOptions.IgnoreCase);

        public static List<string> Armar(Datos d)
        {
            List<string> L = new List<string>();
            List<string> brio = new List<string>();   // lo que Brío puede resolver, segun lo que se encontro
            InfoFija F = MonitorPC.Instancia != null ? MonitorPC.Instancia.Fija : null;
            Lectura pcL = MonitorPC.Instancia != null ? MonitorPC.Instancia.Ultima : null;

            // ---- Sistema ----
            string prod = "", edicion = "", version = "", duenio = "";
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    prod = (k.GetValue("ProductName") as string) ?? ""; edicion = (k.GetValue("EditionID") as string) ?? "";
                    version = (k.GetValue("DisplayVersion") as string) ?? (k.GetValue("ReleaseId") as string) ?? "";
                    duenio = ((k.GetValue("RegisteredOwner") as string) ?? "") + " " + ((k.GetValue("RegisteredOrganization") as string) ?? "");
                }
            }
            catch { }
            bool win11 = Pulso.WinVer.Major >= 10 && Pulso.WinVer.Build >= 22000;
            if (win11) prod = Regex.Replace(prod, @"Windows 10", "Windows 11");   // Windows 11 sigue diciendo "Windows 10" en ProductName
            // La edicion se muestra solo si no es una de las comunes (que ya dice ProductName): LTSC, IoT, N, Server...
            string edicionVer = Regex.IsMatch(edicion, "^(Core|CoreSingleLanguage|Professional|Enterprise|Education)$", RegexOptions.IgnoreCase) ? "" : edicion;
            bool recortado = Recortado.IsMatch(prod + " " + edicion + " " + duenio) || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AtlasOS") != null;
            string fab = "", modelo = ""; bool notebook = false;
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get()) { fab = ((o["Manufacturer"] ?? "") + "").Trim(); modelo = ((o["Model"] ?? "") + "").Trim(); o.Dispose(); }
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    foreach (ManagementObject o in s.Get())
                    {
                        ushort[] tipos = o["ChassisTypes"] as ushort[];
                        if (tipos != null) foreach (ushort t in tipos) if (Array.IndexOf(ChasisNotebook, (int)t) >= 0) notebook = true;
                        o.Dispose();
                    }
            }
            catch { }
            if (Regex.IsMatch(fab + modelo, "System manufacturer|System Product Name|To Be Filled|Default string", RegexOptions.IgnoreCase)) modelo = "";
            Rectangle pantalla = Screen.PrimaryScreen.Bounds;

            // ---- Cuello de botella: CPU vs GPU, VRAM, RAM disponible, disco y resolucion ----
            L.Add("");
            L.Add(T("CUELLO DE BOTELLA:", "BOTTLENECK:"));
            List<string> limita = new List<string>();
            bool gpuTope = Hay(d.GpuUProm) && d.GpuUProm >= 95, cpuTope = Hay(d.GpuUProm) && d.GpuUProm < 80 && Hay(d.NucleoProm) && d.NucleoProm >= 90;
            bool vramTope = !float.IsNaN(d.VramMax) && !float.IsNaN(d.VramTotal) && d.VramTotal > 0 && d.VramMax >= d.VramTotal * 0.95f;
            bool ramTope = (!float.IsNaN(d.RamMax) && d.RamMax >= 90) || (d.RamTotalGB > 0 && d.RamTotalGB - d.RamGbMax < 1.0);
            bool pagina = (Hay(d.PagProm) && d.PagProm >= 300) || (!float.IsNaN(d.PagMax) && d.PagMax >= 2000);
            DiscoFisico discoSis = Disco(pcL, Path.GetPathRoot(Environment.SystemDirectory)), discoApp = d.Ruta != null ? Disco(pcL, Path.GetPathRoot(d.Ruta)) : null;
            if (gpuTope) limita.Add(T("La placa de video (" + F0(d.GpuUProm) + " % de uso promedio) es el límite: es lo esperable en un juego exigente.", "The graphics card (" + F0(d.GpuUProm) + " % average load) is the limit: expected in a demanding game."));
            if (cpuTope) limita.Add(T("El procesador: su núcleo más cargado estuvo al " + F0(d.NucleoProm) + " % mientras la placa de video esperaba al " + F0(d.GpuUProm) + " %.", "The CPU: its busiest core was at " + F0(d.NucleoProm) + " % while the graphics card waited at " + F0(d.GpuUProm) + " %."));
            if (vramTope) limita.Add(T("La VRAM: llegó a " + F1(d.VramMax / 1024) + " de " + F1(d.VramTotal / 1024) + " GB. Al llenarse, las texturas pasan a la RAM y aparecen tirones.", "VRAM: it reached " + F1(d.VramMax / 1024) + " of " + F1(d.VramTotal / 1024) + " GB. Once full, textures spill into RAM and stutters appear."));
            if (ramTope) limita.Add(T("La RAM: llegó al " + F0(d.RamMax) + " % (" + F1(d.RamGbMax) + " de " + F1(d.RamTotalGB) + " GB) y casi no quedó memoria libre.", "RAM: it reached " + F0(d.RamMax) + " % (" + F1(d.RamGbMax) + " of " + F1(d.RamTotalGB) + " GB) with almost no free memory left."));
            if (pagina) limita.Add(T("El disco: Windows leyó en promedio " + F0(d.PagProm) + " páginas/s del disco (pico " + F0(d.PagMax) + ") porque no estaban en la RAM" + (discoSis != null ? ", y el disco del sistema es " + discoSis.Tipo : "") + ".", "The drive: Windows read an average of " + F0(d.PagProm) + " pages/s from disk (peak " + F0(d.PagMax) + ") because they were not in RAM" + (discoSis != null ? ", and the system drive is " + discoSis.Tipo : "") + "."));
            if (limita.Count == 0)
                L.Add(T("  Ningún componente quedó al tope" + (Hay(d.GpuUProm) ? " (placa de video al " + F0(d.GpuUProm) + " % en promedio)" : "") + ": hay margen. Si jugabas, probablemente hay un tope de FPS o VSync.", "  No part was maxed out" + (Hay(d.GpuUProm) ? " (graphics card at " + F0(d.GpuUProm) + " % on average)" : "") + ": there is headroom. If you were gaming, there is probably an FPS cap or VSync."));
            foreach (string x in limita) L.Add("  - " + x);
            L.Add(T("  Resolución activa: ", "  Active resolution: ") + pantalla.Width + "x" + pantalla.Height + (discoApp != null ? T("   ·   la app corre desde un ", "   ·   the app runs from a ") + discoApp.Tipo + " (" + discoApp.Letras + ")" : ""));

            // ---- Diagnostico ----
            List<string> D = new List<string>();
            // Windows
            ulong upMs = 0; try { upMs = GetTickCount64(); } catch { }
            double upDias = upMs / 86400000.0;
            if (upDias >= 7) { D.Add(T("Windows lleva " + F0(upDias) + " días sin reiniciarse (el \"Apagar\" con inicio rápido no cuenta): se acumulan procesos y memoria ocupada. Reiniciá antes de jugar.", "Windows has been up for " + F0(upDias) + " days without a restart (\"Shut down\" with fast startup does not count): processes and used memory pile up. Restart before gaming.")); }
            int procs = 0, servicios = 0; List<string> fondo = new List<string>();
            try { foreach (Process p in Process.GetProcesses()) { procs++; try { if (FondoPesado.IsMatch(p.ProcessName) && !fondo.Contains(p.ProcessName)) fondo.Add(p.ProcessName); } catch { } p.Dispose(); } } catch { }
            try { foreach (System.ServiceProcess.ServiceController s in System.ServiceProcess.ServiceController.GetServices()) { try { if (s.Status == System.ServiceProcess.ServiceControllerStatus.Running) servicios++; } catch { } s.Dispose(); } } catch { }
            List<string> inicio = Inicio();
            List<string> pesados = inicio.FindAll(delegate (string x) { return InicioPesado.IsMatch(x); });
            bool telemetria = false; try { using (System.ServiceProcess.ServiceController s = new System.ServiceProcess.ServiceController("DiagTrack")) telemetria = s.Status == System.ServiceProcess.ServiceControllerStatus.Running; } catch { }
            if (procs >= 220 || servicios >= 130 || fondo.Count > 0 || pesados.Count >= 3)
            {
                D.Add(T("Mucho en segundo plano: " + procs + " procesos y " + servicios + " servicios andando, " + inicio.Count + " programas en el inicio automático" + (pesados.Count > 0 ? " (" + string.Join(", ", pesados.ToArray()) + ")" : "") + (fondo.Count > 0 ? "; abiertos de fondo: " + string.Join(", ", fondo.ToArray()) : "") + (telemetria ? "; la telemetría de Windows (DiagTrack) está activa" : "") + ".",
                        "A lot in the background: " + procs + " processes and " + servicios + " services running, " + inicio.Count + " programs in automatic startup" + (pesados.Count > 0 ? " (" + string.Join(", ", pesados.ToArray()) + ")" : "") + (fondo.Count > 0 ? "; open in the background: " + string.Join(", ", fondo.ToArray()) : "") + (telemetria ? "; Windows telemetry (DiagTrack) is running" : "") + "."));
                brio.Add(T("limpiar el inicio automático y los servicios y procesos de fondo que no usás", "clean up startup and the background services and processes you do not use"));
            }
            List<string> eventos = Eventos();
            if (eventos.Count > 0)
            {
                bool disco = eventos.Exists(delegate (string x) { return Regex.IsMatch(x, @"^(disk|Ntfs|volmgr|stornvme|storahci)\b", RegexOptions.IgnoreCase); });
                bool cortes = eventos.Exists(delegate (string x) { return Regex.IsMatch(x, @"^(Kernel-Power|BugCheck|WHEA-Logger)\b", RegexOptions.IgnoreCase); });
                D.Add(T("Errores críticos en el visor de eventos de los últimos 7 días (" + string.Join(", ", eventos.ToArray()) + "). "
                        + (disco ? "Hay errores de disco: corré \"chkdsk C: /scan\" y revisá la salud en la pestaña Discos. " : "")
                        + (cortes ? "La PC se apagó de golpe, tuvo un pantallazo o un error de hardware: el detalle está en la pestaña Errores. " : "")
                        + "Para descartar archivos de Windows dañados, en una terminal como administrador: \"sfc /scannow\" y después \"DISM /Online /Cleanup-Image /RestoreHealth\".",
                        "Critical errors in Event Viewer in the last 7 days (" + string.Join(", ", eventos.ToArray()) + "). "
                        + (disco ? "There are disk errors: run \"chkdsk C: /scan\" and check health in the Drives tab. " : "")
                        + (cortes ? "The PC shut down suddenly, blue-screened or had a hardware error: the details are in the Errors tab. " : "")
                        + "To rule out damaged Windows files, in an admin terminal: \"sfc /scannow\" and then \"DISM /Online /Cleanup-Image /RestoreHealth\"."));
            }
            double ramVisible = d.RamTotalGB;
            if (win11 && !recortado && ramVisible > 0 && ramVisible <= 8.5)
            {
                D.Add(T("Windows 11 completo con " + F0(ramVisible) + " GB de RAM: el sistema solo ya ocupa 3 a 4 GB y le deja poco al juego.", "Full Windows 11 with " + F0(ramVisible) + " GB of RAM: the system alone takes 3 to 4 GB and leaves little for the game."));
                brio.Add(T("aligerar Windows (apps, servicios y efectos que no usás) para liberar RAM", "lighten Windows (apps, services and effects you do not use) to free RAM"));
            }
            // RAM e integrada
            bool igpu = Integrada.IsMatch(d.GpuNombre ?? "") || (F != null && Integrada.IsMatch(F.GpuNombreWmi) && !Regex.IsMatch(F.GpuNombreWmi, "NVIDIA|GeForce|RTX|GTX|Radeon RX|Arc", RegexOptions.IgnoreCase));
            if (F != null && F.RamTotalGB > 0 && F.RamTotalGB <= 8)
            {
                double reservada = ramVisible > 0 ? F.RamTotalGB - ramVisible : 0;
                if (igpu && reservada >= 1.0)
                    D.Add(T("Tenés " + F.RamTotalGB + " GB con gráfica integrada y el hardware se reserva " + F1(reservada) + " GB para video: al juego le queda menos. " + (notebook ? "En notebooks la BIOS suele estar bloqueada: si aparece la opción (UMA Frame Buffer, iGPU Memory o DVMT), bajala a 512 MB; si no, no se puede cambiar." : "Bajala a 512 MB en la BIOS (UMA Frame Buffer Size, iGPU Memory o DVMT Pre-Allocated, según la placa)."),
                            "You have " + F.RamTotalGB + " GB with integrated graphics and the hardware reserves " + F1(reservada) + " GB for video: the game gets less. " + (notebook ? "Laptop BIOSes are often locked: if the option exists (UMA Frame Buffer, iGPU Memory or DVMT), lower it to 512 MB; otherwise it cannot be changed." : "Lower it to 512 MB in the BIOS (UMA Frame Buffer Size, iGPU Memory or DVMT Pre-Allocated, depending on the board).")));
                if (F.RamCanalNivel == Nivel.Amarillo)
                    D.Add(T("La RAM anda en single channel (un solo módulo o mal ubicados): con gráfica integrada eso le quita mucho rendimiento. Con dos módulos iguales en las ranuras que indica el manual pasa a dual channel.", "RAM runs in single channel (one stick or wrong slots): with integrated graphics that costs a lot of performance. Two matching sticks in the slots the manual says give dual channel."));
            }
            if (F != null && F.RamXmpNivel == Nivel.Amarillo)
                D.Add(T("El XMP/EXPO/DOCP está apagado: la RAM anda más lenta que su velocidad de fábrica. Se activa en la BIOS.", "XMP/EXPO/DOCP is off: RAM runs slower than its rated speed. It is enabled in the BIOS."));
            // Disco y paginacion
            bool lentoSis = discoSis != null && discoSis.Lento, lentoApp = discoApp != null && discoApp.Lento;
            if (pagina && (lentoSis || lentoApp))
            {
                D.Add(T("Hubo mucha paginación sobre un " + (lentoSis ? discoSis.Tipo : discoApp.Tipo) + ": eso da tirones (stuttering). Mové el archivo de paginación y el juego al SSD o NVMe más rápido, ajustá la memoria virtual y liberá RAM antes de jugar.", "There was heavy paging on a " + (lentoSis ? discoSis.Tipo : discoApp.Tipo) + ": that causes stuttering. Move the page file and the game to the fastest SSD or NVMe, adjust virtual memory and free RAM before gaming."));
                brio.Add(T("liberar RAM y ajustar la memoria virtual antes de jugar", "free RAM and adjust virtual memory before gaming"));
            }
            else if (pagina)
            {
                D.Add(T("Hubo mucha paginación: Windows tuvo que ir al disco a buscar memoria. Cerrá lo que no uses antes de jugar o sumá RAM.", "There was heavy paging: Windows had to fetch memory from disk. Close what you do not use before gaming or add RAM."));
                brio.Add(T("liberar RAM antes de jugar", "free RAM before gaming"));
            }
            else if (lentoApp)
                D.Add(T("La app corre desde un " + discoApp.Tipo + ": las cargas y los tirones al cargar zonas nuevas mejoran mucho en un SSD o NVMe.", "The app runs from a " + discoApp.Tipo + ": loading and stutters when loading new areas improve a lot on an SSD or NVMe."));
            // Graficos y procesos
            if (gpuTope && Hay(d.Fps) && d.Fps < 60)
                D.Add(T("La placa de video no da más (" + F0(d.Fps) + " FPS promedio a " + pantalla.Width + "x" + pantalla.Height + "): bajá la resolución o usá escalado (DLSS, FSR, XeSS), y bajá sombras, volumétricos y oclusión ambiental.", "The graphics card is maxed out (" + F0(d.Fps) + " FPS average at " + pantalla.Width + "x" + pantalla.Height + "): lower the resolution or use upscaling (DLSS, FSR, XeSS), and lower shadows, volumetrics and ambient occlusion."));
            if (cpuTope || (Hay(d.CpuUProm) && d.CpuUProm >= 85))
            {
                D.Add(T("El procesador está saturado: cerrá los procesos residentes que no necesitás mientras jugás y bajá opciones que cargan al CPU (distancia de dibujado, multitudes, física).", "The CPU is saturated: close the resident processes you do not need while gaming and lower CPU-heavy options (draw distance, crowds, physics)."));
                brio.Add(T("cerrar los procesos residentes y priorizar el juego", "close resident processes and prioritize the game"));
            }
            if (d.ThrottleCpu >= 3 || d.ThrottleGpu >= 3)
                brio.Add(T("dejar el plan de energía y los ajustes de energía en alto rendimiento", "set the power plan and power settings to high performance"));

            L.Add("");
            L.Add(T("DIAGNÓSTICO DEL SISTEMA", "SYSTEM DIAGNOSIS") + " (" + Junta(prod, edicionVer, version) + (recortado ? T(", edición recortada", ", stripped-down edition") : "") + " · " + Junta(fab, modelo) + (fab + modelo != "" ? " · " : "") + (notebook ? "notebook" : T("PC de escritorio", "desktop PC")) + "):");
            if (D.Count == 0) L.Add(T("  Windows y el hardware no muestran nada que frene el rendimiento.", "  Windows and the hardware show nothing holding performance back."));
            foreach (string x in D) L.Add("  - " + x);
            if (recortado) L.Add(T("  - Tu Windows ya es una edición recortada: Brío no necesita volver a quitarle apps.", "  - Your Windows is already a stripped-down edition: Brío does not need to remove apps again."));

            // ---- Cierre: cuando conviene Brío ----
            bool estable = d.Problemas == 0 && D.Count == 0 && limita.Count <= 1 && !vramTope && !ramTope && !pagina
                && (float.IsNaN(d.CpuTMax) || d.CpuTMax < 80) && (float.IsNaN(d.GpuTMax) || d.GpuTMax < 80) && d.ThrottleCpu < 3 && d.ThrottleGpu < 3;
            L.Add("");
            L.Add(T("CÓMO SEGUIR:", "NEXT STEPS:"));
            if (estable && float.IsNaN(d.CpuTMax))
                L.Add(T("  Con los datos que hay vas estable y con margen de carga (la temperatura del procesador no se midió: " + MonitorPC.SinDriverTexto() + "). Usá Brío para activar ajustes avanzados, desbloquear energía y sacarle todo el jugo a tu PC.", "  With the available data you are stable and have load headroom (the CPU temperature was not measured: " + MonitorPC.SinDriverTexto() + "). Use Brío to enable advanced tweaks, unlock power and get the most out of your PC."));
            else if (estable)
                L.Add(T("  Vas muy estable de temperaturas y con margen de carga. Usá Brío para activar ajustes avanzados, desbloquear energía y sacarle todo el jugo a tu PC.", "  You are very stable on temperatures and have load headroom. Use Brío to enable advanced tweaks, unlock power and get the most out of your PC."));
            else if (brio.Count > 0)
                L.Add(T("  Con Brío podés " + string.Join("; ", brio.ToArray()) + ". Después grabá otra sesión igual a esta para comparar.", "  With Brío you can " + string.Join("; ", brio.ToArray()) + ". Then record another session like this one to compare."));
            else
                L.Add(T("  Lo de arriba es de hardware o del juego: resolvelo primero. Cuando esté estable, Brío te ayuda a exprimir el resto.", "  What is above is hardware or game related: fix it first. Once it is stable, Brío helps you squeeze out the rest."));
            L.Add(T("  (El botón \"" + (MonitorSesion.BrioRuta() != null ? "Abrir Brío" : "Descargar Brío") + "\" está arriba, al lado de \"Grabar sesión\".)", "  (The \"" + (MonitorSesion.BrioRuta() != null ? "Open Brío" : "Get Brío") + "\" button is above, next to \"Record session\".)"));
            return L;
        }

        static string Junta(params string[] s)
        {
            List<string> l = new List<string>();
            foreach (string x in s) if (!string.IsNullOrEmpty(x) && x.Trim() != "") l.Add(x.Trim());
            return string.Join(" ", l.ToArray());
        }

        // El disco fisico que tiene esa letra ("C:\")
        static DiscoFisico Disco(Lectura pc, string raiz)
        {
            if (pc == null || string.IsNullOrEmpty(raiz)) return null;
            string letra = raiz.TrimEnd('\\').ToUpperInvariant();
            foreach (DiscoFisico d in pc.Fisicos)
                foreach (string l in d.Letras.Split(' ')) if (l.ToUpperInvariant() == letra && d.Tipo != "") return d;
            return null;
        }

        // Programas en el inicio automatico (Run de HKCU/HKLM y las carpetas Inicio), sin los que estan deshabilitados
        static List<string> Inicio()
        {
            List<string> r = new List<string>();
            string[] claves = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" };
            foreach (RegistryKey raiz in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string c in claves)
                {
                    try
                    {
                        using (RegistryKey k = raiz.OpenSubKey(c))
                        using (RegistryKey ap = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + (c.Contains("WOW6432Node") ? "Run32" : "Run")))
                        {
                            if (k == null) continue;
                            foreach (string n in k.GetValueNames())
                            {
                                byte[] b = ap != null ? ap.GetValue(n) as byte[] : null;
                                if (b != null && b.Length > 0 && (b[0] & 1) == 1) continue;   // deshabilitado en el Administrador de tareas
                                if (n != "" && !r.Contains(n)) r.Add(n);
                            }
                        }
                    }
                    catch { }
                }
            foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
                try { foreach (string a in Directory.GetFiles(Environment.GetFolderPath(sf))) if (!a.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) r.Add(Path.GetFileNameWithoutExtension(a)); } catch { }
            return r;
        }

        // Origen de los errores criticos del registro Sistema en los ultimos 7 dias (criticos, de disco y de hardware)
        static List<string> Eventos()
        {
            List<string> r = new List<string>();
            try
            {
                string q = "*[System[(Level=1 or (Level=2 and (Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='volmgr'] or Provider[@Name='stornvme'] or Provider[@Name='storahci'] or Provider[@Name='Microsoft-Windows-WHEA-Logger']))) and TimeCreated[timediff(@SystemTime) <= 604800000]]]";
                using (System.Diagnostics.Eventing.Reader.EventLogReader lr = new System.Diagnostics.Eventing.Reader.EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery("System", System.Diagnostics.Eventing.Reader.PathType.LogName, q)))
                {
                    Dictionary<string, int> n = new Dictionary<string, int>();
                    for (int i = 0; i < 300; i++)
                    {
                        System.Diagnostics.Eventing.Reader.EventRecord e = lr.ReadEvent();
                        if (e == null) break;
                        string p = e.ProviderName ?? "?";
                        p = p.Replace("Microsoft-Windows-", "");
                        int c; n.TryGetValue(p, out c); n[p] = c + 1;
                        e.Dispose();
                    }
                    foreach (KeyValuePair<string, int> kv in n) r.Add(kv.Key + (kv.Value > 1 ? " x" + kv.Value : ""));
                }
            }
            catch { }
            return r;
        }
    }
}
