# Lumi - glowing amber fluffy CUBE girl: plush darker faces, bright glowing fur along edges/corners (depth + glow).
# Derived from Cubie. Adds a Hang clip.
# Cubie - fluffy CUBE lady (Leo's Fortune style). Procedural build: atlas, fur-card mesh, rig,
# animations, Unity FBX export. Same pipeline as Boxi (fluffy-character skill), cube-shaped body.
# Run inside Blender 5.x:  exec(open(r"...build_cubie.py").read())
import bpy, math, random, os, shutil
import numpy as np
from mathutils import Vector, Quaternion, Matrix

NAME = "Poppy"
OUT = r"C:\Users\Harshil\Documents\Blender\Poppy"
UNITY = r"C:\Users\Harshil\Documents\Blender\PoppyCharacter"
ACTP = NAME + "_"          # action prefix (keeps clips separate from other characters in the file)
os.makedirs(os.path.join(OUT, "Textures"), exist_ok=True)
rng = random.Random(61)
np.random.seed(23)
TAU = 2 * math.pi

# ---------------------------------------------------------------- cleanup (only this character)
PFX = (NAME, "T_" + NAME, "M_PP_")
for o in list(bpy.data.objects):
    if o.name.startswith(PFX): bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.actions, bpy.data.images):
    for d in list(coll):
        if d.name.startswith(PFX): coll.remove(d)
scn = bpy.context.scene
scn.render.fps = 30

# ---------------------------------------------------------------- palette
# Fur / skin / flower painted GRAYSCALE -> material colour (tint) is the character colour.
COL = dict(
    fur_base=(0.88, 0.88, 0.88), fur_tip=(1.0, 1.0, 1.0),
    dark_base=(0.15, 0.15, 0.15), dark_tip=(0.58, 0.58, 0.58),
    brow_base=(0.38, 0.38, 0.38), brow_tip=(1.0, 1.0, 1.0),
    skin=(0.62, 0.62, 0.62), lash=(0.035, 0.02, 0.045),
    frame=(0.03, 0.028, 0.035), bow=(0.95, 0.95, 0.95), mouth=(0.98, 0.76, 0.22), white=(0.96, 0.95, 0.93),
    iris_rim=(0.26, 0.07, 0.38), iris_mid=(0.96, 0.58, 0.12), iris_in=(1.0, 0.92, 0.58))
# Cubie: coral fur, teal brows, mint flower, emerald eyes
TINT = dict(main=(1.0, 0.38, 0.72), accent=(0.72, 0.46, 1.0), skin=(1.0, 0.30, 0.62), bow=(1.0, 0.78, 0.22), glow=(1.0, 0.70, 0.90), hair=(0.34, 0.09, 0.50), lip=(0.80, 0.12, 0.30), blush=(1.0, 0.10, 0.40), streak=(0.80, 0.40, 1.0))

# ---------------------------------------------------------------- atlas 1024
S = 1024
A = np.zeros((S, S, 4), np.float32)
def fill(x0, y0, x1, y1, c, a=1.0):
    A[y0:y1, x0:x1, :3] = c; A[y0:y1, x0:x1, 3] = a
def strands(x0, y0, x1, y1, base, tip, n, rs, ts, w0, w1, lmin=0.55):
    base = np.array(base, np.float32); tip = np.array(tip, np.float32)
    fill(x0, y0, x1, y1, (base + tip) / 2, 0.0)
    H_ = y1 - y0; cx = (x0 + x1) / 2
    for _ in range(n):
        lf = rng.uniform(lmin, 0.93); hl = int(H_ * lf)
        ys = np.arange(y0, y0 + hl); t = (ys - y0) / H_
        rx = cx + rng.gauss(0, rs); tx = cx + rng.uniform(-ts, ts)
        cv = rng.uniform(-1, 1) * H_ * 0.04
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
nz = np.random.rand(256, 256).astype(np.float32)
for _ in range(4):
    nz = (nz + np.roll(nz, 1, 0) + np.roll(nz, 1, 1) + np.roll(nz, -1, 0) + np.roll(nz, -1, 1)) / 5
nz = (nz - nz.min()) / (np.ptp(nz) + 1e-6)
A[512:768, 768:1024, :3] = np.array(COL['skin'])[None, None] * (0.8 + 0.4 * nz[..., None]); A[512:768, 768:1024, 3] = 1
g = (np.arange(256) + 0.5) / 128 - 1
X, Y = np.meshgrid(g, g)
r = np.sqrt(X ** 2 + Y ** 2); ang = np.arctan2(Y, X)
eye = np.ones((256, 256, 3), np.float32) * np.array(COL['white'], np.float32)
eye *= (1 - 0.38 * np.clip((r - 0.5) / 0.5, 0, 1))[..., None]
IRIS = 0.56
ir = np.clip(r / IRIS, 0, 1)[..., None]
cin, cmid, crim = (np.array(COL[k], np.float32) for k in ('iris_in', 'iris_mid', 'iris_rim'))
icol = np.where(ir < 0.5, cin * (1 - ir / 0.5) + cmid * (ir / 0.5), cmid * (1 - (ir - 0.5) / 0.5) + crim * ((ir - 0.5) / 0.5))
icol *= (0.86 + 0.14 * np.sin(ang * 30 + 2 * np.sin(ang * 7)))[..., None]          # radial light rays
low = np.clip(-Y / IRIS, 0, 1) * np.clip(1 - np.abs(X) / (IRIS * 0.95), 0, 1)       # light pooling in the lower iris (lit from within)
icol = icol * (1 - 0.65 * low[..., None]) + cin[None, None, :] * (0.65 * low[..., None])
m = r < IRIS; eye[m] = icol[m]
ring = (r >= IRIS) & (r < IRIS + 0.03); eye[ring] *= 0.45
eye[r < IRIS * 0.42] = (0.02, 0.01, 0.03)                                            # pupil
wet = np.clip((r - 0.72) / 0.2, 0, 1) * np.clip(-Y * 1.4, 0, 1) * (r > IRIS + 0.05)  # wet tear-line along the lower rim
eye = eye * (1 - 0.55 * wet[..., None]) + 0.55 * wet[..., None]
eye[np.hypot(X + 0.22, Y - 0.23) < 0.125] = (1, 1, 1)                                # big catchlight
eye[np.hypot(X - 0.21, Y + 0.25) < 0.055] = (1.0, 0.97, 0.95)                         # second, smaller
eye[np.hypot(X - 0.04, Y - 0.38) < 0.028] = (1, 1, 1)                                # sparkle
A[768:1024, 768:1024, :3] = eye; A[768:1024, 768:1024, 3] = 1
fill(768, 0, 896, 128, COL['frame']); fill(896, 0, 1024, 128, COL['bow'])
fill(768, 128, 896, 256, COL['mouth'])
# mouth interior swatch: dark plum, darker toward the centre
gm = (np.arange(128) + 0.5) / 64 - 1; MX, MY = np.meshgrid(gm, gm); mr = np.clip(np.sqrt(MX ** 2 + MY ** 2), 0, 1)
A[128:256, 896:1024, :3] = np.array((0.24, 0.05, 0.09))[None, None] * (0.45 + 0.55 * mr[..., None]); A[128:256, 896:1024, 3] = 1
UV_BOW = (0.9375, 0.0625); UV_SMILE = (0.875, 0.625); UV_GOLD = (0.8125, 0.1875); UV_MOUTH = (0.9375, 0.1875); UV_LIP = (0.875, 0.625)
img = bpy.data.images.new("T_%s_Atlas" % NAME, S, S, alpha=True)
img.pixels.foreach_set(A.ravel())
TEX = os.path.join(OUT, "Textures", "T_%s_Atlas.png" % NAME)
img.filepath_raw = TEX; img.file_format = 'PNG'; img.save()

# ---------------------------------------------------------------- materials (index 0 FurMain,1 Skin,2 Gloss,3 FurAccent,4 Bow)
def make_mat(name, rough, alpha=False, tint=None, emit=0.0):
    m = bpy.data.materials.new(name)
    try: m.use_nodes = True
    except Exception: pass
    nt = m.node_tree; b = nt.nodes.get("Principled BSDF")
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = img; t.location = (-620, 200)
    if tint:
        mx = nt.nodes.new("ShaderNodeMix"); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'
        mx.location = (-300, 250); mx.inputs[0].default_value = 1.0
        mx.inputs[7].default_value = (*[c ** 2.2 for c in tint], 1.0)
        nt.links.new(t.outputs['Color'], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs['Base Color'])
    else:
        nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    if emit > 0:
        nt.links.new(mx.outputs[2] if tint else t.outputs['Color'], b.inputs['Emission Color'])
        b.inputs['Emission Strength'].default_value = emit
    b.inputs['Roughness'].default_value = rough
    if alpha:
        nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
        try: m.surface_render_method = 'DITHERED'
        except Exception: m.blend_method = 'CLIP'
    m.use_backface_culling = False
    return m
