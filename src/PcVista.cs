// Panel en vivo de las pestañas de hardware (CPU, GPU, Memoria, Discos). Se dibuja todo a mano en un solo control
// (no un control por dato): asi no parpadea, no gasta memoria cada vez que se actualiza, sigue el tema claro/oscuro
// y la escala de Windows (125 %, 150 %...). Si no entra, se desplaza con la rueda.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Xuca
{
    internal sealed class PcVista : Control
    {
        readonly MonitorPC pc;
        readonly PcPestana pest;
        readonly Timer reloj;
        readonly Font fChica, fNormal, fNegrita, fTitulo, fGrande;
        readonly List<Zona> zonas = new List<Zona>();
        bool verTodos;
        int scroll, contenido, pedida;
        float k = 1;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        class Zona { public Rectangle R; public MethodInvoker Hacer; }

        // cMarca (verde de la app) = controles y seleccion; cAcento (cian) = color de datos de barras y graficos
        Color cFondo, cTarj, cLinea, cTexto, cGris, cAcento, cMarca, cElegida, cVerde, cAmarillo, cRojo, cVioleta;

        public PcVista(MonitorPC pc, PcPestana pest)
        {
            this.pc = pc; this.pest = pest;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            fChica = new Font("Segoe UI", 8.25f);
            fNormal = new Font("Segoe UI", 9f);
            fNegrita = new Font("Segoe UI Semibold", 9f);
            fTitulo = new Font("Segoe UI Semibold", 10.5f);
            fGrande = new Font("Segoe UI Semibold", 19f);
            Height = 220;
            reloj = new Timer();
            reloj.Interval = 1000;
            reloj.Tick += delegate { Invalidate(); };
            reloj.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reloj.Stop(); reloj.Dispose();
                fChica.Dispose(); fNormal.Dispose(); fNegrita.Dispose(); fTitulo.Dispose(); fGrande.Dispose();
            }
            base.Dispose(disposing);
        }

        static string T(string es, string en) { return Modulo.T(es, en); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        int Ppp() { return DeviceDpi; }
        int S(float v) { return (int)Math.Round(v * k); }

        // ---------- Colores (los mismos de la app, segun el tema) ----------
        void Colores()
        {
            bool osc = pc.App != null && pc.App.Oscuro;
            cFondo = osc ? Color.FromArgb(20, 22, 27) : Color.FromArgb(243, 244, 246);
            cTarj = osc ? Color.FromArgb(28, 32, 39) : Color.White;
            cLinea = osc ? Color.FromArgb(46, 52, 64) : Color.FromArgb(209, 213, 219);
            cTexto = osc ? Color.White : Color.FromArgb(17, 24, 39);
            cGris = osc ? Color.FromArgb(150, 160, 175) : Color.FromArgb(107, 114, 128);
            cAcento = osc ? Color.FromArgb(34, 211, 238) : Color.FromArgb(8, 145, 178);
            cMarca = osc ? Color.FromArgb(74, 222, 128) : Color.FromArgb(21, 128, 61);
            cElegida = osc ? Color.FromArgb(22, 50, 35) : Color.FromArgb(220, 252, 231);
            cVerde = osc ? Color.FromArgb(74, 222, 128) : Color.FromArgb(22, 163, 74);
            cAmarillo = osc ? Color.FromArgb(250, 204, 21) : Color.FromArgb(180, 120, 0);
            cRojo = osc ? Color.FromArgb(248, 113, 113) : Color.FromArgb(220, 38, 38);
            cVioleta = osc ? Color.FromArgb(192, 132, 252) : Color.FromArgb(147, 51, 234);
        }

        Color De(Nivel n)
        {
            switch (n)
            {
                case Nivel.Verde: return cVerde;
                case Nivel.Amarillo: return cAmarillo;
                case Nivel.Rojo: return cRojo;
            }
            return cGris;
        }

        static string Palabra(Nivel n, bool temp)
        {
            switch (n)
            {
                case Nivel.Verde: return T("Normal", "Normal");
                case Nivel.Amarillo: return temp ? T("Caliente", "Hot") : T("Alto", "High");
                case Nivel.Rojo: return temp ? T("Al límite", "At limit") : T("Crítico", "Critical");
            }
            return T("Sin dato", "No data");
        }

        static Color Mezcla(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static Color TextoSobre(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? Color.FromArgb(17, 24, 39) : Color.White;
        }

        // ---------- Texto y formas ----------
        const TextFormatFlags F0 = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        const TextFormatFlags FP = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

        void Tx(Graphics g, string s, Font f, Color c, int x, int y) { TextRenderer.DrawText(g, s, f, new Point(x, y), c, F0); }
        void Tx(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra) { TextRenderer.DrawText(g, s, f, r, c, F0 | TextFormatFlags.EndEllipsis | extra); }
        int Ancho(string s, Font f) { return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), F0).Width; }
        int AltoParrafo(string s, Font f, int w) { return TextRenderer.MeasureText(s, f, new Size(Math.Max(10, w), int.MaxValue), FP).Height; }
        void Parrafo(Graphics g, string s, Font f, Color c, Rectangle r) { TextRenderer.DrawText(g, s, f, r, c, FP); }

        static GraphicsPath Redondo(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Caja(Graphics g, Rectangle r, Color fondo, Color borde, int grosor) { Caja(g, r, fondo, borde, grosor, S(8)); }
        void Caja(Graphics g, Rectangle r, Color fondo, Color borde, int grosor, int rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Redondo(r, rad))
            {
                using (SolidBrush b = new SolidBrush(fondo)) g.FillPath(b, p);
                if (grosor > 0) using (Pen pen = new Pen(borde, grosor)) g.DrawPath(pen, p);
            }
        }

        void Barra(Graphics g, Rectangle r, float pct, Color c)
        {
            if (r.Width <= 2) return;
            Caja(g, r, cLinea, cLinea, 0, r.Height / 2);
            if (pct > 0)
            {
                int w = Math.Max(r.Height, (int)(r.Width * Math.Min(100f, pct) / 100f));
                Caja(g, new Rectangle(r.X, r.Y, w, r.Height), c, c, 0, r.Height / 2);
            }
        }

        void Punto(Graphics g, int x, int y, int d, Color c) { using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, x, y, d, d); }

        void Clic(Rectangle r, MethodInvoker m) { Zona z = new Zona(); z.R = r; z.Hacer = m; zonas.Add(z); }

        // ---------- Formatos ----------
        static string Gc(float? v) { return v.HasValue ? ((int)Math.Round(v.Value)).ToString() + " °C" : "—"; }
        static string Gc(float v) { return float.IsNaN(v) ? "—" : ((int)Math.Round(v)).ToString() + " °C"; }
        static string P(float? v) { return v.HasValue ? ((int)Math.Round(v.Value)).ToString() + " %" : "—"; }
        static string Ghz(float? v) { return v.HasValue ? (v.Value / 1000f).ToString("0.00", CI) + " GHz" : "—"; }
        static string Mhz(float? v) { return v.HasValue ? ((int)Math.Round(v.Value)).ToString() + " MHz" : "—"; }
        static string Wt(float? v) { return v.HasValue ? v.Value.ToString(v.Value < 10 ? "0.0" : "0", CI) + " W" : "—"; }
        static string Vt(float? v) { return v.HasValue ? v.Value.ToString("0.000", CI) + " V" : "—"; }
        static string Gb(double v) { return v.ToString(v < 10 ? "0.0" : "0", CI) + " GB"; }
        static string MBs(double v) { return v < 0 ? "—" : v.ToString(v < 10 ? "0.0" : "0", CI) + " MB/s"; }
        static string Junta(params string[] s)
        {
            List<string> l = new List<string>();
            foreach (string x in s) if (!string.IsNullOrEmpty(x) && x != "—") l.Add(x);
            return string.Join("  ·  ", l.ToArray());
        }

        string Maximos(float sesion, float juego, string nomJuego, string unidad)
        {
            if (float.IsNaN(sesion)) return "";
            string s = T("Máx. sesión ", "Session max ") + (int)Math.Round(sesion) + unidad;
            if (!float.IsNaN(juego) && nomJuego != "") s += T("  ·  jugando ", "  ·  gaming ") + (int)Math.Round(juego) + unidad;
            return s;
        }

        // ---------- Mouse ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (Zona z in zonas.ToArray())
                if (z.R.Contains(e.Location)) { try { z.Hacer(); } catch { } Invalidate(); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool sobre = false;
            foreach (Zona z in zonas) if (z.R.Contains(e.Location)) { sobre = true; break; }
            Cursor c = sobre ? Cursors.Hand : Cursors.Default;
            if (Cursor != c) Cursor = c;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, contenido - Height);
            if (max == 0) return;
            scroll = Math.Max(0, Math.Min(max, scroll - e.Delta / 120 * S(40)));
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // Para que la rueda del mouse llegue aca cuando el contenido no entra
            if (contenido > Height && !Focused) Focus();
        }

        // Pide a la ventana la altura que necesita (sin pasar del 62 % de la ventana: el resto es para los avisos)
        void PedirAltura(int h)
        {
            Form f = FindForm();
            int tope = f != null ? (int)(f.ClientSize.Height * 0.62) : S(420);
            h = Math.Max(S(60), Math.Min(h, tope));
            if (Parent == null || Math.Abs(Parent.Height - h) <= 2 || h == pedida) return;
            pedida = h;
            BeginInvoke((MethodInvoker)delegate { if (!IsDisposed && Parent != null) Parent.Height = pedida; });
        }

        // =====================================================================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            try { k = Ppp() / 96f; } catch { k = 1; }
            Colores();
            Graphics g = e.Graphics;
            g.Clear(cFondo);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            zonas.Clear();
            int pad = S(10);
            int x = pad, w = Math.Max(S(240), Width - 2 * pad - S(8));
            int y = S(2) - scroll;
            Lectura L = pc.Ultima;
            InfoFija F = pc.Fija;
            int cat = pest.Cat;

            y += S(6);
            y = Carteles(g, x, y, w, L, cat);
            if (pest.Activo && pc.Activo && L != null && F != null)
            {
                switch (cat)
                {
                    case MonitorPC.CAT_CPU: y = VistaCpu(g, x, y, w, L, F); break;
                    case MonitorPC.CAT_GPU: y = VistaGpu(g, x, y, w, L, F); break;
                    case MonitorPC.CAT_RAM: y = VistaRam(g, x, y, w, L, F); break;
                    default: y = VistaDiscos(g, x, y, w, L); break;
                }
            }
            y += S(4);
            contenido = y + scroll;
            int max = contenido - Height;
            if (scroll > Math.Max(0, max)) { scroll = Math.Max(0, max); Invalidate(); }
            if (max > 0)
            {
                // Barrita de desplazamiento
                int alto = Math.Max(S(24), Height * Height / contenido);
                int top = (Height - alto) * scroll / max;
                Caja(g, new Rectangle(Width - S(6), top, S(4), alto), cLinea, cLinea, 0, S(2));
            }
            PedirAltura(contenido);
        }

        // ---------- Carteles (apagado, PawnIO, errores) ----------
        int Carteles(Graphics g, int x, int y, int w, Lectura L, int cat)
        {
            if (!pest.Activo || !pc.Activo)
                return Cartel(g, x, y, w, Nivel.Gris, T("Esta pestaña está apagada. Prendela con la casilla \"Activado\" de arriba para ver sus datos en vivo.",
                    "This tab is off. Turn it on with the \"Enabled\" box above to see its live data."), null, null);
            if (L == null)
                return Cartel(g, x, y, w, Nivel.Gris, T("Leyendo los sensores...", "Reading sensors..."), null, null);
            if (!L.HaySensores && pc.ErrorSensores != null)
                y = Cartel(g, x, y, w, Nivel.Rojo, T("No se pudieron abrir los sensores (" + pc.ErrorSensores + "). Se vuelve a intentar solo cada 1 minuto. Mientras tanto se muestra lo que da Windows (uso del procesador y RAM).",
                    "Could not open the sensors (" + pc.ErrorSensores + "). It retries by itself every minute. Meanwhile what Windows provides is shown (CPU usage and RAM)."), null, null);
            // PawnIO es una mejora opcional: el cartel solo explica, el boton abre la guia (Pulso nunca baja ni ejecuta el instalador)
            bool sinTempReal = !L.CpuTemp.HasValue || L.CpuTempFuente == "ACPI";
            // Antes que ofrecer PawnIO, decir la verdad: sin administrador la libreria no lee el CPU, y con PawnIO instalado el problema no es que falte
            if (cat == MonitorPC.CAT_CPU && !MonitorPC.EsAdmin && !L.CpuBajoNivel)
                y = Cartel(g, x, y, w, Nivel.Amarillo, T("Pulso no está corriendo como administrador, por eso no puede leer la temperatura, los vatios ni el voltaje del procesador (sin esos permisos la librería de sensores devuelve ceros). Cerralo y abrilo con \"Ejecutar como administrador\".",
                    "Pulso is not running as administrator, so it cannot read the CPU temperature, watts or voltage (without those permissions the sensor library returns zeros). Close it and open it with \"Run as administrator\"."), null, null);
            else if (cat == MonitorPC.CAT_CPU && MonitorPC.PawnOk && MonitorPC.PawnSinDatos && !L.CpuBajoNivel)
                y = Cartel(g, x, y, w, Nivel.Amarillo, T("PawnIO está instalado (v" + MonitorPC.PawnVer + ") pero no entregó datos del procesador: Pulso reintenta solo. Probá cerrar otros programas de sensores (HWiNFO, FanControl, etc.) o reiniciar la PC.",
                    "PawnIO is installed (v" + MonitorPC.PawnVer + ") but gave no CPU data: Pulso is retrying by itself. Try closing other sensor programs (HWiNFO, FanControl, etc.) or restarting the PC."), null, null);
            else if (cat == MonitorPC.CAT_CPU && !MonitorPC.PawnOk && (sinTempReal || MonitorPC.PawnViejo))
            {
                string txt, boton;
                if (MonitorPC.PawnViejo)
                {
                    txt = T("Tu PawnIO (v" + MonitorPC.PawnVersion + ") está desactualizado: con esta versión no se puede leer la temperatura real del procesador. Actualizalo desde su sitio oficial; Pulso lo detecta solo.", "Your PawnIO (v" + MonitorPC.PawnVersion + ") is outdated: the real CPU temperature cannot be read with it. Update it from its official site; Pulso detects it by itself.");
                    boton = T("Cómo actualizar PawnIO", "How to update PawnIO");
                }
                else
                {
                    txt = L.CpuTemp.HasValue
                        ? T("Sin drivers, la temperatura del procesador es aproximada (la zona térmica que da Windows). Para la real, los vatios y los ventiladores de la placa podés instalar PawnIO (opcional, gratis y firmado).",
                            "Without drivers the CPU temperature is approximate (the thermal zone Windows provides). For the real one, watts and motherboard fans you can install PawnIO (optional, free and signed).")
                        : T("Este equipo no da la temperatura del procesador sin drivers. Es opcional: para verla (y los vatios y los ventiladores de la placa) podés instalar PawnIO, gratis y firmado, el mismo que usan LibreHardwareMonitor y FanControl. Todo lo demás se lee igual sin él.",
                            "This PC does not report the CPU temperature without drivers. It is optional: to see it (plus watts and motherboard fans) you can install PawnIO, free and signed, the same one LibreHardwareMonitor and FanControl use. Everything else is read without it.");
                    boton = T("Cómo instalar PawnIO", "How to install PawnIO");
                }
                y = Cartel(g, x, y, w, Nivel.Amarillo, txt, boton, delegate { pc.GuiaPawn(); });
            }
            return y;
        }

        int Cartel(Graphics g, int x, int y, int w, Nivel n, string texto, string boton, MethodInvoker accion)
        {
            int bw = boton != null ? Ancho(boton, fNegrita) + S(26) : 0;
            int tw = w - S(30) - (bw > 0 ? bw + S(12) : 0);
            int th = AltoParrafo(texto, fNormal, tw);
            int h = Math.Max(th, boton != null ? S(28) : 0) + S(18);
            Rectangle r = new Rectangle(x, y, w, h);
            Color c = De(n);
            Caja(g, r, Mezcla(cTarj, c, 0.10f), Mezcla(cLinea, c, 0.55f), 1);
            Caja(g, new Rectangle(x + S(6), y + S(8), S(4), h - S(16)), c, c, 0, S(2));
            Parrafo(g, texto, fNormal, cTexto, new Rectangle(x + S(18), y + (h - th) / 2, tw, th + S(2)));
            if (boton != null)
            {
                Rectangle rb = new Rectangle(r.Right - bw - S(10), y + (h - S(28)) / 2, bw, S(28));
                Caja(g, rb, c, c, 0, S(6));
                Tx(g, boton, fNegrita, TextoSobre(c), rb, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (accion != null) Clic(rb, accion);
            }
            return y + h + S(8);
        }

        // "● Normal" alineado a la derecha en x
        void Pastilla(Graphics g, int derecha, int y, Nivel n, bool temp) { Pastilla(g, derecha, y, n, temp, null); }
        void Pastilla(Graphics g, int derecha, int y, Nivel n, bool temp, string palabra)
        {
            string pal = palabra ?? Palabra(n, temp);
            int pw = Ancho(pal, fChica) + S(24), ph = fChica.Height + S(6);
            Rectangle rp = new Rectangle(derecha - pw, y, pw, ph);
            Color c = De(n);
            Caja(g, rp, Mezcla(cTarj, c, 0.16f), Mezcla(cTarj, c, 0.16f), 0, ph / 2);
            Punto(g, rp.X + S(8), rp.Y + (ph - S(7)) / 2, S(7), c);
            Tx(g, pal, fChica, c, rp.X + S(18), rp.Y + S(3));
        }

        // ---------- Grafico de los ultimos minutos ----------
        void Grafico(Graphics g, Rectangle r, List<Foto> fotos, int seg, float min, float max, string unidad, params object[] series)
        {
            using (Pen p = new Pen(cLinea))
            {
                p.DashStyle = DashStyle.Dot;
                for (int i = 0; i <= 2; i++)
                {
                    int yy = r.Top + r.Height * i / 2;
                    g.DrawLine(p, r.Left, yy, r.Right, yy);
                }
            }
            Tx(g, ((int)max) + unidad, fChica, cGris, r.Left + S(2), r.Top + S(1));
            Tx(g, ((int)min) + unidad, fChica, cGris, r.Left + S(2), r.Bottom - fChica.Height - S(1));
            DateTime fin = DateTime.Now, ini = fin.AddSeconds(-seg);
            for (int s = 0; s + 1 < series.Length; s += 2)
            {
                Func<Foto, float> v = (Func<Foto, float>)series[s];
                Color c = (Color)series[s + 1];
                using (Pen p = new Pen(c, Math.Max(1.5f, 2f * k)))
                {
                    p.LineJoin = LineJoin.Round;
                    List<PointF> pts = new List<PointF>();
                    foreach (Foto f in fotos)
                    {
                        float val = v(f);
                        if (float.IsNaN(val) || f.T < ini)
                        {
                            if (pts.Count >= 2) g.DrawLines(p, pts.ToArray());
                            pts.Clear();
                            continue;
                        }
                        float px = r.Left + (float)((f.T - ini).TotalSeconds / seg) * r.Width;
                        float vv = Math.Max(min, Math.Min(max, val));
                        float py = r.Bottom - (vv - min) / (max - min) * r.Height;
                        pts.Add(new PointF(px, py));
                    }
                    if (pts.Count >= 2) g.DrawLines(p, pts.ToArray());
                }
            }
        }

        int Leyenda(Graphics g, int x, int y, string texto, Color c)
        {
            Punto(g, x, y + (fChica.Height - S(8)) / 2, S(8), c);
            Tx(g, texto, fChica, cGris, x + S(12), y);
            return x + S(12) + Ancho(texto, fChica) + S(14);
        }

        // Un dato chico: titulo gris arriba, valor abajo
        void Stat(Graphics g, int x, int y, int w, string titulo, string valor, Color c)
        {
            Tx(g, titulo, fChica, cGris, new Rectangle(x, y, w, fChica.Height + S(2)), TextFormatFlags.Left);
            Tx(g, valor, fTitulo, c, new Rectangle(x, y + fChica.Height + S(1), w, fTitulo.Height + S(2)), TextFormatFlags.Left);
        }
        int AltoStat() { return fChica.Height + S(1) + fTitulo.Height + S(8); }

        // Placa madre, BIOS, plan de energia, bateria y ventiladores (al pie de la pestaña CPU)
        int Plataforma(Graphics g, int x, int y, int w, Lectura L, InfoFija F)
        {
            // Placa madre, BIOS, energia, ventiladores
            List<KeyValuePair<string, Nivel>> info = new List<KeyValuePair<string, Nivel>>();
            string placa = (F.PlacaFab + " " + F.PlacaModelo).Trim();
            if (placa != "") info.Add(new KeyValuePair<string, Nivel>(T("Placa madre: ", "Motherboard: ") + placa, Nivel.Gris));
            if (F.BiosVersion != "" || F.BiosMarca != "")
            {
                string b = "BIOS: " + Junta(F.BiosMarca, F.BiosVersion, F.BiosFecha.HasValue ? F.BiosFecha.Value.ToString(Modulo.Idioma == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy") : "");
                Nivel nb = Nivel.Gris;
                if (MonitorPC.IntelConBiosVieja(F)) { b += T("  — tu Intel 13.ª/14.ª gen necesita una BIOS más nueva (ver avisos de CPU)", "  — your 13th/14th gen Intel needs a newer BIOS (see CPU alerts)"); nb = Nivel.Rojo; }
                else if (F.BiosFecha.HasValue && (DateTime.Now - F.BiosFecha.Value).TotalDays > 730) b += T("  (tiene más de 2 años: puede haber una más nueva en la página de tu placa)", "  (over 2 years old: there may be a newer one on your motherboard's page)");
                info.Add(new KeyValuePair<string, Nivel>(b, nb));
            }
            string energia = "";
            if (F.Plan != "") energia = T("Plan de energía: ", "Power plan: ") + F.Plan;
            if (L.Bateria >= 0) energia = Junta(energia, T("Batería ", "Battery ") + L.Bateria + " %" + (L.Enchufada ? T(" (enchufada)", " (plugged in)") : T(" (a batería)", " (on battery)"))
                + (L.BateriaW.HasValue ? (L.BateriaW.Value < 0 ? T(", consume ", ", drawing ") : T(", carga a ", ", charging at ")) + Wt(Math.Abs(L.BateriaW.Value)) : ""));
            if (energia != "") info.Add(new KeyValuePair<string, Nivel>(energia, F.PlanGuid == MonitorPC.PlanAhorro || (L.Bateria >= 0 && !L.Enchufada) ? Nivel.Amarillo : Nivel.Gris));
            if (L.Fans.Count > 0)
            {
                List<string> fs = new List<string>();
                foreach (Ventilador v in L.Fans) if (v.Rpm > 0 || v.MaxRpm > 0) fs.Add(v.Nombre + " " + (int)v.Rpm + " RPM");
                if (fs.Count > 0) info.Add(new KeyValuePair<string, Nivel>(T("Ventiladores: ", "Fans: ") + string.Join(", ", fs.ToArray()), Nivel.Gris));
            }
            if (info.Count > 0) y = Lista(g, x, y, w, info);
            return y;
        }

        // Lista de renglones con un punto de color (gris = solo informacion)
        int Lista(Graphics g, int x, int y, int w, List<KeyValuePair<string, Nivel>> items)
        {
            int tw = w - S(40);
            int h = S(10);
            foreach (KeyValuePair<string, Nivel> it in items) h += AltoParrafo(it.Key, fNormal, tw) + S(6);
            h += S(4);
            Caja(g, new Rectangle(x, y, w, h), cTarj, cLinea, 1);
            int yy = y + S(10);
            foreach (KeyValuePair<string, Nivel> it in items)
            {
                int th = AltoParrafo(it.Key, fNormal, tw);
                Punto(g, x + S(14), yy + (fNormal.Height - S(8)) / 2, S(8), it.Value == Nivel.Gris ? cLinea : De(it.Value));
                Parrafo(g, it.Key, fNormal, it.Value == Nivel.Gris ? cTexto : cTexto, new Rectangle(x + S(30), yy, tw, th + S(2)));
                yy += th + S(6);
            }
            return y + h + S(8);
        }

        // =====================================================================================================
        // CPU
        // =====================================================================================================
        int VistaCpu(Graphics g, int x, int y, int w, Lectura L, InfoFija F)
        {
            int gap = S(8);
            bool dos = w >= S(860);
            int lw = dos ? (w - gap) * 52 / 100 : w;
            int tj = L.CpuTjMax > 0 ? L.CpuTjMax : 100;
            Nivel n = MonitorPC.NivelCpu(L.CpuTemp, tj);
            int ast = AltoStat();
            int h = S(12) + fTitulo.Height + S(2) + fChica.Height + S(6) + fGrande.Height + S(8) + ast * 2 + fChica.Height + S(14) + (L.CpuVoltVid ? fChica.Height + S(6) : 0);
            Rectangle r = new Rectangle(x, y, lw, h);
            Caja(g, r, cTarj, n == Nivel.Rojo ? cRojo : cLinea, n == Nivel.Rojo ? 2 : 1);
            int px = r.X + S(14), py = r.Y + S(12), iw = r.Width - S(28);
            string nombre = L.CpuNombre != "" ? L.CpuNombre : F.CpuNombre;
            Tx(g, nombre != "" ? nombre : "CPU", fTitulo, cTexto, new Rectangle(px, py, iw - S(110), fTitulo.Height + S(2)), TextFormatFlags.Left);
            Pastilla(g, r.Right - S(14), py, L.CpuTemp.HasValue ? n : Nivel.Gris, true);
            py += fTitulo.Height + S(2);
            string sub = Junta(F.Nucleos > 0 ? F.Nucleos + T(" núcleos", " cores") + (F.Hilos > 0 ? " / " + F.Hilos + T(" hilos", " threads") : "") : "",
                F.CpuBaseMHz > 0 ? T("base ", "base ") + Ghz(F.CpuBaseMHz) : "");
            Tx(g, sub, fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            py += fChica.Height + S(6);
            string grande = L.CpuTemp.HasValue ? Gc(L.CpuTemp) : "—";
            Tx(g, grande, fGrande, L.CpuTemp.HasValue ? De(n) : cGris, px, py);
            int gx = px + Ancho(grande, fGrande) + S(12);
            // Sin PawnIO la libreria no lee temperatura, vatios ni voltaje del CPU: se dice, no se muestra un numero dudoso
            // Al lado de la temperatura grande hay poco lugar: con PawnIO sin datos va la frase corta (la larga esta en el cartel de arriba)
            string reqDriver = MonitorPC.EsAdmin && MonitorPC.PawnOk && MonitorPC.PawnSinDatos ? MonitorPC.SinDriverCorto() : MonitorPC.SinDriverTexto();
            if (L.CpuTemp.HasValue)
            {
                Tx(g, T("límite (TjMax) ", "limit (TjMax) ") + tj + " °C" + (L.CpuTempFuente == "ACPI" ? T("  ·  aproximada, sin driver (zona térmica de Windows)", "  ·  approximate, no driver (Windows thermal zone)") : ""), fChica, cGris, gx, py + S(6));
                Barra(g, new Rectangle(gx, py + S(8) + fChica.Height, Math.Max(S(40), r.Right - S(14) - gx), S(6)), L.CpuTemp.Value * 100f / tj, De(n));
            }
            else Tx(g, T("Temperatura de CPU: ", "CPU temperature: ") + (L.CpuBajoNivel ? T("no disponible", "not available") : reqDriver), fChica, cGris, gx, py + S(6));
            py += fGrande.Height + S(8);
            int sw = iw / 2;
            Stat(g, px, py, sw, T("Uso", "Load"), P(L.CpuLoad), cTexto);
            string tVel = L.CpuClockWindows && L.CpuPerfPct.HasValue ? T("Velocidad (Windows: ", "Clock (Windows: ") + (int)Math.Round(L.CpuPerfPct.Value) + T(" % de la base)", " % of base)") : T("Velocidad (prom. / pico)", "Clock (avg / peak)");
            Stat(g, px + sw, py, sw, tVel, Ghz(L.CpuClockProm) + (L.CpuClockMax.HasValue ? " / " + Ghz(L.CpuClockMax) : ""), cTexto);
            py += ast;
            string reqCorto = MonitorPC.SinDriverCorto();   // en letra grande y media columna: la frase entera esta arriba
            Stat(g, px, py, sw, L.CpuPowerWindows ? T("Potencia (Windows, RAPL)", "Power (Windows, RAPL)") : T("Potencia", "Power"), L.CpuPower.HasValue || L.CpuBajoNivel ? Wt(L.CpuPower) : reqCorto, L.CpuPower.HasValue ? cTexto : cGris);
            Stat(g, px + sw, py, sw, L.CpuVoltVid ? T("Voltaje (VID)", "Voltage (VID)") : T("Voltaje", "Voltage"), L.CpuVolt.HasValue || L.CpuBajoNivel ? Vt(L.CpuVolt) : reqCorto, L.CpuVolt.HasValue ? cTexto : cGris);
            py += ast;
            MonitorPC.Picos pj = pc.PicoJuego;
            Tx(g, Junta(Maximos(pc.PicoSesion.CpuT, pj.Juego != "" && pj.Juego == L.Juego ? pj.CpuT : float.NaN, L.Juego, " °C"),
                Foto.Hay(pc.PicoSesion.CpuW) ? T("máx. ", "max ") + Wt(pc.PicoSesion.CpuW) : ""), fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            if (L.CpuVoltVid)
            {
                py += fChica.Height + S(6);
                Tx(g, T("VID: lo que pide el procesador (el mayor de los núcleos), no lo medido.", "VID: what the CPU requests (highest of all cores), not measured."), fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            }

            // Derecha: grafico + uso por nucleo
            int ry = dos ? y : y + h + gap, rx = dos ? x + lw + gap : x, rw = dos ? w - lw - gap : w;
            int rh = dos ? h : S(170);
            Rectangle rr = new Rectangle(rx, ry, rw, rh);
            Caja(g, rr, cTarj, cLinea, 1);
            int qx = rx + S(12), qy = ry + S(10), qw = rw - S(24);
            int lgx = Leyenda(g, qx, qy, T("Temperatura", "Temperature"), cAcento);
            Leyenda(g, lgx, qy, T("Uso", "Load"), cVioleta);
            qy += fChica.Height + S(4);
            int nuc = L.CpuNucleos.Count;
            int barH = nuc > 0 ? S(34) : 0;
            int grH = rh - (qy - ry) - S(10) - (nuc > 0 ? barH + fChica.Height + S(10) : 0);
            Func<Foto, float> ft = delegate (Foto f) { return f.CpuT; };
            Func<Foto, float> fu = delegate (Foto f) { return f.CpuU; };
            Grafico(g, new Rectangle(qx, qy, qw, Math.Max(S(30), grH)), pc.Historia(300), 300, 0, 100, "", ft, cAcento, fu, cVioleta);
            qy += Math.Max(S(30), grH) + S(8);
            if (nuc > 0)
            {
                Tx(g, T("Uso por núcleo", "Load per core"), fChica, cGris, qx, qy);
                qy += fChica.Height + S(2);
                int bg = S(3);
                float bw = (qw - (nuc - 1) * bg) / (float)nuc;
                for (int i = 0; i < nuc; i++)
                {
                    int bx = qx + (int)(i * (bw + bg));
                    Rectangle rb = new Rectangle(bx, qy, Math.Max(2, (int)bw), barH);
                    using (SolidBrush b = new SolidBrush(cLinea)) g.FillRectangle(b, rb);
                    int hh = (int)(barH * Math.Min(100f, L.CpuNucleos[i]) / 100f);
                    using (SolidBrush b = new SolidBrush(cVioleta)) g.FillRectangle(b, rb.X, rb.Bottom - hh, rb.Width, hh);
                }
            }
            y = Math.Max(r.Bottom, rr.Bottom) + gap;
            y = Plataforma(g, x, y, w, L, F);
            return Todos(g, x, y, w, L.CpuDatos);
        }

        // =====================================================================================================
        // GPU
        // =====================================================================================================
        int VistaGpu(Graphics g, int x, int y, int w, Lectura L, InfoFija F)
        {
            int gap = S(8);
            if (L.GpuNombre == "")
                return Cartel(g, x, y, w, Nivel.Gris, T("No se encontró una placa de video con sensores." + (F.GpuNombreWmi != "" ? " Windows ve: " + F.GpuNombreWmi + "." : ""),
                    "No graphics card with sensors was found." + (F.GpuNombreWmi != "" ? " Windows sees: " + F.GpuNombreWmi + "." : "")), null, null);
            bool dos = w >= S(860);
            int lw = dos ? (w - gap) * 52 / 100 : w;
            Nivel n = MonitorPC.NivelGpu(L.GpuTemp, L.GpuNvidia);
            Nivel nh = MonitorPC.NivelHot(L.GpuHot), nm = MonitorPC.NivelMemGpu(L.GpuMemTemp);
            Nivel peor = n; if (nh > peor) peor = nh; if (nm > peor) peor = nm;
            int ast = AltoStat();
            bool conector = L.GpuPinMaxA.HasValue;
            // Integrada: sin VRAM propia, ventilador ni hot spot; se muestra solo lo que la grafica informa y la memoria compartida (RAM)
            bool ig = L.GpuIntegrada;
            bool comp = ig && L.GpuCompUsada.HasValue && L.GpuCompTotal.HasValue;
            bool vram = comp || (L.GpuVramUsada.HasValue && L.GpuVramTotal.HasValue);
            string fan = L.GpuFanRpm.HasValue ? ((int)L.GpuFanRpm.Value) + " RPM" : "";
            if (L.GpuFanPct.HasValue) fan = fan != "" ? fan + " (" + (int)L.GpuFanPct.Value + " %)" : ((int)L.GpuFanPct.Value) + " %";
            List<string[]> sts = new List<string[]>();
            sts.Add(new string[] { T("Uso", "Load"), P(L.GpuLoad) });
            sts.Add(new string[] { T("Núcleo", "Core"), Mhz(L.GpuClock) });
            if (!ig || L.GpuMemClock.HasValue) sts.Add(new string[] { T("Memoria", "Memory"), Mhz(L.GpuMemClock) });
            if (!ig || L.GpuPower.HasValue) sts.Add(new string[] { T("Potencia", "Power"), Wt(L.GpuPower) });
            if (!ig || fan != "") sts.Add(new string[] { T("Ventiladores", "Fans"), fan != "" ? fan : "—" });
            if (!ig || L.GpuVolt.HasValue) sts.Add(new string[] { T("Voltaje", "Voltage"), Vt(L.GpuVolt) });
            int filas = (sts.Count + 2) / 3;
            int h = S(12) + fTitulo.Height + S(2) + fChica.Height + S(6) + fGrande.Height + S(8) + ast * filas + (vram ? fNegrita.Height + S(10) : 0)
                + (fChica.Height + S(6)) * ((conector ? 2 : 1) + (L.GpuOtras.Count > 0 ? 1 : 0) + (ig ? 2 : 0)) + S(8);
            Rectangle r = new Rectangle(x, y, lw, h);
            Caja(g, r, cTarj, peor == Nivel.Rojo ? cRojo : cLinea, peor == Nivel.Rojo ? 2 : 1);
            int px = r.X + S(14), py = r.Y + S(12), iw = r.Width - S(28);
            Tx(g, L.GpuNombre, fTitulo, cTexto, new Rectangle(px, py, iw - S(110), fTitulo.Height + S(2)), TextFormatFlags.Left);
            Pastilla(g, r.Right - S(14), py, peor, true, ig && peor == Nivel.Gris ? T("Integrada", "Integrated") : null);
            py += fTitulo.Height + S(2);
            string drv = F.GpuDriver != "" ? T("Driver ", "Driver ") + F.GpuDriver + (F.GpuDriverFecha.HasValue ? " (" + F.GpuDriverFecha.Value.ToString(Modulo.Idioma == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy") + ")" : "") : "";
            // En una integrada (AMD/Intel) un driver "viejo" no se puede dar por desactualizado: las Vega y Radeon Graphics de procesadores viejos ya no reciben versiones nuevas y Windows no dice si hay otra
            bool viejo = !ig && F.GpuDriverFecha.HasValue && (DateTime.Now - F.GpuDriverFecha.Value).TotalDays > 365;
            Tx(g, drv + (viejo ? T("  — tiene más de un año, conviene actualizarlo", "  — over a year old, worth updating") : ""), fChica, viejo ? cAmarillo : cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            py += fChica.Height + S(6);
            string grande = Gc(L.GpuTemp);
            Tx(g, grande, fGrande, De(n), px, py);
            int gx = px + Ancho(grande, fGrande) + S(14);
            int lineaY = py + S(1);
            if (L.GpuHot.HasValue)
            {
                float d = L.GpuTemp.HasValue ? L.GpuHot.Value - L.GpuTemp.Value : float.NaN;
                string ht = T("Hot spot ", "Hot spot ") + Gc(L.GpuHot) + (float.IsNaN(d) ? "" : "  (+" + (int)Math.Round(d) + T(" del núcleo)", " over core)"));
                Tx(g, ht, fNegrita, De(nh), gx, lineaY);
                if (!float.IsNaN(d) && d >= 25) Tx(g, T("  muy separado", "  too far apart"), fNegrita, cAmarillo, gx + Ancho(ht, fNegrita), lineaY);
                lineaY += fNegrita.Height + S(2);
            }
            if (L.GpuMemTemp.HasValue)
                Tx(g, T("Memoria ", "Memory ") + Gc(L.GpuMemTemp), fNegrita, De(nm), gx, lineaY);
            else if (ig && !L.GpuTemp.HasValue)
                Tx(g, T("Sin temperatura (gráfica integrada)", "No temperature (integrated graphics)"), fChica, cGris, gx, lineaY + S(4));
            py += fGrande.Height + S(8);
            int sw = iw / 3;
            for (int i = 0; i < sts.Count; i++)
                Stat(g, px + sw * (i % 3), py + ast * (i / 3), sw, sts[i][0], sts[i][1], cTexto);
            py += ast * filas;
            // VRAM (o, en una integrada, memoria compartida con la RAM)
            if (vram)
            {
                float vu = comp ? L.GpuCompUsada.Value : L.GpuVramUsada.Value, vtot = comp ? L.GpuCompTotal.Value : L.GpuVramTotal.Value;
                float pct = vu * 100f / vtot;
                string vt = (comp ? T("Memoria compartida ", "Shared memory ") : "VRAM ") + Gb(vu / 1024.0) + T(" de ", " of ") + Gb(vtot / 1024.0);
                Tx(g, vt, fNegrita, cTexto, px, py);
                int vw = Ancho(vt, fNegrita) + S(10);
                Barra(g, new Rectangle(px + vw, py + (fNegrita.Height - S(8)) / 2, iw - vw, S(8)), pct, pct >= 97 && !ig ? cRojo : pct >= 90 && !ig ? cAmarillo : cAcento);
                py += fNegrita.Height + S(10);
            }
            if (ig)
            {
                Tx(g, comp ? T("Usa la RAM del sistema: no tiene VRAM propia ni ventilador.", "It uses system RAM: it has no VRAM of its own and no fan.")
                           : T("Usa la RAM del sistema: sin VRAM propia ni ventilador; no informa memoria compartida.", "It uses system RAM: no VRAM of its own, no fan; shared memory not reported."),
                    fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
                py += fChica.Height + S(6);
                Tx(g, T("Lo que no aparece (—) no lo informa esta gráfica integrada; no es un problema del driver.", "Whatever is missing (—) is not reported by this integrated graphics chip; it is not a driver problem."),
                    fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
                py += fChica.Height + S(6);
            }
            if (conector)
            {
                Nivel nc = MonitorPC.Umbral(L.GpuPinMaxA, 8.5f, 9.5f);
                Tx(g, T("Conector 12VHPWR: pin más cargado ", "12VHPWR connector: most loaded pin ") + L.GpuPinMaxA.Value.ToString("0.0", CI) + T(" A (máximo seguro 9.5 A)", " A (safe max 9.5 A)"), fChica, De(nc), new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
                py += fChica.Height + S(6);
            }
            MonitorPC.Picos pj = pc.PicoJuego;
            bool jug = pj.Juego != "" && pj.Juego == L.Juego;
            Tx(g, Junta(Maximos(pc.PicoSesion.GpuT, jug ? pj.GpuT : float.NaN, L.Juego, " °C"), Foto.Hay(pc.PicoSesion.GpuH) ? T("hot spot máx. ", "hot spot max ") + Gc(pc.PicoSesion.GpuH) : "",
                Foto.Hay(pc.PicoSesion.GpuW) ? T("máx. ", "max ") + Wt(pc.PicoSesion.GpuW) : ""), fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            py += fChica.Height + S(6);
            if (L.GpuOtras.Count > 0)
                Tx(g, T("También: ", "Also: ") + string.Join(", ", L.GpuOtras.ToArray()), fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);

            // Derecha: grafico nucleo / hot spot / uso
            int ry = dos ? y : y + h + gap, rx = dos ? x + lw + gap : x, rw = dos ? w - lw - gap : w;
            int rh = dos ? h : S(160);
            Rectangle rr = new Rectangle(rx, ry, rw, rh);
            Caja(g, rr, cTarj, cLinea, 1);
            int qx = rx + S(12), qy = ry + S(10), qw = rw - S(24);
            int lgx = Leyenda(g, qx, qy, T("Núcleo", "Core"), cAcento);
            if (L.GpuHot.HasValue) lgx = Leyenda(g, lgx, qy, "Hot spot", cRojo);
            Leyenda(g, lgx, qy, T("Uso %", "Load %"), cVioleta);
            qy += fChica.Height + S(4);
            Func<Foto, float> ft = delegate (Foto f) { return f.GpuT; };
            Func<Foto, float> fh = delegate (Foto f) { return f.GpuH; };
            Func<Foto, float> fu = delegate (Foto f) { return f.GpuU; };
            Grafico(g, new Rectangle(qx, qy, qw, rr.Bottom - qy - S(10)), pc.Historia(300), 300, 0, 110, "", fu, Mezcla(cTarj, cVioleta, 0.7f), ft, cAcento, fh, cRojo);
            y = Math.Max(r.Bottom, rr.Bottom) + gap;
            return Todos(g, x, y, w, L.GpuDatos);
        }

        // =====================================================================================================
        // RAM
        // =====================================================================================================
        int VistaRam(Graphics g, int x, int y, int w, Lectura L, InfoFija F)
        {
            int gap = S(8);
            bool dos = w >= S(860);
            int lw = dos ? (w - gap) * 40 / 100 : w;
            float? uso = L.RamUso >= 0 ? L.RamUso : (float?)null;
            Nivel n = MonitorPC.NivelRam(L.RamUso);
            int h = S(12) + fTitulo.Height + S(2) + fChica.Height + S(6) + fGrande.Height + S(8) + fChica.Height + S(10) + (fChica.Height + S(6)) * (L.DimmTemp.HasValue ? 2 : 1) + S(6);
            Rectangle r = new Rectangle(x, y, lw, h);
            Caja(g, r, cTarj, n == Nivel.Rojo ? cRojo : cLinea, n == Nivel.Rojo ? 2 : 1);
            int px = r.X + S(14), py = r.Y + S(12), iw = r.Width - S(28);
            Tx(g, T("Memoria RAM", "RAM"), fTitulo, cTexto, px, py);
            Pastilla(g, r.Right - S(14), py, n, false);
            py += fTitulo.Height + S(2);
            Tx(g, Junta(F.RamTotalGB > 0 ? F.RamTotalGB + " GB" : "", F.RamTipo, F.Ram.Count > 0 ? F.Ram.Count + T(" módulos", " modules") : ""), fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            py += fChica.Height + S(6);
            Tx(g, P(uso), fGrande, De(n), px, py);
            int gx = px + Ancho(P(uso), fGrande) + S(12);
            Tx(g, T("usada", "used"), fChica, cGris, gx, py + S(10));
            py += fGrande.Height + S(8);
            string ut = Gb(L.RamUsadaGB) + T(" de ", " of ") + Gb(L.RamTotalGB);
            Tx(g, ut, fChica, cTexto, px, py);
            int uw = Ancho(ut, fChica) + S(10);
            Barra(g, new Rectangle(px + uw, py + (fChica.Height - S(6)) / 2, iw - uw, S(6)), uso ?? 0, De(n));
            py += fChica.Height + S(10);
            if (L.DimmTemp.HasValue)
            {
                Tx(g, T("Temperatura de los módulos: ", "Module temperature: ") + Gc(L.DimmTemp), fChica, De(MonitorPC.NivelDimm(L.DimmTemp)), new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
                py += fChica.Height + S(6);
            }
            if (Foto.Hay(pc.PicoSesion.Ram))
                Tx(g, T("Máx. sesión ", "Session max ") + (int)pc.PicoSesion.Ram + " %", fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);

            // Derecha: diagnostico (XMP, canales, mezcla)
            List<KeyValuePair<string, Nivel>> diag = new List<KeyValuePair<string, Nivel>>();
            if (F.RamXmpTexto != "") diag.Add(new KeyValuePair<string, Nivel>(F.RamXmpTexto, F.RamXmpNivel));
            if (F.RamCanalTexto != "") diag.Add(new KeyValuePair<string, Nivel>(F.RamCanalTexto, F.RamCanalNivel));
            if (F.RamMezclaTexto != "") diag.Add(new KeyValuePair<string, Nivel>(F.RamMezclaTexto, Nivel.Amarillo));
            // Paginacion: cuantas paginas se leen del disco por segundo porque no estaban en la RAM (fallos de pagina duros)
            if (L.PaginasSeg.HasValue || L.PaginacionUso.HasValue)
                diag.Add(new KeyValuePair<string, Nivel>(Junta(
                    L.PaginacionUso.HasValue ? T("Archivo de paginación: ", "Page file: ") + P(L.PaginacionUso) + T(" en uso", " in use") : "",
                    L.PaginasSeg.HasValue ? ((int)L.PaginasSeg.Value) + T(" páginas/s leídas del disco", " pages/s read from disk") : ""),
                    L.PaginasSeg.HasValue && L.PaginasSeg.Value >= 1000 ? Nivel.Amarillo : Nivel.Gris));
            int ry = dos ? y : y + h + gap, rx = dos ? x + lw + gap : x, rw = dos ? w - lw - gap : w;
            int yFin = r.Bottom;
            if (diag.Count > 0)
            {
                int yd = Lista(g, rx, ry, rw, diag) - S(8);
                yFin = Math.Max(yFin, yd);
            }
            y = yFin + gap;

            // Tabla de modulos
            if (F.Ram.Count > 0)
            {
                string[] cab = { T("Ranura", "Slot"), T("Tamaño", "Size"), T("Marca y modelo", "Brand and model"), T("Velocidad (actual / de fábrica)", "Speed (current / rated)"), T("Voltaje", "Voltage") };
                float[] prop = { 0.18f, 0.10f, 0.38f, 0.24f, 0.10f };
                List<string[]> filas = new List<string[]>();
                foreach (ModuloRam m in F.Ram)
                {
                    int fab = MonitorPC.VelocidadDeModelo(m.Modelo);
                    if (fab <= 0) fab = m.Velocidad;
                    filas.Add(new string[] {
                        m.Ranura + (m.Canal != "" ? T(" (canal ", " (channel ") + m.Canal + ")" : ""),
                        Gb(m.GB),
                        Junta(m.Marca, m.Modelo),
                        (m.Configurada > 0 ? m.Configurada.ToString() : "?") + " / " + (fab > 0 ? fab.ToString() : "?") + " MT/s",
                        m.Voltaje > 0 ? (m.Voltaje / 1000.0).ToString("0.00", CI) + " V" : "—" });
                }
                y = Tabla(g, x, y, w, cab, prop, filas, null);
            }
            return y;
        }

        // =====================================================================================================
        // Discos
        // =====================================================================================================
        int VistaDiscos(Graphics g, int x, int y, int w, Lectura L)
        {
            // Lo que da Windows sin drivers: tipo y bus, salud SMART y velocidad en vivo de cada disco fisico
            if (L.Fisicos.Count > 0)
            {
                string[] cf = { T("Disco", "Drive"), T("Tipo", "Type"), T("Salud (SMART)", "Health (SMART)"), T("Lectura", "Read"), T("Escritura", "Write") };
                float[] pf = { 0.40f, 0.14f, 0.16f, 0.15f, 0.15f };
                List<string[]> ff = new List<string[]>();
                List<Nivel[]> nf = new List<Nivel[]>();
                foreach (DiscoFisico d in L.Fisicos)
                {
                    ff.Add(new string[] { d.Nombre + (d.Letras != "" ? "  (" + d.Letras + ")" : ""), d.Tipo != "" ? d.Tipo : "—", d.Salud, MBs(d.LeeMBs), MBs(d.EscribeMBs) });
                    nf.Add(new Nivel[] { Nivel.Gris, Nivel.Gris, d.NivelSalud == Nivel.Verde ? Nivel.Gris : d.NivelSalud, Nivel.Gris, Nivel.Gris });
                }
                y = Tabla(g, x, y, w, cf, pf, ff, nf);
            }
            if (L.Discos.Count == 0)
                return Cartel(g, x, y, w, Nivel.Gris, T("No se encontraron discos con sensores de temperatura (los discos por USB no se leen).", "No drives with temperature sensors were found (USB drives are not read)."), null, null);
            string[] cab = { T("Disco", "Drive"), T("Temperatura", "Temperature"), T("Vida útil", "Life left"), T("Espacio usado", "Space used"), T("Escrito", "Written"), T("Encendido", "Powered on") };
            float[] prop = { 0.32f, 0.16f, 0.11f, 0.15f, 0.11f, 0.15f };
            List<string[]> filas = new List<string[]>();
            List<Nivel[]> niveles = new List<Nivel[]>();
            foreach (Disco d in L.Discos)
            {
                string temp = Gc(d.Temp);
                if (d.Temp.HasValue && d.TempAviso.HasValue) temp += T(" (aviso ", " (warn ") + (int)d.TempAviso.Value + ")";
                string horas = "";
                if (d.Horas.HasValue) { double dias = d.Horas.Value / 24.0; horas = dias >= 2 ? ((int)dias) + T(" días", " days") : ((int)d.Horas.Value) + " h"; }
                string vida = d.Vida.HasValue ? P(d.Vida) : d.Estado != "" ? d.Estado : "—";
                filas.Add(new string[] { d.Nombre, temp, vida, d.Lleno.HasValue ? P(d.Lleno) : "—", d.EscritoGB.HasValue ? MonitorPC.Tb(d.EscritoGB.Value) : "—", horas != "" ? horas : "—" });
                Nivel nl = d.Lleno.HasValue ? MonitorPC.Umbral(d.Lleno, 85, 95) : Nivel.Gris;
                niveles.Add(new Nivel[] { Nivel.Gris, MonitorPC.NivelDisco(d), d.Vida.HasValue ? MonitorPC.NivelVida(d.Vida) : d.NivelEstado, nl, Nivel.Gris, Nivel.Gris });
            }
            y = Tabla(g, x, y, w, cab, prop, filas, niveles);
            // Leyenda: que significa cada columna cuando no hay % (no hay tooltips en esta vista dibujada)
            List<string> ley = new List<string>();
            ley.Add(T("La vida útil la calcula el propio disco (SMART). Un SSD se frena solo si pasa su temperatura de aviso.", "Life left is computed by the drive itself (SMART). An SSD throttles itself if it passes its warning temperature."));
            bool hayEstado = false, sinEscrito = false;
            foreach (Disco d in L.Discos) { if (!d.Vida.HasValue && d.Estado != "") hayEstado = true; if (!d.EscritoGB.HasValue) sinEscrito = true; }
            if (hayEstado)
                ley.Add(T("Un disco rígido (HDD) no se gasta por escribir: no tiene % de vida. Su estado sale de SMART: Mal = Windows predice falla, sectores incorregibles, 100+ reasignados o 10+ pendientes; Atención = algún sector reasignado o pendiente, reintentos de arranque o errores CRC (casi siempre el cable SATA).",
                          "A hard drive (HDD) does not wear out from writing: it has no life %. Its status comes from SMART: Bad = Windows predicts failure, uncorrectable sectors, 100+ reallocated or 10+ pending; Warning = any reallocated or pending sector, spin-up retries or CRC errors (usually the SATA cable)."));
            foreach (Disco d in L.Discos) if (!d.Vida.HasValue && d.EstadoDetalle != "") ley.Add(d.Nombre + " (SMART): " + d.EstadoDetalle + ".");
            if (sinEscrito) ley.Add(T("\"Escrito\" en — = el disco no informa el total escrito (muchos HDD no lo llevan).", "\"Written\" as — = the drive does not report total data written (many HDDs do not track it)."));
            y -= S(4);
            foreach (string s in ley)
            {
                int th = AltoParrafo(s, fChica, w - S(8));
                Parrafo(g, s, fChica, cGris, new Rectangle(x + S(4), y, w - S(8), th + S(2)));
                y += th + S(2);
            }
            return y + S(6);
        }

        // ---------- Tabla simple con encabezado ----------
        int Tabla(Graphics g, int x, int y, int w, string[] cab, float[] prop, List<string[]> filas, List<Nivel[]> niveles)
        {
            int fila = fNormal.Height + S(10);
            int h = S(8) + fila + filas.Count * fila + S(6);
            Caja(g, new Rectangle(x, y, w, h), cTarj, cLinea, 1);
            int iw = w - S(24);
            int[] xs = new int[cab.Length + 1];
            xs[0] = x + S(12);
            for (int i = 0; i < cab.Length; i++) xs[i + 1] = xs[i] + (int)(iw * prop[i]);
            int yy = y + S(8);
            for (int i = 0; i < cab.Length; i++)
                Tx(g, cab[i], fChica, cGris, new Rectangle(xs[i], yy + (fila - fChica.Height) / 2, xs[i + 1] - xs[i] - S(8), fChica.Height + S(2)), TextFormatFlags.Left);
            yy += fila;
            for (int f = 0; f < filas.Count; f++)
            {
                using (Pen p = new Pen(cLinea)) g.DrawLine(p, x + S(10), yy, x + w - S(10), yy);
                for (int i = 0; i < cab.Length && i < filas[f].Length; i++)
                {
                    Nivel nv = niveles != null ? niveles[f][i] : Nivel.Gris;
                    Color c = nv == Nivel.Gris ? cTexto : De(nv);
                    Tx(g, filas[f][i], i == 0 ? fNegrita : fNormal, c, new Rectangle(xs[i], yy + (fila - fNormal.Height) / 2, xs[i + 1] - xs[i] - S(8), fNormal.Height + S(2)), TextFormatFlags.Left);
                }
                yy += fila;
            }
            return y + h + S(8);
        }

        // ---------- "Todos los sensores" (se abre y cierra) ----------
        int Todos(Graphics g, int x, int y, int w, List<Dato> datos)
        {
            if (datos.Count == 0) return y;
            string t = (verTodos ? "▾  " : "▸  ") + T("Todos los sensores", "All sensors") + " (" + datos.Count + ")";
            Rectangle rt = new Rectangle(x, y, Ancho(t, fNegrita) + S(12), fNegrita.Height + S(8));
            Tx(g, t, fNegrita, cMarca, x + S(2), y + S(4));
            Clic(rt, delegate { verTodos = !verTodos; });
            y += rt.Height + S(2);
            if (!verTodos) return y;

            // Renglones: titulo de grupo + sensores
            List<string[]> items = new List<string[]>();
            string grupo = null;
            foreach (Dato d in datos)
            {
                if (d.Grupo != grupo) { grupo = d.Grupo; items.Add(new string[] { d.Grupo }); }
                items.Add(new string[] { d.Nombre, d.Valor, d.Pico });
            }
            int cols = w >= S(900) ? 3 : w >= S(560) ? 2 : 1;
            int porCol = (items.Count + cols - 1) / cols;
            int fila = fChica.Height + S(4);
            int h = S(10) + porCol * fila + S(8);
            Caja(g, new Rectangle(x, y, w, h), cTarj, cLinea, 1);
            int colW = (w - S(24) - (cols - 1) * S(20)) / cols;
            for (int i = 0; i < items.Count; i++)
            {
                int c = i / porCol, f = i % porCol;
                int cx = x + S(12) + c * (colW + S(20)), cy = y + S(10) + f * fila;
                string[] it = items[i];
                if (it.Length == 1) { Tx(g, it[0], fNegrita, cGris, new Rectangle(cx, cy, colW, fila), TextFormatFlags.Left); continue; }
                int vw = Ancho(it[1], fChica), pw = it[2] != "" ? Ancho(T("máx ", "max ") + it[2], fChica) : 0;
                Tx(g, it[0], fChica, cTexto, new Rectangle(cx + S(8), cy, colW - vw - pw - S(24), fila), TextFormatFlags.Left);
                if (pw > 0) Tx(g, T("máx ", "max ") + it[2], fChica, cGris, new Rectangle(cx + colW - pw, cy, pw, fila), TextFormatFlags.Right);
                Tx(g, it[1], fChica, cTexto, new Rectangle(cx + colW - pw - S(12) - vw, cy, vw + S(2), fila), TextFormatFlags.Right);
            }
            return y + h + S(8);
        }
    }
}
