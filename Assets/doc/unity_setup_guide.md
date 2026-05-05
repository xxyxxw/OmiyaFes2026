# Unity セットアップガイド（初心者向け）
## 流れるオブジェクトをインク銃で塗るゲーム

---

## はじめに

このガイドでは、Unityだけを使って（外部の3Dモデルなし）ゲームを動かすための設定手順を説明します。  
プリミティブ（Cube・Sphere などの基本形状）を使って、まずは動く状態を確認しましょう。

---

## ゲームの仕組み（全体像）

```
スマホ（ZIG SIM）
    ↓ ジャイロデータを UDP で送信
PC（Unity）
    ↓ 受信して照準が動く
InkGun（インク銃）
    ↓ 0.3秒ごとに自動発射
InkBullet（インク弾・Sphere）
    ↓ 飛翔中に FloatingObject に衝突
PaintTarget（ペイント処理）
    ↓ 衝突した UV 座標にインクを塗る
オブジェクト表面の色が変わる！
```

---

## STEP 1：Layer（レイヤー）の作成

レイヤーとは「オブジェクトのグループ分け」です。  
弾がペイント対象のオブジェクトだけに当たるようにするために作ります。

1. Unity の上メニューから **Edit → Project Settings** を開く
2. 左側のリストから **Tags and Layers** をクリック
3. **Layers** の欄を下にスクロールし、空欄（User Layer 8 など）を探す
4. 空欄に **`PaintTarget`** と入力して Enter

> ✅ 確認：Layer 8 に「PaintTarget」と表示されていればOK

---

## STEP 2：シーンの準備

### 2-1. 必要な GameObject の確認

Hierarchy（ヒエラルキー）ウィンドウに以下の GameObject が存在するか確認してください。  
なければ右クリック → **Create Empty** で作成します。

| GameObject 名（任意） | アタッチするコンポーネント |
|---|---|
| `_GameManager` | `GameStateManager` |
| `_InkGun` | `InkGun`、`InkColorCycler` |
| `_ObjectSpawner` | `ObjectSpawner` |
| `Main Camera` | カメラ（デフォルトで存在する） |

---

## STEP 3：各コンポーネントの設定

### 3-1. GameStateManager

`_GameManager` に `GameStateManager` をアタッチしたら、Inspector で以下を設定します。

| フィールド名 | 設定値 | 説明 |
|---|---|---|
| Game Duration | `45` | ゲームの制限時間（秒） |
| Inactivity Timeout | `10` | スマホの操作が止まったら何秒でリセットするか |

---

### 3-2. InkGun

`_InkGun` に `InkGun` をアタッチしたら、Inspector で以下を設定します。

| フィールド名 | 設定値 | 説明 |
|---|---|---|
| Muzzle Point | 銃口の Transform | 弾が出る場所。なければ `_InkGun` 自身でもOK |
| Aim Transform | 照準の Transform | スマホで動く照準オブジェクト。なければ空欄でもテスト可 |
| Fire Interval | `0.3` | 何秒ごとに弾を撃つか（小さいほど連射） |
| Ink Projectile Prefab | **空欄のまま** | 未設定だと自動で Sphere を生成する |
| Max Aim Angle | `45` | 照準が画面から何度以内を向いていたら撃つか |
| Bullet Scale | `0.2` | 弾の大きさ（メートル） |
| **Brush World Radius** | `0.5` | **弾が当たった中心から何メートル塗るか** ← ここが塗り範囲！ |

> 💡 **Brush World Radius を大きくすると広く塗れます（0.2〜1.0 で試してみてください）**

---

### 3-3. InkColorCycler

`_InkGun` に `InkColorCycler` もアタッチします。

| フィールド名 | 設定値 | 説明 |
|---|---|---|
| Colors | デフォルトのまま | 赤・ピンク・オレンジ・黄・水色・緑・紫 の7色が設定済み |
| Interval Seconds | `2.5` | 何秒ごとに色が切り替わるか |

---

### 3-4. ObjectSpawner ⭐ 最重要

`_ObjectSpawner` に `ObjectSpawner` をアタッチします。