# index: 0 FurMain, 1 Skin, 2 Gloss (eyes, inner light), 3 FurAccent (brows, glow), 4 Bow (gold hair ties), 5 FurGlow (edges),
#        6 Hair, 7 Lip, 8 FurBlush (cheeks)
MATS = [make_mat("M_PP_FurMain", 0.85, True, TINT['main'], 0.14), make_mat("M_PP_Skin", 0.7, False, TINT['skin'], 0.25),
        make_mat("M_PP_Gloss", 0.1, False, None, 0.45), make_mat("M_PP_FurAccent", 0.85, True, TINT['accent'], 2.2),
        make_mat("M_PP_Bow", 0.3, False, TINT['bow'], 2.4), make_mat("M_PP_FurGlow", 0.8, True, TINT['glow'], 3.2),
        make_mat("M_PP_Hair", 0.7, True, TINT['hair']), make_mat("M_PP_Lip", 0.5, False, TINT['lip'], 0.0),
        make_mat("M_PP_FurBlush", 0.85, True, TINT['blush'], 0.5), make_mat("M_PP_HairGlow", 0.5, True, TINT['streak'], 3.0)]
MATS[6].node_tree.nodes['Principled BSDF'].inputs['Specular IOR Level'].default_value = 0.15

# ---------------------------------------------------------------- geometry builder
V, F, FUV, FM, W, N = [], [], [], [], [], []
def addv(p, w, n=None):
    V.append(tuple(p)); W.append(w); N.append(tuple(n) if n is not None else (0.0, 0.0, 0.0)); return len(V) - 1
def addf(q, uv, mi, center=None, inward=False):
    if center is not None:
        a, b, c = Vector(V[q[0]]), Vector(V[q[1]]), Vector(V[q[2]])
        nn = (b - a).cross(c - a)
        cen = sum((Vector(V[i]) for i in q), Vector()) / len(q)
        if (nn.dot(cen - center) < 0) != inward: q = q[::-1]; uv = uv[::-1]
    F.append(tuple(q)); FUV.append(list(uv)); FM.append(mi)

# Rounded cube body: core half-size H (0.84 m core); with fur the silhouette is ~1 m = Unity 1x1x1 cube.
H, EXP = 0.42, 8.0
C = Vector((0, 0, H))      # sits on the ground (z = 0)
GZ = 0.006
def surf(n, k=1.0):
    s = (abs(n.x) ** EXP + abs(n.y) ** EXP + abs(n.z) ** EXP) ** (1 / EXP)
    return C + n * (H / s) * k
def radial(p):  # soft surface normal of the rounded cube
    d = (p - C) / H
    gv = Vector(tuple(math.copysign(abs(v) ** (EXP - 1), v) for v in d))
    return gv.normalized() if gv.length > 1e-9 else d.normalized()
def shade_n(p):  # mostly the cube normal -> each face reads as a plane; edges roll smoothly (rounded bevel)
    return (radial(p) * 0.85 + (p - C).normalized() * 0.15).normalized()
def edge_f(p):  # 0 on face centres -> 1 along edges / corners
    a = sorted((abs(v) for v in (p - C) / H), reverse=True)
    return smooth(0.62, 0.92, a[1])
def front_pt(x, z, k=0.98): return surf(Vector((x, -H, z - C.z)).normalized(), k)
def in_hair(p):   # where the black hair lives: crown, back of the head, upper sides, front fringe strip
    return (p.z > 2 * H - 0.07 and p.y > -0.30) or (p.y > 0.30 and p.z > 0.58) or (abs(p.x) > 0.30 and p.z > 0.70 and p.y > -0.2)

# Leo-style face: the whole front face is a short-fur face; eyes sunk into it.
EZ, EX, ER = C.z + 0.09, 0.175, 0.215
EY = -H * 0.98 + 0.19     # big eyeball sunk deep: only a flat ~2.5 cm cap shows -> merged into the face, not popping
EYES = {'L': Vector((EX, EY, EZ)), 'R': Vector((-EX, EY, EZ))}
SG = {'L': 1, 'R': -1}
FZ0, FZ1, FXM = EZ - 0.27, EZ + 0.25, 0.37   # face zone on the front face
# smirky smile curve (x, z on the front face): U-shaped, her left corner (+x) higher
SMZ = EZ - 0.225
def smile_xz(t): return (0.085 * t, SMZ + 0.024 * t * t)   # soft, sweet U-shaped smile
SM_PTS = [smile_xz(-1 + 2 * i / 24) for i in range(25)]
def in_smile(p, m=0.02):   # keeps a clean path through the face fur for the smile line
    return min(math.hypot(p.x - x_, p.z - z_) for x_, z_ in SM_PTS) < m or (abs(abs(p.x) - 0.105) < 0.025 and abs(p.z - SM_PTS[-1][1]) < 0.025)
def in_face(p, shrink=0.0):
    return p.y < -H * 0.72 and abs(p.x) < FXM - shrink and FZ0 + shrink < p.z < FZ1 - shrink

def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0); return t * t * (3 - 2 * t)
def wbody(z):
    u = smooth(C.z - 0.15, C.z + 0.25, z); return {'Body': 1 - u, 'Body_Upper': u}
def fib(n):
    out = []
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n; rr = math.sqrt(max(0, 1 - z * z)); ph = i * 2.399963
        out.append(Vector((rr * math.cos(ph), rr * math.sin(ph), z)))
    return out

FURD0 = [d for d in fib(24) if d.z > -0.75]          # sphere dirs (for weighting)
FURH = [surf(d, 0.9) for d in FURD0]                 # bone heads on the cube
FURD = [radial(h) for h in FURH]                     # bone directions (cube normals)
FURN = ['Fur_%02d' % i for i in range(len(FURD0))]
def wfur(n):
    ds = sorted((n.angle(d), i) for i, d in enumerate(FURD0))[:2]
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
            Lv.append(addv(p - s * widths[i] / 2, wts[i], nrm[i])); Rv.append(addv(p + s * widths[i] / 2, wts[i], nrm[i]))
        for i in range(n - 1):
            va = v0 + (v1 - v0) * i / (n - 1); vb = v0 + (v1 - v0) * (i + 1) / (n - 1)
            q = [Lv[i], Rv[i], Rv[i + 1], Lv[i + 1]]; uv = [(u0, va), (u1, va), (u1, vb), (u0, vb)]
            if back: q = q[::-1]; uv = uv[::-1]
            addf(q, uv, mi)
COLS = {'body': (0.004, 0.246), 'dark': (0.254, 0.496), 'brow': (0.504, 0.746)}
def rand_perp(d):
    s = d.cross(Vector((0, 0, 1)))
    if s.length < 0.35: s = d.cross(Vector((1, 0, 0)))
    s = s.normalized() + Vector((rng.gauss(0, 0.3), rng.gauss(0, 0.3), rng.gauss(0, 0.3)))
    s = s - d * s.dot(d)
    return s.normalized() if s.length > 1e-4 else d.orthogonal().normalized()
def latlong(pos, uvf, wf, rings, seg, mi, center, a_max=math.pi, inward=False):
    As = [a_max * i / rings for i in range(rings + 1)]
    full = abs(a_max - math.pi) < 1e-6
    p0 = pos(0, 0); pole0 = addv(p0, wf(p0)); rows = []
    for i in range(1, (rings - 1 if full else rings) + 1):
        rows.append([addv(pos(As[i], TAU * j / seg), wf(pos(As[i], TAU * j / seg))) for j in range(seg)])
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

# ---- rounded-cube core (skin)
latlong(lambda a, b: surf(Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a))), 0.98),
        body_uv, lambda p: wbody(p.z), 24, 40, 1, C)

# ---- eyes
def eye_uv(a, b):
    k = min(math.sin(a) / 0.52, 1.0) if a <= math.pi / 2 else 1.0
    return (0.875 + 0.1245 * k * math.cos(b), 0.875 + 0.1245 * k * math.sin(b))
for s_, c in EYES.items():
    latlong(lambda a, b, c=c: c + ER * Vector((math.sin(a) * math.cos(b), -math.cos(a), math.sin(a) * math.sin(b))),
            eye_uv, lambda p, s_=s_: {'Eye_' + s_: 1.0}, 14, 28, 2, c)

