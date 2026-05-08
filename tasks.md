# OmiyaFes2026 タスク管理

## 最新状態（2026-05-08 17:25）

### ✅ 修正：AimDirectionLineVisualizer + AimRootSetup
**原因**: `AimRootSetup` が実行時に `PoseRotationDriver.aimTarget` を `aim`→`AimRoot`（新規作成）に差し替えるが、`AimDirectionLineVisualizer` は `aim`（静止）を参照したまま → ライン固定。

**修正内容:**
- `AimDirectionLineVisualizer.SetDirectionTransform(Transform t)` を新規追加
- `AimRootSetup.Awake()` にステップ5を追加 → `FindObjectOfType<AimDirectionLineVisualizer>()` → `SetDirectionTransform(aimRoot)` を呼ぶ

### 🔍 診断中：PoseRotationDriver の回転値確認
`ZigSimDiagnostic` を強化 → `RelativeEuler`（スマホの相対回転）を2秒ごと表示
- `RelativeEuler ≈ 0,0,0` のまま → スマホが動いていない or キャリブ問題
- `RelativeEuler が動いている` → 回転計算はOK → AimRootSetupの接続問題

**コンソールで確認する手順:**
1. `GameManager` に `ZigSimDiagnostic` をアタッチ（まだなら）
2. Play → 2秒後に以下が出る:
   ```
   RelativeEuler = pitch=X° yaw=Y° roll=Z°  ← Xが動くか？
   ```
3. `[AimRootSetup] ✅ セットアップ完了` がコンソールにあるか確認

---


### ✅ 完了：SE / BGM 実装
- `SoundManager.cs` 新規作成（シングルトン）
- Inspector で設定：`Shoot SFX`（発射音）、`Hit SFX`（着弾音）、`BGM`（ループ）、音量
- `InkGun.cs` → `FireProjectile()` に `PlayShoot()` 追加
- `InkBullet.cs` → `OnTriggerEnter()` に `PlayHit()` 追加
- BGM は Awake() で自動ループ再生開始

### 🔜 次フェーズ TODO（優先順）
1. **SE実装** → `SoundManager.cs` 新規作成、発射・着弾・ゲーム開始/終了音
2. **銃モデリング（Blender）** → ペイントガン風ローポリ → FBX → Unityインポート
3. **銃口接続（Unity）** → GunModel を AimRoot 配下に配置、MuzzlePoint 位置調整



### ✅ 完了：オブジェクト消滅タイミング延長 (+4秒)
- `ObjectSpawner` に `destroyX = -24f` パラメータを追加（元は `-12f`）
- Inspector の「生成位置」セクションから調整可能（小さい値ほど長く残る）
- `FloatingObject.Initialize(speed, destroyX)` で渡すよう連携

### ✅ 完了：ObjectSpawner Z 奥行き範囲パラメータ追加
- `spawnZ`（固定値）を廃止し、`spawnZMin` / `spawnZMax` の2つに変更
- Inspector の「生成位置」セクションから奥行き範囲を調整可能
- デフォルト値: `spawnZMin = 0`（手前）、`spawnZMax = 10`（奥）
- ログに Z 座標も出力されるようになった

### ✅ 完了：DirectMapping 感度パラメータ追加
- `PoseRotationDriver.cs` に `sensitivityScale` フィールドを追加（Range 0.1〜5.0）
- Inspector の `ZigSimReceiver > PoseRotationDriver > 感度（DirectMapping モード用）` から調整可能
- デフォルト値 `1.0`（変更前と同じ挙動）。`2.0` で2倍感度

### ✅ 完了：学習用ドキュメント追加
- `Assets/doc/code_reading_guide.md` — 全スクリプトマップ・データフロー（ファイルリンク付き）
- `Assets/doc/programming_textbook.md` — プログラミング技術教科書（9章）

---

## ⚠️ 絶対要件（変更禁止）
> ユーザー指定: 2026-05-06 12:14

**スマホの向き = ゲーム内の銃の向きを完全に一致させる。**
- スマホを左に向ける → 銃も左を向く
- 回転しただけで銃の「位置」が変わってはいけない
- 実装モード: `DirectMapping`

---

## シーン設計

```
カメラ(Z≈0)
  ↓ 前方
InkGun（手前）
  ↓ 弾発射
FloatingObj（中間）← 当たったらオブジェクトにインク
  ↓ 外れたら
BackgroundWall（奥）← 当たったら壁にインク
```

## 背景画像セットアップ手順（BackgroundSetup.cs を使う場合）

**Hierarchy に空の GameObject を作成 → BackgroundSetup をアタッチ：**

| Inspector フィールド | 設定内容 |
|---|---|
| **Background Texture** | `background.png` をここにドラッグ |
| **Depth** | カメラから背景までの距離（デフォルト `15`） |
| **Quad Scale** | 背景の幅×高さ（デフォルト `30×18`） |
| **Enable Ink Painting** | `✅` ON → インクが当たるようになる |
| **Paint Texture Size** | ペイントの解像度（デフォルト `512`） |
| **Brush Pixel Radius** | 背景に当たったときのインクの大きさ（デフォルト `80`） |

> ⚠️ コライダーは内部で自動的に Trigger に設定される（手動設定不要）

## 背景壁のセットアップ（手動・Inspector のみ・コード不要）

**Hierarchy に Quad or Plane を配置 → Inspector で：**

| コンポーネント | 設定 |
|---|---|
| **Transform** | Z を奥に（例: `15`）・スケールを大きく |
| **MeshCollider** または **BoxCollider** | `Is Trigger = ✅` |
| **PaintTarget** | アタッチするだけ（自動でテクスチャ生成） |

> ⚠️ コライダーは **Trigger** にすること（InkBulletはOnTriggerEnterでペイント）

---

## インクリセットの仕組み（今回実装済み）

```
GameStateManager.ResetGame()
    ↓
FindObjectsOfType<PaintTarget>()
    ↓ 全PaintTargetを列挙（壁もオブジェクトも）
pt.ClearPaint()  ← テクスチャを透明にクリア
```

→ 壁・浮遊オブジェクト、どちらもゲームリセット時に同時にインクがきれいになる

---

## Inspector 設定（推奨値）

### ZigSimReceiver > PoseRotationDriver
| 項目 | 設定値 |
|---|---|
| **Control Mode** | **`Direct Mapping`** |
| Use ARD Compatible Mode | ✅ ON |

### InkGun
| 項目 | 設定値 |
|---|---|
| **Fire Mode** | **`Projectile`** |
| Fire Interval | `0.3` |

## キー操作
| キー | 機能 |
|---|---|
| Space | ゲーム開始 |
| **C** | キャリブレーション（照準リセット） |
| **R** | 強制リセット（スタッフ用） |
