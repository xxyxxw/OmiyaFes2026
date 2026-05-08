# OmiyaFes2026 コードリーディングガイド

> このドキュメントは「どのファイルに何が書いてあるか」と「どの順番で読むと理解が早いか」をまとめたものです。
>
> 📚 プログラミング技術を学びたい場合は → [programming_textbook.md](programming_textbook.md)

---

## 🗺️ ファイルマップ（全体像）

```
Assets/Scripts/
│
├── ── ゲームの骨格 ──────────────────────────────────────
│   GameStateManager.cs       # ゲーム状態機械（Waiting→Playing→Ending）
│   GameUI.cs                 # タイマー・色インジケーター・待機パネルの HUD
│
├── ── 弾・ペイント ──────────────────────────────────────
│   InkGun.cs                 # インク銃（弾の発射制御・発射モード切替）
│   InkBullet.cs              # インク弾（飛翔・衝突・UV取得・ペイント呼び出し）
│   PaintTarget.cs            # ペイント先オブジェクト（テクスチャにインクを描く）
│   PaintTextureManager.cs    # （将来用）テクスチャ管理の補助
│   InkColorCycler.cs         # インク色を時間で自動サイクル
│
├── ── ターゲットオブジェクト ────────────────────────────
│   ObjectSpawner.cs          # ターゲットを右→左に一定間隔で生成
│   FloatingObject.cs         # ターゲット1個の移動・回転・自動消滅
│
├── ── 照準セットアップ ──────────────────────────────────
│   AimRootSetup.cs           # Play開始時に AimRoot/MuzzlePoint を自動生成・接続
│   AimingController.cs       # 照準が画面正面を向いているか判定（現在は未使用気味）
│
└── Pose/                     # スマホジャイロ入力系（サブシステム）
    ├── UdpQuaternionReceiver.cs      # ZIG SIM からクォータニオンを UDP 受信
    ├── PoseRotationDriver.cs         # 受信データを AimRoot に適用（回転ドライバー）
    ├── PoseCalibrationCoordinator.cs # キャリブレーションのトリガー管理
    ├── QuaternionCoordinateConverter.cs # iOS/Android 座標系 → Unity 座標系の変換
    ├── QuaternionCalibrationUtility.cs  # 相対回転の計算（1関数のみ）
    ├── GunAimVisualizer.cs           # 照準レイを LineRenderer で可視化（デバッグ）
    ├── AimDirectionLineVisualizer.cs # aimTransform.forward を線で可視化（デバッグ）
    ├── PoseDebugOverlay.cs           # 画面上に受信状態・Eulerをテキスト表示（デバッグ）
    └── PoseTestBootstrap.cs          # （テスト用）起動時の自動セットアップ
```

---

## 📊 データフロー（動作の流れ）

```
スマホ（ZIG SIM）
  │ UDP/OSC でクォータニオンを送信（ポート 50000）
  ▼
UdpQuaternionReceiver.cs
  │ バックグラウンドスレッドで受信
  │ iOS→Unity 座標変換（QuaternionCoordinateConverter）
  │ ConsumeLatestRotation() で PoseRotationDriver に渡す
  ▼
PoseRotationDriver.cs
  │ キャリブレーション（基準姿勢からの相対回転を計算）
  │ AimRoot.localRotation に直接適用（DirectMapping モード）
  ▼
AimRoot（Transform）
  │ スマホの向きがそのまま反映される
  ▼
InkGun.cs
  │ fireInterval 秒ごとに AimRoot.forward 方向へ弾発射
  │ モード1: Projectile  → InkBullet を生成して飛ばす
  │ モード2: DirectRaycast → その場で Raycast して即ペイント
  ▼
InkBullet.cs（Projectileモード）
  │ 飛翔 → PaintTarget に OnTriggerEnter で衝突
  │ UV 座標を取得して PaintTarget.Paint() を呼ぶ
  ▼
PaintTarget.cs
  │ RenderTexture にインク色を円形ブラシで描画
  ▼
画面にインクが塗られる ✅
```