# ---- lids (furry hoods) + long lashes
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
    sg = SG[s_]
    for k in range(15):
        b = math.radians(-90 + (-76 + 10.9 * k))
        q = ROT_UP @ Vector((math.sin(AMAX) * math.cos(b), math.sin(AMAX) * math.sin(b), -0.2))
        root = c + q * ER * 1.054
        o = math.cos(b) * sg
        Ll = 0.035 + 0.125 * ((o + 1) / 2) ** 2.2
        radv = (root - c).normalized()
        d = (radv * 0.8 + Vector((0, 0, 0.7)) + Vector((sg * 0.55 * max(o, 0), 0, 0))).normalized()
        tang = (ROT_UP @ Vector((-math.sin(b), math.cos(b), 0))).normalized()
        pts = [root + d * Ll * t + Vector((0, 0, 1)) * Ll * 0.45 * t * t for t in (0, 0.5, 1)]
        card(pts, tang, [0.03, 0.036, 0.038], 0.754, 0.996, 0.252, 0.498, 0, [{'Lid_Upper_' + s_: 1.0}] * 3, [radv] * 3)
    # a few short lower lashes (feminine detail)
    for k in range(7):
        b2 = math.radians(-90 + (-62 + 20.7 * k))
        q2 = ROT_LO @ Vector((math.sin(AMAX) * math.cos(b2), math.sin(AMAX) * math.sin(b2), 0.2))
        root2 = c + q2 * ER * 1.03
        rv2 = (root2 - c).normalized()
        d2 = (rv2 * 0.8 + Vector((0, 0, -0.55)) + Vector((sg * 0.25 * max(math.cos(b2) * sg, 0), 0, 0))).normalized()
        L2 = 0.022 + 0.018 * ((math.cos(b2) * sg + 1) / 2)
        tg2 = (ROT_LO @ Vector((-math.sin(b2), math.cos(b2), 0))).normalized()
        card([root2 + d2 * L2 * t for t in (0, 0.5, 1)], tg2, [0.022, 0.026, 0.028], 0.754, 0.996, 0.252, 0.498, 0,
             [{'Lid_Lower_' + s_: 1.0}] * 3, [rv2] * 3)
    # soft fur nest: short cards ringing the eye (sides + bottom) that lay over the lid edge so the eye merges into the fur
    for k in range(34):
        a2 = TAU * (k + rng.uniform(-0.25, 0.25)) / 34
        if math.sin(a2) > 0.35: continue
        rp = front_pt(c.x + math.cos(a2) * 0.14, c.z + math.sin(a2) * 0.14)
        nn = radial(rp)
        dd = (Vector((-math.cos(a2), 0, -math.sin(a2))) * 0.6 + nn * 0.7 + Vector((rng.gauss(0, 0.08), rng.gauss(0, 0.08), rng.gauss(0, 0.08)))).normalized()
        Ln2 = rng.uniform(0.04, 0.06)
        card([rp + dd * Ln2 * t for t in (0, 0.5, 1)], rand_perp(dd), [0.04, 0.05, 0.055], *COLS['body'], 0.1, 0.995, 0,
             [{'Body_Upper': 1.0}] * 3, [shade_n(rp)] * 3)
    for k in range(26):
        a = rng.uniform(0.15, AMAX - 0.5); b = math.radians(-90 + rng.uniform(-85, 85))
        q = ROT_UP @ Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a)))
        if q.y > 0.25: continue
        root = c + q * ER * 1.04
        d = (q * 0.55 + Vector((0, 0, 0.85))).normalized()
        Ll = rng.uniform(0.04, 0.065)
        pts = [root + d * Ll * t + Vector((0, 0, -1)) * 0.2 * Ll * t * t for t in (0, 0.5, 1)]
        card(pts, rand_perp(d), [0.04, 0.05, 0.055], *COLS['body'], 0.0, 0.995, 0,
             [{'Lid_Upper_' + s_: 1.0}] * 3, [shade_n(p) for p in pts])

# ---- brows (teal arcs on the face)
BROW_PTS, BROW_HEAD = [], {}
for s_, sg in SG.items():
    for row in range(3):
        for k in range(12):
            s = -1 + 2 * k / 11
            x = sg * (0.16 + s * 0.12)
            z = EZ + 0.235 + 0.055 * (1 - (s - 0.3) ** 2) + (row - 1) * 0.014
            p = front_pt(x, z); n = radial(p); root = p + n * 0.03
            BROW_PTS.append(p)
            if row == 1 and k == 5: BROW_HEAD[s_] = (root.copy(), n.copy())
            d = (n * 0.7 + Vector((sg * 0.5 * (0.45 + 0.55 * (s + 1) / 2), 0, 0)) + Vector((0, 0, 0.3))).normalized()
            Lb = rng.uniform(0.07, 0.1)
            pts = [root + d * Lb * t + Vector((0, 0, -1)) * 0.25 * Lb * t * t for t in (0, 0.5, 1)]
            card(pts, rand_perp(d), [0.032, 0.04, 0.044], *COLS['brow'], 0.0, 0.995, 3, [{'Brow_' + s_: 1.0}] * 3, [n] * 3)

# ---- mouth: small, slightly open, wobbly + down-turned corners = fear. No teeth, dark inside visible.
MOUTH = False   # set True to build the fear mouth (geometry, bones, animation)
MZ, MA, MB = EZ - 0.21, 0.08, 0.034         # centre height, half width, half opening height
YF = front_pt(0, MZ).y; YL = YF - 0.034       # lips sit at the face-fur surface (like the eyes)
def mouth_pt(th):
    c, s = math.cos(th), math.sin(th)
    z = (0.32 * MB * s + 0.005 * math.sin(6 * th)) if s > 0 else MB * s * (1 - 0.25 * abs(math.cos(th)))   # wavy tense upper lip, open lower
    z -= 0.026 * abs(c) ** 2.5                                              # corners pulled down
    return Vector((MA * c, YL - 0.004 * c * c, MZ + z))
def mouth_w(th):
    c, s = math.cos(th), math.sin(th)
    wu = smooth(-0.2, 0.6, s); wc = abs(c) ** 4
    w = {'Mouth_Upper': wu * (1 - wc), 'Mouth_Lower': (1 - wu) * (1 - wc)}
    w['Mouth_Corner_' + ('L' if c > 0 else 'R')] = wc
    return {k: v for k, v in w.items() if v > 1e-4}
def in_mouth(p, m=0.035):
    if not MOUTH: return False
    return (p.x / (MA + m)) ** 2 + ((p.z - MZ + 0.006) / (MB + m)) ** 2 < 1
if MOUTH:
    MN = 40
    MTH = [TAU * i / MN for i in range(MN)]
    # lip tube (skin colour)
    rings = []
    for i, th in enumerate(MTH):
        pt = mouth_pt(th); tg = (mouth_pt(th + 0.01) - mouth_pt(th - 0.01)).normalized()
        nn = tg.cross(Vector((0, -1, 0))).normalized(); bb = tg.cross(nn)
        rad = 0.008 + 0.004 * max(0.0, -math.sin(th))      # fuller lower lip
        rings.append([addv(pt + (nn * math.cos(TAU * k / 8) + bb * math.sin(TAU * k / 8)) * rad, mouth_w(th)) for k in range(8)])
    for i in range(MN):
        a_, b_ = rings[i], rings[(i + 1) % MN]
        for k in range(8):
            k2 = (k + 1) % 8
            addf([a_[k], b_[k], b_[k2], a_[k2]], [UV_LIP] * 4, 1, mouth_pt(MTH[i]))
    # recessed dark interior (cone back into the face)
    mc = addv(Vector((0, YL + 0.04, MZ - 0.008)), {'Mouth_Upper': 0.4, 'Mouth_Lower': 0.6})
    ring_in = [addv(mouth_pt(th) + Vector((0, 0.003, 0)), mouth_w(th)) for th in MTH]
    for i in range(MN):
        addf([mc, ring_in[i], ring_in[(i + 1) % MN]], [UV_MOUTH] * 3, 2, Vector((0, YL + 0.3, MZ)))

# ---- smirky smile line (feminine): thin rosy curve + tiny dimple, rigged to Smile_L / Smile_C / Smile_R
def smile_pt(t):
    x, z = smile_xz(t); fp = front_pt(x, z); n = radial(fp)
    return fp + n * 0.022, n
def smile_w(t):
    wl = smooth(0.15, 1.0, t); wr = smooth(0.15, 1.0, -t)
    return {k_: v_ for k_, v_ in (('Smile_L', wl), ('Smile_R', wr), ('Smile_C', 1 - wl - wr)) if v_ > 1e-4}
