"""
Offline zoom-sharpness analysis for the RecordingChecks `sharp-zoom` scenario.

Inputs : pattern-1x.png (what the screen showed = what the recorder captured),
         pattern-2x.png (the same layout rendered natively at 2x: ground truth for an ideal 2x zoom),
         the recorded mp4 (Smart Tracking zoomed on the pattern).
Method : 1. grab steady-state frames from the recording,
         2. register each to the 1x pattern (zoom z, crop origin ox/oy) by phase correlation + least squares,
         3. build the ground-truth crop from the 2x render at the same registration,
         4. score the recorded frame, and a set of *modelled* pipelines built from the 1x pattern, against
            that ground truth: PSNR, SSIM, high-frequency energy ratio, edge rise width, overshoot (halo).
The modelled pipelines let the stages be separated: filter alone (Catmull-Rom), filter + unsharp (what the
shader does), and then the encoded result — the gap between the last two is encoder/chroma loss.

usage: python sharpness.py DIR [--ffmpeg PATH] [--t0 8 --t1 12]
"""
import argparse, subprocess, sys, os, json
import numpy as np
from PIL import Image
from scipy import ndimage, optimize


def load_gray(path):
    im = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    return 0.2126 * im[..., 0] + 0.7152 * im[..., 1] + 0.0722 * im[..., 2]


def extract_frames(ffmpeg, mp4, t0, t1, out_dir, fps=4):
    os.makedirs(out_dir, exist_ok=True)
    for f in os.listdir(out_dir):
        if f.startswith("f_"):
            os.remove(os.path.join(out_dir, f))
    subprocess.run([ffmpeg, "-v", "error", "-ss", str(t0), "-to", str(t1), "-i", mp4, "-vf", f"fps={fps}",
                    os.path.join(out_dir, "f_%03d.png")], check=True)
    return sorted(os.path.join(out_dir, f) for f in os.listdir(out_dir) if f.startswith("f_"))


# ----------------------------------------------------------------------------- resamplers (mirror the shader)
def cr_weights(t):
    t2, t3 = t * t, t * t * t
    return np.stack([-0.5 * t3 + t2 - 0.5 * t, 1.5 * t3 - 2.5 * t2 + 1.0, -1.5 * t3 + 2.0 * t2 + 0.5 * t, 0.5 * t3 - 0.5 * t2], -1)


def lanczos_weights(t, a=3):
    # taps at distance t+a-1 ... t-a
    d = t[..., None] - np.arange(-(a - 1), a + 1)[None, :]
    w = np.sinc(d) * np.sinc(d / a)
    return w / w.sum(-1, keepdims=True)


def resample(src, z, ox, oy, W, H, kind="cr"):
    """Separable resample of `src` onto a WxH grid: out[px,py] = src(ox + px/z, oy + py/z) in pixel-index coordinates."""
    xs = ox + np.arange(W) / z
    ys = oy + np.arange(H) / z
    xb, yb = np.floor(xs).astype(int), np.floor(ys).astype(int)
    fx, fy = xs - xb, ys - yb
    if kind == "cr":
        wx, wy, taps = cr_weights(fx), cr_weights(fy), np.arange(-1, 3)
    elif kind == "lanczos3":
        wx, wy, taps = lanczos_weights(fx), lanczos_weights(fy), np.arange(-2, 4)
    elif kind == "bilinear":
        wx = np.stack([1 - fx, fx], -1); wy = np.stack([1 - fy, fy], -1); taps = np.arange(0, 2)
    elif kind.startswith("bicubic"):  # Mitchell-Netravali family bicubic<B>_<C>
        B, C = [float(v) for v in kind[7:].split("_")]
        def mn(x):
            x = np.abs(x)
            return np.where(x < 1, ((12 - 9 * B - 6 * C) * x**3 + (-18 + 12 * B + 6 * C) * x**2 + (6 - 2 * B)) / 6,
                            np.where(x < 2, ((-B - 6 * C) * x**3 + (6 * B + 30 * C) * x**2 + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) / 6, 0))
        taps = np.arange(-1, 3)
        wx = mn(fx[:, None] - taps[None, :]); wy = mn(fy[:, None] - taps[None, :])
    else:
        raise ValueError(kind)
    h, w = src.shape
    tmp = np.zeros((h, W))
    for k, d in enumerate(taps):
        idx = np.clip(xb + d, 0, w - 1)
        tmp += src[:, idx] * wx[:, k][None, :]
    out = np.zeros((H, W))
    for k, d in enumerate(taps):
        idx = np.clip(yb + d, 0, h - 1)
        out += tmp[idx, :] * wy[:, k][:, None]
    return np.clip(out, 0, 1)


