using UnityEngine;
using UnityEngine.InputSystem;

namespace ToolRuntimeGizmos.Gizmo
{
    /// <summary>
    /// ギズモへポインタ入力を供給する側のインターフェース。
    ///
    /// 既定はデバイス (Mouse / Touchscreen / Pen) を直接読む DevicePointerSource。
    /// アプリが独自の InputActionAsset や入力抽象を持っている場合は、これを実装して
    /// ToolPostureGizmo.PointerSource に差し込めば、押下・ドラッグの状態遷移は
    /// ギズモ側の実装をそのまま使える。
    ///
    /// 画面座標の生成までアプリ側で行いたい場合は、そもそも
    /// GizmoInputMode.External にして TryPick / BeginDrag / UpdateDrag / EndDrag を
    /// 直接呼ぶ方が素直。
    /// </summary>
    public interface IGizmoPointerSource
    {
        /// <summary>
        /// このフレームのポインタ状態を返す。有効な入力が無ければ false。
        /// </summary>
        bool TryRead(out PointerSample sample);
    }

    /// <summary>
    /// Mouse / Touchscreen / Pen を直接読む既定の供給元。
    /// </summary>
    internal class DevicePointerSource : IGizmoPointerSource
    {
        public static readonly DevicePointerSource Default = new DevicePointerSource();

        public bool TryRead(out PointerSample sample) => GizmoPointer.TryRead(out sample);
    }

    /// <summary>
    /// InputAction からポインタ状態を組み立てる供給元。
    /// アプリの InputActionAsset にある「位置」と「押下」のアクションをそのまま渡す。
    ///
    /// <code>
    /// gizmo.PointerSource = new InputActionPointerSource(
    ///     actions.FindAction("UI/Point"),
    ///     actions.FindAction("UI/Click"));
    /// </code>
    /// </summary>
    internal class InputActionPointerSource : IGizmoPointerSource
    {
        private readonly InputAction _point;
        private readonly InputAction _press;

        /// <summary>
        /// 押下アクションのバインド元がタッチかどうかで当たり判定の広さを切り替えるか。
        /// </summary>
        public bool DetectTouchFromDevice = true;

        public InputActionPointerSource(InputAction pointAction, InputAction pressAction)
        {
            _point = pointAction;
            _press = pressAction;
        }

        public bool TryRead(out PointerSample sample)
        {
            sample = default;
            if (_point == null || _press == null) return false;
            if (!_point.enabled || !_press.enabled) return false;

            bool isTouch = false;
            if (DetectTouchFromDevice)
            {
                InputControl control = _press.activeControl ?? _point.activeControl;
                isTouch = control != null && control.device is Touchscreen;
            }

            sample.valid = true;
            sample.position = _point.ReadValue<Vector2>();
            sample.isDown = _press.IsPressed();
            sample.pressedThisFrame = _press.WasPressedThisFrame();
            sample.releasedThisFrame = _press.WasReleasedThisFrame();
            sample.isTouch = isTouch;

            // EventSystem 上のポインタ ID。タッチの ID までは分からないので
            // マウス扱い (-1) にしておく。UI との重なり判定を厳密にしたい場合は
            // アプリ側で External モードにして ID を渡すこと。
            sample.pointerId = -1;
            return true;
        }
    }
}
