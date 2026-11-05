using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Gaming.Input;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace PowderPulse
{
    /// <summary>
    /// Hosts the game (web/index.html) in WebView2 and gives it what a browser can't:
    /// the Xbox controller via Windows.Gaming.Input, and songs from USB drives.
    /// </summary>
    public sealed partial class MainPage : Windows.UI.Xaml.Controls.Page
    {
        private const string AppHost = "game.powderpulse";
        private const string MusicHost = "https://music.powderpulse/";
        private static readonly string[] AudioTypes = { ".mp3", ".m4a", ".aac", ".flac", ".wav", ".wma", ".ogg", ".opus" };

        private WebView2 web;
        private DispatcherTimer padTimer;
        private string lastPad = "";
        private int padRepeat;
        private List<StorageFile> tracks = new List<StorageFile>();
        private bool scanning;

        public MainPage()
        {
            InitializeComponent();
            // B is a grab button in the game, so never let it navigate the app away.
            SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) => e.Handled = true;
            Loaded += async (s, e) => await StartWeb();
        }

        // ------------------------------------------------------------ Log (read it in Device Portal > File explorer > LocalAppData > PowderPulse > LocalState)

        private static async void Log(string line)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("powderpulse_log.txt", CreationCollisionOption.OpenIfExists);
                await FileIO.AppendTextAsync(file, DateTime.Now.ToString("HH:mm:ss ", CultureInfo.InvariantCulture) + line + "\r\n");
            }
            catch { }
        }

        // ------------------------------------------------------------ WebView2

        private async Task StartWeb()
        {
            Log("Starting");
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
                core.WebResourceRequested += OnMusicRequested;
                core.WebMessageReceived += OnWebMessage;
                core.NavigationCompleted += (s, e) =>
                {
                    Log("Game loaded: " + e.IsSuccess);
                    web.Focus(FocusState.Programmatic);
                };
                core.Navigate("https://" + AppHost + "/index.html");

                padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                padTimer.Tick += PollPad;
                padTimer.Start();
            }
            catch (Exception ex)
            {
                Log("Start failed: " + ex);
                StatusText.Text = "Powder Pulse couldn't start (WebView2 failed to load).\n\n" + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void Post(JsonObject msg)
        {
            try { web?.CoreWebView2?.PostWebMessageAsString(msg.Stringify()); }
            catch (Exception ex) { Log("Post failed: " + ex.Message); }
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
            string type = msg.GetNamedString("type", "");
            if (type == "music.requestLibrary") await ScanUsb();
        }

        // ------------------------------------------------------------ Controller: sent to the page every frame it changes

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

        // ------------------------------------------------------------ Music from USB drives

        private async Task ScanUsb()
        {
            if (scanning) return;
            scanning = true;
            PostStatus("Looking for music on USB drives…");
            var found = new List<StorageFile>();
            try
            {
                var drives = await KnownFolders.RemovableDevices.GetFoldersAsync();
                Log("USB drives: " + drives.Count);
                foreach (var drive in drives)
                {
                    try
                    {
                        var opts = new QueryOptions(CommonFileQuery.OrderByName, AudioTypes);
                        opts.FolderDepth = FolderDepth.Deep;
                        var files = await drive.CreateFileQueryWithOptions(opts).GetFilesAsync(0, 3000);
                        found.AddRange(files);
                    }
                    catch (Exception ex) { Log("Scan of " + drive.Name + " failed: " + ex.Message); }
                }
            }
            catch (Exception ex) { Log("USB scan failed: " + ex.Message); }
            scanning = false;

            if (found.Count == 0)
            {
                PostStatus("No songs found on a USB drive. Plug one in and try again. NAS music is coming in a later build.");
                return;
            }
            tracks = found;
            var list = new JsonArray();
            for (int i = 0; i < found.Count; i++)
            {
                var t = new JsonObject();
                t["title"] = JsonValue.CreateStringValue(found[i].DisplayName);
                t["url"] = JsonValue.CreateStringValue(MusicHost + i.ToString(CultureInfo.InvariantCulture) + found[i].FileType.ToLowerInvariant());
                list.Add(t);
            }
            var msg = new JsonObject();
            msg["type"] = JsonValue.CreateStringValue("music.library");
            msg["source"] = JsonValue.CreateStringValue("usb");
            msg["tracks"] = list;
            Post(msg);
            Log("Sent " + found.Count + " songs");
        }

        private static string MimeFor(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".mp3": return "audio/mpeg";
                case ".m4a": case ".aac": return "audio/mp4";
                case ".flac": return "audio/flac";
                case ".wav": return "audio/wav";
                case ".ogg": case ".opus": return "audio/ogg";
                case ".wma": return "audio/x-ms-wma";
                default: return "application/octet-stream";
            }
        }

        private async void OnMusicRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var uri = new Uri(args.Request.Uri);
                string name = uri.AbsolutePath.Trim('/');
                int dot = name.IndexOf('.');
                if (dot > 0) name = name.Substring(0, dot);
                int idx;
                if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) && idx >= 0 && idx < tracks.Count)
                {
                    var file = tracks[idx];
                    var stream = await file.OpenReadAsync();
                    string headers = "Content-Type: " + MimeFor(file.FileType) + "\r\nAccess-Control-Allow-Origin: *";
                    args.Response = sender.Environment.CreateWebResourceResponse(stream, 200, "OK", headers);
                }
                else
                {
                    args.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
                }
            }
            catch (Exception ex)
            {
                Log("Song request failed: " + ex.Message);
                try { args.Response = sender.Environment.CreateWebResourceResponse(null, 500, "Error", ""); } catch { }
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
