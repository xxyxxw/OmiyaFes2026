# 実装報告書 — インタラクティブ・ペイントシューター

> 作成日: 2026-04-19  
> 対象フェーズ: Phase 1〜4（コード先行実装）  
> Unity バージョン: 6000.0.68f1 / URP

---

## 1. 実装完了スクリプト一覧

### Pose システム（ZIG SIM 通信・照準制御）

| ファイル | 役割 | 状態 |
|---|---|---|
| `Scripts/Pose/UdpQuaternionReceiver.cs` | ZIG SIM から UDP/OSC でクォータニオンを受信する | ✅ 完了 |
| `Scripts/Pose/QuaternionCoordinateConverter.cs` | iOS 座標系 → Unity 座標系に変換（静的クラス） | ✅ 完了 |
| `Scripts/Pose/PoseRotationDriver.cs` | ジャイロ値でスポットライト（照準）の向きを制御 | ✅ 完了 |
| `Scripts/Pose/PoseCalibrationCoordinator.cs` | Cキー押下でキャリブレーションを実行 | ✅ 完了 |
| `Scripts/Pose/PoseDebugOverlay.cs` | Dキーでデバッグ情報を画面に表示 | ✅ 完了 |
| `Scripts/Pose/PoseTestBootstrap.cs` | Play Mode 開始時に Pose システムを自動セットアップ | ✅ 完了 |

### ゲームロジック

| ファイル | 役割 | 状態 |
|---|---|---|
| `Scripts/GameStateManager.cs` | Waiting / Playing / Ending の状態管理。Rキー・F11対応 | ✅ 完了 |
| `Scripts/PaintTextureManager.cs` | RenderTexture の初期化・リセット・Renderer への適用 | ✅ 完了 |
| `Scripts/InkColorCycler.cs` | 7色を一定間隔で自動サイクル | ✅ 完了 |
| `Scripts/InkGun.cs` | 自動発射・Raycast・ペイント描画・エフェクト生成 | ✅ 完了 |
| `Scripts/InkBullet.cs` | 飛翔弾の直進移動・寿命管理（物理なし） | ✅ 完了 |
| `Scripts/AimingController.cs` | 照準が画面正面（最大角度内）かを判定 | ✅ 完了 |
| `Scripts/GameUI.cs` | タイマー・色インジケーター・待機メッセージを管理 | ✅ 完了 |

### シェーダー

| ファイル | 役割 | 状態 |
|---|---|---|
| `Shaders/InkPaintShader.shader` | ペイント合成・光沢演出・ブラシスタンプ（URP 対応） | ✅ 完了（要実機検証） |

---

## 2. Unity 内での使用方法（シーン組み立て手順）

### Step 1: シーンの作成

```
File > New Scene > 空のシーンを作成
名前: OmiyaFes2026
保存先: Assets/Scenes/OmiyaFes2026.unity
```

---

### Step 2: Pose システムの組み立て

1. **空の GameObject を作成** → 名前: `PoseSystem`
2. 以下のコンポーネントを `PoseSystem` に追加（Add Component）:

   | コンポーネント | 設定箇所 |
   |---|---|
   | `UdpQuaternionReceiver` | Port: `8000`（ZIG SIM 側と合わせる） |
   | `PoseRotationDriver` | `AimTarget`: 照準用スポットライトの Transform を設定 |
   | `PoseCalibrationCoordinator` | そのまま（Cキー固定） |
   | `PoseDebugOverlay` | `Receiver` と `Driver` フィールドに `PoseSystem` をドラッグ |

3. **スポットライト（照準）を作成**:
   ```
   GameObject > Light > Spot Light
   名前: AimSpotlight
   Position: (0, 5, 0)
   Rotation: (90, 0, 0)
   Spot Angle: 20〜30
   ```
4. `PoseRotationDriver` の **AimTarget** に `AimSpotlight` をドラッグ

> 💡 開発テスト時は `PoseTestBootstrap` コンポーネントを追加するだけでスポットライトが自動生成される。本番シーンでは手動組み立て後に外してよい。

---

### Step 3: キャラクター（ペイントターゲット）のセットアップ

> ⚠️ Blenderモデル完成前は Unity の **Sphere** で代替する

#### Blenderモデルが来た後の手順

1. `Assets/OmiyaFes2026/Models/` に FBX をドラッグ＆ドロップ
2. インポート設定: `Scale Factor = 1` / `Read/Write Enabled = ON`（Raycast UV のため必須）
3. シーンに配置 → **Inspector > Tag** に `PaintTarget` タグを新規作成して付与
4. `Add Component > Mesh Collider` → **Convex のチェックを外す**（チェックが入ると UV が返らない）
5. **マテリアルを URP Lit シェーダーに差し替え**（自動生成されたマテリアルはそのまま使えない）

#### Blenderモデル到着前の暫定（Sphere代替）

```
GameObject > 3D Object > Sphere
名前: MascotDummy
Tag: PaintTarget
Add Component > Mesh Collider（Convex=OFF）
```

---

### Step 4: ゲームシステム GameObject のセットアップ

