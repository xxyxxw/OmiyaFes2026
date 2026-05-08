# OmiyaFes2026 プログラミング教科書

> このゲームのコードから実際に学べるプログラミング技術をまとめた教科書です。
> コードを読みながら「なぜこう書くのか」を理解することを目標にしています。

---

## 目次

1. [クラスとオブジェクト指向](#1-クラスとオブジェクト指向)
2. [シングルトンパターン](#2-シングルトンパターン)
3. [イベントとデリゲート](#3-イベントとデリゲート)
4. [状態機械（State Machine）](#4-状態機械state-machine)
5. [コルーチン（非同期処理）](#5-コルーチン非同期処理)
6. [マルチスレッド・ロック](#6-マルチスレッドロック)
7. [インタフェース・継承・静的クラス](#7-インタフェース継承静的クラス)
8. [null安全・フォールバック設計](#8-null安全フォールバック設計)
9. [Unityコンポーネント設計](#9-unityコンポーネント設計)

---

## 1. クラスとオブジェクト指向

### クラスとは？

クラスは「設計図」です。1つの責任を持つデータと処理をひとまとめにしたものです。

### 例：`FloatingObject`（シンプルで読みやすい）

📄 参照: [FloatingObject.cs](../Scripts/FloatingObject.cs)

```csharp
public class FloatingObject : MonoBehaviour
{
    // フィールド（このオブジェクト専用のデータ）
    private float _moveSpeed = 3f;
    private Vector3 _rotationAxis = Vector3.up;

    // メソッド（処理）
    public void Initialize(float speed)
    {
        _moveSpeed = speed;
    }

    private void Update()
    {
        transform.Translate(Vector3.left * _moveSpeed * Time.deltaTime, Space.World);
    }
}
```

**ポイント:**
- `private` フィールドは外から見えない（カプセル化）
- `public` メソッドは外から呼べる（公開インタフェース）
- `_` アンダースコア始まりはプライベートフィールドの命名慣習

---

### 責任の分離（Single Responsibility Principle）

このプロジェクトでは、1クラス1責任が徹底されています：

| クラス | 責任 |
|---|---|
| `GameStateManager` | ゲーム状態の管理のみ |
| `InkGun` | 弾の発射のみ |
| `InkBullet` | 弾の飛翔・衝突のみ |
| `PaintTarget` | テクスチャに描くのみ |
| `FloatingObject` | オブジェクトを動かすのみ |

→ 1つのクラスが大きすぎたら「分割すべきサイン」です。

---

## 2. シングルトンパターン

### シングルトンとは？

シーン中に「1つだけ存在させたいオブジェクト」を、どこからでも参照できるようにするパターンです。

📄 参照: [GameStateManager.cs](../Scripts/GameStateManager.cs)

```csharp
public class GameStateManager : MonoBehaviour
{
    // static = クラス全体で1つだけ存在する変数
    public static GameStateManager Instance { get; private set; }

    private void Awake()
    {
        // すでに Instance があれば自分を削除（2個目を防ぐ）
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
}
```

**使う側のコード:**
```csharp
// どのスクリプトからでもアクセスできる
GameStateManager.Instance.StartGame();
float t = GameStateManager.Instance.RemainingTime;
```

**なぜシングルトンを使うか:**
- ゲームの「管理役」は1つだけあればよい
- `FindObjectOfType()` を毎フレーム呼ぶコストが不要
- グローバルなアクセス点を1箇所に集中させる

---

## 3. イベントとデリゲート

### イベントとは？

「何かが起きたとき、登録された処理を全部呼び出す」仕組みです。
通知を送る側（GameStateManager）と受け取る側（ObjectSpawner, GameUI）が直接依存しない設計になります。

📄 参照（送る側）: [GameStateManager.cs](../Scripts/GameStateManager.cs)  
📄 参照（受け取る側）: [ObjectSpawner.cs](../Scripts/ObjectSpawner.cs)

```csharp
// ── GameStateManager（送る側） ──
public event System.Action OnGameStart;  // イベントの宣言
public event System.Action OnGameEnd;

public void StartGame()
{
    OnGameStart?.Invoke();  // 登録された全処理を呼び出す
}
```

```csharp
// ── ObjectSpawner（受け取る側） ──
private void Start()
{
    // += でイベントに「関数」を登録する
    GameStateManager.Instance.OnGameStart += HandleGameStart;
    GameStateManager.Instance.OnReset     += HandleReset;
}

private void OnDestroy()
{
    // -= で登録を解除（メモリリーク防止）
    GameStateManager.Instance.OnGameStart -= HandleGameStart;
}

private void HandleGameStart()
{
    // ゲーム開始時にここが呼ばれる
    _spawnCoroutine = StartCoroutine(SpawnLoop());
}
```

**`?.Invoke()` の意味:**  
`OnGameStart` に誰も登録していない（null）場合でも安全に呼べる書き方。
`?` が「null なら呼ばない」を意味します。

---

## 4. 状態機械（State Machine）

### 状態機械とは？

「今どの状態にいるか」を管理し、状態に応じた処理を切り替える設計パターンです。

📄 参照: [GameStateManager.cs](../Scripts/GameStateManager.cs)

```csharp
// 状態を列挙型（enum）で定義
public enum GameState { Waiting, Playing, Ending }

public GameState CurrentState { get; private set; } = GameState.Waiting;

private void Update()
{
    // 現在の状態に応じてメソッドを切り替える
    switch (CurrentState)
    {
        case GameState.Waiting:  HandleWaiting();  break;
        case GameState.Playing:  HandlePlaying();  break;
        case GameState.Ending:   ResetGame();      break;
    }
}
```

**状態遷移図:**
```
Waiting → (パケット受信) → Playing → (45秒経過) → Ending → (即座に) → Waiting
```

**なぜ状態機械を使うか:**
- `if (isPlaying && !isEnding && !isWaiting)` のような複雑な条件分岐を避けられる
- 状態が増えても `enum` に追加するだけ
- バグが起きたとき「どの状態のロジックか」が明確

---

## 5. コルーチン（非同期処理）

### コルーチンとは？

「一定時間待ってから続きを実行する」処理を書けるUnity専用の仕組みです。
`Thread` より軽く、Unity の Update ループと連携して動きます。

📄 参照: [ObjectSpawner.cs](../Scripts/ObjectSpawner.cs)

```csharp
// IEnumerator を返すメソッドがコルーチン
private IEnumerator SpawnLoop()
{
    while (true)  // 無限ループ（StopCoroutine で止める）
    {
        SpawnObject();  // オブジェクトを生成

        float interval = Random.Range(1.0f, 3.0f);
        yield return new WaitForSeconds(interval);  // ★ここで一時停止
        // interval 秒後に次のループへ続く
    }
}

// 起動・停止の方法
_spawnCoroutine = StartCoroutine(SpawnLoop());
StopCoroutine(_spawnCoroutine);
```

**`yield return` の意味:**
- そこで処理を「一時停止」してUnityに制御を返す
- 次のフレームや指定秒後に「続きから」再開する

---

## 6. マルチスレッド・ロック

### なぜマルチスレッドが必要か？

UDP受信は「ネットワーク待機」が発生するため、メインスレッド（Update）でやると画面が止まります。
バックグラウンドスレッドで受信し、メインスレッドへ安全に渡す設計が必要です。

📄 参照: [UdpQuaternionReceiver.cs](../Scripts/Pose/UdpQuaternionReceiver.cs)

```csharp
// ── バックグラウンドスレッドで受信 ──
private Thread _receiveThread;
private readonly object _lock = new object();  // ロック用オブジェクト

private void StartReceiving()
{
    _receiveThread = new Thread(ReceiveLoop)
    {
        IsBackground = true  // メインが終わったら自動終了
    };
    _receiveThread.Start();
}

private void ReceiveLoop()
{
    while (_running)
    {
        // ここはバックグラウンドスレッド
        int size = _socket.ReceiveFrom(recvBuf, ref ep);

        lock (_lock)  // ★ここでロック（同時アクセス防止）
        {
            _pendingQuat = convertedQuaternion;
            _hasPending  = true;
            ReceivedPacketCount++;
        }
    }
}

// ── メインスレッド（Update）から安全に取り出す ──
public bool ConsumeLatestRotation(out Quaternion rotation)
{
    lock (_lock)  // 同じロックで保護
    {
        if (!_hasPending) { rotation = LatestConvertedRotation; return false; }
        rotation    = _pendingQuat;
        _hasPending = false;
        return true;
    }
}
```

**`lock` の意味:**  
2つのスレッドが同じデータを同時に書き換えると値が壊れます（レースコンディション）。
`lock` ブロックは「1スレッドだけ入れる部屋」を作り、衝突を防ぎます。

---

## 7. インタフェース・継承・静的クラス

### 継承（MonoBehaviour）

📄 参照: [FloatingObject.cs](../Scripts/FloatingObject.cs)

```csharp
// : MonoBehaviour = MonoBehaviour を継承する
public class FloatingObject : MonoBehaviour
{
    // MonoBehaviour の機能（Awake, Update, Destroy など）が使える
    private void Update() { ... }
}
```

### 静的クラス（インスタンス不要なユーティリティ）

「データを持たず計算だけするクラス」は `static` にすると `new` 不要で呼べます。

📄 参照: [QuaternionCalibrationUtility.cs](../Scripts/Pose/QuaternionCalibrationUtility.cs)

```csharp
public static class QuaternionCalibrationUtility
{
    // static メソッド = クラス名.メソッド名() で呼べる
    public static Quaternion CalculateRelativeRotation(
        Quaternion reference, Quaternion current)
    {
        return Quaternion.Inverse(reference) * current;
    }
}

// 使う側（new 不要）
Quaternion rel = QuaternionCalibrationUtility.CalculateRelativeRotation(refQ, nowQ);
```

📄 参照: [QuaternionCoordinateConverter.cs](../Scripts/Pose/QuaternionCoordinateConverter.cs) も同様の静的クラス設計。

### 列挙型（enum）

複数の「選択肢」を型安全に表現できます。

```csharp
// InkGun.cs より
public enum FireMode
{
    Projectile,      // 弾を飛ばす
    DirectRaycast,   // 即座にRaycast
}

[SerializeField] private FireMode fireMode = FireMode.Projectile;

// 使う側
switch (fireMode)
{
    case FireMode.Projectile:    UpdateProjectile();    break;
    case FireMode.DirectRaycast: UpdateDirectRaycast(); break;
}
```

---

## 8. null安全・フォールバック設計

### null チェック

オブジェクトが存在しない（null）場合にアクセスするとクラッシュします。
このプロジェクトでは3つのパターンが使われています。

📄 参照: [InkGun.cs](../Scripts/InkGun.cs), [PoseRotationDriver.cs](../Scripts/Pose/PoseRotationDriver.cs)

```csharp
// パターン1: ?. 演算子（null なら呼ばない）
OnGameStart?.Invoke();

// パターン2: null 合体演算子（null なら代替値）
Color inkColor = _colorCycler != null ? _colorCycler.CurrentColor : Color.white;

// パターン3: フォールバック関数
private Transform ResolveAimTransform()
{
    if (aimTransform != null) return aimTransform;

    // null だった場合は警告を出して代替を返す
    Debug.LogWarning("[InkGun] aimTransform が null です");
    return transform;  // this.transform にフォールバック
}
```

**フォールバック設計の利点:**
- クラッシュせずに「とりあえず動く」状態を維持できる
- `Warning` を出すことで「設定忘れ」に気づける
- デモ・展示では「完全に動かない」より「動くが精度が低い」方が好ましい

---

## 9. Unityコンポーネント設計

### コンポーネント間の接続方法

コンポーネントが互いに参照を持つ方法は3種類あります。

#### 方法A：Inspector で手動設定（最も安全）

```csharp
[SerializeField] private Transform aimTransform;  // Inspector でドラッグ
```

#### 方法B：Awake/Start で自動検索

📄 参照: [GameStateManager.cs](../Scripts/GameStateManager.cs)

```csharp
private void Awake()
{
    // シーン内から型で検索（遅いので Awake で1回だけ）
    _receiver = FindObjectOfType<UdpQuaternionReceiver>();
}
```

#### 方法C：実行時にメソッドで設定（AimRootSetup方式）

📄 参照: [AimRootSetup.cs](../Scripts/AimRootSetup.cs)

```csharp
// Play開始時に自動接続する
private void Awake()
{
    poseDriver = FindObjectOfType<PoseRotationDriver>();
    poseDriver.SetAimTarget(aimRoot);  // 公開メソッドで設定
}
```

---

### `[SerializeField]` と `[Header]` の使い方

```csharp
[Header("発射設定")]          // Inspector にグループ見出しを表示
[Tooltip("何秒おきに発射するか")]  // マウスオーバーで説明表示
[SerializeField] private float fireInterval = 0.3f;  // privateでもInspectorに表示

[Range(1, 256)]               // スライダーで入力範囲を制限
[SerializeField] private int brushPixelRadius = 128;
```

---

### GetComponent の活用

同じ GameObject の別コンポーネントを取得する方法：

📄 参照: [PoseRotationDriver.cs](../Scripts/Pose/PoseRotationDriver.cs)

```csharp
// RequireComponent で「このコンポーネントには必ず UdpQuaternionReceiver が必要」を宣言
[RequireComponent(typeof(UdpQuaternionReceiver))]
public class PoseRotationDriver : MonoBehaviour
{
    private UdpQuaternionReceiver _receiver;

    private void Awake()
    {
        // GetComponent = 同じGameObjectの指定コンポーネントを取得
        _receiver = GetComponent<UdpQuaternionReceiver>();
    }
}
```

**`[RequireComponent]` の効果:**
- Inspectorから誤って削除しようとするとエラーが出る
- スクリプトの「依存関係」をコード上で明示できる

---

## まとめ：このコードで学んだ設計原則

| 原則 | 実装例 |
|---|---|
| 単一責任（1クラス1仕事） | InkGun / InkBullet / PaintTarget の分離 |
| 開放閉鎖（拡張しやすく） | FireMode の enum で新モード追加が容易 |
| null安全 | `?.Invoke()`, フォールバック関数 |
| イベント駆動 | OnGameStart/End/Reset で疎結合 |
| スレッドセーフ | `lock` で受信スレッドとメインスレッドを分離 |
| シングルトン | GameStateManager.Instance |
| コンポーネント化 | PaintTarget・FloatingObject の動的追加 |
