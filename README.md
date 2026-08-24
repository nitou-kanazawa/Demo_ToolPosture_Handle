# Tool Posture Handle

溶接トーチ / 切削工具の**ツール姿勢**を、Unity のランタイム上でギズモ操作するための実装です。

エディタ専用の `UnityEditor.Handles` に頼らないので、ビルドしたアプリの中でそのまま姿勢を編集できます。
描画は頂点カラーメッシュの手続き生成、当たり判定はハンドルごとのコライダー、
ドラッグはワールドのレイだけで完結します。

![gizmo](Assets/Screenshots/toolposture_zyx.png)

- Unity 6000.4 / Input System 1.19（Input System 専用設定）
- **Built-in RP / URP のどちらでも動作**（パッケージ側に URP 依存はありません）

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

溶接規格の狙い角 / 進行角は工具軸から導出できます。保持はしません。

```
w = atan2(X·L, X·N)     狙い角     AWS work angle   / CAM tilt angle
t = atan2(X·M, X·N)     進行角  AWS travel angle / CAM lead angle
```

`ToolPostureAngles.WorkAngleDeg` / `TravelAngleDeg` で読み書きできます。
AWS 表記でやり取りしたい場合に使います。**可動範囲は持ちません**（後述）。

日本語の呼称は AWS travel angle = **進行角**（前進角 push / 後退角 drag はその符号の呼び分け）、
AWS work angle = **狙い角**（作業角）です。

### 極の扱い

φ = 90°（工具軸が N に一致）では「どちら向きに倒れているか」という情報が姿勢の中に存在せず、
θ は工具軸に影響しなくなります。この実装は **θ をそのまま保持し続ける**ので、
垂直を経由しても旋回角は失われません。

さらに **φ は 90° を超えてよい**設計です。θ を固定したまま φ を 90° より大きくすると
工具軸は反対側へ倒れるため、垂直をまたぐドラッグでも θ が 180° 飛びません。
一意な値が必要なときは `Normalize()` を明示的に呼びます。

---

## ハンドル

保持している量（θ, φ, spin）に 1 対 1 で対応する 3 つと、直接操作用のボールです。

| ハンドル | 乗る平面 | 編集する量 |
|---|---|---|
| 傾斜角の円弧 | N と現在の工具軸（θ に追従） | φ（N からの傾き α = 90 − φ） |
| 旋回リング | LM 平面（母材面に水平） | θ |
| スピンリング | 工具軸に垂直 | spin |
| 軸先端ボール | 球面 | 工具軸を直接（θ と φ を同時に） |

**投影角 w / t にはハンドルも可動範囲もありません。**θ と φ で工具軸が一意に決まるので
投影角の円弧は冗長でしたし、可動範囲も同様です。

以前は w / t に範囲を持たせ、そこから方位ごとの傾斜上限を逆算していました。
すると **傾斜を縛っているのが別の欄で、しかも方位によって変わる**ため、
「傾斜の可動範囲を広げても動く範囲が変わらない」という追いにくい状態になります
（実際に踏みました）。可動範囲は**保持している角にだけ**持たせています。

- 表示 / 非表示を個別に切替可能（実行中は `1`〜`4` キー）
- ドラッグ中は操作中のハンドル以外を隠す（コライダーも一緒に切れる）
- 各角に **0 度位置 / 回転方向 / 可動範囲 / スナップ幅** をカスタムできる `AngleConvention`
- マウス / タッチ / ペンに対応（タッチは当たり判定を自動的に太くする）

### 当たり判定は円弧に巻いたチューブ

ハンドルごとにコライダーを持ちます。円弧・リングには**円弧に沿ったトーラス**、
軸先端には `SphereCollider` を割り当てます。

断面が円なので、**スクリーン上のシルエット幅が視線角度によらず一定**です。
平面内で半径方向のずれを見る方式だと、母材面を浅い角度から見たときに
掴み幅が `sin(入射角)` で潰れて実質掴めなくなります（旋回リングは母材面そのものに
乗っているので、溶接の作業視点では常時これを踏みます）。

