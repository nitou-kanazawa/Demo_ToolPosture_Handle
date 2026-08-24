# Tool Posture Handle

溶接トーチ / 切削工具の**ツール姿勢**を、Unity のランタイム上でギズモ操作するための実装です。

エディタ専用の `UnityEditor.Handles` に頼らず、描画・当たり判定・ドラッグ処理をすべて自前で持つため、
ビルドしたアプリの中でそのまま姿勢を編集できます。

![gizmo](Assets/Screenshots/toolposture_zyx.png)

- Unity 6000.4 / URP 17.4 / Input System 1.19（Input System 専用設定）

---

## 姿勢の定義

経路上の各点に正規直交フレーム **(L, M, N)** を張り、そこに対して工具軸 **X** を定めます。

| 記号 | 意味 | 一般名称 |
|---|---|---|
| **M** | 進行方向 `p(i) → p(i+1)` | feed direction / travel direction / 接線 T |
| **N** | 面法線（M に直交化済み） | surface normal |
| **L** | M と N に直交（進行方向の右側） | cross-feed / 従法線 B |

工具軸 **X** は「TCP から工具本体へ向かう向き」＝母材から離れる向きです。

```
X = ( cos φ cos θ,  cos φ sin θ,  sin φ )      … LMN 成分
```

| 角 | 意味 | 一般名称 |
|---|---|---|
| **θ** 旋回角 | LM 平面上で L 軸正方向から測った方位 | tilt direction |
| **φ** 仰角 | LM 平面から測った角。90° で工具軸が N に一致（垂直姿勢） | elevation |
| **spin** | 工具軸まわりの回転 | tool roll / C 軸 |

これは 5 軸加工の工具姿勢制御と同一の構造で、6 番目（軸まわり回転）が
ロボットの **functional redundancy**（ツール軸まわりの 1 自由度ヌル空間）に相当します。

### 投影角（AWS 標準）は導出値

溶接規格の狙い角 / 前進後退角は工具軸から導出できます。保持はしません。

```
w = atan2(X·L, X·N)     狙い角     AWS work angle   / CAM tilt angle
t = atan2(X·M, X·N)     前進後退角  AWS travel angle / CAM lead angle
```

`ToolPostureAngles.WorkAngleDeg` / `TravelAngleDeg` で読み書きできます。

### 極の扱い

φ = 90°（工具軸が N に一致）では「どちら向きに倒れているか」という情報が姿勢の中に存在せず、
θ は工具軸に影響しなくなります。この実装は **θ をそのまま保持し続ける**ので、
垂直を経由しても旋回角は失われません。

さらに **φ は 90° を超えてよい**設計です。θ を固定したまま φ を 90° より大きくすると
工具軸は反対側へ倒れるため、垂直をまたぐドラッグでも θ が 180° 飛びません。
一意な値が必要なときは `Normalize()` を明示的に呼びます。

---

## ハンドル

| ハンドル | 乗る平面 | 編集する量 |
|---|---|---|
| 傾き円弧 | N と現在の工具軸（θ に追従） | φ（N からの傾き α = 90 − φ） |
| 旋回リング | LM 平面（母材面に水平） | θ |
| 前進後退角の円弧 | MN 平面（固定） | t |
| 狙い角の円弧 | LN 平面（固定・任意） | w |
| 軸先端ボール | 球面 | 工具軸を直接 |
| スピンリング | 工具軸に垂直 | spin |

- 表示 / 非表示を個別に切替可能（実行中は `1`〜`5` キー）
- ドラッグ中は操作中のハンドル以外を隠す
- 各角に **0 度位置 / 回転方向 / 可動範囲 / スナップ幅** をカスタムできる `AngleConvention`
- マウス / タッチ / ペンに対応（タッチは当たり判定を自動的に広げる）

### 回転ドラッグは接線投影方式

`UnityEditor.Handles` の回転ギズモや Runtime Transform Gizmos と同じ方式です。
掴んだ時点で「回転中心・掴んだ点・その点の接線」を固定し、スクリーン上のドラッグを
接線へ投影して弧長 → 角度に換算します。

光線とハンドル平面の交点から極角を取る方式は、視線が平面に寝ると破綻します（実測）。

| 視線と平面のなす角 | 光線 × 平面 | 接線投影 |
|---:|---:|---:|
| 90° | 1.02 °/5px | 1.10 °/5px |
| 10° | 2.16 | 1.11 |
| 2° | 16.90 | 1.12 |
| 0.2° | 141.45 ／ 発散 | 1.12 |

---

## スクリプトからの操作

ギズモは Unity の `Camera` に直接依存せず、`IGizmoViewport` だけを見ます。
実写画像への重畳ビューなど、投影がアプリ独自（レンズ歪み補正など）の場合でも差し替えられます。

