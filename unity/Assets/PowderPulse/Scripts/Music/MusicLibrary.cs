using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
#if ENABLE_WINMD_SUPPORT
using Windows.Storage;
using Windows.Storage.Search;
#endif

namespace PowderPulse.Music
{
    public sealed class Song
    {
        public string Title, Artist, Album, Genre, Url, Ext;
        public int Track;
        public double Duration;
        public bool Usb;
        public bool Convert;          // FLAC/M4A: turned into WAV just before it plays
#if ENABLE_WINMD_SUPPORT
        public StorageFile File;      // USB song on Xbox: copied to the app's cache before playing
#endif
        public string Label => string.IsNullOrEmpty(Artist) ? Title : Title + " – " + Artist;
    }

    /// <summary>
    /// Finds songs on DLNA media servers (your NAS) and on USB drives.
    /// Xbox: KnownFolders.RemovableDevices. Mac editor: drives under /Volumes. Windows: removable drives.
    /// </summary>
    public static class MusicLibrary
    {
        const int MaxSongs = 40000, MaxBrowseCalls = 500;
        // Unity can decode these everywhere; FLAC/M4A/WMA are skipped for now
        static readonly string[] Playable = { ".mp3", ".ogg", ".wav", ".aif", ".aiff" };
        static readonly string[] AudioExt = { ".mp3", ".ogg", ".wav", ".aif", ".aiff", ".m4a", ".aac", ".flac", ".wma", ".opus" };
        // these get converted to WAV with the system's own decoder (Apple's afconvert on the Mac, Windows' MediaTranscoder on Xbox)
        static readonly string[] Convertible = { ".flac", ".m4a", ".aac", ".alac" };

        public static bool CanConvert =>
#if ENABLE_WINMD_SUPPORT || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            true;
#else
            false;
#endif

        static bool Accept(string ext, out bool convert)
        {
            convert = false;
            if (Playable.Contains(ext)) return true;
            if (CanConvert && Convertible.Contains(ext)) { convert = true; return true; }
            Skipped++;
            return false;
        }
        static readonly Regex AllSongs = new Regex(@"^(all\s*(music|songs|tracks|audio)|songs|tracks)$", RegexOptions.IgnoreCase);
        static readonly Regex MusicRoot = new Regex(@"music|audio", RegexOptions.IgnoreCase);
        static readonly Regex NotMusic = new Regex(@"photo|picture|image|video|movie|film|tv|recording|playlist", RegexOptions.IgnoreCase);

        public static int Skipped { get; private set; }

        public static AudioType TypeFor(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".aif": case ".aiff": return AudioType.AIFF;
                default: return AudioType.MPEG;
            }
        }

        static string ExtForMime(string mime)
        {
            switch ((mime ?? "").ToLowerInvariant())
            {
                case "audio/mpeg": case "audio/mp3": case "audio/x-mpeg": return ".mp3";
                case "audio/ogg": case "audio/x-ogg": case "application/ogg": return ".ogg";
                case "audio/wav": case "audio/x-wav": case "audio/wave": case "audio/l16": return ".wav";
                case "audio/aiff": case "audio/x-aiff": return ".aiff";
                case "audio/mp4": case "audio/x-m4a": case "audio/aac": return ".m4a";
                case "audio/flac": case "audio/x-flac": return ".flac";
                default: return "";
            }
        }

        // ---------- NAS ----------
        public static void ResetCounts() => Skipped = 0;

        public static async Task<List<Song>> ScanNas(Action<string> status)
        {
            var found = new List<Song>();
            var servers = await Dlna.Discover();
            Debug.Log("Media servers found: " + servers.Count);
            foreach (var srv in servers)
            {
                if (found.Count >= MaxSongs) break;
                status?.Invoke("Reading music from " + srv.Name + "…");
                await Crawl(srv, found);
            }
            return found;
        }

        sealed class Counter { public int N; }

        static async Task<List<DlnaEntry>> BrowseAll(DlnaServer srv, string id, Counter calls)
        {
            var items = new List<DlnaEntry>();
            int start = 0;
            while (calls.N < MaxBrowseCalls && items.Count < MaxSongs)
            {
                calls.N++;
                var page = await Dlna.Browse(srv, id, start, 500);
                items.AddRange(page.items);
                start += page.returned;
                if (page.returned == 0 || start >= page.total) break;
            }
            return items;
        }