---

## 📅 ゲーム状態フロー

```
Waiting（待機中）
  → ZIG SIM からパケット受信 or スペースキー
  → GameStateManager.StartGame()
Playing（プレイ中）
  → 45秒タイマー or 非操作タイムアウト
  → GameStateManager.EndGame()
Ending（終了）
  → 即座に ResetGame()
  → ObjectSpawner がオブジェクト全削除
  → PaintTarget がインクを全クリア
Waiting（待機中）← ループ
```

---

## 📖 理解の優先順位

> 「このゲームがどう動くか」を最速で理解するための読む順番です。

### 🔴 STEP 1（必読・全体構造）

| ファイル | 読む目的 | 学べる技術 |
|---|---|---|
| [tasks.md](../../tasks.md) | ゲームの仕様・シーン設計の全体方針を把握 | - |
| [GameStateManager.cs](../Scripts/GameStateManager.cs) | ゲーム全体の状態遷移（土台）を理解する | シングルトン・イベント・状態機械 |
| [InkGun.cs](../Scripts/InkGun.cs) | インク銃の主役。`FireMode` の2種と発射の仕組み | enum・コンポーネント参照 |

### 🟠 STEP 2（インク・ペイントの仕組み）

| ファイル | 読む目的 | 学べる技術 |
|---|---|---|
| [InkBullet.cs](../Scripts/InkBullet.cs) | 弾が飛んで衝突し UV を取得する流れ | 物理・衝突・null安全 |
| [PaintTarget.cs](../Scripts/PaintTarget.cs) | RenderTexture への CPU ブラシ描画 | テクスチャ処理 |
| [InkColorCycler.cs](../Scripts/InkColorCycler.cs) | 色サイクルの仕組み（シンプルで読みやすい） | タイマー・プロパティ |

### 🟡 STEP 3（スマホ入力パイプライン）

| ファイル | 読む目的 | 学べる技術 |
|---|---|---|
| [UdpQuaternionReceiver.cs](../Scripts/Pose/UdpQuaternionReceiver.cs) | ZIG SIM からの UDP 受信・OSC パース（最初は L70〜90 と Update のみでOK） | マルチスレッド・lock |
| [PoseRotationDriver.cs](../Scripts/Pose/PoseRotationDriver.cs) | キャリブレーションと DirectMapping の適用 | クォータニオン・状態フラグ |
| [QuaternionCoordinateConverter.cs](../Scripts/Pose/QuaternionCoordinateConverter.cs) | iOS/Android 座標系 → Unity 左手系変換 | 静的クラス・数学 |
| [QuaternionCalibrationUtility.cs](../Scripts/Pose/QuaternionCalibrationUtility.cs) | 1関数だけ（超短い） | 静的メソッド |

### 🟢 STEP 4（ターゲット生成）

| ファイル | 読む目的 | 学べる技術 |
|---|---|---|
| [ObjectSpawner.cs](../Scripts/ObjectSpawner.cs) | ターゲットの生成ループとイベント購読パターン | コルーチン・イベント |
| [FloatingObject.cs](../Scripts/FloatingObject.cs) | 1オブジェクトの移動・回転・消滅（シンプル） | クラス・Initialize パターン |

### 🔵 STEP 5（自動接続・UI）

| ファイル | 読む目的 | 学べる技術 |
|---|---|---|
| [AimRootSetup.cs](../Scripts/AimRootSetup.cs) | Play時に AimRoot/MuzzlePoint を自動生成して接続 | FindObjectOfType・動的生成 |
| [GameUI.cs](../Scripts/GameUI.cs) | タイマー・色インジケーター・待機パネルの HUD | UI更新・イベント購読 |
| [AimingController.cs](../Scripts/AimingController.cs) | 照準が画面正面角度内かを判定 | Vector3.Angle の使い方 |

