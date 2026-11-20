"""
HID++ の最小実装（Windows / hidapi / Logi Bolt・Unifying レシーバー経由）

プロトコルの扱いは Solaar (GPL-2.0) と Mouser (MIT) の実装・公開資料を参考に、
必要な部分だけを書き起こしたもの。

レポート形式
  short: 0x10, 7 バイト  [0x10, dev, b2, b3, p0, p1, p2]
  long : 0x11, 20 バイト [0x11, dev, b2, b3, p0 ... p15]
  HID++ 2.0 では b2 = 機能インデックス, b3 = (関数番号 << 4) | ソフトウェアID
  HID++ 1.0 では b2 = sub_id, b3 = address
"""
from __future__ import annotations

import threading
import time
from dataclasses import dataclass, field
from typing import Callable

import hid

LOGI_VID = 0x046D
RECEIVER_PIDS = {
    0xC548: "Logi Bolt",
    0xC52B: "Unifying",
    0xC532: "Unifying",
}

REPORT_SHORT = 0x10
REPORT_LONG = 0x11
SHORT_LEN = 7
LONG_LEN = 20

# Options+ (logioptionsplus_agent) が使うソフトウェアID。2026-11-18 の抜き差し実験で実測。
OPTIONS_PLUS_SWID = 0x0E
# 自作アプリが名乗るソフトウェアID（1〜15）。Options+ と被らない値にすること。
DEFAULT_SWID = 0x05

FEATURE_NAMES = {
    0x0000: "ROOT",
    0x0001: "FEATURE_SET",
    0x0002: "FEATURE_INFO",
    0x0003: "DEVICE_FW_VERSION",
    0x0004: "DEVICE_UNIT_ID",
    0x0005: "DEVICE_NAME",
    0x0007: "DEVICE_FRIENDLY_NAME",
    0x0008: "KEEP_ALIVE",
    0x0020: "CONFIG_CHANGE",
    0x0021: "CRYPTO_ID",
    0x0030: "TARGET_SOFTWARE",
    0x0080: "WIRELESS_SIGNAL_STRENGTH",
    0x00C2: "DFUCONTROL_SIGNED",
    0x00C3: "DFUCONTROL",
    0x00D0: "DFU",
    0x1000: "BATTERY_STATUS",
    0x1004: "UNIFIED_BATTERY",
    0x1802: "DEVICE_RESET",
    0x1805: "OOBSTATE",
    0x1806: "CONFIG_DEVICE_PROPS",
    0x1814: "CHANGE_HOST",
    0x1815: "HOSTS_INFO",
    0x1981: "BACKLIGHT",
    0x1982: "BACKLIGHT2",
    0x1983: "BACKLIGHT3",
    0x1990: "ILLUMINATION",
    0x19B0: "HAPTIC?",
    0x19C0: "FORCE_SENSING_BUTTON?",
    0x1B04: "REPROG_CONTROLS_V4",
    0x1BC0: "REPORT_HID_USAGE",
    0x1C00: "PERSISTENT_REMAPPABLE_ACTION",
    0x1D4B: "WIRELESS_DEVICE_STATUS",
    0x1DF0: "REMAINING_PAIRING",
    0x1F20: "ADC_MEASUREMENT",
    0x2001: "LEFT_RIGHT_SWAP",
    0x2006: "POINTER_AXIS_ORIENTATION",
    0x2100: "VERTICAL_SCROLLING",
    0x2110: "SMART_SHIFT",
    0x2111: "SMART_SHIFT_ENHANCED",
    0x2120: "HI_RES_SCROLLING",
    0x2121: "HIRES_WHEEL",
    0x2130: "LOWRES_WHEEL",
    0x2150: "THUMB_WHEEL",
    0x2200: "MOUSE_POINTER",
    0x2201: "ADJUSTABLE_DPI",
    0x2202: "EXTENDED_ADJUSTABLE_DPI",
    0x2205: "POINTER_SPEED",
    0x2230: "ANGLE_SNAPPING",
    0x2250: "XY_STATS",
    0x2251: "WHEEL_STATS",
    0x40A0: "FN_INVERSION",
    0x40A2: "NEW_FN_INVERSION",
    0x40A3: "K375S_FN_INVERSION",
    0x4100: "ENCRYPTION",
    0x4220: "LOCK_KEY_STATE",
    0x4521: "KEYBOARD_DISABLE_KEYS",
    0x4522: "KEYBOARD_DISABLE_BY_USAGE",
    0x4530: "DUALPLATFORM",
    0x4531: "MULTIPLATFORM",
    0x4540: "KEYBOARD_LAYOUT_2",
    0x6500: "GESTURE",
    0x6501: "GESTURE_2",
    0x8060: "REPORT_RATE",
}

# REPROG_CONTROLS_V4 の主なコントロールID（CID）
CID_NAMES = {
    0x0050: "左クリック",
    0x0051: "右クリック",
    0x0052: "中ボタン",
    0x0053: "戻る",
    0x0056: "進む",
    0x005B: "左チルト",
    0x005D: "右チルト",
    0x00C3: "ジェスチャーボタン",
    0x00C4: "SmartShift(ホイールモード切替)",
    0x00D7: "仮想ジェスチャーボタン",
    0x01A0: "ハプティック(Actions Ring)",
}

