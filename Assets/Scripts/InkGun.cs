using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク銃の主制御。
    ///
    /// ▼ FireMode.Projectile（デフォルト）
    ///   fireInterval 秒間隔で弾 (InkBullet) を生成し、InkBullet が Trigger で PaintTarget を塗る。
    ///   弾速・発射間隔・対象物移動・UV近似による誤差あり。
    ///
    /// ▼ FireMode.DirectRaycast（ARD互換検証用）
    ///   aimTransform.forward で毎フレーム Physics.Raycast して PaintTarget に直接塗る。
    ///   弾の遅延・Trigger遅れ・UV近似なし → スマホ姿勢の精度を純粋に評価できる。
    ///   Debug.DrawRay で Sceneビューにレイを可視化。
    ///
    /// ▼ muzzlePoint / aimTransform が未設定の場合は Warning を出して transform を代用。
    /// </summary>
    public class InkGun : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // FireMode 列挙
        // ────────────────────────────────────────────────────────────

        public enum FireMode
        {
            /// <summary>従来の弾（InkBullet）を生成して飛ばす方式</summary>
            Projectile,
            /// <summary>aimTransform.forward で毎フレーム Raycast して直接塗る方式（ARD互換検証用）</summary>
            DirectRaycast,
        }

        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("発射モード")]
        [Tooltip("DirectRaycast = スマホ向き精度テスト用。Projectile = 従来の弾方式。")]
        [SerializeField] private FireMode fireMode = FireMode.Projectile;

        [Header("銃口オブジェクト")]
        [Tooltip("弾が飛び出す銃口 Transform（Ray の始点）。null なら aimTransform を使うが Warning が出る。")]
        [SerializeField] private Transform muzzlePoint;

        [Header("照準オブジェクト（PoseRotationDriver のターゲット = AimRoot）")]
        [Tooltip("スマホのジャイロで回転する照準 Transform（AimRoot など）。null なら this.transform を使うが Warning が出る。")]
        [SerializeField] private Transform aimTransform;

        [Header("発射設定")]
        [Tooltip("何秒おきに1発発射するか（Projectileモードのみ有効）")]
        [SerializeField] private float fireInterval = 0.3f;

        [Tooltip("弾の飛翔プレファブ（未設定時はプリミティブ Sphere を自動生成）")]
        [SerializeField] private GameObject inkProjectilePrefab;

        [Header("照準内外判定（度）")]
        [Tooltip("照準がこの角度以内ならば発射する")]
        [SerializeField] private float maxAimAngle = 45f;

        [Header("弾のサイズ")]
        [Tooltip("Projectile/DirectRaycastトレーサー共通の弾の見た目サイズ")]
        [SerializeField] private float bulletScale = 0.15f;

        [Header("インク塗りブラシ半径（ピクセル単位）")]
        [Tooltip("着弾点中心から何ピクセル塗るか（例: 128 → 直径256px≈テクスチャの半分）")]
        [SerializeField] [Range(1, 256)] private int brushPixelRadius = 128;

        [Header("DirectRaycast 視覚エフェクト")]
        [Tooltip("DirectRaycastモードでも飛んでいく球（トレーサー）を表示するか")]
        [SerializeField] private bool showDirectRaycastTracer = true;
        [Tooltip("トレーサー球の飛翔速度（m/s）")]
        [SerializeField] private float tracerSpeed = 30f;

        [Header("DirectRaycast 設定")]
        [Tooltip("Raycast の最大距離（m）")]
        [SerializeField] private float raycastMaxDistance = 100f;
        [Tooltip("Raycast がヒットしたとき PaintTarget を塗るか（false にすると方向確認のみ）")]
        [SerializeField] private bool raycastDoPaint = true;
        [Tooltip("Debug.DrawRay の表示時間（秒）")]
        [SerializeField] private float debugRayDuration = 0.05f;

        // ────────────────────────────────────────────────────────────
        // 内部参照・状態
        // ────────────────────────────────────────────────────────────

        private InkColorCycler _colorCycler;
        private Camera          _mainCamera;
        private float           _fireTimer = 0f;
        private bool            _muzzleWarningEmitted  = false;
        private bool            _aimWarningEmitted     = false;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _colorCycler = FindObjectOfType<InkColorCycler>();
            _mainCamera  = Camera.main;

            // 起動時に null チェック
            if (aimTransform == null)
            {
                Debug.LogWarning(
                    "[InkGun] ⚠ aimTransform が未設定です。this.transform にフォールバックします。\n" +
                    "Inspector で AimRoot などの PoseRotationDriver が回転する Transform を設定してください。", this);
                _aimWarningEmitted = true;
            }
            if (muzzlePoint == null)
            {
                Debug.LogWarning(
                    "[InkGun] ⚠ muzzlePoint が未設定です。aimTransform（またはthis.transform）を銃口として使います。\n" +
                    "Inspector で AimRoot の子 Transform（MuzzlePoint）を設定してください。", this);
                _muzzleWarningEmitted = true;
            }

            Debug.Log(
                $"[InkGun] 起動設定\n" +
                $"  fireMode      = {fireMode}\n" +
                $"  aimTransform  = {(aimTransform != null ? aimTransform.name : "null (→ this.transform)")}\n" +
                $"  muzzlePoint   = {(muzzlePoint  != null ? muzzlePoint.name  : "null (→ aimTransform)")}");
        }

        private void Update()
        {
            if (GameStateManager.Instance == null) return;
            if (GameStateManager.Instance.CurrentState != GameStateManager.GameState.Playing) return;

            switch (fireMode)
            {
                case FireMode.DirectRaycast:
                    UpdateDirectRaycast();
                    break;
                case FireMode.Projectile:
                    UpdateProjectile();
                    break;
            }
        }

        // ────────────────────────────────────────────────────────────
        // DirectRaycast モード（毎フレーム）
        // ────────────────────────────────────────────────────────────

        private void UpdateDirectRaycast()
        {
            Transform aim    = ResolveAimTransform();
            Transform muzzle = ResolveMuzzlePoint();

            Vector3 origin    = muzzle.position;
            // aim.forward = スマホの向き（DirectMappingモード）= レイの方向
            Vector3 direction = aim.forward;

            // Sceneビューにレイを可視化
            Debug.DrawRay(origin, direction * raycastMaxDistance, Color.green, debugRayDuration);

            // 発射タイマー（常に進む）
            _fireTimer += Time.deltaTime;
            if (_fireTimer < fireInterval) return;
            _fireTimer = 0f;

            // IsAimingAtScreenチェックを外した（常にRaycast実行）
            RaycastHit hit;
            if (!Physics.Raycast(origin, direction, out hit, raycastMaxDistance))
            {
                Debug.Log($"[InkGun] DirectRaycast: ヒットなし direction={direction:F3}");
                return;
            }

            // 命中情報ログ
            Debug.Log(
                $"[InkGun] DirectRaycast: ヒット!\n" +
                $"  object = {hit.collider.name}\n" +
                $"  point  = {hit.point}\n" +
                $"  normal = {hit.normal}\n" +
                $"  textureCoord = {hit.textureCoord}");

            if (!raycastDoPaint) return;

            var paintTarget = hit.collider.GetComponent<PaintTarget>();
            if (paintTarget == null)
            {
                Debug.Log($"[InkGun] DirectRaycast: {hit.collider.name} に PaintTarget なし");
                return;
            }

            // UV取得: MeshCollider なら textureCoord が正確
            Vector2 uv;
            bool exactUV = false;
            var meshCol = hit.collider as MeshCollider;
            if (meshCol != null && hit.textureCoord.sqrMagnitude > 0f)
            {
                uv      = hit.textureCoord;
                exactUV = true;
            }
            else
            {
                // MeshCollider なし → BoundsPointToUV で近似
                uv = BoundsPointToUV(hit.collider, hit.point);
                if (meshCol == null)
                    Debug.LogWarning(
                        $"[InkGun] DirectRaycast: {hit.collider.name} に MeshCollider がありません。\n" +
                        "UV は BoundsPointToUV（近似）を使います。正確な塗りには MeshCollider が必要です。");
            }

            Color inkColor = _colorCycler != null ? _colorCycler.CurrentColor : Color.white;
            paintTarget.Paint(uv, inkColor, brushPixelRadius);

            // トレーサー球（視覚エフェクト）
            if (showDirectRaycastTracer)
                SpawnVisualTracer(origin, hit.point, inkColor);

            Debug.Log(
                $"[InkGun] DirectRaycast: Paint 完了\n" +
                $"  PaintTarget = {paintTarget.name}\n" +
                $"  UV          = {uv}  ({(exactUV ? "正確 (MeshCollider)" : "近似 (BoundsPointToUV)")})\n" +
                $"  aimFwd      = {direction:F3}");
        }

        // ────────────────────────────────────────────────────────────
        // Projectile モード
        // ────────────────────────────────────────────────────────────

        private void UpdateProjectile()
        {
            // タイマーは常に進める（IsAimingAtScreenに骐らず一定間隔で必ず発射）
            _fireTimer += Time.deltaTime;
            if (_fireTimer >= fireInterval)
            {
                _fireTimer = 0f;
                FireProjectile();
            }
        }

        private void FireProjectile()
        {
            Transform aim    = ResolveAimTransform();
            Transform muzzle = ResolveMuzzlePoint();

            Vector3 origin    = muzzle.position;
            // aim.forward = スマホの向き（DirectMappingモード）= 弾の飛ぶ方向
            Vector3 direction = aim.forward;

            SoundManager.Instance?.PlayShoot();
            SpawnBullet(origin, direction);
        }

        private void SpawnBullet(Vector3 origin, Vector3 direction)
        {
            // ── オブジェクト生成 ──────────────────────────────────
            GameObject bulletGo;

            if (inkProjectilePrefab != null)
            {
                bulletGo = Instantiate(inkProjectilePrefab, origin, Quaternion.LookRotation(direction));
            }
            else
            {
                bulletGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulletGo.transform.position   = origin;
                bulletGo.transform.rotation   = Quaternion.LookRotation(direction);
                bulletGo.transform.localScale = Vector3.one * bulletScale;

                var sc = bulletGo.GetComponent<SphereCollider>();
                if (sc != null) sc.isTrigger = true;
            }

            // ── 色設定 ────────────────────────────────────────────
            Color inkColor = _colorCycler != null ? _colorCycler.CurrentColor : Color.white;
            var rend = bulletGo.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material       = new Material(rend.sharedMaterial);
                rend.material.color = inkColor;
            }

            // ── SphereCollider を Trigger に設定 ──────────────────
            var col = bulletGo.GetComponent<SphereCollider>();
            if (col == null) col = bulletGo.AddComponent<SphereCollider>();
            col.isTrigger = true;

            // ── Rigidbody (kinematic) ─────────────────────────────
            var rb = bulletGo.GetComponent<Rigidbody>();
            if (rb == null) rb = bulletGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            // ── InkBullet を付与して発射 ──────────────────────────
            var bullet = bulletGo.GetComponent<InkBullet>();
            if (bullet == null) bullet = bulletGo.AddComponent<InkBullet>();
            bullet.Initialize(direction, inkColor, brushPixelRadius);
        }

        // ────────────────────────────────────────────────────────────
        // 照準判定
        // ────────────────────────────────────────────────────────────

        private bool IsAimingAtScreen(Transform aim)
        {
            if (aim == null) return true;
            Vector3 aimDir      = aim.forward;
            Vector3 screenNormal = _mainCamera != null
                ? _mainCamera.transform.forward
                : Vector3.forward;
            return Vector3.Angle(aimDir, screenNormal) <= maxAimAngle;
        }

        // ────────────────────────────────────────────────────────────
        // null フォールバック
        // ────────────────────────────────────────────────────────────

        private Transform ResolveAimTransform()
        {
            if (aimTransform != null) return aimTransform;
            if (!_aimWarningEmitted)
            {
                Debug.LogWarning("[InkGun] ⚠ aimTransform が null です。this.transform を使います。", this);
                _aimWarningEmitted = true;
            }
            return transform;
        }

        private Transform ResolveMuzzlePoint()
        {
            if (muzzlePoint != null) return muzzlePoint;
            if (!_muzzleWarningEmitted)
            {
                Debug.LogWarning("[InkGun] ⚠ muzzlePoint が null です。aimTransform を銃口として使います。", this);
                _muzzleWarningEmitted = true;
            }
            return ResolveAimTransform();
        }

        // ────────────────────────────────────────────────────────────
        // 実行時参照セッター（AimRootSetup から使用）
        // ────────────────────────────────────────────────────────────

        /// <summary>実行時に外部から aimTransform を設定する。</summary>
        public void SetAimTransform(Transform aim)
        {
            aimTransform = aim;
            _aimWarningEmitted = false;
            Debug.Log($"[InkGun] aimTransform を {(aim != null ? aim.name : "null")} に設定しました。");
        }

        /// <summary>実行時に外部から muzzlePoint を設定する。</summary>
        public void SetMuzzlePoint(Transform muzzle)
        {
            muzzlePoint = muzzle;
            _muzzleWarningEmitted = false;
            Debug.Log($"[InkGun] muzzlePoint を {(muzzle != null ? muzzle.name : "null")} に設定しました。");
        }

        // ────────────────────────────────────────────────────────────
        // DirectRaycast 視覚トレーサー
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// DirectRaycastヒット時に銃口→命中点方向へ飛ぶ視覚球を生成する。
        /// ペイント処理はRaycastで完了済みなので、この球はvisualのみ（塗らない）。
        /// </summary>
        private void SpawnVisualTracer(Vector3 origin, Vector3 hitPoint, Color inkColor)
        {
            Vector3 direction = (hitPoint - origin).normalized;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "InkTracer";
            go.transform.position   = origin;
            go.transform.localScale = Vector3.one * bulletScale;

            // Colliderは無効化（再ヒット判定不要）
            var col = go.GetComponent<SphereCollider>();
            if (col != null) Destroy(col);

            // 色を設定
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material       = new Material(rend.sharedMaterial);
                rend.material.color = inkColor;
            }

            // Rigidbody で飛ばす
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity  = false;
            rb.linearVelocity     = direction * tracerSpeed;

            // 命中点付近で自動消滅（飛距離/速度 + 少し余裕）
            float dist    = Vector3.Distance(origin, hitPoint);
            float lifetime = dist / tracerSpeed + 0.15f;
            Destroy(go, lifetime);
        }

        // ────────────────────────────────────────────────────────────
        // UV近似ユーティリティ
        // ────────────────────────────────────────────────────────────

        private static Vector2 BoundsPointToUV(Collider col, Vector3 worldPoint)
        {
            Vector3 local = col.transform.InverseTransformPoint(worldPoint);
            float u = Mathf.Clamp01(local.x + 0.5f);
            float v = Mathf.Clamp01(local.y + 0.5f);
            return new Vector2(u, v);
        }
    }
}
