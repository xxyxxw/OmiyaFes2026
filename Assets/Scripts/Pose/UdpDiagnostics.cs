using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ZIG SIM の UDP 通信が正しく届いているか診断するスクリプト。
    /// シーン内の任意の GameObject にアタッチして Play するだけで
    /// Console にすべての診断結果が出る。
    /// </summary>
    public class UdpDiagnostics : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Header("診断設定")]
        [Tooltip("UdpQuaternionReceiver と同じポート番号を入れる")]
        [SerializeField] private int listenPort = 50000;

        [Tooltip("受信したパケットの生バイトを16進数でログに出す（詳細デバッグ）")]
        [SerializeField] private bool logRawBytes = true;

        [Tooltip("受信したパケットの内容をテキストとしてもログに出す")]
        [SerializeField] private bool logAsText = true;

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private UdpClient  _udp;
        private Thread     _thread;
        private bool       _running;

        // メインスレッドへのログ受け渡し
        private string     _pendingLog;
        private bool       _hasLog;
        private readonly object _logLock = new object();

        private int  _packetCount  = 0;
        private bool _everReceived = false;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Start()
        {
            // ── STEP 1: ネットワーク情報をログ ─────────────────────
            LogNetworkInterfaces();

            // ── STEP 2: ポートのバインドを試みる ───────────────────
            if (!TryBindPort()) return;

            // ── STEP 3: 受信スレッド開始 ───────────────────────────
            _running = true;
            _thread  = new Thread(ReceiveLoop) { IsBackground = true };
            _thread.Start();

            Debug.Log($"[UdpDiagnostics] ✅ ポート {listenPort} で受信待機開始。ZIG SIM から送信してください。");
            Debug.Log($"[UdpDiagnostics] 📱 ZIG SIM 設定 → IP: <上のネットワーク情報を参照> / Port: {listenPort} / Protocol: UDP / QUATERNION: ON");
        }

        private void Update()
        {
            // バックグラウンドスレッドからのログをメインスレッドで出す
            lock (_logLock)
            {
                if (_hasLog)
                {
                    Debug.Log(_pendingLog);
                    _hasLog    = false;
                    _pendingLog = null;
                }
            }

            // 10秒経っても届いていなければ警告（60フレーム×600 = 約10秒）
            if (!_everReceived && Time.frameCount == 600)
            {
                Debug.LogWarning(
                    "[UdpDiagnostics] ⚠️ 10秒間パケットが届いていません。\n" +
                    "考えられる原因:\n" +
                    "  1. ZIG SIM の IP アドレスが違う（上のネットワーク情報で正しい IP を確認）\n" +
                    "  2. ZIG SIM の Port が違う（" + listenPort + " を設定してください）\n" +
                    "  3. スマホと PC が同じ Wi-Fi につながっていない\n" +
                    "  4. Windows ファイアウォールがポートをブロックしている\n" +
                    "     → PowerShell(管理者)で実行:\n" +
                    "     netsh advfirewall firewall add rule name=\"Unity UDP " + listenPort + "\" dir=in action=allow protocol=UDP localport=" + listenPort
                );
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _udp?.Close();
            _thread?.Join(500);
        }

        // ────────────────────────────────────────────────────────────
        // STEP 1: ネットワークインターフェース一覧
        // ────────────────────────────────────────────────────────────

        private void LogNetworkInterfaces()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[UdpDiagnostics] ── PC のネットワーク情報 ──────────────");

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // ループバック・無効なものはスキップ
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue; // IPv4のみ

                    sb.AppendLine($"  📡 [{ni.Name}] IP: {ip.Address}");
                }
            }

            sb.AppendLine("[UdpDiagnostics] ➡ ZIG SIM の「IP Address」欄に上の IP をいれてください（192.168.0.xxx が一般的な Wi-Fi IP）");
            Debug.Log(sb.ToString());
        }

        // ────────────────────────────────────────────────────────────
        // STEP 2: ポートバインドテスト
        // ────────────────────────────────────────────────────────────

        private bool TryBindPort()
        {
            try
            {
                _udp = new UdpClient(listenPort);
                Debug.Log($"[UdpDiagnostics] ✅ ポート {listenPort} のバインドに成功。ファイアウォールはOKの可能性が高い。");
                return true;
            }
            catch (SocketException ex)
            {
                Debug.LogError(
                    $"[UdpDiagnostics] ❌ ポート {listenPort} のバインドに失敗！\n" +
                    $"エラー: {ex.Message}\n" +
                    $"考えられる原因:\n" +
                    $"  - 別のアプリが同じポートを使っている（Unity を再起動してみてください）\n" +
                    $"  - UdpQuaternionReceiver も同じポートを使っているので競合している\n" +
                    $"    → このスクリプトを使う間は ZigSimReceiver を無効にしてください"
                );
                return false;
            }
        }

        // ────────────────────────────────────────────────────────────
        // STEP 3: 受信ループ
        // ────────────────────────────────────────────────────────────

        private void ReceiveLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    _everReceived = true;
                    _packetCount++;

                    // ── パケット情報をまとめてログ ────────────────
                    var sb = new StringBuilder();
                    sb.AppendLine($"[UdpDiagnostics] 📦 パケット受信 #{_packetCount}");
                    sb.AppendLine($"  送信元: {remote.Address}:{remote.Port}");
                    sb.AppendLine($"  バイト数: {data.Length} bytes");

                    // テキストとして表示
                    if (logAsText)
                    {
                        string text = Encoding.ASCII.GetString(data);
                        sb.AppendLine($"  テキスト表示: {EscapeNonPrintable(text)}");
                    }

                    // 16進数で表示
                    if (logRawBytes)
                    {
                        sb.AppendLine($"  HEX: {BitConverter.ToString(data, 0, Math.Min(data.Length, 64))}");
                    }

                    // OSC フォーマットかどうかチェック
                    AnalyzeOscFormat(data, sb);

                    // メインスレッドに転送
                    lock (_logLock)
                    {
                        _pendingLog = sb.ToString();
                        _hasLog     = true;
                    }
                }
                catch (SocketException)
                {
                    break; // 終了時の正常な例外
                }
                catch (Exception ex)
                {
                    lock (_logLock)
                    {
                        _pendingLog = $"[UdpDiagnostics] 受信エラー: {ex.Message}";
                        _hasLog     = true;
                    }
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        // OSC フォーマット解析
        // ────────────────────────────────────────────────────────────

        private void AnalyzeOscFormat(byte[] data, StringBuilder sb)
        {
            try
            {
                if (data.Length == 0) return;

                // ── OSC Bundle 形式（ZIG SIM は複数センサー有効時にこれで送る） ──
                // Bundle は "#bundle\0" (8バイト) で始まる
                if (data.Length >= 8 && Encoding.ASCII.GetString(data, 0, 7) == "#bundle")
                {
                    sb.AppendLine("  ✅ OSC Bundle フォーマット検出！（ZIG SIM の複数センサー送信）");
                    sb.AppendLine("  ⚠️ 現在の UdpQuaternionReceiver は Bundle 非対応 → 要修正");

                    // Bundle 内の各メッセージアドレスを列挙する
                    // 構造: "#bundle\0" + timetag(8) + [size(4) + message]*
                    bool foundQuat = false;
                    int pos = 16; // ヘッダ8 + タイムタグ8 をスキップ
                    int msgIndex = 0;
                    while (pos + 4 < data.Length)
                    {
                        // メッセージサイズ(big-endian 4バイト)
                        int msgSize = (data[pos] << 24) | (data[pos+1] << 16) | (data[pos+2] << 8) | data[pos+3];
                        pos += 4;
                        if (msgSize <= 0 || pos + msgSize > data.Length) break;

                        // アドレス文字列を読む
                        int addrEnd = pos;
                        while (addrEnd < pos + msgSize && data[addrEnd] != 0) addrEnd++;
                        string addr = Encoding.ASCII.GetString(data, pos, addrEnd - pos);
                        sb.AppendLine($"    [{msgIndex++}] アドレス: \"{addr}\"");

                        // UUID 付きの ZIGSIM アドレスを検出
                        // 例: /ZIGSIM/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx/quat
                        if (addr.StartsWith("/ZIGSIM/", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = addr.Split('/');
                            if (parts.Length >= 4)
                            {
                                string uuid   = parts[2];
                                string sensor = parts[3];
                                sb.AppendLine($"      → ZIG SIM デバイス UUID: {uuid}");
                                sb.AppendLine($"      → センサー: {sensor}");
                                if (sensor.ToLower().Contains("quat"))
                                {
                                    sb.AppendLine($"      ✅ クォータニオンメッセージを確認！");
                                    foundQuat = true;
                                }
                            }
                        }

                        pos += msgSize;
                    }

                    if (foundQuat)
                        sb.AppendLine("  ✅ Bundle 内にクォータニオンあり。UdpQuaternionReceiver を Bundle 対応にすれば動く！");
                    else
                        sb.AppendLine("  ⚠️ Bundle 内にクォータニオンメッセージが見つかりません。ZIG SIM で QUATERNION センサーをONにしてください。");
                }
                // ── 通常の OSC メッセージ（先頭が '/'）──
                else if (data[0] == '/')
                {
                    int i = 0;
                    while (i < data.Length && data[i] != 0) i++;
                    string address = Encoding.ASCII.GetString(data, 0, i);
                    sb.AppendLine($"  ✅ OSC シングルメッセージ検出！ アドレス: \"{address}\"");

                    string laddr = address.ToLower();
                    if (laddr.Contains("quat") || laddr.Contains("attitude") || laddr.Contains("rotation"))
                        sb.AppendLine("  ✅ クォータニオンデータ → UdpQuaternionReceiver で受信できるはず！");
                    else
                        sb.AppendLine("  ⚠️ クォータニオンアドレスではない。ZIG SIM で QUATERNION センサーがONか確認。");
                }
                else
                {
                    sb.AppendLine($"  ⚠️ 未知のフォーマット（先頭バイト: 0x{data[0]:X2} = '{(char)data[0]}'）");
                    sb.AppendLine("  ZIG SIM の Protocol が OSC になっているか確認してください。");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  OSC解析中にエラー: {ex.Message}");
            }
        }

        // 非印刷文字をエスケープして表示
        private string EscapeNonPrintable(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\0')       sb.Append("<NULL>");
                else if (c < ' ')    sb.Append($"<{(int)c}>");
                else                 sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
