using UnityEngine;

namespace PowderPulse
{
    /// <summary>
    /// The rider, built from simple toon-shaded shapes and posed every frame:
    /// knees bend with speed and landings, the body leans into carves, hands grab the board.
    /// Stand-in until a rigged character replaces it; the physics doesn't care which.
    /// Model space: board along +z (nose), rider faces +x (toe edge).
    /// </summary>
    public class RiderModel
    {
        public readonly Transform root, spinG, flipG;
        readonly Transform model;
        readonly Transform[] thigh = new Transform[2], shin = new Transform[2], arm = new Transform[2];
        readonly Transform torso, head, trailT;
        readonly Renderer[] renderers;
        public readonly TrailRenderer trail;
        float crouch = 0.25f, crouchV, sway;

        static readonly Vector3[] Foot = { new Vector3(0f, 0.1f, 0.27f), new Vector3(0f, 0.1f, -0.27f) };

        public RiderModel(Materials m)
        {
            root = new GameObject("Rider").transform;
            spinG = new GameObject("Spin").transform; spinG.SetParent(root, false);
            flipG = new GameObject("Flip").transform; flipG.SetParent(spinG, false);
            model = new GameObject("Model").transform; model.SetParent(flipG, false);

            // board with kicked-up nose and tail
            Part(PrimitiveType.Cube, m.board, new Vector3(0f, 0.035f, 0f), new Vector3(0.32f, 0.045f, 1.3f));
            Part(PrimitiveType.Cube, m.board, new Vector3(0f, 0.07f, 0.72f), new Vector3(0.3f, 0.045f, 0.22f), Quaternion.Euler(-22f, 0f, 0f));
            Part(PrimitiveType.Cube, m.board, new Vector3(0f, 0.07f, -0.72f), new Vector3(0.3f, 0.045f, 0.22f), Quaternion.Euler(22f, 0f, 0f));
            Part(PrimitiveType.Cube, m.ink, new Vector3(0f, 0.062f, 0f), new Vector3(0.12f, 0.012f, 1.0f));   // top-sheet stripe
            for (int i = 0; i < 2; i++)
            {
                Part(PrimitiveType.Cube, m.boots, Foot[i] + new Vector3(0.02f, -0.01f, 0f), new Vector3(0.3f, 0.14f, 0.15f));
                thigh[i] = Part(PrimitiveType.Capsule, m.pants, Vector3.zero, Vector3.one);
                shin[i] = Part(PrimitiveType.Capsule, m.pants, Vector3.zero, Vector3.one);
                arm[i] = Part(PrimitiveType.Capsule, m.jacket, Vector3.zero, Vector3.one);
            }
            torso = Part(PrimitiveType.Capsule, m.jacket, Vector3.zero, Vector3.one);
            head = new GameObject("Head").transform; head.SetParent(model, false);
            Part(PrimitiveType.Sphere, m.skin, Vector3.zero, Vector3.one * 0.25f, Quaternion.identity, head);
            Part(PrimitiveType.Sphere, m.beanie, new Vector3(0f, 0.05f, 0f), new Vector3(0.27f, 0.2f, 0.27f), Quaternion.identity, head);
            Part(PrimitiveType.Sphere, m.beanie, new Vector3(0f, 0.17f, 0f), Vector3.one * 0.08f, Quaternion.identity, head);
            Part(PrimitiveType.Cube, m.goggles, new Vector3(0.06f, 0.0f, 0.08f), new Vector3(0.2f, 0.07f, 0.09f), Quaternion.Euler(0f, -40f, 0f), head);

            // board track, flat on the snow
            trailT = new GameObject("Track").transform;
            trailT.SetParent(root, false);
            trailT.localPosition = new Vector3(0f, 0.03f, -0.3f);
            trailT.localRotation = Quaternion.Euler(90f, 0f, 0f);
            trail = trailT.gameObject.AddComponent<TrailRenderer>();
            trail.material = m.track;
            trail.alignment = LineAlignment.TransformZ;
            trail.time = 6f; trail.minVertexDistance = 0.6f; trail.widthMultiplier = 0.34f;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) }
            };

            renderers = model.GetComponentsInChildren<Renderer>();
        }

        Transform Part(PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent ? parent : model, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>Stretch a capsule between two points (model space).</summary>
        static void Limb(Transform t, Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = b - a;
            float len = Mathf.Max(d.magnitude, 0.01f);
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            t.localScale = new Vector3(radius * 2f, (len + radius * 2f) * 0.5f, radius * 2f);
        }

        /// <summary>Knee or elbow point for a two-part limb of total length L, bending toward 'bend'.</summary>
        static Vector3 Joint(Vector3 a, Vector3 b, float L, Vector3 bend)
        {
            float half = Vector3.Distance(a, b) * 0.5f, seg = L * 0.5f;
            float out_ = Mathf.Sqrt(Mathf.Max(0f, seg * seg - half * half));
            return (a + b) * 0.5f + bend * out_;
        }

        public void Update(Rider r, float dt, float time)
        {
            r.UpdateSprings(dt);
            root.position = r.pos;
            root.rotation = Quaternion.Euler(r.pitch * Mathf.Rad2Deg, r.heading * Mathf.Rad2Deg, r.lean * Mathf.Rad2Deg);
            if (r.crashed)
            {
                spinG.localRotation = Quaternion.Euler(0f, (r.spin + r.tumbleRot.y) * Mathf.Rad2Deg, 0f);
                flipG.localRotation = Quaternion.Euler((r.flip + r.tumbleRot.x) * Mathf.Rad2Deg, 0f, r.tumbleRot.z * Mathf.Rad2Deg);
            }
            else
            {
                spinG.localRotation = Quaternion.Euler(0f, r.spin * Mathf.Rad2Deg, 0f);
                flipG.localRotation = Quaternion.Euler(r.flip * Mathf.Rad2Deg, 0f, 0f);
            }
            flipG.localScale = new Vector3(1f - r.squash * 0.5f, 1f + r.squash, 1f - r.squash * 0.5f);

            // knees: athletic stance, deeper when tucking, grabbing or soaking up a landing
            bool air = !r.grounded && !r.crashed;
            float target = 0.28f + (r.tuck && !air ? 0.35f : 0f) + (r.grab != null ? 0.75f : 0f) + (air && r.airT > 0.15f ? 0.25f : 0f)
                           + Mathf.Clamp01((r.speed - 25f) / 15f) * 0.15f;
            crouchV += (target - crouch) * 120f * dt - r.hipKick * 1.6f;
            r.hipKick = 0f;
            crouchV *= Mathf.Exp(-12f * dt);
            crouch = Mathf.Clamp(crouch + crouchV * dt, -0.25f, 1.15f);
            sway = Mathf.Lerp(sway, r.grounded ? r.steer : 0f, 1f - Mathf.Exp(-dt * 6f));

            float hipY = 0.95f - crouch * 0.36f;
            // carving: hips drive toward the toe or heel edge
            Vector3 pelvis = new Vector3(0.04f + crouch * 0.08f - sway * 0.08f, hipY, 0f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 hip = pelvis + new Vector3(0f, 0f, i == 0 ? 0.12f : -0.12f);
                Vector3 knee = Joint(Foot[i], hip, 0.98f, new Vector3(1f, 0f, i == 0 ? 0.25f : -0.25f).normalized);
                Limb(thigh[i], hip, knee, 0.085f);
                Limb(shin[i], knee, Foot[i] + Vector3.up * 0.05f, 0.075f);
            }
            float bow = 0.16f + crouch * 0.22f;
            Vector3 chest = pelvis + new Vector3(bow, 0.52f - crouch * 0.05f, -sway * 0.05f);
            Limb(torso, pelvis, chest, 0.17f);
            head.localPosition = chest + new Vector3(0.05f, 0.22f, 0.02f);
            // head turns to look down the hill past the front shoulder
            head.localRotation = Quaternion.Euler(0f, -60f, 0f);

            Vector3 sF = chest + new Vector3(-0.02f, -0.04f, 0.2f), sB = chest + new Vector3(-0.02f, -0.04f, -0.2f);
            float bob = Mathf.Sin(time * 2.3f) * 0.03f;
            Vector3 hF = sF + new Vector3(0.18f, -0.35f + bob, 0.32f + sway * 0.1f);
            Vector3 hB = sB + new Vector3(0.05f - sway * 0.1f, -0.4f - bob, -0.32f);
            if (air && r.airT > 0.1f) { hF = sF + new Vector3(0.1f, 0.05f, 0.45f); hB = sB + new Vector3(0f, 0.0f, -0.45f); }
            switch (r.grab)
            {
                case "indy": hB = new Vector3(0.17f, 0.09f, -0.04f); break;
                case "method": hF = new Vector3(-0.17f, 0.09f, 0.06f); break;
                case "tail": hB = new Vector3(0f, 0.1f, -0.7f); break;
                case "nose": hF = new Vector3(0f, 0.1f, 0.7f); break;
            }
            Limb(arm[0], sF, hF, 0.065f);
            Limb(arm[1], sB, hB, 0.065f);

            trail.emitting = r.grounded && !r.crashed;
            bool blink = r.invuln > 0f && !r.crashed && ((int)(time / 0.09f)) % 2 == 1;
            foreach (var rd in renderers) rd.enabled = !blink;
        }
    }
}
