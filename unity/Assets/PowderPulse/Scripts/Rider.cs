using System.Collections.Generic;
using UnityEngine;

namespace PowderPulse
{
    /// <summary>
    /// Snowboard physics and trick scoring, ported from the web game's step()/land().
    /// Carving, ollies, spins and flips with momentum, grabs, landing assist, sketchy landings, crashes.
    /// </summary>
    public class Rider
    {
        const float TAU = Mathf.PI * 2f;

        public Vector3 pos;
        public float vy, heading, speed = 10f;
        public bool grounded = true;
        public float airT, spin, spinStart, spinVel, flip, flipVel, pitch, lean, leanV;
        public float groundT, invuln, crashT;
        public bool popped, bigAir, crashed;
        public string grab, grabName;
        public float grabT;
        public Vector3 tumble, tumbleRot;
        public float squash, squashV, hipKick;   // visual springs the model reads
        public int score, combo;
        public float steer, impact;               // last steer input, last landing impact (for effects)
        public bool tuck;

        readonly World w;
        readonly Game game;

        public Rider(World world, Game g)
        {
            w = world; game = g;
            Respawn(0f, 12f, 10f);
            invuln = 1f;
        }

        public Vector3 Forward => new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));

        void Respawn(float x, float z, float v)
        {
            pos = new Vector3(x, w.Ground(x, z), z);
            vy = 0f; heading = 0f; speed = v; grounded = true; airT = 0f;
            spin = 0f; flip = 0f; spinVel = 0f; flipVel = 0f; crashed = false; tumbleRot = Vector3.zero;
            grab = null; grabName = null; grabT = 0f; bigAir = false;
        }

        public void Step(float dt, PadState inp, bool firstSubstep)
        {
            steer = inp.steer; tuck = inp.tuck;
            bool jumpEdge = firstSubstep && inp.jumpEdge;
            if (crashed)
            {
                crashT -= dt;
                speed *= Mathf.Exp(-dt * 1.5f);
                Move(dt);
                vy -= World.G * dt;
                pos.y = Mathf.Max(w.Ground(pos.x, pos.z), pos.y + vy * dt);
                tumbleRot += tumble * dt;
                if (crashT <= 0f) { Respawn(pos.x, pos.z, Mathf.Max(speed, 8f)); invuln = 1.6f; }
                return;
            }
            invuln = Mathf.Max(0f, invuln - dt);

            if (grounded)
            {
                groundT += dt;
                if (groundT > 3f && combo > 0) combo = 0;
                float turnRate = 1.9f - Mathf.Min(speed / 40f, 1f) * 0.6f;
                heading = Mathf.Clamp(heading + inp.steer * turnRate * dt, -1.35f, 1.35f);
                if (inp.steer == 0f) heading *= Mathf.Exp(-dt * 0.4f);
                Vector3 f = Forward;
                float dh = w.Ground(pos.x + f.x * 0.6f, pos.z + f.z * 0.6f) - w.Ground(pos.x - f.x * 0.6f, pos.z - f.z * 0.6f);
                float slope = dh / 1.2f;
                float maxV = inp.tuck ? 38f : 30f;
                speed += -slope * World.G * dt;
                speed -= (0.6f + Mathf.Abs(inp.steer) * speed * 0.22f + (speed > maxV ? (speed - maxV) * 1.5f : 0f)
                          + 0.008f * speed * speed * (inp.tuck ? 0.55f : 1f)) * dt;
                speed = Mathf.Clamp(speed, 3f, 60f);
                pitch = Mathf.Lerp(pitch, -Mathf.Atan(slope), 1f - Mathf.Exp(-dt * 12f));
                float tgt = -inp.steer * 0.42f * Mathf.Clamp(speed / 20f, 0.3f, 1f);
                leanV += (tgt - lean) * 90f * dt; leanV *= Mathf.Exp(-11f * dt); lean += leanV * dt;
                if (jumpEdge)
                {   // ollie
                    vy = Mathf.Max(vy, 0f) + 8.2f + Mathf.Max(0f, vy * 0.3f);
                    Takeoff(); popped = true; hipKick += 2.6f; squashV += 3.2f;
                    game.Fx.Burst(pos, 8, 0.5f);
                }
            }
            else
            {
                airT += dt;
                const float spinRate = 7.4f, flipRate = 5.8f;
                // spins and flips build up and carry momentum, so they whip around instead of snapping
                spinVel += (inp.steer * spinRate - spinVel) * (1f - Mathf.Exp(-dt * (inp.steer != 0f ? 7f : 3f)));
                flipVel += (inp.vert * flipRate - flipVel) * (1f - Mathf.Exp(-dt * (inp.vert != 0f ? 8f : 3.5f)));
                // landing assist: just before touchdown, ease into the nearest clean rotation
                if (TimeToLand() < 0.4f)
                {
                    float k = 1f - Mathf.Exp(-dt * 14f);
                    if (Mathf.Abs(inp.steer) < 0.3f) spinVel = Mathf.Lerp(spinVel, (Mathf.Round(spin / Mathf.PI) * Mathf.PI - spin) * 8f, k);
                    if (Mathf.Abs(inp.vert) < 0.3f) flipVel = Mathf.Lerp(flipVel, (Mathf.Round(flip / TAU) * TAU - flip) * 8f, k);
                }
                spin += spinVel * dt;
                flip += flipVel * dt;
                if (inp.grab != null && airT > 0.1f) { grab = inp.grab; grabName = inp.grab; grabT += dt; } else grab = null;
                pitch = Mathf.Lerp(pitch, 0f, 1f - Mathf.Exp(-dt * 3f));
                lean = Mathf.Lerp(lean, 0f, 1f - Mathf.Exp(-dt * 4f)); leanV = 0f;
                if (jumpEdge && !popped && airT < 0.18f) { vy += 6.5f; popped = true; game.Popup("", "POP!"); }
                vy -= World.G * dt;
            }

            Move(dt);
            float gh = w.Ground(pos.x, pos.z);
            if (grounded)
            {
                if (gh < pos.y + (vy - World.G * dt) * dt - 0.03f)
                {
                    Takeoff(); popped = false; pos.y += vy * dt;
                    if (vy > 4f) { hipKick += 1.6f; squashV += 2.4f; game.Fx.Burst(pos, 10, 0.4f); }
                }
                else { vy = Mathf.Clamp((gh - pos.y) / dt, -60f, 22f); pos.y = gh; }
            }
            else
            {
                pos.y += vy * dt;
                if (pos.y <= gh)
                {
                    float hit = -Mathf.Min(0f, vy), aired = airT;
                    pos.y = gh; vy = Mathf.Min(0f, vy);
                    Land();
                    if (!crashed && aired > 0.3f)
                    {
                        hipKick -= Mathf.Clamp(hit * 0.11f, 0.4f, 3.6f);
                        squashV -= Mathf.Clamp(hit * 0.16f, 1f, 5.5f);
                        game.Fx.LandingBurst(pos, hit);
                        game.Shake(Mathf.Clamp((hit - 10f) / 28f, 0f, 0.55f));
                    }
                }
            }
            CheckTrees();
        }

        void Move(float dt)
        {
            Vector3 f = Forward;
            pos.x = Mathf.Clamp(pos.x + f.x * speed * dt, -World.W / 2f - 25f, World.W / 2f + 25f);
            pos.z += f.z * speed * dt;
        }

        void CheckTrees()
        {
            if (invuln > 0f) return;
            var trees = w.trees;
            for (int i = w.FirstTree(pos.z - 2.5f); i < trees.Count && trees[i].z < pos.z + 2.5f; i++)
            {
                var t = trees[i];
                float dx = t.x - pos.x, dz = t.z - pos.z, r = 0.9f * t.s + 0.45f;
                if (dx * dx + dz * dz < r * r && pos.y < w.Ground(t.x, t.z) + 7f * t.s) { Crash("tree"); break; }
            }
        }

        void Takeoff()
        {
            grounded = false; airT = 0f; spinStart = spin; grabT = 0f; grabName = null;
            bigAir = w.IsBigAirLaunch(pos.x, pos.z);   // mega ramps and big cliffs earn slow motion
        }

        /// <summary>Seconds until the current flight meets the snow (1 if longer).</summary>
        float TimeToLand()
        {
            float y = pos.y, v = vy, x = pos.x, z = pos.z;
            Vector3 f = Forward * speed;
            const float h = 0.025f;
            for (float t = 0f; t < 1f; t += h)
            {
                v -= World.G * h; y += v * h; x += f.x * h; z += f.z * h;
                if (y <= w.Ground(x, z)) return t;
            }
            return 1f;
        }

        void Crash(string reason)
        {
            if (invuln > 0f || crashed) return;
            crashed = true; speed *= 0.35f; combo = 0; grab = null; crashT = 1.1f;
            tumble = new Vector3(Random.Range(-14f, 14f), Random.Range(-10f, 10f), Random.Range(-14f, 14f));
            game.Fx.LandingBurst(pos, Mathf.Max(14f, -vy + speed * 0.4f));
            game.Shake(0.6f);
            game.Music?.Crash();
            game.Popup(reason == "tree" ? "Timber!" : "Wipeout", "SHAKE IT OFF", true);
        }

        static readonly Dictionary<string, string> GrabNames = new Dictionary<string, string>
            { { "indy", "Indy" }, { "method", "Method" }, { "tail", "Tail Grab" }, { "nose", "Nose Grab" } };

        void Land()
        {
            float air = airT, gT = grabT; string gName = grabName;
            grounded = true; airT = 0f; grab = null; grabT = 0f; grabName = null; bigAir = false; spinVel = 0f; flipVel = 0f; groundT = 0f;
            if (air < 0.35f) { spin = Mathf.Round(spin / Mathf.PI) * Mathf.PI; flip = 0f; return; }
            float spinR = Mathf.Round(spin / Mathf.PI) * Mathf.PI, spinErr = Mathf.Abs(spin - spinR);
            float flipR = Mathf.Round(flip / TAU) * TAU, flipErr = Mathf.Abs(flip - flipR);
            const float tol = 0.95f;                          // about 55 degrees off still lands clean
            bool sketchy = false;
            if (spinErr > tol || flipErr > tol * 1.1f)
            {
                if (spinErr > tol + 0.6f || flipErr > tol * 1.1f + 0.6f) { Crash("land"); return; }
                sketchy = true;                               // way off but saveable: keep riding, lose the combo
            }
            int deg = Mathf.RoundToInt(Mathf.Abs(spinR - spinStart) * Mathf.Rad2Deg);
            int flips = Mathf.RoundToInt(Mathf.Abs(flipR) / TAU);
            bool front = flipR > 0f;
            spin = spinR; flip = 0f;
            float pts = air * 120f + deg / 180f * 260f + flips * 750f;
            var names = new List<string>();
            if (air > 2.2f) { names.Add("Mega Air"); pts += air * 150f; }
            if (flips > 0) names.Add((flips == 2 ? "Double " : flips >= 3 ? "Triple " : "") + (front ? "Frontflip" : "Backflip"));
            bool grabbed = gT > 0.25f && gName != null;
            string grabLabel = grabbed ? (gT > 0.9f ? "Tweaked " : "") + GrabNames[gName] : "";
            if (grabbed) pts += gT * 350f;
            string main = ((deg >= 180 ? deg.ToString() : "") + " " + grabLabel).Trim();
            if (main.Length > 0) names.Add(main);
            if (names.Count == 0 && air > 1.1f) names.Add("Big Air");
            if (sketchy) ScoreTrick(names, pts, true, false);
            else if (names.Count > 0) ScoreTrick(names, pts, false, deg >= 180 && grabbed);
            else score += Mathf.RoundToInt(air * 60f);
        }

        void ScoreTrick(List<string> names, float pts, bool sketchy, bool style)
        {
            string label = string.Join(" + ", names);
            if (sketchy)
            {
                int p = Mathf.RoundToInt(pts * 0.4f / 10f) * 10;
                score += p; combo = 0; speed *= 0.7f; squashV -= 3f; hipKick -= 2f;
                lean = (Random.value < .5f ? -1f : 1f) * 0.5f;
                game.Popup("Sketchy!", names.Count > 0 ? $"{label} · +{p:N0}" : "SAVED IT", true);
                game.Shake(0.4f);
                return;
            }
            combo++;
            float mult = (1f + (combo - 1) * 0.5f) * (style ? 1.25f : 1f);
            int total = Mathf.RoundToInt(pts * mult / 10f) * 10;
            score += total;
            game.Music?.Land(combo);
            game.Popup(label, $"+{total:N0}" + (mult > 1f ? $"  ×{mult:0.0}" : "") + (style ? "  STYLE" : ""));
            game.Shake(0.25f);
        }

        /// <summary>Per-frame visual springs (squash and stretch).</summary>
        public void UpdateSprings(float dt)
        {
            squashV += -squash * 140f * dt; squashV *= Mathf.Exp(-10f * dt);
            squash = Mathf.Clamp(squash + squashV * dt, -0.32f, 0.3f);
        }
    }
}
