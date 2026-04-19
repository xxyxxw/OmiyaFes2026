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
        [Header("受信設定")]
        [SerializeField] private int port = 8000;

        /// <summary>最後に受信したクォータニオン（iOS座標系）</summary>
        public Quaternion LatestQuaternion { get; private set; } = Quaternion.identity;

        /// <summary>パケットを1フレーム以内に受信したか</summary>
        public bool IsReceiving { get; private set; } = false;

        private UdpClient _udpClient;
        private Thread _receiveThread;
        private bool _running = false;

        // スレッド間共有
        private Quaternion _pendingQuat = Quaternion.identity;
        private bool _hasPending = false;
        private readonly object _lock = new object();

        private void OnEnable()
        {
            StartReceiving();
        }

        private void OnDisable()
        {
            StopReceiving();
        }

        private void Update()
        {
            lock (_lock)
            {
                if (_hasPending)
                {
                    LatestQuaternion = _pendingQuat;
                    IsReceiving = true;
                    _hasPending = false;
                }
                else
                {
                    IsReceiving = false;
                }
            }
        }

        private void StartReceiving()
        {
            _udpClient = new UdpClient(port);
            _running = true;
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();
            Debug.Log($"[UdpQuaternionReceiver] 受信開始 port={port}");
        }

        private void StopReceiving()
        {
            _running = false;
            _udpClient?.Close();
            _receiveThread?.Join(500);
            Debug.Log("[UdpQuaternionReceiver] 受信停止");
        }

        private void ReceiveLoop()
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref remoteEP);
                    if (TryParseOscQuaternion(data, out Quaternion q))
                    {
                        lock (_lock)
                        {
                            _pendingQuat = q;
                            _hasPending = true;
                        }
                    }
                }
                catch (SocketException)
                {
                    // OnDisable 時のソケット閉鎖による例外は無視
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UdpQuaternionReceiver] 受信エラー: {ex.Message}");
                }
            }
        }

        /// <summary>OSC バイナリからクォータニオン (x,y,z,w) を読み出す</summary>
        private bool TryParseOscQuaternion(byte[] data, out Quaternion result)
        {
            result = Quaternion.identity;
            try
            {
                // OSCアドレスを読み飛ばす（null終端 + 4バイトアライン）
                int i = 0;
                while (i < data.Length && data[i] != 0) i++;
                i = AlignTo4(i + 1);

                // タイプタグ文字列を読み飛ばす
                if (i >= data.Length || data[i] != (byte)',') return false;
                while (i < data.Length && data[i] != 0) i++;
                i = AlignTo4(i + 1);

                // float x4 を読む（big-endian）
                float x = ReadFloatBE(data, i);     i += 4;
                float y = ReadFloatBE(data, i);     i += 4;
                float z = ReadFloatBE(data, i);     i += 4;
                float w = ReadFloatBE(data, i);

                result = new Quaternion(x, y, z, w);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int AlignTo4(int v) => (v + 3) & ~3;

        private static float ReadFloatBE(byte[] data, int offset)
        {
            byte[] b = { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(b, 0);
        }
    }
}
