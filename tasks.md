# OmiyaFes2026 タスク管理

## 最新状態（2026-05-06 12:39）

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

## 背景壁のセットアップ（Inspector のみ・コード不要）

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