HIDPP20_ERRORS = {
    1: "Unknown",
    2: "InvalidArgument",
    3: "OutOfRange",
    4: "HardwareError",
    5: "LogitechInternal",
    6: "InvalidFeatureIndex",
    7: "InvalidFunctionId",
    8: "Busy",
    9: "Unsupported",
}

HIDPP10_ERRORS = {
    0x01: "InvalidSubID",
    0x02: "InvalidAddress",
    0x03: "InvalidValue",
    0x04: "ConnectFail",
    0x05: "TooManyDevices",
    0x06: "AlreadyExists",
    0x07: "Busy",
    0x08: "UnknownDevice",
    0x09: "ResourceError(未接続/スリープ)",
    0x0A: "RequestUnavailable",
    0x0B: "InvalidParamValue",
    0x0C: "WrongPinCode",
}


class HidppError(Exception):
    def __init__(self, kind: str, code: int):
        self.kind = kind
        self.code = code
        table = HIDPP20_ERRORS if kind == "2.0" else HIDPP10_ERRORS
        super().__init__(f"HID++ {kind} error 0x{code:02X} {table.get(code, '?')}")


@dataclass
class Msg:
    """レシーバーから届いた1件のレポート"""

    mono: float  # time.monotonic()
    wall: float  # time.time()
    raw: bytes

    @property
    def report_id(self) -> int:
        return self.raw[0]

    @property
    def dev(self) -> int:
        return self.raw[1]

    @property
    def b2(self) -> int:
        return self.raw[2]

    @property
    def b3(self) -> int:
        return self.raw[3]

    @property
    def params(self) -> bytes:
        return self.raw[4:]

    @property
    def func(self) -> int:
        return self.b3 >> 4

    @property
    def swid(self) -> int:
        return self.b3 & 0x0F

    def hex(self) -> str:
        return self.raw.hex(" ")


def find_receiver() -> tuple[int, bytes, bytes] | None:
    """(pid, short用パス, long用パス) を返す。見つからなければ None"""
    short_path = long_path = None
    found_pid = None
    for d in hid.enumerate(LOGI_VID, 0):
        if d["product_id"] not in RECEIVER_PIDS or d["usage_page"] != 0xFF00:
            continue
        if found_pid is not None and d["product_id"] != found_pid:
            continue  # レシーバーが複数ある場合は最初の1台だけ
        found_pid = d["product_id"]
        if d["usage"] == 0x0001:
            short_path = d["path"]
        elif d["usage"] == 0x0002:
            long_path = d["path"]
    if found_pid is None or short_path is None or long_path is None:
        return None
    return found_pid, short_path, long_path


@dataclass
class _Pending:
    dev: int
    b2: int
    b3: int
    event: threading.Event = field(default_factory=threading.Event)
    reply: Msg | None = None
    error: HidppError | None = None