        /// <summary>
        /// Walks the server's music folders. Most servers (Synology, Plex, MiniDLNA) have an "All Music"
        /// style list; when one shows up it's used on its own so songs aren't collected again through
        /// Artist/Album/Genre views.
        /// </summary>
        static async Task Crawl(DlnaServer srv, List<Song> found)
        {
            var calls = new Counter();
            var seen = new HashSet<string>(found.Select(s => s.Url));
            var queue = new Queue<(string id, int depth)>();
            try
            {
                var top = await BrowseAll(srv, "0", calls);
                var containers = top.Where(o => o.Kind == "container").ToList();
                var music = containers.Where(o => MusicRoot.IsMatch(o.Title)).ToList();
                foreach (var c in music.Count > 0 ? music : containers.Where(o => !NotMusic.IsMatch(o.Title))) queue.Enqueue((c.Id, 1));
                AddSongs(top, found, seen);
                while (queue.Count > 0 && found.Count < MaxSongs && calls.N < MaxBrowseCalls)
                {
                    var next = queue.Dequeue();
                    var items = await BrowseAll(srv, next.id, calls);
                    AddSongs(items, found, seen);
                    if (next.depth >= 7) continue;
                    var subs = items.Where(o => o.Kind == "container").ToList();
                    var flat = subs.FirstOrDefault(o => AllSongs.IsMatch(o.Title.Trim()));
                    if (flat != null) { queue.Clear(); queue.Enqueue((flat.Id, next.depth + 1)); continue; }
                    foreach (var c in subs) if (!NotMusic.IsMatch(c.Title)) queue.Enqueue((c.Id, next.depth + 1));
                }
            }
            catch (Exception e) { Debug.LogWarning("Reading " + srv.Name + " failed: " + e.Message); }
            Debug.Log($"Crawled {srv.Name}: {found.Count} songs so far, {calls.N} requests");
        }

        static void AddSongs(List<DlnaEntry> items, List<Song> found, HashSet<string> seen)
        {
            foreach (var o in items)
            {
                if (o.Kind != "audio" || string.IsNullOrEmpty(o.Url) || !seen.Add(o.Url)) continue;
                string url = o.Url, ext = ExtOf(o.Mime, o.Url);
                if (!Playable.Contains(ext))
                {   // many servers also offer a ready-to-play copy (MP3 or WAV) of FLAC/M4A files: use it when there is one
                    foreach (var r in o.Res)
                    {
                        string e2 = ExtOf(r.Value, r.Key);
                        if (Playable.Contains(e2)) { url = r.Key; ext = e2; break; }
                    }
                }
                if (!Accept(ext, out bool convert)) continue;
                found.Add(new Song { Title = o.Title, Artist = o.Artist, Album = o.Album, Genre = o.Genre, Track = o.Track, Duration = o.Duration, Url = url, Ext = ext, Convert = convert });
            }
        }

        static string ExtOf(string mime, string url)
        {
            string ext = ExtForMime(mime);
            if (ext != "") return ext;
            try { return Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant(); } catch { return ""; }
        }

        // ---------- USB ----------
        public static async Task<List<Song>> ScanUsb()
        {
            var found = new List<Song>();
#if ENABLE_WINMD_SUPPORT
            try
            {
                var drives = await KnownFolders.RemovableDevices.GetFoldersAsync();
                foreach (var drive in drives)
                {
                    try
                    {
                        var opts = new QueryOptions(CommonFileQuery.OrderByName, AudioExt);
                        opts.FolderDepth = FolderDepth.Deep;
                        var files = await drive.CreateFileQueryWithOptions(opts).GetFilesAsync(0, 5000);
                        foreach (var f in files)
                        {
                            string ext = (f.FileType ?? "").ToLowerInvariant();
                            if (!Accept(ext, out bool convert)) continue;
                            var song = new Song { Title = f.DisplayName, Ext = ext, Usb = true, File = f, Convert = convert };
                            try
                            {
                                var p = await f.Properties.GetMusicPropertiesAsync();
                                if (!string.IsNullOrWhiteSpace(p.Title)) song.Title = p.Title;
                                song.Artist = string.IsNullOrWhiteSpace(p.Artist) ? p.AlbumArtist : p.Artist;
                                song.Album = p.Album;
                                song.Genre = p.Genre != null && p.Genre.Count > 0 ? p.Genre[0] : null;
                                song.Track = (int)p.TrackNumber;
                                song.Duration = p.Duration.TotalSeconds;
                            }
                            catch { }
                            found.Add(song);
                        }
                    }
                    catch (Exception e) { Debug.LogWarning("USB scan of a drive failed: " + e.Message); }
                }
            }
            catch (Exception e) { Debug.LogWarning("USB scan failed: " + e.Message); }
#else
            // drive listing and file walking stay off the main thread: a sleepy drive never freezes the game
            await Task.Run(() =>
            {
                var roots = new List<string>();
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                try
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        // USB drives only: skip the Mac's own disk and network shares (a NAS mounted in Finder)
                        string d = drive.RootDirectory.FullName.TrimEnd('/');
                        if (!d.StartsWith("/Volumes/") || drive.DriveType == DriveType.Network) continue;
                        string fs = (drive.DriveFormat ?? "").ToLowerInvariant();
                        if (fs.Contains("smb") || fs.Contains("afp") || fs.Contains("nfs") || fs.Contains("webdav") || fs.Contains("cifs")) continue;
                        if (Directory.Exists(Path.Combine(d, "System/Library"))) continue;
                        roots.Add(d);
                    }
                }
                catch (Exception e) { Debug.Log("Listing drives failed: " + e.Message); }
#else
                foreach (var d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Removable && d.IsReady) roots.Add(d.RootDirectory.FullName);
#endif
                var until = DateTime.UtcNow.AddSeconds(10);    // a huge or slow drive never holds the music up
                foreach (var root in roots) Walk(root, 0, found, until);
            });
