# インタラクティブ・ペイントシューター 実装タスクリスト

> 作成日: 2026-04-18  
> 設計書: `all_taxt.txt`, `unity_task.txt`  
> 参考リポ: https://github.com/uni-bit/yugo-ShibaLab-ARD  

---

## 確定仕様（質問回答を反映）

| 項目 | 内容 |
|---|---|
| 塗り面積計算 | ❌ 実装しない（リザルト画面なし） |
| ゲーム終了仕様 | 時間切れ or 非操作10秒でインクリセット → 待機画面へ |
| カラー選択 | 自動サイクルのみ（後で追加検討） |
| WiFi | 持参ルーター使用 |
| インク弾メッシュ | Blenderで制作してUnityにFBX/GLBインポート |
| オブジェクト塗り処理 | UnityのRenderTextureで実装（Blender不使用） |

---

## フェーズ別タスク

---

### Phase 0: 環境構築・リポジトリ準備

- [ ] Unity 6000.0.68f1 + URP + Input System のプロジェクト確認（既存プロジェクトを使用）
- [ ] 参考リポジトリをローカルにクローン
  ```
  git clone https://github.com/uni-bit/yugo-ShibaLab-ARD
  ```
- [ ] 参考リポジトリの Scripts/Pose/ フォルダを本プロジェクトの Assets/OmiyaFes2026/Scripts/Pose/ にコピー
  - `UdpQuaternionReceiver.cs`
  - `QuaternionCoordinateConverter.cs`
  - `PoseRotationDriver.cs`
  - `PoseCalibrationCoordinator.cs`
  - `PoseTestBootstrap.cs`
  - `PoseDebugOverlay.cs`
  - `SpotlightSensor.cs`（Scripts/Stages/ から）
- [ ] 本プロジェクトの namespace / using を確認してコンパイルエラーをつぶす
- [ ] メインシーン（OmiyaFes2026.unity）を新規作成

---

### Phase 1: Blender作業（キャラクター + インク弾）

> ⚠️ Unityのペイント実装（Phase 4）はこのPhaseが終わらないと進められない

#### 1-1. マスコットキャラクター制作
- [ ] キャラクターモデリング（ポリゴン数目安: 10,000〜50,000）
- [ ] **UV展開（必須）** — UV Editing > スマートUV展開（U → Smart UV Project）
  - 重なりなく全面に展開すること（ペイントのために必須）
- [ ] PBRマテリアル設定（BaseColor / Roughness / Normal）
- [ ] （任意）アイドルアニメーション追加
- [ ] FBXエクスポート
  - Apply Scalings: FBX Units Scale
  - Forward: -Z Forward / Up: Y Up
  - Apply Transform にチェック
- [ ] UnityのAssets/OmiyaFes2026/Models/ にインポート
- [ ] Unity側でURP対応マテリアルに差し替え（URPLit シェーダー使用）

#### 1-2. インク弾メッシュ制作
- [ ] インクの飛翔弾モデル制作（インクタレた丸い形状など）
- [ ] 着弾時のインク拡散エフェクトモデル or アニメーション制作
- [ ] FBX or GLBエクスポート
- [ ] UnityでPrefab化
  - `InkProjectile.prefab`（飛翔弾）
  - `InkSplash.prefab`（着弾エフェクト）

---

### Phase 2: ZIG SIM通信実装（参考リポジトリ流用）

- [ ] PoseTestBootstrap を空のGameObjectにアタッチ
- [ ] Play Modeでリグが自動生成されることを確認
- [ ] iPhone に ZIG SIM インストール
- [ ] PC と iPhone を同一WiFiに接続し、PCのローカルIPを確認
- [ ] ZIG SIM 設定
  - Protocol: UDP
  - Format: OSC
  - Port: 8000
  - QUATERNION: ON
- [ ] Unity側でクォータニオンが受信できているか Debug.Log で確認
- [ ] `PoseDebugOverlay`（Dキー）でデバッグ表示確認

---

### Phase 3: 照準制御実装（参考リポジトリ流用）

- [ ] `PoseRotationDriver.cs` でスポットライトの向きがジャイロ連動することを確認
- [ ] スマホを傾けると照準（スポットライト）が追従することをテスト
- [ ] `PoseCalibrationCoordinator.cs` でCキーキャリブレーション動作確認
- [ ] スポットライトの照射円をカメラ画面に可視化（光の円形表示）
- [ ] スマホが外を向いた場合の判定ロジック実装
  ```
  照準方向Vector が 画面正面に対して閾値角以上 → 発射停止フラグON
  非操作10秒継続 → 全塗りリセット + 待機状態へ
  ```

---

### Phase 4: ペイントシステム実装（新規実装・最重要）

> UnityがBlenderモデルのUV座標にインクを描画する仕組み

#### 4-1. RenderTexture セットアップ
- [ ] 解像度 1024×1024 の RenderTexture を作成（`PaintTexture`）
- [ ] キャラクターマテリアルの MainTexture に PaintTexture をセット
- [ ] 起動時にPaintTextureを透明で初期化するスクリプト作成（`PaintTextureManager.cs`）

#### 4-2. Raycast → UV座標取得
- [ ] カメラ / 照準中心から毎フレームRaycastを発行（スクリプト: `InkGun.cs`）
- [ ] `RaycastHit.textureCoord` でUV座標を取得
- [ ] ヒット対象が `PaintTarget` タグのオブジェクトのみペイント処理へ進む