1. 空 GameObject を作成 → 名前: `GameSystem`
2. 以下のコンポーネントを追加:

   | コンポーネント | 設定 |
   |---|---|
   | `GameStateManager` | Game Duration: `45`, Inactivity Timeout: `10` |
   | `PaintTextureManager` | Target Renderer: キャラクターの Renderer をドラッグ |
   | `InkColorCycler` | Interval Seconds: `2.5`（デフォルト7色） |
   | `InkGun` | 下記参照 |
   | `AimingController` | Aim Transform: AimSpotlight / Screen Normal: Main Camera |

3. **InkGun の設定**:
   | フィールド | 設定値 |
   |---|---|
   | Muzzle Point | 銃口用の空 GameObject（AimSpotlight 子要素に配置）|
   | Aim Transform | AimSpotlight の Transform |
   | Fire Interval | 0.5 |
   | Ink Projectile Prefab | InkProjectile.prefab（Blenderモデル後） |
   | Ink Splash Prefab | InkSplash.prefab（Blenderモデル後） |
   | Max Aim Angle | 45 |
   | Main Camera | Main Camera |
   | Paint Target Layer | PaintTarget レイヤー（Layer設定が必要） |
   | Brush Radius | 0.05（UV空間上のブラシ半径） |

---

### Step 5: UI の組み立て

1. **Canvas を作成**: `GameObject > UI > Canvas`
   - Render Mode: `Screen Space - Overlay`
2. 以下の UI 要素を追加:

   | 要素 | 名前 | 配置位置 |
   |---|---|---|
   | TextMeshPro | TimerText | 右上（Anchor: top-right） |
   | Image | ColorIndicator | 左下（Anchor: bottom-left）, W:60 H:60 |
   | Panel + TextMeshPro | WaitingPanel | 画面中央 |

3. Canvas に `GameUI` コンポーネントを追加し、各フィールドにドラッグ:
   - Timer Text → TimerText
   - Color Indicator → ColorIndicator
   - Waiting Panel → WaitingPanel
   - Waiting Message Text → WaitingPanel 内の TextMeshPro

---

### Step 6: Layer 設定（必須）

```
Edit > Project Settings > Tags and Layers
User Layer に「PaintTarget」を追加（例: Layer 8）
```

キャラクター（Sphere または FBX モデル）の Layer を `PaintTarget` に設定。  
`InkGun` の `Paint Target Layer` マスクで `PaintTarget` のみ選択。

---

### Step 7: 動作確認チェックリスト

- [ ] Play Mode でコンソールに `[UdpQuaternionReceiver] 受信開始 port=8000` と出るか
- [ ] ZIG SIM を起動して送信開始 → `IsReceiving: True` がデバッグオーバーレイ（Dキー）に表示されるか
- [ ] スマホを傾けるとスポットライトが追従するか
- [ ] Cキーでキャリブレーション → 傾きがリセットされるか
- [ ] Sphere に向かって照準を合わせるとインクが塗られるか（RenderTexture の変化を Material で確認）
- [ ] Rキーで全塗りがクリアされるか
- [ ] 非操作 10 秒で待機画面に戻るか

---

## 3. 判明した問題点・技術的リスク

### 🔴 問題 1: `hit.textureCoord` の取得条件が厳しい

**内容:**  
`Physics.Raycast` の `RaycastHit.textureCoord` は **MeshCollider（Convex = OFF）** が付いておりかつメッシュが **Read/Write Enabled** でないと常に `(0, 0)` を返す。

**影響:**  
- UV の正確なペイントが機能しない
- Blender モデルインポート時のインポート設定ミスでサイレントバグになる

**対処:**
```
FBX インポート設定（Inspector）:
  Read/Write: ✅ ON にする
  Mesh Collider の Convex: ✅ OFF にする（デフォルトは ON なので要注意）
```

---

### 🔴 問題 2: CPU ブラシ描画が重い（フォールバック実装）

**内容:**  
`InkGun.cs` 内の `SimpleCpuBrush()` はペイント毎に Texture2D の ReadPixels → Apply → Graphics.Blit を行うため、**1 フレームあたり数十 ms の処理落ちが発生し得る**。

**影響:**  
- 0.5 秒間隔の発射でも、描画に詰まりが生じる可能性がある
- 展示中にフレームレートが下がる

**対処 (TODO):**  
`InkPaintShader` の `BrushStamp` パスを使った **GPU ブラシ描画（Graphics.Blit + MaterialPropertyBlock）** に差し替える必要がある。現在はシェーダー準備のコードを書いたが、CPU→GPUの切り替えロジックは未実装。

---

### 🟡 問題 3: `FindObjectOfType` の多用によるパフォーマンス懸念

**内容:**  
`GameStateManager.cs` の `HandleWaiting()` と `HandlePlaying()` で毎フレーム `FindObjectOfType<UdpQuaternionReceiver>()` を呼んでいる。

**影響:**  
- シーン内オブジェクト数が増えると処理が重くなる

**対処 (TODO):**  
`Start()` で一度キャッシュして参照を保持するよう変更すること。

