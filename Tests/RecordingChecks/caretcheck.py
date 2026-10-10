"""
Caret-following check for the RecordingChecks `caret30` scenario (real keystrokes into a native edit control
placed far from the parked mouse).

For frames sampled from the recording it recovers the visible crop (zoom 2x registration of the frame against the
1x backdrop) and compares the crop with the system caret position the test form logged. Smart Tracking is
following the caret when, while typing, the caret stays inside the crop and the crop is not simply parked on the
(stale) mouse position.

usage: python caretcheck.py DIR [--mp4 FILE] [--ffmpeg PATH] [--type-start 3.0]
"""
import argparse, csv, os, subprocess
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.signal import fftconvolve


def load_gray(path):
    im = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    return 0.2126 * im[..., 0] + 0.7152 * im[..., 1] + 0.0722 * im[..., 2]


def coarse(frame, pat, z0=2.0):
    small = ndimage.zoom(frame, 1 / z0, order=1)
    hp = lambda a: a - ndimage.gaussian_filter(a, 6)
    A, B = hp(pat), hp(small)
    num = fftconvolve(A, B[::-1, ::-1], mode="valid")
    energy = fftconvolve(A * A, np.ones_like(B), mode="valid")
    corr = num / np.sqrt(np.maximum(energy, 1e-9) * np.sum(B * B))
    iy, ix = np.unravel_index(np.argmax(corr), corr.shape)
    return float(corr[iy, ix]), int(ix), int(iy)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--mp4")
    ap.add_argument("--ffmpeg", default="ffmpeg/ffmpeg.exe")
    ap.add_argument("--type-start", type=float, default=3.0)
    ap.add_argument("--lag", type=float, default=0.2, help="video time minus harness clock (s), start-up offset")
    a = ap.parse_args()
    ff = os.path.abspath(a.ffmpeg)
    d = a.dir
    mp4 = a.mp4 or next(os.path.join(r, f) for r, _, fs in os.walk(d) for f in fs if f.lower().endswith(".mp4"))
    fdir = os.path.join(d, "caret-frames")
    os.makedirs(fdir, exist_ok=True)
    for f in os.listdir(fdir):
        os.remove(os.path.join(fdir, f))
    subprocess.run([ff, "-v", "error", "-i", mp4, "-vf", "fps=4", os.path.join(fdir, "f_%03d.png")], check=True)
    frames = sorted(os.listdir(fdir))
    pat = load_gray(os.path.join(d, "pattern-1x.png"))
    H, W = pat.shape
    log = [(float(r["t_seconds"]), int(r["caret_x"]), int(r["caret_y"])) for r in csv.DictReader(open(os.path.join(d, "caret-log.csv")))]
    log = [l for l in log if l[0] >= 0]
    park = (int(W * 0.10), int(H * 0.14))
    print(f"caret log: {len(log)} positions, first {log[0] if log else None}, last {log[-1] if log else None}; mouse parked at {park}")
    print(f"{'t(s)':>5s} {'zoomed':>6s} {'crop centre':>13s} {'caret':>11s} {'caret in crop':>13s} {'caret-to-centre':>16s} {'mouse in crop':>13s}")
    typing = []
    for i, f in enumerate(frames):
        t = i / 4.0
        fr = load_gray(os.path.join(fdir, f))
        ncc, ix, iy = coarse(fr, pat)
        zoomed = ncc > 0.35
        if zoomed:
            x0, y0, cw, ch = ix, iy, W / 2, H / 2
        else:
            x0, y0, cw, ch = 0, 0, W, H
        cx, cy = x0 + cw / 2, y0 + ch / 2
        cur = [l for l in log if l[0] + a.lag <= t]
        caret = cur[-1][1:] if cur else None
        inside = caret is not None and x0 <= caret[0] <= x0 + cw and y0 <= caret[1] <= y0 + ch
        dist = float(np.hypot(caret[0] - cx, caret[1] - cy)) if caret else float("nan")
        mouse_in = x0 <= park[0] <= x0 + cw and y0 <= park[1] <= y0 + ch
        print(f"{t:5.2f} {('2x' if zoomed else '1x'):>6s} ({cx:5.0f},{cy:5.0f}) {str(caret):>11s} {str(bool(inside)):>13s} {dist:14.0f}px {str(mouse_in):>13s}   ncc {ncc:.2f}")
        if t >= a.type_start + 1.5 and caret is not None:
            typing.append((zoomed, inside, dist, mouse_in))
    if typing:
        z = [x for x in typing if x[0]]
        print()
        print(f"typing frames (from {a.type_start + 1.5:.1f} s): {len(typing)}; zoomed {len(z)}")
        if z:
            print(f"  caret inside the zoomed crop : {sum(1 for x in z if x[1])}/{len(z)}")
            print(f"  median caret-to-crop-centre  : {np.median([x[2] for x in z]):.0f} px (crop is 960x540)")
            print(f"  parked mouse inside the crop : {sum(1 for x in z if x[3])}/{len(z)}")


if __name__ == "__main__":
    main()
