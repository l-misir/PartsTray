"""
レシーバー上の HID++ 通信を「聞くだけ」で記録する（何も送信しない）。

  .venv\\Scripts\\python.exe tools\\sniff.py            全部画面に出す
  .venv\\Scripts\\python.exe tools\\sniff.py --quiet    画面には要点(★)だけ。常駐させて様子を見る用

機器がつながる（リンク確立）たびに、Options+ が
  - 最初に話しかけるまで
  - ボタンの横取り設定(setCidReporting)を始めるまで
  - DPI を書き込むまで
の秒数を測り、画面に★で表示して logs/timeline.csv に1行ずつ追記する。
全通信の生ログは logs/sniff_*.log に保存される。Ctrl+C で終了。
"""
from __future__ import annotations

import argparse
import csv
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)

from logigap.decode import WHO_OPTIONS, decode, load_devices  # noqa: E402
from logigap.hidpp import DEFAULT_SWID, Msg, Receiver, find_receiver  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
TIMELINE_CSV = ROOT / "logs" / "timeline.csv"
TIMELINE_HEADER = ["リンク確立時刻", "機器", "きっかけ", "Options+初通信(秒)", "Options+横取り開始(秒)",
                   "Options+DPI書込(秒)", "書き込まれたDPI", "備考"]


def fmt_wall(wall: float) -> str:
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(wall)) + f".{int(wall * 1000) % 1000:03d}"