#### 4-3. ブラシ描画
- [ ] UV座標上に円形ブラシテクスチャをスタンプ描画（`Graphics.Blit`使用）
- [ ] ブラシ形状: 外周が不均一な円形アルファテクスチャを用意（にじみ感）
- [ ] 中心濃く・外周薄いグラデーションアルファを設定
- [ ] 選択中の色でブラシ色を変更（カラー自動サイクルに連動）
- [ ] 重ね塗り可能（同一UV座標へのBlitを許容）

#### 4-4. 濡れた光沢エフェクト（シェーダー）
- [ ] URPカスタムシェーダー作成（`InkPaintShader.shader`）
  - 塗り部分の Smoothness を一時的に上げる（光沢感演出）
  - 縁のにじみ処理（UVオフセットサンプリング）
- [ ] 光沢は着弾後3〜5秒でフェードアウトするタイムライン管理

#### 4-5. インクリセット
- [ ] `PaintTextureManager.ResetTexture()` で全塗りをクリア
- [ ] Rキー → 即時リセット
- [ ] 非操作10秒継続 → 自動リセット + 待機画面へ遷移

---

### Phase 5: インク弾発射・着弾処理

- [ ] `InkGun.cs` に0.5秒間隔の自動発射タイマーを実装
- [ ] スマホが画面外を向いている間は発射停止
- [ ] 銃口オブジェクト（`MuzzlePoint`）から照準方向に弾を生成（`Instantiate`）
- [ ] インク弾はRigidbodyなし・transform.Translateで直進移動
- [ ] 一定距離 or 一定時間で弾を`Destroy`
- [ ] Raycastのヒット地点でPhase 4の描画処理を呼び出す
- [ ] ヒット地点に`InkSplash.prefab`を生成、一定時間後に`Destroy`

---

### Phase 6: ゲームフロー・UI

#### 6-1. ゲーム状態管理
- [ ] `GameStateManager.cs` を作成
  - 状態: `Waiting` / `Playing` / `Ending`
  - ZIG SIMからパケット受信開始 → `Playing`に遷移
  - 非操作10秒 → `Waiting`に戻る（リセット）

#### 6-2. タイマー
- [ ] ゲームプレイ中カウントダウンタイマー（30〜60秒）を表示
- [ ] タイムアップ → インクリセット → 待機画面へ

#### 6-3. カラー自動サイクル
- [ ] 色リスト: 赤・ピンク・オレンジ・黄色・水色・緑・紫
- [ ] 2〜3秒ごとに自動で次の色へ切り替え
- [ ] 現在の色を画面上に小さく表示（カラーインジケーター）

#### 6-4. HUD（Unity UICanvas）
- [ ] 残り時間表示（右上）
- [ ] 現在色インジケーター（左下）
- [ ] 待機中メッセージ: 「スマホを傾けてインクを塗ろう！」
- [ ] Dキー: デバッグオーバーレイ表示切替
- [ ] Rキー: 手動リセット（スタッフ用）
- [ ] F11キー: フルスクリーン切替

---

### Phase 7: 展示準備・最終調整

- [ ] Windows向けにビルド（exe化）
- [ ] ZIG SIM 設定手順カード印刷用ドキュメント作成（IP・ポート記載）
- [ ] WiFiルーター持参・PCに接続しIPアドレス固定設定
- [ ] 展示PC上でexeが自動起動するか確認
- [ ] フルスクリーン・プロジェクター接続テスト
- [ ] 通し動作テスト（来場者ロールプレイ）
- [ ] スタッフ向け操作マニュアル作成（Rキー・Cキー手順）

---

## 実装の依存関係（作業順序）

```
Phase 0（環境）
    ↓
Phase 1-1（Blenderキャラ・UV展開）← これがないとPhase4に進めない
    ↓
Phase 2（ZIG SIM通信）
    ↓
Phase 3（照準制御）
    ↓
Phase 4（ペイントシステム）← 最重要・最難関
    ↓
Phase 1-2（インク弾モデル）と Phase 5（発射処理）を並行
    ↓
Phase 6（ゲームフロー・UI）
    ↓
Phase 7（展示準備）
```

---

## 参考リポジトリから流用するスクリプト一覧

| スクリプト | 場所 | 流用方法 |
|---|---|---|
| `UdpQuaternionReceiver.cs` | Scripts/Pose/ | そのままコピー |
| `QuaternionCoordinateConverter.cs` | Scripts/Pose/ | そのままコピー |
| `PoseRotationDriver.cs` | Scripts/Pose/ | そのままコピー |
| `PoseCalibrationCoordinator.cs` | Scripts/Pose/ | そのままコピー |
| `PoseTestBootstrap.cs` | Scripts/Pose/ | そのままコピー |
| `PoseDebugOverlay.cs` | Scripts/Pose/ | そのままコピー |
| `SpotlightSensor.cs` | Scripts/Stages/ | IsLit/Exposure01を流用・改変 |

---

## 新規作成スクリプト一覧

| スクリプト | 役割 |
|---|---|
| `GameStateManager.cs` | Waiting/Playing/Ending の状態管理 |
| `PaintTextureManager.cs` | RenderTexture初期化・リセット |
| `InkGun.cs` | 自動連射・Raycast・UV描画呼び出し |
| `InkBullet.cs` | インク弾の移動・寿命管理 |
| `InkColorCycler.cs` | カラー自動サイクル管理 |
| `AimingController.cs` | 照準方向の画面内外判定・停止制御 |
| `GameUI.cs` | HUD（タイマー・色インジケーター・メッセージ） |
| `InkPaintShader.shader` | 光沢・にじみ演出URP対応シェーダー |