class Receiver:
    """
    レシーバーの HID++ コレクション（short/long）を開き、受信を全リスナーへ配る。
    Windows では同じ HID コレクションを複数アプリが開いても、入力レポートは
    全員にコピーされて届く（はず）。この性質で Options+ の通信を観測する。
    """

    def __init__(self, swid: int = DEFAULT_SWID):
        if not 1 <= swid <= 15:
            raise ValueError("swid は 1〜15")
        self.swid = swid
        self.pid: int | None = None
        self._short = None
        self._long = None
        self._threads: list[threading.Thread] = []
        self._stop = threading.Event()
        self._lock = threading.Lock()
        self._pending: list[_Pending] = []
        self._listeners: list[Callable[[Msg], None]] = []
        self.on_lost: Callable[[], None] | None = None
        self._lost_reported = False

    # ---- 開閉 ----
    def open(self) -> bool:
        info = find_receiver()
        if info is None:
            return False
        pid, short_path, long_path = info
        s = hid.device()
        s.open_path(short_path)
        lg = hid.device()
        lg.open_path(long_path)
        self.pid, self._short, self._long = pid, s, lg
        self._stop.clear()
        self._lost_reported = False
        self._threads = [
            threading.Thread(target=self._reader, args=(s,), daemon=True, name="hidpp-short"),
            threading.Thread(target=self._reader, args=(lg,), daemon=True, name="hidpp-long"),
        ]
        for t in self._threads:
            t.start()
        return True

    def close(self) -> None:
        self._stop.set()
        for t in self._threads:
            t.join(timeout=1.0)
        for h in (self._short, self._long):
            if h is not None:
                try:
                    h.close()
                except Exception:
                    pass
        self._short = self._long = None
        self._threads = []

    @property
    def name(self) -> str:
        return RECEIVER_PIDS.get(self.pid or 0, "?")

    def add_listener(self, fn: Callable[[Msg], None]) -> None:
        self._listeners.append(fn)

    # ---- 受信 ----
    def _reader(self, h) -> None:
        while not self._stop.is_set():
            try:
                data = h.read(64, 200)
            except Exception:
                self._report_lost()
                return
            if not data:
                continue
            msg = Msg(time.monotonic(), time.time(), bytes(data))
            self._dispatch(msg)

    def _report_lost(self) -> None:
        with self._lock:
            if self._lost_reported or self._stop.is_set():
                return
            self._lost_reported = True
        if self.on_lost:
            self.on_lost()

    def _dispatch(self, msg: Msg) -> None:
        if len(msg.raw) >= 7:
            self._match_pending(msg)
        for fn in list(self._listeners):
            try:
                fn(msg)
            except Exception as e:  # リスナーの不具合で受信を止めない
                print(f"[listener error] {e!r}")

    def _match_pending(self, msg: Msg) -> None:
        with self._lock:
            for p in self._pending:
                if p.event.is_set() or msg.dev != p.dev:
                    continue
                if msg.b2 == p.b2 and msg.b3 == p.b3:
                    p.reply = msg
                elif msg.report_id == REPORT_LONG and msg.b2 == 0xFF and msg.b3 == p.b2 and msg.params[0] == p.b3:
                    # 2.0 エラー: [0x11, dev, 0xFF, 元featIdx, 元(fn|swid), エラーコード, ...]
                    p.error = HidppError("2.0", msg.params[1])
                elif msg.b2 == 0x8F and msg.b3 == p.b2 and msg.params[0] == p.b3:
                    # 1.0 エラー: [0x10, dev, 0x8F, 元b2, 元b3, エラーコード, 0]
                    p.error = HidppError("1.0", msg.params[1])
                else:
                    continue
                p.event.set()

    # ---- 送信 ----
    def _write(self, report_id: int, dev: int, b2: int, b3: int, params: bytes) -> None:
        length = SHORT_LEN if report_id == REPORT_SHORT else LONG_LEN
        body = bytes([report_id, dev, b2, b3]) + params
        if len(body) > length:
            raise ValueError("params が長すぎる")
        body = body.ljust(length, b"\x00")
        h = self._short if report_id == REPORT_SHORT else self._long
        if h is None:
            raise RuntimeError("レシーバーが開かれていない")
        h.write(body)

    def request(self, dev: int, b2: int, b3: int, params: bytes = b"", *,
                long: bool = True, timeout: float = 1.0) -> Msg:
        p = _Pending(dev, b2, b3)
        with self._lock:
            self._pending.append(p)
        try:
            self._write(REPORT_LONG if long else REPORT_SHORT, dev, b2, b3, params)
            if not p.event.wait(timeout):
                raise TimeoutError(f"応答なし dev={dev} b2=0x{b2:02X} b3=0x{b3:02X}")
            if p.error:
                raise p.error
            assert p.reply is not None
            return p.reply
        finally:
            with self._lock:
                self._pending.remove(p)

    def request20(self, dev: int, feat_index: int, func: int, params: bytes = b"",
                  timeout: float = 2.0) -> bytes:
        """HID++ 2.0 の関数呼び出し。応答のパラメータ部分を返す"""
        b3 = ((func & 0x0F) << 4) | self.swid
        return self.request(dev, feat_index, b3, params, long=True, timeout=timeout).params


class Device:
    """レシーバーにぶら下がる1台（device index 1〜6）"""

    def __init__(self, rx: Receiver, index: int):
        self.rx = rx
        self.index = index
        self.features: dict[int, int] = {}  # feature id -> feature index

    def ping(self, timeout: float = 2.5) -> tuple[int, int] | None:
        """HID++ 2.0 のプロトコルバージョン。応答がなければ None
        （スリープ中の機器は起きるまで 1 秒前後かかる）"""
        try:
            r = self.rx.request20(self.index, 0x00, 1, bytes([0, 0, 0x5A]), timeout=timeout)
        except (HidppError, TimeoutError):
            return None
        return r[0], r[1]

    def feature_index(self, feature_id: int) -> int | None:
        if feature_id in self.features:
            return self.features[feature_id]
        r = self.rx.request20(self.index, 0x00, 0, feature_id.to_bytes(2, "big"))
        idx = r[0]
        if idx == 0 and feature_id != 0:
            return None
        self.features[feature_id] = idx
        return idx

    def enumerate_features(self) -> list[dict]:
        fs = self.feature_index(0x0001)
        if fs is None:
            return []
        count = self.rx.request20(self.index, fs, 0)[0]
        out = [{"index": 0, "id": 0x0000, "type": 0, "version": 0}]
        for i in range(1, count + 1):
            r = self.rx.request20(self.index, fs, 1, bytes([i]))
            fid = int.from_bytes(r[0:2], "big")
            out.append({"index": i, "id": fid, "type": r[2], "version": r[3]})
            self.features[fid] = i
        return out

    def name(self) -> str | None:
        idx = self.feature_index(0x0005)
        if idx is None:
            return None
        length = self.rx.request20(self.index, idx, 0)[0]
        buf = b""
        while len(buf) < length:
            chunk = self.rx.request20(self.index, idx, 1, bytes([len(buf)]))
            if not chunk:
                break
            buf += chunk[: length - len(buf)]
        return buf.rstrip(b"\x00").decode("utf-8", "replace")