def unsharp(img, amount):
    k = np.array([1, 2, 1], float) / 4
    blur = ndimage.convolve1d(ndimage.convolve1d(img, k, axis=0, mode="nearest"), k, axis=1, mode="nearest")
    return np.clip(img + amount * (img - blur), 0, 1)


# ----------------------------------------------------------------------------- registration
def coarse(frame, pat1, z0=2.0):
    small = ndimage.zoom(frame, 1 / z0, order=1)
    hp = lambda a: a - ndimage.gaussian_filter(a, 6)
    A = hp(pat1); B = hp(small)
    from scipy.signal import fftconvolve
    num = fftconvolve(A, B[::-1, ::-1], mode="valid")
    energy = fftconvolve(A * A, np.ones_like(B), mode="valid")
    corr = num / np.sqrt(np.maximum(energy, 1e-9) * np.sum(B * B))
    iy, ix = np.unravel_index(np.argmax(corr), corr.shape)
    return float(corr[iy, ix]), ix, iy


def register(frame, pat1, z0=2.0):
    H, W = frame.shape
    small = ndimage.zoom(frame, 1 / z0, order=1)
    hp = lambda a: a - ndimage.gaussian_filter(a, 6)
    A = hp(pat1); B = hp(small)
    # cross-correlate B (small) inside A via FFT
    from scipy.signal import fftconvolve
    num = fftconvolve(A, B[::-1, ::-1], mode="valid")
    ones = np.ones_like(B)
    energy = fftconvolve(A * A, ones, mode="valid")
    corr = num / np.sqrt(np.maximum(energy, 1e-9) * np.sum(B * B))
    iy, ix = np.unravel_index(np.argmax(corr), corr.shape)
    print(f"  coarse NCC peak {corr[iy, ix]:.3f} at ({ix},{iy})", flush=True)
    p0 = np.array([z0, ix, iy], float)

    def loss(p):
        z, ox, oy = p
        # evaluate on a decimated grid for speed
        ys, xs = np.mgrid[200:H-200:4, 300:W-300:4]
        sx, sy = ox + xs / z, oy + ys / z
        m = ndimage.map_coordinates(pat1, [sy, sx], order=3, mode="nearest")
        return float(np.mean((m - frame[200:H-200:4, 300:W-300:4]) ** 2))

    res = optimize.minimize(loss, p0, method="Nelder-Mead", options={"xatol": 2e-3, "fatol": 1e-8, "maxiter": 250})
    return res.x


def best_shift(ref, img, rng=2.0):
    """Sub-pixel shift (dy,dx) of img that best matches ref, found by least squares on a spline-shifted copy."""
    def loss(p):
        s = ndimage.shift(img, (p[0], p[1]), order=3, mode="nearest")
        c = (slice(6, -6), slice(6, -6))
        return float(np.mean((s[c] - ref[c]) ** 2))
    best = optimize.minimize(loss, [0.0, 0.0], method="Nelder-Mead", options={"xatol": 1e-3, "fatol": 1e-10})
    return best.x


# ----------------------------------------------------------------------------- metrics
def ssim(a, b, win=7):
    c1, c2 = 0.01**2, 0.03**2
    f = lambda x: ndimage.uniform_filter(x, win)
    ma, mb = f(a), f(b)
    va, vb, cab = f(a * a) - ma * ma, f(b * b) - mb * mb, f(a * b) - ma * mb
    s = ((2 * ma * mb + c1) * (2 * cab + c2)) / ((ma * ma + mb * mb + c1) * (va + vb + c2))
    return float(np.mean(s[win:-win, win:-win]))


