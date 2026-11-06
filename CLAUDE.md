# Powder Pulse

Cel-shaded street-style snowboarding roguelite for Xbox Series X (Dev Mode first, Store later). The music the player picks is reshaped live by the ride (altitude, speed, air time, crashes, combos).

## Layout
- `PowderPulse/web/index.html` — the whole game (Three.js r128, Web Audio). `three.min.js` is downloaded by CI, not committed.
- `PowderPulse/MainPage.xaml.cs` — UWP host: WebView2 on virtual host `game.powderpulse`; polls the controller with Windows.Gaming.Input and posts `{type:'pad', b, x, y, rt}` (bit i = standard Gamepad button i); serves USB songs at `https://music.powderpulse/<index><ext>` with CORS so Web Audio can process them.
- Page → host: `{type:'music.requestLibrary', quiet}` (sent quietly at launch, and by Find my music) and `{type:'log', text}` (written to the log, e.g. songs that fail to play).
- Host → page: the whole library in chunks of 800, `music.library` `{part, last, total, source, songs:[{k,t,a,l,g,y,n,d}]}` (k = NAS address or `usb:<i>`, t title, a artist, l album, g genre, y year, n track, d seconds), or `music.status` `{text}`.
- NAS crawl: Music-looking top containers, then the first "All Music/Songs/Tracks" list it meets; otherwise walks folders (max 500 browse calls, 40,000 songs). Songs are served at `https://music.powderpulse/nas?u=<address>` (only hosts of discovered media servers) and `/usb?i=<n>`. NAS songs are downloaded whole into memory (max 80 MB, 2 at a time, 3-minute timeout) because WebResourceRequested needs a seekable stream.
- The page keeps the library, the saved playlist (`localStorage pp_playlist`) and the play mode (`pp_mode`: all, playlist, custom). A new library never cuts off the song that's playing.
- Shuffle switch (`btnShuffle` on the Music screen, `localStorage pp_shuffle`, on by default) applies to PLAY, PLAY PLAYLIST and play-from-here. Flipping it mid-song keeps the current song and reorders the rest (`queueBase` holds the unshuffled list).
- A ride never waits more than 3 s for audio to start (`begin()`); audio failures go to `reportError`.
- `Microsoft.Web.WebView2 1.0.4078.44` must stay referenced explicitly, or Xbox reports "WebView2 runtime not detected" (learned on WMC SX).
- Don't commit home-network details (NAS names, IPs); the repo may be public.
- Log: `LocalState\powderpulse_log.txt` (Device Portal → File explorer).