#endif
            return found;
        }

#if !ENABLE_WINMD_SUPPORT
        static void Walk(string dir, int depth, List<Song> found, DateTime until)
        {
            if (depth > 8 || found.Count >= 5000 || DateTime.UtcNow > until) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(f);
                    if (name.StartsWith(".")) continue;
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (!AudioExt.Contains(ext)) continue;
                    if (!Accept(ext, out bool convert)) continue;
                    // Artist/Album/01 Title.mp3 is the usual layout
                    var album = Path.GetFileName(Path.GetDirectoryName(f));
                    var artist = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f)));
                    var title = Regex.Replace(Path.GetFileNameWithoutExtension(f), @"^\d{1,3}[\s._-]+", "");
                    found.Add(new Song { Title = title, Album = album, Artist = depth >= 2 ? artist : null, Url = new Uri(f).AbsoluteUri, Ext = ext, Usb = true, Convert = convert });
                }
                foreach (var d in Directory.GetDirectories(dir))
                    if (!Path.GetFileName(d).StartsWith(".")) Walk(d, depth + 1, found, until);
            }
            catch { }
        }
#endif

        /// <summary>A URL Unity can load the song from. USB songs on Xbox are copied into the cache first.</summary>
        public static async Task<string> PlayableUrl(Song s)
        {
#if ENABLE_WINMD_SUPPORT
            if (s.File != null)
            {
                var cache = await StorageFolder.GetFolderFromPathAsync(Application.temporaryCachePath);
                var copy = await s.File.CopyAsync(cache, "usb-song" + s.Ext, NameCollisionOption.GenerateUniqueName);
                return new Uri(copy.Path).AbsoluteUri;
            }
#endif
            await Task.Yield();
            return s.Url;
        }

        /// <summary>
        /// Turns a FLAC/M4A song into a WAV in the cache and returns its file URL.
        /// NAS songs are downloaded first; the WAV is deleted again once the song is done (ForgetCopy).
        /// </summary>
        public static async Task<string> ConvertToWav(Song s, string url)
        {
            string cache = Application.temporaryCachePath, id = Guid.NewGuid().ToString("N");
            string src, wav = Path.Combine(cache, "song-" + id + ".wav");
            bool tempSrc = false;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                src = Path.Combine(cache, "src-" + id + s.Ext); tempSrc = true;
                using (var r = new UnityWebRequest(url, "GET"))
                {
                    r.downloadHandler = new DownloadHandlerFile(src) { removeFileOnAbort = true };
                    r.timeout = 180;
                    await Dlna.Send(r);
                    if (r.result != UnityWebRequest.Result.Success) throw new Exception("download failed: " + r.error);
                }
            }
            else
            {
                src = new Uri(url).LocalPath;
                tempSrc = src.StartsWith(cache);          // a USB copy made for playing
            }
            try
            {
#if ENABLE_WINMD_SUPPORT
                var srcFile = await StorageFile.GetFileFromPathAsync(src);
                var folder = await StorageFolder.GetFolderFromPathAsync(cache);
                var dstFile = await folder.CreateFileAsync(Path.GetFileName(wav), CreationCollisionOption.ReplaceExisting);
                var transcoder = new Windows.Media.Transcoding.MediaTranscoder();
                var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.High);
                var prep = await transcoder.PrepareFileTranscodeAsync(srcFile, dstFile, profile);
                if (!prep.CanTranscode) throw new Exception("Windows can't convert it: " + prep.FailureReason);
                await prep.TranscodeAsync();
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                await Task.Run(() =>
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/afconvert", $"-f WAVE -d LEI16@44100 \"{src}\" \"{wav}\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        string err = p.StandardError.ReadToEnd();
                        if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } throw new Exception("conversion took too long"); }
                        if (p.ExitCode != 0) throw new Exception("afconvert: " + err.Trim());
                    }
                });
#else
                await Task.Yield();
                throw new Exception("this platform can't convert " + s.Ext);
#endif
            }
            catch
            {
                try { File.Delete(wav); } catch { }
                throw;
            }
            finally
            {
                if (tempSrc) { try { File.Delete(src); } catch { } }
            }
            return new Uri(wav).AbsoluteUri;
        }

        /// <summary>Removes converted songs and USB copies left over from an earlier session.</summary>
        public static void CleanCache()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Application.temporaryCachePath))
                {
                    var n = Path.GetFileName(f);
                    if (n.StartsWith("song-") || n.StartsWith("src-") || n.StartsWith("usb-song")) File.Delete(f);
                }
            }
            catch { }
        }

        /// <summary>Deletes copies of USB songs made for playing (Xbox only).</summary>
        public static void ForgetCopy(string url)
        {
            try
            {
                if (url == null || !url.StartsWith("file:")) return;
                var path = new Uri(url).LocalPath;
                if (path.StartsWith(Application.temporaryCachePath)) File.Delete(path);
            }
            catch { }
        }
    }
}