def psnr(a, b):
    mse = np.mean((a - b) ** 2)
    return 99.0 if mse < 1e-12 else float(10 * np.log10(1 / mse))


def hf_ratio(img, ref):
    lap = lambda x: ndimage.laplace(x)
    return float(np.var(lap(img)) / np.var(lap(ref)))


def edge_metrics(img, ref, mask):
    """Edge acuity on pixels that lie on a strong edge of the ground truth.
    rise  : mean over edge pixels of the 10-90 % transition width, estimated as (max-min step)/max gradient along the gradient direction.
    over  : overshoot beyond the ground-truth local range, as a fraction of that range (halo/ringing)."""
    g = ref
    gy, gx = np.gradient(g)
    mag = np.hypot(gx, gy)
    sel = (mag > 0.08) & mask
    gyi, gxi = np.gradient(img)
    magi = np.hypot(gxi, gyi)
    rise = float(np.mean(0.8 / np.maximum(magi[sel], 1e-3))) if sel.any() else float("nan")
    rise_ref = float(np.mean(0.8 / np.maximum(mag[sel], 1e-3))) if sel.any() else float("nan")
    lo = ndimage.minimum_filter(ref, 5); hi = ndimage.maximum_filter(ref, 5)
    over = np.maximum(img - hi, 0) + np.maximum(lo - img, 0)
    rng = np.maximum(hi - lo, 1e-3)
    ov = float(np.mean((over / rng)[sel])) if sel.any() else float("nan")
    return rise, rise_ref, ov



def esf_metrics(img, gt, y0=40, y1=460, x0=200, x1=1800):
    """Edge spread across the rotated black squares. Per row, the left edge of every square is located in the
    ground truth (sub-pixel 0.5 crossing); the image is sampled at fixed offsets from that crossing, which pools
    the slanted edge into an oversampled edge-spread function. Reports the 10-90 % width (px of the zoomed frame),
    the dark-side undershoot (never visible when clipped to 0, so reported only as the ESF minimum) and the
    bright-side overshoot (ESF maximum over 1, which saturate() clips away on pure white)."""
    roi = gt[y0:y1, x0:x1]
    lab, n = ndimage.label(roi < 0.15)
    sizes = ndimage.sum(np.ones_like(roi), lab, range(1, n + 1))
    offs = np.arange(-7, 7.01, 0.25)
    acc = [[] for _ in offs]
    for k in range(1, n + 1):
        if sizes[k - 1] < 8000: continue
        ys, xs = np.where(lab == k)
        yt, yb = ys.min() + 24, ys.max() - 24
        for y in range(yt, yb):
            row = roi[y]
            xr = xs[ys == y]
            if xr.size == 0: continue
            xl = xr.min()
            # sub-pixel crossing of 0.5 left of the first dark pixel
            for xi in range(max(xl - 3, 0), xl + 3):
                if row[xi] >= 0.5 > row[xi + 1]:
                    xc = xi + (row[xi] - 0.5) / (row[xi] - row[xi + 1]); break
            else:
                continue
            vals = ndimage.map_coordinates(img, [np.full(offs.shape, y + y0, float), xc + x0 + offs], order=1, mode="nearest")
            for i, v in enumerate(vals): acc[i].append(v)
    prof = np.array([np.mean(a) if a else np.nan for a in acc])
    hi, lo = np.nanmean(prof[:8]), np.nanmean(prof[-8:])
    norm = (prof - lo) / (hi - lo)
    # 10-90 width by linear interpolation between samples
    def cross(level):
        for i in range(len(offs) - 1):
            if (norm[i] - level) * (norm[i + 1] - level) <= 0 and norm[i] != norm[i + 1]:
                return offs[i] + (level - norm[i]) / (norm[i + 1] - norm[i]) * (offs[i + 1] - offs[i])
        return np.nan
    return float(cross(0.1) - cross(0.9)), float(np.nanmax(norm) - 1.0), float(-np.nanmin(norm))


