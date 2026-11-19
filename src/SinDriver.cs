// Datos REALES que Windows da sin ningun driver propio (ni PawnIO ni LibreHardwareMonitor): sirven solos y completan
// lo que la libreria no puede leer sin PawnIO. Nada de esto instala ni carga drivers.
//  - CpuWindows: contadores "Processor Information" (frecuencia real = base x % de rendimiento, uso, % de limite de
//    rendimiento que impone el firmware), potencia del paquete y de cada nucleo ("Energy Meter", RAPL) y la zona termica
//    ACPI (solo si es creible; ver ZonaTermica).
//  - SmartAta: estado SMART de los SATA/HDD por WMI (necesita administrador; sin el, "—").
//  - DiscoDirecto: temperatura, vida util, escrito y horas de los NVMe, pidiendoselos al propio disco con
//    IOCTL_STORAGE_QUERY_PROPERTY (registro SMART/Health del NVMe y la "temperatura del dispositivo" de Windows).
//    Se abre el disco SIN permisos de lectura/escritura: anda sin administrador. Los SATA (SMART por ATA) necesitan
//    administrador y pasar comandos ATA: de esos se encarga la libreria (Sensores.cs).
//  - BateriaWindows: consumo de la bateria en vatios (notebooks), de CallNtPowerInformation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Xuca
{
    internal sealed class CpuWindows
    {
        IntPtr q, cPerf, cUso, cLim, cTz, cEnergia;
        float tzMin = float.NaN, tzMax = float.NaN;
        int baseReg;

        public void Abrir()
        {
            try
            {
                if (MonitorPC.PdhOpenQuery(null, IntPtr.Zero, out q) != 0) { q = IntPtr.Zero; return; }
                cPerf = C("\\Processor Information(_Total)\\% Processor Performance");
                cUso = C("\\Processor Information(_Total)\\% Processor Utility");
                cLim = C("\\Processor Information(_Total)\\% Performance Limit");
                cTz = C("\\Thermal Zone Information(*)\\High Precision Temperature");
                cEnergia = C("\\Energy Meter(*)\\Power");
                MonitorPC.PdhCollectQueryData(q);
                // Velocidad base para quien no la tiene por WMI (el overlay): la que anota Windows al arrancar
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) { object v = k.GetValue("~MHz"); if (v is int) baseReg = (int)v; }
            }
            catch { q = IntPtr.Zero; }
        }

        IntPtr C(string ruta) { IntPtr c; return MonitorPC.PdhAddEnglishCounter(q, ruta, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero; }

        public void Cerrar()
        {
            try { if (q != IntPtr.Zero) MonitorPC.PdhCloseQuery(q); } catch { }
            q = IntPtr.Zero;
        }

        // Completa solo lo que la libreria no dio (sin PawnIO: MHz y temperatura). baseMHz = 0: la del registro.
        public void Leer(Lectura L, int baseMHz)
        {
            if (q == IntPtr.Zero) return;
            try
            {
                MonitorPC.PdhCollectQueryData(q);
                float? perf = MonitorPC.Pdh(cPerf), lim = MonitorPC.Pdh(cLim);
                if (perf.HasValue && perf.Value > 0 && perf.Value < 400) L.CpuPerfPct = perf;
                if (lim.HasValue && lim.Value > 0 && lim.Value <= 100) L.CpuLimitePct = lim;
                int b = baseMHz > 0 ? baseMHz : baseReg;
                // Lo mismo que muestra el Administrador de tareas: velocidad base x % de rendimiento
                if (!L.CpuClockProm.HasValue && L.CpuPerfPct.HasValue && b > 0) { L.CpuClockProm = b * L.CpuPerfPct.Value / 100f; L.CpuClockWindows = true; }
                if (!L.CpuLoad.HasValue) { float? u = MonitorPC.Pdh(cUso); if (u.HasValue) L.CpuLoad = Math.Max(0f, Math.Min(100f, u.Value)); }
                if (!L.CpuTemp.HasValue) { float? tz = ZonaTermica(); if (tz.HasValue) { L.CpuTemp = tz; L.CpuTempFuente = "ACPI"; } }
                if (!L.CpuPower.HasValue) Energia(L);
            }
            catch { }
        }

        // Potencia del CPU sin driver: el "Energy Meter" de Windows (EMI) expone los contadores RAPL del procesador.
        // "Power" viene en mW: comprobado en un Ryzen 7 9800X3D con Energy (pWh) / Time (ms) -> 41.4 W contra 41 308 mW.
        // Instancias: RAPL_Package0_PKG (paquete) y RAPL_Package0_CoreN_CORE (cada nucleo); _Total da 0. Sin contador: nada.
        void Energia(Lectura L)
        {
            float pkg = 0;
            List<KeyValuePair<int, float>> nucleos = new List<KeyValuePair<int, float>>();
            foreach (KeyValuePair<string, double> kv in MonitorPC.PdhVarios(cEnergia))
            {
                float w = (float)(kv.Value / 1000.0);
                if (kv.Key.EndsWith("_PKG", StringComparison.OrdinalIgnoreCase)) pkg += w;
                Match m = Regex.Match(kv.Key, @"Package0_Core(\d+)_CORE$", RegexOptions.IgnoreCase);
                if (m.Success && w >= 0 && w < 100) nucleos.Add(new KeyValuePair<int, float>(int.Parse(m.Groups[1].Value), w));
            }
            if (pkg <= 0 || pkg >= 400) return;   // no plausible: mejor nada
            L.CpuPower = pkg; L.CpuPowerWindows = true;
            // Para "Ver todos" (si la libreria ya dio los vatios por nucleo, con PawnIO, no se llega aca)
            string grupo = Modulo.T("Potencia", "Power");
            L.CpuDatos.Add(Dato(grupo, Modulo.T("Paquete (Windows)", "Package (Windows)"), pkg));
            nucleos.Sort(delegate (KeyValuePair<int, float> a, KeyValuePair<int, float> b) { return a.Key.CompareTo(b.Key); });
            foreach (KeyValuePair<int, float> n in nucleos) L.CpuDatos.Add(Dato(grupo, Modulo.T("Núcleo #", "Core #") + (n.Key + 1) + " (Windows)", n.Value));
        }

        static Dato Dato(string grupo, string nombre, float w)
        {
            Dato d = new Dato(); d.Grupo = grupo; d.Nombre = nombre; d.Pico = "";
            d.Valor = w.ToString(w < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " W";
            return d;
        }

        // Temperatura del procesador SIN driver: la zona termica ACPI que Windows expone (decimas de Kelvin). En notebooks suele
        // seguir al procesador; en muchas PCs de escritorio es un valor fijo o de otra parte de la placa (en una B650 da 17 °C).
        // Por eso se usa solo si es creible (25 a 110 °C) y despues de verla moverse al menos 2 °C; si no, mejor no mostrar nada.
        // (LibreHardwareMonitor 0.9.6 no lee la temperatura del CPU sin PawnIO: en AMD y en Intel hace falta acceso a MSR/SMN.)
        float? ZonaTermica()
        {
            float mejor = float.NaN;
            foreach (double v in MonitorPC.PdhVarios(cTz).Values)
            {
                float c = (float)(v / 10.0 - 273.15);
                if (c >= 25 && c <= 110 && (float.IsNaN(mejor) || c > mejor)) mejor = c;
            }
            if (float.IsNaN(mejor)) return null;
            if (float.IsNaN(tzMin) || mejor < tzMin) tzMin = mejor;
            if (float.IsNaN(tzMax) || mejor > tzMax) tzMax = mejor;
            return tzMax - tzMin >= 2 ? mejor : (float?)null;
        }
    }

    internal static class DiscoDirecto
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string n, uint acceso, uint compartir, IntPtr sa, uint disp, uint fl, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inb, int ins, byte[] outb, int outs, out int ret, IntPtr ov);
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
        const int StorageDeviceProtocolSpecificProperty = 50, StorageDeviceTemperatureProperty = 52;
        const int ProtocolTypeNvme = 3, NVMeDataTypeIdentify = 1, NVMeDataTypeLogPage = 2;

        // Lo que el disco numero n (\\.\PhysicalDriveN) dice de si mismo, o null si no responde (SATA sin admin, USB, RAID...)
        public static Disco Leer(int n, string nombre, bool nvme)
        {
            try
            {
                // Acceso 0 (ni lectura ni escritura): alcanza para IOCTL_STORAGE_QUERY_PROPERTY y no pide administrador
                using (SafeFileHandle h = CreateFile(@"\\.\PhysicalDrive" + n, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return null;
                    Disco d = new Disco(); d.Nombre = nombre;
                    // 1) "Temperatura del dispositivo" de Windows (STORAGE_TEMPERATURE_DATA_DESCRIPTOR): grados C y umbrales
                    byte[] o = Consulta(h, StorageDeviceTemperatureProperty, null, 512);
                    if (o != null && o.Length >= 40 && BitConverter.ToUInt16(o, 12) > 0)
                    {
                        d.TempCritica = BitConverter.ToInt16(o, 8); d.TempAviso = BitConverter.ToInt16(o, 10);
                        d.Temp = BitConverter.ToInt16(o, 24 + 2);   // TemperatureInfo[0].Temperature
                    }
                    if (nvme)
                    {
                        // 2) Registro SMART / Health del NVMe (log page 02h): temperatura en Kelvin, % usado, escrito, horas
                        byte[] s = Nvme(h, NVMeDataTypeLogPage, 2, 512);
                        if (s != null)
                        {
                            int k = BitConverter.ToUInt16(s, 1);
                            if (k > 0) d.Temp = k - 273;
                            d.Vida = Math.Max(0, 100 - s[5]);
                            d.EscritoGB = (float)(BitConverter.ToUInt64(s, 48) * 512000.0 / 1e9);   // unidades de 1000 sectores de 512 bytes
                            d.Horas = BitConverter.ToUInt64(s, 128);
                        }
                        // 3) Umbrales de fabrica del controlador (Identify: WCTEMP y CCTEMP, en Kelvin) si Windows no los dio
                        if (!d.TempAviso.HasValue || d.TempAviso.Value <= 0)
                        {
                            byte[] id = Nvme(h, NVMeDataTypeIdentify, 1, 4096);
                            if (id != null) { int w = BitConverter.ToUInt16(id, 266), c = BitConverter.ToUInt16(id, 268); if (w > 273) d.TempAviso = w - 273; if (c > 273) d.TempCritica = c - 273; }
                        }
                    }
                    // Valores imposibles = el disco no lo informa (mismo criterio que Sensores.LeerDisco)
                    if (d.Temp.HasValue && (d.Temp.Value <= 0 || d.Temp.Value > 120)) d.Temp = null;
                    if (d.TempAviso.HasValue && (d.TempAviso.Value < 40 || d.TempAviso.Value > 110)) d.TempAviso = null;
                    if (d.TempCritica.HasValue && (d.TempCritica.Value < 40 || d.TempCritica.Value > 120)) d.TempCritica = null;
                    return d.Temp.HasValue || d.Vida.HasValue ? d : null;
                }
            }
            catch { return null; }
        }

        // STORAGE_PROPERTY_QUERY (PropertyId, QueryType = estandar, AdditionalParameters) -> lo que devuelve, o null
        static byte[] Consulta(SafeFileHandle h, int propiedad, byte[] extra, int largo)
        {
            byte[] q = new byte[Math.Max(8 + (extra != null ? extra.Length : 0), 12)];
            BitConverter.GetBytes(propiedad).CopyTo(q, 0);
            if (extra != null) extra.CopyTo(q, 8);
            byte[] o = new byte[Math.Max(largo, q.Length)];
            int r;
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, q, q.Length, o, o.Length, out r, IntPtr.Zero) || r < 8) return null;
            return o;
        }

        // Pedido "protocol specific" al NVMe (STORAGE_PROTOCOL_SPECIFIC_DATA, 40 bytes + el lugar para la respuesta)
        static byte[] Nvme(SafeFileHandle h, int tipo, int valor, int largo)
        {
            byte[] p = new byte[40 + largo];
            BitConverter.GetBytes(ProtocolTypeNvme).CopyTo(p, 0);
            BitConverter.GetBytes(tipo).CopyTo(p, 4);
            BitConverter.GetBytes(valor).CopyTo(p, 8);          // log page 02h / Identify CNS 1 (controlador)
            BitConverter.GetBytes(40).CopyTo(p, 16);            // ProtocolDataOffset: la respuesta va despues de esta estructura
            BitConverter.GetBytes(largo).CopyTo(p, 20);         // ProtocolDataLength
            byte[] o = Consulta(h, StorageDeviceProtocolSpecificProperty, p, 8 + p.Length);
            if (o == null) return null;
            // STORAGE_PROTOCOL_DATA_DESCRIPTOR: Version, Size y la misma estructura (con el offset y largo reales)
            int off = 8 + BitConverter.ToInt32(o, 8 + 16), len = BitConverter.ToInt32(o, 8 + 20);
            if (len < Math.Min(largo, 512) || off < 8 || off + len > o.Length) return null;
            byte[] d = new byte[len];
            Array.Copy(o, off, d, 0, len);
            return d;
        }
    }

    // SMART de los discos SATA/ATA por WMI (root\wmi, MSStorageDriver_ATAPISmartData y _FailurePredictStatus): lo da el
    // driver de almacenamiento de Windows, sin driver propio, pero SOLO con administrador (Pulso corre asi). Sin admin
    // WMI niega el acceso y queda todo en "—". Un HDD no tiene % de desgaste: se calcula un estado con atributos reales.
    internal sealed class SmartAta
    {
        public string Nombre = "", Estado = "", Detalle = "";
        public Nivel Nivel = Nivel.Gris;
        public float? Temp, EscritoGB;

        static string T(string es, string en) { return Modulo.T(es, en); }

        // Un resultado por disco fisico (por su nombre en Windows) que responda
        public static List<SmartAta> Leer(List<DiscoFisico> fisicos)
        {
            List<SmartAta> r = new List<SmartAta>();
            try
            {
                // Numero de disco -> PNPDeviceID (la instancia WMI de SMART es ese mismo id + "_0")
                Dictionary<string, int> pnp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Index, PNPDeviceID FROM Win32_DiskDrive"))
                    foreach (ManagementObject o in s.Get()) { using (o) { object id = o["PNPDeviceID"], ix = o["Index"]; if (id != null && ix != null) pnp[id.ToString()] = Convert.ToInt32(ix); } }
                Dictionary<string, bool> falla = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus"))
                    foreach (ManagementObject o in s.Get()) { using (o) { if (o["InstanceName"] != null) falla[o["InstanceName"].ToString()] = o["PredictFailure"] is bool && (bool)o["PredictFailure"]; } }
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData"))
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string inst = o["InstanceName"] as string; byte[] v = o["VendorSpecific"] as byte[];
                            if (inst == null || v == null) continue;
                            int num = -1;
                            foreach (KeyValuePair<string, int> kv in pnp) if (inst.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) { num = kv.Value; break; }
                            DiscoFisico fi = fisicos.Find(delegate (DiscoFisico x) { return x.Numero == num; });
                            if (fi == null) continue;
                            bool predice; falla.TryGetValue(inst, out predice);
                            SmartAta a = Evaluar(v, predice, fi.Tipo.StartsWith("HDD", StringComparison.Ordinal));
                            a.Nombre = fi.Nombre;
                            r.Add(a);
                        }
                    }
            }
            catch { }   // sin administrador: "Access denied" -> sin datos
            return r;
        }

        // VendorSpecific: 2 bytes de version y 30 atributos de 12 bytes (id, flags x2, actual, peor, crudo x6, reservado)
        public static Dictionary<int, long> Atributos(byte[] v)
        {
            Dictionary<int, long> a = new Dictionary<int, long>();
            for (int p = 2; p + 12 <= v.Length; p += 12)
            {
                int id = v[p]; if (id == 0) continue;
                long crudo = 0; for (int k = 5; k >= 0; k--) crudo = (crudo << 8) | v[p + 5 + k];
                a[id] = crudo;
            }
            return a;
        }

        // Umbrales: Mal = Windows predice falla, hay sectores incorregibles (198), 100+ reasignados (5) o 10+ pendientes (197).
        // Atencion = algun reasignado, pendiente, evento de reasignacion (196), reintento de arranque (10) o error CRC (199, casi siempre el cable).
        public static SmartAta Evaluar(byte[] v, bool predice, bool hdd)
        {
            Dictionary<int, long> a = Atributos(v);
            SmartAta s = new SmartAta();
            long rea = Cuenta(a, 5), pen = Cuenta(a, 197), inc = Cuenta(a, 198), crc = Cuenta(a, 199), ev = Cuenta(a, 196), arr = Cuenta(a, 10);
            bool mal = predice || inc > 0 || rea >= 100 || pen >= 10;
            bool atencion = rea > 0 || pen > 0 || ev > 0 || arr > 0 || crc > 0;
            s.Nivel = mal ? Nivel.Rojo : atencion ? Nivel.Amarillo : Nivel.Verde;
            s.Estado = mal ? T("Mal", "Bad") : atencion ? T("Atención", "Warning") : T("Sano", "Healthy");
            if (hdd && !mal && !atencion) s.Estado += " (HDD)";
            List<string> d = new List<string>();
            if (predice) d.Add(T("Windows predice una falla", "Windows predicts a failure"));
            if (a.ContainsKey(5)) d.Add(rea + T(" reasignados", " reallocated"));
            if (a.ContainsKey(197)) d.Add(pen + T(" pendientes", " pending"));
            if (a.ContainsKey(198)) d.Add(inc + T(" incorregibles", " uncorrectable"));
            if (a.ContainsKey(199)) d.Add(crc + T(" errores CRC", " CRC errors"));
            if (ev > 0) d.Add(ev + T(" eventos de reasignación", " reallocation events"));
            if (arr > 0) d.Add(arr + T(" reintentos de arranque", " spin-up retries"));
            s.Detalle = string.Join(", ", d.ToArray());
            // Temperatura: 194 (o 190) = grados en el byte bajo del valor crudo
            long t = a.ContainsKey(194) ? a[194] & 0xFF : a.ContainsKey(190) ? a[190] & 0xFF : 0;
            if (t > 0 && t < 100) s.Temp = t;
            // Escrito: 241 en sectores de 512 bytes solo en HDD (en SSD cada fabricante usa otra unidad: no se adivina)
            if (hdd && a.ContainsKey(241) && a[241] > 0) s.EscritoGB = (float)(a[241] * 512.0 / 1e9);
            return s;
        }

        // Los contadores van en los 4 bytes bajos del valor crudo (los altos los usan algunos fabricantes para otra cosa)
        static long Cuenta(Dictionary<int, long> a, int id) { long v; return a.TryGetValue(id, out v) ? v & 0xFFFFFFFF : 0; }
    }

    internal static class BateriaWindows
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_BATTERY_STATE
        {
            public byte AcOnLine, BatteryPresent, Charging, Discharging, Spare1, Spare2, Spare3, Tag;
            public uint MaxCapacity, RemainingCapacity; public int Rate; public uint EstimatedTime, DefaultAlert1, DefaultAlert2;
        }
        [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int nivel, IntPtr inb, int ins, out SYSTEM_BATTERY_STATE s, int outs);
        const int SystemBatteryState = 5;

        // Vatios que entran (+, cargando) o salen (-, a bateria) de la bateria; null en una PC sin bateria o si no lo informa
        public static float? Vatios()
        {
            try
            {
                SYSTEM_BATTERY_STATE s;
                if (CallNtPowerInformation(SystemBatteryState, IntPtr.Zero, 0, out s, Marshal.SizeOf(typeof(SYSTEM_BATTERY_STATE))) != 0 || s.BatteryPresent == 0) return null;
                if (s.Rate == int.MinValue || s.Rate == 0 || Math.Abs(s.Rate) > 400000) return null;   // 0x80000000 = desconocido
                return s.Rate / 1000f;
            }
            catch { return null; }
        }
    }
}
