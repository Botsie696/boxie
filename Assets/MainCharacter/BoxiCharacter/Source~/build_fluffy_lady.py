# Fluffy Lady - procedural build: atlas texture, fur-card mesh, rig, animations, Unity FBX export.
# Run inside Blender 5.x:  exec(open(r"...build_fluffy_lady.py").read())
import bpy, math, random, os
import numpy as np
from mathutils import Vector, Quaternion, Matrix

OUT = r"C:\Users\Harshil\Documents\Blender\FluffyLady"
os.makedirs(os.path.join(OUT, "Textures"), exist_ok=True)
rng = random.Random(11)
np.random.seed(11)
TAU = 2 * math.pi

# ---------------------------------------------------------------- cleanup (re-runnable)
PFX = ("FluffyLady", "FL_", "T_FluffyLady", "M_FL", "PV_")
for o in list(bpy.data.objects):
    if o.name.startswith(PFX): bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.actions,
             bpy.data.images, bpy.data.cameras, bpy.data.lights, bpy.data.worlds):
    for d in list(coll):
        if d.name.startswith(PFX): coll.remove(d)
CLIP_NAMES = {"Run_InPlace", "Run_InPlace_v2", "Idle", "Jump_Start", "Jump_Air", "Land", "Smash", "Blink"}
for a in list(bpy.data.actions):
    if a.name.split('.')[0] in CLIP_NAMES: bpy.data.actions.remove(a)
scn = bpy.context.scene
scn.render.fps = 30

# ---------------------------------------------------------------- palette (sRGB) - edit to recolour
COL = dict(
    fur_base=(0.46, 0.20, 0.56), fur_tip=(0.86, 0.60, 0.92),
    dark_base=(0.20, 0.07, 0.27), dark_tip=(0.56, 0.30, 0.66),
    brow_base=(0.58, 0.10, 0.26), brow_tip=(1.00, 0.48, 0.60),
    skin=(0.44, 0.19, 0.50), lash=(0.035, 0.02, 0.045),
    frame=(0.03, 0.028, 0.035), bow=(0.96, 0.36, 0.56), mouth=(0.25, 0.06, 0.15), white=(0.96, 0.95, 0.93),
    iris_rim=(0.05, 0.16, 0.34), iris_mid=(0.22, 0.55, 0.86), iris_in=(0.60, 0.84, 0.97))

# ---------------------------------------------------------------- atlas 1024
S = 1024
A = np.zeros((S, S, 4), np.float32)

def fill(x0, y0, x1, y1, c, a=1.0):
    A[y0:y1, x0:x1, :3] = c; A[y0:y1, x0:x1, 3] = a

def strands(x0, y0, x1, y1, base, tip, n, rs, ts, w0, w1, lmin=0.55):
    base = np.array(base, np.float32); tip = np.array(tip, np.float32)
    fill(x0, y0, x1, y1, (base + tip) / 2, 0.0)
    H = y1 - y0; cx = (x0 + x1) / 2
    for _ in range(n):
        lf = rng.uniform(lmin, 0.93); hl = int(H * lf)
        ys = np.arange(y0, y0 + hl); t = (ys - y0) / H
        rx = cx + rng.gauss(0, rs); tx = cx + rng.uniform(-ts, ts)
        cv = rng.uniform(-1, 1) * H * 0.04
        x = rx + (tx - rx) * t + cv * np.sin(np.pi * t)
        hw = rng.uniform(w0, w1) * (1 - 0.8 * t / lf) + 0.35
        fade = np.clip((lf - t) / 0.1, 0, 1)
        sh = rng.uniform(0.72, 1.15)
        col = np.clip((base[None] * (1 - t[:, None]) + tip[None] * t[:, None]) * sh, 0, 1)
        for dx in range(-6, 7):
            xi = np.floor(x).astype(int) + dx
            cov = np.clip(hw + 0.5 - np.abs(xi + 0.5 - x), 0, 1) * fade
            m = (xi >= x0) & (xi < x1) & (cov > 0.01)
            if not m.any(): continue
            yy = ys[m]; xx = xi[m]; c = cov[m][:, None]
            A[yy, xx, :3] = A[yy, xx, :3] * (1 - c) + col[m] * c
            A[yy, xx, 3] = np.maximum(A[yy, xx, 3], cov[m])

strands(0, 0, 256, 1024, COL['fur_base'], COL['fur_tip'], 330, 20, 110, 1.3, 2.8, 0.45)
strands(256, 0, 512, 1024, COL['dark_base'], COL['dark_tip'], 330, 20, 110, 1.3, 2.8, 0.45)
strands(512, 0, 768, 1024, COL['brow_base'], COL['brow_tip'], 330, 20, 110, 1.5, 3.0, 0.45)
strands(768, 256, 1024, 512, COL['lash'], COL['lash'], 9, 26, 95, 5.0, 6.5, 0.8)

# body skin
nz = np.random.rand(256, 256).astype(np.float32)
for _ in range(4):
    nz = (nz + np.roll(nz, 1, 0) + np.roll(nz, 1, 1) + np.roll(nz, -1, 0) + np.roll(nz, -1, 1)) / 5
nz = (nz - nz.min()) / (np.ptp(nz) + 1e-6)
A[512:768, 768:1024, :3] = np.array(COL['skin'])[None, None] * (0.8 + 0.4 * nz[..., None])
A[512:768, 768:1024, 3] = 1

