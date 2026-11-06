using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PowderPulse.Music;

namespace PowderPulse
{
    /// <summary>Every material the game uses, created once and shared so the SRP batcher stays fast.</summary>
    public class Materials
    {
        public Material snow, trunk, pine, pineSnow, lip, lipMega, board, ink, boots, pants, jacket, skin, beanie, goggles, track, spray, sky;

        static Color Hex(uint c) => new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

        static Material Toon(Shader s, uint baseC, uint shadeC, float outline, float rim = 0.5f)
        {
            var m = new Material(s);
            m.SetColor("_BaseColor", Hex(baseC));
            m.SetColor("_ShadeColor", Hex(shadeC));
            m.SetFloat("_OutlineWidth", outline);
            m.SetFloat("_Rim", rim);
            if (outline <= 0f) m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            return m;
        }

        public Materials()
        {
            var toon = Shader.Find("PowderPulse/Toon");
            snow = Toon(toon, 0xEEF1FA, 0x9097C6, 0f, 0.25f); snow.SetFloat("_StepWidth", 0.12f);
            trunk = Toon(toon, 0x5B4239, 0x2E2230, 0.05f);
            pine = Toon(toon, 0x3F6F72, 0x1F3346, 0.06f, 0.35f);
            pineSnow = Toon(toon, 0xE6EBF8, 0x8088B8, 0.06f);
            lip = Toon(toon, 0x4A5A96, 0x2A3160, 0.04f);
            lipMega = Toon(toon, 0xE6C27A, 0x8F7140, 0.05f);
            board = Toon(toon, 0xD98AA0, 0x7E4460, 0.02f);
            ink = Toon(toon, 0x1A1F36, 0x10131F, 0f, 0f);
            boots = Toon(toon, 0xD6DCEE, 0x6F78A6, 0.02f);
            pants = Toon(toon, 0x3D3966, 0x211F3B, 0.02f);
            jacket = Toon(toon, 0x4F93A6, 0x264F66, 0.02f);
            skin = Toon(toon, 0xE9B99A, 0x9C6F68, 0.02f);
            beanie = Toon(toon, 0xE6C27A, 0x8F7140, 0.02f);
            goggles = Toon(toon, 0x232842, 0x10131F, 0.01f, 1.2f);
            var fx = Shader.Find("PowderPulse/SnowFx");
            spray = new Material(fx); spray.SetFloat("_Mode", 0f);
            track = new Material(fx); track.SetFloat("_Mode", 1f); track.SetColor("_Color", new Color(0.55f, 0.6f, 0.85f, 0.45f));
            sky = new Material(Shader.Find("PowderPulse/Sky"));
            sky.SetColor("_Horizon", new Color(0.86f, 0.66f, 0.66f));
            sky.SetColor("_Bottom", new Color(0.6f, 0.58f, 0.76f));
        }
    }