| フィールド名 | 設定値 | 説明 |
|---|---|---|
| Spawn X | `10` | オブジェクトが生成される X 座標（画面右外） |
| Spawn Y Min | `-2` | 生成される高さの最小値 |
| Spawn Y Max | `2` | 生成される高さの最大値 |
| Spawn Z | `0` | Z 座標（カメラに映る位置に合わせて調整） |
| Move Speed Min | `2` | 移動速度の最小値（Units/秒） |
| Move Speed Max | `4` | 移動速度の最大値（Units/秒） |
| Interval Min | `1` | 生成間隔の最小（秒） |
| Interval Max | `3` | 生成間隔の最大（秒） |
| Size Min | `0.8` | オブジェクトの大きさ最小 |
| Size Max | `1.5` | オブジェクトの大きさ最大 |
| **Paint Target Layer** | **8** | STEP 1 で作った `PaintTarget` レイヤーの番号 |

> ⚠️ **Paint Target Layer の番号を間違えると弾が当たりません！**  
> Project Settings > Tags and Layers で確認してください。

---

### 3-5. PaintTextureManager の無効化

以前の設計で使っていた `PaintTextureManager` がシーンにある場合：
1. その GameObject を Hierarchy で選択
2. Inspector の左上のチェックボックスを **オフ（非アクティブ）** にする

---

## STEP 4：カメラの位置調整

オブジェクトが画面に映るように `Main Camera` の Z 座標を調整します。

### 推奨設定例

| 軸 | 値 |
|---|---|
| Position X | `0` |
| Position Y | `0` |
| Position Z | `-10` |

`ObjectSpawner` の `Spawn Z` は `0`、カメラが Z=-10 から Z=0 を見る形です。  
もし映らない場合は `Spawn Z` を Camera の Z + 10 程度に合わせてみてください。

---

## STEP 5：ZIG SIM の設定（スマホ操作）

スマホアプリ「ZIG SIM」を使ってジャイロデータを送信します。

1. スマホと PC が **同じ Wi-Fi** に接続されていることを確認
2. ZIG SIM アプリを起動し、以下を設定：

| 項目 | 設定値 |
|---|---|
| IP Address | PC の IP アドレス（ipconfig で確認） |
| Port | `50000`（UdpQuaternionReceiver に合わせる） |
| Sensor | **Quaternion（クォータニオン）をON** にする |

3. ZIG SIM の「SEND」を押すとゲームが自動でスタートします

---

## STEP 6：Play Mode で動作確認

1. Unity の **▶ ボタン**（Play）を押す
2. ZIG SIM を送信するか、デバッグ用に `R` キーを押してゲームをリセット

### 確認チェックリスト

- [ ] Hierarchy に `FloatingObj_Cube_0` などが生成されている
- [ ] オブジェクトが右から左に流れている
- [ ] 弾（Sphere）が発射されている
- [ ] 弾がオブジェクトに当たると色が変わる
- [ ] `R` キーでリセットしてオブジェクトが全て消える

### うまくいかないときは？

| 症状 | 原因と対処 |
|---|---|
| オブジェクトが生成されない | GameStateManager が Waiting 状態。ZIG SIM を送信するか、GameStateManager.StartGame() を手動で呼ぶ |
| 弾が当たっても塗れない | ObjectSpawner の `paintTargetLayer` の番号を確認 |
| 弾がすり抜ける | InkBullet の SphereCollider が isTrigger になっているか確認 |
| UV が (0,0) になる | MeshCollider が付いているか確認（BoxCollider や SphereCollider では UV が取れない） |

---

## デバッグ用：ZIG SIM なしでテストする方法

`GameStateManager.cs` の `HandleWaiting()` を一時的に書き換えてスペースキーで開始できます：

```csharp
private void HandleWaiting()
{
    // テスト用：スペースキーでゲーム開始
    if (Input.GetKeyDown(KeyCode.Space))
        StartGame();
}
```

> ✅ 動作確認が終わったら元の ZIG SIM 判定コードに戻すこと！

---

## コンポーネント依存関係まとめ

```
GameStateManager  ← ObjectSpawner が購読（OnGameStart/OnReset）
                  ← InkGun が参照（Playing 状態のとき発射）

InkColorCycler    ← InkGun が参照（現在のインク色を取得）

InkGun
  └─ 発射 → InkBullet（動的生成）
               └─ OnTriggerEnter → PaintTarget.Paint()

ObjectSpawner
  └─ 生成 → FloatingObject（移動）
            ├─ MeshCollider（UV取得用）
            ├─ PaintTarget（テクスチャ管理）
            └─ Rigidbody(kinematic)
```