母材面に寝た旋回リングの線上を一周狙って、外せるまでのピクセル数を実測した値です。

| 母材面からの視線仰角 | 平面内で半径方向（旧） | チューブ（現行） |
|---:|---:|---:|
| 90° | 12.75px | 10.0px |
| 45° | 8.25px | 8.0px |
| 20° | 3.75px | 7.5px |
| 10° | 1.75px | 7.8px |
| 5° | 0.75px | 7.8px |
| 2° | 0.25px | 7.8px |
| 1° | **0.00px**（掴めない） | 7.8px |

同じ条件で、全ハンドルを表示したままリングの線上を狙って
**別のハンドルを掴んでしまう**点の数（72 点中）:

| 仰角 | 旧（6 ハンドル・平面内判定） | 新（4 ハンドル・チューブ） |
|---:|---:|---:|
| 90° | 0 | 0 |
| 45° | 7 | **5** |
| 20° | 15 | **2** |
| 10° | 10 | **0** |
| 5° | 7 | **0** |

コライダーが実際の 3D 面までの距離を返すので、重なったハンドルの前後関係が正しく決まります。
投影角のハンドル 2 つを削ったぶん、そもそも重なる相手も減っています。

残る誤爆は、**当たり判定のチューブが描画（8px 幅の帯）より太い**ことによるものです。
隣のハンドルのチューブが手前にあると、描画上は重なって見えなくてもそちらが勝ちます。
`hitPixelWidth` を下げれば減りますが、その分どの角度でも掴みにくくなります。

コライダーは **Ignore Raycast レイヤー**に置き、判定には `Physics.Raycast` ではなく
`Collider.Raycast` を各コライダーへ直接撃ちます。シーンクエリに一切参加しないので、
アプリ側が干渉チェック等で回している raycast を汚しません。

### コライダーのデバッグ表示

当たり判定は目に見えないので、`colliderGizmo` で Gizmos に出せます
（Game View で見るには Gizmos の表示を有効にしてください）。

| モード | 内容 |
|---|---|
| `Off`（既定） | 描かない |
| `Outline` | チューブの稜線 4 本と断面円。軽くて形が読みやすく、再生していなくても出る |
| `Wireframe` | 実際のコライダーメッシュそのもの。面取りまで見える |

今は掴めないハンドル（非表示、ドラッグ中に隠れているもの）は別の色で描かれます。

> ギズモ本体は Gizmos ではなく `Graphics.RenderMesh` による通常の描画なので、
> Gizmos トグルとは無関係に常に出ます。トグルが効くのはこのコライダー表示だけです。

### 回転ドラッグはレイの接線投影

掴んだ時点で「掴んだ点」と「その点の接線」を固定し、以降はレイと接線直線の
最近接点だけを見ます。`UnityEditor.Handles` と同じ考え方を、スクリーン座標ではなく
ワールドのレイで行う版です。**カメラにもスクリーン座標にも依存しません。**

レイが接線と平行に近づくと最近接点が発散するので、`sin²(なす角) < 0.02`（約 8°）の
間は値の更新を止めて直前の値を保ちます。

---

## プリセット（ScriptableObject）

インスペクタが 45 フィールドまで膨れていたので、使い回したいものを 2 枚のアセットへ出しました。
コンポーネントに残るのは 18 フィールドです。

| アセット | 中身 | 切り替えたい単位 |
|---|---|---|
| `GizmoTheme` | 色 10 + 太さ 15 + 配置 7 + 当たり判定 4 + デバッグ色 2 | 見た目・入力デバイス |
| `ToolPostureProfile` | 角度規約 3（θ / φ / spin） + spin 基準 | 工程・開先形状・材料 |

寸法と配置は**描画に出てくる値を全部**出してあります（ハードコードは残っていません）。

**太さ [px]** — `gizmoPixelSize` を基準に画面上の大きさで指定します。