    /// <summary>Starts Powder Pulse in whatever scene is open, so pressing Play is all it takes.</summary>
    static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindAnyObjectByType<Game>() == null) new GameObject("Powder Pulse").AddComponent<Game>();
        }
    }

    public class Game : MonoBehaviour
    {
        public SnowFx Fx { get; private set; }
        public MusicPlayer Music { get; private set; }
        World world;
        Rider rider;
        RiderModel model;
        TerrainStreamer terrain;
        Materials mats;
        Camera cam;
        Light sun;
        MotionBlur motionBlur;
        float camYaw, shake, timeScale = 1f, popT, hintT = 8f;
        string popTitle = "", popSub = "";
        bool popBad, paused;
        GUIStyle big, mid, small;

        void Awake()
        {
            Application.targetFrameRate = 60;
            mats = new Materials();
            SetUpLookAndLight();
            world = new World(Random.Range(1, int.MaxValue));
            world.GenerateTo(600f);
            Fx = new SnowFx(mats);
            rider = new Rider(world, this);
            model = new RiderModel(mats);
            terrain = new TerrainStreamer(world, mats);
            terrain.BuildAllNow(rider.pos.z);
            PlaceCamera(1f, true);
            Music = new GameObject("Music").AddComponent<MusicPlayer>();   // its own object: the bass filter can't share one with the listener
            Music.FindMusic(true);     // quietly at launch; the pause menu can look again
            Popup("Free Ride", "DROP IN");
        }

        void SetUpLookAndLight()
        {
            // dusk: low warm sun ahead of the rider, cool lavender shadows, soft haze in the distance
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) Destroy(v.gameObject);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { if (sun == null) sun = l; else Destroy(l.gameObject); }
            if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(24f, 200f, 0f);
            sun.color = new Color(1f, 0.93f, 0.88f);
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.46f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.5f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.33f, 0.5f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.6f, 0.58f, 0.76f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 480f;
            mats.sky.SetVector("_SunDir", -sun.transform.forward);
            RenderSettings.skybox = mats.sky;
            RenderSettings.sun = sun;

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.shadowDistance = 90f;

            cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; cam.gameObject.AddComponent<AudioListener>(); }
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 1500f; cam.fieldOfView = 64f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f); bloom.threshold.Override(0.92f); bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.75f));
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.1f); color.contrast.Override(8f); color.saturation.Override(-4f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.3f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.08f, 0.07f, 0.18f));
            motionBlur = profile.Add<MotionBlur>(true);
            motionBlur.intensity.Override(0f); motionBlur.quality.Override(MotionBlurQuality.Low);
            var vol = new GameObject("Look").AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = profile;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var inp = PadInput.Read();
            if (inp.pauseEdge || (paused && inp.aEdge)) paused = !paused;
            if (paused)
            {
                if (inp.nextEdge) Music.Next();
                if (inp.xEdge) Music.SetShuffle(!Music.Shuffle);
                if (inp.bEdge) Music.FindMusic(false);
                Music.Drive(new RideSound { mode = MusicMode.Pause }, dt);
                return;
            }
            if (inp.nextEdge) Music.Next();
            hintT -= dt;

            world.GenerateTo(rider.pos.z + 700f);
            // slow motion while flying off mega ramps and big cliffs
            float tsT = (!rider.grounded && !rider.crashed && rider.bigAir && rider.airT > 0.15f) ? 0.62f : 1f;
            timeScale = Mathf.Lerp(timeScale, tsT, 1f - Mathf.Exp(-dt * (tsT < 1f ? 6f : 10f)));
            float gdt = dt * timeScale;
            const int sub = 3;
            for (int i = 0; i < sub; i++) rider.Step(gdt / sub, inp, i == 0);

            Fx.Carve(rider, gdt);
            model.Update(rider, gdt, Time.time);
            terrain.Update(rider.pos.z);
            PlaceCamera(gdt, false);
            // a lap of Free Ride is one 1.2 km mountain: altitude runs from 1 at the top to 0 at the bottom
            float alt = 1f - Mathf.Repeat(rider.pos.z, 1200f) / 1200f;
            Music.Drive(new RideSound
            {
                mode = rider.crashed ? MusicMode.Crash : MusicMode.Ride, alt = alt, speedN = Mathf.Clamp01(rider.speed / 38f),
                air = !rider.grounded && !rider.crashed && rider.airT > 0.12f, airT = rider.airT, combo = rider.combo
            }, dt);
            motionBlur.intensity.value = Mathf.Lerp(motionBlur.intensity.value, Mathf.Clamp01((rider.speed - 24f) / 16f) * 0.35f, 1f - Mathf.Exp(-dt * 3f));
            if (popT > 0f) popT -= dt;
        }

        /// <summary>Always directly behind the rider.</summary>
        void PlaceCamera(float dt, bool snap)
        {
            var r = rider;
            camYaw = snap ? r.heading : Mathf.Lerp(camYaw, r.heading, 1f - Mathf.Exp(-dt * 3.2f));
            bool air = !r.grounded && !r.crashed;
            float back = 7.5f + r.speed * 0.06f + (air && r.bigAir ? Mathf.Min(r.airT * 4f, 5f) : 0f);
            float up = 3.2f + (air ? Mathf.Min(r.airT * 1.5f, r.bigAir ? 4.5f : 2.5f) : 0f);
            float tx = r.pos.x - Mathf.Sin(camYaw) * back, tz = r.pos.z - Mathf.Cos(camYaw) * back;
            float ty = Mathf.Max(r.pos.y + up, world.Ground(tx, tz) + 2f);
            var p = cam.transform.position;
            float k = snap ? 1f : 1f - Mathf.Exp(-dt * 6f), ky = snap ? 1f : 1f - Mathf.Exp(-dt * (air ? 7f : 4f));
            p = new Vector3(Mathf.Lerp(p.x, tx, k), Mathf.Lerp(p.y, ty, ky), Mathf.Lerp(p.z, tz, k));
            if (shake > 0f) { p += new Vector3(Random.Range(-.5f, .5f), Random.Range(-.5f, .5f), 0f) * shake; shake = Mathf.Max(0f, shake - dt * 1.5f); }
            cam.transform.position = p;
            float ahead = air && r.bigAir ? 2f : 6f;
            cam.transform.LookAt(new Vector3(r.pos.x + Mathf.Sin(r.heading) * ahead, r.pos.y + 1.2f, r.pos.z + Mathf.Cos(r.heading) * ahead));
            float fovT = 64f + Mathf.Clamp01(r.speed / 38f) * 16f;
            cam.fieldOfView = snap ? fovT : Mathf.Lerp(cam.fieldOfView, fovT, 1f - Mathf.Exp(-dt * 3f));
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        public void Popup(string title, string sub, bool bad = false)
        {
            popTitle = title; popSub = sub; popBad = bad; popT = 1.4f;
        }

        // Simple HUD for now; a styled UI comes later.
        void OnGUI()
        {
            float s = Screen.height / 1080f;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                mid = new GUIStyle(big);
                small = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }
            big.fontSize = Mathf.RoundToInt(64 * s); mid.fontSize = Mathf.RoundToInt(26 * s); small.fontSize = Mathf.RoundToInt(28 * s);
            var snowC = new Color(0.91f, 0.93f, 0.96f); var amber = new Color(0.9f, 0.76f, 0.48f); var rose = new Color(0.85f, 0.54f, 0.63f);

            small.normal.textColor = snowC; small.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(48 * s, 36 * s, 800 * s, 50 * s), rider.score.ToString("N0"), small);
            if (rider.combo > 1)
            {
                small.normal.textColor = amber;
                GUI.Label(new Rect(48 * s, 80 * s, 800 * s, 50 * s), $"COMBO ×{1f + (rider.combo - 1) * 0.5f:0.0}", small);
            }
            small.normal.textColor = snowC; small.alignment = TextAnchor.UpperRight;
            GUI.Label(new Rect(Screen.width - 848 * s, 36 * s, 800 * s, 50 * s), $"{Mathf.RoundToInt(rider.speed * 3.6f)} KM/H", small);

            if (popT > 0f)
            {
                float a = Mathf.Clamp01(popT / 0.3f);
                big.normal.textColor = new Color(popBad ? rose.r : snowC.r, popBad ? rose.g : snowC.g, popBad ? rose.b : snowC.b, a);
                mid.normal.textColor = new Color(amber.r, amber.g, amber.b, a);
                GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 90 * s), popTitle, big);
                GUI.Label(new Rect(0, Screen.height * 0.18f + 84 * s, Screen.width, 40 * s), popSub, mid);
            }
            // now playing: shown for a few seconds when a song starts
            float since = Time.unscaledTime - Music.SongStartedAt;
            if (!paused && since < 5f)
            {
                small.alignment = TextAnchor.LowerLeft;
                small.normal.textColor = new Color(snowC.r, snowC.g, snowC.b, Mathf.Clamp01(5f - since));
                GUI.Label(new Rect(48 * s, Screen.height - 96 * s, Screen.width * 0.6f, 50 * s), "♪ " + Music.NowPlaying, small);
            }
            if (paused)
            {
                GUI.color = new Color(0.06f, 0.07f, 0.13f, 0.72f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                big.normal.textColor = snowC; mid.normal.textColor = snowC;
                float y = Screen.height * 0.22f;
                GUI.Label(new Rect(0, y, Screen.width, 90 * s), "Paused", big);
                mid.normal.textColor = amber;
                GUI.Label(new Rect(0, y + 110 * s, Screen.width, 40 * s), "♪ " + Music.NowPlaying, mid);
                mid.normal.textColor = snowC;
                string lib = Music.NasCount + Music.UsbCount > 0 ? $"NAS {Music.NasCount:N0} songs · USB {Music.UsbCount:N0} songs" : "Demo loop · no NAS or USB music found yet";
                GUI.Label(new Rect(0, y + 160 * s, Screen.width, 40 * s), lib, mid);
                if (!string.IsNullOrEmpty(Music.Status)) GUI.Label(new Rect(0, y + 200 * s, Screen.width, 40 * s), Music.Scanning ? Music.Status : Music.Status, mid);
                mid.normal.textColor = amber;
                GUI.Label(new Rect(0, y + 280 * s, Screen.width, 40 * s), "A  RESUME      Y  NEXT SONG      X  SHUFFLE " + (Music.Shuffle ? "ON" : "OFF") + "      B  FIND MY MUSIC", mid);
                mid.normal.textColor = snowC;
                GUI.Label(new Rect(0, y + 320 * s, Screen.width, 40 * s), "KEYBOARD: ENTER · N · H · F", mid);
            }
            else if (hintT > 0f)
            {
                mid.normal.textColor = new Color(snowC.r, snowC.g, snowC.b, Mathf.Clamp01(hintT));
                GUI.Label(new Rect(0, Screen.height - 150 * s, Screen.width, 40 * s),
                    "STEER: STICK / ARROWS   JUMP: A / SPACE   TUCK: RT / SHIFT   GRABS: X B LB RB / J K U I   SPIN & FLIP IN THE AIR", mid);
                GUI.Label(new Rect(0, Screen.height - 110 * s, Screen.width, 40 * s), "Y / N: NEXT SONG   MENU / ESC: PAUSE AND MUSIC", mid);
            }
        }
    }
}