def sm_tube(pts, nrm, rads, wts, sides=6):
    rings = []
    for i, c_ in enumerate(pts):
        tg = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        nn = tg.cross(nrm[i]).normalized(); ring = []
        for k in range(sides):
            a = TAU * k / sides; off = nrm[i] * math.cos(a) + nn * math.sin(a)
            ring.append(addv(c_ + off * rads[i], wts[i], off))
        rings.append(ring)
    for i in range(len(pts) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            addf([rings[i][k], rings[i + 1][k], rings[i + 1][k2], rings[i][k2]], [UV_SMILE] * 4, 7, pts[i])
    for ring, ce_, near in ((rings[0], pts[0], pts[1]), (rings[-1], pts[-1], pts[-2])):
        cv = addv(ce_, wts[0] if ring is rings[0] else wts[-1], (ce_ - near).normalized())
        for k in range(sides): addf([ring[k], cv, ring[(k + 1) % sides]], [UV_SMILE] * 3, 7, near)
SMN = 30
sp_, sn_ = zip(*[smile_pt(-1 + 2 * i / SMN) for i in range(SMN + 1)])
sm_tube(list(sp_), list(sn_), [0.0075 * (0.45 + 0.55 * math.cos((-1 + 2 * i / SMN) * math.pi / 2)) + 0.0015 for i in range(SMN + 1)],
        [smile_w(-1 + 2 * i / SMN) for i in range(SMN + 1)])
# dimples: a small arc just past each corner
for sgn, bn in ((1, 'Smile_L'), (-1, 'Smile_R')):
    ex_, ez_ = SM_PTS[-1 if sgn > 0 else 0]
    dp, dn = [], []
    for i in range(8):
        a = math.radians(-55 + 110 * i / 7)
        fp = front_pt(ex_ + sgn * (0.012 + 0.012 * math.cos(a)), ez_ + 0.012 * math.sin(a)); n_ = radial(fp)
        dp.append(fp + n_ * 0.022); dn.append(n_)
    sm_tube(dp, dn, [0.0035 * (0.55 + 0.45 * math.sin(math.pi * i / 7)) + 0.001 for i in range(8)], [{bn: 1.0}] * 8, 5)
def blush_mat(p):   # soft cheek blush: probability falls off smoothly -> colours blend, no hard patch
    d = min(((abs(p.x) - 0.225) / 0.085) ** 2 + ((p.z - (EZ - 0.14)) / 0.07) ** 2, 9)
    return 8 if rng.random() < 0.95 * math.exp(-d) else 0

# ---- face fur: dense short fur over the front face, combed away from the eyes
NXg, NZg = 30, 22
for i in range(NXg):
    for j in range(NZg):
        x = -FXM - 0.02 + (2 * FXM + 0.04) * (i + 0.5 + rng.uniform(-0.4, 0.4)) / NXg
        z = FZ0 - 0.02 + (FZ1 - FZ0 + 0.04) * (j + 0.5 + rng.uniform(-0.4, 0.4)) / NZg
        p = front_pt(x, z)
        de = min((p - e).length for e in EYES.values())
        if de < ER * 1.08 or in_mouth(p) or in_hair(p) or in_smile(p): continue
        n = radial(p)
        near = min(EYES.values(), key=lambda e: (p - e).length)
        aw = p - near; aw.y = 0; aw = aw.normalized() if aw.length > 1e-4 else Vector((0, 0, 1))
        d = (n * 0.9 + aw * 0.55 + Vector((0, 0, 0.2)) + Vector((rng.gauss(0, 0.2), rng.gauss(0, 0.2), rng.gauss(0, 0.2)))).normalized()
        Lp = rng.uniform(0.045, 0.075) * (0.7 + 0.6 * smooth(ER * 1.08, ER * 1.8, de))
        if in_mouth(p, 0.08): Lp *= 0.7   # shorter fur hugging the mouth
        pts = [p - n * 0.008 + d * Lp * t + Vector((0, 0, -1)) * 0.15 * Lp * t * t for t in (0, 0.5, 1)]
        card(pts, rand_perp(d), [0.05, 0.062, 0.072], *COLS['body'], 0.0, 0.995, blush_mat(p), [wbody(p.z)] * 3, [shade_n(q) for q in pts])

# ---- body fur (shorter than Boxi so the cube silhouette stays crisp)
def ground_clamp(p):
    if p.z >= GZ: return p
    h = Vector((p.x, p.y, 0.0)); out = h.normalized() if h.length > 1e-4 else Vector((1, 0, 0))
    return Vector((p.x, p.y, GZ)) + out * (GZ - p.z) * 0.8
def fur_layer(count, kind):
    # 'velvet': short dense darker plush on the faces (planes stay readable -> depth)
    # 'edge'  : longer bright glowing fur along edges + corners (silhouette glows, bevel reads in ortho)
    for n0 in fib(count):
        root0 = surf(n0, 0.92)
        ef = edge_f(root0)
        if kind == 'velvet' and ef > 0.75: continue
        if kind == 'edge' and ef < 0.3: continue
        if root0.z < 0.03: continue
        if kind == 'velvet' and in_hair(root0): continue   # glow fur stays on the hairline / corners / edges
        if in_face(root0): continue
        if min((root0 - b).length for b in BROW_PTS) < 0.05: continue
        n = (n0 + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.015).normalized()
        root = surf(n, 0.92); cn = radial(root); sn = (root - C).normalized()
        if kind == 'velvet':
            Ln = rng.uniform(0.04, 0.068); segs, wdt, mat = 1, 0.052, 0
            # combed plush: lies along the surface (down on the sides, back on top), card plane ~ surface plane
            g = Vector((0, 0, -1)) + cn * cn.z
            if g.length < 0.3: g = Vector((0, 1, 0)) - cn * cn.y
            g.normalize()
            root = root + cn * 0.06   # start on top of the skin, not inside it
            d = (cn * 0.75 + g * 0.65 + Vector((rng.gauss(0, 0.12), rng.gauss(0, 0.12), rng.gauss(0, 0.12)))).normalized()
            side_v = cn.cross(d)
            side_v = (side_v.normalized() if side_v.length > 1e-4 else rand_perp(d)) + Vector((rng.gauss(0, 0.15), rng.gauss(0, 0.15), rng.gauss(0, 0.15)))
            side_v = (side_v - d * side_v.dot(d)).normalized()
            v0, v1 = 0.16 + 0.22 * max(cn.z, 0.0), 0.80 + 0.19 * max(cn.z, 0.0) + 0.05 * ef   # tops catch more light
        else:
            Ln = rng.uniform(0.07, 0.11) * (1 + 0.6 * smooth(0.8, 1.0, ef)) * (1 + 0.5 * smooth(0.55, 0.8, sorted((abs(v) for v in (root - C) / H))[0])) * (1 + 0.2 * max(cn.z, 0)); segs, wdt, mat = 2, 0.045, 5
            d = (cn * 0.55 + sn * 0.45 + Vector((rng.gauss(0, 0.15), rng.gauss(0, 0.15), rng.gauss(0, 0.15) - 0.05))).normalized()
            side_v = rand_perp(d)
            v0, v1 = 0.45, 0.995
        ts = [i / segs for i in range(segs + 1)]
        pts = [ground_clamp(root + d * Ln * t + Vector((0, 0, -1)) * 0.2 * Ln * t * t) for t in ts]
        if any(in_face(p, 0.03) and p.y < -H * 0.9 for p in pts[1:]): continue
        if any((p - e_).length < 0.225 for p in pts for e_ in EYES.values()): continue
        flex = 0.4 if kind == 'velvet' else 0.9
        wb, wf = wbody(root.z), wfur(n)
        wts = [mixw((wb, 1 - flex * t ** 1.3), (wf, flex * t ** 1.3)) for t in ts]
        card(pts, side_v, [wdt * (1 + 0.5 * t) for t in ts], *COLS['body'], v0, v1, mat, wts, [shade_n(p) for p in pts])
fur_layer(5200, 'velvet')
fur_layer(2400, 'edge')   # scattered glow fur on the near-edge faces (soft fill)

# ---- deterministic glow lines on ALL 12 edges + 8 corners: thin crossed cards (two perpendicular planes) so the glow reads
#      from any viewing angle, never a thick wall. Fuller tufts at the corners.
def edge_lines(per_edge=90):
    for ax in range(3):
        o1, o2 = (ax + 1) % 3, (ax + 2) % 3
        A = Vector((1.0 if ax == 0 else 0.0, 1.0 if ax == 1 else 0.0, 1.0 if ax == 2 else 0.0))
        for s1 in (-1, 1):
            for s2 in (-1, 1):
                bis = Vector((0, 0, 0)); bis[o1] = s1; bis[o2] = s2; bis.normalize()
                for i in range(per_edge):
                    t = -1 + 2 * (i + rng.random()) / per_edge
                    n = Vector((0, 0, 0)); n[ax] = t * 0.92; n[o1] = s1; n[o2] = s2
                    root = surf(n.normalized(), 0.95)
                    if root.z < 0.01: continue
                    corner = smooth(0.72, 1.0, abs(t))
                    for cross in (0, 1):
                        L = rng.uniform(0.055, 0.085) * (1.0 + 0.7 * corner)
                        d = (bis * 0.8 + A * rng.gauss(0, 0.18) + Vector((rng.gauss(0, 0.1), rng.gauss(0, 0.1), rng.gauss(0, 0.1)))).normalized()
                        if corner > 0.3: d = (d + A * math.copysign(0.55 * corner, t)).normalized()    # corner tufts fan outward
                        u_ = [0.0, 0.5, 1.0]
                        pts = [ground_clamp(root + d * L * u + Vector((0, 0, -0.18)) * L * u * u) for u in u_]
                        side = A if cross == 0 else d.cross(A).normalized()
                        wb, wf = wbody(root.z), wfur(n.normalized())
                        wts = [mixw((wb, 1 - 0.9 * u ** 1.3), (wf, 0.9 * u ** 1.3)) for u in u_]
                        card(pts, side, [0.032, 0.04, 0.034], *COLS['body'], 0.5, 0.995, 5, wts, [shade_n(q) for q in pts])
edge_lines()


# ---- hair: soft cap + two space buns (gold ties) + two long spiral ringlets falling behind the cheeks.
#      Bones: Bun_L/R (on Body_Upper), Curl_<S>_01..03 (chain on the bun). Material 6 = hair (tintable cutout), 4 = gold ties.
BUN = {'L': Vector((0.27, 0.12, 2 * H + 0.075)), 'R': Vector((-0.27, 0.12, 2 * H + 0.075))}
BR = 0.13
HAIR_UV = UV_BOW      # light swatch, tinted by the hair material (the dark frame swatch would read near-black)
def curl_pts(s_, n=24):
    sg = SG[s_]; b0 = BUN[s_] + Vector((sg * 0.10, 0.0, -0.06)); pts = []
    for i in range(n + 1):
        s = i / n
        base = b0 + Vector((sg * (0.10 + 0.10 * s), -0.34 * s, -0.46 * s))
        th = TAU * 2.4 * s + (0.0 if s_ == 'L' else math.pi)
        rr = 0.05 * (1 - 0.25 * s) * min(1.0, s * 6)
        pts.append(base + Vector((math.cos(th) * rr, math.sin(th) * rr * 0.8, 0.0)))
    return pts
CPTS = {s_: curl_pts(s_) for s_ in SG}
def curl_w(s_, s):
    names = ['Curl_%s_%02d' % (s_, k) for k in (1, 2, 3)]
    u = s * 3; k = min(int(u), 2); f = u - k
    w = {names[k]: 1.0}
    if f > 0.6 and k < 2: w[names[k + 1]] = 0.5 * (f - 0.6) / 0.4
    if f < 0.4 and k > 0: w[names[k - 1]] = 0.5 * (0.4 - f) / 0.4
    if s < 0.1: w['Bun_' + s_] = 1 - s / 0.1
    tw = sum(w.values()); return {a: b / tw for a, b in w.items()}
def _nn(tg):
    n_ = tg.cross(Vector((0, 1, 0)))
    return n_.normalized() if n_.length > 1e-4 else Vector((1, 0, 0))
for s_, sg in SG.items():
    cen = BUN[s_]; bn = 'Bun_' + s_
    latlong(lambda a, b, cen=cen: cen + Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a) * 0.85)) * BR,
            const_uv(HAIR_UV), lambda q, bn=bn: {bn: 1.0}, 10, 18, 6, cen)
    for dv in fib(260):               # swirled cards = cinnamon-roll bun
        if dv.z < -0.3: continue
        root = cen + Vector((dv.x, dv.y, dv.z * 0.85)) * BR * 0.97
        sw = Vector((-dv.y, dv.x, 0.0))
        d = (dv * 0.55 + sw * 0.8 + Vector((0, 0, 0.1)) + Vector((rng.gauss(0, 0.1), rng.gauss(0, 0.1), rng.gauss(0, 0.1)))).normalized()
        Lq = rng.uniform(0.04, 0.07)
        _st = math.fmod(abs(math.atan2(dv.y, dv.x)) * 3 / math.pi + (0.0 if s_ == 'L' else 0.5), 1.0) < 0.06
        card([root + d * Lq * (1.25 if _st else 1.0) * t for t in (0, 0.5, 1)], rand_perp(d), [0.014, 0.018, 0.02] if _st else [0.04, 0.05, 0.055], *COLS['body'], 0.1, 0.995, 9 if _st else 6, [{bn: 1.0}] * 3, [dv] * 3)
    base = cen + Vector((0, 0, -0.085)); TJ = 20; tor = []          # gold hair tie
    for j in range(TJ):
        a = TAU * j / TJ; dj = Vector((math.cos(a), math.sin(a), 0.0)); row = []
        for m in range(8):
            b = TAU * m / 8; off = dj * math.cos(b) + Vector((0, 0, math.sin(b)))
            row.append(addv(base + dj * 0.115 + off * 0.02, {bn: 1.0}, off))
        tor.append(row)
    for j in range(TJ):
        a = TAU * j / TJ; cc = base + Vector((math.cos(a), math.sin(a), 0.0)) * 0.115
        for m in range(8):
            j2 = (j + 1) % TJ; m2 = (m + 1) % 8
            addf([tor[j][m], tor[j2][m], tor[j2][m2], tor[j][m2]], [UV_BOW] * 4, 4, cc)
    pts = CPTS[s_]; n = len(pts) - 1; sides = 8; rings = []          # spiral ringlet
    for i, c_ in enumerate(pts):
        sv = i / n; tg = (pts[min(i + 1, n)] - pts[max(i - 1, 0)]).normalized()
        nn = _nn(tg); bb = tg.cross(nn).normalized(); rad = 0.026 * (1 - 0.55 * sv ** 1.5); ring = []
        for k in range(sides):
            a = TAU * k / sides; off = nn * math.cos(a) + bb * math.sin(a)
            ring.append(addv(c_ + off * rad, curl_w(s_, sv), off))
        rings.append(ring)
    for i in range(n):
        for k in range(sides):
            k2 = (k + 1) % sides
            addf([rings[i][k], rings[i + 1][k], rings[i + 1][k2], rings[i][k2]], [HAIR_UV] * 4, 6, pts[i])
    tc = addv(pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.02, curl_w(s_, 1.0), (pts[-1] - pts[-2]).normalized())
    for k in range(sides): addf([rings[-1][k], tc, rings[-1][(k + 1) % sides]], [HAIR_UV] * 3, 6, pts[-1])
    def spine_at(sv):
        f = min(max(sv, 0.0), 1.0) * n; k = min(int(f), n - 1); return pts[k].lerp(pts[k + 1], f - k)
    for q in range(90):           # fluffy locks that follow the spiral
        sv0 = rng.uniform(0.0, 0.7); a = TAU * rng.random(); rr_ = rng.uniform(0.012, 0.04)
        pp, nr, wt = [], [], []
        for j_ in range(5):
            sv = min(sv0 + j_ * 0.09, 1.0); i_ = min(int(sv * n), n - 1)
            tg = (pts[i_ + 1] - pts[i_]).normalized(); nn = _nn(tg); bb = tg.cross(nn).normalized()
            off = nn * math.cos(a + sv * 6) + bb * math.sin(a + sv * 6)
            pp.append(spine_at(sv) + off * rr_ * (1 - 0.5 * sv)); nr.append(off); wt.append(curl_w(s_, sv))
        card(pp, rand_perp((pp[-1] - pp[0]).normalized()), [w_ * (0.28 if q % 4 == 0 else 1.0) for w_ in (0.05, 0.058, 0.058, 0.05, 0.034)], *COLS['body'], 0.1, 0.995, (9 if q % 4 == 0 else 6), wt, nr)