| | 項目 |
|---|---|
| 全体の大きさ | `gizmoPixelSize` |
| 円弧 | `arcPixelWidth` / `fallbackArcHalfWidthDeg` |
| 軸と矢印 | `frameAxisPixelWidth` / `toolAxisPixelWidth` / `arrowHeadPixelRadius` / `toolArrowHeadPixelRadius` / `arrowHeadPixelLength` / `originDotPixelRadius` |
| 目盛りと破線 | `tickPixelLength` / `limitTickPixelLength` / `tickPixelWidth` / `dashPixelLength` / `thinPixelWidth` |
| ノブと先端 | `knobPixelRadius` / `tipPixelRadius` |

**配置** — `gizmoPixelSize` に対する比率で指定します。

| | 項目 |
|---|---|
| 工具軸 | `toolAxisLengthRatio`（軸先端ボールもこの位置に乗る） |
| フレーム軸 | `frameAxisLengthRatio`（M / N）・`crossFeedAxisLengthRatio`（L） |
| 円弧・リング | `tiltArcRadiusRatio` / `azimuthRingRadiusRatio` / `spinRingRadiusRatio` |
| スピンリングの位置 | `spinRingOffsetRatio`（工具軸に沿ってどこに置くか） |

当たり判定の太さを `GizmoTheme` に入れてあるのは、タッチ向けに「太いチューブ + 大きいノブ」を
まとめて切り替えたいことが多く、別アセットに分けると 2 枚を必ず対で差し替える運用になるためです。

**どちらも未設定で動きます。**`Theme` / `Profile` プロパティは未設定なら組み込み既定
（`CreateInstance` した静的インスタンス）を返すので、アセットを 1 つも作らずに使い始められます。

```csharp
gizmo.Theme = touchTheme;      // 実行中に差し替えれば次のフレームから反映される
gizmo.Profile = narrowGroove;
```

同梱のサンプル: `Assets/ToolPosture/Presets/`

- `GizmoTheme_Touch` … チューブ・ノブ・軸をまとめて太く
- `GizmoTheme_HighContrast` … 屋外・明るい背景向けに彩度と不透明度を上げたもの
- `ToolPostureProfile_NarrowGroove` … 傾斜角を 65〜90° に絞り、スナップを 1° に

> アセットなので、**実行中に中身を書き換えるとエディタでは永続化されます。**
> ギズモ側は読むだけです。インスタンスごとに変えたい場合は `Instantiate` してから差し替えてください。

インスペクタは折りたたみ式にしてあり、割り当てたアセットの中身をその場で編集できます。
未設定の欄では「新規」ボタンからアセットを作って割り当てられます。

---

## `ToolPostureGizmo` の責務

このコンポーネントは「**与えられた 1 つのフレームに対して工具軸 X と軸まわりの回転を定める**」
ことだけを行います。フレームをどこから持ってくるか（経路の補間、区間の選択、法線の直交化）は
関知せず、`Frame` へ代入されたものをそのまま使います。

```csharp
gizmo.Frame = myPath.GetFrame(segment, u);   // 誰が計算してもよい
```

描画の直前に呼ばれる `PreparingFrame` フックからも渡せます。編集中はエディタが
`Update` を回さないことがあり、そこだけに頼るとギズモがフォールバック位置に出てしまうので、
供給元はこちらにも繋いでおくのが確実です。

```csharp
gizmo.PreparingFrame += g => g.Frame = myPath.GetFrame(segment, u);
```

デモでは `WeldPath` が両方を担当します（`[DefaultExecutionOrder(-100)]` でギズモより先に走る）。

### Quaternion はギズモから出さない

ギズモの出力は**工具軸ベクトル X と角度まで**です。そこから回転を組むには
「向けたい対象のどのローカル軸を工具軸に合わせるか」という**対象側の都合**が要り、
その値はロボットのフランジと工具モデルで別物になります。
なのでギズモが 1 組だけ抱える形にはせず、対象を持っている側が Core の関数を呼びます。

```csharp
Quaternion r = gizmo.Angles.GetToolRotation(
    gizmo.Frame, shaftAxis, referenceAxis, gizmo.Profile.spinReference);
```

