"""
ZIG SIM 受信診断テスト（Unity不要）
=====================================
1. Unity を停止してから（ポートを空けるため）このスクリプトを実行
2. ZIG SIM から送信する
3. 「パケット受信！」が出ればネットワークはOK → Unity側の問題
4. 何も出なければ → ZIG SIMの設定またはネットワークの問題

実行方法:
  python test_udp_receive.py
"""

import socket
import struct
import threading
import time

LISTEN_PORT = 50000

def decode_packet(data, addr):
    """受信したパケットを解析して表示"""
    first = data[0] if data else 0

    if first == ord('#'):
        kind = "OSC Bundle (#bundle)"
    elif first == ord('/'):
        kind = "OSC Message (/...)"
    elif first == ord('{'):
        kind = f"JSON: {data[:100].decode('utf-8', errors='replace')}"
    else:
        kind = f"Unknown (0x{first:02X})"

    print(f"\n{'='*55}")
    print(f"✅ パケット受信！")
    print(f"   送信元 IP  : {addr[0]}")
    print(f"   送信元Port : {addr[1]}")
    print(f"   バイト数   : {len(data)}")
    print(f"   種別       : {kind}")
    print(f"   HEX先頭    : {data[:24].hex(' ')}")
    print(f"{'='*55}")

def listen():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.settimeout(1.0)

    try:
        sock.bind(('0.0.0.0', LISTEN_PORT))
    except OSError as e:
        print(f"\n❌ ポート{LISTEN_PORT}のバインド失敗: {e}")
        print("   → Unity が起動中の場合は停止してから実行してください")
        return

    print(f"\n{'='*55}")
    print(f"🎧 UDP port={LISTEN_PORT} で待機中...")
    print(f"   ZIG SIM の送信先を次のIPに設定してください:")
    
    # ローカルIPを全部表示
    import socket as s
    hostname = s.gethostname()
    ips = s.getaddrinfo(hostname, None)
    shown = set()
    for r in ips:
        ip = r[4][0]
        if ':' not in ip and ip != '127.0.0.1' and ip not in shown:
            print(f"   ✦ {ip}:{LISTEN_PORT}")
            shown.add(ip)
    
    print(f"{'='*55}\n")
    print("ZIG SIM で送信してください... (Ctrl+C で終了)")

    count = 0
    last_warn = time.time()

    while True:
        try:
            data, addr = sock.recvfrom(65535)
            count += 1
            decode_packet(data, addr)
            last_warn = time.time()
        except socket.timeout:
            if time.time() - last_warn > 5:
                print(f"⏳ 待機中... (5秒間パケットなし) | 受信合計: {count}件")
                last_warn = time.time()
        except KeyboardInterrupt:
            break

    sock.close()
    print(f"\n終了。受信合計: {count}件")

listen()