# eye
g = (np.arange(256) + 0.5) / 128 - 1
X, Y = np.meshgrid(g, g)
r = np.sqrt(X ** 2 + Y ** 2); ang = np.arctan2(Y, X)
eye = np.ones((256, 256, 3), np.float32) * np.array(COL['white'], np.float32)
eye *= (1 - 0.22 * np.clip((r - 0.7) / 0.3, 0, 1))[..., None]
ir = np.clip(r / 0.52, 0, 1)[..., None]
cin, cmid, crim = (np.array(COL[k], np.float32) for k in ('iris_in', 'iris_mid', 'iris_rim'))
icol = np.where(ir < 0.55, cin * (1 - ir / 0.55) + cmid * (ir / 0.55),
                cmid * (1 - (ir - 0.55) / 0.45) + crim * ((ir - 0.55) / 0.45))
icol *= (0.82 + 0.18 * np.sin(ang * 37 + 2 * np.sin(ang * 11)))[..., None]
m = r < 0.52; eye[m] = icol[m]
ring = (r >= 0.52) & (r < 0.56); eye[ring] *= 0.55
eye[r < 0.24] = (0.01, 0.01, 0.015)
eye[np.sqrt((X + 0.17) ** 2 + (Y - 0.2) ** 2) < 0.075] = (1, 1, 1)
eye[np.sqrt((X - 0.12) ** 2 + (Y + 0.12) ** 2) < 0.03] = (0.9, 0.95, 1)
A[768:1024, 768:1024, :3] = eye; A[768:1024, 768:1024, 3] = 1

# solid swatches
fill(768, 0, 896, 128, COL['frame']); fill(896, 0, 1024, 128, COL['bow'])
fill(768, 128, 896, 256, COL['mouth']); fill(896, 128, 1024, 256, COL['white'])
UV_FRAME = (0.8125, 0.0625); UV_BOW = (0.9375, 0.0625)

img = bpy.data.images.new("T_FluffyLady_Atlas", S, S, alpha=True)
img.pixels.foreach_set(A.ravel())
TEX = os.path.join(OUT, "Textures", "T_FluffyLady_Atlas.png")
img.filepath_raw = TEX; img.file_format = 'PNG'; img.save()

# ---------------------------------------------------------------- materials
def make_mat(name, rough, alpha=False):
    m = bpy.data.materials.new(name)
    try: m.use_nodes = True
    except Exception: pass
    nt = m.node_tree; b = nt.nodes.get("Principled BSDF")
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = img; t.location = (-420, 200)
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = rough
    if alpha:
        nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
        try: m.surface_render_method = 'DITHERED'
        except Exception: m.blend_method = 'CLIP'
    m.use_backface_culling = False
    return m

M_FUR = make_mat("M_FL_Fur", 0.85, True)
M_BODY = make_mat("M_FL_Body", 0.7)
M_GLOSS = make_mat("M_FL_Gloss", 0.12)

# ---------------------------------------------------------------- geometry builder
V, F, FUV, FM, W, N = [], [], [], [], [], []

def addv(p, w, n=None):
    V.append(tuple(p)); W.append(w); N.append(tuple(n) if n is not None else (0.0, 0.0, 0.0))
    return len(V) - 1

def addf(q, uv, mi, center=None, inward=False):
    if center is not None:
        a, b, c = Vector(V[q[0]]), Vector(V[q[1]]), Vector(V[q[2]])
        nn = (b - a).cross(c - a)
        cen = sum((Vector(V[i]) for i in q), Vector()) / len(q)
        if (nn.dot(cen - center) < 0) != inward:
            q = q[::-1]; uv = uv[::-1]
    F.append(tuple(q)); FUV.append(list(uv)); FM.append(mi)

R, RZ = 0.5, 0.45
C = Vector((0, 0, 0.46))
EZ, EX, EY, ER = C.z + 0.06, 0.175, -0.40, 0.16
EYES = {'L': Vector((EX, EY, EZ)), 'R': Vector((-EX, EY, EZ))}
SG = {'L': 1, 'R': -1}

def surf(n, k=1.0): return C + Vector((n.x * R, n.y * R, n.z * RZ)) * k
def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0); return t * t * (3 - 2 * t)
def wbody(z):
    u = smooth(C.z - 0.15, C.z + 0.25, z); return {'Body': 1 - u, 'Body_Upper': u}
def radial(p):
    d = p - C; return Vector((d.x / R ** 2, d.y / R ** 2, d.z / RZ ** 2)).normalized()
def fib(n):
    out = []
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n; rr = math.sqrt(max(0, 1 - z * z)); ph = i * 2.399963
        out.append(Vector((rr * math.cos(ph), rr * math.sin(ph), z)))
    return out

FURD = [d for d in fib(24) if d.z > -0.75]
FURN = ['Fur_%02d' % i for i in range(len(FURD))]

def wfur(n):
    ds = sorted((n.angle(d), i) for i, d in enumerate(FURD))[:2]
    w0 = 1 / (ds[0][0] + 0.05); w1 = 1 / (ds[1][0] + 0.05); s = w0 + w1
    return {FURN[ds[0][1]]: w0 / s, FURN[ds[1][1]]: w1 / s}

def mixw(*pairs):
    out = {}
    for w, s in pairs:
        for k, v in w.items(): out[k] = out.get(k, 0) + v * s
    return out

def card(pts, side, widths, u0, u1, v0, v1, mi, wts, nrm):
    n = len(pts)
    for back in (False, True):
        Lv, Rv = [], []
        for i, p in enumerate(pts):
            s = side[i] if isinstance(side, list) else side
            Lv.append(addv(p - s * widths[i] / 2, wts[i], nrm[i]))
            Rv.append(addv(p + s * widths[i] / 2, wts[i], nrm[i]))
        for i in range(n - 1):
            va = v0 + (v1 - v0) * i / (n - 1); vb = v0 + (v1 - v0) * (i + 1) / (n - 1)
            q = [Lv[i], Rv[i], Rv[i + 1], Lv[i + 1]]; uv = [(u0, va), (u1, va), (u1, vb), (u0, vb)]
            if back: q = q[::-1]; uv = uv[::-1]
            addf(q, uv, mi)

