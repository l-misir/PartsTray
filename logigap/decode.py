"""受信した HID++ レポートを人が読める形にする（盗み聞きツール・本体アプリ共通）"""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from .hidpp import (CID_NAMES, FEATURE_NAMES, HIDPP10_ERRORS, HIDPP20_ERRORS,
                    OPTIONS_PLUS_SWID, REPORT_LONG, REPORT_SHORT, Msg)

ROOT = Path(__file__).resolve().parent.parent
DEVICES_JSON = ROOT / "data" / "devices.json"

WHO_EVENT = "通知"
WHO_SELF = "自分"
WHO_OPTIONS = "Options+"
WHO_OTHER = "他アプリ"
WHO_RECEIVER = "レシーバー"


@dataclass
class DeviceInfo:
    index: int
    name: str
    by_index: dict[int, int]  # feature index -> feature id


def load_devices(path: Path = DEVICES_JSON) -> dict[int, DeviceInfo]:
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    out: dict[int, DeviceInfo] = {}
    for idx_s, d in data.get("devices", {}).items():
        by_index = {f["index"]: int(f["id"], 16) for f in d.get("features", [])}
        out[int(idx_s)] = DeviceInfo(int(idx_s), d.get("name") or f"dev{idx_s}", by_index)
    return out


@dataclass
class Decoded:
    who: str            # 通知 / 自分 / Options+ / 他アプリ / レシーバー
    swid: int | None
    feature_id: int | None
    func: int | None
    text: str
    tags: set[str]      # 判定用のタグ（"set_cid_reporting", "set_dpi", "link_up" など）
    value: int | None = None  # set_dpi の DPI 値など


def _cid(b: bytes) -> str:
    c = int.from_bytes(b[:2], "big")
    return f"0x{c:04X}({CID_NAMES.get(c, '?')})"


def who_of(swid: int, our_swid: int) -> str:
    if swid == 0:
        return WHO_EVENT
    if swid == our_swid:
        return WHO_SELF
    if swid == OPTIONS_PLUS_SWID:
        return WHO_OPTIONS
    return WHO_OTHER


def decode(msg: Msg, devices: dict[int, DeviceInfo], our_swid: int) -> Decoded:
    r = msg.raw
    if msg.report_id not in (REPORT_SHORT, REPORT_LONG) or len(r) < 7:
        return Decoded("?", None, None, None, f"(非HID++ report 0x{r[0]:02X})", set())

    dev = msg.dev
    dname = "レシーバー" if dev == 0xFF else (devices[dev].name if dev in devices else f"dev{dev}")
    b2, b3, p = msg.b2, msg.b3, msg.params

    # --- HID++ 1.0（レシーバー由来の通知・レジスタ・エラー） ---
    if msg.report_id == REPORT_SHORT and b2 == 0x41:
        flags = p[0]
        link = not (flags & 0x40)
        wpid = (p[2] << 8) | p[1]
        state = "リンク確立" if link else "リンク切断"
        return Decoded(WHO_RECEIVER, None, None, None,
                       f"[{dname}] 接続通知: {state} protocol=0x{b3:02X} flags=0x{flags:02X} wpid=0x{wpid:04X}",
                       {"link_up" if link else "link_down"})
    if msg.report_id == REPORT_SHORT and b2 == 0x40:
        return Decoded(WHO_RECEIVER, None, None, None, f"[{dname}] ペアリング解除通知", {"unpaired"})
    if b2 == 0x8F:
        # [0x10, dev, 0x8F, 元b2, 元b3, エラーコード, 0]
        code = p[1]
        text = f"HID++1.0 エラー 0x{code:02X} {HIDPP10_ERRORS.get(code, '?')} (要求 b2=0x{b3:02X} b3=0x{p[0]:02X})"
        if 0x80 <= b3 <= 0x83:  # レジスタアクセスのエラー。b3 はレジスタ番号でソフトウェアIDは無い
            return Decoded(WHO_RECEIVER, None, None, None, f"[{dname}] レジスタ{text}", {"error10"})
        sw = p[0] & 0x0F
        return Decoded(who_of(sw, our_swid), sw, None, None, f"[{dname}] {text}", {"error10"})
    if dev == 0xFF and 0x80 <= b2 <= 0x83:
        kind = {0x80: "SET_REG", 0x81: "GET_REG", 0x82: "SET_LONG_REG", 0x83: "GET_LONG_REG"}[b2]
        return Decoded(WHO_RECEIVER, None, None, None, f"[{dname}] {kind} 0x{b3:02X} {p.hex(' ')}", {"register"})

    # --- HID++ 2.0 エラー ---
    if msg.report_id == REPORT_LONG and b2 == 0xFF:
        # [0x11, dev, 0xFF, 元featIdx, 元(fn|swid), エラーコード, ...]
        fi, ob3, code = b3, p[0], p[1]
        sw = ob3 & 0x0F
        fid = devices[dev].by_index.get(fi) if dev in devices else None
        return Decoded(who_of(sw, our_swid), sw, fid, ob3 >> 4,
                       f"[{dname}] HID++2.0 エラー {HIDPP20_ERRORS.get(code, hex(code))} "
                       f"({_fname(fid, fi)} fn{ob3 >> 4})", {"error20"})

    # --- HID++ 2.0 通常 ---
    fi, fn, sw = b2, b3 >> 4, b3 & 0x0F
    fid = devices[dev].by_index.get(fi) if dev in devices else None
    who = who_of(sw, our_swid)
    detail, tags, value = _detail(fid, fi, fn, sw, p)
    return Decoded(who, sw, fid, fn, f"[{dname}] {_fname(fid, fi)} fn{fn} {detail}", tags, value)


