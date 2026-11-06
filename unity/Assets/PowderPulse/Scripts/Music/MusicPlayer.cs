using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PowderPulse.Music
{
    public enum MusicMode { Ride, Crash, Pause, Menu }

    /// <summary>What the ride is doing right now; the music reshapes itself from this every frame.</summary>
    public struct RideSound
    {
        public MusicMode mode;
        public float alt;       // 0 bottom of the run .. 1 top
        public float speedN;    // 0..1
        public bool air;
        public float airT;
        public int combo;
    }

    /// <summary>
    /// Plays your music (NAS, USB, or the built-in demo loop) and reshapes it with the ride, like the web
    /// version: speed opens the low-pass, air muffles and adds echo, altitude thins the bass and adds space,
    /// combos boost the bass, clean landings punch through, crashes tape-stop.
    /// A new library never cuts off the song that's playing; the next song is loaded while this one plays.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        AudioSource src;
        AudioLowPassFilter lp;
        AudioHighPassFilter hp;
        BassShelf bass;
        AudioReverbFilter rev;
        AudioClip demo;

        readonly List<Song> baseList = new List<Song>();   // library order
        readonly List<Song> queue = new List<Song>();      // play order (shuffled or not)
        int idx = -1;
        Song current;
        string currentUrl;
        bool loading, scanning;
        int failures;
        (Song song, AudioClip clip, string url)? next;
        Task prefetching;

        float lpHz = 22000f, hpHz = 10f, wet = 0.1f, duck = 1f, bassDb, rate = 1f, rateTarget = 1f, crashT, landKick;
        public float Volume = 0.9f;

        public bool Shuffle { get; private set; } = true;
        public string Status { get; private set; } = "";
        public string NowPlaying => current != null ? current.Label : "Powder Pulse demo loop";
        public int NasCount { get; private set; }
        public int UsbCount { get; private set; }
        public bool Scanning => scanning;
        public float SongStartedAt { get; private set; }

        void Awake()
        {
            Shuffle = PlayerPrefs.GetInt("pp_shuffle", 1) == 1;
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = Volume;
            hp = gameObject.AddComponent<AudioHighPassFilter>();
            lp = gameObject.AddComponent<AudioLowPassFilter>();
            bass = gameObject.AddComponent<BassShelf>();
            rev = gameObject.AddComponent<AudioReverbFilter>();
            rev.reverbPreset = AudioReverbPreset.User;
            rev.dryLevel = 0f; rev.room = -800f; rev.roomHF = -1200f; rev.decayTime = 2.4f; rev.reflectionsLevel = -1400f;
            rev.reverbLevel = -10000f; rev.diffusion = 100f; rev.density = 100f;
            MusicLibrary.CleanCache();
            demo = DemoLoop.Make();
            PlayDemo();
        }

        void PlayDemo()
        {
            current = null;
            src.clip = demo; src.loop = true; src.Play();
            SongStartedAt = Time.unscaledTime;
        }

        // ---------- library ----------
        public async void FindMusic(bool quiet)
        {
            if (scanning) return;
            scanning = true;
            try
            {
                MusicLibrary.ResetCounts();
                Status = "Looking for your NAS and USB drives…";
                var usbT = MusicLibrary.ScanUsb();
                var nas = await MusicLibrary.ScanNas(s => Status = s);
                NasCount = nas.Count; UsbCount = 0;
                // NAS songs start right away; USB songs join whenever that scan finishes
                if (nas.Count > 0) { SetLibrary(nas); Status = Summary(); }
                var usb = await usbT;
                UsbCount = usb.Count;
                Debug.Log($"Music found: {nas.Count} on the NAS, {usb.Count} on USB, {MusicLibrary.Skipped} skipped: {MusicLibrary.SkippedKinds}");
                if (usb.Count > 0) { var all = new List<Song>(nas); all.AddRange(usb); SetLibrary(all); }
                if (nas.Count + usb.Count == 0)
                {
                    Status = quiet ? "" : "No music found. Check the NAS media server is on, or plug in a USB drive, then try again.";
                    return;
                }
                Status = Summary();
            }
            catch (Exception e) { Debug.LogWarning("Finding music failed: " + e); Status = "Something went wrong while looking for music."; }
            finally { scanning = false; }
        }

        string Summary() =>
            $"{NasCount + UsbCount:N0} songs ready" + (MusicLibrary.Skipped > 0 ? $" · {MusicLibrary.Skipped:N0} skipped (format not supported)" : "");

        void SetLibrary(List<Song> songs)
        {
            baseList.Clear(); baseList.AddRange(songs);
            Reorder(keepCurrent: false);
            next = null;
            if (current == null) { idx = -1; PlayNext(); }   // the demo loop gives way to your music right away
            else idx = -1;                                   // a song is playing: keep it, the new list starts after it
        }

        void Reorder(bool keepCurrent)
        {
            queue.Clear();
            var rest = new List<Song>(baseList);
            if (keepCurrent && current != null && rest.Remove(current)) queue.Add(current);
            if (Shuffle) for (int i = rest.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (rest[i], rest[j]) = (rest[j], rest[i]); }
            else if (keepCurrent && current != null)
            {   // carry on in library order from the song that's playing
                int at = baseList.IndexOf(current);
                rest = new List<Song>();
                for (int k = 1; k < baseList.Count; k++) rest.Add(baseList[(at + k) % baseList.Count]);
            }
            queue.AddRange(rest);
        }

        public void SetShuffle(bool on)
        {
            Shuffle = on;
            PlayerPrefs.SetInt("pp_shuffle", on ? 1 : 0);
            if (baseList.Count == 0) return;
            Reorder(keepCurrent: true);
            idx = current != null ? 0 : -1;
            next = null;
            Status = on ? "Shuffle on" : "Shuffle off · playing in order";
        }

        // ---------- playback ----------
        public void Next()
        {
            if (queue.Count == 0) { Status = "No music found yet"; return; }
            PlayNext();
        }

        async void PlayNext()
        {
            if (loading || queue.Count == 0) return;
            loading = true;
            try
            {
                idx = (idx + 1) % queue.Count;
                var song = queue[idx];
                AudioClip clip; string url;
                if (next.HasValue && next.Value.song == song) { clip = next.Value.clip; url = next.Value.url; next = null; }
                else (clip, url) = await Load(song);
                if (clip == null)
                {
                    if (++failures >= 5) { Status = "Your songs couldn't be played; playing the demo loop."; failures = 0; PlayDemo(); return; }
                    loading = false; PlayNext(); return;
                }
                failures = 0;
                var old = src.clip; var oldUrl = currentUrl;
                current = song; currentUrl = url;
                src.clip = clip; src.loop = false; src.Play();
                SongStartedAt = Time.unscaledTime;
                if (old != null && old != demo) Destroy(old);
                MusicLibrary.ForgetCopy(oldUrl);
                prefetching = Prefetch();
            }
            finally { loading = false; }
        }

        async Task Prefetch()
        {
            if (queue.Count < 2) return;
            var song = queue[(idx + 1) % queue.Count];
            var (clip, url) = await Load(song);
            if (clip == null) return;
            if (next.HasValue && next.Value.clip != clip) { Destroy(next.Value.clip); MusicLibrary.ForgetCopy(next.Value.url); }
            next = (song, clip, url);
        }

        static async Task<(AudioClip, string)> Load(Song s)
        {
            string url = null;
            try
            {
                url = await MusicLibrary.PlayableUrl(s);
                if (s.Convert)
                {
                    var t0 = Time.realtimeSinceStartup;
                    url = await MusicLibrary.ConvertToWav(s, url);
                    Debug.Log($"Converted {s.Label} ({s.Ext}) in {Time.realtimeSinceStartup - t0:0.0}s");
                }
                using (var r = UnityWebRequestMultimedia.GetAudioClip(url, s.Convert ? AudioType.WAV : MusicLibrary.TypeFor(s.Ext)))
                {
                    var dh = (DownloadHandlerAudioClip)r.downloadHandler;
                    dh.streamAudio = false;
                    dh.compressed = true;        // keep long songs small in memory
                    r.timeout = 180;
                    await Dlna.Send(r);
                    if (r.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"Couldn't load {s.Label}: {r.error}"); return (null, url); }
                    var clip = dh.audioClip;
                    if (clip == null || clip.length < 1f) { Debug.LogWarning($"Couldn't decode {s.Label}"); return (null, url); }
                    clip.name = s.Label;
                    return (clip, url);
                }
            }
            catch (Exception e) { Debug.LogWarning($"Couldn't play {s.Label}: {e.Message}"); return (null, url); }
        }

        // ---------- the ride reshapes the music ----------
        public void Crash() { rateTarget = 0.3f; crashT = 0.7f; }

        public void Land(int combo)
        {
            // a clean landing punches through: filter wide open, bass kick, a little louder for a moment
            lpHz = 18000f; bassDb = Mathf.Min(combo * 1.6f, 9f) + 9f; duck = 1.15f; landKick = 0.35f;
        }

        public void Drive(RideSound s, float dt)
        {
            float lpT, hpT, wetT, duckT = 1f, bassT = Mathf.Min(s.combo * 1.6f, 9f);
            switch (s.mode)
            {
                case MusicMode.Ride:
                    hpT = 25f + s.alt * s.alt * 520f;
                    wetT = 0.07f + s.alt * 0.28f;
                    if (s.air) { float k = Mathf.Clamp01(s.airT / 0.6f); lpT = Mathf.Lerp(4000f, 650f, k); wetT += 0.35f * k; duckT = Mathf.Lerp(1f, 0.72f, k); }
                    else lpT = 900f + Mathf.Pow(s.speedN, 1.4f) * 18500f;
                    break;
                case MusicMode.Crash: lpT = 380f; hpT = 30f; wetT = 0.45f; duckT = 0.85f; break;
                case MusicMode.Pause: lpT = 650f; hpT = 40f; wetT = 0.3f; duckT = 0.7f; bassT = 0f; break;
                default: lpT = 1700f; hpT = 60f; wetT = 0.42f; duckT = 0.85f; bassT = 0f; break;
            }
            float a(float tc) => 1f - Mathf.Exp(-dt / tc);
            if (landKick > 0f) landKick -= dt;
            else
            {
                lpHz = Mathf.Exp(Mathf.Lerp(Mathf.Log(lpHz), Mathf.Log(lpT), a(s.air ? 0.12f : 0.15f)));
                bassDb = Mathf.Lerp(bassDb, bassT, a(0.25f));
                duck = Mathf.Lerp(duck, duckT, a(0.1f));
            }
            hpHz = Mathf.Exp(Mathf.Lerp(Mathf.Log(hpHz), Mathf.Log(hpT), a(0.3f)));
            wet = Mathf.Lerp(wet, wetT, a(0.2f));
            if (crashT > 0f) { crashT -= dt; if (crashT <= 0f) rateTarget = 1f; }
            rate = Mathf.Lerp(rate, rateTarget, a(rateTarget < 1f ? 0.2f : 0.45f));

            lp.cutoffFrequency = Mathf.Clamp(lpHz, 200f, 22000f);
            hp.cutoffFrequency = Mathf.Clamp(hpHz, 10f, 2000f);
            bass.gainDb = bassDb;
            rev.reverbLevel = Mathf.Clamp(2000f * Mathf.Log10(Mathf.Max(wet, 0.001f)) + 600f, -10000f, 1000f);
            rev.dryLevel = -wet * 450f;
            src.volume = Mathf.Clamp01(Volume * duck);
            src.pitch = Mathf.Clamp(rate, 0.25f, 1.2f);

            // song finished: on to the next (the demo loops by itself)
            if (current != null && !loading && !src.isPlaying && src.clip != null && src.time == 0f && Application.isFocused) PlayNext();
        }

        public void Pause(bool paused)
        {
            if (paused) src.Pause(); else src.UnPause();
        }
    }

    /// <summary>Low-shelf bass boost (biquad) on the audio thread; gain set from the game.</summary>
    public class BassShelf : MonoBehaviour
    {
        public volatile float gainDb;
        float applied = float.NaN;
        double b0, b1, b2, a1, a2;
        double[] x1 = new double[8], x2 = new double[8], y1 = new double[8], y2 = new double[8];
        int rate;

        void Awake() { rate = AudioSettings.outputSampleRate; }

        void Coeffs(float db)
        {
            // RBJ cookbook low shelf, 110 Hz, S = 1
            double A = Math.Pow(10, db / 40.0), w = 2 * Math.PI * 110.0 / Math.Max(rate, 8000), cs = Math.Cos(w), sn = Math.Sin(w);
            double alpha = sn / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * alpha;
            double a0 = (A + 1) + (A - 1) * cs + sq;
            b0 = A * ((A + 1) - (A - 1) * cs + sq) / a0;
            b1 = 2 * A * ((A - 1) - (A + 1) * cs) / a0;
            b2 = A * ((A + 1) - (A - 1) * cs - sq) / a0;
            a1 = -2 * ((A - 1) + (A + 1) * cs) / a0;
            a2 = ((A + 1) + (A - 1) * cs - sq) / a0;
            applied = db;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float db = gainDb;
            if (db < 0.05f && float.IsNaN(applied)) return;
            if (Mathf.Abs(db - applied) > 0.05f || float.IsNaN(applied)) Coeffs(db);
            channels = Math.Min(channels, 8);
            for (int i = 0; i < data.Length; i += channels)
                for (int c = 0; c < channels; c++)
                {
                    double x = data[i + c];
                    double y = b0 * x + b1 * x1[c] + b2 * x2[c] - a1 * y1[c] - a2 * y2[c];
                    x2[c] = x1[c]; x1[c] = x; y2[c] = y1[c]; y1[c] = y;
                    data[i + c] = (float)(y * (db > 3 ? 0.85 : 1));   // a little headroom when boosting
                }
        }
    }

    /// <summary>A short relaxing loop synthesized at start-up, so there's music before your library loads.</summary>
    public static class DemoLoop
    {
        public static AudioClip Make()
        {
            const int sr = 32000; const float bpm = 96f;
            float beat = 60f / bpm; int bars = 8; int n = Mathf.CeilToInt(sr * bars * 4 * beat);
            var buf = new float[n * 2];
            var rnd = new System.Random(7);
            int[][] chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };
            float Hz(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);
            void Add(int start, int len, Func<int, float> f, float pan)
            {
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float v = f(i);
                    buf[(start + i) * 2] += v * (1f - pan); buf[(start + i) * 2 + 1] += v * (1f + pan);
                }
            }
            for (int bar = 0; bar < bars; bar++)
            {
                var ch = chords[(bar / 2) % 4];
                int barStart = (int)(bar * 4 * beat * sr);
                // soft pad: detuned sines with slow swell
                foreach (int m in ch)
                {
                    float f = Hz(m), len = 4 * beat;
                    Add(barStart, (int)(len * sr), i => { float t = i / (float)sr; float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / len)); return 0.05f * env * (Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * f * 1.003f * t)); }, (m % 3 - 1) * 0.3f);
                }
                // bass on the root
                float bf = Hz(ch[0] - 24);
                for (int b = 0; b < 4; b++)
                    Add(barStart + (int)(b * beat * sr), (int)(beat * 0.9f * sr), i => { float t = i / (float)sr; return 0.16f * Mathf.Exp(-t * 3f) * Mathf.Sin(2 * Mathf.PI * bf * t); }, 0f);
                // plucked arpeggio
                for (int s = 0; s < 8; s++)
                {
                    int m = ch[s % 3] + (s >= 4 ? 12 : 0); float f = Hz(m);
                    Add(barStart + (int)(s * beat * 0.5f * sr), (int)(0.6f * sr), i => { float t = i / (float)sr; return 0.06f * Mathf.Exp(-t * 7f) * Mathf.Sin(2 * Mathf.PI * f * t + 0.6f * Mathf.Sin(2 * Mathf.PI * f * 2f * t)); }, s % 2 == 0 ? -0.4f : 0.4f);
                }
                // kick on 1 and 3, soft hat on the off-beats
                for (int b = 0; b < 4; b += 2)
                    Add(barStart + (int)(b * beat * sr), (int)(0.35f * sr), i => { float t = i / (float)sr; float f = 42f + 120f * Mathf.Exp(-t * 30f); return 0.35f * Mathf.Exp(-t * 9f) * Mathf.Sin(2 * Mathf.PI * f * t); }, 0f);
                for (int b = 0; b < 4; b++)
                    Add(barStart + (int)((b + 0.5f) * beat * sr), (int)(0.05f * sr), i => 0.03f * Mathf.Exp(-i / (float)sr * 80f) * (float)(rnd.NextDouble() * 2 - 1), 0.2f);
            }
            float peak = 0f; foreach (var v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0.9f) for (int i = 0; i < buf.Length; i++) buf[i] *= 0.9f / peak;
            var clip = AudioClip.Create("Powder Pulse demo loop", n, 2, sr, false);
            clip.SetData(buf, 0);
            return clip;
        }
    }
}