COLS = {'body': (0.004, 0.246), 'dark': (0.254, 0.496), 'brow': (0.504, 0.746)}
def rand_perp(d):
    r_ = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1)))
    s = d.cross(r_)
    return s.normalized() if s.length > 1e-4 else d.orthogonal().normalized()

def latlong(pos, uvf, wf, rings, seg, mi, center, a_max=math.pi, inward=False):
    As = [a_max * i / rings for i in range(rings + 1)]
    full = abs(a_max - math.pi) < 1e-6
    p0 = pos(0, 0); pole0 = addv(p0, wf(p0))
    rows = []
    last = rings - 1 if full else rings
    for i in range(1, last + 1):
        row = []
        for j in range(seg):
            p = pos(As[i], TAU * j / seg); row.append(addv(p, wf(p)))
        rows.append(row)
    for j in range(seg):
        j2 = (j + 1) % seg; b = TAU * j / seg; b2 = TAU * (j + 1) / seg
        addf([pole0, rows[0][j], rows[0][j2]], [uvf(0, (b + b2) / 2), uvf(As[1], b), uvf(As[1], b2)], mi, center, inward)
    for i in range(len(rows) - 1):
        for j in range(seg):
            j2 = (j + 1) % seg; b = TAU * j / seg; b2 = TAU * (j + 1) / seg
            addf([rows[i][j], rows[i + 1][j], rows[i + 1][j2], rows[i][j2]],
                 [uvf(As[i + 1], b), uvf(As[i + 2], b), uvf(As[i + 2], b2), uvf(As[i + 1], b2)], mi, center, inward)
    if full:
        p1 = pos(math.pi, 0); pole1 = addv(p1, wf(p1)); k = len(rows) - 1
        for j in range(seg):
            j2 = (j + 1) % seg; b = TAU * j / seg; b2 = TAU * (j + 1) / seg
            addf([rows[k][j], pole1, rows[k][j2]], [uvf(As[k + 1], b), uvf(math.pi, (b + b2) / 2), uvf(As[k + 1], b2)], mi, center, inward)

def body_uv(a, b): return (0.752 + 0.246 * b / TAU, 0.502 + 0.246 * (1 - a / math.pi))
def const_uv(uv): return lambda a, b: uv

# ---- body core
latlong(lambda a, b: surf(Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a))), 0.93),
        body_uv, lambda p: wbody(p.z), 16, 28, 1, C)

# ---- eyes (pole facing -Y so iris is centred)
def eye_uv(a, b):
    k = math.sin(a) if a <= math.pi / 2 else 1.0
    return (0.875 + 0.1245 * k * math.cos(b), 0.875 + 0.1245 * k * math.sin(b))
for s_, c in EYES.items():
    latlong(lambda a, b, c=c: c + ER * Vector((math.sin(a) * math.cos(b), -math.cos(a), math.sin(a) * math.sin(b))),
            eye_uv, lambda p, s_=s_: {'Eye_' + s_: 1.0}, 14, 28, 2, c)

# ---- eyelids (rest = relaxed half-lidded look; blink by rotating bone around world X)
AMAX = math.acos(-0.2)
ROT_UP = Matrix.Rotation(math.radians(-32), 3, 'X')
ROT_LO = Matrix.Rotation(math.radians(45), 3, 'X')
for s_, c in EYES.items():
    for rad, inward in ((ER * 1.062, False), (ER * 1.023, True)):
        latlong(lambda a, b, c=c, rad=rad: c + ROT_UP @ Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a))) * rad,
                body_uv, lambda p, s_=s_: {'Lid_Upper_' + s_: 1.0}, 8, 28, 1, c, AMAX, inward)
    for rad, inward in ((ER * 1.031, False), (ER * 0.992, True)):
        latlong(lambda a, b, c=c, rad=rad: c + ROT_LO @ Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), -math.cos(a))) * rad,
                body_uv, lambda p, s_=s_: {'Lid_Lower_' + s_: 1.0}, 8, 28, 1, c, AMAX, inward)
    # lashes along the upper-lid rim, longer + flicked at the outer corner
    sg = SG[s_]
    for k in range(9):
        b = math.radians(-90 + (-72 + 18 * k))
        q = ROT_UP @ Vector((math.sin(AMAX) * math.cos(b), math.sin(AMAX) * math.sin(b), -0.2))
        root = c + q * ER * 1.054
        o = math.cos(b) * sg  # -1 inner .. +1 outer
        Ll = 0.032 + 0.06 * ((o + 1) / 2) ** 2
        radv = (root - c).normalized()
        d = (radv * 0.8 + Vector((0, 0, 0.65)) + Vector((sg * 0.5 * max(o, 0), 0, 0))).normalized()
        tang = (ROT_UP @ Vector((-math.sin(b), math.cos(b), 0))).normalized()
        pts = [root + d * Ll * t + Vector((0, 0, 1)) * Ll * 0.4 * t * t for t in (0, 0.5, 1)]
        card(pts, tang, [0.026, 0.03, 0.032], 0.754, 0.996, 0.252, 0.498, 0,
             [{'Lid_Upper_' + s_: 1.0}] * 3, [radv] * 3)

