// Xuca Monitor: aviso de version nueva (se fija en los Releases de GitHub) y zoom del texto.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Xuca
{
    public partial class XucaMonitor
    {
        const string ApiUltima = "https://api.github.com/repos/011Xuca/xuca-monitor/releases/latest";

        System.Threading.Timer timerAct;
        ToolStripMenuItem miAct;
        Button cartelAct;
        volatile string nuevaVer, urlNueva;
        bool actualizando;

        // Se fija a los 20 s de abrir y despues cada 6 horas (una consulta chiquita a GitHub)
        void EmpezarBusquedaAct()
        {
            // Lo que dejo una actualizacion anterior
            try { File.Delete(Exe + ".viejo"); } catch { }
            timerAct = new System.Threading.Timer(delegate { Buscar(); }, null, 20000, 6 * 3600 * 1000);
        }

        static Version Ver(string t)
        {
            Match m = Regex.Match(t ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        void Buscar()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string json;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "XucaMonitor/" + Version;
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString(ApiUltima);
                }
                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                Match url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+XucaMonitor\\.exe)\"", RegexOptions.IgnoreCase);
                if (!tag.Success || !url.Success) return;
                Version nueva = Ver(tag.Groups[1].Value), mia = Ver(Version);
                if (nueva == null || mia == null || nueva <= mia) return;
                nuevaVer = nueva.ToString(3);
                urlNueva = url.Groups[1].Value;
                EnUi(delegate { TextoActualizar(); });
            }
            catch { }
        }

        void CrearCartelAct()
        {
            cartelAct = new Button();
            cartelAct.Dock = DockStyle.Top;
            cartelAct.Height = (int)(34 * fv.DeviceDpi / 96f);
            cartelAct.FlatStyle = FlatStyle.Flat;
            cartelAct.FlatAppearance.BorderSize = 0;
            cartelAct.BackColor = Color.FromArgb(229, 181, 103);
            cartelAct.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 200, 130);
            cartelAct.ForeColor = Color.FromArgb(32, 24, 8);
            cartelAct.Font = new Font("Segoe UI Semibold", 9.5f);
            cartelAct.Cursor = Cursors.Hand;
            cartelAct.UseVisualStyleBackColor = false;
            cartelAct.Click += delegate { Actualizar(); };
            fv.Controls.Add(cartelAct);
            TextoActualizar();
        }

        void TextoActualizar()
        {
            bool hay = nuevaVer != null;
            if (miAct != null)
            {
                miAct.Visible = hay;
                if (hay) miAct.Text = T("Actualizar a la v", "Update to v") + nuevaVer;
            }
            if (cartelAct != null && !cartelAct.IsDisposed)
            {
                cartelAct.Visible = hay;
                if (hay) cartelAct.Text = actualizando ? T("Descargando la versión nueva...", "Downloading the new version...")
                    : T("Hay una versión nueva (v" + nuevaVer + "). Tocá acá para actualizar: se baja de GitHub y se vuelve a abrir sola.",
                        "There is a new version (v" + nuevaVer + "). Click here to update: it downloads from GitHub and reopens by itself.");
            }
        }

        // Baja el .exe nuevo, lo pone en lugar de este y abre el nuevo
        void Actualizar()
        {
            if (actualizando || urlNueva == null) return;
            actualizando = true;
            TextoActualizar();
            string url = urlNueva;
            bool conVentana = fv != null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string nuevo = Exe + ".nuevo", viejo = Exe + ".viejo";
                string error = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "XucaMonitor/" + Version;
                        wc.DownloadFile(url, nuevo);
                    }
                    // Que sea un .exe de verdad y completo
                    byte[] b = File.ReadAllBytes(nuevo);
                    if (b.Length < 20000 || b[0] != (byte)'M' || b[1] != (byte)'Z') throw new Exception(T("la descarga vino incompleta", "the download was incomplete"));
                    // Windows deja renombrar un .exe abierto: el viejo pasa a .viejo y el nuevo toma su lugar
                    try { File.Delete(viejo); } catch { }
                    File.Move(Exe, viejo);
                    try { File.Move(nuevo, Exe); }
                    catch { File.Move(viejo, Exe); throw; }
                }
                catch (Exception ex) { error = ex.Message; try { File.Delete(nuevo); } catch { } }
                EnUi(delegate
                {
                    actualizando = false;
                    if (error != null)
                    {
                        TextoActualizar();
                        MessageBox.Show(T("No se pudo actualizar: ", "Could not update: ") + error, Nombre, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    string exe = Exe;
                    Salir();
                    try { Process.Start(exe, "/actualizado" + (conVentana ? "" : " /inicio")); } catch { }
                });
            });
        }

        // ---------- Zoom del texto (Ctrl + rueda, Ctrl + / Ctrl -, Ctrl + 0) ----------
        float zoom = 10f;

        void PrepararZoom()
        {
            tb.MouseWheel += delegate (object s, MouseEventArgs e)
            {
                if ((Control.ModifierKeys & Keys.Control) == 0) return;
                HandledMouseEventArgs h = e as HandledMouseEventArgs;
                if (h != null) h.Handled = true;
                CambiarZoom(zoom + (e.Delta > 0 ? 1f : -1f));
            };
        }

        void TeclaZoom(object s, KeyEventArgs e)
        {
            if (!e.Control) return;
            if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) CambiarZoom(zoom + 1f);
            else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) CambiarZoom(zoom - 1f);
            else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) CambiarZoom(10f);
            else return;
            e.Handled = true; e.SuppressKeyPress = true;
        }

        void CambiarZoom(float z)
        {
            z = Math.Max(7f, Math.Min(24f, z));
            if (z == zoom || tb == null) return;
            zoom = z;
            Font viejo = tb.Font;
            tb.Font = new Font("Consolas", zoom);
            viejo.Dispose();
            GuardarAjuste("zoom", zoom.ToString(CultureInfo.InvariantCulture));
        }
    }
}