# hair cap: short soft fur over crown / back / upper sides, combed toward the nearest bun
for n0 in fib(3000):
    root0 = surf(n0, 0.97)
    if not in_hair(root0) or root0.z < 0.5 or root0.y < -0.3: continue
    cn = radial(root0); root = root0 + cn * 0.02
    tgt = min(BUN.values(), key=lambda c_: (c_ - root).length)
    d = ((tgt - root).normalized() * 0.55 + cn * 0.55 + Vector((rng.gauss(0, 0.1), rng.gauss(0, 0.1), rng.gauss(0, 0.1)))).normalized()
    Ln = rng.uniform(0.07, 0.11)
    pts = [root + d * Ln * t + Vector((0, 0, -1)) * 0.1 * Ln * t * t for t in (0, 0.5, 1)]
    _cs = (abs(root.x) * 9.0) % 1.0 < 0.045 and root.y > -0.05
    card(pts, rand_perp(d), [0.014, 0.018, 0.02] if _cs else [0.05, 0.06, 0.065], *COLS['body'], 0.1, 0.995, 9 if _cs else 6, [{'Body_Upper': 1.0}] * 3, [shade_n(q) for q in pts])
# scalp: the skin under the hair becomes hair material so no pink shows through the strands
for _fi in range(len(F)):
    if FM[_fi] != 1: continue
    _c = sum((Vector(V[_i]) for _i in F[_fi]), Vector()) / len(F[_fi])
    if in_hair(_c) and _c.z > 0.5: FM[_fi] = 6