# ---- brows (thick rose arcs)
BROW_PTS = []
BROW_HEAD = {}
for s_, sg in SG.items():
    for row in range(3):
        for k in range(12):
            s = -1 + 2 * k / 11
            x = sg * (0.19 + s * 0.14)
            z = EZ + 0.21 + 0.04 * (1 - (s - 0.3) ** 2) + (row - 1) * 0.02
            y = -R * math.sqrt(max(0.0, 1 - x * x / R ** 2 - (z - C.z) ** 2 / RZ ** 2))
            p = Vector((x, y, z)); n = radial(p)
            root = p + n * 0.045
            BROW_PTS.append(p)
            if row == 1 and k == 5: BROW_HEAD[s_] = (root.copy(), n.copy())
            d = (n * 0.55 + Vector((sg * 0.8 * (0.45 + 0.55 * (s + 1) / 2), 0, 0)) + Vector((0, 0, 0.3))).normalized()
            Lb = rng.uniform(0.08, 0.11)
            pts = [root + d * Lb * t + Vector((0, 0, -1)) * 0.25 * Lb * t * t for t in (0, 0.5, 1)]
            card(pts, rand_perp(d), [0.05, 0.06, 0.065], *COLS['brow'], 0.0, 0.995, 0,
                 [{'Brow_' + s_: 1.0}] * 3, [n] * 3)

# ---- fur cards
EYE_DIRS = [(c - C).normalized() for c in EYES.values()]
def eye_ang(n): return min(n.angle(e) for e in EYE_DIRS)

def fur_layer(count, lmin, lmax, segs, wscale, colsel, flex):
    for n0 in fib(count):
        if n0.z < -0.8: continue
        ea = eye_ang(n0)
        if ea < math.radians(25): continue
        root0 = surf(n0, 0.9)
        if min((root0 - b).length for b in BROW_PTS) < 0.06: continue
        face = 1.0 if n0.y > -0.3 else 0.35 + 0.65 * smooth(math.radians(25), math.radians(56), ea)
        n = (n0 + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.02).normalized()
        root = surf(n, 0.9)
        Ln = rng.uniform(lmin, lmax) * (0.85 + 0.3 * max(n.z, 0)) * face
        d = (n + Vector((rng.gauss(0, 0.25), rng.gauss(0, 0.25), rng.gauss(0, 0.25) - 0.12))).normalized()
        ts = [i / segs for i in range(segs + 1)]
        pts = [root + d * Ln * t + Vector((0, 0, -1)) * 0.22 * Ln * t * t for t in ts]
        wb, wf = wbody(root.z), wfur(n)
        wts = [mixw((wb, 1 - flex * t ** 1.3), (wf, flex * t ** 1.3)) for t in ts]
        nr = [(p - C).normalized() for p in pts]
        widths = [wscale * (1 + 0.55 * t) * (0.6 + 0.4 * face) for t in ts]
        card(pts, rand_perp(d), widths, *COLS[colsel(n)], 0.0, 0.995, 0, wts, nr)

fur_layer(430, 0.09, 0.15, 1, 0.085, lambda n: 'body' if n.y < -0.5 else 'dark', 0.4)
fur_layer(820, 0.20, 0.33, 3, 0.07, lambda n: 'dark' if n.z < -0.35 else 'body', 0.9)

# ---- top tuft (3-bone chain)
TP = [Vector((0, 0.02, 0.84)), Vector((0, -0.02, 1.05)), Vector((0.035, -0.05, 1.20)), Vector((0.12, -0.06, 1.29))]
def tuft_at(u):
    k = min(int(u), 2); f = u - k
    return TP[k].lerp(TP[k + 1], f)
def tuft_w(u):
    k = min(int(u), 2); f = u - k
    w = {'Tuft_%02d' % (k + 1): 1.0}
    if f > 0.6 and k < 2: w['Tuft_%02d' % (k + 2)] = 0.5 * (f - 0.6) / 0.4
    if f < 0.4 and k > 0: w['Tuft_%02d' % k] = 0.5 * (0.4 - f) / 0.4
    if u < 0.35: w['Body_Upper'] = 1 - u / 0.35
    s = sum(w.values()); return {a: b / s for a, b in w.items()}
for ci in range(15):
    jit = Vector((rng.uniform(-0.035, 0.035), rng.uniform(-0.035, 0.035), 0))
    us = [3 * i / 9 for i in range(10)]
    pts = [tuft_at(u) + jit * (1 - u / 3 * 0.5) for u in us]
    sides = []
    for i, p in enumerate(pts):
        tg = (pts[min(i + 1, 9)] - pts[max(i - 1, 0)]).normalized()
        base = tg.orthogonal().normalized()
        sides.append((Quaternion(tg, math.pi * ci / 15) @ base).normalized())
    widths = [0.13 * (1 - 0.55 * u / 3) for u in us]
    card(pts, sides, widths, *COLS['body'], 0.0, 0.995, 0, [tuft_w(u) for u in us],
         [(p - C).normalized() for p in pts])

# ---- bow
NB = Vector((-0.5, -0.38, 0.78)).normalized()
BOWC = surf(NB, 1.0) + NB * 0.19
fwd = NB; right = Vector((0, 0, 1)).cross(fwd).normalized(); upv = fwd.cross(right)
BM = Matrix((right, fwd, upv)).transposed()
def bow_part(off, scl, roll):
    rot = Matrix.Rotation(roll, 3, 'Y')
    cen = BOWC + BM @ off
    latlong(lambda a, b: cen + BM @ (rot @ Vector((math.sin(a) * math.cos(b) * scl[0], math.cos(a) * scl[1], math.sin(a) * math.sin(b) * scl[2]))),
            const_uv(UV_BOW), lambda p: {'Bow': 1.0}, 8, 14, 1, cen)
