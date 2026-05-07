using UnityEngine;
using OmiyaFes2026.Pose;

namespace OmiyaFes2026
{
    /// <summary>
    /// Play Mode 開始時に AimRoot / MuzzlePoint を自動生成し、
    /// PoseRotationDriver と InkGun の参照を接続する。
    ///
    /// ▼ 使い方
    ///   1. ZigSimReceiver の GameObject にこのスクリプトを追加する。
    ///   2. Play → AimRoot と MuzzlePoint が自動生成・接続される。
    ///   3. 安定したら Scene に手動で AimRoot を作り、このスクリプトを外してもよい。
    ///
    /// ▼ 接続されるもの
    ///   - PoseRotationDriver.aimTarget = AimRoot
    ///   - InkGun.aimTransform         = AimRoot
    ///   - InkGun.muzzlePoint          = AimRoot/MuzzlePoint
    /// </summary>
    [AddComponentMenu("OmiyaFes/AimRoot Setup")]
    public class AimRootSetup : MonoBehaviour
    {
        [Header("既存の AimRoot（未設定なら自動生成）")]
        [Tooltip("既に Scene に AimRoot がある場合はここに設定。設定なければ Awake で生成。")]
        [SerializeField] private Transform aimRoot;

        [Header("接続先（未設定なら自動検索）")]
        [SerializeField] private PoseRotationDriver poseDriver;
        [SerializeField] private InkGun inkGun;

        private void Awake()
        {
            // ── 1. AimRoot 確保 ──────────────────────────────────────
            if (aimRoot == null)
            {
                var existingAimRoot = GameObject.Find("AimRoot");
                if (existingAimRoot != null)
                {
                    aimRoot = existingAimRoot.transform;
                    Debug.Log("[AimRootSetup] 既存の AimRoot を発見して使います。");
                }
                else
                {
                    // 新規生成
                    var go = new GameObject("AimRoot");
                    go.transform.SetParent(null); // ルートに配置
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    aimRoot = go.transform;
                    Debug.Log("[AimRootSetup] AimRoot を自動生成しました。");
                }
            }

            // ── 2. MuzzlePoint 確保 ─────────────────────────────────
            var muzzleTf = aimRoot.Find("MuzzlePoint");
            if (muzzleTf == null)
            {
                var muzzleGo = new GameObject("MuzzlePoint");
                muzzleGo.transform.SetParent(aimRoot);
                // 銃口を AimRoot の前方 0.5m に配置（モデルに合わせて調整）
                muzzleGo.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                muzzleGo.transform.localRotation = Quaternion.identity;
                muzzleTf = muzzleGo.transform;
                Debug.Log("[AimRootSetup] MuzzlePoint を自動生成しました（AimRoot の子）。");
            }

            // ── 3. PoseRotationDriver に接続 ─────────────────────────
            if (poseDriver == null)
                poseDriver = FindObjectOfType<PoseRotationDriver>();

            if (poseDriver != null)
            {
                // リフレクションを使わず SerializedField 相当の公開メソッドで設定
                poseDriver.SetAimTarget(aimRoot);
                Debug.Log($"[AimRootSetup] PoseRotationDriver.aimTarget = {aimRoot.name}");
            }
            else
            {
                Debug.LogWarning("[AimRootSetup] ⚠ PoseRotationDriver が見つかりません。手動で接続してください。");
            }

            // ── 4. InkGun に接続 ────────────────────────────────────
            if (inkGun == null)
                inkGun = FindObjectOfType<InkGun>();

            if (inkGun != null)
            {
                inkGun.SetAimTransform(aimRoot);
                inkGun.SetMuzzlePoint(muzzleTf);
                Debug.Log(
                    $"[AimRootSetup] InkGun.aimTransform = {aimRoot.name}\n" +
                    $"[AimRootSetup] InkGun.muzzlePoint  = {muzzleTf.name}");
            }
            else
            {
                Debug.LogWarning("[AimRootSetup] ⚠ InkGun が見つかりません。手動で接続してください。");
            }

            Debug.Log(
                "[AimRootSetup] ✅ セットアップ完了\n" +
                $"  AimRoot     = {aimRoot.name}\n" +
                $"  MuzzlePoint = {muzzleTf.name}\n" +
                "  スマホを動かすと AimRoot が追従します。\n" +
                "  Sceneビューで AimRoot の赤矢印がスマホ操作で動くか確認してください。");
        }
    }
}
