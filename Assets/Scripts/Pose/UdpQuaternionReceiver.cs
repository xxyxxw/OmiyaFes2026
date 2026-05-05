using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ZIG SIM から UDP/OSC で送られてくるクォータニオンを受信する。
    /// OSC Bundle / OSC Message / JSON / テキスト / バイナリ 全フォーマット対応。
    /// yugo-ShibaLab-ARD の実績コードをベースに OmiyaFes 向けに移植。
    /// </summary>
    [AddComponentMenu("Networking/UDP Quaternion Receiver")]
    public class UdpQuaternionReceiver : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // 内部型定義
        // ────────────────────────────────────────────────────────────

        private struct OscMessage
        {
            public string Address;
            public string TypeTags;
            public List<float> FloatArguments;
            public List<string> DebugArguments;
        }

        private struct QuaternionPacketParseResult
        {
            public bool Succeeded;
            public bool HasCompleteQuaternion;
            public Quaternion Quaternion;
            public string Message;
        }

        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Header("受信設定")]
        [SerializeField] private int port = 50000;

        [Header("デバッグ設定")]
        [Tooltip("ONにすると受信パケットの詳細をConsoleに出力する")]
        [SerializeField] private bool debugMode = true;
        [Tooltip("ステータスレポートの間隔（秒）。0にすると毎パケット出力")]
        [SerializeField] private float debugReportInterval = 3f;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ（OmiyaFes 互換）
        // ────────────────────────────────────────────────────────────

        /// <summary>最後に受信したクォータニオン（生データ）</summary>
        public Quaternion LatestQuaternion { get; private set; } = Quaternion.identity;

        /// <summary>今フレームにパケットを受信したか（GameStateManager が参照）</summary>
        public bool IsReceiving { get; private set; } = false;

        // 参照リポジトリ互換プロパティ
        public int ReceivedPacketCount { get; private set; }
        public string LastSender { get; private set; } = "-";
        public string LastStatus { get; private set; } = "Waiting for UDP packets...";
        public DateTime LastReceivedTime { get; private set; } = DateTime.MinValue;

        // ── デバッグ用（メインスレッド側でログを出す）────────────
        // 受信スレッドはDebug.Logを直接呼べないため、ここにキューして
        // Update()でまとめて出力する
        private readonly Queue<string> _debugLogQueue = new Queue<string>();
        private float _nextReportTime;
        private int _lastReportedCount;

        // 受信判定用─────────────────────────────────────────────
        // 「直近 N 秒以内にパケットが来たか」で判定する（フレーム単位だと偏りすぎる）
        private const float ReceivingTimeout = 0.5f;  // この秒数以内に受信があればIsReceiving=true
        private float _lastPacketTime = -999f;         // 最後にパケットを受信したメインスレッド時刻

        // ────────────────────────────────────────────────────────────
        // 正規表現（スタティック、コンパイル済み）
        // ────────────────────────────────────────────────────────────

        private static readonly Regex FloatRegex = new Regex(
            @"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        private static readonly Regex JsonQuaternionRegex = new Regex(
            @"""quaternion""\s*:\s*\{\s*""x""\s*:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*,\s*""y""\s*:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*,\s*""z""\s*:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*,\s*""w""\s*:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private readonly object _lock = new object();
        private Socket _socket;        // UdpClientを完全廃止、生 Socket のみ使用
        private Thread _receiveThread;
        private volatile bool _running;

        // メインスレッドへの受け渡しバッファ
        private Quaternion _pendingQuat = Quaternion.identity;
        private bool _hasPending;

        // OSC コンポーネント組み立て用（Bundle内に個別成分が来る場合）
        private Vector4 _partialQuat;
        private bool _hasX, _hasY, _hasZ, _hasW;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            Application.runInBackground = true;
            StartReceiving();
        }

        private void OnDisable() => StopReceiving();
        private void OnDestroy()  => StopReceiving();

        private void Update()
        {
            lock (_lock)
            {
                if (_hasPending)
                {
                    LatestQuaternion = _pendingQuat;
                    _hasPending = false;
                    _lastPacketTime = Time.time;  // 受信時刻を更新
                }
            }

            // 「直近 0.5秒以内にパケットが来た」ならIsReceiving=true
            // フレーム単位だと途切れたフレームで誤半断するため時間ベースにする
            IsReceiving = (Time.time - _lastPacketTime) <= ReceivingTimeout;

            // ── デバッグログをメインスレッドから出力 ──────────────
            if (debugMode)
            {
                FlushDebugLogs();
                EmitPeriodicReport();
            }
        }

        /// <summary>受信スレッドがキューに積んだログをまとめてConsoleに出す</summary>
        private void FlushDebugLogs()
        {
            while (true)
            {
                string msg;
                lock (_lock)
                {
                    if (_debugLogQueue.Count == 0) break;
                    msg = _debugLogQueue.Dequeue();
                }
                Debug.Log(msg);
            }
        }

        /// <summary>一定間隔で受信カウントなどをまとめて出力する</summary>
        private void EmitPeriodicReport()
        {
            if (Time.time < _nextReportTime) return;
            _nextReportTime = Time.time + Mathf.Max(debugReportInterval, 0.5f);

            int current;
            string status, sender;
            lock (_lock)
            {
                current = ReceivedPacketCount;
                status  = LastStatus;
                sender  = LastSender;
            }

            int delta = current - _lastReportedCount;
            _lastReportedCount = current;

            if (current == 0)
            {
                Debug.LogWarning(
                    $"[UdpQR] ⚠ UDP port={port} 待機中 — まだパケット未受信。\n" +
                    "ZIG SIM の IP・ポート・フォーマット設定を確認してください。");
            }
            else
            {
                Debug.Log(
                    $"[UdpQR] ✅ 受信中 port={port} | 合計={current} (+{delta}件/{debugReportInterval:F0}s)\n" +
                    $"   送信元: {sender}\n" +
                    $"   状態:   {status}\n" +
                    $"   姿勢:   x={LatestQuaternion.x:F3} y={LatestQuaternion.y:F3} " +
                    $"z={LatestQuaternion.z:F3} w={LatestQuaternion.w:F3}");
            }
        }

        // ────────────────────────────────────────────────────────────
        // 受信制御
        // ────────────────────────────────────────────────────────────

        private void StartReceiving()
        {
            if (_running) return;
            try
            {
                // ── 生 Socket で直接バインド（UdpClient ラッパーなし）────────────────
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                // SO_EXCLUSIVEADDRUSE: 他プロセスにポートを奢われないように独占
                // Windows 専用だがこれが最も確実な方法
                try
                {
                    _socket.SetSocketOption(SocketOptionLevel.Socket,
                        SocketOptionName.ExclusiveAddressUse, true);
                }
                catch { /* Monoでサポートされない場合はスキップ */ }

                _socket.ReceiveTimeout = 1000; // 1秒タイムアウト
                _socket.Bind(new IPEndPoint(IPAddress.Any, port));

                _running = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "UdpQuaternionReceiver"
                };
                _receiveThread.Start();

                lock (_lock) { LastStatus = $"Listening on UDP {port}"; }
                Debug.Log($"[UdpQuaternionReceiver] 受信開始 port={port} (Socket直接バインド)");
            }
            catch (Exception ex)
            {
                _running = false;
                Debug.LogError($"[UdpQuaternionReceiver] 起動失敗: {ex.Message}", this);
            }
        }

        private void StopReceiving()
        {
            _running = false;
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket = null;
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                if (!_receiveThread.Join(500))
                    _receiveThread.Interrupt();
            }
            _receiveThread = null;
            Debug.Log("[UdpQuaternionReceiver] 受信停止");
        }

        // ────────────────────────────────────────────────────────────
        // 受信ループ（バックグラウンドスレッド）
        // ────────────────────────────────────────────────────────────

        private void ReceiveLoop()
        {
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            var recvBuf  = new byte[65535];
            int loopCount = 0;

            EnqueueDebugLog("[UdpQR] 🟢 受信スレッド 開始");

            while (_running)
            {
                loopCount++;

                if (debugMode && loopCount % 10 == 1)
                    EnqueueDebugLog($"[UdpQR] ⏳ Receive待機中... (loop={loopCount})");

                try
                {
                    EndPoint ep = remoteEP;
                    int size = _socket.ReceiveFrom(recvBuf, ref ep);  // UdpClientなし・生 Socket直接
                    remoteEP = (IPEndPoint)ep;
                    byte[] data = new byte[size];
                    Array.Copy(recvBuf, data, size);

                    // ── パケット到達ログ ────────────────
                    if (debugMode)
                    {
                        string formatHint = size > 0
                            ? (data[0] == (byte)'#' ? "OSC-Bundle" :
                               data[0] == (byte)'/' ? "OSC-Msg" :
                               data[0] == (byte)'{' ? "JSON" : $"Unknown(0x{data[0]:X2})")
                            : "Empty";
                        EnqueueDebugLog(
                            $"[UdpQR] 📦 パケット到達 from={remoteEP} bytes={size} format={formatHint}");
                    }

                    QuaternionPacketParseResult result = TryParseQuaternionPacket(data);

                    if (result.Succeeded && result.HasCompleteQuaternion)
                    {
                        lock (_lock)
                        {
                            _pendingQuat = result.Quaternion;
                            _hasPending  = true;
                            ReceivedPacketCount++;
                            LastSender       = remoteEP.ToString();
                            LastReceivedTime = DateTime.Now;
                            LastStatus       = result.Message;
                        }
                        if (debugMode)
                        {
                            int cnt;
                            lock (_lock) { cnt = ReceivedPacketCount; }
                            if (cnt <= 5 || cnt % 100 == 0)
                                EnqueueDebugLog(
                                    $"[UdpQR] ✅ パース成功 #{cnt} [{result.Message}]\n" +
                                    $"   Quat: x={result.Quaternion.x:F3} y={result.Quaternion.y:F3} " +
                                    $"z={result.Quaternion.z:F3} w={result.Quaternion.w:F3}");
                        }
                    }
                    else
                    {
                        string failMsg = result.Succeeded ? result.Message : "Packet received but parse failed.";
                        lock (_lock)
                        {
                            LastSender       = remoteEP.ToString();
                            LastReceivedTime = DateTime.Now;
                            LastStatus       = failMsg;
                        }
                        if (debugMode)
                        {
                            int cnt;
                            lock (_lock) { cnt = ReceivedPacketCount; }
                            if (cnt < 5)
                            {
                                string hexHead = data.Length > 0
                                    ? BitConverter.ToString(data, 0, Mathf.Min(data.Length, 16))
                                    : "(empty)";
                                EnqueueDebugLog(
                                    $"[UdpQR] ❌ パース失敗 [{failMsg}]\n" +
                                    $"   HEX先頭: {hexHead}");
                            }
                        }
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue; // 1秒タイムアウトは正常（無視）
                }
                catch (ObjectDisposedException) { break; }
                catch (ThreadInterruptedException) { break; }
                catch (SocketException ex)
                {
                    // 以前は無音で食いつぶしていた→必ずログ出力する
                    if (!_running) break;
                    EnqueueDebugLog($"[UdpQR] ⚠ SocketException code={ex.SocketErrorCode} msg={ex.Message}");
                }
                catch (Exception ex)
                {
                    lock (_lock) { LastStatus = "Receive error: " + ex.Message; }
                    EnqueueDebugLog("[UdpQR] ⚠ 受信エラー: " + ex.GetType().Name + " " + ex.Message);
                }
            }

            EnqueueDebugLog("[UdpQR] 🔴 受信スレッド 終了");
        }

        /// <summary>
        /// 受信スレッドからデバッグログをキューに積む（メインスレッドで出力される）
        /// </summary>
        private void EnqueueDebugLog(string message)
        {
            lock (_lock)
            {
                // キューが溢れないよう最大20件に制限
                if (_debugLogQueue.Count < 20)
                    _debugLogQueue.Enqueue(message);
            }
        }

        // ────────────────────────────────────────────────────────────
        // パーサー本体（OSC Bundle → OSC Message → JSON → Text → Binary）
        // ────────────────────────────────────────────────────────────

        private QuaternionPacketParseResult TryParseQuaternionPacket(byte[] data)
        {
            // 1. OSC（Bundle/Message）
            if (TryParseOscPacket(data, out List<OscMessage> oscMessages, out _))
            {
                QuaternionPacketParseResult r = TryBuildQuaternionFromOscMessages(oscMessages);
                if (r.Succeeded) return r;
            }

            // 2. JSON
            if (TryParseTextQuaternion(data, out Quaternion q))
                return CreateCompleteResult(q, "JSON/Text quaternion received.");

            // 3. バイナリ 16byte
            if (TryParseBinaryQuaternion(data, out Quaternion qb))
                return CreateCompleteResult(qb, "Binary quaternion received.");

            return default;
        }

        // ── OSC パケット全体のパース ──────────────────────────────

        private static bool TryParseOscPacket(byte[] data, out List<OscMessage> messages, out string error)
        {
            messages = new List<OscMessage>();
            error    = null;
            if (data == null || data.Length == 0) return false;

            if (IsOscBundle(data, 0, data.Length))
                return TryParseOscBundle(data, 0, data.Length, messages, out error);

            if (data[0] == (byte)'/')
                return TryParseOscMessage(data, 0, data.Length, messages, out error);

            return false;
        }

        private static bool TryParseOscBundle(byte[] data, int start, int length,
            List<OscMessage> messages, out string error)
        {
            error = null;
            int end = start + length;
            if (length < 16 || !IsOscBundle(data, start, length)) return false;

            int offset = start + 16; // #bundle\0 (8) + timetag (8)
            while (offset < end)
            {
                if (offset + 4 > end) { error = "Bundle element size truncated."; return false; }

                int elemSize = ReadIntBigEndian(data, offset);
                offset += 4;
                if (elemSize <= 0 || offset + elemSize > end) { error = "Bundle element size invalid."; return false; }

                if (IsOscBundle(data, offset, elemSize))
                {
                    if (!TryParseOscBundle(data, offset, elemSize, messages, out error)) return false;
                }
                else if (data[offset] == (byte)'/')
                {
                    if (!TryParseOscMessage(data, offset, elemSize, messages, out error)) return false;
                }

                offset += elemSize;
            }
            return messages.Count > 0;
        }

        private static bool TryParseOscMessage(byte[] data, int start, int length,
            List<OscMessage> messages, out string error)
        {
            error = null;
            int end = start + length;
            if (length <= 0 || start < 0 || end > data.Length || data[start] != (byte)'/') return false;

            int addrEnd = FindOscStringEnd(data, start, end);
            if (addrEnd < 0) { error = "OSC address not terminated."; return false; }

            string address = Encoding.ASCII.GetString(data, start, addrEnd - start);

            int typeTagOffset = AlignOscIndex(addrEnd + 1);
            if (typeTagOffset >= end) { error = "OSC typetag offset invalid."; return false; }

            int typeTagEnd = FindOscStringEnd(data, typeTagOffset, end);
            if (typeTagEnd < 0) { error = "OSC typetag not terminated."; return false; }

            string typeTags = Encoding.ASCII.GetString(data, typeTagOffset, typeTagEnd - typeTagOffset);
            if (string.IsNullOrEmpty(typeTags) || typeTags[0] != ',') { error = "OSC typetag invalid."; return false; }

            int payloadOffset = AlignOscIndex(typeTagEnd + 1);
            var floatArgs = new List<float>();
            var debugArgs = new List<string>();

            for (int i = 1; i < typeTags.Length; i++)
            {
                char tag = typeTags[i];
                switch (tag)
                {
                    case 'f':
                        if (payloadOffset + 4 > end) { error = "OSC float truncated."; return false; }
                        float fv = ReadFloatBigEndian(data, payloadOffset); payloadOffset += 4;
                        floatArgs.Add(fv); debugArgs.Add(fv.ToString("F6", CultureInfo.InvariantCulture));
                        break;
                    case 'i':
                        if (payloadOffset + 4 > end) { error = "OSC int truncated."; return false; }
                        int iv = ReadIntBigEndian(data, payloadOffset); payloadOffset += 4;
                        debugArgs.Add(iv.ToString(CultureInfo.InvariantCulture));
                        break;
                    case 'd':
                        if (payloadOffset + 8 > end) { error = "OSC double truncated."; return false; }
                        double dv = ReadDoubleBigEndian(data, payloadOffset); payloadOffset += 8;
                        floatArgs.Add((float)dv); debugArgs.Add(dv.ToString("F6", CultureInfo.InvariantCulture));
                        break;
                    case 'h':
                        if (payloadOffset + 8 > end) { error = "OSC int64 truncated."; return false; }
                        long lv = ReadLongBigEndian(data, payloadOffset); payloadOffset += 8;
                        debugArgs.Add(lv.ToString(CultureInfo.InvariantCulture));
                        break;
                    case 't':
                        if (payloadOffset + 8 > end) { error = "OSC timetag truncated."; return false; }
                        payloadOffset += 8; debugArgs.Add("timetag");
                        break;
                    case 'r': case 'm': case 'c':
                        if (payloadOffset + 4 > end) { error = "OSC 4byte truncated."; return false; }
                        payloadOffset += 4; debugArgs.Add("4byte");
                        break;
                    case 's':
                        int sEnd = FindOscStringEnd(data, payloadOffset, end);
                        if (sEnd < 0) { error = "OSC string truncated."; return false; }
                        string sv = Encoding.ASCII.GetString(data, payloadOffset, sEnd - payloadOffset);
                        payloadOffset = AlignOscIndex(sEnd + 1);
                        debugArgs.Add("\"" + sv + "\"");
                        break;
                    case 'T': debugArgs.Add("true"); break;
                    case 'F': debugArgs.Add("false"); break;
                    case 'N': case 'I': debugArgs.Add(tag == 'N' ? "nil" : "inf"); break;
                    default:
                        error = "Unsupported typetag: " + tag; return false;
                }
            }

            messages.Add(new OscMessage
            {
                Address = address,
                TypeTags = typeTags,
                FloatArguments = floatArgs,
                DebugArguments = debugArgs
            });
            return true;
        }

        // ── クォータニオン組み立て ────────────────────────────────

        private QuaternionPacketParseResult TryBuildQuaternionFromOscMessages(List<OscMessage> messages)
        {
            bool hasQComponent = false;
            for (int i = 0; i < messages.Count; i++)
            {
                OscMessage msg = messages[i];

                // float が 4 個以上 → 1パケット完結
                if (msg.FloatArguments != null && msg.FloatArguments.Count >= 4)
                {
                    Quaternion q = new Quaternion(
                        msg.FloatArguments[0], msg.FloatArguments[1],
                        msg.FloatArguments[2], msg.FloatArguments[3]);
                    if (IsFiniteQuaternion(q))
                        return CreateCompleteResult(q, "OSC quaternion (4-float) received.");
                }

                // 成分ごとに来る場合
                if (msg.FloatArguments == null || msg.FloatArguments.Count != 1) continue;
                string comp = ExtractQuaternionComponent(msg.Address);
                if (string.IsNullOrEmpty(comp)) continue;

                hasQComponent = true;
                QuaternionPacketParseResult partial = AccumulateComponent(comp, msg.FloatArguments[0]);
                if (partial.HasCompleteQuaternion) return partial;
            }

            if (hasQComponent)
                return new QuaternionPacketParseResult
                {
                    Succeeded = true,
                    HasCompleteQuaternion = false,
                    Message = $"Waiting components x:{(_hasX ? _partialQuat.x.ToString("F3") : "-")} y:{(_hasY ? _partialQuat.y.ToString("F3") : "-")} z:{(_hasZ ? _partialQuat.z.ToString("F3") : "-")} w:{(_hasW ? _partialQuat.w.ToString("F3") : "-")}"
                };

            return default;
        }

        private QuaternionPacketParseResult AccumulateComponent(string comp, float value)
        {
            switch (comp)
            {
                case "x": _partialQuat.x = value; _hasX = true; break;
                case "y": _partialQuat.y = value; _hasY = true; break;
                case "z": _partialQuat.z = value; _hasZ = true; break;
                case "w": _partialQuat.w = value; _hasW = true; break;
            }

            if (!(_hasX && _hasY && _hasZ && _hasW))
                return new QuaternionPacketParseResult { Succeeded = true, HasCompleteQuaternion = false };

            Quaternion q = new Quaternion(_partialQuat.x, _partialQuat.y, _partialQuat.z, _partialQuat.w);
            _hasX = _hasY = _hasZ = _hasW = false;

            if (!IsFiniteQuaternion(q)) return default;
            return CreateCompleteResult(q, "OSC quaternion (components) assembled.");
        }

        // ── JSON / テキスト パーサー ──────────────────────────────

        private static bool TryParseTextQuaternion(byte[] data, out Quaternion q)
        {
            q = Quaternion.identity;
            string text = Encoding.UTF8.GetString(data).Trim('\0', ' ', '\r', '\n', '\t');
            if (string.IsNullOrEmpty(text)) return false;

            // JSON 形式
            Match m = JsonQuaternionRegex.Match(text);
            if (m.Success &&
                float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
            {
                q = new Quaternion(x, y, z, w);
                return IsFiniteQuaternion(q);
            }

            // CSV / スペース区切り
            MatchCollection mc = FloatRegex.Matches(text);
            if (mc.Count >= 4 &&
                float.TryParse(mc[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                float.TryParse(mc[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                float.TryParse(mc[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z) &&
                float.TryParse(mc[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out w))
            {
                q = new Quaternion(x, y, z, w);
                return IsFiniteQuaternion(q);
            }

            return false;
        }

        // ── バイナリ 16byte パーサー ──────────────────────────────

        private static bool TryParseBinaryQuaternion(byte[] data, out Quaternion q)
        {
            q = Quaternion.identity;
            if (data == null || data.Length != 16) return false;

            Quaternion le = new Quaternion(
                ReadFloatLittleEndian(data, 0), ReadFloatLittleEndian(data, 4),
                ReadFloatLittleEndian(data, 8), ReadFloatLittleEndian(data, 12));
            Quaternion be = new Quaternion(
                ReadFloatBigEndian(data, 0), ReadFloatBigEndian(data, 4),
                ReadFloatBigEndian(data, 8), ReadFloatBigEndian(data, 12));

            bool leOk = IsFiniteQuaternion(le);
            bool beOk = IsFiniteQuaternion(be);
            if (!leOk && !beOk) return false;
            if (leOk && !beOk) { q = le; return true; }
            if (!leOk) { q = be; return true; }

            float leErr = Mathf.Abs(1f - QuaternionMagnitudeSq(le));
            float beErr = Mathf.Abs(1f - QuaternionMagnitudeSq(be));
            q = leErr <= beErr ? le : be;
            return true;
        }

        // ────────────────────────────────────────────────────────────
        // ユーティリティ
        // ────────────────────────────────────────────────────────────

        private static QuaternionPacketParseResult CreateCompleteResult(Quaternion q, string msg)
            => new QuaternionPacketParseResult { Succeeded = true, HasCompleteQuaternion = true, Quaternion = q, Message = msg };

        private static string ExtractQuaternionComponent(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            string[] parts = address.Trim().ToLowerInvariant()
                .Replace("\\", "/").Replace(":", "/").Trim('/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "quaternion")
                    return NormalizeComponent(parts[i + 1]);

            if (parts.Length > 0)
                return NormalizeComponent(parts[parts.Length - 1]);
            return null;
        }

        private static string NormalizeComponent(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string n = s.Trim().ToLowerInvariant();
            if (n.EndsWith("x")) return "x";
            if (n.EndsWith("y")) return "y";
            if (n.EndsWith("z")) return "z";
            if (n.EndsWith("w")) return "w";
            return null;
        }

        private static bool IsOscBundle(byte[] data, int start, int length)
            => length >= 8 && start >= 0 && start + length <= data.Length
               && data[start] == (byte)'#'
               && Encoding.ASCII.GetString(data, start, 8) == "#bundle\0";

        private static int FindOscStringEnd(byte[] data, int start, int end)
        {
            for (int i = start; i < end; i++)
                if (data[i] == 0) return i;
            return -1;
        }

        private static int AlignOscIndex(int i) => (i + 3) & ~3;

        private static int ReadIntBigEndian(byte[] data, int offset)
        {
            if (!BitConverter.IsLittleEndian) return BitConverter.ToInt32(data, offset);
            byte[] r = new byte[4]; Array.Copy(data, offset, r, 0, 4); Array.Reverse(r);
            return BitConverter.ToInt32(r, 0);
        }

        private static long ReadLongBigEndian(byte[] data, int offset)
        {
            if (!BitConverter.IsLittleEndian) return BitConverter.ToInt64(data, offset);
            byte[] r = new byte[8]; Array.Copy(data, offset, r, 0, 8); Array.Reverse(r);
            return BitConverter.ToInt64(r, 0);
        }

        private static double ReadDoubleBigEndian(byte[] data, int offset)
        {
            if (!BitConverter.IsLittleEndian) return BitConverter.ToDouble(data, offset);
            byte[] r = new byte[8]; Array.Copy(data, offset, r, 0, 8); Array.Reverse(r);
            return BitConverter.ToDouble(r, 0);
        }

        private static float ReadFloatBigEndian(byte[] data, int offset)
        {
            if (!BitConverter.IsLittleEndian) return BitConverter.ToSingle(data, offset);
            byte[] r = new byte[4]; Array.Copy(data, offset, r, 0, 4); Array.Reverse(r);
            return BitConverter.ToSingle(r, 0);
        }

        private static float ReadFloatLittleEndian(byte[] data, int offset)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(data, offset);
            byte[] r = new byte[4]; Array.Copy(data, offset, r, 0, 4); Array.Reverse(r);
            return BitConverter.ToSingle(r, 0);
        }

        private static bool IsFiniteQuaternion(Quaternion q)
            => !float.IsNaN(q.x) && !float.IsInfinity(q.x)
            && !float.IsNaN(q.y) && !float.IsInfinity(q.y)
            && !float.IsNaN(q.z) && !float.IsInfinity(q.z)
            && !float.IsNaN(q.w) && !float.IsInfinity(q.w);

        private static float QuaternionMagnitudeSq(Quaternion q)
            => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
    }
}
