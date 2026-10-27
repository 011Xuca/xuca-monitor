// Lectura de eventos de Windows (la usan los dos monitores). Lee directo el registro de eventos, sin PowerShell.
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;

namespace Xuca
{
    public static class Eventos
    {
        // Devuelve los eventos de "log" desde "desde", del mas viejo al mas nuevo (como mucho "max", los mas nuevos)
        public static List<EventRecord> Leer(string log, DateTime desde, int max, bool soloErrores)
        {
            List<EventRecord> r = new List<EventRecord>();
            long ms = (long)Math.Max(1000, (DateTime.Now - desde).TotalMilliseconds);
            string q = "*[System[" + (soloErrores ? "(Level=1 or Level=2 or Level=3) and " : "") + "TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
            try
            {
                EventLogQuery eq = new EventLogQuery(log, PathType.LogName, q);
                eq.ReverseDirection = true;
                using (EventLogReader rd = new EventLogReader(eq))
                {
                    EventRecord e;
                    while (r.Count < max && (e = rd.ReadEvent()) != null) r.Add(e);
                }
            }
            catch { }
            r.Reverse();
            return r;
        }

        public static string Hora(EventRecord e)
        {
            return e.TimeCreated.HasValue ? e.TimeCreated.Value.ToString("HH:mm:ss") : "--:--:--";
        }

        public static string[] Lineas(EventRecord e)
        {
            string m = null;
            try { m = e.FormatDescription(); } catch { }
            if (m == null) return new string[0];
            List<string> l = new List<string>();
            foreach (string x in m.Split('\n')) if (x.Trim() != "") l.Add(x.Trim());
            return l.ToArray();
        }

        public static string PrimeraLinea(EventRecord e, int max)
        {
            string[] l = Lineas(e);
            string m = l.Length > 0 ? l[0] : Modulo.T("(sin texto)", "(no text)");
            if (m.Length > max) m = m.Substring(0, max) + "...";
            return m;
        }
    }
}
