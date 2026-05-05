# 2026OmiyaFes – タスク管理

## ✅ 完了済み

- [x] UDP 受信スクリプト（UdpQuaternionReceiver）の実装
- [x] OSC/JSON/バイナリ全フォーマット対応パーサー
- [x] キャリブレーション機能（Calibrate() / C キー）
- [x] デバッグオーバーレイ（PoseDebugOverlay）
- [x] ZIG SIM UDP 通信の疎通確認（ファイアウォール設定含む）
- [x] 座標系変換修正：`IosToUnity()` を `(-x, -y, z, w)` に変更
- [x] **PoseRotationDriver 全面書き直し**：MoveCrosshair モードを追加し、スマホの傾き→銃の横・縦移動に対応
- [x] **GunAimVisualizer 新規作成**：LineRenderer でレイをゲーム画面に表示

## 🔄 進行中

- [ ] Unity で動作確認
  - PoseRotationDriver の `controlMode` を `MoveCrosshair` に設定
  - `aimTarget` に銃オブジェクト（または空のTransform）を設定
  - GunAimVisualizer を銃オブジェクトに追加
  - ZIG SIM で接続 → スマホを振って銃が横・縦に動くか確認
  - C キーでキャリブレーション

## 📋 未着手

- [ ] インク弾の発射ロジック（InkGun の動作確認）
- [ ] ペイント対象オブジェクトへのテクスチャ描画
- [ ] スポーン管理（InkObjectSpawner の調整）
- [ ] ゲームUI（スコア・残弾・タイマー）
- [ ] BGM / SE
- [ ] 最終動作確認・会場テスト

---

## 📝 設計メモ

### PoseRotationDriver の制御モード（2026-05-05 追加）

| モード | 挙動 | 用途 |
|---|---|---|
| `RotateGun` | スマホの向きで銃の Rotation を直接制御 | 姿勢をそのまま反映したい場合 |
| `MoveCrosshair` | スマホの傾き角度を画面上のXY位置に変換してaimTargetを移動 | **銃を横・縦に移動させたい場合（今回の要件）** |

### MoveCrosshair モードの調整パラメータ

| パラメータ | 説明 | 推奨値 |
|---|---|---|
| `sensitivityH/V` | 感度（大きいほど少ない傾きで端まで動く） | 2〜4 |
| `maxYawDeg` | 左右の最大角度（これ以上傾けても端で止まる） | 30〜50° |
| `maxPitchDeg` | 上下の最大角度 | 20〜40° |
| `smoothing` | スムージング（0=即時, 0.1=自然, 0.5=遅め） | 0.05〜0.15 |
| `screenDepth` | 銃が動く平面のカメラからの距離（m） | 5〜15 |

### 座標系変換（確定版）
```
IosToUnity: new Quaternion(-ios.x, -ios.y, ios.z, ios.w)
```