# ---------------------------------------------------------------- mesh object
me = bpy.data.meshes.new(NAME + "_Mesh")
me.from_pydata(V, [], F); me.update()
uvl = me.uv_layers.new(name="UVMap")
uvl.data.foreach_set("uv", [c for uvs in FUV for uv in uvs for c in uv])
me.polygons.foreach_set("material_index", FM)
me.polygons.foreach_set("use_smooth", [True] * len(F))
for m_ in MATS: me.materials.append(m_)
loop_v = [0] * len(me.loops); me.loops.foreach_get("vertex_index", loop_v)
me.normals_split_custom_set([N[v] for v in loop_v]); me.update()
obj = bpy.data.objects.new(NAME + "_Body", me)
scn.collection.objects.link(obj)

# ---------------------------------------------------------------- armature (same bone layout as Boxi; Face_Front replaces Glasses)
arm_d = bpy.data.armatures.new(NAME + "_Rig")
arm = bpy.data.objects.new(NAME, arm_d)
scn.collection.objects.link(arm)
arm_d.display_type = 'STICK'; arm.show_in_front = True
vl = bpy.context.view_layer; vl.update()
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
for s_, c in EYES.items():
    bone('Eye_' + s_, c, c + Vector((0, -0.1, 0)), 'Body_Upper')
    bone('Lid_Upper_' + s_, c, c + Vector((0.06, 0, 0)), 'Body_Upper')
    bone('Lid_Lower_' + s_, c, c + Vector((0.045, 0, 0)), 'Body_Upper')
    h, n = BROW_HEAD[s_]; bone('Brow_' + s_, h, h + n * 0.06, 'Body_Upper')
bone('Face_Front', (0, -H - 0.05, EZ), (0, -H - 0.13, EZ), 'Body_Upper')
for _nm, _tt in (('Smile_L', 0.85), ('Smile_C', 0.0), ('Smile_R', -0.85)):
    _h = smile_pt(_tt)[0]; bone(_nm, _h, _h + Vector((0, -0.04, 0)), 'Body_Upper')
for s_ in SG:
    bone('Bun_' + s_, BUN[s_], BUN[s_] + Vector((0, 0, 0.1)), 'Body_Upper')
    cp_ = CPTS[s_]; prev_ = 'Bun_' + s_
    for k in range(3):
        nm_ = 'Curl_%s_%02d' % (s_, k + 1)
        bone(nm_, cp_[8 * k], cp_[min(8 * (k + 1), 24)], prev_); prev_ = nm_
if MOUTH:
    bone('Mouth_Upper', (0, YL, MZ + 0.012), (0, YL - 0.04, MZ + 0.012), 'Body')
    bone('Mouth_Lower', (0, YL, MZ - 0.025), (0, YL - 0.04, MZ - 0.025), 'Body')
    for s_, sg in SG.items():
        bone('Mouth_Corner_' + s_, (sg * MA, YL, MZ - 0.014), (sg * MA, YL - 0.04, MZ - 0.014), 'Body')
for i in range(len(FURN)):
    bone(FURN[i], FURH[i], FURH[i] + FURD[i] * 0.1, 'Body_Upper' if FURD0[i].z > -0.1 else 'Body')
bpy.ops.object.mode_set(mode='OBJECT')

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
def fur_pose(p, wind, flutter, t, ks=1, splay=0.0, ground=True):
    for i, bn in enumerate(FURN):
        n = FURD[i]; w = Vector(wind)
        if ground and n.z < 0.1:
            low = smooth(0.1, -0.6, n.z)
            h = Vector((n.x, n.y, 0.0)); h = h.normalized() if h.length > 1e-4 else Vector((0, 0, 0))
            if w.z < 0: w = Vector((w.x, w.y, w.z * (1 - low))) + h * (-w.z) * 0.7 * low
            w += h * splay * low
        wp = w - n * n.dot(w); mag = wp.length
        ph1, ph2, k1, k2 = FPH[i]
        ax = n.cross(wp).normalized() if mag > 1e-5 else n.orthogonal().normalized()
        ax2 = n.cross(ax).normalized()
        a1 = mag + flutter * math.sin(TAU * (k1 * ks * t + ph1))
        a2 = flutter * 0.7 * math.sin(TAU * (k2 * ks * t + ph2))
        setp(p, bn, rot=qw(bn, ax, a1) @ qw(bn, ax2, a2))
# ---- rollercoaster posture: instead of leaning forward/down, she sits BACK (body + head tilt back, chest/face open to
# the camera). Authored forward pitches are inverted + scaled, then a constant backward base lean is added.
LEANB = True
BACK_BODY, BACK_UPPER, EYE_COMP = math.radians(2), math.radians(8), 0.12
def lean_back(x):  return (-0.25 * x - BACK_BODY) if LEANB else x
def ground_lift(a): return 0.55 * math.sin(max(0.0, -a)) if LEANB else 0.0   # keeps the back-bottom edge on the track
def nod_back(x):   return (-0.3 * x - BACK_UPPER) if LEANB else x
def body(p, lift=0.0, sz=1.0, lean=0.0, fwd=0.0):
    fwd = lean_back(fwd)
    lift += ground_lift(fwd)
    sx = 1 / math.sqrt(sz)
    setp(p, 'Body', loc=lw('Body', (0, 0, lift)), scl=(sx, sz, sx), rot=qw('Body', (0, 1, 0), lean) @ qw('Body', (1, 0, 0), fwd))
def upper(p, lean=0.0, nod=0.0):
    nod = nod_back(nod)
    setp(p, 'Body_Upper', rot=qw('Body_Upper', (0, 1, 0), lean) @ qw('Body_Upper', (1, 0, 0), nod))
def eyes(p, yaw, up):
    if LEANB: up -= EYE_COMP   # head is tilted back -> eyes look down a little to stay on the track ahead
    for s_ in SG: bn = 'Eye_' + s_; setp(p, bn, rot=qw(bn, (0, 0, 1), yaw) @ qw(bn, (1, 0, 0), -up))
def lids(p, up, low, b=0.0):
    up = up + 0.05; low = low - 0.07   # soft open eyes, lower lids lifted (smiling eyes)
    up = up + (0.707 - up) * b; low = low + (-0.27 - low) * b
    for s_ in SG:
        setp(p, 'Lid_Upper_' + s_, rot=qw('Lid_Upper_' + s_, (1, 0, 0), up))
        setp(p, 'Lid_Lower_' + s_, rot=qw('Lid_Lower_' + s_, (1, 0, 0), low))
def brows(p, angry=0.0, raise_=0.0):
    angry = angry * 0.5 - 0.22   # resting expression = soft, hopeful inner-brow lift
    for s_, sg in SG.items():
        bn = 'Brow_' + s_; setp(p, bn, rot=qw(bn, (0, -1, 0), sg * angry), loc=lw(bn, (0, 0, raise_)))
def tuft(p, angs):
    if 'Tuft_01' not in MINV: return
    for j, (ay, ax) in enumerate(angs):
        bn = 'Tuft_%02d' % (j + 1); setp(p, bn, rot=qw(bn, (0, 1, 0), ay) @ qw(bn, (1, 0, 0), ax))
def glasses(p, dz=0.0, tilt=0.0):
    pass  # Cubie has no glasses
def smile(p, amt=0.0, skew=0.0):
    # amt > 0 = bigger smile (corners up + out), < 0 = worried; skew > 0 lifts her left corner more (smirk)
    if 'Smile_L' not in MINV: return
    setp(p, 'Smile_L', loc=lw('Smile_L', (0.006 * amt, 0, 0.012 * amt * (1 + 0.3 * skew))))
    setp(p, 'Smile_R', loc=lw('Smile_R', (-0.006 * amt, 0, 0.012 * amt * (1 - 0.15 * skew))))
    setp(p, 'Smile_C', loc=lw('Smile_C', (0, 0, -0.004 * amt)))
def pony(p, t=0.0, stream=0.0, flutter=0.0, freq=0, side=0.0, bounce=0.0):
    # buns bob a little; ringlets stream back (+stream), whip along the chain, sway sideways, bounce on impacts
    if 'Bun_L' not in MINV: return
    for s_, sg in SG.items():
        ph = 0.0 if s_ == 'L' else 0.45
        bn = 'Bun_' + s_
        setp(p, bn, rot=qw(bn, (1, 0, 0), 0.25 * stream + 0.1 * bounce + 0.3 * flutter * math.sin(TAU * freq * t + ph)) @ qw(bn, (0, 0, 1), 0.3 * side))
        for k in range(3):
            cn = 'Curl_%s_%02d' % (s_, k + 1)
            a = stream * (0.5 + 0.3 * k) + bounce * 0.2 * (k + 1) + flutter * 1.2 * math.sin(TAU * freq * t + ph - 0.8 * (k + 1))
            sd = 0.6 * side + 0.5 * flutter * math.sin(TAU * freq * t + ph - 0.55 * k + 1.1)
            setp(p, cn, rot=qw(cn, (1, 0, 0), a) @ qw(cn, (0, 0, 1), sd))
