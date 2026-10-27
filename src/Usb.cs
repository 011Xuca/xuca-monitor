// Monitor USB: avisa que aparato se desconecto cuando suena el ruido de Windows.
// No revisa nada cada tanto: Windows le avisa (WM_DEVICECHANGE) y recien ahi trabaja.
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Xuca
{
    public class MonitorUsb : Modulo
    {
        public MonitorUsb()
        {
            Clave = "usb"; PestEs = PestEn = TipEs = TipEn = "USB"; NombreEs = "Monitor USB"; NombreEn = "USB Monitor";
            RegistroArch = "registro_usb.txt"; HistArch = "historial_usb.txt";
            ContEs = "Desconexiones"; ContEn = "Disconnections";
            VacioEs = "Todavía no se desconectó nada. Cuando suene el ruido, acá vas a ver qué fue.";
            VacioEn = "Nothing has disconnected yet. When you hear the sound, you will see here what it was.";
            ApagadoEs = "El monitor USB está apagado. Prendelo con la casilla \"Activado\" para ver qué se desconecta cuando suena el ruido.";
            ApagadoEn = "The USB monitor is off. Turn it on with the \"Enabled\" box to see what disconnects when you hear the sound.";
        }

        class Ev { public bool Sale; public DateTime T; public string Ruta; }

        Escucha disp;
        System.Windows.Forms.Timer espera;
        readonly List<Ev> lote = new List<Ev>();
        readonly object cerrojoProc = new object();
        readonly Dictionary<string, DateTime> salidas = new Dictionary<string, DateTime>();

        protected override void Empezar()
        {
            // Un aparato manda varios avisos seguidos (uno por cada parte): se espera 1,5 s de calma y se muestran juntos
            if (espera == null) CrearEspera();
            disp = new Escucha(this);
            if (!disp.Ok)
            {
                string t = T("Windows no dejó escuchar los avisos de dispositivos. Apagalo y volvelo a prender.", "Windows did not allow listening for device events. Turn it off and on again.");
                Log(t);
                Avisar(t);
            }
        }

        void CrearEspera()
        {
            espera = new System.Windows.Forms.Timer();
            espera.Interval = 1500;
            espera.Tick += delegate
            {
                espera.Stop();
                List<Ev> copia = new List<Ev>(lote);
                lote.Clear();
                if (copia.Count > 0) ThreadPool.QueueUserWorkItem(delegate { lock (cerrojoProc) { try { Procesar(copia); } catch (Exception ex) { Log("Error: " + ex.Message); } } });
            };
        }

        protected override void Terminar()
        {
            if (espera != null) espera.Stop();
            if (disp != null) { disp.Cerrar(); disp = null; }
            lote.Clear();
        }

        void Llego(bool sale, string ruta)
        {
            Ev e = new Ev(); e.Sale = sale; e.T = DateTime.Now; e.Ruta = ruta;
            lote.Add(e);
            espera.Stop();
            espera.Start();
        }

        // Ventana invisible que recibe el mismo aviso de Windows que hace sonar el ruido
        class Escucha : NativeWindow
        {
            [StructLayout(LayoutKind.Sequential)] struct DBH { public int size; public int type; public int res; }
            [StructLayout(LayoutKind.Sequential)] struct DBI { public int size; public int type; public int res; public Guid cls; public short name; }
            [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterDeviceNotification(IntPtr h, IntPtr f, int flags);
            [DllImport("user32.dll")] static extern bool UnregisterDeviceNotification(IntPtr h);
            IntPtr reg;
            readonly MonitorUsb m;
            public bool Ok;
            public Escucha(MonitorUsb mon)
            {
                m = mon;
                CreateHandle(new CreateParams());
                DBI f = new DBI(); f.size = Marshal.SizeOf(typeof(DBI)); f.type = 5;
                IntPtr p = Marshal.AllocHGlobal(f.size);
                Marshal.StructureToPtr(f, p, false);
                // 4 = todas las clases de dispositivo
                reg = RegisterDeviceNotification(Handle, p, 4);
                Ok = reg != IntPtr.Zero;
                Marshal.FreeHGlobal(p);
            }
            public void Cerrar()
            {
                if (reg != IntPtr.Zero) { UnregisterDeviceNotification(reg); reg = IntPtr.Zero; }
                DestroyHandle();
            }
            protected override void WndProc(ref Message msg)
            {
                if (msg.Msg == 0x219 && msg.LParam != IntPtr.Zero)
                {
                    long w = msg.WParam.ToInt64();
                    if (w == 0x8000 || w == 0x8004)
                    {
                        DBH h = (DBH)Marshal.PtrToStructure(msg.LParam, typeof(DBH));
                        if (h.type == 5) m.Llego(w == 0x8004, Marshal.PtrToStringUni(new IntPtr(msg.LParam.ToInt64() + 28)));
                    }
                }
                base.WndProc(ref msg);
            }
        }

        // ---------- Datos del aparato (cfgmgr32, sin WMI) ----------
        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint inst, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_DevNode_PropertyW(uint inst, ref DEVPROPKEY key, out uint tipo, byte[] buf, ref uint largo, uint flags);

        static readonly Guid GDev = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        static readonly Guid GBus = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2");

        static string Prop(uint inst, Guid g, uint pid)
        {
            DEVPROPKEY k = new DEVPROPKEY(); k.fmtid = g; k.pid = pid;
            uint tipo, largo = 0;
            CM_Get_DevNode_PropertyW(inst, ref k, out tipo, null, ref largo, 0);
            if (largo == 0 || largo > 4096) return "";
            byte[] b = new byte[largo];
            if (CM_Get_DevNode_PropertyW(inst, ref k, out tipo, b, ref largo, 0) != 0 || tipo != 0x12) return "";
            return Encoding.Unicode.GetString(b, 0, (int)largo).TrimEnd('\0').Trim();
        }

        class Datos { public string Nombre = "", Producto = "", Clase = ""; }

        static Datos Leer(string id)
        {
            Datos d = new Datos();
            uint inst;
            // 1 = tambien los que ya no estan conectados
            if (CM_Locate_DevNodeW(out inst, id, 1) == 0)
            {
                d.Nombre = Prop(inst, GDev, 14);
                if (d.Nombre == "") d.Nombre = Prop(inst, GDev, 2);
                d.Clase = Prop(inst, GDev, 9);
                d.Producto = Prop(inst, GBus, 4);
            }
            if (d.Nombre == "") d.Nombre = d.Producto;
            if (d.Nombre == "") d.Nombre = T("(sin nombre)", "(no name)");
            return d;
        }

        // "\\?\USB#VID_1234&PID_5678#xxx#{guid}" -> "USB\VID_1234&PID_5678\xxx"
        static string AId(string ruta)
        {
            if (ruta == null) return "";
            string r = Regex.Replace(ruta, @"^\\\\\?\\", "");
            r = Regex.Replace(r, @"#\{[0-9A-Fa-f\-]+\}.*$", "");
            return r.Replace('#', '\\');
        }

        // Agrupa por aparato: lo que tiene VID/PID es el mismo aparato fisico aunque tenga varias partes
        static string ClaveDe(string id)
        {
            Match m = Regex.Match(id, "VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
            if (m.Success) return ("VID_" + m.Groups[1].Value + "&PID_" + m.Groups[2].Value).ToUpperInvariant();
            m = Regex.Match(id, "_VID&([0-9A-Fa-f]{6,8})_PID&([0-9A-Fa-f]{4})");
            if (m.Success) return ("BT VID " + m.Groups[1].Value + " PID " + m.Groups[2].Value).ToUpperInvariant();
            return id.ToUpperInvariant();
        }

        static List<string> Pista(List<string> ids, bool volvio)
        {
            string todo = string.Join(" ", ids.ToArray()).ToUpperInvariant();
            List<string> p = new List<string>();
            if (volvio) p.Add(T("Se desconectó y volvió solo: casi siempre es el cable o el puerto (flojo o con poca energía), el ahorro de energía USB o el driver. Probá otro puerto, mejor uno de atrás (directo a la placa madre).", "It disconnected and came back by itself: it is almost always the cable or the port (loose or low on power), USB power saving or the driver. Try another port, preferably a back one (straight on the motherboard)."));
            if (Regex.IsMatch(todo, @"BTHENUM|BTHLE|BTH\\")) p.Add(T("Es algo Bluetooth (auriculares, control, celular): se corta cuando se aleja, se apaga o entra en reposo.", "It is a Bluetooth device (headphones, controller, phone): it drops when it moves away, turns off or goes to sleep."));
            if (Regex.IsMatch(todo, @"DISPLAY\\|MONITOR\\")) p.Add(T("Es un monitor: pasa cuando se apaga, entra en reposo o cambia de entrada (muy común con DisplayPort).", "It is a monitor: it happens when it turns off, goes to sleep or switches input (very common with DisplayPort)."));
            if (Regex.IsMatch(todo, "HDAUDIO|MMDEVAPI")) p.Add(T("Es una salida o entrada de sonido: pasa al prender o apagar auriculares, parlantes o el monitor con sonido (HDMI/DisplayPort).", "It is an audio output or input: it happens when headphones, speakers or a monitor with sound (HDMI/DisplayPort) turn on or off."));
            if (Regex.IsMatch(todo, "XINPUT|XBOX|VID_045E&PID_02")) p.Add(T("Es un control de Xbox o su receptor.", "It is an Xbox controller or its receiver."));
            if (Regex.IsMatch(todo, @"USBSTOR|SCSI\\|STORAGE")) p.Add(T("Es un disco o pendrive.", "It is a drive or USB stick."));
            if (Regex.IsMatch(todo, @"ROOT\\|SWD\\") && !Regex.IsMatch(todo, @"USB\\|HID\\|BTH")) p.Add(T("Es un dispositivo virtual (lo crea un programa: VPN, audio virtual, antitrampas, programas de mouse o teclado, etc.).", "It is a virtual device (made by a program: VPN, virtual audio, anti-cheat, mouse or keyboard software, etc.)."));
            return p;
        }

        class Grupo { public List<string> Ids = new List<string>(); public bool Sale, Entra; }

        void Procesar(List<Ev> evs)
        {
            DateTime ini = evs[0].T;
            List<string> orden = new List<string>();
            Dictionary<string, Grupo> grupos = new Dictionary<string, Grupo>();
            foreach (Ev e in evs)
            {
                string id = AId(e.Ruta);
                if (id == "") continue;
                string k = ClaveDe(id);
                Grupo g;
                if (!grupos.TryGetValue(k, out g)) { g = new Grupo(); grupos[k] = g; orden.Add(k); }
                bool ya = false;
                foreach (string x in g.Ids) if (string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) ya = true;
                if (!ya) g.Ids.Add(id);
                if (e.Sale) g.Sale = true; else g.Entra = true;
            }
            if (orden.Count == 0) return;

            List<string> L = new List<string>();
            int desc = 0;
            string sinNombre = T("(sin nombre)", "(no name)");
            foreach (string k in orden)
            {
                Grupo g = grupos[k];
                // El nombre del aparato: primero la parte "USB\VID..." sin MI_ (el aparato entero), si no la primera
                string princ = null;
                foreach (string i in g.Ids) if (Regex.IsMatch(i, @"^USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}\\", RegexOptions.IgnoreCase)) { princ = i; break; }
                if (princ == null) princ = g.Ids[0];
                Datos d = Leer(princ);
                List<string> nombres = new List<string>();
                foreach (string i in g.Ids)
                {
                    Datos x = i == princ ? d : Leer(i);
                    if (x.Nombre != sinNombre && !nombres.Contains(x.Nombre)) nombres.Add(x.Nombre);
                }

                bool volvio = false;
                string que;
                if (g.Sale && g.Entra) { que = T("SE DESCONECTÓ Y VOLVIÓ A CONECTAR", "DISCONNECTED AND RECONNECTED"); volvio = true; desc++; }
                else if (g.Sale) { que = T("SE DESCONECTÓ", "DISCONNECTED"); desc++; salidas[k] = ini; }
                else
                {
                    que = T("SE CONECTÓ", "CONNECTED");
                    DateTime s;
                    if (salidas.TryGetValue(k, out s))
                    {
                        int seg = (int)Math.Round((ini - s).TotalSeconds);
                        if (seg <= 30) { que = T("VOLVIÓ A CONECTARSE (estuvo " + seg + " s desconectado)", "RECONNECTED (it was gone for " + seg + " s)"); volvio = true; }
                        salidas.Remove(k);
                    }
                }

                string titulo = d.Nombre;
                if (d.Producto != "" && d.Producto != d.Nombre) titulo += "  (\"" + d.Producto + "\")";
                L.Add(Fecha(ini) + "  " + que);
                L.Add(T("  Aparato: ", "  Device:  ") + titulo);
                if (d.Clase != "") L.Add(T("  Tipo:    ", "  Type:    ") + d.Clase);
                if (k.StartsWith("VID_")) L.Add("  Ids:     " + k);
                if (nombres.Count > 1) L.Add(T("  Partes:  ", "  Parts:   ") + string.Join(", ", nombres.GetRange(0, Math.Min(6, nombres.Count)).ToArray()));
                foreach (string p in Pista(g.Ids, volvio)) L.Add("  -> " + p);
                L.Add(T("  Id completo: ", "  Full id: ") + princ);
                L.Add("");
            }

            // Errores y avisos de Windows desde 10 s antes
            List<EventRecord> ev = Eventos.Leer("System", ini.AddSeconds(-10), 20, true);
            if (ev.Count > 8) ev.RemoveRange(0, ev.Count - 8);
            if (ev.Count > 0)
            {
                L.Add(T("Errores o avisos de Windows en ese momento:", "Windows errors or warnings at that moment:"));
                foreach (EventRecord e in ev)
                {
                    L.Add("  " + Eventos.Hora(e) + "  " + e.ProviderName + " (" + e.Id + "): " + Eventos.PrimeraLinea(e, 120));
                    e.Dispose();
                }
            }

            foreach (string x in L) Log(x);
            Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), desc);
        }
    }
}
