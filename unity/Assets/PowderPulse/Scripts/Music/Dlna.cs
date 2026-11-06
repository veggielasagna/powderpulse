using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace PowderPulse.Music
{
    public sealed class DlnaServer
    {
        public string Id, Name, Location, ControlUrl, ServiceType;
    }

    public sealed class DlnaEntry
    {
        public string Id, Title, Kind, Artist, Album, Genre, Date, Url, Mime;
        public int Track;
        public double Duration;
        public List<KeyValuePair<string, string>> Res = new List<KeyValuePair<string, string>>();   // every audio copy: url, mime
    }

    /// <summary>
    /// Finds DLNA/UPnP media servers on the home network (SSDP) and reads their ContentDirectory.
    /// Same protocol the Xbox's own media player uses; no login involved. Ported from the Xbox app
    /// (Services/Dlna.cs) to plain .NET sockets + UnityWebRequest so it runs in the editor and on Xbox.
    /// Call everything from the main thread.
    /// </summary>
    public static class Dlna
    {
        const string UserAgent = "PowderPulse/1.0 UPnP/1.1 DLNADOC/1.50";

        public static async Task<List<DlnaServer>> Discover(float seconds = 3f)
        {
            var locations = new HashSet<string>();
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                string[] targets =
                {
                    "urn:schemas-upnp-org:device:MediaServer:1",
                    "urn:schemas-upnp-org:service:ContentDirectory:1",
                    "ssdp:all"
                };
                for (int round = 0; round < 2; round++)
                    foreach (var st in targets)
                    {
                        var msg = Encoding.ASCII.GetBytes(
                            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + st +
                            "\r\nUSER-AGENT: PowderPulse/1.0 UPnP/1.1\r\n\r\n");
                        try { await udp.SendAsync(msg, msg.Length, group); } catch (Exception e) { Debug.Log("SSDP send: " + e.Message); }
                    }
                var deadline = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var recv = udp.ReceiveAsync();
                    var done = await Task.WhenAny(recv, Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, deadline - Time.realtimeSinceStartup))));
                    if (done != recv) break;
                    string text = Encoding.UTF8.GetString(recv.Result.Buffer);
                    string loc = Header(text, "LOCATION");
                    if (!string.IsNullOrEmpty(loc)) locations.Add(loc);
                }
            }
            catch (Exception e) { Debug.LogWarning("SSDP search failed: " + e.Message); }
            finally { try { udp?.Close(); } catch { } }

            var servers = new Dictionary<string, DlnaServer>();
            foreach (var loc in locations)
                foreach (var s in await LoadDescription(loc))
                    servers[s.Id] = s;
            return servers.Values.ToList();
        }

        static string Header(string text, string name)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (string.Equals(line.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        /// <summary>Sends a request and waits for it without blocking the game.</summary>
        public static Task<UnityWebRequest> Send(UnityWebRequest r)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            r.SendWebRequest().completed += _ => tcs.TrySetResult(r);
            return tcs.Task;
        }

        static string Text(UnityWebRequest r)
        {
            var bytes = r.downloadHandler?.data;
            if (bytes == null) return "";
            var t = Encoding.UTF8.GetString(bytes);
            return t.Length > 0 && t[0] == '﻿' ? t.Substring(1) : t;
        }

        static async Task<List<DlnaServer>> LoadDescription(string location)
        {
            var found = new List<DlnaServer>();
            try
            {
                using (var r = UnityWebRequest.Get(location))
                {
                    r.timeout = 10;
                    r.SetRequestHeader("User-Agent", UserAgent);
                    await Send(r);
                    if (r.result != UnityWebRequest.Result.Success) return found;
                    var doc = XDocument.Parse(Text(r));
                    XNamespace ns = doc.Root.Name.Namespace;
                    string urlBase = (string)doc.Root.Element(ns + "URLBase");
                    var baseUri = new Uri(string.IsNullOrWhiteSpace(urlBase) ? location : urlBase.Trim());
                    foreach (var dev in doc.Descendants(ns + "device"))
                    {
                        var list = dev.Element(ns + "serviceList");
                        var svc = list?.Elements(ns + "service").FirstOrDefault(x => ((string)x.Element(ns + "serviceType") ?? "").Contains(":service:ContentDirectory:"));
                        if (svc == null) continue;
                        found.Add(new DlnaServer
                        {
                            Id = ((string)dev.Element(ns + "UDN") ?? location).Trim(),
                            Name = ((string)dev.Element(ns + "friendlyName") ?? "Media server").Trim(),
                            Location = location,
                            ServiceType = ((string)svc.Element(ns + "serviceType")).Trim(),
                            ControlUrl = new Uri(baseUri, ((string)svc.Element(ns + "controlURL") ?? "").Trim()).ToString()
                        });
                    }
                }
            }
            catch (Exception e) { Debug.Log("DLNA description " + location + ": " + e.Message); }
            return found;
        }

        /// <summary>Lists the children of a container. Returns the entries and the server's total count.</summary>
        public static async Task<(List<DlnaEntry> items, int total, int returned)> Browse(DlnaServer srv, string objectId, int start, int count)
        {
            string body =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body><u:Browse xmlns:u=\"" + srv.ServiceType + "\">" +
                "<ObjectID>" + XmlEscape(objectId) + "</ObjectID><BrowseFlag>BrowseDirectChildren</BrowseFlag><Filter>*</Filter>" +
                "<StartingIndex>" + start.ToString(CultureInfo.InvariantCulture) + "</StartingIndex>" +
                "<RequestedCount>" + count.ToString(CultureInfo.InvariantCulture) + "</RequestedCount>" +
                "<SortCriteria></SortCriteria></u:Browse></s:Body></s:Envelope>";
            using (var r = new UnityWebRequest(srv.ControlUrl, "POST"))
            {
                r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "text/xml; charset=\"utf-8\"" };
                r.downloadHandler = new DownloadHandlerBuffer();
                r.timeout = 20;
                r.SetRequestHeader("SOAPACTION", "\"" + srv.ServiceType + "#Browse\"");
                r.SetRequestHeader("User-Agent", UserAgent);
                await Send(r);
                if (r.result != UnityWebRequest.Result.Success)
                    throw new Exception("The media server answered: " + r.error);
                var doc = XDocument.Parse(Text(r));
                var resultEl = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Result");
                var totalEl = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TotalMatches");
                var returnedEl = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "NumberReturned");
                var items = ParseDidl(resultEl != null ? resultEl.Value : "");
                return (items, ParseInt(totalEl?.Value, items.Count), ParseInt(returnedEl?.Value, items.Count));
            }
        }

        static List<DlnaEntry> ParseDidl(string didl)
        {
            var list = new List<DlnaEntry>();
            if (string.IsNullOrWhiteSpace(didl)) return list;
            XDocument d;
            try { d = XDocument.Parse(didl); }
            catch { d = XDocument.Parse(Regex.Replace(didl, "&(?!(amp|lt|gt|quot|apos|#[0-9]+|#x[0-9a-fA-F]+);)", "&amp;")); }
            foreach (var el in d.Root.Elements())
            {
                string ln = el.Name.LocalName;
                if (ln != "container" && ln != "item") continue;
                string cls = Child(el, "class") ?? "";
                var e = new DlnaEntry
                {
                    Id = (string)el.Attribute("id") ?? "",
                    Title = Child(el, "title") ?? "Untitled",
                    Artist = Child(el, "artist") ?? Child(el, "albumArtist") ?? Child(el, "creator"),
                    Album = Child(el, "album"),
                    Genre = Child(el, "genre"),
                    Date = Child(el, "date"),
                    Track = ParseInt(Child(el, "originalTrackNumber"), 0),
                    Kind = ln == "container" ? "container" : cls.Contains("audioItem") ? "audio" : "other"
                };
                if (ln == "item")
                {
                    XElement main = null;
                    foreach (var res in el.Elements().Where(x => x.Name.LocalName == "res"))
                    {
                        string mime = MimeOf((string)res.Attribute("protocolInfo") ?? "");
                        if (mime.StartsWith("image/") || string.IsNullOrWhiteSpace(res.Value)) continue;
                        e.Res.Add(new KeyValuePair<string, string>(res.Value.Trim(), mime));
                        if (main == null) { main = res; e.Mime = mime; }
                    }
                    if (main != null)
                    {
                        e.Url = main.Value.Trim();
                        e.Duration = ParseDuration((string)main.Attribute("duration"));
                    }
                }
                list.Add(e);
            }
            return list;
        }

        static string Child(XElement el, string localName)
        {
            var c = el.Elements().FirstOrDefault(x => x.Name.LocalName == localName);
            var v = c?.Value.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }

        static string MimeOf(string protocolInfo)
        {
            var parts = protocolInfo.Split(':');
            return parts.Length >= 3 ? parts[2].ToLowerInvariant() : "";
        }

        static string XmlEscape(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

        public static int ParseInt(string s, int def) =>
            int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

        static double ParseDuration(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            double total = 0;
            foreach (var p in s.Trim().Split(':'))
            {
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
                total = total * 60 + v;
            }
            return total;
        }
    }
}