def bow(p, ay=0.0, ax=0.0):
    if 'Bow' not in MINV: return
    setp(p, 'Bow', rot=qw('Bow', (0, 1, 0), ay) @ qw('Bow', (1, 0, 0), ax))
def mouth(p, open_=0.0, wide=0.0, trem=0.0):
    if not MOUTH: return
    # rest shape is already the subtle fear mouth; open_ drops the lower lip, wide pulls corners out+down
    setp(p, 'Mouth_Lower', loc=lw('Mouth_Lower', (0, 0, -0.018 * open_ + trem)))
    setp(p, 'Mouth_Upper', loc=lw('Mouth_Upper', (0, 0, 0.004 * open_ + 0.5 * trem)))
    for s_, sg in SG.items():
        setp(p, 'Mouth_Corner_' + s_, loc=lw('Mouth_Corner_' + s_, (sg * 0.012 * wide, 0, -0.006 * wide)))

# ---------------------------------------------------------------- clips (all along the track toward -Y)
def run(f):  # alt: rolling gallop, small contact bounces
    L = 48; t = f / L; p = {}
    hop = abs(math.sin(math.pi * 4 * t)); contact = 1 - smooth(0, 0.4, hop)
    body(p, lift=0.018 * hop, sz=1 - 0.09 * contact + 0.03 * hop, lean=math.radians(1.5) * math.sin(TAU * 2 * t),
         fwd=math.radians(7) + math.radians(2.5) * math.sin(TAU * 4 * t))
    upper(p, math.radians(1) * math.sin(TAU * 2 * t + 0.6), math.radians(5) + math.radians(2.5) * math.sin(TAU * 4 * t - 0.9))
    fur_pose(p, (0, 0.62, -0.3 * contact + 0.1 * hop), 0.13, t, 2, splay=0.25 * contact)
    tuft(p, [(0.08 * math.sin(TAU * 6 * t - j), -0.32 - 0.05 * j + 0.14 * math.sin(TAU * 4 * t - 0.9 * (j + 1))) for j in range(3)])
    bow(p, 0.06 * math.sin(TAU * 6 * t + 1), -0.15 + 0.1 * math.sin(TAU * 8 * t))
    eyes(p, 0.0, 0.02 * math.sin(TAU * 4 * t))
    lids(p, 0.12, -0.04, blink(f, 30))
    brows(p, 0.15, -0.008 * contact)
    pony(p, t, 0.55, 0.12, 4, 0.0, 0.15 * contact)
    smile(p, 0.6 + 0.2 * contact, 0.8)
    mouth(p, 0.2 + 0.12 * contact, 0.35, 0.001 * math.sin(TAU * 8 * t))
    return p

def idle(f):
    L = 72; t = f / L; p = {}; br = math.sin(TAU * t)
    body(p, sz=1 + 0.025 * br)
    upper(p, math.radians(5) * math.sin(TAU * t + 0.5), math.radians(1.5) * math.sin(TAU * 2 * t))   # curious head tilt
    fur_pose(p, (0, 0.05, -0.10 + 0.04 * br), 0.035, t, 1, splay=0.06 + 0.03 * br)
    tuft(p, [(0.06 * math.sin(TAU * 2 * t - 0.7 * j), 0.04 * math.sin(TAU * t - 0.5 * j)) for j in range(3)])
    yaw = interp([(0, 0), (8, 0), (13, -0.3), (26, -0.3), (31, 0.0), (44, 0.0), (49, 0.24), (60, 0.24), (66, 0.0), (72, 0)], f)
    up = interp([(0, 0.05), (13, 0.0), (26, 0.0), (31, 0.16), (44, 0.16), (49, -0.04), (60, -0.04), (66, 0.05), (72, 0.05)], f)
    seek = smooth(26, 31, f) * (1 - smooth(44, 49, f))      # looks up at the viewer: brows lift, smile grows
    eyes(p, yaw, up); lids(p, 0.0, 0.0, max(blink(f, 6, 7), blink(f, 56, 7), blink(f, 64, 6)))
    brows(p, -0.3 * seek, 0.004 * br + 0.012 * seek)
    bow(p, 0.05 * math.sin(TAU * 2 * t))
    pony(p, t, 0.0, 0.05, 2, 0.06 * math.sin(TAU * t + 0.8))
    smile(p, 0.55 + 0.2 * math.sin(TAU * t) + 0.4 * seek, 0.0)
    mouth(p, 0.1 + 0.05 * math.sin(TAU * t), 0.25, 0.0012 * math.sin(TAU * 12 * t))  # nervous tremble
    return p

def jump_start(f):
    p = {}; t = f / 30
    if f <= 5:
        s = smooth(0, 5, f); sz = 1 - 0.22 * s; lift = 0; vert = 0.25 * s; upl = 0.1 * s; br = -0.01 * s
        fw = 0.06 * s; spl = 0.35 * s
    else:
        e = 1 - (1 - (f - 5) / 7) ** 2
        sz = 0.78 + 0.42 * e; lift = 0.07 * e; vert = 0.25 - 0.85 * e; upl = 0.1 - 0.24 * e; br = -0.01 + 0.035 * e
        fw = 0.06 + 0.04 * e; spl = 0.35 * (1 - e)
    body(p, lift, sz, fwd=fw); upper(p, 0, 0.5 * fw)
    fur_pose(p, (0, 0.3, vert), 0.05, t, splay=spl)
    tuft(p, [(0, -vert * 0.3 * (j + 1)) for j in range(3)])
    eyes(p, 0, 0.18 * max(0, -vert)); lids(p, upl, 0.04 * max(0, -vert)); brows(p, 0, br)
    bow(p, 0, -vert * 0.3)
    pony(p, t, 0.1 + 0.5 * max(0.0, -vert), 0.06, 4)
    smile(p, 0.5 + 0.4 * max(0.0, -vert), 0.6)
    mouth(p, 0.3 + 0.5 * max(0.0, -vert), 0.3)
    return p

def jump_air(f):
    L = 24; t = f / L; p = {}
    body(p, 0.07, 1.07 + 0.015 * math.sin(TAU * 2 * t), fwd=0.1 + 0.02 * math.sin(TAU * t)); upper(p, 0, 0.04)
    fur_pose(p, (0, 0.45, 0.5), 0.13, t)
    tuft(p, [(0.12 * math.sin(TAU * 3 * t - 0.9 * j), -0.15 + 0.1 * math.sin(TAU * 2 * t - j)) for j in range(3)])
    eyes(p, 0, -0.18); lids(p, -0.14, 0.04); brows(p, -0.05, 0.025)
    bow(p, 0.08 * math.sin(TAU * 3 * t), -0.15)
    pony(p, t, 0.75, 0.14, 4, 0.0, 0.2)
    smile(p, 1.0, 0.4)
    mouth(p, 0.9, 0.15, 0.0015 * math.sin(TAU * 6 * t))  # scared "o" in the air
    return p

def land(f):
    p = {}; t = f / 30
    sz = interp([(0, 1.07), (2, 0.70), (6, 1.08), (10, 0.96), (14, 1.01), (18, 1.0)], f)
    lift = interp([(0, 0.07), (2, 0.0), (18, 0.0)], f)
    vert = 0.5 * (1 - f / 2) if f < 2 else -0.7 * math.exp(-(f - 2) / 4.5) * math.cos(TAU * (f - 2) / 11)
    fw = interp([(0, 0.10), (3, 0.17), (8, -0.03), (13, 0.02), (18, 0.0)], f)
    body(p, lift, sz, fwd=fw); upper(p, 0, -0.08 * vert + 0.4 * fw)
    fur_pose(p, (0, 0.35 * (1 - f / 18), vert), 0.04, t, splay=max(0.0, 1 - sz) * 1.6)
    tuft(p, [(0, -vert * 0.3 * (j + 1) - 0.6 * fw) for j in range(3)])
    sq = interp([(0, 0), (2, 0.35), (6, 0.3), (12, 0), (18, 0)], f); op = 1 - smooth(0, 3, f)
    lids(p, -0.14 * op + sq, 0.04 * op - sq * 0.3)
    eyes(p, 0, interp([(0, -0.18), (4, 0.0), (18, 0.0)], f)); brows(p, sq * 0.4, -0.02 * sq / 0.35)
    bow(p, 0, -vert * 0.3)
    pony(p, t, interp([(0, 0.7), (3, -0.25), (7, 0.2), (12, 0.0), (18, 0.0)], f), 0.1 * math.exp(-f / 6), 3)
    smile(p, interp([(0, 0.9), (4, 0.3), (10, 0.8), (18, 0.6)], f), 0.7)
    mouth(p, interp([(0, 0.9), (2, 0.1), (6, 0.5), (12, 0.25), (18, 0.2)], f), 0.3 + sq)
    return p

