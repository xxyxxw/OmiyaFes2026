using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// プリミティブオブジェクト（Cube/Sphere）を右側から自動生成し、
    /// ゲーム終了・リセット時に全削除する。
    /// </summary>
    public class ObjectSpawner : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("生成位置")]
        [Tooltip("生成する X 座標（画面右外）")]
        [SerializeField] private float spawnX    = 10f;
        [Tooltip("生成する Y 座標の最小値")]
        [SerializeField] private float spawnYMin = -2f;
        [Tooltip("生成する Y 座標の最大値")]
        [SerializeField] private float spawnYMax =  2f;
        [Tooltip("生成する Z 座標の最小値（手前）")]
        [SerializeField] private float spawnZMin =  0f;
        [Tooltip("生成する Z 座標の最大値（奥）")]
        [SerializeField] private float spawnZMax = 10f;

        [Tooltip("この X 座標を下回ったらオブジェクトを削除（小さいほど長く残る）")]
        [SerializeField] private float destroyX = -24f;

        [Header("移動速度（Units/秒）")]
        [SerializeField] private float moveSpeedMin = 2f;
        [SerializeField] private float moveSpeedMax = 4f;

        [Header("生成間隔（秒）")]
        [SerializeField] private float intervalMin = 1.0f;
        [SerializeField] private float intervalMax = 3.0f;

        [Header("オブジェクトサイズ")]
        [SerializeField] private float sizeMin = 0.8f;
        [SerializeField] private float sizeMax = 1.5f;

        [Header("レイヤー設定")]
        [Tooltip("Project Settings > Tags and Layers で作成した 'PaintTarget' レイヤーの番号")]
        [SerializeField] private int paintTargetLayer = 8;

        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        private Coroutine _spawnCoroutine;

        // 生成したオブジェクトを追跡（リセット時に全削除するため）
        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Start()
        {
            if (GameStateManager.Instance == null)
            {
                Debug.LogWarning("[ObjectSpawner] GameStateManager が見つかりません。");
                return;
            }

            // ゲーム状態イベントを購読
            GameStateManager.Instance.OnGameStart += HandleGameStart;
            GameStateManager.Instance.OnGameEnd   += HandleGameEnd;
            GameStateManager.Instance.OnReset      += HandleReset;
        }

        private void OnDestroy()
        {
            if (GameStateManager.Instance == null) return;
            GameStateManager.Instance.OnGameStart -= HandleGameStart;
            GameStateManager.Instance.OnGameEnd   -= HandleGameEnd;
            GameStateManager.Instance.OnReset      -= HandleReset;
        }

        // ────────────────────────────────────────────────────────────
        // イベントハンドラ
        // ────────────────────────────────────────────────────────────

        private void HandleGameStart()
        {
            if (_spawnCoroutine != null)
                StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = StartCoroutine(SpawnLoop());
            Debug.Log("[ObjectSpawner] 生成ループ開始");
        }

        private void HandleGameEnd()
        {
            StopSpawning();
        }

        private void HandleReset()
        {
            StopSpawning();
            DestroyAllObjects();
        }

        private void StopSpawning()
        {
            if (_spawnCoroutine != null)
            {
                StopCoroutine(_spawnCoroutine);
                _spawnCoroutine = null;
            }
        }

        private void DestroyAllObjects()
        {
            foreach (var obj in _spawnedObjects)
            {
                if (obj != null)
                    Destroy(obj);
            }
            _spawnedObjects.Clear();
            Debug.Log("[ObjectSpawner] 全オブジェクト削除完了");
        }

        // ────────────────────────────────────────────────────────────
        // 生成ループ
        // ────────────────────────────────────────────────────────────

        private IEnumerator SpawnLoop()
        {
            while (true)
            {
                SpawnObject();
                float interval = Random.Range(intervalMin, intervalMax);
                yield return new WaitForSeconds(interval);
            }
        }

        private void SpawnObject()
        {
            // Cube か Sphere をランダム選択
            PrimitiveType type = Random.value > 0.5f
                ? PrimitiveType.Cube
                : PrimitiveType.Sphere;

            GameObject go = GameObject.CreatePrimitive(type);

            // ── 位置・サイズ設定 ──────────────────────────────────
            float y    = Random.Range(spawnYMin, spawnYMax);
            float z    = Random.Range(spawnZMin, spawnZMax);
            float size = Random.Range(sizeMin, sizeMax);
            go.transform.position   = new Vector3(spawnX, y, z);
            go.transform.localScale = Vector3.one * size;
            go.name  = $"FloatingObj_{type}_{_spawnedObjects.Count}";
            go.layer = paintTargetLayer;

            // ── コライダー設定 ────────────────────────────────────
            // デフォルト（Box/Sphere）を除去して MeshCollider に差し替える。
            // MeshCollider（non-convex）なら RaycastHit.textureCoord が使えるため
            // UV 描画が正確にできる。
            var defaultCol = go.GetComponent<Collider>();
            if (defaultCol != null)
                DestroyImmediate(defaultCol);

            var meshCol = go.AddComponent<MeshCollider>();
            meshCol.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            // convex = false（デフォルト）→ textureCoord 有効

            // ── Rigidbody（kinematic）設定 ─────────────────────────
            // FloatingObject が transform.Translate で動かすため kinematic にする。
            // non-convex MeshCollider + kinematic Rigidbody は Unity で有効な組み合わせ。
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            // ── マテリアル：白（塗られる前の初期色）────────────────
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(rend.sharedMaterial);
                rend.material.color = Color.white;
            }

            // ── コンポーネント付与 ─────────────────────────────────
            go.AddComponent<PaintTarget>();

            float speed = Random.Range(moveSpeedMin, moveSpeedMax);
            var floater = go.AddComponent<FloatingObject>();
            floater.Initialize(speed, destroyX);

            // 追跡リストに追加
            _spawnedObjects.Add(go);

            Debug.Log($"[ObjectSpawner] {type} 生成 (y={y:F1}, z={z:F1}, size={size:F1}, speed={speed:F1})");
        }
    }
}