bow_part(Vector((0.11, 0, 0)), (0.12, 0.045, 0.08), math.radians(-18))
bow_part(Vector((-0.11, 0, 0)), (0.12, 0.045, 0.08), math.radians(18))
bow_part(Vector((0, 0.015, 0)), (0.05, 0.05, 0.05), 0)

# ---- cat-eye glasses
def tube(path, closed, rad, ref, bone, sides=8):
    n = len(path); rings = []
    for i, p in enumerate(path):
        t = (path[(i + 1) % n] - path[i - 1]) if closed else (path[min(i + 1, n - 1)] - path[max(i - 1, 0)])
        t.normalize(); nn = t.cross(ref).normalized(); bb = t.cross(nn)
        rr = rad(i)
        rings.append([addv(p + (nn * math.cos(TAU * k / sides) + bb * math.sin(TAU * k / sides)) * rr, {bone: 1.0})
                      for k in range(sides)])
    for i in range(n if closed else n - 1):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(sides):
            k2 = (k + 1) % sides
            addf([a[k], b[k], b[k2], a[k2]], [UV_FRAME] * 4, 2, path[i])
    # orientation check uses ring centre: faces point away from path centre
def lens_path(sg):
    pts = []; th_list = []
    for k in range(56):
        th = TAU * k / 56; c, s = math.cos(th), math.sin(th)
        xl = 0.18 * math.copysign(abs(c) ** (2 / 3.2), c); zl = 0.15 * math.copysign(abs(s) ** (2 / 3.2), s)
        o = max(0, xl / 0.18); up = max(0, zl / 0.15)
        zl += 0.045 * o ** 3 * (0.3 + up); xl += 0.02 * o ** 3 * up
        x = sg * (0.2 + xl); z = EZ + 0.005 + zl
        pts.append(Vector((x, -0.635 + 0.55 * x * x, z))); th_list.append(th)
    return pts, th_list
for s_, sg in SG.items():
    pts, ths = lens_path(sg)
    tube(pts, True, lambda i, ths=ths: 0.012 + 0.007 * max(0, math.sin(ths[i])), Vector((0, -1, 0)), 'Glasses')
    tube([Vector((sg * 0.395, -0.55, EZ + 0.06)), Vector((sg * 0.44, -0.33, EZ + 0.07)), Vector((sg * 0.46, -0.08, EZ + 0.07))],
         False, lambda i: 0.009, Vector((0, 0, 1)), 'Glasses')
tube([Vector((0.03 - 0.06 * k / 8, -0.635, EZ + 0.065 + 0.015 * math.sin(math.pi * k / 8))) for k in range(9)],
     False, lambda i: 0.01, Vector((0, -1, 0)), 'Glasses')

# ---------------------------------------------------------------- mesh object
me = bpy.data.meshes.new("FluffyLady_Mesh")
me.from_pydata(V, [], F)
me.update()
uvl = me.uv_layers.new(name="UVMap")
uvl.data.foreach_set("uv", [c for uvs in FUV for uv in uvs for c in uv])
me.polygons.foreach_set("material_index", FM)
me.polygons.foreach_set("use_smooth", [True] * len(F))
for m_ in (M_FUR, M_BODY, M_GLOSS): me.materials.append(m_)
loop_v = [0] * len(me.loops); me.loops.foreach_get("vertex_index", loop_v)
me.normals_split_custom_set([N[v] for v in loop_v])
me.update()
obj = bpy.data.objects.new("FluffyLady_Body", me)
scn.collection.objects.link(obj)

# ---------------------------------------------------------------- armature
arm_d = bpy.data.armatures.new("FluffyLady_Rig")
arm = bpy.data.objects.new("FluffyLady", arm_d)
scn.collection.objects.link(arm)
arm_d.display_type = 'STICK'; arm.show_in_front = True
vl = bpy.context.view_layer
vl.update()
for o in scn.objects: o.select_set(False)
vl.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_d.edit_bones
def bone(name, h, t, parent=None):
    b = eb.new(name); b.head = h; b.tail = t; b.roll = 0
    if parent: b.parent = eb[parent]; b.use_connect = False
bone('Root', (0, 0, 0), (0, 0.25, 0))
bone('Body', (0, 0, 0), (0, 0, C.z), 'Root')
bone('Body_Upper', C, C + Vector((0, 0, 0.35)), 'Body')
bone('Tuft_01', TP[0], TP[1], 'Body_Upper')
bone('Tuft_02', TP[1], TP[2], 'Tuft_01')
bone('Tuft_03', TP[2], TP[3], 'Tuft_02')
for s_, c in EYES.items():
    bone('Eye_' + s_, c, c + Vector((0, -0.1, 0)), 'Body_Upper')
    bone('Lid_Upper_' + s_, c, c + Vector((0.06, 0, 0)), 'Body_Upper')
    bone('Lid_Lower_' + s_, c, c + Vector((0.045, 0, 0)), 'Body_Upper')
    h, n = BROW_HEAD[s_]; bone('Brow_' + s_, h, h + n * 0.06, 'Body_Upper')
bone('Glasses', (0, -0.64, EZ), (0, -0.72, EZ), 'Body_Upper')
bone('Bow', BOWC, BOWC + NB * 0.06, 'Body_Upper')
for i, d in enumerate(FURD):
    h = surf(d, 0.9); bone(FURN[i], h, h + d * 0.12, 'Body_Upper' if d.z > -0.1 else 'Body')
bpy.ops.object.mode_set(mode='OBJECT')

