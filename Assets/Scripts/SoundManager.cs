using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// ゲーム全体の SE / BGM を管理するシングルトン。
    ///
    /// ▼ Inspector 設定
    ///   - Shoot SFX   : 発射音（AudioClip）
    ///   - Hit SFX     : 着弾音（AudioClip）
    ///   - BGM         : BGM（AudioClip）ループ再生
    ///   - SFX Volume  : SE の音量（0〜1）
    ///   - BGM Volume  : BGM の音量（0〜1）
    ///
    /// ▼ 使い方
    ///   SoundManager.Instance?.PlayShoot();  // 発射音
    ///   SoundManager.Instance?.PlayHit();    // 着弾音
    /// </summary>
    [AddComponentMenu("OmiyaFes/Sound Manager")]
    public class SoundManager : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // シングルトン
        // ────────────────────────────────────────────────────────────

        public static SoundManager Instance { get; private set; }

        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("SE クリップ")]
        [Tooltip("インク弾の発射音")]
        [SerializeField] private AudioClip shootSFX;

        [Tooltip("オブジェクトへの着弾音")]
        [SerializeField] private AudioClip hitSFX;

        [Header("BGM")]
        [Tooltip("ループ再生する BGM")]
        [SerializeField] private AudioClip bgmClip;

        [Header("音量")]
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float bgmVolume = 0.5f;

        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        /// <summary>SE 再生用（PlayOneShot で重ね鳴り可能）</summary>
        private AudioSource _sfxSource;

        /// <summary>BGM 専用（ループ）</summary>
        private AudioSource _bgmSource;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // シングルトン保護
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // SE 用 AudioSource を追加
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop        = false;
            _sfxSource.volume      = sfxVolume;

            // BGM 用 AudioSource を追加
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop        = true;
            _bgmSource.volume      = bgmVolume;

            // BGM 即時再生開始
            PlayBGM();
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド（外部から呼ぶ）
        // ────────────────────────────────────────────────────────────

        /// <summary>インク弾の発射音を再生する（重ね鳴りOK）</summary>
        public void PlayShoot()
        {
            if (shootSFX == null) return;
            _sfxSource.PlayOneShot(shootSFX, sfxVolume);
        }

        /// <summary>オブジェクトへの着弾音を再生する（重ね鳴りOK）</summary>
        public void PlayHit()
        {
            if (hitSFX == null) return;
            _sfxSource.PlayOneShot(hitSFX, sfxVolume);
        }

        /// <summary>BGM を再生（最初から）</summary>
        public void PlayBGM()
        {
            if (bgmClip == null)
            {
                Debug.LogWarning("[SoundManager] BGM クリップが設定されていません。Inspector で設定してください。");
                return;
            }
            _bgmSource.clip = bgmClip;
            _bgmSource.Play();
            Debug.Log($"[SoundManager] BGM 再生開始: {bgmClip.name}");
        }

        /// <summary>BGM を一時停止</summary>
        public void PauseBGM()  => _bgmSource.Pause();

        /// <summary>BGM を再開</summary>
        public void ResumeBGM() => _bgmSource.UnPause();

        /// <summary>BGM を停止</summary>
        public void StopBGM()   => _bgmSource.Stop();

        // ────────────────────────────────────────────────────────────
        // 音量 Inspector 変更をリアルタイム反映（Editor 用）
        // ────────────────────────────────────────────────────────────

        private void OnValidate()
        {
            if (_sfxSource != null) _sfxSource.volume = sfxVolume;
            if (_bgmSource != null) _bgmSource.volume = bgmVolume;
        }
    }
}
