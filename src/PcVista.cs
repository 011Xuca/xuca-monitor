// Panel en vivo de la pestaña PC. Se dibuja todo a mano en un solo control (no un control por dato): asi no
// parpadea, no gasta memoria cada vez que se actualiza (antes se creaban etiquetas y fuentes nuevas cada 1.5 s),
// sigue el tema claro/oscuro y la escala de Windows (125 %, 150 %...). Si no entra, se desplaza con la rueda.
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
        readonly Timer reloj;
        readonly Font fChica, fNormal, fNegrita, fTitulo, fGrande;
        readonly List<Zona> zonas = new List<Zona>();
        bool verTodos;
        int scroll, contenido, pedida;
        float k = 1;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        class Zona { public Rectangle R; public MethodInvoker Hacer; }

        Color cFondo, cTarj, cLinea, cTexto, cGris, cAcento, cElegida, cVerde, cAmarillo, cRojo, cVioleta;

        public PcVista(MonitorPC pc)
        {
            this.pc = pc;
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
            cElegida = osc ? Color.FromArgb(14, 52, 60) : Color.FromArgb(207, 250, 254);
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
            int cat = pc.CatActual;

            y = Pestanas(g, x, y, w);
            y += S(8);
            y = Carteles(g, x, y, w, L, cat);
            if (pc.Activo && L != null && F != null)
            {
                switch (cat)
                {
                    case MonitorPC.CAT_CPU: y = VistaCpu(g, x, y, w, L, F); break;
                    case MonitorPC.CAT_GPU: y = VistaGpu(g, x, y, w, L, F); break;
                    case MonitorPC.CAT_RAM: y = VistaRam(g, x, y, w, L, F); break;
                    case MonitorPC.CAT_DISCOS: y = VistaDiscos(g, x, y, w, L); break;
                    default: y = VistaGeneral(g, x, y, w, L, F); break;
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

        // ---------- Fila de categorias + "Guardar avisos" ----------
        int Pestanas(Graphics g, int x, int y, int w)
        {
            int h = S(28), xx = x;
            for (int i = 0; i < MonitorPC.CATS; i++)
            {
                MonitorPC.Categoria c = pc.Cat(i);
                if (c == null) return y;
                string t = c.Nombre + "  " + (c.Activo ? c.Cuenta.ToString() : "off");
                int tw = Ancho(t, fNegrita) + S(22);
                Rectangle r = new Rectangle(xx, y, tw, h);
                bool sel = i == pc.CatActual;
                Caja(g, r, sel ? cElegida : cTarj, sel ? cAcento : cLinea, sel ? 2 : 1);
                Tx(g, t, fNegrita, c.Activo ? cTexto : cGris, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int ii = i;
                Clic(r, delegate { pc.Seleccionar(ii); scroll = 0; if (pc.App != null) pc.App.Cambio(pc); });
                xx += tw + S(6);
            }
            int cat = pc.CatActual;
            MonitorPC.Categoria a = pc.Cat(cat);
            string ct = cat == MonitorPC.CAT_GENERAL ? T("Guardar avisos (General junta todo)", "Save alerts (General collects all)") : T("Guardar avisos de ", "Save alerts for ") + a.Nombre;
            int cw = S(22) + Ancho(ct, fNormal);
            int cx = xx + S(10), cy = y;
            if (cx + cw > x + w) { cx = x; cy = y + h + S(6); }
            Rectangle cb = new Rectangle(cx, cy + (h - S(16)) / 2, S(16), S(16));
            Caja(g, cb, a.Activo ? cAcento : cTarj, a.Activo ? cAcento : cGris, 1, S(3));
            if (a.Activo)
            {
                using (Pen p = new Pen(TextoSobre(cAcento), Math.Max(2f, 2f * k)))
                    g.DrawLines(p, new Point[] { new Point(cb.X + S(4), cb.Y + S(8)), new Point(cb.X + S(7), cb.Y + S(11)), new Point(cb.X + S(12), cb.Y + S(5)) });
            }
            Tx(g, ct, fNormal, cTexto, cx + S(22), cy + (h - fNormal.Height) / 2);
            Clic(new Rectangle(cx, cy, cw, h), delegate
            {
                MonitorPC.Categoria c = pc.Cat(cat);
                pc.ActivarCat(cat, !c.Activo);
                if (pc.App != null) pc.App.Cambio(pc);
            });
            return Math.Max(y + h, cy + h);
        }

        // ---------- Carteles (apagado, PawnIO, errores) ----------
        int Carteles(Graphics g, int x, int y, int w, Lectura L, int cat)
        {
            if (!pc.Activo)
                return Cartel(g, x, y, w, Nivel.Gris, T("El monitor de PC está apagado. Prendelo con la casilla \"Activado\" de arriba para ver temperaturas, uso y todo lo demás en vivo.",
                    "The PC monitor is off. Turn it on with the \"Enabled\" box above to see temperatures, usage and everything else live."), null, null);
            if (L == null)
                return Cartel(g, x, y, w, Nivel.Gris, T("Leyendo los sensores...", "Reading sensors..."), null, null);
            if (!L.HaySensores && pc.ErrorSensores != null)
                y = Cartel(g, x, y, w, Nivel.Rojo, T("No se pudieron abrir los sensores (" + pc.ErrorSensores + "). Se vuelve a intentar solo cada 1 minuto. Mientras tanto se muestra lo que da Windows (uso del procesador y RAM).",
                    "Could not open the sensors (" + pc.ErrorSensores + "). It retries by itself every minute. Meanwhile what Windows provides is shown (CPU usage and RAM)."), null, null);
            bool cpuSinTemp = L.HaySensores && !L.CpuTemp.HasValue;
            if ((cat == MonitorPC.CAT_GENERAL || cat == MonitorPC.CAT_CPU) && (cpuSinTemp || MonitorPC.PawnViejo) && !MonitorPC.PawnOk || pc.Instalando)
            {
                string txt, boton = null;
                if (pc.Instalando) txt = pc.EstadoPawn;
                else if (MonitorPC.PawnViejo)
                {
                    txt = T("Tu PawnIO (v" + MonitorPC.PawnVersion + ") está desactualizado: con esta versión no se puede leer la temperatura del procesador.", "Your PawnIO (v" + MonitorPC.PawnVersion + ") is outdated: the CPU temperature cannot be read with it.");
                    boton = T("Actualizar PawnIO", "Update PawnIO");
                }
                else
                {
                    txt = T("Para ver la temperatura y los vatios del procesador (y los ventiladores de la placa) hace falta PawnIO, un driver gratis y firmado: el mismo que usan LibreHardwareMonitor y FanControl. La placa de video, la RAM y los discos se leen igual sin él.",
                        "To see CPU temperature and watts (and motherboard fans) PawnIO is needed, a free signed driver: the same one LibreHardwareMonitor and FanControl use. The graphics card, RAM and drives are read without it anyway.");
                    boton = T("Instalar PawnIO", "Install PawnIO");
                }
                y = Cartel(g, x, y, w, Nivel.Amarillo, txt, boton, delegate { pc.InstalarPawn(); });
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

        // ---------- Tarjeta chica (vista General) ----------
        int AltoTarjeta() { return S(10) + fTitulo.Height + S(2) + fChica.Height + S(4) + fGrande.Height + S(4) + (fChica.Height + S(5)) * 3 + S(6); }

        void Tarjeta(Graphics g, Rectangle r, string titulo, string nombre, string grande, Nivel n, bool temp, float? barra, string barraTxt, string linea1, string linea2, int irA)
        {
            Tarjeta(g, r, titulo, nombre, grande, n, n, null, temp, barra, barraTxt, linea1, linea2, irA);
        }

        // nGrande = color del numero grande; nEstado + palabra = la pastilla (por ej. un disco frio pero con poca vida)
        void Tarjeta(Graphics g, Rectangle r, string titulo, string nombre, string grande, Nivel n, Nivel nEstado, string palabra, bool temp, float? barra, string barraTxt, string linea1, string linea2, int irA)
        {
            Caja(g, r, cTarj, nEstado == Nivel.Rojo ? cRojo : cLinea, nEstado == Nivel.Rojo ? 2 : 1);
            int px = r.X + S(12), py = r.Y + S(10), iw = r.Width - S(24);
            Tx(g, titulo, fTitulo, cTexto, px, py);
            Pastilla(g, r.Right - S(12), py, nEstado, temp, palabra);
            py += fTitulo.Height + S(2);
            Tx(g, nombre, fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            py += fChica.Height + S(4);
            Tx(g, grande, fGrande, n == Nivel.Gris ? cGris : De(n), px, py);
            py += fGrande.Height + S(4);
            if (barraTxt != null)
            {
                string bt = barraTxt + " " + P(barra);
                int bw = Ancho(bt, fChica);
                Tx(g, bt, fChica, cGris, px, py);
                Barra(g, new Rectangle(px + bw + S(8), py + (fChica.Height - S(6)) / 2, iw - bw - S(8), S(6)), barra ?? 0, cAcento);
            }
            py += fChica.Height + S(5);
            if (!string.IsNullOrEmpty(linea1)) Tx(g, linea1, fChica, cTexto, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            py += fChica.Height + S(5);
            if (!string.IsNullOrEmpty(linea2)) Tx(g, linea2, fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);
            if (irA >= 0) Clic(r, delegate { pc.Seleccionar(irA); scroll = 0; if (pc.App != null) pc.App.Cambio(pc); });
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

        // =====================================================================================================
        // General
        // =====================================================================================================
        int VistaGeneral(Graphics g, int x, int y, int w, Lectura L, InfoFija F)
        {
            int gap = S(8);
            int cols = w >= S(4 * 190) + 3 * gap ? 4 : 2;
            int cw = (w - (cols - 1) * gap) / cols, ch = AltoTarjeta();
            MonitorPC.Picos ps = pc.PicoSesion, pj = pc.PicoJuego;
            string nj = L.Juego != "" ? L.Juego : "";
            bool jug = pj.Juego != "" && pj.Juego == nj;

            // CPU
            Nivel ncpu = MonitorPC.NivelCpu(L.CpuTemp, L.CpuTjMax);
            Tarjeta(g, Celda(x, y, cw, ch, gap, cols, 0), "CPU", L.CpuNombre != "" ? L.CpuNombre : F.CpuNombre, L.CpuTemp.HasValue ? Gc(L.CpuTemp) : P(L.CpuLoad),
                L.CpuTemp.HasValue ? ncpu : Nivel.Gris, true, L.CpuLoad, T("Uso", "Load"),
                Junta(Ghz(L.CpuClockProm), Wt(L.CpuPower)), Maximos(ps.CpuT, jug ? pj.CpuT : float.NaN, nj, " °C"), MonitorPC.CAT_CPU);

            // GPU
            Nivel ngpu = MonitorPC.NivelGpu(L.GpuTemp, L.GpuNvidia);
            Nivel nhot = MonitorPC.NivelHot(L.GpuHot);
            if (nhot > ngpu) ngpu = nhot;
            Tarjeta(g, Celda(x, y, cw, ch, gap, cols, 1), "GPU", L.GpuNombre != "" ? L.GpuNombre : (F.GpuNombreWmi != "" ? F.GpuNombreWmi : T("No se encontró", "Not found")), Gc(L.GpuTemp),
                ngpu, true, L.GpuLoad, T("Uso", "Load"),
                Junta(L.GpuHot.HasValue ? T("Hot spot ", "Hot spot ") + Gc(L.GpuHot) : "", Wt(L.GpuPower)), Maximos(ps.GpuT, jug ? pj.GpuT : float.NaN, nj, " °C"), MonitorPC.CAT_GPU);

            // RAM
            float? ram = L.RamUso >= 0 ? L.RamUso : (float?)null;
            Tarjeta(g, Celda(x, y, cw, ch, gap, cols, 2), "RAM", Junta(F.RamTotalGB > 0 ? F.RamTotalGB + " GB" : "", F.RamTipo, F.Ram.Count > 0 ? F.Ram.Count + T(" módulos", " modules") : ""), P(ram),
                MonitorPC.NivelRam(L.RamUso), false, ram, T("Usada", "Used"),
                Gb(L.RamUsadaGB) + T(" de ", " of ") + Gb(L.RamTotalGB), RamCorto(F), MonitorPC.CAT_RAM);

            // Disco: el que esta peor (o el primero)
            Disco d = null; Nivel nd = Nivel.Gris;
            foreach (Disco x2 in L.Discos)
            {
                Nivel n2 = MonitorPC.NivelDisco(x2); Nivel v2 = MonitorPC.NivelVida(x2.Vida); if (v2 > n2) n2 = v2;
                if (d == null || n2 > nd) { d = x2; nd = n2; }
            }
            if (d != null)
            {
                Nivel nt = MonitorPC.NivelDisco(d), nv = MonitorPC.NivelVida(d.Vida);
                string pal = nv > nt ? (nv == Nivel.Rojo ? T("Casi gastado", "Nearly worn out") : T("Poca vida", "Low life")) : null;
                Tarjeta(g, Celda(x, y, cw, ch, gap, cols, 3), L.Discos.Count > 1 ? T("Discos (" + L.Discos.Count + ")", "Drives (" + L.Discos.Count + ")") : T("Disco", "Drive"), d.Nombre, Gc(d.Temp), nt, nd, pal, true,
                    d.Lleno, T("Lleno", "Full"), d.Vida.HasValue ? T("Vida ", "Life ") + P(d.Vida) : "", d.TempMax.HasValue ? T("Máx. sesión ", "Session max ") + Gc(d.TempMax) : "", MonitorPC.CAT_DISCOS);
            }
            else
                Tarjeta(g, Celda(x, y, cw, ch, gap, cols, 3), T("Discos", "Drives"), T("Sin datos de los discos", "No drive data"), "—", Nivel.Gris, true, null, null, "", "", MonitorPC.CAT_DISCOS);

            y += ((4 + cols - 1) / cols) * (ch + gap);

            // Grafico de temperaturas
            int gh = S(110);
            Rectangle rg = new Rectangle(x, y, w, gh);
            Caja(g, rg, cTarj, cLinea, 1);
            int lx = x + S(12);
            Tx(g, T("Temperaturas, últimos 5 minutos", "Temperatures, last 5 minutes"), fNegrita, cTexto, lx, y + S(8));
            int lgx = x + S(12) + Ancho(T("Temperaturas, últimos 5 minutos", "Temperatures, last 5 minutes"), fNegrita) + S(16);
            lgx = Leyenda(g, lgx, y + S(9), "CPU", cAcento);
            lgx = Leyenda(g, lgx, y + S(9), "GPU", cVioleta);
            Func<Foto, float> fc = delegate (Foto f) { return f.CpuT; };
            Func<Foto, float> fg = delegate (Foto f) { return f.GpuT; };
            Grafico(g, new Rectangle(x + S(12), y + S(14) + fNegrita.Height, w - S(24), gh - S(24) - fNegrita.Height), pc.Historia(300), 300, 20, 100, "°", fc, cAcento, fg, cVioleta);
            y += gh + gap;

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
            if (L.Bateria >= 0) energia = Junta(energia, T("Batería ", "Battery ") + L.Bateria + " %" + (L.Enchufada ? T(" (enchufada)", " (plugged in)") : T(" (a batería)", " (on battery)")));
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

        static Rectangle Celda(int x, int y, int cw, int ch, int gap, int cols, int i)
        {
            return new Rectangle(x + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap), cw, ch);
        }

        static string RamCorto(InfoFija F)
        {
            string a = F.RamXmpNivel == Nivel.Verde ? T("XMP/EXPO bien", "XMP/EXPO ok") : F.RamXmpNivel == Nivel.Amarillo ? T("XMP/EXPO apagado", "XMP/EXPO off") : "";
            string b = F.RamCanalNivel == Nivel.Verde ? "dual channel" : F.RamCanalNivel == Nivel.Amarillo ? "single channel" : "";
            return Junta(a, b);
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
            int h = S(12) + fTitulo.Height + S(2) + fChica.Height + S(6) + fGrande.Height + S(8) + ast * 2 + fChica.Height + S(14);
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
            string grande = L.CpuTemp.HasValue ? Gc(L.CpuTemp) : T("sin temperatura", "no temperature");
            Tx(g, grande, fGrande, L.CpuTemp.HasValue ? De(n) : cGris, px, py);
            int gx = px + Ancho(grande, fGrande) + S(12);
            if (L.CpuTemp.HasValue)
            {
                Tx(g, T("límite (TjMax) ", "limit (TjMax) ") + tj + " °C", fChica, cGris, gx, py + S(6));
                Barra(g, new Rectangle(gx, py + S(8) + fChica.Height, Math.Max(S(40), r.Right - S(14) - gx), S(6)), L.CpuTemp.Value * 100f / tj, De(n));
            }
            py += fGrande.Height + S(8);
            int sw = iw / 2;
            Stat(g, px, py, sw, T("Uso", "Load"), P(L.CpuLoad), cTexto);
            Stat(g, px + sw, py, sw, T("Velocidad (prom. / pico)", "Clock (avg / peak)"), Ghz(L.CpuClockProm) + (L.CpuClockMax.HasValue ? " / " + Ghz(L.CpuClockMax) : ""), cTexto);
            py += ast;
            Stat(g, px, py, sw, T("Potencia", "Power"), Wt(L.CpuPower), cTexto);
            Stat(g, px + sw, py, sw, T("Voltaje", "Voltage"), Vt(L.CpuVolt), cTexto);
            py += ast;
            MonitorPC.Picos pj = pc.PicoJuego;
            Tx(g, Junta(Maximos(pc.PicoSesion.CpuT, pj.Juego != "" && pj.Juego == L.Juego ? pj.CpuT : float.NaN, L.Juego, " °C"),
                Foto.Hay(pc.PicoSesion.CpuW) ? T("máx. ", "max ") + Wt(pc.PicoSesion.CpuW) : ""), fChica, cGris, new Rectangle(px, py, iw, fChica.Height + S(2)), TextFormatFlags.Left);

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
            bool vram = L.GpuVramUsada.HasValue && L.GpuVramTotal.HasValue;
            int h = S(12) + fTitulo.Height + S(2) + fChica.Height + S(6) + fGrande.Height + S(8) + ast * 2 + (vram ? fNegrita.Height + S(10) : 0)
                + (fChica.Height + S(6)) * ((conector ? 2 : 1) + (L.GpuOtras.Count > 0 ? 1 : 0)) + S(8);
            Rectangle r = new Rectangle(x, y, lw, h);
            Caja(g, r, cTarj, peor == Nivel.Rojo ? cRojo : cLinea, peor == Nivel.Rojo ? 2 : 1);
            int px = r.X + S(14), py = r.Y + S(12), iw = r.Width - S(28);
            Tx(g, L.GpuNombre, fTitulo, cTexto, new Rectangle(px, py, iw - S(110), fTitulo.Height + S(2)), TextFormatFlags.Left);
            Pastilla(g, r.Right - S(14), py, peor, true);
            py += fTitulo.Height + S(2);
            string drv = F.GpuDriver != "" ? T("Driver ", "Driver ") + F.GpuDriver + (F.GpuDriverFecha.HasValue ? " (" + F.GpuDriverFecha.Value.ToString(Modulo.Idioma == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy") + ")" : "") : "";
            bool viejo = F.GpuDriverFecha.HasValue && (DateTime.Now - F.GpuDriverFecha.Value).TotalDays > 365;
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
            py += fGrande.Height + S(8);
            int sw = iw / 3;
            Stat(g, px, py, sw, T("Uso", "Load"), P(L.GpuLoad), cTexto);
            Stat(g, px + sw, py, sw, T("Núcleo", "Core"), Mhz(L.GpuClock), cTexto);
            Stat(g, px + sw * 2, py, sw, T("Memoria", "Memory"), Mhz(L.GpuMemClock), cTexto);
            py += ast;
            Stat(g, px, py, sw, T("Potencia", "Power"), Wt(L.GpuPower), cTexto);
            string fan = L.GpuFanRpm.HasValue ? ((int)L.GpuFanRpm.Value) + " RPM" : "";
            if (L.GpuFanPct.HasValue) fan = fan != "" ? fan + " (" + (int)L.GpuFanPct.Value + " %)" : ((int)L.GpuFanPct.Value) + " %";
            Stat(g, px + sw, py, sw, T("Ventiladores", "Fans"), fan != "" ? fan : "—", cTexto);
            Stat(g, px + sw * 2, py, sw, T("Voltaje", "Voltage"), Vt(L.GpuVolt), cTexto);
            py += ast;
            // VRAM
            if (vram)
            {
                float pct = L.GpuVramUsada.Value * 100f / L.GpuVramTotal.Value;
                string vt = "VRAM " + Gb(L.GpuVramUsada.Value / 1024.0) + T(" de ", " of ") + Gb(L.GpuVramTotal.Value / 1024.0);
                Tx(g, vt, fNegrita, cTexto, px, py);
                int vw = Ancho(vt, fNegrita) + S(10);
                Barra(g, new Rectangle(px + vw, py + (fNegrita.Height - S(8)) / 2, iw - vw, S(8)), pct, pct >= 97 ? cRojo : pct >= 90 ? cAmarillo : cAcento);
                py += fNegrita.Height + S(10);
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
            if (L.Discos.Count == 0)
                return Cartel(g, x, y, w, Nivel.Gris, T("No se encontraron discos con sensores (los discos por USB no se leen).", "No drives with sensors were found (USB drives are not read)."), null, null);
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
                filas.Add(new string[] { d.Nombre, temp, d.Vida.HasValue ? P(d.Vida) : "—", d.Lleno.HasValue ? P(d.Lleno) : "—", d.EscritoGB.HasValue ? MonitorPC.Tb(d.EscritoGB.Value) : "—", horas != "" ? horas : "—" });
                Nivel nl = d.Lleno.HasValue ? MonitorPC.Umbral(d.Lleno, 85, 95) : Nivel.Gris;
                niveles.Add(new Nivel[] { Nivel.Gris, MonitorPC.NivelDisco(d), MonitorPC.NivelVida(d.Vida), nl, Nivel.Gris, Nivel.Gris });
            }
            y = Tabla(g, x, y, w, cab, prop, filas, niveles);
            Tx(g, T("La vida útil la calcula el propio disco (SMART). Un SSD se frena solo si pasa su temperatura de aviso.", "Life left is computed by the drive itself (SMART). An SSD throttles itself if it passes its warning temperature."),
                fChica, cGris, new Rectangle(x + S(4), y - S(4), w, fChica.Height + S(2)), TextFormatFlags.Left);
            return y + fChica.Height + S(6);
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
            Tx(g, t, fNegrita, cAcento, x + S(2), y + S(4));
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