# skin weights (normalised, max 4 influences)
groups = {}
for vi, w in enumerate(W):
    items = sorted(w.items(), key=lambda kv: -kv[1])[:4]
    s = sum(v for _, v in items) or 1
    for k, v in items:
        if v / s > 1e-4: groups.setdefault(k, []).append((vi, v / s))
for k, lst in groups.items():
    vg = obj.vertex_groups.new(name=k)
    for vi, wv in lst: vg.add([vi], wv, 'REPLACE')
obj.parent = arm
mod = obj.modifiers.new("Armature", 'ARMATURE'); mod.object = arm

# ---------------------------------------------------------------- animation helpers
BONES = [b.name for b in arm.data.bones]
MINV = {b.name: b.matrix_local.to_3x3().inverted() for b in arm.data.bones}
def qw(bn, axis, a):
    ax = MINV[bn] @ Vector(axis)
    if ax.length < 1e-8 or abs(a) < 1e-9: return Quaternion()
    return Quaternion(ax.normalized(), a)
def lw(bn, v): return MINV[bn] @ Vector(v)
def setp(p, bn, loc=None, rot=None, scl=None):
    e = p.setdefault(bn, [Vector((0, 0, 0)), Quaternion(), Vector((1, 1, 1))])
    if loc is not None: e[0] = e[0] + loc
    if rot is not None: e[1] = rot @ e[1]
    if scl is not None: e[2] = Vector(scl)
def interp(keys, f):
    for i in range(len(keys) - 1):
        f0, f1 = keys[i][0], keys[i + 1][0]
        if f0 <= f <= f1:
            s = 0 if f1 == f0 else (f - f0) / (f1 - f0); s = s * s * (3 - 2 * s)
            return keys[i][1] + (keys[i + 1][1] - keys[i][1]) * s
    return keys[-1][1]
def blink(f, f0, d=7):
    x = (f - f0) / d
    if x <= 0 or x >= 1: return 0.0
    if x < 0.35: s = x / 0.35; return s * s * (3 - 2 * s)
    s = (x - 0.35) / 0.65; return 1 - s * s * (3 - 2 * s)

FPH = [(rng.random(), rng.random(), rng.choice([3, 4, 5]), rng.choice([5, 6, 7])) for _ in FURN]
def fur_pose(p, wind, flutter, t, ks=1):
    w = Vector(wind)
    for i, bn in enumerate(FURN):
        n = FURD[i]; wp = w - n * n.dot(w); mag = wp.length
        ph1, ph2, k1, k2 = FPH[i]
        ax = n.cross(wp).normalized() if mag > 1e-5 else n.orthogonal().normalized()
        ax2 = n.cross(ax).normalized()
        a1 = mag + flutter * math.sin(TAU * (k1 * ks * t + ph1))
        a2 = flutter * 0.7 * math.sin(TAU * (k2 * ks * t + ph2))
        setp(p, bn, rot=qw(bn, ax, a1) @ qw(bn, ax2, a2))
def body(p, lift=0.0, sz=1.0, lean=0.0):
    sx = 1 / math.sqrt(sz)
    setp(p, 'Body', loc=lw('Body', (0, 0, lift)), scl=(sx, sz, sx), rot=qw('Body', (0, 1, 0), lean))
def upper(p, lean=0.0, nod=0.0):
    setp(p, 'Body_Upper', rot=qw('Body_Upper', (0, 1, 0), lean) @ qw('Body_Upper', (1, 0, 0), nod))
def eyes(p, yaw, up):
    for s_ in SG: bn = 'Eye_' + s_; setp(p, bn, rot=qw(bn, (0, 0, 1), yaw) @ qw(bn, (1, 0, 0), -up))
def lids(p, up, low, b=0.0):
    up = up + (0.707 - up) * b; low = low + (-0.27 - low) * b
    for s_ in SG:
        setp(p, 'Lid_Upper_' + s_, rot=qw('Lid_Upper_' + s_, (1, 0, 0), up))
        setp(p, 'Lid_Lower_' + s_, rot=qw('Lid_Lower_' + s_, (1, 0, 0), low))
def brows(p, angry=0.0, raise_=0.0):
    for s_, sg in SG.items():
        bn = 'Brow_' + s_; setp(p, bn, rot=qw(bn, (0, -1, 0), sg * angry), loc=lw(bn, (0, 0, raise_)))
def tuft(p, angs):
    for j, (ay, ax) in enumerate(angs):
        bn = 'Tuft_%02d' % (j + 1); setp(p, bn, rot=qw(bn, (0, 1, 0), ay) @ qw(bn, (1, 0, 0), ax))
def glasses(p, dz=0.0, tilt=0.0):
    setp(p, 'Glasses', loc=lw('Glasses', (0, 0, dz)), rot=qw('Glasses', (0, -1, 0), tilt))
def bow(p, ay=0.0, ax=0.0):
    setp(p, 'Bow', rot=qw('Bow', (0, 1, 0), ay) @ qw('Bow', (1, 0, 0), ax))

