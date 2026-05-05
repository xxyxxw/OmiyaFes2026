"""
ZIG SIM 疑似送信テスト
Unity の UdpQuaternionReceiver が各フォーマットを正しく受信できるか確認する。

使い方:
  1. Unity でシーンを再生
  2. このスクリプトを python test_udp_send.py で実行
  3. Unity の Console に受信ログが出ればOK
"""

import socket
import struct
import json
import time
import math

TARGET_IP   = "127.0.0.1"   # Unity が同じPC上なら localhost
TARGET_PORT = 50000
UUID        = "testdevice01" # ZIG SIM の UUID に相当（何でもOK）

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# ─────────────────────────────────────────────────
# OSC ユーティリティ
# ─────────────────────────────────────────────────

def pad4(data: bytes) -> bytes:
    """4バイト境界にパディング"""
    r = len(data) % 4
    return data + b'\x00' * ((4 - r) % 4)

def osc_string(s: str) -> bytes:
    """OSC 文字列（null終端 + 4バイトアライン）"""
    b = s.encode('ascii') + b'\x00'
    return pad4(b)

def osc_float(f: float) -> bytes:
    """OSC float (big-endian)"""
    return struct.pack('>f', f)

def build_osc_message(address: str, floats: list) -> bytes:
    """単純な OSC メッセージ（float × N）"""
    type_tag = ',' + 'f' * len(floats)
    msg  = osc_string(address)
    msg += osc_string(type_tag)
    for f in floats:
        msg += osc_float(f)
    return msg

def build_osc_bundle(messages: list) -> bytes:
    """OSC Bundle にメッセージリストを梱包する"""
    bundle  = b'#bundle\x00'
    bundle += struct.pack('>Q', 1)   # timetag = 1 (immediate)
    for msg in messages:
        bundle += struct.pack('>i', len(msg))
        bundle += msg
    return bundle

# ─────────────────────────────────────────────────
# テスト用クォータニオン生成（Y軸回転）
# ─────────────────────────────────────────────────

def make_quat(angle_deg: float):
    """Y軸まわりに angle_deg 度回転したクォータニオン"""
    a = math.radians(angle_deg / 2)
    return (0.0, math.sin(a), 0.0, math.cos(a))  # x, y, z, w

# ─────────────────────────────────────────────────
# 送信パターン
# ─────────────────────────────────────────────────

def send_osc_bundle(angle_deg: float):
    """
    ZIG SIM が複数センサーON時に送るフォーマット（OSC Bundle）
    /ZIGSIM/UUID/quat  ,ffff  x y z w
    """
    x, y, z, w = make_quat(angle_deg)
    addr = f"/ZIGSIM/{UUID}/quat"
    msg = build_osc_message(addr, [x, y, z, w])
    bundle = build_osc_bundle([msg])
    sock.sendto(bundle, (TARGET_IP, TARGET_PORT))
    print(f"[Bundle] angle={angle_deg:.0f}° quat=({x:.3f},{y:.3f},{z:.3f},{w:.3f})")

def send_osc_message(angle_deg: float):
    """
    シングル OSC メッセージ（センサー1つのみON時）
    /ZIGSIM/UUID/quat  ,ffff  x y z w
    """
    x, y, z, w = make_quat(angle_deg)
    addr = f"/ZIGSIM/{UUID}/quat"
    msg = build_osc_message(addr, [x, y, z, w])
    sock.sendto(msg, (TARGET_IP, TARGET_PORT))
    print(f"[OSC Msg] angle={angle_deg:.0f}° quat=({x:.3f},{y:.3f},{z:.3f},{w:.3f})")

def send_json(angle_deg: float):
    """JSON フォーマット"""
    x, y, z, w = make_quat(angle_deg)
    payload = json.dumps({"quaternion": {"x": x, "y": y, "z": z, "w": w}})
    sock.sendto(payload.encode('utf-8'), (TARGET_IP, TARGET_PORT))
    print(f"[JSON]    angle={angle_deg:.0f}° quat=({x:.3f},{y:.3f},{z:.3f},{w:.3f})")

# ─────────────────────────────────────────────────
# メイン：3フォーマットを順番に送信してテスト
# ─────────────────────────────────────────────────

print("=" * 55)
print(f"送信先: {TARGET_IP}:{TARGET_PORT}")
print("Unityのシーンが再生中であることを確認してください")
print("=" * 55)
print()

# ── フェーズ 1: OSC Bundle（ZIG SIM 複数センサーON時）──
print("▶ フェーズ1: OSC Bundle 送信（5回）")
for i in range(5):
    send_osc_bundle(i * 30)
    time.sleep(0.2)

time.sleep(0.5)

# ── フェーズ 2: 単体 OSC メッセージ ──────────────────
print("\n▶ フェーズ2: OSC メッセージ送信（5回）")
for i in range(5):
    send_osc_message(90 + i * 30)
    time.sleep(0.2)

time.sleep(0.5)

# ── フェーズ 3: JSON ──────────────────────────────────
print("\n▶ フェーズ3: JSON 送信（5回）")
for i in range(5):
    send_json(180 + i * 30)
    time.sleep(0.2)

time.sleep(0.5)

# ── フェーズ 4: 連続送信（60fps 相当）────────────────
print("\n▶ フェーズ4: 連続送信（60fps × 3秒）OSC Bundle")
for i in range(180):
    angle = (i * 2) % 360
    send_osc_bundle(angle)
    time.sleep(1.0 / 60.0)

print()
print("=" * 55)
print("送信完了！Unity Console を確認してください。")
print("受信できていれば:")
print("  [UdpQuaternionReceiver] 受信開始 port=50000")
print("  OSC quaternion (4-float) received.")
print("  などのログが出ているはずです。")
print("=" * 55)

sock.close()