```csharp
public interface IGizmoViewport
{
    Camera  RenderCamera { get; }
    Vector3 EyePosition  { get; }
    Vector2 PixelSize    { get; }
    Ray     ScreenPointToRay(Vector2 screenPos);
    bool    TryWorldToScreenPoint(Vector3 world, out Vector2 screenPos);
    float   WorldPerPixel(Vector3 world);
}
```

```csharp
gizmo.inputMode = GizmoInputMode.External;   // 自前の入力読みを止める
gizmo.Viewport  = myViewport;                // 投影を差し替え (null で既定へ戻る)

gizmo.TryPick(screenPos, out GizmoHandleId id);
gizmo.BeginDrag(id, screenPos);
gizmo.UpdateDrag(screenPos, snap);
gizmo.EndDrag();   /   gizmo.CancelDrag();

gizmo.SetSpherical(azimuthDeg, elevationDeg);
gizmo.SetAngleDisplay(id, displayDeg);       // 規約を通した表示値で与える
gizmo.SetToolAxisWorld(direction);           // 工具軸を直接与える

gizmo.ToolAxisWorld;   // 主たる出力
gizmo.ToolRotation;    // spin まで含めた完全な姿勢
```

### ZYX オイラー（ロボット姿勢）との相互変換

ロボット制御側がツール姿勢を ZYX オイラー角で持っている場合、双方向に変換できます。

```csharp
// 姿勢 -> 回転 -> ZYX
var euler = ZyxEulerAngles.FromRotation(
    angles.GetToolRotation(frame, shaftAxis, referenceAxis, spinReference));

// ZYX -> 回転 -> 姿勢
angles.SetToolRotation(frame, euler.ToRotation(), shaftAxis, referenceAxis, spinReference);
```

`ZyxEulerAngles` は `R = Rz * Ry * Rx`（内因的 Z→Y'→X''）です。
**Unity の `Quaternion.eulerAngles` は ZXY 順**なので、混同しないよう専用の型にしてあります。
`RollDeg` / `PitchDeg` / `YawDeg` は `zDeg` / `yDeg` / `xDeg` の別名です
（ロボット側で Z 軸回転を Roll と呼ぶ慣習に合わせたもの）。

スピン 0 度の基準は `SpinReference` で選べます。

| モード | 0 度の基準 | 用途 |
|---|---|---|
| `FeedProjected`（既定） | 進行方向 M を工具軸直交面へ投影 | 溶接線に対する相対角 |
| `WorldAxisCross` | `cross(ワールド軸, 工具軸)` | ロボット側がワールド基準で回転角を定義している場合 |

経路が曲がると両者は乖離するので、ロボットと値を突き合わせる場合は必ず合わせてください。

---

## デモシーン

`Assets/Scenes/ToolPostureDemo.unity` を開いて再生します。

| 操作 | 割り当て |
|---|---|
| ハンドル操作 | 左ドラッグ / タッチ |
| スナップ | Ctrl |
| 視点 | 右ドラッグ（回転）・中ドラッグ（パン）・ホイール（ズーム）／ タッチは 1 本指と 2 本指 |
| ハンドル表示切替 | `1`〜`5` または画面のボタン |
| 区間 / 区間内位置 | `←→` / `↑↓` |
| 3D / 2D 操作の切替 | `T` |
| ロボット姿勢 (ZYX) の編集 | 右上パネルの Roll / Pitch / Yaw ボタン |

`T` で切り替わる 2D 重畳ビューは、外部パラから配置した想定の重畳カメラを RenderTexture に描き、
レンズ歪み（Brown-Conrady の k1）を掛けて UI 上に表示します。
その画像上の座標を外部ドライブ API に流すことで、アプリ側が独自の 2D↔3D 変換を持つ場合を再現しています。

---

## 構成

```
Assets/ToolPosture/
  Runtime/Core/     PathFrame / ToolPostureAngles / AngleConvention / IPathFrameSource
                    ZyxEulerAngles / SpinReference
  Runtime/Path/     WeldPath                    経路（点列 + 生の法線）
  Runtime/Gizmo/    ToolPostureGizmo            ギズモ本体・外部ドライブ API
                    GizmoHandles                各ハンドル
                    TangentRotationDrag         接線投影方式の回転ドラッグ
                    IGizmoViewport              画面 ⇔ ワールドの抽象
                    GizmoMeshBuilder / GizmoPicker / GizmoPointer
  Runtime/UI/       PostureReadoutUI            数値表示・表示切替
  Runtime/Demo/     OverlayViewDemo / DistortedOverlayViewport / RobotPostureBridge
                    OrbitCamera / WeldPathSurface
  Editor/           ToolPostureGizmoEditor      シーンビュー版（Handles 使用）
  Tests/EditMode/   73 件
```

EditMode テストは Test Runner、または `unity test --mode EditMode --output result.xml --timeout 300` で実行できます。

## ライセンス

MIT
