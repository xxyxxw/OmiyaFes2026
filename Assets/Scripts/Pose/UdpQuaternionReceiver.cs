using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ZIG SIM から UDP/OSC で送られてくるクォータニオンを受信する。
    /// 受信スレッドで動くため、メインスレッドへの受け渡しは LatestQuaternion プロパティ経由。
    /// </summary>
    public class UdpQuaternionReceiver : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("受信設定")]
        // ZIG SIM 側でも同じポート番号を設定すること（デフォルト: 8000）
        [SerializeField] private int port = 8000;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>最後に受信したクォータニオン（iOS座標系のまま）</summary>
        public Quaternion LatestQuaternion { get; private set; } = Quaternion.identity;

        /// <summary>
        /// 今フレームにパケットを受信したか。
        /// GameStateManager がゲーム開始条件として参照する。
        /// </summary>
        public bool IsReceiving { get; private set; } = false;

        // ────────────────────────────────────────────────────────────
        // 内部状態（スレッドセーフ設計）
        // ────────────────────────────────────────────────────────────

        private UdpClient _udpClient;       // UDP ソケット本体
        private Thread _receiveThread;      // 受信処理を行うバックグラウンドスレッド
        private bool _running = false;      // スレッド継続フラグ

        // ── スレッド間の安全なデータ受け渡し ──────────────────────
        // 受信スレッドが書き込み、メインスレッド(Update)が読み出す
        private Quaternion _pendingQuat = Quaternion.identity; // 受信待ちクォータニオン
        private bool _hasPending = false;                       // 未読データがあるか
        private readonly object _lock = new object();           // lock の対象オブジェクト

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            // GameObject が有効になったら受信開始
            StartReceiving();
        }

        private void OnDisable()
        {
            // GameObject が無効になったら受信停止（ソケットを閉じる）
            StopReceiving();
        }

        private void Update()
        {
            // ── メインスレッドへのデータ受け渡し ──────────────────
            // lock で受信スレッドとの競合を防ぎつつデータを取得する
            lock (_lock)
            {
                if (_hasPending)
                {
                    // 受信スレッドがセットした新しいデータをメインスレッドに反映
                    LatestQuaternion = _pendingQuat;
                    IsReceiving  = true;
                    _hasPending  = false;
                }
                else
                {
                    // このフレームでパケットが来なかった
                    IsReceiving = false;
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        // 受信制御
        // ────────────────────────────────────────────────────────────

        private void StartReceiving()
        {
            _udpClient = new UdpClient(port); // 指定ポートで UDP 待受

            _running = true;

            // IsBackground = true にするとアプリ終了時にスレッドが自動終了する
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();

            Debug.Log($"[UdpQuaternionReceiver] 受信開始 port={port}");
        }

        private void StopReceiving()
        {
            _running = false;
            _udpClient?.Close(); // ソケットを閉じることで Receive() のブロックが解除される

            // スレッドに最大 500ms 待機して終了を待つ（無限待機を防ぐ）
            _receiveThread?.Join(500);

            Debug.Log("[UdpQuaternionReceiver] 受信停止");
        }

        // ────────────────────────────────────────────────────────────
        // 受信ループ（バックグラウンドスレッドで常時実行）
        // ────────────────────────────────────────────────────────────

        private void ReceiveLoop()
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0); // 受信元アドレス（任意）

            while (_running)
            {
                try
                {
                    // データが来るまでここでスレッドがブロックされる
                    byte[] data = _udpClient.Receive(ref remoteEP);

                    // OSC バイナリをパースしてクォータニオンを取り出す
                    if (TryParseOscQuaternion(data, out Quaternion q))
                    {
                        // lock で保護しながらメインスレッド用バッファに書き込む
                        lock (_lock)
                        {
                            _pendingQuat = q;
                            _hasPending  = true;
                        }
                    }
                }
                catch (SocketException)
                {
                    // OnDisable 時のソケット閉鎖による例外は無視してループを抜ける
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UdpQuaternionReceiver] 受信エラー: {ex.Message}");
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        // OSC パーサー
        // ────────────────────────────────────────────────────────────

        /// <summary>OSC バイナリからクォータニオン (x,y,z,w) を読み出す</summary>
        private bool TryParseOscQuaternion(byte[] data, out Quaternion result)
        {
            result = Quaternion.identity;
            try
            {
                // ── OSC フォーマット構造 ──────────────────────────────
                // [アドレス文字列 (null終端)] [4バイトアライン] [タイプタグ ",ffff" (null終端)] [4バイトアライン] [float x4]

                // ① アドレス文字列を読み飛ばす（null終端 + 4バイトアライン）
                int i = 0;
                while (i < data.Length && data[i] != 0) i++;
                i = AlignTo4(i + 1); // null の次バイトから4バイト境界に揃える

                // ② タイプタグ文字列を読み飛ばす（先頭は ',' のはず）
                if (i >= data.Length || data[i] != (byte)',') return false;
                while (i < data.Length && data[i] != 0) i++;
                i = AlignTo4(i + 1);

                // ③ float を 4 つ読む（OSC は big-endian）
                float x = ReadFloatBE(data, i);     i += 4;
                float y = ReadFloatBE(data, i);     i += 4;
                float z = ReadFloatBE(data, i);     i += 4;
                float w = ReadFloatBE(data, i);

                result = new Quaternion(x, y, z, w);
                return true;
            }
            catch
            {
                return false; // パース失敗時は false を返す
            }
        }

        // v を4の倍数に切り上げる（OSC のバイトアライン処理）
        // 例: AlignTo4(5) → 8, AlignTo4(4) → 4
        private static int AlignTo4(int v) => (v + 3) & ~3;

        /// <summary>
        /// big-endian の float を読み出す。
        /// .NET は little-endian なのでバイトを逆順に並べ直してから変換する。
        /// </summary>
        private static float ReadFloatBE(byte[] data, int offset)
        {
            // バイトを逆順に並べ直して little-endian として解釈する
            byte[] b = { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(b, 0);
        }
    }
}
