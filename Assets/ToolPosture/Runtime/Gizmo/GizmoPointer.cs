using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// 1 回分のポインタ入力。マウス / タッチ / ペンを同じ形で扱う。
    /// </summary>
    public struct PointerSample
    {
        public bool valid;
        public Vector2 position;
        public bool isDown;
        public bool pressedThisFrame;
        public bool releasedThisFrame;
        public bool isTouch;

        /// <summary>
        /// EventSystem.IsPointerOverGameObject に渡す ID。マウス / ペンは -1。
        /// </summary>
        public int pointerId;
    }

    /// <summary>
    /// 入力デバイスの差を吸収するヘルパ。
    ///
    /// Input System ではタッチはマウスに合成されない (Touchscreen は独立したデバイス) ため、
    /// Mouse.current だけを読むとタッチに一切反応しない。ここで一本化する。
    /// </summary>
    public static class GizmoPointer
    {
        /// <summary>
        /// マウス / ペンの EventSystem 上のポインタ ID (PointerInputModule.kMouseLeftId)。
        /// </summary>
        private const int MousePointerId = -1;

        #region ポインタ取得

        /// <summary>
        /// 現在のポインタを 1 つ返す。接触中のタッチがあればタッチを優先し、
        /// 無ければマウス、さらに無ければペンを使う。
        /// </summary>
        public static bool TryRead(out PointerSample sample)
        {
            sample = default;

            Touchscreen ts = Touchscreen.current;
            if (ts != null)
            {
                TouchControl t = ts.primaryTouch;
                bool down = t.press.isPressed;
                bool pressed = t.press.wasPressedThisFrame;
                bool released = t.press.wasReleasedThisFrame;

                if (down || pressed || released)
                {
                    sample.valid = true;
                    sample.position = t.position.ReadValue();
                    sample.isDown = down;
                    sample.pressedThisFrame = pressed;
                    sample.releasedThisFrame = released;
                    sample.isTouch = true;
                    sample.pointerId = t.touchId.ReadValue();
                    return true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                sample.valid = true;
                sample.position = mouse.position.ReadValue();
                sample.isDown = mouse.leftButton.isPressed;
                sample.pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
                sample.releasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
                sample.isTouch = false;
                sample.pointerId = MousePointerId;
                return true;
            }

            Pen pen = Pen.current;
            if (pen != null)
            {
                sample.valid = true;
                sample.position = pen.position.ReadValue();
                sample.isDown = pen.tip.isPressed;
                sample.pressedThisFrame = pen.tip.wasPressedThisFrame;
                sample.releasedThisFrame = pen.tip.wasReleasedThisFrame;
                sample.isTouch = false;
                sample.pointerId = MousePointerId;
                return true;
            }

            return false;
        }

        #endregion

        #region タッチ

        /// <summary>
        /// 接触中のタッチ数。
        /// </summary>
        public static int ActiveTouchCount()
        {
            Touchscreen ts = Touchscreen.current;
            if (ts == null) return 0;

            int n = 0;
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
                if (IsActive(touches[i])) n++;
            return n;
        }

        /// <summary>
        /// 接触中のタッチのうち index 番目の位置と移動量。
        /// </summary>
        public static bool TryGetActiveTouch(int index, out Vector2 position, out Vector2 delta, out int touchId)
        {
            position = default;
            delta = default;
            touchId = 0;

            Touchscreen ts = Touchscreen.current;
            if (ts == null) return false;

            int n = 0;
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (!IsActive(touches[i])) continue;
                if (n == index)
                {
                    position = touches[i].position.ReadValue();
                    delta = touches[i].delta.ReadValue();
                    touchId = touches[i].touchId.ReadValue();
                    return true;
                }
                n++;
            }
            return false;
        }

        private static bool IsActive(TouchControl t)
        {
            UnityEngine.InputSystem.TouchPhase phase = t.phase.ReadValue();
            return phase == UnityEngine.InputSystem.TouchPhase.Began
                || phase == UnityEngine.InputSystem.TouchPhase.Moved
                || phase == UnityEngine.InputSystem.TouchPhase.Stationary;

        #endregion

        }
    }
}
