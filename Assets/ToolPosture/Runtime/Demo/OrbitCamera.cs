using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using ToolPosture.Gizmo;

namespace ToolPosture.Demo
{
    /// <summary>
    /// デモ用の視点操作。
    ///
    /// マウス: 左ドラッグはギズモ操作に使うので、視点回転は右ドラッグ、パンは中ドラッグ。
    /// タッチ: 1 本指で視点回転 (ギズモを掴んでいる間と UI の上では無効)、2 本指でピンチズーム + パン。
    /// </summary>
    [AddComponentMenu("Tool Posture/Orbit Camera")]
    public class OrbitCamera : MonoBehaviour
    {

        #region 設定とライフサイクル

        public Transform target;
        public float distance = 4.5f;
        public float minDistance = 0.5f;
        public float maxDistance = 40f;
        public float yaw = 40f;
        public float pitch = 26f;

        [Header("感度")]
        public float orbitSpeed = 0.22f;
        public float touchOrbitSpeed = 0.16f;
        public float panSpeed = 0.0022f;
        public float zoomStep = 0.12f;
        public float pinchZoomSpeed = 0.004f;

        private Vector3 _panOffset;
        private float _pinchPrevDistance = -1f;
        private Vector3 _frozenPivot;
        private bool _pivotFrozen;

        private Vector3 RawPivot => (target != null ? target.position : Vector3.zero) + _panOffset;

        /// <summary>
        /// 注視点。ドラッグ中は掴んだ瞬間の位置で固定する。
        ///
        /// 注視対象が「掴んでいる物」に追従していると、動かす -> カメラが追う ->
        /// レイの原点がずれて最近接点が進む -> さらに動く、という正のフィードバックになり、
        /// 指を止めていても走り続ける (実測: 1 フレームあたり 0.0105 で発散)。
        /// </summary>
        private Vector3 Pivot => _pivotFrozen ? _frozenPivot : RawPivot;

        private void OnEnable()
        {
            _panOffset = Vector3.zero;
            _pivotFrozen = false;
            Apply();
        }

        private void LateUpdate()
        {
            UpdatePivotFreeze();
            if (!HandleTouch()) HandleMouse();
            Apply();
        }

        private void UpdatePivotFreeze()
        {
            bool dragging = RuntimeGizmo.AnyDragging;
            if (dragging == _pivotFrozen) return;

            if (dragging) _frozenPivot = RawPivot;
            _pivotFrozen = dragging;
        }

        #endregion

        #region タッチ

        /// <summary>
        /// タッチで操作したなら true。マウス処理はスキップする。
        /// </summary>
        private bool HandleTouch()
        {
            int count = GizmoPointer.ActiveTouchCount();
            if (count == 0)
            {
                _pinchPrevDistance = -1f;
                return false;
            }

            if (count >= 2)
            {
                HandlePinch();
                return true;
            }

            _pinchPrevDistance = -1f;

            // ギズモを掴んでいる間、および UI の上では視点を動かさない
            if (RuntimeGizmo.AnyDragging) return true;
            if (!GizmoPointer.TryGetActiveTouch(0, out _, out Vector2 delta, out int touchId)) return true;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touchId)) return true;

            yaw += delta.x * touchOrbitSpeed;
            pitch = Mathf.Clamp(pitch - delta.y * touchOrbitSpeed, -85f, 85f);
            return true;
        }

        private void HandlePinch()
        {
            if (!GizmoPointer.TryGetActiveTouch(0, out Vector2 p0, out Vector2 d0, out _)) return;
            if (!GizmoPointer.TryGetActiveTouch(1, out Vector2 p1, out Vector2 d1, out _)) return;

            float spread = Vector2.Distance(p0, p1);

            if (_pinchPrevDistance > 0f)
            {
                float change = _pinchPrevDistance - spread;
                distance = Mathf.Clamp(distance * (1f + change * pinchZoomSpeed), minDistance, maxDistance);

                Vector2 average = (d0 + d1) * 0.5f;
                _panOffset += (transform.right * -average.x + transform.up * -average.y) * (panSpeed * distance);
            }

            _pinchPrevDistance = spread;
        }

        #endregion

        #region マウス

        private void HandleMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue();

            if (mouse.rightButton.isPressed)
            {
                yaw += delta.x * orbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, -85f, 85f);
            }

            if (mouse.middleButton.isPressed)
                _panOffset += (transform.right * -delta.x + transform.up * -delta.y) * (panSpeed * distance);

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance * (1f - Mathf.Sign(scroll) * zoomStep), minDistance, maxDistance);
        }

        // ------------------------------------------------------------------

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(Pivot - rot * Vector3.forward * distance, rot);
        }

        [ContextMenu("ターゲットに合わせる")]
        public void FrameTarget()
        {
            _panOffset = Vector3.zero;
            Apply();
        }

        #endregion
    }
}