@dataclass
class LinkEvent:
    """1台の機器が1回つながってから、Options+ が設定し終えるまでの記録"""
    dev: int
    name: str
    mono: float
    wall: float
    trigger: str
    first_contact: float | None = None
    first_divert: float | None = None
    first_dpi: float | None = None
    dpi: int | None = None
    written: bool = False

    def row(self, note: str = "") -> list[str]:
        def s(v: float | None) -> str:
            return "" if v is None else f"{v:.3f}"
        return [fmt_wall(self.wall), self.name, self.trigger, s(self.first_contact), s(self.first_divert),
                s(self.first_dpi), "" if self.dpi is None else str(self.dpi), note]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--swid", type=lambda s: int(s, 0), default=DEFAULT_SWID,
                    help="「自分」とみなすソフトウェアID（表示の区別用。送信はしない）")
    ap.add_argument("--duration", type=float, default=0, help="指定秒数で自動終了（0=無制限）")
    ap.add_argument("--quiet", action="store_true", help="画面には要点(★)だけを出す（ログには全部残す）")
    args = ap.parse_args()

    devices = load_devices()
    if not devices:
        print("※ data/devices.json がありません。先に probe.bat を実行すると機能名で表示されます。")

    log_dir = ROOT / "logs"
    log_dir.mkdir(exist_ok=True)
    log_path = log_dir / time.strftime("sniff_%Y%m%d_%H%M%S.log")
    logf = open(log_path, "w", encoding="utf-8")
    print(f"ログ: {log_path}")
    print(f"タイムライン: {TIMELINE_CSV}")

    lock = threading.RLock()
    links: dict[int, LinkEvent] = {}
    receiver_arrival: dict[str, float] = {}

    def out(line: str, console: bool = True) -> None:
        with lock:
            logf.write(line + "\n")
            logf.flush()
            if console:
                print(line)

    def star(line: str) -> None:
        out(f"★ {time.strftime('%H:%M:%S')} {line}", console=True)

    def write_row(ev: LinkEvent, note: str = "") -> None:
        if ev.written:
            return
        ev.written = True
        new = not TIMELINE_CSV.exists()
        with open(TIMELINE_CSV, "a", encoding="utf-8-sig", newline="") as f:
            w = csv.writer(f)
            if new:
                w.writerow(TIMELINE_HEADER)
            w.writerow(ev.row(note))

    def has_dpi(dev: int) -> bool:
        return dev in devices and 0x2201 in devices[dev].by_index.values()

    def finish_all(note: str) -> None:
        for ev in links.values():
            write_row(ev, note if ev.first_divert is None else "")
        links.clear()

    def on_msg(msg: Msg) -> None:
        d = decode(msg, devices, args.swid)
        t0 = receiver_arrival.get("t", msg.mono)
        stamp = time.strftime("%H:%M:%S", time.localtime(msg.wall)) + f".{int(msg.wall * 1000) % 1000:03d}"
        sw = f"swid={d.swid:X}" if d.swid is not None else "      "
        line = f"{stamp} +{msg.mono - t0:8.3f}s {d.who:<8} {sw:<7} {d.text}   | {msg.hex()}"
        out(line, console=not args.quiet and "noisy" not in d.tags)

        dev = msg.dev
        name = devices[dev].name if dev in devices else f"dev{dev}"
        with lock:
            if "link_up" in d.tags:
                if dev in links:
                    write_row(links[dev], "次のリンク確立までに設定されなかった")
                since_rx = msg.mono - receiver_arrival.get("t", msg.mono)
                trigger = (f"レシーバー検出(または記録開始)から{since_rx:.1f}秒" if since_rx < 120
                           else "再接続(スリープ復帰・切替等)")
                links[dev] = LinkEvent(dev, name, msg.mono, msg.wall, trigger)
                star(f"{name}: リンク確立（{trigger}）")
                return
            if "link_down" in d.tags and dev in links:
                write_row(links.pop(dev), "設定前にリンク切断")
                return
            ev = links.get(dev)
            if ev is None or d.who != WHO_OPTIONS:
                return
            dt = msg.mono - ev.mono
            if ev.first_contact is None:
                ev.first_contact = dt
                star(f"{name}: リンク確立から {dt:.2f} 秒で Options+ が最初に通信")
            if "set_cid_reporting" in d.tags and ev.first_divert is None:
                ev.first_divert = dt
                star(f"{name}: リンク確立から {dt:.2f} 秒で Options+ がボタン設定を開始")
            if "set_dpi" in d.tags:
                ev.dpi = d.value
                if ev.first_dpi is None:
                    ev.first_dpi = dt
                star(f"{name}: リンク確立から {dt:.2f} 秒で Options+ が DPI={d.value} を書き込み")
            # 設定開始から 3 秒経ったら一区切り（DPI は既定値→設定値の 2 段で書かれるので少し待つ）
            done_at = ev.first_dpi if has_dpi(dev) else ev.first_divert
            if ev.first_divert is not None and done_at is not None and dt > done_at + 3:
                write_row(ev)

    t_start = time.monotonic()
    try:
        waiting_shown = False
        while True:
            if args.duration and time.monotonic() - t_start > args.duration:
                break
            rx = Receiver(swid=args.swid)
            try:
                opened = rx.open()
            except OSError as e:
                opened = False
                if not waiting_shown:
                    out(f"レシーバーを開けません: {e}")
            if not opened:
                if not waiting_shown:
                    out("レシーバー待ち…（差し込むと記録開始）")
                    waiting_shown = True
                time.sleep(0.2)
                continue
            waiting_shown = False
            receiver_arrival["t"] = time.monotonic()
            lost = threading.Event()
            rx.on_lost = lost.set
            rx.add_listener(on_msg)
            out(f"===== {time.strftime('%H:%M:%S')} レシーバー検出: {rx.name} (PID 0x{rx.pid:04X}) =====")
            last_check = time.monotonic()
            while not lost.is_set():
                if args.duration and time.monotonic() - t_start > args.duration:
                    break
                lost.wait(0.2)
                # 読み取りエラーが来ない場合に備え、1秒ごとにレシーバーの存在も確認
                if time.monotonic() - last_check > 1.0:
                    last_check = time.monotonic()
                    if find_receiver() is None:
                        lost.set()
            rx.close()
            if lost.is_set():
                out(f"===== {time.strftime('%H:%M:%S')} レシーバーが外されました =====")
                with lock:
                    finish_all("設定前にレシーバーが外された")
    except KeyboardInterrupt:
        pass
    finally:
        with lock:
            finish_all("記録終了時点で未設定")
        logf.close()
        print(f"ログ: {log_path}")
        print(f"タイムライン: {TIMELINE_CSV}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
