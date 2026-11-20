// Pulso: visor de novedades. Muestra la ultima version del CHANGELOG (CHANGELOG.md en español y CHANGELOG.en.md en
// ingles, embebidos en el .exe por compilar.bat: anda sin internet) en el idioma de la app. Si la version pasa de
// TopeCaracteres se corta en el ultimo limite de viñeta que entra (nunca a mitad de una) y avisa que el resto de la
// version y el historial completo estan en GitHub (boton). Ventana propia, no modal: la app sigue andando.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Xuca
{
    public static class Novedades
    {
        // Tope de texto visible (sin ** ni enlaces) de la ultima version; si la supera se corta en el ultimo limite de viñeta que entra
        const int TopeCaracteres = 2000;
        const string Repo = "https://github.com/011Xuca/pulso/blob/main/";
        static Form ventana;
        static string idiomaAbierto;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        static string Leer(string recurso)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso))
                {
                    if (s == null) return "";
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch { return ""; }
        }

        // Texto visible de una linea del changelog (sin **, `codigo` ni enlaces): es lo que se cuenta contra el tope
        public static string Visible(string l)
        {
            return Inline.Replace(Enlace.Replace(l, "$1"), delegate (Match m) { return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value; });
        }

        // Las lineas de la ultima version (la primera "## ..."), con su titulo. Si el texto visible pasa de
        // TopeCaracteres, se corta en el ultimo limite de linea (viñeta o sub-viñeta) que entra, nunca a mitad de una,
        // y no queda un titulo "###" suelto al final. completa = la version entro entera.
        public static List<string> Ultima(string md, int tope, out bool completa)
        {
            List<string> r = new List<string>();
            completa = true;
            bool dentro = false;
            foreach (string l0 in md.Replace("\r", "").Split('\n'))
            {
                string l = l0.TrimEnd();
                if (l.StartsWith("## ")) { if (dentro) break; dentro = true; r.Add(l); continue; }
                if (dentro && l.Length > 0) r.Add(l);
            }
            int n = 0, hasta = r.Count;
            for (int i = 0; i < r.Count; i++)
            {
                n += Visible(r[i]).Length + 1;
                if (n > tope && i > 0) { hasta = i; completa = false; break; }
            }
            if (!completa) { r.RemoveRange(hasta, r.Count - hasta); while (r.Count > 1 && r[r.Count - 1].StartsWith("### ")) r.RemoveAt(r.Count - 1); }
            return r;
        }

        public static void Mostrar(bool oscuro, Icon icono)
        {
            string en = Modulo.Idioma == "en" ? "en" : "es";
            if (ventana != null && !ventana.IsDisposed)
            {
                if (idiomaAbierto == en) { if (ventana.WindowState == FormWindowState.Minimized) ventana.WindowState = FormWindowState.Normal; ventana.Activate(); return; }
                ventana.Close();
            }
            idiomaAbierto = en;
            string archivo = en == "en" ? "CHANGELOG.en.md" : "CHANGELOG.md";
            bool completa;
            List<string> u = Ultima(Leer("changelog." + en + ".md"), TopeCaracteres, out completa);

            Color fondo = oscuro ? Color.FromArgb(28, 32, 39) : Color.White;
            Color ventanaC = oscuro ? Color.FromArgb(20, 22, 27) : Color.FromArgb(243, 244, 246);
            Color texto = oscuro ? Color.White : Color.FromArgb(17, 24, 39);
            Color gris = oscuro ? Color.FromArgb(150, 160, 175) : Color.FromArgb(107, 114, 128);
            Color acento = oscuro ? Color.FromArgb(74, 222, 128) : Color.FromArgb(21, 128, 61);
            Color linea = oscuro ? Color.FromArgb(46, 52, 64) : Color.FromArgb(209, 213, 219);

            Form f = new Form();
            f.Text = T("Novedades de ", "What's new in ") + Pulso.Nombre + " v" + Pulso.Version;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = new Font("Segoe UI", 9.5f);
            float k = f.DeviceDpi / 96f;
            f.ClientSize = new Size((int)(720 * k), (int)(640 * k));
            f.MinimumSize = new Size((int)(420 * k), (int)(320 * k));
            f.BackColor = ventanaC; f.ForeColor = texto;
            if (icono != null) f.Icon = icono;

            Panel pie = new Panel();
            pie.Dock = DockStyle.Bottom; pie.Height = (int)(86 * k); pie.BackColor = ventanaC; pie.Padding = new Padding((int)(14 * k), (int)(8 * k), (int)(14 * k), (int)(10 * k));
            Label aviso = new Label();
            aviso.Dock = DockStyle.Top; aviso.Height = (int)(38 * k); aviso.ForeColor = gris;
            aviso.Text = completa ? T("Las versiones anteriores están en GitHub.", "Earlier versions are on GitHub.")
                : T("El resto de esta versión y el historial completo están en GitHub.", "The rest of this version and the full history are on GitHub.");
            Button ir = new Button(), cerrar = new Button();
            foreach (Button b in new Button[] { ir, cerrar })
            {
                b.FlatStyle = FlatStyle.Flat; b.AutoSize = true; b.Height = (int)(30 * k); b.Cursor = Cursors.Hand;
                b.BackColor = fondo; b.ForeColor = texto; b.UseVisualStyleBackColor = false; b.FlatAppearance.BorderColor = linea;
                b.Padding = new Padding((int)(8 * k), 0, (int)(8 * k), 0);
            }
            ir.Text = T("Ver el historial completo en GitHub", "See the full history on GitHub");
            ir.FlatAppearance.BorderColor = acento;
            ir.Location = new Point((int)(14 * k), (int)(46 * k));
            string url = Repo + archivo;
            ir.Click += delegate { try { Process.Start(url); } catch { } };
            cerrar.Text = T("Cerrar", "Close");
            cerrar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cerrar.Location = new Point(pie.Width - (int)(100 * k), (int)(46 * k));
            cerrar.Click += delegate { f.Close(); };
            pie.Controls.Add(aviso); pie.Controls.Add(ir); pie.Controls.Add(cerrar);
            pie.Resize += delegate { cerrar.Left = pie.ClientSize.Width - cerrar.Width - (int)(14 * k); };

            RichTextBox rt = new RichTextBox();
            rt.Dock = DockStyle.Fill; rt.ReadOnly = true; rt.BorderStyle = BorderStyle.None; rt.BackColor = fondo; rt.ForeColor = texto;
            rt.DetectUrls = false; rt.TabStop = false; rt.Font = f.Font; rt.ScrollBars = RichTextBoxScrollBars.Vertical;
            Panel marco = new Panel();
            marco.Dock = DockStyle.Fill; marco.Padding = new Padding((int)(14 * k)); marco.BackColor = fondo;
            marco.Controls.Add(rt);
            f.Controls.Add(marco); f.Controls.Add(pie);

            if (u.Count == 0) { rt.Text = T("No se pudo cargar el historial de cambios. Está en GitHub.", "The change history could not be loaded. It is on GitHub."); }
            else
            {
                Dibujar(rt, u, f.Font, acento, gris, k);
                // Al final, que se vea claro que hay mas
                rt.SelectionIndent = 0; rt.SelectionHangingIndent = 0; rt.SelectionColor = gris; rt.SelectionFont = new Font(f.Font, FontStyle.Italic);
                rt.AppendText("\n" + (completa
                    ? T("Las versiones anteriores están en GitHub.", "Earlier versions are on GitHub.")
                    : T("… El resto de esta versión y el historial completo están en GitHub.", "… The rest of this version and the full history are on GitHub.")));
            }
            rt.SelectionStart = 0; rt.SelectionLength = 0;
            f.HandleCreated += delegate
            {
                try
                {
                    int v = oscuro ? 1 : 0; DwmSetWindowAttribute(f.Handle, 20, ref v, 4); DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                    rt.SelectionStart = 0; rt.ScrollToCaret();
                }
                catch { }
            };
            f.FormClosed += delegate { f.Dispose(); };
            ventana = f;
            f.Show();
        }

        static string T(string es, string en) { return Modulo.T(es, en); }

        static readonly Regex Enlace = new Regex(@"\[([^\]]+)\]\([^)]+\)");
        static readonly Regex Inline = new Regex(@"\*\*(.+?)\*\*|`([^`]+)`");

        static void Dibujar(RichTextBox rt, List<string> lineas, Font baseF, Color acento, Color gris, float k)
        {
            foreach (string l in lineas)
            {
                if (l.StartsWith("### "))
                {
                    Fijar(rt, 0, 0, gris, new Font(baseF.FontFamily, baseF.Size, FontStyle.Bold));
                    rt.AppendText("\n" + l.Substring(4).ToUpperInvariant() + "\n");
                }
                else if (l.StartsWith("## "))
                {
                    Fijar(rt, 0, 0, acento, new Font(baseF.FontFamily, baseF.Size + 5, FontStyle.Bold));
                    rt.AppendText((rt.TextLength > 0 ? "\n\n" : "") + l.Substring(3) + "\n");
                }
                else
                {
                    string t = Enlace.Replace(l, "$1");
                    int nivel = 0; while (nivel < t.Length && t[nivel] == ' ') nivel++;
                    string cuerpo = t.Substring(nivel);
                    bool vin = cuerpo.StartsWith("- ");
                    if (vin) cuerpo = cuerpo.Substring(2);
                    int sangria = (int)((vin ? 12 + nivel * 10 : 0) * k);
                    Fijar(rt, sangria, vin ? (int)(14 * k) : 0, rt.ForeColor, baseF);
                    if (vin) rt.AppendText("•  ");
                    int pos = 0;
                    foreach (Match m in Inline.Matches(cuerpo))
                    {
                        if (m.Index > pos) { Fijar(rt, sangria, vin ? (int)(14 * k) : 0, rt.ForeColor, baseF); rt.AppendText(cuerpo.Substring(pos, m.Index - pos)); }
                        if (m.Groups[1].Success) { Fijar(rt, sangria, vin ? (int)(14 * k) : 0, rt.ForeColor, new Font(baseF, FontStyle.Bold)); rt.AppendText(m.Groups[1].Value); }
                        else { Fijar(rt, sangria, vin ? (int)(14 * k) : 0, rt.ForeColor, new Font("Consolas", baseF.Size - 0.5f)); rt.AppendText(m.Groups[2].Value); }
                        pos = m.Index + m.Length;
                    }
                    Fijar(rt, sangria, vin ? (int)(14 * k) : 0, rt.ForeColor, baseF);
                    rt.AppendText(cuerpo.Substring(pos) + "\n");
                }
            }
        }

        static void Fijar(RichTextBox rt, int sangria, int colgante, Color c, Font f)
        {
            rt.SelectionStart = rt.TextLength; rt.SelectionLength = 0;
            rt.SelectionIndent = sangria; rt.SelectionHangingIndent = colgante; rt.SelectionRightIndent = 0;
            rt.SelectionColor = c; rt.SelectionFont = f;
        }
    }
}
