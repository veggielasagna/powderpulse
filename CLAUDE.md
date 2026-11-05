# Powder Pulse

Cel-shaded street-style snowboarding roguelite for Xbox Series X (Dev Mode first, Store later). The music the player picks is reshaped live by the ride (altitude, speed, air time, crashes, combos).

## Layout
- `PowderPulse/web/index.html` — the whole game (Three.js r128, Web Audio). `three.min.js` is downloaded by CI, not committed.
- `PowderPulse/MainPage.xaml.cs` — UWP host: WebView2 on virtual host `game.powderpulse`; polls the controller with Windows.Gaming.Input and posts `{type:'pad', b, x, y, rt}` (bit i = standard Gamepad button i); serves USB songs at `https://music.powderpulse/<index><ext>` with CORS so Web Audio can process them.
- Page → host: `{type:'music.requestLibrary', quiet}` (sent quietly at launch, and by Find my music) and `{type:'log', text}` (written to the log, e.g. songs that fail to play).
- Host → page: the whole library in chunks of 800, `music.library` `{part, last, total, source, songs:[{k,t,a,l,g,y,n,d}]}` (k = NAS address or `usb:<i>`, t title, a artist, l album, g genre, y year, n track, d seconds), or `music.status` `{text}`.
- NAS crawl: Music-looking top containers, then the first "All Music/Songs/Tracks" list it meets; otherwise walks folders (max 500 browse calls, 40,000 songs). Songs are served at `https://music.powderpulse/nas?u=<address>` (only hosts of discovered media servers) and `/usb?i=<n>`. NAS songs are downloaded whole into memory (max 80 MB, 2 at a time, 3-minute timeout) because WebResourceRequested needs a seekable stream.
- The page keeps the library, the saved playlist (`localStorage pp_playlist`) and the play mode (`pp_mode`: all, playlist, custom). A new library never cuts off the song that's playing.
- `Microsoft.Web.WebView2 1.0.4078.44` must stay referenced explicitly, or Xbox reports "WebView2 runtime not detected" (learned on WMC SX).
- Don't commit home-network details (NAS names, IPs); the repo may be public.
- Log: `LocalState\powderpulse_log.txt` (Device Portal → File explorer).

## Game structure (index.html)
- Modes: `run` (roguelite), `free` (endless mountains), `skate` (endless snowskate street spots). `isFree()` is true for free and skate.
- Terrain: `groundH` = slope + noise + side walls + kickers (16 m buckets in `c.kb`) − drops (cliffs, pitches, stair sets; prefix sums in `c.drops`, built by `indexCourse`). `groundFast` reads the height grid sampled for the terrain mesh (particles, tracks). Snowskate rails are polylines `{kind, x, w, h, pts:[{z,y}]}` read with `railAt`.
- Controls: X/B/LB/RB are one set of trick buttons: snowboard grabs (Indy/Method/Tail/Nose) in the air, LB/RB presses and butters on the snow; snowskate flips (Kickflip/Heelflip/Shove-it/Tre). Stick up/down = flips (snowboard air) or manuals (snowskate ground).
- Performance: adaptive resolution (`adaptResolution`), crystals updated only near the rider (`flakeWindow`), audio automation only when a target moves (`Music.set`).

## Build
Push to `main` → `.github/workflows/build.yml` on windows-2022 → artifact `Powder-Pulse-1.0.<run>.0-install` (sideload .msix + dependency .appx files + HOW-TO-INSTALL.txt). Same pipeline as WMC SX. Nothing compiles locally; CI is the compile check.

## Next
- Bundle the Google fonts so the game looks right offline.