def smash(f):
    p = {}; t = f / 30
    sz = interp([(0, 1.0), (7, 1.12), (9, 1.28), (10, 0.6), (13, 1.12), (17, 0.94), (21, 1.02), (26, 1.0)], f)
    lift = interp([(0, 0.0), (7, 0.16), (10, 0.0), (26, 0.0)], f)
    if f < 7: vert = -0.3 * smooth(0, 7, f)
    elif f < 10: vert = -0.3 + 0.9 * smooth(7, 10, f)
    else: vert = -1.0 * math.exp(-(f - 10) / 5) * math.cos(TAU * (f - 10) / 12)
    body(p, lift, sz, fwd=interp([(0, 0.0), (7, -0.05), (10, 0.06), (16, -0.02), (26, 0.0)], f)); upper(p, 0, -0.1 * vert)
    fur_pose(p, (0, 0.15, vert), 0.05, t, splay=max(0.0, 1 - sz) * 2.0)
    tuft(p, [(0.1 * math.sin(TAU * t * 3 - j), -vert * 0.35 * (j + 1)) for j in range(3)])
    ang = interp([(0, 0), (5, 0.35), (12, 0.35), (20, 0.12), (26, 0)], f)
    lids(p, interp([(0, 0), (5, 0.25), (10, 0.4), (16, 0.25), (26, 0)], f), interp([(0, 0), (5, -0.1), (10, -0.15), (26, 0)], f))
    eyes(p, 0, interp([(0, 0), (6, -0.1), (10, 0.1), (20, 0), (26, 0)], f))
    brows(p, ang, -0.015 * ang); bow(p, 0.1 * math.sin(TAU * t * 3), -vert * 0.35)
    pony(p, t, interp([(0, 0.1), (7, 0.9), (10, -0.2), (13, 0.3), (18, 0.05), (26, 0.05)], f), 0.08, 4)
    smile(p, interp([(0, 0.4), (7, 0.9), (10, 0.2), (16, 0.7), (26, 0.5)], f), 1.0)
    mouth(p, interp([(0, 0.2), (7, 0.6), (10, 0.0), (14, 0.5), (26, 0.2)], f), interp([(0, 0.3), (10, 0.9), (20, 0.4), (26, 0.3)], f))
    return p

def run_v2(f):  # main run: upright, grounded, leaning/bending into the run, wind from the front
    L = 96; t = f / L; p = {}
    vib = math.sin(TAU * 24 * t)
    setp(p, 'Body', loc=lw('Body', (0, 0, 0.004 * vib + ground_lift(lean_back(math.radians(6) + math.radians(1) * math.sin(TAU * 8 * t))))),
         scl=(0.98, 0.965 - 0.008 * math.sin(TAU * 16 * t), 1.05 + 0.01 * math.sin(TAU * 16 * t)),
         rot=qw('Body', (1, 0, 0), lean_back(math.radians(6) + math.radians(1) * math.sin(TAU * 8 * t)))
             @ qw('Body', (0, 0, 1), math.radians(1.5) * math.sin(TAU * 4 * t))
             @ qw('Body', (0, 1, 0), math.radians(1.2) * math.sin(TAU * 6 * t)))
    # the cube bends: top half leans further into the run than the base
    upper(p, math.radians(0.8) * math.sin(TAU * 12 * t), math.radians(6) + math.radians(1.5) * math.sin(TAU * 8 * t - 0.8))
    fur_pose(p, (0, 0.85, 0.05 + 0.04 * math.sin(TAU * 8 * t)), 0.16, t, 4, splay=0.08 + 0.03 * math.sin(TAU * 16 * t))
    tuft(p, [(0.1 * math.sin(TAU * 10 * t - j), -0.42 - 0.06 * j + 0.16 * math.sin(TAU * 12 * t - 0.9 * (j + 1))) for j in range(3)])
    bow(p, 0.08 * math.sin(TAU * 12 * t + 1), -0.25 + 0.12 * math.sin(TAU * 16 * t))
    eyes(p, 0.02 * math.sin(TAU * 6 * t), 0.015 * vib)
    lids(p, 0.2, -0.08, max(blink(f, 40, 6), blink(f, 82, 6)))
    brows(p, 0.25 + 0.03 * math.sin(TAU * 8 * t), -0.006 + 0.002 * vib)
    pony(p, t, 0.75, 0.14, 12, 0.08 * math.sin(TAU * 4 * t))
    smile(p, 0.7 + 0.1 * math.sin(TAU * 8 * t), 0.9)
    mouth(p, 0.28 + 0.08 * math.sin(TAU * 8 * t), 0.4, 0.0015 * vib)  # running scared, lip tremble
    return p

def hang(f):  # hanging from a ledge by the top edge: stretched, pendulum around the TOP, fur droops, worried look up
    L = 72; t = f / L; p = {}
    sz = 1.10 + 0.015 * math.sin(TAU * 2 * t)
    ax = math.radians(4.0) * math.sin(TAU * t)
    ay = math.radians(2.5) * math.sin(TAU * t + 1.3)
    h = 2 * H * sz
    sx = 1 / math.sqrt(sz)
    # rotate about the bottom pivot, then translate so the top edge stays at the hold point
    setp(p, 'Body', loc=lw('Body', (-h * math.sin(ay), h * math.sin(ax),
                                     -(2 * H) * (sz - 1) + h * (1 - math.cos(ax)) + h * (1 - math.cos(ay)))),
         scl=(sx, sz, sx), rot=qw('Body', (1, 0, 0), ax) @ qw('Body', (0, 1, 0), ay))
    upper(p, 0.02 * math.sin(TAU * t + 0.7), -0.04)
    fur_pose(p, (0.08 * math.sin(TAU * t + 1.3), -0.1 * math.sin(TAU * t), -0.5), 0.05, t, 2, ground=False)
    eyes(p, 0.06 * math.sin(TAU * t), 0.2)
    lids(p, -0.12, 0.03, blink(f, 44, 8))
    pony(p, t, 0.0, 0.05, 1, 0.08 * math.sin(TAU * t + 1.3))
    smile(p, -0.5, 0.0)   # worried while hanging
    brows(p, -0.2, 0.012)
    return p

def blink_only(f):
    p = {}; lids(p, 0, 0, blink(f, 0, 8)); return p

LIDS = [b for b in BONES if b.startswith('Lid_')]
CLIPS = [("Run_InPlace_v2", 96, run_v2, None), ("Run_InPlace", 48, run, None), ("Idle", 72, idle, None),
         ("Jump_Start", 12, jump_start, None), ("Jump_Air", 24, jump_air, None), ("Land", 18, land, None),
         ("Smash", 26, smash, None), ("Hang", 72, hang, None), ("Blink", 8, blink_only, LIDS)]

arm.animation_data_create()
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
ID = [Vector((0, 0, 0)), Quaternion(), Vector((1, 1, 1))]
made = []
for name, L, fn, only in CLIPS:
    act = bpy.data.actions.new(ACTP + name); act.use_fake_user = True
    arm.animation_data.action = act
    bl = only or BONES
    LEANB = name != "Hang"
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
    act.use_cyclic = name in ("Run_InPlace", "Run_InPlace_v2", "Idle", "Jump_Air", "Hang")
    made.append(name)

# One NLA track per clip, named with the plain clip name. Export uses NLA strips so only THIS
# character's clips go into its FBX (other characters in the file share bone names).
arm.animation_data.action = None
for name, L, fn, only in CLIPS:
    tr = arm.animation_data.nla_tracks.new(); tr.name = name
    st = tr.strips.new(name, 0, bpy.data.actions[ACTP + name]); st.name = name
    if hasattr(st, "action_slot") and st.action_slot is None and len(st.action.slots):
        st.action_slot = st.action.slots[0]

# ---------------------------------------------------------------- show Cubie, hide other characters
for o in scn.objects:
    if o.name.startswith(("FluffyLady", "Cubie", "Sunny", "Lumi")):
        o.hide_set(True); o.hide_render = True

# ---------------------------------------------------------------- export to the Unity folder
os.makedirs(os.path.join(UNITY, "Models"), exist_ok=True); os.makedirs(os.path.join(UNITY, "Textures"), exist_ok=True)
shutil.copy(TEX, os.path.join(UNITY, "Textures", "T_%s_Atlas.png" % NAME))
vl.update()
for o in scn.objects: o.select_set(False)
arm.select_set(True); obj.select_set(True); vl.objects.active = arm
for tr in arm.animation_data.nla_tracks: tr.mute = False
FBX = os.path.join(UNITY, "Models", NAME + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False,
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False)
# back to a clean preview state: tracks muted, run clip active
for tr in arm.animation_data.nla_tracks: tr.mute = True
arm.animation_data.action = bpy.data.actions[ACTP + "Run_InPlace_v2"]
scn.frame_start, scn.frame_end = 0, 95

tris = sum(len(f) - 2 for f in F)
result = {"verts": len(V), "tris": tris, "bones": len(BONES), "clips": made, "fbx": FBX,
          "fbx_mb": round(os.path.getsize(FBX) / 1e6, 2)}