工具モデルを追従させるだけなら `ToolPostureFollower` を付けます
（`shaftAxis` / `referenceAxis` はモデルのローカル軸なので、このコンポーネントが持ちます）。
ロボット側から姿勢が降ってくる場合は `ApplyRotation(Quaternion)` で逆算して書き戻せます。

### 描画はカメラの描画コールバックから投入される

`Graphics.RenderMesh` は 1 フレーム限りの投入なので、`LateUpdate` から呼ぶと
「エディタがティックしていないが再描画はされる」状況（編集中の Game View / Scene View）で
何も出なくなります。`Camera.onPreCull`（Built-in RP）と
`RenderPipelineManager.beginCameraRendering`（URP / HDRP）の両方に繋いであるので、
再生していなくても必ず描画されます。

## スクリプトからの操作

操作の入口は**ワールドのレイ**です。アプリが独自の 2D→3D 変換を持つ場合
（実写重畳ビューなど）は、そこで作ったレイをそのまま渡します。
当たり判定はワールド上のコライダーなので、ギズモ側に投影を教える必要はありません。

```csharp
gizmo.inputMode = GizmoInputMode.External;   // 自前の入力読みを止める

Ray ray = myApp.ScreenPointToRay(touchPos);  // アプリ側の 2D -> 3D 変換

if (gizmo.TryPick(ray, out GizmoHandleId id, out Vector3 point))
    gizmo.BeginDrag(id, ray, point);         // 掴んだ点を渡すと掴み位置が正確になる

gizmo.UpdateDrag(ray, snap);
gizmo.EndDrag();   /   gizmo.CancelDrag();
gizmo.UpdateHover(ray);

gizmo.SetSpherical(azimuthDeg, elevationDeg);
gizmo.SetAngleDisplay(id, displayDeg);       // 規約を通した表示値で与える
gizmo.SetToolAxisWorld(direction);           // 工具軸を直接与える

gizmo.ToolAxisWorld;   // 主たる出力
gizmo.Angles;          // theta / phi / spin
gizmo.PostureChanged;  // 姿勢が変わったときのイベント
```

アプリ側で既に raycast を撃っている場合は、その `Collider` から引けます
（ギズモのコライダーは Ignore Raycast レイヤーなので、拾うにはマスクに含めること）。

```csharp
if (gizmo.TryResolve(hit.collider, out GizmoHandleId id))
    gizmo.BeginDrag(id, ray, hit.point);
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
| ハンドル表示切替 | `1`〜`4` または画面のボタン |
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
                    ZyxEulerAngles / SpinReference      ← 依存ゼロの別アセンブリ
                    ToolPostureProfile          角度規約と可動範囲 (SO)
  Runtime/Path/     WeldPath                    経路。フレームを計算してギズモへ渡す
  Runtime/Tool/     ToolPostureFollower         工具モデルを姿勢に追従させる
  Runtime/Gizmo/    ToolPostureGizmo            ギズモ本体・レイでの操作 API
                    GizmoTheme                  見た目と当たりの太さ (SO)
                    GizmoHandles                各ハンドル（値の読み書きと形状）
                    GizmoHandleShape            円弧 / 球の形状記述
                    GizmoHandleColliders        チューブコライダーの生成と追従
                    RayTangentDrag              レイの接線投影による回転ドラッグ
                    GizmoMeshBuilder / GizmoPointer / IGizmoPointerSource
  Runtime/UI/       PostureReadoutUI            数値表示・表示切替
  Runtime/Demo/     OverlayViewDemo / DistortedOverlayViewport / RobotPostureBridge
                    OrbitCamera / WeldPathSurface
  Presets/          GizmoTheme_Touch / GizmoTheme_HighContrast
                    ToolPostureProfile_NarrowGroove
  Editor/           ToolPostureGizmoEditor      シーンビュー版 + 折りたたみインスペクタ
  Tests/EditMode/   87 件
```

EditMode テストは Test Runner、または `unity test --mode EditMode --output result.xml --timeout 300` で実行できます。

## ライセンス

MIT
