"""
レシーバーにつながっている機器と、その HID++ 機能一覧を調べる（読み取り専用）。

  .venv\\Scripts\\python.exe tools\\probe.py

送るのは「問い合わせ」だけで、マウスやキーボードの設定は一切変更しない。
結果は data/devices.json に保存され、sniff.py の解読に使われる。
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from logigap.decode import DEVICES_JSON  # noqa: E402
from logigap.hidpp import (CID_NAMES, DEFAULT_SWID, FEATURE_NAMES, Device,  # noqa: E402
                           HidppError, Receiver)

KEY_FLAGS = [
    (0x0001, "mse"), (0x0002, "isFn"), (0x0004, "nonstandard"), (0x0008, "fnSensitive"),
    (0x0010, "reprogrammable"), (0x0020, "divertable"), (0x0040, "persistDivertable"),
    (0x0080, "virtual"), (0x0100, "rawXY"), (0x0200, "forceRawXY"), (0x0400, "analytics"),
]


def parse_dpi_list(raw: bytes) -> list[str]:
    vals = [int.from_bytes(raw[i:i + 2], "big") for i in range(0, len(raw) - 1, 2)]
    out = []
    for v in vals:
        if v == 0:
            break
        if v >= 0xE000:
            out.append(f"(刻み{v & 0x1FFF})")
        else:
            out.append(str(v))
    return out


def probe_mouse_bits(dev: Device, info: dict) -> None:
    rx, i = dev.rx, dev.index

    if 0x2201 in dev.features:
        fi = dev.features[0x2201]
        try:
            n = rx.request20(i, fi, 0)[0]
            lst = rx.request20(i, fi, 1, bytes([0]))
            cur = rx.request20(i, fi, 2, bytes([0]))
            dpi = int.from_bytes(cur[1:3], "big")
            default = int.from_bytes(cur[3:5], "big")
            dpi_list = parse_dpi_list(lst[1:])
            print(f"    DPI: 現在 {dpi} / 既定 {default} / センサー数 {n} / 設定可能 {' '.join(dpi_list)}")
            info["dpi"] = {"current": dpi, "default": default, "list": dpi_list}
        except (HidppError, TimeoutError) as e:
            print(f"    DPI: 取得失敗 {e}")

    if 0x2202 in dev.features:
        print("    EXTENDED_ADJUSTABLE_DPI(0x2202) あり（詳細の読み方は未実装）")

    if 0x2205 in dev.features:
        fi = dev.features[0x2205]
        try:
            v = int.from_bytes(rx.request20(i, fi, 0)[0:2], "big")
            print(f"    POINTER_SPEED: {v / 256:.3f}x (raw 0x{v:04X})")
            info["pointer_speed_raw"] = v
        except (HidppError, TimeoutError) as e:
            print(f"    POINTER_SPEED: 取得失敗 {e}")

    if 0x1B04 in dev.features:
        fi = dev.features[0x1B04]
        try:
            count = rx.request20(i, fi, 0)[0]
        except (HidppError, TimeoutError) as e:
            print(f"    REPROG_CONTROLS_V4: 取得失敗 {e}")
            return
        print(f"    ボタン(CID) {count} 個:")
        controls = []
        for k in range(count):
            try:
                r = rx.request20(i, fi, 1, bytes([k]))
                cid = int.from_bytes(r[0:2], "big")
                tid = int.from_bytes(r[2:4], "big")
                flags = r[4] | (r[8] << 8)
                rep = rx.request20(i, fi, 2, cid.to_bytes(2, "big"))
                rflags = rep[2]
                remap = int.from_bytes(rep[3:5], "big")
            except (HidppError, TimeoutError) as e:
                print(f"      #{k}: 取得失敗 {e}")
                continue
            names = ",".join(n for bit, n in KEY_FLAGS if flags & bit)
            state = []
            if rflags & 0x01:
                state.append("★diverted(どこかのアプリが横取り中)")
            if rflags & 0x04:
                state.append("persist")
            if rflags & 0x10:
                state.append("rawXY")
            if remap and remap != cid:
                state.append(f"remap→0x{remap:04X}")
            print(f"      CID 0x{cid:04X} {CID_NAMES.get(cid, ''):<24} TID 0x{tid:04X} [{names}] {' '.join(state)}")
            controls.append({"cid": f"0x{cid:04X}", "tid": f"0x{tid:04X}", "flags": flags,
                             "reporting_flags": rflags, "remap": f"0x{remap:04X}"})
        info["controls"] = controls


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--swid", type=lambda s: int(s, 0), default=DEFAULT_SWID, help="自分のソフトウェアID (既定 0x0E)")
    args = ap.parse_args()

    rx = Receiver(swid=args.swid)
    if not rx.open():
        print("Logi Bolt / Unifying レシーバーが見つかりません")
        return 1
    print(f"レシーバー: {rx.name} (PID 0x{rx.pid:04X})  自分のswid=0x{args.swid:X}")
    result = {"receiver": {"pid": f"0x{rx.pid:04X}", "name": rx.name},
              "probed_at": time.strftime("%Y-%m-%d %H:%M:%S"), "devices": {}}
    try:
        for idx in range(1, 7):
            dev = Device(rx, idx)
            ver = dev.ping()
            if ver is None:
                continue
            try:
                feats = dev.enumerate_features()
                name = dev.name()
            except (HidppError, TimeoutError) as e:
                print(f"\n[dev {idx}] 応答はあるが調査失敗: {e}")
                continue
            print(f"\n[dev {idx}] {name}  HID++ {ver[0]}.{ver[1]}  機能 {len(feats)} 個")
            for f in feats:
                print(f"    [{f['index']:2d}] 0x{f['id']:04X} {FEATURE_NAMES.get(f['id'], '')}")
            info = {"name": name, "protocol": f"{ver[0]}.{ver[1]}",
                    "features": [{"index": f["index"], "id": f"0x{f['id']:04X}",
                                  "name": FEATURE_NAMES.get(f["id"], ""), "version": f["version"]} for f in feats]}
            probe_mouse_bits(dev, info)
            result["devices"][str(idx)] = info
    finally:
        rx.close()

    if not result["devices"]:
        print("\n応答する機器がありませんでした（スリープ中ならマウスを動かして再実行）")
        return 1
    DEVICES_JSON.parent.mkdir(parents=True, exist_ok=True)
    DEVICES_JSON.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n保存しました: {DEVICES_JSON}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
