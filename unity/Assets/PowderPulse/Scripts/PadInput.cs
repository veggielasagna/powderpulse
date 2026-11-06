using UnityEngine;
using UnityEngine.InputSystem;

namespace PowderPulse
{
    /// <summary>One frame of controls. Xbox pad first, keyboard for testing on the Mac.</summary>
    public struct PadState
    {
        public float steer;      // -1 left .. +1 right
        public float vert;       // -1 down .. +1 up (stick up = frontflip)
        public bool tuck;        // RT / Shift
        public bool jumpEdge;    // A / Space, pressed this frame
        public string grab;      // X/B/LB/RB held: indy, method, tail, nose
        public bool pauseEdge;   // Menu / Esc
        public bool nextEdge;    // Y / N: next song
        public bool xEdge;       // X / H (pause menu: shuffle)
        public bool bEdge;       // B / F (pause menu: find my music)
        public bool aEdge;       // A / Enter (pause menu: resume)
    }

    public static class PadInput
    {
        static float Dead(float v) => Mathf.Abs(v) < 0.2f ? 0f : (v - Mathf.Sign(v) * 0.2f) / 0.8f;

        public static PadState Read()
        {
            var s = new PadState();
            var gp = Gamepad.current;
            var kb = Keyboard.current;
            if (gp != null)
            {
                Vector2 st = gp.leftStick.ReadValue();
                s.steer = Dead(st.x);
                s.vert = Dead(st.y);
                s.tuck = gp.rightTrigger.ReadValue() > 0.4f;
                s.jumpEdge = gp.buttonSouth.wasPressedThisFrame;
                s.pauseEdge = gp.startButton.wasPressedThisFrame;
                s.nextEdge = gp.buttonNorth.wasPressedThisFrame;
                s.xEdge = gp.buttonWest.wasPressedThisFrame;
                s.bEdge = gp.buttonEast.wasPressedThisFrame;
                s.aEdge = gp.buttonSouth.wasPressedThisFrame;
                if (gp.buttonWest.isPressed) s.grab = "indy";
                else if (gp.buttonEast.isPressed) s.grab = "method";
                else if (gp.leftShoulder.isPressed) s.grab = "tail";
                else if (gp.rightShoulder.isPressed) s.grab = "nose";
            }
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) s.steer -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) s.steer += 1f;
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) s.vert += 1f;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) s.vert -= 1f;
                s.tuck |= kb.leftShiftKey.isPressed;
                s.jumpEdge |= kb.spaceKey.wasPressedThisFrame;
                s.pauseEdge |= kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
                s.nextEdge |= kb.nKey.wasPressedThisFrame;
                s.xEdge |= kb.hKey.wasPressedThisFrame;
                s.bEdge |= kb.fKey.wasPressedThisFrame;
                s.aEdge |= kb.enterKey.wasPressedThisFrame;
                if (s.grab == null)
                {
                    if (kb.jKey.isPressed) s.grab = "indy";
                    else if (kb.kKey.isPressed) s.grab = "method";
                    else if (kb.uKey.isPressed) s.grab = "tail";
                    else if (kb.iKey.isPressed) s.grab = "nose";
                }
            }
            s.steer = Mathf.Clamp(s.steer, -1f, 1f);
            s.vert = Mathf.Clamp(s.vert, -1f, 1f);
            return s;
        }
    }
}
