using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Showcase.Runtime
{
    // Input bridge between the on-screen HUD (virtual sticks, built by
    // ShowcaseModeHud) and the walker. The HUD writes stick deflections here;
    // FirstPersonController reads them every frame alongside keyboard/mouse.
    //
    // This is the whole "mobile-friendly but non-conflicting" contract: on
    // touch devices camera motion comes ONLY from these explicit on-screen
    // sticks (which live on a higher-sorting screen panel and capture their
    // pointer), so touching an exhibit never swings the camera, and using a
    // stick never leaks a press into an exhibit. We deliberately do NOT do
    // free drag-look on touch — a drag that starts on a world panel already
    // belongs to that panel's slider / scroll view.
    public static class WorldNavInput
    {
        public static Vector2 Move;   // x = strafe, y = forward; each -1..1
        public static Vector2 Look;   // x = yaw rate, y = pitch rate; each -1..1

        // Whether the touch control scheme should be offered. Touchscreen
        // covers WebGL on phones/tablets (the Input System registers one when
        // the browser reports touch support); isMobilePlatform catches native
        // mobile players even if the device enumerates late.
        public static bool TouchUiLikely =>
            Touchscreen.current != null || Application.isMobilePlatform;

        public static void Reset()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
        }
    }

    // Minimal first-person walker for the world-space corridor gallery.
    //
    // Deliberately physics-free: the corridor floor is dead flat, so a
    // CharacterController + gravity would only add failure modes (tunnelling,
    // stuck-on-seam) for zero benefit. We drive the Transform directly and
    // clamp it inside an axis-aligned box (the corridor interior).
    //
    // Input is read through the Input System package because the project ships
    // with `activeInputHandler: 1` (New Input System only) — the legacy
    // UnityEngine.Input.* API throws InvalidOperationException in that mode.
    //
    // Look model chosen for "walk AND click the panels":
    //   - Cursor stays FREE (no pointer lock) so UI Toolkit's
    //     WorldDocumentRaycaster can turn the mouse's screen position into a
    //     ray and hit the wall panels — clicking a button just works.
    //   - Mouse-look is therefore opt-in: hold RIGHT mouse to swing the view.
    //   - Q / E turn the view from the keyboard so the whole thing is usable
    //     with no mouse-look at all (and on trackpads where drag-look is
    //     awkward). Forward/back is the headline motion the brief asked for;
    //     turning is the assist.
    //   - Touch devices use the HUD's virtual sticks via WorldNavInput.
    [DisallowMultipleComponent]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Movement")]
        public float MoveSpeed      = 6.5f;
        public float SprintMultiplier = 1.9f;
        public float Acceleration   = 12f;   // smoothing toward target velocity
        public float EyeHeight      = 1.65f;

        [Header("Look")]
        public float MouseSensitivity = 0.13f;   // degrees per mouse-pixel
        public float KeyTurnSpeed     = 110f;     // degrees / sec for Q,E
        public float StickLookSpeed   = 150f;     // degrees / sec at full stick deflection
        public float MinPitch = -72f;
        public float MaxPitch = 72f;

        // Corridor interior the walker is confined to (world-space, XZ).
        public Vector2 BoundsX = new Vector2(-3f, 3f);
        public Vector2 BoundsZ = new Vector2(-2f, 90f);

        // Raised by Esc so the owner can drop back to the flat showcase.
        public event Action ExitRequested;

        // When this returns true, movement/turn KEYS are ignored for the frame.
        // The corridor wires it to "a world-panel text field has focus" so
        // typing 'w' into the INPUTS exhibit doesn't walk the camera into the
        // wall. Esc (exit) and the HUD sticks stay live — neither can be a
        // character the user meant to type.
        public Func<bool> SuppressKeys;

        float _yaw;
        float _pitch;
        Vector3 _velocity;   // smoothed horizontal velocity

        // Seed yaw/pitch from the current transform so enabling the controller
        // doesn't snap the camera to a hard-coded facing.
        public void SyncFromTransform()
        {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = NormalizePitch(e.x);
            _velocity = Vector3.zero;
        }

        static float NormalizePitch(float x)
        {
            // Euler X comes back in [0,360); fold the top half to negatives so
            // clamping against MinPitch/MaxPitch behaves.
            if (x > 180f) x -= 360f;
            return x;
        }

        void OnEnable()
        {
            // Re-sync every time we take control (mode toggle re-enables us).
            SyncFromTransform();
        }

        void OnDisable()
        {
            // Don't let a held stick keep steering the restored screen-mode
            // camera transform on the next entry.
            WorldNavInput.Reset();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ExitRequested?.Invoke();
                return;
            }

            bool keysSuppressed = SuppressKeys != null && SuppressKeys();

            UpdateLook(dt, keysSuppressed ? null : kb, mouse);
            UpdateMove(dt, keysSuppressed ? null : kb);
        }

        void UpdateLook(float dt, Keyboard kb, Mouse mouse)
        {
            // Keyboard turn (always available).
            if (kb != null)
            {
                if (kb.eKey.isPressed) _yaw += KeyTurnSpeed * dt;
                if (kb.qKey.isPressed) _yaw -= KeyTurnSpeed * dt;
            }

            // Drag-look while the right button is held. delta is in pixels for
            // the frame; without pointer lock it goes quiet at the screen edge,
            // which is why Q/E exist as the guaranteed fallback.
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw   += d.x * MouseSensitivity;
                _pitch -= d.y * MouseSensitivity;
            }

            // HUD look stick. Quadratic response: small deflections give slow,
            // precise turns (needed to settle on a 2.5 m panel), full tilt
            // reaches StickLookSpeed.
            Vector2 stick = WorldNavInput.Look;
            if (stick.sqrMagnitude > 0.0001f)
            {
                stick *= stick.magnitude;
                _yaw   += stick.x * StickLookSpeed * dt;
                _pitch -= stick.y * StickLookSpeed * dt;
            }

            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void UpdateMove(float dt, Keyboard kb)
        {
            float fwd = 0f, strafe = 0f;
            bool sprint = false;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    fwd    += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  fwd    -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) strafe += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  strafe -= 1f;
                sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }

            // HUD move stick adds on top of keyboard; the sum is clamped to
            // unit length below so mixed input never exceeds normal speed.
            strafe += WorldNavInput.Move.x;
            fwd    += WorldNavInput.Move.y;

            // Move in the XZ plane using yaw only — looking up/down must never
            // launch the walker off the floor.
            Quaternion flat = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 wish = flat * new Vector3(strafe, 0f, fwd);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = MoveSpeed * (sprint ? SprintMultiplier : 1f);
            Vector3 targetVel = wish * speed;
            _velocity = Vector3.Lerp(_velocity, targetVel, 1f - Mathf.Exp(-Acceleration * dt));

            Vector3 p = transform.position + _velocity * dt;
            p.x = Mathf.Clamp(p.x, BoundsX.x, BoundsX.y);
            p.z = Mathf.Clamp(p.z, BoundsZ.x, BoundsZ.y);
            p.y = EyeHeight;
            transform.position = p;
        }
    }
}