def study(d, z):
    """Model-only comparison at zoom z against a native z-times render, for a fixed crop centred on the screen centre."""
    pat1 = load_gray(os.path.join(d, "pattern-1x.png"))
    gtimg = load_gray(os.path.join(d, f"pattern-{z:g}x.png"))
    H, W = 1080, 1920
    ox, oy = 960 - W / z / 2, 540 - H / z / 2
    yy, xx = np.mgrid[0:H, 0:W]
    gt = ndimage.map_coordinates(gtimg, [z * (oy + yy / z + 0.5) - 0.5, z * (ox + xx / z + 0.5) - 0.5], order=1, mode="nearest")
    amount = min(1.0, (z - 1.0) * 1.1)
    cr = resample(pat1, z, ox, oy, W, H, "cr")
    lz = resample(pat1, z, ox, oy, W, H, "lanczos3")
    cands = {"ground truth (native render)": gt, "Catmull-Rom": cr}
    for k in (0.5, 0.75, 1.0, 1.25):
        cands[f"Catmull-Rom + unsharp({k})"] = unsharp(cr, k)
    cands["  (shipping amount %.2f)" % amount] = unsharp(cr, amount)
    cands["Lanczos3"] = lz
    for k in (0.3, 0.5, 0.75):
        cands[f"Lanczos3 + unsharp({k})"] = unsharp(lz, k)
    cands["bicubic(0,0.75)"] = resample(pat1, z, ox, oy, W, H, "bicubic0.0_0.75")
    RX, RY, RW, RH = 560, 520, 800, 400
    cs = (slice(RY, RY + RH), slice(RX, RX + RW))
    print(f"zoom {z:g}x (shipping unsharp amount {amount:.2f})")
    print(f"{'pipeline':36s} {'PSNR dB':>8s} {'SSIM':>7s} {'HF ratio':>9s} {'edge 10-90%':>12s} {'text over':>10s}")
    for name, img in cands.items():
        sh = best_shift(gt[cs], img[cs]) if not name.startswith("ground") else (0, 0)
        img2 = ndimage.shift(img, sh, order=3, mode="nearest") if not name.startswith("ground") else img
        r, g = img2[cs], gt[cs]
        c = (slice(20, -20), slice(20, -20))
        _, _, ov = edge_metrics(r, g, np.ones_like(r, bool))
        # squares sit at y 320-440 of the 1x layout: place the ESF window around them for this crop
        try:
            w1090, eov, _ = esf_metrics(img2, gt, y0=int((320 - oy) * z), y1=int((440 - oy) * z), x0=int((600 - ox) * z), x1=int((1400 - ox) * z))
        except Exception:
            w1090 = float("nan")
        print(f"{name:36s} {psnr(r[c], g[c]):8.2f} {ssim(r[c], g[c]):7.4f} {hf_ratio(r[c], g[c]):9.3f} {w1090:10.2f}px {ov*100:9.2f}%")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--ffmpeg", default="ffmpeg/ffmpeg.exe")
    ap.add_argument("--t0", type=float, default=2.0)
    ap.add_argument("--t1", type=float, default=13.0)
    ap.add_argument("--mp4")
    ap.add_argument("--json")
    ap.add_argument("--study", type=float)
    a = ap.parse_args(); a.ffmpeg = os.path.abspath(a.ffmpeg)
    d = a.dir
    if a.study:
        study(d, a.study); return
    mp4 = a.mp4 or next(os.path.join(d, f) for f in os.listdir(d) if f.lower().endswith(".mp4") and "pattern" not in f)
    pat1 = load_gray(os.path.join(d, "pattern-1x.png"))
    pat2 = load_gray(os.path.join(d, "pattern-2x.png"))
    frames = extract_frames(a.ffmpeg, mp4, a.t0, a.t1, os.path.join(d, "frames"), fps=2)
    if not frames:
        sys.exit("no frames extracted")

    rows = {}
    zs = []
    RX, RY, RW, RH = 560, 240, 800, 600   # evaluation region of the output frame (the whole screen carries pattern)
    # keep frames that sit at a steady 2x crop: strong correlation with the 1x pattern at z=2 and the same
    # origin as the next kept frame (camera not panning)
    steady = []
    for fp in frames:
        fr_full = load_gray(fp)
        ncc, ix, iy = coarse(fr_full, pat1)
        if ncc > 0.6:
            steady.append((fp, ix, iy))
    print(f"  steady 2x frames: {len(steady)} of {len(frames)}", flush=True)
    chosen = [t for i, t in enumerate(steady) if i + 1 < len(steady) and abs(steady[i + 1][1] - t[1]) <= 1 and abs(steady[i + 1][2] - t[2]) <= 1][:2]
    if not chosen:
        chosen = steady[:2]
    for fp, _, _ in chosen:
        fr_full = load_gray(fp)
        z, ox, oy = register(fr_full, pat1)
        zs.append((z, ox, oy)); print(f"  registered {os.path.basename(fp)}: zoom {z:.4f} origin ({ox:.2f},{oy:.2f}) residual rms {np.sqrt(np.mean((ndimage.map_coordinates(pat1,[oy+np.mgrid[0:1080:6,0:1920:6][0]/z, ox+np.mgrid[0:1080:6,0:1920:6][1]/z],order=3,mode='nearest')-fr_full[::6,::6])**2)):.4f}", flush=True)
        if abs(z - 2.0) > 0.02:
            continue  # only evaluate steady 2x frames: GT is a native 2x render
        H, W = fr_full.shape
        yy, xx = np.mgrid[0:H, 0:W]
        gt = ndimage.map_coordinates(pat2, [2 * (oy + yy / z + 0.5) - 0.5, 2 * (ox + xx / z + 0.5) - 0.5], order=1, mode="nearest")
        cr = resample(pat1, z, ox, oy, W, H, "cr")
        lz = resample(pat1, z, ox, oy, W, H, "lanczos3")
        cands = {
            "recorded": fr_full,
            "model: Catmull-Rom": cr,
            "model: Catmull-Rom + unsharp(1.0)": unsharp(cr, 1.0),
            "model: bilinear": resample(pat1, z, ox, oy, W, H, "bilinear"),
            "model: Lanczos3": lz,
            "model: Lanczos3 + unsharp(0.5)": unsharp(lz, 0.5),
            "model: Mitchell(1/3,1/3)": resample(pat1, z, ox, oy, W, H, "bicubic0.3333_0.3333"),
            "model: bicubic(0,0.75)": resample(pat1, z, ox, oy, W, H, "bicubic0.0_0.75"),
        }
        cslice = (slice(RY, RY + RH), slice(RX, RX + RW))
        mask = np.ones((RH, RW), bool)
        for name, img in cands.items():
            sh = best_shift(gt[cslice], img[cslice])
            img2 = ndimage.shift(img, sh, order=3, mode="nearest")
            r, g = img2[cslice], gt[cslice]
            c = (slice(20, -20), slice(20, -20))
            _, _, ov = edge_metrics(r, g, mask)
            w1090, eov, eun = esf_metrics(img2, gt)
            rows.setdefault(name, []).append((psnr(r[c], g[c]), ssim(r[c], g[c]), hf_ratio(r[c], g[c]), w1090, ov, eov, eun))

    print(f"frames: {len(frames)}  registered zoom values: {[round(z[0], 3) for z in zs]}")
    print(f"{'pipeline':36s} {'PSNR dB':>8s} {'SSIM':>7s} {'HF ratio':>9s} {'edge 10-90%':>12s} {'text over':>10s} {'ESF over':>9s}")
    out = {}
    for name, v in rows.items():
        m = np.mean(np.array(v), axis=0)
        out[name] = dict(psnr=m[0], ssim=m[1], hf=m[2], edge_width_px=m[3], text_overshoot=m[4], esf_overshoot=m[5], esf_undershoot=m[6], frames=len(v))
        print(f"{name:36s} {m[0]:8.2f} {m[1]:7.4f} {m[2]:9.3f} {m[3]:10.2f}px {m[4]*100:9.2f}% {m[5]*100:8.2f}%")
    # ground truth edge width for reference
    if frames:
        pass
    if a.json:
        json.dump(out, open(a.json, "w"), indent=1)


if __name__ == "__main__":
    main()
