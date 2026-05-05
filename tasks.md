# 2026OmiyaFes – タスク管理

## ✅ 完了済み

- [x] UDP 受信スクリプト（UdpQuaternionReceiver）の実装
- [x] OSC/JSON/バイナリ全フォーマット対応パーサー
- [x] キャリブレーション機能（Calibrate() / C キー）
- [x] デバッグオーバーレイ（PoseDebugOverlay）
- [x] ZIG SIM UDP 通信の疎通確認（ファイアウォール設定含む）
- [x] GunAimVisualizer 新規作成（LineRenderer でレイをゲーム画面に表示）
- [x] **ARD 完全移植（2026-05-05）**：
  - `QuaternionCoordinateConverter` → LookRotation ベース・半球安定化・IosToUnity後方互換
  - `QuaternionCalibrationUtility` → 新規作成（`CalculateRelativeRotation`）
  - `UdpQuaternionReceiver` → `ConsumeLatestRotation` / `ClearPendingRotation` / `ConsumePendingRecenterRequest` / `StabilizeRawQuaternion` 追加・受信ループで `ConvertToUnity` 適用
  - `PoseRotationDriver` → ARD 方式（`initialLocalRotation * relativeRotation * modelOffset`）・autoCalibrateOnFirstPacket・rotationSmoothing・`ResetCalibration()`
  - `PoseCalibrationCoordinator` → ARD 方式（ResetCalibration + ConsumePendingRecenterRequest）

## 🔄 進行中

- [ ] **Unity で実機確認**
  - Inspector で `coordinatePreset = IPhoneCoreMotion`、`screenFaceDown = false` を確認
  - ZIG SIM 接続 → ReceivedPacketCount が増えるか確認
  - C キーでキャリブレーション → スマホを左右/上下に向けて aimTarget が追従するか
  - `controlMode = RotateGun` が推奨（初回はこちらで挙動確認）

## 📋 未着手

- [ ] インク弾の発射ロジック確認（InkGun の `aimTransform.forward` が正しく飛ぶか）
- [ ] ペイント対象オブジェクトへのテクスチャ描画
- [ ] スポーン管理（InkObjectSpawner の調整）
- [ ] ゲームUI（スコア・残弾・タイマー）
- [ ] BGM / SE
- [ ] 最終動作確認・会場テスト

---

## 📝 設計メモ

### ARD 移植後の処理パイプライン（2026-05-05）

```
ZIG SIM (UDP) → UdpQuaternionReceiver
  └─ パース → StabilizeRawQuaternion（正規化・半球安定化）
  └─ ConvertToUnity(IPhoneCoreMotion, LookRotationベース)
  └─ LatestConvertedRotation / _pendingQuat（変換済み）
        ↓
PoseRotationDriver.Update()
  └─ ConsumeLatestRotation() で変換済みQuat取得
  └─ 初回 or リセット → referenceSensorRotation に保存
  └─ CalculateRelativeRotation(ref, current) → 相対回転
  └─ ApplyRelativeAxisPreset（iPhone軸補正: -1,-1,1）
  └─ initialLocalRotation * relativeRotation * modelOffset
  └─ aimTarget.localRotation に適用（Slerp or 即時）
        ↓
InkGun → aimTransform.forward で弾を発射
```

### Inspector 推奨設定

| 項目 | 推奨値 |
|---|---|
| `coordinatePreset` | `IPhoneCoreMotion` |
| `screenFaceDown` | `false`（銃のような横向き持ち） |
| `stabilizeQuaternionHemisphere` | `true` |
| `autoCalibrateOnFirstPacket` | `true` |
| `rotationSmoothing` | `0`（即時）〜`0.1`（滑らか） |
| `iPhoneRelativeAxisSigns` | `(-1, -1, 1)`（ARD デフォルト） |
| `controlMode` | `RotateGun`（まず確認） |

### 座標系変換（ARD 移植後）
```
生 Quat → StabilizeRawQuaternion（w<0なら全符号反転）
→ ConvertIPhoneCoreMotion:
    deviceTop = RotateVector(q, Vector3.up)
    deviceScreenOut = RotateVector(q, Vector3.forward)
    → LookRotation(deviceTop.normalized, -deviceScreenOut.normalized)
→ Euler オフセット適用
```