# ---------------------------------------------------------------- clips
def run(f):  # fast travel toward +X (screen right), in place, wind streaming fur back
    L = 48; t = f / L; p = {}
    hop = abs(math.sin(math.pi * 4 * t)); contact = 1 - smooth(0, 0.4, hop)
    body(p, lift=0.04 * hop, sz=1 - 0.11 * contact + 0.05 * hop,
         lean=math.radians(7) + math.radians(2) * math.sin(TAU * 4 * t))
    upper(p, math.radians(8) + math.radians(3) * math.sin(TAU * 4 * t - 0.9), math.radians(1.5) * math.sin(TAU * 4 * t - 1.4))
    fur_pose(p, (-0.62, 0, -0.35 * contact + 0.12 * hop), 0.13, t, 2)
    tuft(p, [(-0.32 - 0.05 * j + 0.14 * math.sin(TAU * 4 * t - 0.9 * (j + 1)), 0.08 * math.sin(TAU * 6 * t - j)) for j in range(3)])
    bow(p, -0.15 + 0.1 * math.sin(TAU * 8 * t), 0.06 * math.sin(TAU * 6 * t + 1))
    eyes(p, 0.38, 0.02 * math.sin(TAU * 4 * t))
    lids(p, 0.12, -0.04, blink(f, 30))
    brows(p, 0.15, -0.008 * contact)
    glasses(p, 0.006 * math.sin(TAU * 4 * t - 1.6), 0.02 * math.sin(TAU * 4 * t - 1.0))
    return p

def idle(f):
    L = 72; t = f / L; p = {}; br = math.sin(TAU * t)
    body(p, sz=1 + 0.025 * br)
    upper(p, math.radians(2) * math.sin(TAU * t + 0.5), math.radians(1.5) * math.sin(TAU * 2 * t))
    fur_pose(p, (-0.08, 0, -0.10 + 0.04 * br), 0.035, t, 1)
    tuft(p, [(0.06 * math.sin(TAU * 2 * t - 0.7 * j), 0.04 * math.sin(TAU * t - 0.5 * j)) for j in range(3)])
    yaw = interp([(0, 0), (18, 0), (22, -0.28), (40, -0.28), (44, 0.18), (60, 0.18), (64, 0), (72, 0)], f)
    up = interp([(0, 0), (18, 0), (22, 0.06), (40, 0.06), (44, -0.06), (60, -0.06), (64, 0), (72, 0)], f)
    eyes(p, yaw, up); lids(p, 0, 0, blink(f, 50, 8)); brows(p, 0, 0.004 * br)
    bow(p, 0.05 * math.sin(TAU * 2 * t)); glasses(p)
    return p

def jump_start(f):
    p = {}; t = f / 30
    if f <= 5:
        s = smooth(0, 5, f); sz = 1 - 0.22 * s; lift = 0; vert = 0.25 * s; upl = 0.1 * s; br = -0.01 * s
    else:
        e = 1 - (1 - (f - 5) / 7) ** 2
        sz = 0.78 + 0.42 * e; lift = 0.07 * e; vert = 0.25 - 0.85 * e; upl = 0.1 - 0.24 * e; br = -0.01 + 0.035 * e
    body(p, lift, sz); upper(p)
    fur_pose(p, (0, 0, vert), 0.05, t)
    tuft(p, [(0, -vert * 0.3 * (j + 1)) for j in range(3)])
    eyes(p, 0, 0.18 * max(0, -vert)); lids(p, upl, 0.04 * max(0, -vert)); brows(p, 0, br)
    glasses(p, -0.02 * vert); bow(p, 0, -vert * 0.3)
    return p

def jump_air(f):
    L = 24; t = f / L; p = {}
    body(p, 0.07, 1.07 + 0.015 * math.sin(TAU * 2 * t)); upper(p)
    fur_pose(p, (0, 0, 0.5), 0.13, t)
    tuft(p, [(0.12 * math.sin(TAU * 3 * t - 0.9 * j), -0.15 + 0.1 * math.sin(TAU * 2 * t - j)) for j in range(3)])
    eyes(p, 0, -0.18); lids(p, -0.14, 0.04); brows(p, -0.05, 0.025)
    glasses(p, 0.008 + 0.003 * math.sin(TAU * 2 * t)); bow(p, 0.08 * math.sin(TAU * 3 * t), -0.15)
    return p

def land(f):
    p = {}; t = f / 30
    sz = interp([(0, 1.07), (2, 0.70), (6, 1.08), (10, 0.96), (14, 1.01), (18, 1.0)], f)
    lift = interp([(0, 0.07), (2, 0.0), (18, 0.0)], f)
    vert = 0.5 * (1 - f / 2) if f < 2 else -0.7 * math.exp(-(f - 2) / 4.5) * math.cos(TAU * (f - 2) / 11)
    body(p, lift, sz); upper(p, 0, -0.08 * vert)
    fur_pose(p, (0, 0, vert), 0.04, t)
    tuft(p, [(0, -vert * 0.3 * (j + 1)) for j in range(3)])
    sq = interp([(0, 0), (2, 0.35), (6, 0.3), (12, 0), (18, 0)], f); op = 1 - smooth(0, 3, f)
    lids(p, -0.14 * op + sq, 0.04 * op - sq * 0.3)
    eyes(p, 0, interp([(0, -0.18), (4, 0.0), (18, 0.0)], f)); brows(p, sq * 0.4, -0.02 * sq / 0.35)
    glasses(p, 0.02 * vert); bow(p, 0, -vert * 0.3)
    return p

