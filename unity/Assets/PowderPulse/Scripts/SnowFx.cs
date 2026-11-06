using UnityEngine;

namespace PowderPulse
{
    /// <summary>Snow spray from carves, takeoffs and landings. Kept light so it never hides the rider.</summary>
    public class SnowFx
    {
        readonly ParticleSystem ps;
        float carveAcc;
        static readonly Color32 Powder = new Color32(236, 240, 255, 210);

        public SnowFx(Materials m)
        {
            var go = new GameObject("Snow Spray");
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;   // keeps simulating; nothing emits on its own (emission is off)
            main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.55f;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) }
            };
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.6f; drag.limit = 40f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m.spray;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        void Emit(Vector3 p, Vector3 v, float size, float life)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = p, velocity = v, startSize = size, startLifetime = life, startColor = Powder, applyShapeToPosition = false
            };
            ps.Emit(e, 1);
        }

        /// <summary>Spray thrown off the board's edge while carving.</summary>
        public void Carve(Rider r, float dt)
        {
            if (!r.grounded || r.crashed || r.speed < 5f) { carveAcc = 0f; return; }
            float amount = (Mathf.Abs(r.steer) * 0.9f + 0.08f) * Mathf.Clamp01(r.speed / 25f) * 26f;
            carveAcc += amount * dt;
            Vector3 f = r.Forward, right = new Vector3(f.z, 0f, -f.x);
            float side = r.steer >= 0f ? -1f : 1f;      // snow flies out of the turn
            while (carveAcc >= 1f)
            {
                carveAcc -= 1f;
                Vector3 p = r.pos + right * side * 0.25f + f * Random.Range(-0.6f, 0.2f) + Vector3.up * 0.05f;
                Vector3 v = right * side * Random.Range(1.5f, 4f) * Mathf.Abs(r.steer) + Vector3.up * Random.Range(0.8f, 2.6f) + f * r.speed * 0.25f;
                Emit(p, v, Random.Range(0.12f, 0.3f), Random.Range(0.5f, 0.9f));
            }
        }

        public void Burst(Vector3 p, int n, float strength)
        {
            for (int i = 0; i < n; i++)
            {
                var d = Random.insideUnitCircle;
                Emit(p + new Vector3(d.x, 0.05f, d.y) * 0.3f, new Vector3(d.x * 3f, Random.Range(0.5f, 2.5f), d.y * 3f) * strength * 2f,
                     Random.Range(0.15f, 0.35f), Random.Range(0.4f, 0.8f));
            }
        }

        /// <summary>A ring of powder on touchdown, bigger for harder landings.</summary>
        public void LandingBurst(Vector3 p, float impact)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(impact * 1.4f), 6, 36);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f + Random.value * 0.4f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float s = Random.Range(2f, 4.5f) * Mathf.Clamp(impact / 18f, 0.5f, 1.4f);
                Emit(p + dir * 0.5f + Vector3.up * 0.1f, dir * s + Vector3.up * Random.Range(0.6f, 2.2f), Random.Range(0.2f, 0.45f), Random.Range(0.5f, 1f));
            }
        }
    }
}