def _fname(fid: int | None, fi: int) -> str:
    if fid is None:
        return f"feat[{fi}]"
    return f"{FEATURE_NAMES.get(fid, f'0x{fid:04X}')}[{fi}]"


def _detail(fid: int | None, fi: int, fn: int, sw: int,
            p: bytes) -> tuple[str, set[str], int | None]:
    event = sw == 0
    if fi == 0x00:
        if fn == 0:
            return f"getFeature → index={p[0]} type=0x{p[1]:02X} ver={p[2]}", {"root"}, None
        if fn == 1:
            return f"ping → protocol {p[0]}.{p[1]}", {"ping"}, None

    if fid == 0x1B04:  # REPROG_CONTROLS_V4
        if event and fn == 0:
            cids = [p[i:i + 2] for i in range(0, 8, 2) if p[i:i + 2] != b"\x00\x00"]
            pressed = ", ".join(_cid(c) for c in cids) or "なし(離した)"
            return f"divertedButtons 押下中: {pressed}", {"diverted_button"}, None
        if event and fn == 1:
            dx = int.from_bytes(p[0:2], "big", signed=True)
            dy = int.from_bytes(p[2:4], "big", signed=True)
            return f"rawXY dx={dx} dy={dy}", {"raw_xy", "noisy"}, None
        if event and fn == 2:
            # analytics key events: (CID 2バイト, 押下状態 1バイト) の並び
            items = []
            for i in range(0, 15, 3):
                c = int.from_bytes(p[i:i + 2], "big")
                if c:
                    items.append(f"{_cid(p[i:i + 2])}{'押' if p[i + 2] else '離'}")
            return f"analytics {' '.join(items)}", {"analytics", "noisy"}, None
        if fn == 2:
            return (f"getCidReporting {_cid(p)} flags=0x{p[2]:02X} "
                    f"remap=0x{int.from_bytes(p[3:5], 'big'):04X} flags2=0x{p[5]:02X}"), {"get_cid_reporting"}, None
        if fn == 3:
            divert = "横取りON" if p[2] & 0x01 else "横取りOFF"
            return (f"★setCidReporting {_cid(p)} flags=0x{p[2]:02X}({divert}) "
                    f"remap=0x{int.from_bytes(p[3:5], 'big'):04X} flags2=0x{p[5]:02X}"), {"set_cid_reporting"}, None

    if fid == 0x2201:  # ADJUSTABLE_DPI
        dpi = int.from_bytes(p[1:3], "big")
        if fn == 2:
            return f"getSensorDpi sensor={p[0]} dpi={dpi}", {"get_dpi"}, dpi
        if fn == 3:
            return f"★setSensorDpi sensor={p[0]} dpi={dpi}", {"set_dpi"}, dpi

    if fid == 0x2150:  # THUMB_WHEEL
        if event and fn == 0:
            rot = int.from_bytes(p[0:2], "big", signed=True)
            return f"サムホイール回転 {rot}", {"thumb_event", "noisy"}, None
        if fn == 0:
            return (f"getThumbwheelInfo nativeRes={int.from_bytes(p[0:2], 'big')} "
                    f"divertedRes={int.from_bytes(p[2:4], 'big')} caps=0x{p[5]:02X}"), set(), None
        if fn == 1:
            return f"getThumbwheelStatus 横取り={p[0]} 反転={p[1]}", {"get_thumb"}, None
        if fn == 2:
            return f"★setThumbwheelReporting {p[:2].hex(' ')}", {"set_thumb"}, None

    if fid == 0x2121:  # HIRES_WHEEL
        if event:
            return f"ホイールイベント {p[:4].hex(' ')}", {"wheel_event", "noisy"}, None
        if fn in (1, 2):
            m = p[0]
            label = "getWheelMode" if fn == 1 else "★setWheelMode"
            return (f"{label} 横取り={m & 1} 高解像度={(m >> 1) & 1} 反転={(m >> 2) & 1}"), {"wheel_mode"}, None
        if fn == 3:
            return f"getRatchetSwitchState {p[0]}", set(), None

    if fid == 0x2111:  # SMART_SHIFT_ENHANCED
        if fn in (1, 2):
            label = "getRatchetControlMode" if fn == 1 else "★setRatchetControlMode"
            return f"{label} mode={p[0]} autoDisengage={p[1]} torque={p[2]}", {"smartshift"}, None

    if fid == 0x2202 and not event:
        return f"(Extended DPI) {p.hex(' ')}", {"ext_dpi"}, None
    if fid == 0x2205:  # POINTER_SPEED
        v = int.from_bytes(p[0:2], "big")
        if fn == 0:
            return f"getPointerSpeed {v / 256:.3f}x", {"get_speed"}, None
        if fn == 1:
            return f"★setPointerSpeed {v / 256:.3f}x", {"set_speed"}, None
    if fid == 0x1D4B and event and fn == 0:
        return (f"WirelessDeviceStatus status=0x{p[0]:02X} request=0x{p[1]:02X} "
                f"reason=0x{p[2]:02X}（request=1 は「再設定してほしい」の合図）"), {"wireless_status"}, None
    return p.hex(" "), set(), None