## Game structure (index.html)
- Modes: `run` (roguelite), `free` (endless mountains), `skate` (endless snowskate street spots). `isFree()` is true for free and skate.
- Terrain: `groundH` = slope + noise + side walls + kickers (16 m buckets in `c.kb`) − drops (cliffs, pitches, stair sets; prefix sums in `c.drops`, built by `indexCourse`). `groundFast` reads the height grid sampled for the terrain mesh (particles, tracks). Snowskate rails are polylines `{kind, x, w, h, pts:[{z,y}]}` read with `railAt`. Street gaps (`c.gaps`) are a kicked plaza edge over snow-capped planters (`c.blocks`, crash if hit low); every gap clears with an ollie at 9–13 m/s.
- No gems (crystals) in Free Ride or Snowskate; they only appear in the roguelite run.
- Draw calls: `mergeStatic(group)` bakes every static mesh of a course (and the backdrop) into one mesh per material. Snowskate streets went from ~1,150 draws to ~115 (the rider is most of what's left); before this, phones couldn't load Snowskate. Keep new course props as plain meshes with shared materials so they merge; never create a material per prop. Lamp glows are one Points cloud.
- Memory: per-course outline shapes (`hullGeo` while `buildingCourse`) and instanced tree/rock/gem shapes are freed when the next course is built.
- Browser check: `.github/workflows/browser-check.yml` runs `tests/browser-check.mjs` in headless Chromium with the real three.js on every push that touches the game. It presses Drop In, Free Ride and Snowskate (phone and TV sizes), fails on page errors or a stalled rider, and force-pushes screenshots plus the report to the `ci-shots` branch (`git clone -b ci-shots` to look at them). Use it; a stand-in for three.js missed the frozen-render bug below.
- Never build an InstancedMesh with zero instances or without per-instance colors when its material was used with colors before: three.js r128 reuses the colored shader and throws in `renderBufferDirect` every frame (Free Ride and Snowskate froze on 2026-11-04). With no crystals the crystal layer is simply not built.
- Tricky mode: `Tricky` (meter fills on every clean `scoreTrick`, 6 needed; LT = gamepad button 6, T, or tap the HUD meter; 10 s of ×1.35 speed and ×1.55 spin/flip). The Options screen (`scrOptions`) turns it off (`localStorage pp_tricky`). The browser check covers both.
- Trees option: `Opts.trees` (`localStorage pp_trees`). Courses keep every tree in `c.allTrees`; `applyTreeOption` (called by `buildCourseMeshes`) picks `c.trees`, so switching mid-ride just rebuilds the meshes. Covered by the browser check.
- NAS playback: the page sends `{type:'music.prefetch', u}` for the next NAS song when one starts; the Xbox app downloads it (≤40 MB, one at a time) so songs change without a gap.
- Xbox app: posts `{type:'active', on}` when the guide or another app covers the game (the page pauses, the controller isn't sent while inactive), and reloads `index.html?recovered=1` if the page's engine dies (once a minute at most).
- Font: the Xbox build downloads Chakra Petch into `web/fonts` and swaps the Google Fonts link for `fonts.css`, so it looks right offline (falls back to the online link if that step fails).
- Errors show as a small on-screen note and go to the Xbox log (`reportError`).
- Camera: always directly behind the rider (the owner asked for no side swings or filmer angles).
- Menus with a controller: `navMove(dir)` moves to the nearest button in the pushed direction (song list rows go one by one; only visible rows count from outside the list); holding repeats at 90→55→30 ms. Music screen: opens on the first song, LB/RB switch tabs (`libTabStep`), LT/RT jump a page (`libPage`), focus stays on the same row number when the list redraws (`menuEl`). Covered by the browser check (`__pp.menu`).
- Controls: X/B/LB/RB are one set of trick buttons: snowboard grabs (Indy/Method/Tail/Nose) in the air, LB/RB presses and butters on the snow; snowskate flips (Kickflip/Heelflip/Shove-it/Tre). Stick up/down = flips (snowboard air) or manuals (snowskate ground).
- Performance: adaptive resolution (`adaptResolution`), crystals updated only near the rider (`flakeWindow`), audio automation only when a target moves (`Music.set`).

## Unity version (unity/)
- Unity 6.3 LTS (6000.3.28f1), URP, Input System only. The owner's working copy is `~/PowderPulse` on the Mac; this folder is the GitHub copy. Keep both in sync (scripts are written here and copied to the Mac; Unity-generated `.meta` files come back from the Mac). When copying to the Mac, stage each file under a new name (e.g. `outputs/sync2/X.v2.cs`): re-committing the same staged path has delivered an older copy. Afterwards stage the Mac files back and `cmp` them.
- Everything is built from code at Play: `Boot` (RuntimeInitializeOnLoadMethod) adds `Game` to whatever scene is open. No scene editing needed.
- Scripts: `World` (endless Free Ride mountain, same recipe as the web `genCourse`, `Ground(x,z)`), `Rider` (physics + trick scoring ported from the web `step()`/`land()`; +x is right in Unity, so steering is not inverted), `RiderModel` (rider from primitives, posed per frame; stand-in for a rigged character), `TerrainStreamer` (64 m chunks, 1 m grid, trees, kicker lips), `SnowFx`, `Game` (look/light/post, camera always directly behind, OnGUI HUD and pause/music menu).
- Shaders in `Resources/` (so builds include them): `Toon` (two-tone, cast shadows, rim, inverted-hull outline in the SRPDefaultUnlit pass, ShadowCaster/DepthOnly/DepthNormals), `Sky`, `SnowFx`.
- Music (`Scripts/Music`): `Dlna` (SSDP over UdpClient + ContentDirectory over UnityWebRequest, port of Services/Dlna.cs), `MusicLibrary` (same NAS crawl rules as the Xbox app; USB = KnownFolders.RemovableDevices on Xbox, /Volumes on the Mac skipping network shares, 10 s budget), `MusicPlayer` (prefetches the next song, shuffle in `PlayerPrefs pp_shuffle`, demo loop synthesized at start, ride reactions ported from the web `Music.update`; `BassShelf` is an OnAudioFilterRead low shelf and must live on a GameObject without an AudioListener).
- Unity decodes mp3/ogg/wav/aiff. For FLAC/M4A the NAS's ready-to-play copy (another `<res>`) is used when offered; otherwise the song is marked `Convert` and turned into a WAV in the cache just before it plays (`MusicLibrary.ConvertToWav`: Apple's `afconvert` on the Mac, `MediaTranscoder` on Xbox; deleted after playing, leftovers cleared by `CleanCache`). On the owner's NAS this took the library from 9,264 to 13,241 songs (18 still skipped; the Music found log lists the skipped formats); an M4A converted in 0.6 s on the Mac. The Xbox (MediaTranscoder) path is untested.
- Mac editor needs macOS "Local Network" permission for Unity to find the NAS.
- `Editor/XboxBuild.cs`: `ProjectSetup` ([InitializeOnLoad]) allows plain-http downloads and sets the UWP capabilities; `XboxBuild.Build` is the GameCI build method (D3D, IL2CPP, honours -customBuildPath).
- Xbox build: `.github/workflows/unity-xbox.yml` (GameCI unity-builder on windows-2022, then the manifest is patched: identity PowderPulse.Unity / CN=PowderPulse, music file types for removableStorage; msbuild Master x64 sideload). Needs secrets UNITY_EMAIL, UNITY_PASSWORD, UNITY_LICENSE (contents of the Mac's Unity_lic.ulf); without them it stops with a warning. Not yet run end to end.

## Build
Web version: push to `main` (outside `unity/`) → `.github/workflows/build.yml` on windows-2022 → artifact `Powder-Pulse-1.0.<run>.0-install` (sideload .msix + dependency .appx files + HOW-TO-INSTALL.txt). Same pipeline as WMC SX. Nothing compiles locally; CI is the compile check.

## Next
- Unity Xbox export: needs Windows with Unity Hub signed in (see Unity section).
