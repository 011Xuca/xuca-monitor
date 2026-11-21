// Pulso: aviso de version nueva (se fija en los Releases de GitHub) y zoom del texto.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Xuca
{
    public partial class Pulso
    {
        const string ApiUltima = "https://api.github.com/repos/011Xuca/pulso/releases/latest";
        const string AssetExe = "Pulso.exe";

        System.Threading.Timer timerAct;
        ToolStripMenuItem miAct;
        Button cartelAct;
        volatile string nuevaVer, urlNueva, shaNueva;
        bool actualizando;

        // Una sola consulta a GitHub por cada vez que se abre la app, a los 20 s (para no frenar el arranque).
        // Se llama al final de Iniciar: si se llego hasta aca, esta version arranco bien y la anterior ya no hace falta.
        void EmpezarBusquedaAct()
        {
            // Instalada desde la Microsoft Store la actualiza la Store (la carpeta del paquete no se puede escribir)
            if (Empaquetada) return;
            // Lo que dejo una actualizacion anterior (o una a medias)
            foreach (string resto in new string[] { ".viejo", ".nuevo", ".roto" }) { try { File.Delete(Exe + resto); } catch { } }
            timerAct = new System.Threading.Timer(delegate { Buscar(); }, null, 20000, Timeout.Infinite);
        }

        static Version Ver(string t)
        {
            Match m = Regex.Match(t ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        // Si hay una version nueva, aparece el cartel amarillo y la opcion en la bandeja; si no (o sin internet), nada
        void Buscar()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string json;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "Pulso/" + Version;
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString(ApiUltima);
                }
                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                Match url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+/" + Regex.Escape(AssetExe) + ")\"", RegexOptions.IgnoreCase);
                if (!tag.Success || !url.Success) return;
                Version nueva = Ver(tag.Groups[1].Value), mia = Ver(Version);
                if (nueva == null || mia == null || nueva <= mia) return;
                // El sha256 que GitHub publica del .exe, si lo da: esta en el mismo asset, despues de "uploader": {...}
                string antes = json.Substring(0, url.Index);
                Match sha = Regex.Match(antes.Substring(antes.LastIndexOf('}') + 1), "\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"");
                shaNueva = sha.Success ? sha.Groups[1].Value : null;
                urlNueva = url.Groups[1].Value;
                nuevaVer = nueva.ToString(3);
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
            string url = urlNueva, sha = shaNueva;
            Version esperada = Ver(nuevaVer);
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
                        wc.Headers[HttpRequestHeader.UserAgent] = "Pulso/" + Version;
                        wc.DownloadFile(url, nuevo);
                    }
                    // Que sea el .exe completo (mismo sha256 que publica GitHub) y un programa .NET de verdad
                    string incompleta = T("la descarga vino incompleta o dañada", "the download was incomplete or damaged");
                    if (sha != null)
                    {
                        using (SHA256 h = new SHA256CryptoServiceProvider()) // la version CryptoServiceProvider tambien anda con FIPS activado
                            if (!string.Equals(BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(nuevo))).Replace("-", ""), sha, StringComparison.OrdinalIgnoreCase))
                                throw new Exception(incompleta);
                    }
                    Version bajada;
                    try { bajada = AssemblyName.GetAssemblyName(nuevo).Version; }
                    catch { throw new Exception(incompleta); }
                    // Si el .exe publicado dice una version menor que su Release, al abrir volveria a ofrecer la misma actualizacion para siempre
                    if (esperada == null || bajada < esperada)
                        throw new Exception(T("el .exe publicado es la v" + bajada.ToString(3) + ", no la v" + nuevaVer, "the published .exe is v" + bajada.ToString(3) + ", not v" + nuevaVer));

                    // Windows deja renombrar un .exe abierto: el viejo pasa a .viejo y el nuevo toma su lugar
                    CerrarOtrasCopias(Exe);
                    try { File.Delete(viejo); } catch { }
                    Mover(Exe, viejo);
                    try { Mover(nuevo, Exe); }
                    catch { Mover(viejo, Exe); throw; }
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

        // Cierra y vuelve a abrir esta misma copia (con o sin ventana, como estaba). Lo usa la deteccion de PawnIO.
        public void Reiniciar()
        {
            bool conVentana = fv != null;
            string exe = Exe;
            Salir();
            try { Process.Start(exe, "/actualizado" + (conVentana ? "" : " /inicio")); } catch { }
        }

        // Esta copia se cierra sola despues del reemplazo, pero puede haber otras del mismo .exe (otra sesion de Windows,
        // o una version anterior que quedo colgada al cerrarse): si siguen vivas el .viejo no se puede borrar y la
        // actualizacion falla. Se cierran de golpe (no se les puede pedir que salgan desde otra sesion).
        static void CerrarOtrasCopias(string exe)
        {
            Process yo = Process.GetCurrentProcess();
            foreach (Process p in Process.GetProcessesByName(yo.ProcessName))
            {
                try
                {
                    if (p.Id == yo.Id) continue;
                    string f = p.MainModule.FileName;
                    if (f.Equals(exe, StringComparison.OrdinalIgnoreCase) || f.Equals(exe + ".viejo", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch { } // de otro usuario o ya cerrada: se sigue igual
                finally { p.Dispose(); }
            }
        }

        // La version recien bajada no pudo arrancar (/actualizado y error en Iniciar): vuelve a poner la anterior
        // (el .viejo, que solo se borra cuando una version arranca bien) y la abre. Despues de usarlo el .viejo ya
        // no existe, asi que no puede quedar dando vueltas.
        static void VolverAVersionAnterior(bool oculto)
        {
            string exe = Application.ExecutablePath, viejo = exe + ".viejo", roto = exe + ".roto";
            if (!File.Exists(viejo)) return;
            try
            {
                try { File.Delete(roto); } catch { }
                Mover(exe, roto);
                try { Mover(viejo, exe); }
                catch { Mover(roto, exe); throw; }
                Process.Start(exe, "/actualizado" + (oculto ? " /inicio" : ""));
            }
            catch (Exception ex)
            {
                // Que quede escrito por que no se pudo (si no, la app queda rota sin ninguna pista)
                GuardarCrash(new Exception(T("No se pudo volver a la versión anterior. Está guardada al lado como ", "Could not go back to the previous version. It is saved next to it as ") + viejo, ex));
                return;
            }
            Environment.Exit(1); // que esta copia no quede viva con el mutex o el icono de la bandeja
        }

        // Renombrar con reintentos (hasta ~5 s): el antivirus suele abrir un .exe recien escrito o recien ejecutado
        // unos instantes para analizarlo, y mientras tanto Windows no deja renombrarlo.
        static void Mover(string de, string a)
        {
            for (int i = 0; ; i++)
            {
                try { File.Move(de, a); return; }
                catch (IOException) { if (i >= 20) throw; }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; }
                Thread.Sleep(250);
            }
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
