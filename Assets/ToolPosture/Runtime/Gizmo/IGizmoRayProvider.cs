using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// 画面座標をワールドのレイに変換する側。
    ///
    /// 2D 重畳ビューのように投影がカメラと違う場合、この変換だけがアプリ固有になる。
    /// ギズモの当たり判定はワールド上のコライダーなので、正しいレイさえ渡せば
    /// 投影がどうであっても掴める。
    ///
    /// 触れない位置 (画像の外など) では false を返すこと。呼び出し側は
    /// 「新しく掴むのは止めるが、進行中のドラッグは維持する」と解釈する。
    /// </summary>
    public interface IGizmoRayProvider
    {
        bool TryScreenToRay(Vector2 screenPosition, out Ray ray);
    }
}
