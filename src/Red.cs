// Monitor de red: avisa cuando se corta internet o sube el ping, y explica por que.
// Todo corre en un hilo aparte con prioridad baja; la ventana solo existe mientras esta abierta.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Xuca
{
    public class MonitorRed : Modulo
    {
        public MonitorRed()
        {
            Clave = "red"; PestEs = "Red"; PestEn = "Network"; TipEs = "Red"; TipEn = "Net"; NombreEs = "Monitor de red"; NombreEn = "Network Monitor";
            RegistroArch = "registro_red.txt"; HistArch = "historial_red.txt";
            ContEs = "Cortes y lag"; ContEn = "Drops and lag";
            VacioEs = "Todo bien por ahora. Si se corta internet o sube el ping, acá vas a ver qué pasó.";
            VacioEn = "All good so far. If the internet drops or ping goes up, you will see here what happened.";
            ApagadoEs = "El monitor de red está apagado. Prendelo con la casilla \"Activado\" para saber por qué se corta internet o sube el ping.";
            ApagadoEn = "The network monitor is off. Turn it on with the \"Enabled\" box to find out why the internet drops or ping goes up.";
        }

        // Cada cuanto se mira (ms). 40 vueltas = 10 s para medir el lag.
        const int Espera = 250;

        class Placa { public string Id, Nombre, Desc, Tipo, Gw; public bool Arriba; public long Vel; }
        class Cambio { public DateTime T; public string Nombre, Tipo; public bool Arriba; public long Vel; }

        static readonly Regex Virtuales = new Regex("Virtual|VPN|Hyper-V|VMware|VirtualBox|TAP-|Loopback|Bluetooth|WAN Miniport|Npcap|ZeroTier|Tailscale|Radmin|Hamachi|WireGuard|Kernel Debug", RegexOptions.IgnoreCase);
        static readonly Regex Sospechosos = new Regex(@"^(steam|EpicGamesLauncher|Battle\.net|Agent|EADesktop|RiotClientServices|OneDrive|Dropbox|GoogleDriveFS|qbittorrent|uTorrent|BitTorrent|obs64|MoUsoCoreWorker|TiWorker|wuauclt)$", RegexOptions.IgnoreCase);

        Thread hilo;
        volatile bool parar;
        readonly ManualResetEvent despertar = new ManualResetEvent(false);
        volatile bool cambioRed = true;

        readonly Ping p1 = new Ping(), p2 = new Ping(), p3 = new Ping();
        int rtt = -1, rttR = -1;

        List<Placa> prev;
        readonly List<Cambio> hist = new List<Cambio>();
        string rIni;
        bool gwResponde, gwFallo, gwProbado, enCorte, enLag;
        int fallos, pasos, baseR;
        double baseP = 9999;
        long pool0;
        readonly Queue<int> mues = new Queue<int>();
        DateTime iniPosible, ini, fin, lagIni;

        protected override void Empezar()
        {
            // Todo de cero (por si se apago y se volvio a prender)
            parar = false; despertar.Reset(); cambioRed = true;
            hist.Clear(); mues.Clear();
            gwResponde = gwFallo = gwProbado = enCorte = enLag = false;
            fallos = 0; pasos = 0; baseP = 9999;
            // Windows avisa cuando cambia algo de la red: asi se relee la lista de placas solo cuando hace falta
            NetworkChange.NetworkAddressChanged += AlCambiar;
            NetworkChange.NetworkAvailabilityChanged += AlCambiarDisp;
            hilo = new Thread(Vuelta);
            hilo.IsBackground = true;
            hilo.Priority = ThreadPriority.BelowNormal;
            hilo.Start();
        }

        protected override void Terminar()
        {
            NetworkChange.NetworkAddressChanged -= AlCambiar;
            NetworkChange.NetworkAvailabilityChanged -= AlCambiarDisp;
            parar = true;
            despertar.Set();
            if (hilo != null) hilo.Join(3000);
        }

        void AlCambiar(object s, EventArgs e) { cambioRed = true; }
        void AlCambiarDisp(object s, NetworkAvailabilityEventArgs e) { cambioRed = true; }

        // Duerme sin gastar nada; devuelve true si hay que cerrar
        bool Dormir(int ms) { return despertar.WaitOne(ms) || parar; }

        // ---------- Placas ----------
        static bool EsFisica(NetworkInterface n)
        {
            NetworkInterfaceType t = n.NetworkInterfaceType;
            bool tipoOk = t == NetworkInterfaceType.Ethernet || t == NetworkInterfaceType.Wireless80211 || t == NetworkInterfaceType.GigabitEthernet || t == NetworkInterfaceType.FastEthernetT || t == NetworkInterfaceType.FastEthernetFx || t == NetworkInterfaceType.Ethernet3Megabit;
            return tipoOk && !Virtuales.IsMatch(n.Description ?? "");
        }

        // Placas de red fisicas (cable y WiFi), sin las virtuales ni las VPN
        static List<Placa> Placas()
        {
            List<Placa> r = new List<Placa>();
            foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!EsFisica(n)) continue;
                Placa p = new Placa();
                p.Id = n.Id; p.Nombre = n.Name; p.Desc = n.Description;
                p.Tipo = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "WiFi" : "Cable";
                p.Arriba = n.OperationalStatus == OperationalStatus.Up;
                try { p.Vel = n.Speed; } catch { }
                try
                {
                    foreach (GatewayIPAddressInformation g in n.GetIPProperties().GatewayAddresses)
                    {
                        if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && g.Address.ToString() != "0.0.0.0") { p.Gw = g.Address.ToString(); break; }
                    }
                }
                catch { }
                r.Add(p);
            }
            return r;
        }

        // "2.5 Gbps", "1 Gbps", "100 Mbps"
        static string VelTexto(long bps)
        {
            if (bps <= 0) return "";
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (bps >= 1000000000) return ((double)bps / 1000000000).ToString("0.#", ci) + " Gbps";
            return ((double)bps / 1000000).ToString("0", ci) + " Mbps";
        }

        static string Router(List<Placa> l)
        {
            if (l == null) return null;
            foreach (Placa x in l) if (x.Arriba && x.Gw != null) return x.Gw;
            return null;
        }

        // ---------- Pings ----------
        // Internet anda si responde Cloudflare o Google (se preguntan los dos a la vez). Deja en rtt el mas bajo.
        bool HayInternet()
        {
            rtt = -1;
            Task<PingReply> a = null, b = null;
            try { a = p1.SendPingAsync("1.1.1.1", 800); } catch { }
            try { b = p2.SendPingAsync("8.8.8.8", 800); } catch { }
            foreach (Task<PingReply> x in new Task<PingReply>[] { a, b })
            {
                if (x == null) continue;
                try
                {
                    x.Wait();
                    if (x.Result.Status == IPStatus.Success)
                    {
                        int v = (int)x.Result.RoundtripTime;
                        if (rtt < 0 || v < rtt) rtt = v;
                    }
                }
                catch { }
            }
            return rtt >= 0;
        }

        bool PingRouter(string ip)
        {
            rttR = -1;
            if (ip == null) return false;
            try
            {
                PingReply rp = p3.Send(ip, 500);
                if (rp.Status == IPStatus.Success) { rttR = (int)rp.RoundtripTime; return true; }
            }
            catch { }
            return false;
        }

        // Ping promedio al router (5 intentos); -1 si no responde
        int PingRouterProm(string ip)
        {
            int suma = 0, n = 0;
            for (int i = 0; i < 5; i++) if (PingRouter(ip)) { suma += rttR; n++; }
            return n == 0 ? -1 : suma / n;
        }

        // Bytes que entraron y salieron por las placas fisicas
        static void Trafico(out long rx, out long tx)
        {
            rx = 0; tx = 0;
            foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || !EsFisica(n)) continue;
                try { IPInterfaceStatistics st = n.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
            }
        }

        // ---------- Memoria del kernel y tiempo encendida (sin WMI) ----------
        [StructLayout(LayoutKind.Sequential)]
        struct PERF_INFO
        {
            public int cb;
            public IntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public int HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll")] static extern bool GetPerformanceInfo(out PERF_INFO pi, int size);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        // Memoria del kernel que no se puede paginar (MB): si crece sin parar, algun driver pierde memoria
        static long PoolMB()
        {
            try
            {
                PERF_INFO pi;
                if (!GetPerformanceInfo(out pi, Marshal.SizeOf(typeof(PERF_INFO)))) return -1;
                return (long)Math.Round((double)pi.KernelNonpaged.ToInt64() * pi.PageSize.ToInt64() / 1048576);
            }
            catch { return -1; }
        }

        // ---------- Vuelta principal ----------
        void Vuelta()
        {
            try { Preparar(); }
            catch (Exception ex) { Log("Error: " + ex.Message); }
            while (!parar)
            {
                try { Paso(); }
                catch (Exception ex) { Log("Error: " + ex.Message); }
                if (Dormir(Espera)) break;
            }
        }

        void Preparar()
        {
            prev = Placas();
            cambioRed = false;
            rIni = Router(prev);
            for (int i = 0; i < 3 && !gwResponde; i++) if (PingRouter(rIni)) gwResponde = true;
            foreach (Placa x in prev) if (x.Arriba) Log(T("Placa: ", "Card: ") + x.Tipo + " " + x.Nombre + " (" + x.Desc + ") " + VelTexto(x.Vel));
            if (rIni != null)
            {
                if (gwResponde) Log("Router: " + rIni + T(" (responde)", " (answers)"));
                else Log("Router: " + rIni + T(" (no responde a ping, no se va a poder saber si falla el router)", " (does not answer ping, router failures cannot be detected)"));
            }
            foreach (string o in OpcionesScript()) Log(o);
            Log("");
            baseR = PingRouterProm(rIni);
            pool0 = PoolMB();
        }

        void Paso()
        {
            bool ok = HayInternet();
            // La lista de placas se relee si Windows aviso un cambio, si fallo el ping o cada 1 s
            List<Placa> ahora = prev;
            if (cambioRed || !ok || pasos % 4 == 0)
            {
                cambioRed = false;
                ahora = Placas();
                foreach (Placa x in ahora)
                {
                    Placa antes = null;
                    foreach (Placa y in prev) if (y.Id == x.Id) { antes = y; break; }
                    if (antes != null && antes.Arriba != x.Arriba)
                    {
                        Cambio c = new Cambio(); c.T = DateTime.Now; c.Nombre = x.Nombre; c.Tipo = x.Tipo; c.Arriba = x.Arriba; c.Vel = x.Vel;
                        hist.Add(c);
                        string e = T("se conectó", "connected");
                        if (x.Vel > 0) e += T(" a ", " at ") + VelTexto(x.Vel);
                        if (!x.Arriba) e = T("se desconectó", "disconnected");
                        Log(x.Tipo + " " + x.Nombre + " " + e);
                    }
                }
                if (hist.Count > 50) hist.RemoveRange(0, hist.Count - 50);
            }

            if (!ok)
            {
                fallos++;
                if (fallos == 1) { iniPosible = DateTime.Now; gwFallo = false; gwProbado = false; }
                if (fallos >= 2 && !enCorte) { enCorte = true; ini = iniPosible; }
                string r = Router(ahora);
                if (r == null) r = Router(prev);
                if (r == null) r = rIni;
                if (gwResponde && r != null)
                {
                    gwProbado = true;
                    if (!PingRouter(r)) gwFallo = true;
                }
            }
            else
            {
                if (enCorte)
                {
                    fin = DateTime.Now;
                    enCorte = false;
                    Reporte();
                }
                fallos = 0;
                // Lag: promedio de los ultimos 10 s comparado con lo mas bajo que se vio
                mues.Enqueue(rtt);
                while (mues.Count > 40) mues.Dequeue();
                if (mues.Count >= 40)
                {
                    long s = 0;
                    foreach (int v in mues) s += v;
                    int prom = (int)Math.Round((double)s / mues.Count);
                    if (prom < baseP) baseP = prom;
                    double lim = Math.Max(60, Math.Max(baseP + 40, baseP * 2.5));
                    if (!enLag && prom > lim)
                    {
                        enLag = true;
                        lagIni = DateTime.Now;
                        ReporteLag(prom);
                    }
                    else if (enLag && prom < baseP + 20)
                    {
                        enLag = false;
                        double min = Math.Round((DateTime.Now - lagIni).TotalMinutes, 1);
                        string t = Fecha(DateTime.Now) + T("  El ping volvió a la normalidad (" + prom + " ms) después de " + min + " min de lag.", "  Ping is back to normal (" + prom + " ms) after " + min.ToString(CultureInfo.InvariantCulture) + " min of lag.");
                        Log(t);
                        Agregar(t, 0);
                    }
                }
                // Cada ~30 s, si no hay lag, se actualiza el ping normal al router
                if (!enLag && pasos % 120 == 0 && pasos > 0 && rIni != null)
                {
                    int v = PingRouterProm(rIni);
                    if (v >= 0 && (baseR < 0 || v < baseR)) baseR = v;
                }
                if (!gwResponde)
                {
                    string r = Router(ahora);
                    if (r != null && PingRouter(r)) { gwResponde = true; rIni = r; }
                }
            }
            pasos++;
            prev = ahora;
        }

        // ---------- Reportes ----------
        void ReporteLag(int prom)
        {
            List<string> L = new List<string>();
            L.Add("LAG: " + Fecha(DateTime.Now) + T("   el ping subió a " + prom + " ms (lo normal en esta PC es " + (int)baseP + " ms)", "   ping went up to " + prom + " ms (normal on this PC is " + (int)baseP + " ms)"));
            L.Add("");
            long rx1, tx1, rx2, tx2;
            Trafico(out rx1, out tx1);
            if (Dormir(2000)) return;
            Trafico(out rx2, out tx2);
            double baja = Math.Round((rx2 - rx1) * 8.0 / 2 / 1000000, 1);
            double sube = Math.Round((tx2 - tx1) * 8.0 / 2 / 1000000, 1);
            List<Placa> placas = Placas();
            int pr = PingRouterProm(Router(placas));
            long pool = PoolMB();
            bool mucho = baja >= 20 || sube >= 3;
            List<string> sosp = new List<string>();
            foreach (Process p in Process.GetProcesses())
            {
                try { if (Sospechosos.IsMatch(p.ProcessName) && !sosp.Contains(p.ProcessName)) sosp.Add(p.ProcessName); } catch { }
                p.Dispose();
            }
            sosp.Sort(StringComparer.OrdinalIgnoreCase);
            CultureInfo ci = CultureInfo.InvariantCulture;
            string sBaja = Idioma == "en" ? baja.ToString(ci) : baja.ToString(), sSube = Idioma == "en" ? sube.ToString(ci) : sube.ToString();

            L.Add(T("QUÉ PASA:", "WHAT IS GOING ON:"));
            if (pool0 > 0 && pool > 0 && pool > pool0 * 2 && pool - pool0 > 300)
            {
                L.Add(T("  La memoria del kernel creció mucho (" + pool0 + " MB al abrir, ahora " + pool + " MB): algún driver pierde memoria.", "  Kernel memory grew a lot (" + pool0 + " MB at start, now " + pool + " MB): some driver is leaking memory."));
                L.Add(T("  Por eso se arregla al reiniciar. Casi siempre es el driver de red: hay que actualizarlo desde la página de la placa madre.", "  That is why a restart fixes it. It is almost always the network driver: update it from your motherboard page."));
            }
            if (pr >= 0 && baseR >= 0 && pr > baseR + 30)
            {
                L.Add(T("  El ping al router también subió (" + pr + " ms, normal " + baseR + " ms): el problema está entre la PC y el router.", "  Ping to the router went up too (" + pr + " ms, normal " + baseR + " ms): the problem is between the PC and the router."));
                if (mucho) L.Add(T("  La PC está usando mucho internet en este momento (ver abajo): eso llena la conexión y sube el ping.", "  The PC is using a lot of internet right now (see below): that fills the connection and raises ping."));
                else L.Add(T("  Si es WiFi: señal o interferencia. Si es cable: driver o placa de red.", "  On WiFi: signal or interference. On cable: driver or network card."));
            }
            else if (mucho)
            {
                L.Add(T("  La PC está bajando o subiendo mucho: eso llena la conexión y sube el ping (se llama bufferbloat).", "  The PC is downloading or uploading a lot: that fills the connection and raises ping (it is called bufferbloat)."));
                L.Add(T("  Cerrá o pausá lo que esté descargando o transmitiendo.", "  Close or pause whatever is downloading or streaming."));
            }
            else
            {
                L.Add(T("  El router responde bien y la PC no está usando mucho internet.", "  The router answers fine and the PC is not using much internet."));
                L.Add(T("  Si al reiniciar la PC se arregla, igual es algo de la PC (driver o una opción de red).", "  If restarting the PC fixes it, it is still something on the PC (driver or a network setting)."));
            }
            L.Add("");
            L.Add(T("PROBÁ ANTES DE REINICIAR:", "TRY BEFORE RESTARTING:"));
            L.Add(T("  Configuración > Red e Internet > Configuración de red avanzada > Deshabilitar la placa y volver a habilitarla.", "  Settings > Network & internet > Advanced network settings > Disable the adapter and enable it again."));
            L.Add(T("  Si con eso el ping vuelve a la normalidad, el culpable es el driver o una opción de la placa de red.", "  If ping goes back to normal after that, the culprit is the driver or a network card setting."));
            L.Add("");
            L.Add(T("DATOS:", "DATA:"));
            L.Add(T("  Internet usado ahora: baja " + sBaja + " Mbps, sube " + sSube + " Mbps", "  Internet in use now: down " + sBaja + " Mbps, up " + sSube + " Mbps"));
            if (pr >= 0) L.Add(T("  Ping al router: " + pr + " ms (normal " + baseR + " ms)", "  Ping to router: " + pr + " ms (normal " + baseR + " ms)"));
            else L.Add(T("  Ping al router: no responde", "  Ping to router: no answer"));
            if (pool > 0) L.Add(T("  Memoria del kernel (no paginada): " + pool + " MB (al abrir el monitor: " + pool0 + " MB)", "  Kernel memory (non-paged): " + pool + " MB (when the monitor started: " + pool0 + " MB)"));
            double hs = Math.Round(GetTickCount64() / 3600000.0, 1);
            L.Add(T("  Encendida hace: " + hs + " horas", "  Up for: " + hs.ToString(ci) + " hours"));
            foreach (Placa x in placas) if (x.Arriba && x.Vel > 0) L.Add(T("  Velocidad de ", "  Link speed of ") + x.Tipo + " " + x.Nombre + ": " + VelTexto(x.Vel));
            if (sosp.Count > 0) L.Add(T("  Programas abiertos que suelen usar mucho internet: ", "  Open programs that often use a lot of internet: ") + string.Join(", ", sosp.ToArray()));
            L.Add("");
            L.Add(T("OPCIONES DE RED DEL XUCA OPTIMIZER EN ESTA PC:", "XUCA OPTIMIZER NETWORK OPTIONS ON THIS PC:"));
            foreach (string o in OpcionesScript()) L.Add("  " + o);

            foreach (string x in L) Log(x);
            Log("");
            Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), 1);
        }

        void Reporte()
        {
            double dur = Math.Round((fin - ini).TotalSeconds, 1);
            if (dur < 1) dur = 1;
            // Windows tarda un poco en anotar los eventos
            if (Dormir(2000)) return;

            List<Cambio> cambios = new List<Cambio>();
            List<Cambio> caidas = new List<Cambio>();
            foreach (Cambio c in hist)
            {
                if (c.T >= ini.AddSeconds(-5) && c.T <= fin.AddSeconds(2)) { cambios.Add(c); if (!c.Arriba) caidas.Add(c); }
            }
            string sDur = Idioma == "en" ? dur.ToString(CultureInfo.InvariantCulture) : dur.ToString();

            List<string> L = new List<string>();
            L.Add(T("CORTE: ", "DROP: ") + Fecha(ini) + T("   (duró unos " + sDur + " s)", "   (lasted about " + sDur + " s)"));
            L.Add("");
            L.Add(T("QUÉ PASÓ:", "WHAT HAPPENED:"));
            if (caidas.Count > 0)
            {
                Cambio n = caidas[0];
                if (n.Tipo == "WiFi")
                {
                    L.Add(T("  El WiFi se desconectó (" + n.Nombre + ").", "  The WiFi disconnected (" + n.Nombre + ")."));
                    L.Add(T("  Suele ser señal débil, el router o el driver del WiFi.", "  It is usually weak signal, the router or the WiFi driver."));
                    L.Add(T("  Si aplicaste \"WiFi y cable a la vez\", probá restablecerlo.", "  If you applied \"WiFi and cable at the same time\", try resetting it."));
                }
                else
                {
                    L.Add(T("  La placa de red del cable perdió la conexión (" + n.Nombre + ").", "  The wired network card lost its link (" + n.Nombre + ")."));
                    L.Add(T("  Suele ser el driver, el ahorro de energía de la placa o el cable.", "  It is usually the driver, the card power saving or the cable."));
                    L.Add(T("  Actualizá el driver de red desde la página de tu placa madre (Buscar drivers del Xuca OPtimizer).", "  Update the network driver from your motherboard page (Find drivers in Xuca OPtimizer)."));
                    L.Add(T("  En placas de 2.5 Gbps (como las Realtek 2.5GbE) probá \"Limitar el cable a 1 Gbps\" y \"Desactivar el ahorro de energía de la red\".", "  On 2.5 Gbps cards (like Realtek 2.5GbE) try \"Limit the cable to 1 Gbps\" and \"Disable network power saving\"."));
                }
            }
            else if (gwProbado && gwFallo)
            {
                L.Add(T("  La placa siguió conectada, pero el router no respondió.", "  The card stayed connected, but the router did not answer."));
                L.Add(T("  El problema está entre la PC y el router: reiniciá el router y probá otro cable o puerto.", "  The problem is between the PC and the router: restart the router and try another cable or port."));
            }
            else if (gwProbado)
            {
                L.Add(T("  La PC y el router andaban bien: el corte vino del módem o del proveedor de internet.", "  The PC and the router were fine: the drop came from the modem or the internet provider."));
                L.Add(T("  No lo causa la PC ni el script. Si pasa seguido, hay que llamar al proveedor.", "  It is not caused by the PC or the script. If it happens often, call your provider."));
            }
            else L.Add(T("  La placa siguió conectada, pero no se pudo saber si el router respondía.", "  The card stayed connected, but it could not tell if the router was answering."));
            L.Add("");
            L.Add(T("CAMBIOS EN LAS PLACAS DE RED:", "NETWORK CARD CHANGES:"));
            if (cambios.Count > 0)
            {
                foreach (Cambio c in cambios)
                {
                    string e = T("se conectó", "connected");
                    if (c.Arriba && c.Vel > 0) e += T(" a ", " at ") + VelTexto(c.Vel);
                    if (!c.Arriba) e = T("SE DESCONECTÓ", "DISCONNECTED");
                    L.Add("  " + c.T.ToString("HH:mm:ss") + "  " + c.Tipo + " " + c.Nombre + " " + e);
                }
            }
            else L.Add(T("  Ninguno (las placas siguieron conectadas).", "  None (the cards stayed connected)."));
            L.Add("");
            L.Add(T("OPCIONES DE RED DEL XUCA OPTIMIZER EN ESTA PC:", "XUCA OPTIMIZER NETWORK OPTIONS ON THIS PC:"));
            foreach (string o in OpcionesScript()) L.Add("  " + o);
            L.Add("");
            L.Add(T("EVENTOS DE WINDOWS (desde 1 minuto antes):", "WINDOWS EVENTS (from 1 minute before):"));
            List<EventRecord> ev = new List<EventRecord>();
            ev.AddRange(Eventos.Leer("System", ini.AddSeconds(-60), 30, false));
            ev.AddRange(Eventos.Leer("Microsoft-Windows-WLAN-AutoConfig/Operational", ini.AddSeconds(-60), 30, false));
            ev.Sort(delegate (EventRecord a, EventRecord b) { return Nullable.Compare(a.TimeCreated, b.TimeCreated); });
            int desde = Math.Max(0, ev.Count - 15);
            for (int i = 0; i < ev.Count; i++)
            {
                EventRecord e = ev[i];
                if (i >= desde)
                {
                    string[] lineas = Eventos.Lineas(e);
                    L.Add("  " + Eventos.Hora(e) + "  " + e.ProviderName + " (" + e.Id + "): " + Eventos.PrimeraLinea(e, 130));
                    foreach (string m in lineas) if (Regex.IsMatch(m, "Reason|Motivo")) { L.Add("            " + m); break; }
                }
                e.Dispose();
            }
            if (ev.Count == 0) L.Add(T("  Ninguno.", "  None."));

            foreach (string x in L) Log(x);
            Log("");
            Agregar(string.Join("\r\n", L.ToArray()).TrimEnd(), 1);
        }

        // ---------- Opciones de red del Xuca OPtimizer puestas en esta PC (leidas del registro) ----------
        const string ClaseRed = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        static readonly string[] ClavesEee = { "*EEE", "EEE", "AdvancedEEE", "GreenEthernet", "EnableGreenEthernet", "PowerSavingMode" };
        static readonly Regex Rapida = new Regex(@"(?<![\d.])(2\.5|5|10)\s*Gbps", RegexOptions.IgnoreCase);
        static readonly Regex UnGiga = new Regex(@"(?<![\d.])(1(\.0)?\s*Gbps|1000\s*Mbps)", RegexOptions.IgnoreCase);

        static int Num(object v)
        {
            if (v is int) return (int)v;
            int n;
            if (v != null && int.TryParse(v.ToString(), out n)) return n;
            return -1;
        }

        List<string> OpcionesScript()
        {
            List<string> o = new List<string>();
            string ap = T("APLICADO", "APPLIED");
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy"))
                {
                    if (k != null && Num(k.GetValue("fMinimizeConnections")) == 0) o.Add(T("\"WiFi y cable a la vez\": ", "\"WiFi and cable at the same time\": ") + ap);
                }
            }
            catch { }

            // Placas fisicas que no estan deshabilitadas (las deshabilitadas no aparecen en la lista)
            Dictionary<string, string> nombres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Placa p in Placas()) nombres[p.Id] = p.Nombre;
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(ClaseRed))
                {
                    if (cls != null)
                    {
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            RegistryKey k = null;
                            try { k = cls.OpenSubKey(sub); } catch { }
                            if (k == null) continue;
                            using (k)
                            {
                                string id = k.GetValue("NetCfgInstanceId") as string;
                                string nom;
                                if (id == null || !nombres.TryGetValue(id, out nom)) continue;
                                List<string> apag = new List<string>();
                                foreach (string c in ClavesEee)
                                {
                                    object v = k.GetValue(c);
                                    if (v != null && v.ToString() == "0") apag.Add(c);
                                }
                                if (apag.Count > 0) o.Add(T("\"Desactivar el ahorro de energía de la red\": ", "\"Disable network power saving\": ") + ap + T(" en ", " on ") + nom + " (" + string.Join(", ", apag.ToArray()) + " = 0)");
                                // "Limitar el cable a 1 Gbps": placa de 2.5 Gbps o mas con Speed & Duplex fijo en 1 Gbps
                                object sd = k.GetValue("*SpeedDuplex");
                                if (sd != null)
                                {
                                    using (RegistryKey en = k.OpenSubKey(@"Ndi\params\*SpeedDuplex\enum"))
                                    {
                                        if (en != null)
                                        {
                                            bool rapida = false;
                                            foreach (string vn in en.GetValueNames()) if (Rapida.IsMatch(Convert.ToString(en.GetValue(vn)))) rapida = true;
                                            string actual = Convert.ToString(en.GetValue(sd.ToString()));
                                            if (rapida && actual != null && UnGiga.IsMatch(actual)) o.Add(T("\"Limitar el cable a 1 Gbps\": ", "\"Limit the cable to 1 Gbps\": ") + ap + T(" en ", " on ") + nom);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey ifs = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"))
                {
                    bool nagle = false;
                    if (ifs != null)
                    {
                        foreach (string sub in ifs.GetSubKeyNames())
                        {
                            using (RegistryKey k = ifs.OpenSubKey(sub))
                            {
                                if (k != null && Num(k.GetValue("TCPNoDelay")) == 1) { nagle = true; break; }
                            }
                        }
                    }
                    if (nagle) o.Add(T("\"Desactivar el algoritmo de Nagle\": ", "\"Disable Nagle's algorithm\": ") + ap);
                }
            }
            catch { }
            if (o.Count == 0) o.Add(T("Ninguna opción de red del script está aplicada.", "None of the script network options are applied."));
            return o;
        }
    }
}