### ⚪ STEP 6（デバッグ用・読まなくても動く）

| ファイル | 内容 |
|---|---|
| [PoseDebugOverlay.cs](../Scripts/Pose/PoseDebugOverlay.cs) | D キーで受信状態と Euler 角をオーバーレイ表示 |
| [GunAimVisualizer.cs](../Scripts/Pose/GunAimVisualizer.cs) | 照準レイを LineRenderer で Game ビューに可視化 |
| [AimDirectionLineVisualizer.cs](../Scripts/Pose/AimDirectionLineVisualizer.cs) | aimTransform.forward を線で可視化（シンプル版） |
| [PoseCalibrationCoordinator.cs](../Scripts/Pose/PoseCalibrationCoordinator.cs) | C キー・スマホタッチでキャリブを実行するだけ |

---

## 🔑 重要キーワード・設計パターン

### シングルトン（GameStateManager）
```csharp
// 他のスクリプトから状態を参照する方法
GameStateManager.Instance.CurrentState      // 現在のゲーム状態
GameStateManager.Instance.RemainingTime     // 残り時間（秒）
GameStateManager.Instance.OnGameStart += () => { /* 処理 */ };  // イベント購読
```

### インスペクター設定の対応表（触ることの多い設定）

| GameObject | コンポーネント | 重要な設定 |
|---|---|---|
| ZigSimReceiver | `UdpQuaternionReceiver` | `port=50000`, `screenFaceDown=true` |
| ZigSimReceiver | `PoseRotationDriver` | `controlMode=DirectMapping`, `useArdCompatibleMode=true` |
| ZigSimReceiver | `AimRootSetup` | Play時に AimRoot を自動生成（通常は触らない） |
| InkGun | `InkGun` | `fireMode=Projectile`, `fireInterval=0.3`, `brushPixelRadius=128` |
| Canvas | `GameUI` | timerText / colorIndicator を繋ぐ |
| ObjectSpawner | `ObjectSpawner` | spawnX/Y/Z, moveSpeed, interval |

### UV取得の精度（重要）
```
MeshCollider（convex=false）がある → hit.textureCoord で正確UV ✅
MeshCollider がない              → BoundsPointToUV で近似UV ⚠️（ずれる）
```
`ObjectSpawner` は生成時に自動で MeshCollider を付与するため、浮遊ターゲットは正確な UV が得られる。

### キャリブレーション
- **C キー** → 現在のスマホ姿勢を「正面」として登録
- **スマホ画面タップ** → 同様にリセンター
- 初回パケット受信時に自動キャリブ（`autoCalibrateOnFirstPacket=true`）

---

## 📁 doc フォルダの他のドキュメント

| ファイル | 内容 |
|---|---|
| [programming_textbook.md](programming_textbook.md) | ★ このコードで学べるプログラミング技術の教科書 |
| [unity_setup_guide.md](unity_setup_guide.md) | Hierarchy・Inspector の具体的な設定手順 |
| [implementation_report.md](implementation_report.md) | これまでの実装経緯と設計判断の記録 |
| [blender_model_guide.md](blender_model_guide.md) | Blender モデルの要件・設定ガイド |

---

## 💡 よくある確認ポイント

| 症状 | 最初に見るファイル |
|---|---|
| ZIG SIM が繋がらない | `UdpQuaternionReceiver.cs`（port・IsReceiving） |
| 照準がおかしい向きに動く | `PoseRotationDriver.cs`（invertLeftRight/UpDown）|
| インクが的外れの位置に塗られる | `InkBullet.cs`（UV取得ロジック）・`PaintTarget.cs` |
| オブジェクトが出てこない | `ObjectSpawner.cs`（GameStateManager イベント購読） |
| タイマーが動かない | `GameStateManager.cs`（HandlePlaying）・`GameUI.cs` |
| ゲームが始まらない | `GameStateManager.cs`（HandleWaiting の IsReceiving 判定） |
