using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using PowderPulse.Services;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Gaming.Input;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace PowderPulse
{
    /// <summary>
    /// Hosts the game (web/index.html) in WebView2 and gives it what a browser can't:
    /// the Xbox controller via Windows.Gaming.Input, and songs from the NAS (DLNA) and USB drives.
    /// Songs reach the page through https://music.powderpulse/ with CORS headers, so Web Audio
    /// can filter them; a plain http:// NAS address would be blocked as mixed content.
    /// </summary>
    public sealed partial class MainPage : Windows.UI.Xaml.Controls.Page
    {
        private sealed class Song
        {
            public string Title;
            public string Url;          // NAS song
            public StorageFile File;    // USB song
            public string Mime;
        }

        private const string AppHost = "game.powderpulse";
        private const string MusicHost = "https://music.powderpulse/";
        private static readonly string[] AudioTypes = { ".mp3", ".m4a", ".aac", ".flac", ".wav", ".wma", ".ogg", ".opus" };
        private const int MaxSongs = 3000;
        private const int MaxBrowseCalls = 300;
        private const long MaxSongBytes = 80L * 1024 * 1024;

        private WebView2 web;
        private DispatcherTimer padTimer;
        private string lastPad = "";
        private int padRepeat;
        private List<Song> songs = new List<Song>();
        private bool scanning;
        private readonly SemaphoreSlim songGate = new SemaphoreSlim(2, 2);

        public MainPage()
        {
            InitializeComponent();
            Log.Start();
            // B is a grab button in the game, so never let it navigate the app away.
            SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) => e.Handled = true;
            Loaded += async (s, e) => await StartWeb();
        }

        // ------------------------------------------------------------ WebView2

        private async Task StartWeb()
        {
            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FF1B1030");

                web = new WebView2();
                web.HorizontalAlignment = HorizontalAlignment.Stretch;
                web.VerticalAlignment = VerticalAlignment.Stretch;
                WebHost.Children.Add(web);

                await web.EnsureCoreWebView2Async();
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                string folder = Path.Combine(Package.Current.InstalledLocation.Path, "web");
                core.SetVirtualHostNameToFolderMapping(AppHost, folder, CoreWebView2HostResourceAccessKind.Allow);
                core.AddWebResourceRequestedFilter(MusicHost + "*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnSongRequested;
                core.WebMessageReceived += OnWebMessage;
                core.ProcessFailed += (s, e) => Log.Write("WebView2 process failed: " + e.ProcessFailedKind);
                core.NavigationCompleted += (s, e) =>
                {
                    Log.Write("Game loaded: " + e.IsSuccess + (e.IsSuccess ? "" : " " + e.WebErrorStatus));
                    web.Focus(FocusState.Programmatic);
                };
                core.Navigate("https://" + AppHost + "/index.html");

                padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                padTimer.Tick += PollPad;
                padTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error("StartWeb", ex);
                StatusText.Text = "Powder Pulse couldn't start (WebView2 failed to load).\n\n" + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void Post(JsonObject msg)
        {
            try { web?.CoreWebView2?.PostWebMessageAsString(msg.Stringify()); }
            catch (Exception ex) { Log.Write("Post failed: " + ex.Message); }
        }

        private void PostStatus(string text)
        {
            var o = new JsonObject();
            o["type"] = JsonValue.CreateStringValue("music.status");
            o["text"] = JsonValue.CreateStringValue(text);
            Post(o);
        }

        private async void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string raw;
            try { raw = args.TryGetWebMessageAsString(); } catch { return; }
            JsonObject msg;
            if (!JsonObject.TryParse(raw, out msg)) return;
            string type = J.Str(msg, "type", "");
            if (type == "music.requestLibrary") await FindMusic(J.Bool(msg, "quiet", false));
        }

        // ------------------------------------------------------------ Controller: sent to the page whenever it changes

        private void PollPad(object sender, object e)
        {
            if (web == null || web.CoreWebView2 == null) return;
            var pads = Gamepad.Gamepads;
            if (pads.Count == 0) return;
            var r = pads[0].GetCurrentReading();
            var gb = r.Buttons;
            int b = 0;
            if ((gb & GamepadButtons.A) != 0) b |= 1 << 0;
            if ((gb & GamepadButtons.B) != 0) b |= 1 << 1;
            if ((gb & GamepadButtons.X) != 0) b |= 1 << 2;
            if ((gb & GamepadButtons.Y) != 0) b |= 1 << 3;
            if ((gb & GamepadButtons.LeftShoulder) != 0) b |= 1 << 4;
            if ((gb & GamepadButtons.RightShoulder) != 0) b |= 1 << 5;
            if (r.LeftTrigger > 0.4) b |= 1 << 6;
            if (r.RightTrigger > 0.4) b |= 1 << 7;
            if ((gb & GamepadButtons.View) != 0) b |= 1 << 8;
            if ((gb & GamepadButtons.Menu) != 0) b |= 1 << 9;
            if ((gb & GamepadButtons.DPadUp) != 0) b |= 1 << 12;
            if ((gb & GamepadButtons.DPadDown) != 0) b |= 1 << 13;
            if ((gb & GamepadButtons.DPadLeft) != 0) b |= 1 << 14;
            if ((gb & GamepadButtons.DPadRight) != 0) b |= 1 << 15;

            var inv = CultureInfo.InvariantCulture;
            string msg = "{\"type\":\"pad\",\"b\":" + b.ToString(inv)
                + ",\"x\":" + r.LeftThumbstickX.ToString("0.###", inv)
                + ",\"y\":" + (-r.LeftThumbstickY).ToString("0.###", inv)
                + ",\"rt\":" + r.RightTrigger.ToString("0.##", inv) + "}";
            // send on change, plus a heartbeat so the page knows the controller is still there
            if (msg == lastPad && ++padRepeat < 15) return;
            padRepeat = 0;
            lastPad = msg;
            try { web.CoreWebView2.PostWebMessageAsString(msg); } catch { }
        }

        // ------------------------------------------------------------ Finding music: NAS (DLNA) and USB drives

        private async Task FindMusic(bool quiet)
        {
            if (scanning) return;
            scanning = true;
            try
            {
                if (!quiet) PostStatus("Looking for your NAS and USB drives…");
                var nasTask = ScanNas(quiet);
                var usbTask = ScanUsb();
                await Task.WhenAll(nasTask, usbTask);
                var nas = nasTask.Result;
                var usb = usbTask.Result;
                Log.Write("Music found: " + nas.Count + " on the NAS, " + usb.Count + " on USB");

                var all = new List<Song>(nas);
                all.AddRange(usb);
                if (all.Count == 0)
                {
                    if (!quiet) PostStatus("No music found. Check that the NAS media server is on, or plug in a USB drive, then try again.");
                    return;
                }
                // shuffle so every run plays something different
                var rng = new Random();
                for (int i = all.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var t = all[i]; all[i] = all[j]; all[j] = t;
                }
                if (all.Count > MaxSongs) all = all.Take(MaxSongs).ToList();
                songs = all;

                var list = new JsonArray();
                for (int i = 0; i < all.Count; i++)
                {
                    var t = new JsonObject();
                    t["title"] = JsonValue.CreateStringValue(all[i].Title);
                    t["url"] = JsonValue.CreateStringValue(MusicHost + "s/" + i.ToString(CultureInfo.InvariantCulture));
                    list.Add(t);
                }
                var msg = new JsonObject();
                msg["type"] = JsonValue.CreateStringValue("music.library");
                msg["source"] = JsonValue.CreateStringValue(nas.Count > 0 ? "nas" : "usb");
                msg["count"] = JsonValue.CreateNumberValue(all.Count);
                msg["tracks"] = list;
                Post(msg);
            }
            catch (Exception ex)
            {
                Log.Error("FindMusic", ex);
                if (!quiet) PostStatus("Something went wrong while looking for music.");
            }
            finally
            {
                scanning = false;
            }
        }

        private async Task<List<Song>> ScanUsb()
        {
            var found = new List<Song>();
            try
            {
                var drives = await KnownFolders.RemovableDevices.GetFoldersAsync();
                foreach (var drive in drives)
                {
                    try
                    {
                        var opts = new QueryOptions(CommonFileQuery.OrderByName, AudioTypes);
                        opts.FolderDepth = FolderDepth.Deep;
                        var files = await drive.CreateFileQueryWithOptions(opts).GetFilesAsync(0, MaxSongs);
                        foreach (var f in files) found.Add(new Song { Title = f.DisplayName, File = f, Mime = MimeFor(f.FileType) });
                    }
                    catch (Exception ex) { Log.Write("USB scan of a drive failed: " + ex.Message); }
                }
            }
            catch (Exception ex) { Log.Write("USB scan failed: " + ex.Message); }
            return found;
        }

        private static readonly Regex AllSongs = new Regex(@"^(all\s*(music|songs|tracks|audio)|songs|tracks)$", RegexOptions.IgnoreCase);
        private static readonly Regex MusicRoot = new Regex(@"music|audio", RegexOptions.IgnoreCase);
        private static readonly Regex NotMusic = new Regex(@"photo|picture|image|video|movie|film|tv|recording|playlist", RegexOptions.IgnoreCase);

        private async Task<List<Song>> ScanNas(bool quiet)
        {
            var found = new List<Song>();
            try
            {
                await Dlna.Discover();
                // servers answer within a couple of seconds
                for (int i = 0; i < 6 && Dlna.Servers.Count == 0; i++) await Task.Delay(500);
                await Task.Delay(500);
                var servers = Dlna.Servers;
                Log.Write("Media servers found: " + servers.Count);
                foreach (var srv in servers)
                {
                    if (found.Count >= MaxSongs) break;
                    if (!quiet) PostStatus("Reading music from " + srv.Name + "…");
                    await CrawlServer(srv, found);
                }
            }
            catch (Exception ex) { Log.Error("ScanNas", ex); }
            return found;
        }

        private async Task<List<JsonObject>> BrowseAll(string serverId, string objectId, Counter calls)
        {
            var items = new List<JsonObject>();
            int start = 0;
            while (calls.N < MaxBrowseCalls && items.Count < 6000)
            {
                calls.N++;
                var page = await Dlna.Browse(serverId, objectId, start, 500);
                var arr = J.Arr(page, "items");
                foreach (var v in arr) items.Add(v.GetObject());
                int returned = (int)J.Num(page, "returned", arr.Count);
                int total = (int)J.Num(page, "total", 0);
                start += returned;
                if (returned == 0 || start >= total) break;
            }
            return items;
        }

        private sealed class Counter { public int N; }

        /// <summary>
        /// Walks the server's music folders. Most servers (Synology, Plex, MiniDLNA) have an
        /// "All Music" style list; when one shows up it's used on its own so songs aren't
        /// collected again through Artist/Album/Genre views.
        /// </summary>
        private async Task CrawlServer(DlnaServer srv, List<Song> found)
        {
            var calls = new Counter();
            var seen = new HashSet<string>(found.Select(s => s.Url));
            var queue = new Queue<Tuple<string, int>>();
            try
            {
                var top = await BrowseAll(srv.Id, "0", calls);
                var containers = top.Where(o => J.Str(o, "kind") == "container").ToList();
                var music = containers.Where(o => MusicRoot.IsMatch(J.Str(o, "title", ""))).ToList();
                var start = music.Count > 0 ? music : containers.Where(o => !NotMusic.IsMatch(J.Str(o, "title", ""))).ToList();
                foreach (var c in start) queue.Enqueue(Tuple.Create(J.Str(c, "id"), 1));
                AddSongs(top, found, seen);

                while (queue.Count > 0 && found.Count < MaxSongs && calls.N < MaxBrowseCalls)
                {
                    var next = queue.Dequeue();
                    var items = await BrowseAll(srv.Id, next.Item1, calls);
                    AddSongs(items, found, seen);
                    if (next.Item2 >= 7) continue;
                    var subs = items.Where(o => J.Str(o, "kind") == "container").ToList();
                    var flat = subs.FirstOrDefault(o => AllSongs.IsMatch(J.Str(o, "title", "").Trim()));
                    if (flat != null)
                    {
                        queue.Clear();
                        queue.Enqueue(Tuple.Create(J.Str(flat, "id"), next.Item2 + 1));
                        continue;
                    }
                    foreach (var c in subs)
                        if (!NotMusic.IsMatch(J.Str(c, "title", ""))) queue.Enqueue(Tuple.Create(J.Str(c, "id"), next.Item2 + 1));
                }
            }
            catch (Exception ex) { Log.Error("CrawlServer", ex); }
            Log.Write("Crawled a media server: " + found.Count + " songs so far, " + calls.N + " requests");
        }

        private static void AddSongs(List<JsonObject> items, List<Song> found, HashSet<string> seen)
        {
            foreach (var o in items)
            {
                if (J.Str(o, "kind") != "audio") continue;
                string url = J.Str(o, "url");
                if (string.IsNullOrEmpty(url) || !seen.Add(url)) continue;
                string title = J.Str(o, "title", "Untitled");
                string artist = J.Str(o, "artist");
                found.Add(new Song { Title = string.IsNullOrEmpty(artist) ? title : artist + " – " + title, Url = url, Mime = J.Str(o, "mime") });
                if (found.Count >= MaxSongs) return;
            }
        }

        private static string MimeFor(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".mp3": return "audio/mpeg";
                case ".m4a": case ".aac": return "audio/mp4";
                case ".flac": return "audio/flac";
                case ".wav": return "audio/wav";
                case ".ogg": case ".opus": return "audio/ogg";
                case ".wma": return "audio/x-ms-wma";
                default: return "audio/mpeg";
            }
        }

        // ------------------------------------------------------------ Serving a song to the page

        private async void OnSongRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var uri = new Uri(args.Request.Uri);
                string name = uri.AbsolutePath.Trim('/');
                if (name.StartsWith("s/")) name = name.Substring(2);
                int idx;
                var list = songs;
                if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) && idx >= 0 && idx < list.Count)
                {
                    var song = list[idx];
                    IRandomAccessStream stream = null;
                    if (song.File != null) stream = await song.File.OpenReadAsync();
                    else stream = await FetchSong(song.Url);
                    if (stream != null)
                    {
                        string mime = string.IsNullOrEmpty(song.Mime) || !song.Mime.StartsWith("audio") ? "audio/mpeg" : song.Mime;
                        args.Response = sender.Environment.CreateWebResourceResponse(stream, 200, "OK",
                            "Content-Type: " + mime + "\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store");
                        return;
                    }
                }
                args.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
            }
            catch (Exception ex)
            {
                Log.Write("Song request failed: " + ex.Message);
                try { args.Response = sender.Environment.CreateWebResourceResponse(null, 500, "Error", "Access-Control-Allow-Origin: *"); } catch { }
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>Downloads a whole song from the NAS into memory (one or two at a time, nothing huge).</summary>
        private async Task<IRandomAccessStream> FetchSong(string url)
        {
            await songGate.WaitAsync();
            try
            {
                using (var resp = await Dlna.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!resp.IsSuccessStatusCode) { Log.Write("NAS song answered " + (int)resp.StatusCode); return null; }
                    long? len = resp.Content.Headers.ContentLength;
                    if (len.HasValue && len.Value > MaxSongBytes) { Log.Write("Skipped a song over 80 MB"); return null; }
                    var mem = new InMemoryRandomAccessStream();
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    {
                        var buf = new byte[131072];
                        int n;
                        var writer = new DataWriter(mem);
                        while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                        {
                            if (n == buf.Length) writer.WriteBytes(buf);
                            else { var part = new byte[n]; Array.Copy(buf, part, n); writer.WriteBytes(part); }
                            await writer.StoreAsync();
                            if (mem.Size > (ulong)MaxSongBytes) { writer.DetachStream(); mem.Dispose(); return null; }
                        }
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    mem.Seek(0);
                    return mem;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't read a song from the NAS: " + ex.Message);
                return null;
            }
            finally
            {
                songGate.Release();
            }
        }
    }
}