def smash(f):  # ground pound: rise + tuck, slam, heavy squash, fur flare, wobble
    p = {}; t = f / 30
    sz = interp([(0, 1.0), (7, 1.12), (9, 1.28), (10, 0.6), (13, 1.12), (17, 0.94), (21, 1.02), (26, 1.0)], f)
    lift = interp([(0, 0.0), (7, 0.16), (10, 0.0), (26, 0.0)], f)
    if f < 7: vert = -0.3 * smooth(0, 7, f)
    elif f < 10: vert = -0.3 + 0.9 * smooth(7, 10, f)
    else: vert = -1.0 * math.exp(-(f - 10) / 5) * math.cos(TAU * (f - 10) / 12)
    body(p, lift, sz); upper(p, 0, -0.1 * vert)
    fur_pose(p, (0, 0, vert), 0.05, t)
    tuft(p, [(0.1 * math.sin(TAU * t * 3 - j), -vert * 0.35 * (j + 1)) for j in range(3)])
    ang = interp([(0, 0), (5, 0.35), (12, 0.35), (20, 0.12), (26, 0)], f)
    lids(p, interp([(0, 0), (5, 0.25), (10, 0.4), (16, 0.25), (26, 0)], f), interp([(0, 0), (5, -0.1), (10, -0.15), (26, 0)], f))
    eyes(p, 0, interp([(0, 0), (6, -0.1), (10, 0.1), (20, 0), (26, 0)], f))
    brows(p, ang, -0.015 * ang); glasses(p, 0.025 * vert); bow(p, 0.1 * math.sin(TAU * t * 3), -vert * 0.35)
    return p

def run_v2(f):  # high-speed chase straight ahead (facing dir, -Y = Unity +Z), upright, grounded, wind from front, 96f loop
    L = 96; t = f / L; p = {}
    vib = math.sin(TAU * 24 * t)
    setp(p, 'Body', loc=lw('Body', (0, 0, 0.004 * vib)),
         scl=(0.98, 0.965 - 0.008 * math.sin(TAU * 16 * t), 1.05 + 0.01 * math.sin(TAU * 16 * t)),
         rot=qw('Body', (1, 0, 0), math.radians(6) + math.radians(1) * math.sin(TAU * 8 * t))
             @ qw('Body', (0, 0, 1), math.radians(1.5) * math.sin(TAU * 4 * t))
             @ qw('Body', (0, 1, 0), math.radians(1.2) * math.sin(TAU * 6 * t)))
    upper(p, math.radians(0.8) * math.sin(TAU * 12 * t), math.radians(4) + math.radians(1.5) * math.sin(TAU * 8 * t - 0.8))
    fur_pose(p, (0, 0.85, 0.05 + 0.04 * math.sin(TAU * 8 * t)), 0.16, t, 4)
    tuft(p, [(0.1 * math.sin(TAU * 10 * t - j), -0.42 - 0.06 * j + 0.16 * math.sin(TAU * 12 * t - 0.9 * (j + 1))) for j in range(3)])
    bow(p, 0.08 * math.sin(TAU * 12 * t + 1), -0.25 + 0.12 * math.sin(TAU * 16 * t))
    eyes(p, 0.02 * math.sin(TAU * 6 * t), 0.015 * vib)
    lids(p, 0.2, -0.08, max(blink(f, 40, 6), blink(f, 82, 6)))
    brows(p, 0.25 + 0.03 * math.sin(TAU * 8 * t), -0.006 + 0.002 * vib)
    glasses(p, 0.004 * math.sin(TAU * 16 * t - 1.2), 0.025 * math.sin(TAU * 8 * t - 1.0))
    return p

def blink_only(f):
    p = {}; lids(p, 0, 0, blink(f, 0, 8)); return p

LIDS = [b for b in BONES if b.startswith('Lid_')]
CLIPS = [("Run_InPlace", 48, run, None), ("Run_InPlace_v2", 96, run_v2, None), ("Idle", 72, idle, None), ("Jump_Start", 12, jump_start, None),
         ("Jump_Air", 24, jump_air, None), ("Land", 18, land, None), ("Smash", 26, smash, None),
         ("Blink", 8, blink_only, LIDS)]

arm.animation_data_create()
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
ID = [Vector((0, 0, 0)), Quaternion(), Vector((1, 1, 1))]
made = []
for name, L, fn, only in CLIPS:
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data.action = act
    bl = only or BONES
    frames = list(range(L + 1)); poses = [fn(f) for f in frames]
    for bn in bl:
        seq = [pz.get(bn, ID) for pz in poses]
        qs = [e[1].copy() for e in seq]
        for i in range(1, len(qs)):
            if qs[i].dot(qs[i - 1]) < 0: qs[i].negate()
        chans = [('location', [[e[0][i] for e in seq] for i in range(3)]),
                 ('rotation_quaternion', [[q[i] for q in qs] for i in range(4)]),
                 ('scale', [[e[2][i] for e in seq] for i in range(3)])]
        for path, comps in chans:
            for i, vals in enumerate(comps):
                fc = act.fcurve_ensure_for_datablock(arm, 'pose.bones["%s"].%s' % (bn, path), index=i, group_name=bn)
                fc.keyframe_points.add(len(frames))
                fc.keyframe_points.foreach_set('co', [c for fr, v in zip(frames, vals) for c in (fr, v)])
                fc.update()
    act.use_frame_range = True; act.frame_start = 0; act.frame_end = L
    act.use_cyclic = name in ("Run_InPlace", "Run_InPlace_v2", "Idle", "Jump_Air")
    made.append(name)

arm.animation_data.action = bpy.data.actions["Run_InPlace"]
scn.frame_start, scn.frame_end = 0, 48
scn.frame_set(12)

# ---------------------------------------------------------------- export
vl.update()
for o in scn.objects: o.select_set(False)
arm.select_set(True); obj.select_set(True); vl.objects.active = arm
FBX = os.path.join(OUT, "FluffyLady.fbx")
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False,
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='COPY', embed_textures=True)

tris = sum(len(f) - 2 for f in F)
result = {"verts": len(V), "tris": tris, "bones": len(BONES), "clips": made, "fbx": FBX,
          "fbx_mb": round(os.path.getsize(FBX) / 1e6, 2)}