```csharp
// 修正方針
private UdpQuaternionReceiver _receiver;
private void Start() => _receiver = FindObjectOfType<UdpQuaternionReceiver>();
```

---

### 🟡 問題 4: TextMeshPro 依存

**内容:**  
`GameUI.cs` は `TMPro`（TextMeshPro）を using している。  
Unity プロジェクトに TextMeshPro パッケージが未インポートの場合コンパイルエラーになる。

**確認手順:**  
```
Window > Package Manager > TextMeshPro が Installed になっているか確認
なければ: Window > TextMeshPro > Import TMP Essential Resources
```

---

### 🟡 問題 5: OSC パーサーの精度（ZIG SIM の OSC メッセージ仕様依存）

**内容:**  
`UdpQuaternionReceiver.cs` の `TryParseOscQuaternion()` は OSC 仕様の最低限実装。  
ZIG SIM が送る OSC アドレスは `/gyrosc/quat` や `/zig/quaternion` などアプリバージョンにより異なる場合がある。

**確認手順:**  
Wireshark または `Debug.Log` でバイト列を見て、アドレス文字列を確認すること。  
合わない場合はパーサーの読み飛ばしロジックを調整する。

---

### 🟢 問題 6: Blender モデル未到着時の暫定動作

**内容:**  
`inkProjectilePrefab` / `inkSplashPrefab` が null のとき `InkGun` はスキップのみ（エラーなし）。  
`targetRenderer` が null のとき `PaintTextureManager` は警告ログのみ。  
→ **コンパイルエラーや NullReferenceException は起きない設計になっている。**

**暫定テスト環境:**  
| 本番 | 暫定代替 |
|---|---|
| Blender FBX キャラ | Unity Sphere（MeshCollider=OFF） |
| InkProjectile.prefab | なし（スキップ） |
| InkSplash.prefab | なし（スキップ） |

---

## 4. 新しくわかったこと・設計上の気づき

### 💡 気づき 1: ペイントとエフェクトは分離すべき

既存の `unity_task.txt` にも記載があったが、コード実装してみると**「ビジュアルの弾演出」と「UV ペイント処理」は完全に独立して実装できる**。

- **Raycast ヒット → UV ペイント**（Blenderモデルのコライダー依存）
- **Instantiate InkSplash → 見た目演出**（モデルが来た時に追加）

つまり Blenderモデルが来る前に**ゲームロジックとZIG SIM通信のテストは完全に可能**。

---

### 💡 気づき 2: URP シェーダーの光沢演出はブラシスタンプとは別 Pass が必要

ペイント RenderTexture に書き込む「ブラシスタンプ Pass」と、キャラクターを描画する「Forward Pass（光沢演出含む）」は**シェーダーの Pass を分けないと両立しない**。  
→ `InkPaintShader.shader` で 2Pass 構成として実装済み。ただし光沢のフェードアウトは Unity コルーチン側から `_GlossFade` プロパティを時間制御で変化させる実装が未完（TODO）。

---

### 💡 気づき 3: MeshCollider の Convex 問題は Unity の仕様

Unity の `RaycastHit.textureCoord` は **非 Convex MeshCollider のみ対応**。  
正確には「Mesh の三角形インデックスとバリセントリック座標から UV を逆算する処理」が Convex ではできない Unity の内部制約。  
→ Blenderモデルのインポート後に **必ず Convex のチェックを外すこと**（デフォルト ON なので見落としやすい）。

---

### 💡 気づき 4: ZIG SIM の OSC アドレスはバージョンで変わる可能性がある

参考リポジトリ（`yugo-ShibaLab-ARD`）の実装を読んでいると、OSC アドレスのフォーマットがバージョンによって異なる場合がある。  
今回のパーサーは「アドレス文字列を読み飛ばして float×4 を取る」方針にしているため概ね対応できるが、**タイプタグ（`,ffff`の有無）の形式が異なると誤読する**。  
→ 初回 ZIG SIM 連携テスト時に `Debug.Log` でバイト列を確認すること。

---

## 5. 残り作業（Priority 順）

| Priority | タスク | 備考 |
|---|---|---|
| 🔴 1 | Blender マスコットキャラクター UV 展開・FBX エクスポート | これ待ちでペイントのE2Eテスト不可 |
| 🔴 2 | Unity シーン組み立て（Step 2〜6 実施） | コードをアタッチして動作確認 |
| 🔴 3 | ZIG SIM 実機テスト | iOS + Unity 実機でクォータニオン受信確認 |
| 🟡 4 | CPU→GPU ブラシ描画切り替え（`InkPaintShader` 統合） | パフォーマンス改善 |
| 🟡 5 | `FindObjectOfType` をキャッシュに変更 | 軽微だが修正推奨 |
| 🟡 6 | TextMeshPro インポート確認 | `GameUI.cs` 使用前に必要 |
| 🟢 7 | Blender インク弾・着弾エフェクト制作 | キャラ完成後でOK |
| 🟢 8 | 光沢フェードアウトのコルーチン実装 | 演出面の改善 |
