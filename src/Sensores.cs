// Lo UNICO de la app que usa LibreHardwareMonitorLib (sensores reales de CPU, GPU, RAM, placa madre y discos).
// Esta separado a proposito: el resto del codigo solo ve las clases simples de Pc.cs (Lectura, Disco, Dato...),
// asi si la libreria no se puede cargar, falla solo esto y la app sigue andando sin la pestaña PC.
// Los nombres de los sensores son los de LibreHardwareMonitor 0.9.6 (revisados en su codigo fuente).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace Xuca
{
    internal sealed class Sensores
    {
        Computer pc;
        int vuelta;

        public void Abrir()
        {
            pc = new Computer();
            pc.IsCpuEnabled = true;
            pc.IsGpuEnabled = true;
            pc.IsMemoryEnabled = true;
            pc.IsMotherboardEnabled = true;
            pc.IsStorageEnabled = true;
            pc.Open();
            SinHistorial();
        }

        public void Cerrar()
        {
            try { if (pc != null) pc.Close(); } catch { }
            pc = null;
        }

        // LibreHardwareMonitor guarda por defecto 1 dia de valores de cada sensor (para sus graficos). En una app
        // que queda prendida todo el dia eso crece sin parar (decenas de MB) y cada lectura se hace mas lenta.
        // Se apaga: el minimo y el maximo de la sesion los sigue llevando igual.
        void SinHistorial()
        {
            foreach (IHardware h in pc.Hardware) SinHistorial(h);
        }

        static void SinHistorial(IHardware h)
        {
            foreach (ISensor s in h.Sensors)
                if (s.ValuesTimeWindow != TimeSpan.Zero) s.ValuesTimeWindow = TimeSpan.Zero;
            foreach (IHardware sh in h.SubHardware) SinHistorial(sh);
        }

        // ---------- Datos que no cambian (BIOS y placa madre, de la tabla SMBIOS) ----------
        public void LeerFija(InfoFija f)
        {
            try
            {
                SMBios sm = pc.SMBios;
                if (sm == null) return;
                if (sm.Bios != null)
                {
                    f.BiosMarca = Limpio(sm.Bios.Vendor);
                    f.BiosVersion = Limpio(sm.Bios.Version);
                    if (sm.Bios.Date.HasValue) f.BiosFecha = sm.Bios.Date.Value;
                }
                if (sm.Board != null)
                {
                    f.PlacaFab = Limpio(sm.Board.ManufacturerName);
                    f.PlacaModelo = Limpio(sm.Board.ProductName);
                }
            }
            catch { }
        }

        static string Limpio(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            // Lo que ponen los fabricantes cuando no completaron el dato
            if (Regex.IsMatch(s, "^(To Be Filled|Default string|System Product Name|Not Applicable|None|N/A|0+)$", RegexOptions.IgnoreCase)) return "";
            return s;
        }

        // ---------- Lectura en vivo ----------
        public void Leer(Lectura L)
        {
            vuelta++;
            // El SMART de los discos cambia lento y leerlo cuesta mas: cada ~10 s alcanza
            bool discos = vuelta % 5 == 1;
            IList<IHardware> todos = pc.Hardware;
            foreach (IHardware h in todos)
            {
                if (h.HardwareType == HardwareType.Storage && !discos) continue;
                try { h.Update(); } catch { }
                foreach (IHardware sh in h.SubHardware) { try { sh.Update(); } catch { } }
            }
            // Algunos sensores aparecen recien despues de la primera lectura
            if (vuelta <= 3 || discos) SinHistorial();
            L.HaySensores = true;

            IHardware cpu = null;
            foreach (IHardware h in todos) if (h.HardwareType == HardwareType.Cpu) { cpu = h; break; }
            if (cpu != null) LeerCpu(cpu, L);

            IHardware gpu = GpuPrincipal(todos, L.GpuOtras);
            if (gpu != null) LeerGpu(gpu, L);

            foreach (IHardware h in todos)
            {
                if (h.HardwareType == HardwareType.Memory)
                {
                    // DDR5 (y algunas DDR4) tienen sensor de temperatura en cada modulo: "DIMM #1", "DIMM #2"...
                    foreach (ISensor s in h.Sensors)
                        if (s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Name != null && s.Name.StartsWith("DIMM #", StringComparison.Ordinal))
                            if (!L.DimmTemp.HasValue || s.Value.Value > L.DimmTemp.Value) L.DimmTemp = s.Value.Value;
                }
                else if (h.HardwareType == HardwareType.Motherboard)
                    LeerPlaca(h, L);
                else if (h.HardwareType == HardwareType.Storage)
                    LeerDisco(h, L);
            }
        }

        void LeerCpu(IHardware cpu, Lectura L)
        {
            L.CpuNombre = cpu.Name ?? "";
            // Temperatura: el sensor que de verdad es la del procesador, en orden de preferencia.
            // OJO: en Intel tambien hay sensores "Distance to TjMax" del tipo temperatura que NO son temperaturas
            // (es cuanto falta para el limite): por eso no sirve tomar "el maximo de todos".
            ISensor t = Exacto(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "CCDs Max (Tdie)", "Core Max", "Core (Tctl)", "CPU Core Max");
            if (t == null) t = MaxSensor(cpu, SensorType.Temperature, "Distance", "TjMax", "Resolution", "Limit");
            if (t != null && t.Value.HasValue && t.Value.Value > 0 && t.Value.Value < 150)
            {
                L.CpuTemp = t.Value;
                L.CpuTempFuente = t.Name ?? "";
                if (t.Max.HasValue) L.CpuTempMaxLib = t.Max;
                // Intel dice su TjMax (temperatura a la que se frena solo) en los parametros del sensor
                try
                {
                    foreach (IParameter p in t.Parameters)
                        if (p.Name != null && p.Name.StartsWith("TjMax", StringComparison.Ordinal) && p.Value > 60 && p.Value < 130) L.CpuTjMax = (int)Math.Round(p.Value);
                }
                catch { }
            }
            L.CpuLoad = V(Exacto(cpu, SensorType.Load, "CPU Total"));
            L.CpuPower = V(Exacto(cpu, SensorType.Power, "CPU Package", "Package"));
            L.CpuVolt = V(Exacto(cpu, SensorType.Voltage, "CPU Core", "Core (SVI2 TFN)"));

            // Velocidad: promedio y maximo de los nucleos (sin "Bus Speed" ni los promedios que ya calcula la libreria)
            float suma = 0, max = 0; int n = 0;
            foreach (ISensor s in cpu.Sensors)
            {
                if (s.SensorType != SensorType.Clock || !s.Value.HasValue || s.Name == null) continue;
                if (s.Name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0 || s.Name.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (s.Value.Value < 100) continue;
                suma += s.Value.Value; n++;
                if (s.Value.Value > max) max = s.Value.Value;
            }
            if (n > 0) { L.CpuClockProm = suma / n; L.CpuClockMax = max; }
            // En AMD la "efectiva" descuenta el tiempo dormido: es la mas real
            ISensor ef = Exacto(cpu, SensorType.Clock, "Cores (Average Effective)");
            if (ef != null && ef.Value.HasValue && ef.Value.Value > 100) L.CpuClockProm = ef.Value;

            foreach (ISensor s in cpu.Sensors)
                if (s.SensorType == SensorType.Load && s.Value.HasValue && s.Name != null && s.Name.StartsWith("CPU Core #", StringComparison.Ordinal) && s.Name.IndexOf("Thread", StringComparison.OrdinalIgnoreCase) < 0)
                    L.CpuNucleos.Add(s.Value.Value);
            if (L.CpuNucleos.Count == 0)
                foreach (ISensor s in cpu.Sensors)
                    if (s.SensorType == SensorType.Load && s.Value.HasValue && s.Name != null && s.Name.StartsWith("CPU Core #", StringComparison.Ordinal))
                        L.CpuNucleos.Add(s.Value.Value);

            Todos(cpu, L.CpuDatos, false);
        }

        // La placa de video principal: la dedicada (NVIDIA, o la de mas memoria propia), no la integrada del procesador
        static IHardware GpuPrincipal(IList<IHardware> todos, List<string> otras)
        {
            IHardware mejor = null;
            float mejorPuntos = -1;
            foreach (IHardware h in todos)
            {
                if (h.HardwareType != HardwareType.GpuNvidia && h.HardwareType != HardwareType.GpuAmd && h.HardwareType != HardwareType.GpuIntel) continue;
                float vram = V(Exacto(h, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")) ?? 0;
                float puntos = vram + (h.HardwareType == HardwareType.GpuNvidia ? 1000000 : 0);
                if (puntos > mejorPuntos)
                {
                    if (mejor != null) otras.Add(mejor.Name);
                    mejor = h; mejorPuntos = puntos;
                }
                else otras.Add(h.Name);
            }
            return mejor;
        }

        void LeerGpu(IHardware gpu, Lectura L)
        {
            L.GpuNombre = gpu.Name ?? "";
            L.GpuNvidia = gpu.HardwareType == HardwareType.GpuNvidia;
            L.GpuAmd = gpu.HardwareType == HardwareType.GpuAmd;
            L.GpuTemp = V(Exacto(gpu, SensorType.Temperature, "GPU Core"));
            L.GpuHot = V(Exacto(gpu, SensorType.Temperature, "GPU Hot Spot"));
            L.GpuMemTemp = V(Exacto(gpu, SensorType.Temperature, "GPU Memory Junction", "GPU Memory"));
            if (!L.GpuTemp.HasValue) L.GpuTemp = V(MaxSensor(gpu, SensorType.Temperature, "Hot Spot", "Memory", "VR ", "PLX", "Liquid"));
            L.GpuLoad = V(Exacto(gpu, SensorType.Load, "GPU Core", "D3D 3D"));
            L.GpuClock = V(Exacto(gpu, SensorType.Clock, "GPU Core"));
            L.GpuMemClock = V(Exacto(gpu, SensorType.Clock, "GPU Memory"));
            L.GpuPower = V(Exacto(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power", "GPU Core"));
            L.GpuVolt = V(Exacto(gpu, SensorType.Voltage, "GPU Core Voltage", "GPU Core"));
            L.GpuVramUsada = V(Exacto(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"));
            L.GpuVramTotal = V(Exacto(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total"));
            if (L.GpuVramTotal.HasValue && L.GpuVramTotal.Value < 1) L.GpuVramTotal = null;

            foreach (ISensor s in gpu.Sensors)
            {
                if (!s.Value.HasValue || s.Name == null) continue;
                if (s.SensorType == SensorType.Fan && !L.GpuFanRpm.HasValue) L.GpuFanRpm = s.Value;
                else if (s.SensorType == SensorType.Control && !L.GpuFanPct.HasValue && s.Name.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0) L.GpuFanPct = s.Value;
                // Conector 12VHPWR / 12V-2x6 (RTX 40/50 con sensores por pin): la corriente de cada pin
                else if (s.SensorType == SensorType.Current && s.Name.StartsWith("12VHPWR Pin", StringComparison.Ordinal))
                {
                    if (!L.GpuPinMaxA.HasValue || s.Value.Value > L.GpuPinMaxA.Value) { L.GpuPinMaxA = s.Value; L.GpuPinNombre = s.Name; }
                }
            }
            Todos(gpu, L.GpuDatos, false);
        }

        void LeerPlaca(IHardware placa, Lectura L)
        {
            // Los ventiladores y temperaturas de la placa estan en el chip Super I/O (hace falta PawnIO para leerlo)
            foreach (IHardware sio in placa.SubHardware)
            {
                foreach (ISensor s in sio.Sensors)
                {
                    if (!s.Value.HasValue || s.Name == null) continue;
                    if (s.SensorType == SensorType.Fan)
                    {
                        Ventilador v = new Ventilador();
                        v.Nombre = s.Name; v.Rpm = s.Value.Value;
                        v.MaxRpm = s.Max.HasValue ? s.Max.Value : v.Rpm;
                        L.Fans.Add(v);
                    }
                }
                Todos(sio, L.PlacaDatos, true);
            }
        }

        void LeerDisco(IHardware h, Lectura L)
        {
            Disco d = new Disco();
            d.Nombre = h.Name ?? "";
            d.Temp = V(Exacto(h, SensorType.Temperature, "Temperature", "Composite Temperature"));
            d.TempAviso = V(Exacto(h, SensorType.Temperature, "Warning Temperature"));
            d.TempCritica = V(Exacto(h, SensorType.Temperature, "Critical Temperature"));
            d.Vida = V(Exacto(h, SensorType.Level, "Life"));
            d.Lleno = V(Exacto(h, SensorType.Load, "Used Space"));
            d.EscritoGB = V(Exacto(h, SensorType.Data, "Data Written"));
            d.Horas = V(Exacto(h, SensorType.Factor, "Power On Hours"));
            d.Actividad = V(Exacto(h, SensorType.Load, "Total Activity"));
            ISensor t = Exacto(h, SensorType.Temperature, "Temperature", "Composite Temperature");
            if (t != null && t.Max.HasValue) d.TempMax = t.Max;
            // Valores imposibles = el disco no lo informa
            if (d.Temp.HasValue && (d.Temp.Value <= 0 || d.Temp.Value > 120)) d.Temp = null;
            if (d.TempAviso.HasValue && (d.TempAviso.Value < 40 || d.TempAviso.Value > 110)) d.TempAviso = null;
            if (d.TempCritica.HasValue && (d.TempCritica.Value < 40 || d.TempCritica.Value > 120)) d.TempCritica = null;
            if (d.Vida.HasValue && (d.Vida.Value < 0 || d.Vida.Value > 100)) d.Vida = null;
            L.Discos.Add(d);
        }

        // ---------- Todos los sensores (para la lista "Ver todos") ----------
        static readonly SensorType[] OrdenTipos = { SensorType.Temperature, SensorType.Load, SensorType.Clock, SensorType.Power, SensorType.Voltage, SensorType.Current, SensorType.Fan, SensorType.Control, SensorType.SmallData };

        static void Todos(IHardware h, List<Dato> lista, bool soloTempsYFans)
        {
            foreach (SensorType tipo in OrdenTipos)
            {
                if (soloTempsYFans && tipo != SensorType.Temperature && tipo != SensorType.Fan && tipo != SensorType.Voltage) continue;
                foreach (ISensor s in h.Sensors)
                {
                    if (s.SensorType != tipo || !s.Value.HasValue || s.Name == null) continue;
                    if (s.Name.IndexOf("Distance to TjMax", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (tipo == SensorType.Temperature && (s.Value.Value <= 0 || s.Value.Value > 150)) continue;
                    if (tipo == SensorType.Voltage && soloTempsYFans && s.Value.Value <= 0) continue;
                    Dato d = new Dato();
                    d.Grupo = Grupo(tipo);
                    d.Nombre = s.Name;
                    d.Valor = Formato(tipo, s.Value.Value);
                    d.Pico = s.Max.HasValue && tipo != SensorType.Voltage && tipo != SensorType.SmallData ? Formato(tipo, s.Max.Value) : "";
                    lista.Add(d);
                }
            }
        }

        static string Grupo(SensorType t)
        {
            switch (t)
            {
                case SensorType.Temperature: return Modulo.T("Temperaturas", "Temperatures");
                case SensorType.Load: return Modulo.T("Uso", "Load");
                case SensorType.Clock: return Modulo.T("Velocidades", "Clocks");
                case SensorType.Power: return Modulo.T("Potencia", "Power");
                case SensorType.Voltage: return Modulo.T("Voltajes", "Voltages");
                case SensorType.Current: return Modulo.T("Corriente", "Current");
                case SensorType.Fan: return Modulo.T("Ventiladores", "Fans");
                case SensorType.Control: return Modulo.T("Control de ventiladores", "Fan control");
                case SensorType.SmallData: return Modulo.T("Memoria", "Memory");
            }
            return "";
        }

        static string Formato(SensorType t, float v)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            switch (t)
            {
                case SensorType.Temperature: return v.ToString("0", ci) + " °C";
                case SensorType.Load: return v.ToString("0", ci) + " %";
                case SensorType.Control: return v.ToString("0", ci) + " %";
                case SensorType.Clock: return v.ToString("0", ci) + " MHz";
                case SensorType.Power: return v.ToString(v < 10 ? "0.0" : "0", ci) + " W";
                case SensorType.Voltage: return v.ToString("0.000", ci) + " V";
                case SensorType.Current: return v.ToString("0.0", ci) + " A";
                case SensorType.Fan: return v.ToString("0", ci) + " RPM";
                case SensorType.SmallData: return v.ToString("0", ci) + " MB";
            }
            return v.ToString("0.##", ci);
        }

        // ---------- Ayudas ----------
        static float? V(ISensor s) { return s == null ? (float?)null : s.Value; }

        // El primer sensor de ese tipo cuyo nombre sea exactamente uno de estos (en orden de preferencia) y tenga valor
        static ISensor Exacto(IHardware h, SensorType t, params string[] nombres)
        {
            foreach (string n in nombres)
                foreach (ISensor s in h.Sensors)
                    if (s.SensorType == t && s.Value.HasValue && string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        // El de valor mas alto de ese tipo, sin los que tengan alguna de estas palabras en el nombre
        static ISensor MaxSensor(IHardware h, SensorType t, params string[] sin)
        {
            ISensor m = null;
            foreach (ISensor s in h.Sensors)
            {
                if (s.SensorType != t || !s.Value.HasValue || s.Name == null) continue;
                bool fuera = false;
                foreach (string x in sin) if (s.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) { fuera = true; break; }
                if (fuera) continue;
                if (m == null || s.Value.Value > m.Value.Value) m = s;
            }
            return m;
        }
    }
}
