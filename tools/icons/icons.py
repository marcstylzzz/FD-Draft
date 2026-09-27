"""FD-Draft toolbar icons: 24x24 vector primitives, shared by the HTML preview and the WPF app.

Primitive tuples:
  ("l", x1, y1, x2, y2, role, opts)      line
  ("p", d, role, opts)                   SVG-style path (M L A Q C Z only)
  ("c", cx, cy, r, role, opts)           circle
  ("r", x, y, w, h, role, opts)          rectangle
  ("t", x, y, text, role, size, anchor)  text, baseline at y, anchor start|middle|end
opts: dict with dash (bool), fill (bool/role), w (stroke width)
Roles: ink (existing geometry), new (result, blue), lbl (annotation text, red),
       snap (snap marker, orange), ghost (before-state, grey dashed), ok (create, green)
"""
import math

def arrow(x1, y1, x2, y2, role="ink", head=3.2, w=1.4):
    a = math.atan2(y2 - y1, x2 - x1)
    hx1 = x2 - head * math.cos(a - 0.45); hy1 = y2 - head * math.sin(a - 0.45)
    hx2 = x2 - head * math.cos(a + 0.45); hy2 = y2 - head * math.sin(a + 0.45)
    f = lambda v: round(v, 2)
    return [("l", x1, y1, x2, y2, role, {"w": w}),
            ("p", f"M{f(hx1)} {f(hy1)} L{x2} {y2} L{f(hx2)} {f(hy2)} Z", role, {"fill": True, "w": 0.8})]

def arrowhead(x, y, ang_deg, role="ink", head=3.2):
    a = math.radians(ang_deg); f = lambda v: round(v, 2)
    return [("p", f"M{f(x - head*math.cos(a-0.45))} {f(y - head*math.sin(a-0.45))} L{x} {y} L{f(x - head*math.cos(a+0.45))} {f(y - head*math.sin(a+0.45))} Z", role, {"fill": True, "w": 0.8})]

BRG = "45°"      # bearing stand-in
DST = "12.5"     # distance stand-in
S = 6.2          # label text size

ICONS = {}
def icon(bar, key, name):
    def deco(fn):
        ICONS[key] = dict(bar=bar, name=name, prims=fn())
        return fn
    return deco

# ---------------- Modify ----------------
@icon("Modify", "move", "Move")
def _():
    return [("r", 2.5, 12.5, 8, 8, "ghost", {"dash": True}),
            ("r", 13.5, 3.5, 8, 8, "new", {})] + arrow(7, 16, 15.5, 9.5, "ink")

@icon("Modify", "copy", "Copy")
def _():
    return [("r", 2.5, 12.5, 8, 8, "ink", {}),
            ("r", 13.5, 3.5, 8, 8, "new", {})] + arrow(7, 16, 15.5, 9.5, "ink") + \
           [("l", 18.5, 14.5, 18.5, 20.5, "ok", {"w": 1.6}), ("l", 15.5, 17.5, 21.5, 17.5, "ok", {"w": 1.6})]

@icon("Modify", "rotate", "Rotate")
def _():
    return [("l", 4, 20, 20, 20, "ghost", {"dash": True}),
            ("l", 4, 20, 15.3, 8.7, "new", {"w": 1.8}),
            ("c", 4, 20, 1.4, "ink", {"fill": True}),
            ("p", "M17 20 A13 13 0 0 0 13.2 11.2", "ink", {"w": 1.3})] + arrowhead(13.2, 11.2, -130, "ink")

@icon("Modify", "mirror", "Mirror")
def _():
    return [("l", 12, 2, 12, 22, "ink", {"dash": True, "w": 1.1}),
            ("p", "M10 5 L3 18 L10 18 Z", "ghost", {"dash": True}),
            ("p", "M14 5 L21 18 L14 18 Z", "new", {})]

@icon("Modify", "stretch", "Stretch")
def _():
    return [("p", "M3 20 L10 8 L20 20", "ghost", {"dash": True}),
            ("p", "M3 20 L15 5 L20 20", "new", {})] + arrow(10, 8, 13.8, 6.4, "ink", head=2.6, w=1.1) + \
           [("r", 13.5, 3.5, 3, 3, "snap", {"fill": True})]

@icon("Modify", "trim", "Trim")
def _():
    return [("l", 12, 2, 12, 22, "ink", {"w": 1.8}),
            ("l", 2, 12, 12, 12, "new", {"w": 1.8}),
            ("l", 12, 12, 22, 12, "ghost", {"dash": True}),
            ("l", 16, 8, 20, 16, "lbl", {"w": 1.4}), ("l", 20, 8, 16, 16, "lbl", {"w": 1.4})]

@icon("Modify", "extend", "Extend")
def _():
    return [("l", 20, 2, 20, 22, "ink", {"w": 1.8}),
            ("l", 2, 12, 10, 12, "ink", {"w": 1.8}),
            ("l", 10, 12, 20, 12, "new", {"w": 1.8, "dash": True})] + arrowhead(19, 12, 0, "new")

@icon("Modify", "fillet", "Fillet")
def _():
    return [("l", 3, 21, 3, 11, "ink", {"w": 1.8}), ("l", 13, 3, 21, 3, "ink", {"w": 1.8}),
            ("p", "M3 11 L3 3 L13 3", "ghost", {"dash": True}),
            ("p", "M3 11 A10 10 0 0 1 13 3", "new", {"w": 1.8})]

@icon("Modify", "offset", "Offset")
def _():
    return [("p", "M3 19 L3 9 A6 6 0 0 1 9 3 L21 3", "ink", {"w": 1.6}),
            ("p", "M9 21 L9 13 A4 4 0 0 1 13 9 L21 9", "new", {"w": 1.6})] + arrow(3.5, 20.5, 8, 20.5, "ink", head=2.4, w=1)

@icon("Modify", "erase", "Erase")
def _():
    return [("p", "M3 17 L10 6 L18 13", "ghost", {"dash": True}),
            ("p", "M7 21 L20 8 L23 11 L13 21 Z", "lbl", {"w": 1.3}),
            ("l", 11, 17, 16, 22, "lbl", {"w": 1.1})]

@icon("Modify", "explode", "Explode")
def _():
    return [("p", "M2 9 L2 2 L9 2", "new", {"w": 1.7}), ("p", "M15 2 L22 2 L22 9", "new", {"w": 1.7}),
            ("p", "M22 15 L22 22 L15 22", "new", {"w": 1.7}), ("p", "M9 22 L2 22 L2 15", "new", {"w": 1.7})] + \
           arrow(10, 10, 6.5, 6.5, "lbl", 2.4, 1.1) + arrow(14, 10, 17.5, 6.5, "lbl", 2.4, 1.1) + arrow(14, 14, 17.5, 17.5, "lbl", 2.4, 1.1) + arrow(10, 14, 6.5, 17.5, "lbl", 2.4, 1.1)

@icon("Modify", "join", "Join")
def _():
    return [("p", "M3 19 L10 8", "ink", {"w": 1.8}), ("p", "M10 8 L21 14", "ink", {"w": 1.8}),
            ("c", 10, 8, 2.4, "new", {"w": 1.4})]

# ---------------- View ----------------
@icon("View", "zoomwin", "Zoom Window")
def _():
    return [("r", 2.5, 2.5, 13, 10, "ink", {"dash": True, "w": 1.1}),
            ("c", 14, 13, 5, "new", {"w": 1.7}), ("l", 17.6, 16.6, 22, 21, "new", {"w": 2.4})]

@icon("View", "zoomin", "Zoom In")
def _():
    return [("c", 10, 10, 7, "new", {"w": 1.7}), ("l", 15, 15, 21.5, 21.5, "new", {"w": 2.4}),
            ("l", 6.5, 10, 13.5, 10, "ink", {"w": 1.7}), ("l", 10, 6.5, 10, 13.5, "ink", {"w": 1.7})]

@icon("View", "zoomext", "Zoom Extents")
def _():
    return [("p", "M2.5 8 L2.5 2.5 L8 2.5 M16 2.5 L21.5 2.5 L21.5 8 M21.5 16 L21.5 21.5 L16 21.5 M8 21.5 L2.5 21.5 L2.5 16", "ink", {"w": 1.7}),
            ("p", "M7 16 L11 8 L17 13", "new", {"w": 1.6})]

@icon("View", "pan", "Pan")
def _():
    out = [("l", 12, 4, 12, 20, "ink", {"w": 1.5}), ("l", 4, 12, 20, 12, "ink", {"w": 1.5})]
    for x, y, a in [(12, 2.5, -90), (12, 21.5, 90), (2.5, 12, 180), (21.5, 12, 0)]:
        out += arrowhead(x, y, a, "new", 4)
    return out

# ---------------- Object snaps ----------------
@icon("Snaps", "snap_end", "Endpoint")
def _():
    return [("l", 3, 21, 16, 8, "ink", {"w": 1.6}), ("r", 13, 3, 7, 7, "snap", {"w": 1.8})]

@icon("Snaps", "snap_mid", "Midpoint")
def _():
    return [("l", 2, 20, 22, 6, "ink", {"w": 1.6}), ("p", "M12 7.5 L17 16 L7 16 Z", "snap", {"w": 1.8})]

@icon("Snaps", "snap_int", "Intersection")
def _():
    return [("l", 3, 20, 21, 5, "ink", {"w": 1.4}), ("l", 3, 5, 21, 20, "ink", {"w": 1.4}),
            ("l", 8, 7.5, 16, 17.5, "snap", {"w": 2.4}), ("l", 16, 7.5, 8, 17.5, "snap", {"w": 2.4})]

@icon("Snaps", "snap_cen", "Center")
def _():
    return [("c", 12, 12, 9, "ink", {"w": 1.4}), ("c", 12, 12, 3.6, "snap", {"w": 1.8})]

@icon("Snaps", "snap_perp", "Perpendicular")
def _():
    return [("l", 3, 20, 21, 20, "ink", {"w": 1.4}), ("l", 12, 20, 12, 4, "ink", {"w": 1.4}),
            ("p", "M5 20 L5 13 L12 13", "snap", {"w": 1.8})]

@icon("Snaps", "snap_node", "Node")
def _():
    return [("c", 12, 12, 7, "snap", {"w": 1.8}), ("l", 7, 7, 17, 17, "ink", {"w": 1.4}), ("l", 17, 7, 7, 17, "ink", {"w": 1.4})]

# ---------------- MS Labels 1 ----------------
def course(broken=False, gap=(8, 16)):
    if broken:
        return [("l", 1.5, 16, gap[0], 16, "ink", {"w": 1.6}), ("l", gap[1], 16, 22.5, 16, "ink", {"w": 1.6})]
    return [("l", 1.5, 16, 22.5, 16, "ink", {"w": 1.6})]

@icon("FD Labels", "split_brg", "Auto Split Bearing")
def _():
    return [("l", 1.5, 13, 22.5, 13, "ink", {"w": 1.6}),
            ("t", 7, 10.5, "N45°", "lbl", 6, "middle"), ("t", 17, 21, "12'30\"", "lbl", 6, "middle")]

@icon("FD Labels", "brg_on", "Bearing on Centre of Line")
def _():
    return course(True, (5, 19)) + [("t", 12, 18.2, BRG, "lbl", S, "middle")]

@icon("FD Labels", "brg_off", "Auto Bearing off Line")
def _():
    return course() + [("t", 12, 7.5, BRG, "lbl", S, "middle"), ("l", 12, 9, 12, 15, "ghost", {"dash": True, "w": 1})]

@icon("FD Labels", "dist_on", "Auto Distance")
def _():
    return course(True, (4, 20)) + [("t", 12, 18.2, DST, "lbl", S, "middle")]

@icon("FD Labels", "dist_off", "Auto Distance off Line")
def _():
    return course() + [("t", 12, 7.5, DST, "lbl", S, "middle"), ("l", 12, 9, 12, 15, "ghost", {"dash": True, "w": 1})]

@icon("FD Labels", "brg_dist", "Auto Bearing/Distance")
def _():
    return [("l", 1.5, 12, 22.5, 12, "ink", {"w": 1.6}),
            ("t", 12, 9.5, BRG, "lbl", S, "middle"), ("t", 12, 19.5, DST, "new", S, "middle")]

@icon("FD Labels", "brg_dash_dist", "Auto Bearing - Distance")
def _():
    return course() + [("t", 0.5, 12.5, "45°", "lbl", 5.2, "start"), ("t", 23.5, 12.5, "12.5", "new", 5.2, "end"),
                       ("l", 9.6, 10.6, 11.4, 10.6, "ink", {"w": 1})]

@icon("FD Labels", "dist_dash_brg", "Auto Distance - Bearing")
def _():
    return course() + [("t", 0.5, 12.5, "12.5", "new", 5.2, "start"), ("t", 23.5, 12.5, "45°", "lbl", 5.2, "end"),
                       ("l", 12.6, 10.6, 14.4, 10.6, "ink", {"w": 1})]

@icon("FD Labels", "brg_over_dist", "Auto Bearing/Distance // Line")
def _():
    return [("l", 1.5, 20.5, 22.5, 20.5, "ink", {"w": 1.6}),
            ("t", 12, 9, BRG, "lbl", S, "middle"), ("t", 12, 16.5, DST, "new", S, "middle")]

@icon("FD Labels", "dist_over_brg", "Auto Distance/Bearing // Line")
def _():
    return [("l", 1.5, 20.5, 22.5, 20.5, "ink", {"w": 1.6}),
            ("t", 12, 9, DST, "new", S, "middle"), ("t", 12, 16.5, BRG, "lbl", S, "middle")]

@icon("FD Labels", "add_angle", "Auto Add Angle")
def _():
    return [("l", 3, 20, 22, 20, "ink", {"w": 1.6}), ("l", 3, 20, 17, 4, "ink", {"w": 1.6}),
            ("p", "M12 20 A9 9 0 0 0 8.9 13.2", "lbl", {"w": 1.3}),
            ("t", 18.5, 16.5, "48°", "lbl", 5.6, "middle")]

@icon("FD Labels", "arrows_line", "Auto Arrows on Line")
def _():
    return [("l", 1.5, 18, 22.5, 18, "ink", {"w": 1.6})] + arrow(4, 11, 20, 11, "lbl", 3.4, 1.2) + arrowhead(4, 11, 180, "lbl", 3.4)

@icon("FD Labels", "curve_on", "Label on Curve")
def _():
    return [("p", "M2 20 A16 16 0 0 1 22 20", "ink", {"w": 1.6}),
            ("p", "M5.5 15 A12 12 0 0 1 18.5 15", "lbl", {"w": 2.6, "dash": True})]

@icon("FD Labels", "curve_off", "Label off Curve")
def _():
    return [("p", "M2 22 A14 14 0 0 1 16 10", "ink", {"w": 1.6}),
            ("t", 14.5, 5.5, "R=", "lbl", 5.6, "middle"), ("t", 19, 12.5, "L=", "lbl", 5.6, "middle"),
            ("l", 9, 13.5, 11.5, 11, "ghost", {"dash": True, "w": 1})]

@icon("FD Labels", "text_arc", "Text on Arc")
def _():
    return [("p", "M2 21 A15 15 0 0 1 22 21", "ghost", {"dash": True, "w": 1.1}),
            ("t", 5.2, 16, "a", "lbl", 7, "middle"), ("t", 12, 11.5, "b", "lbl", 7, "middle"), ("t", 18.8, 16, "c", "lbl", 7, "middle")]

# ---------------- a few from the new bars ----------------
@icon("FD Ties", "house_tie", "Auto House Tie with Arrows")
def _():
    return [("l", 2, 3, 2, 21, "ink", {"w": 1.6}),
            ("r", 10.5, 7.5, 11, 9, "new", {"w": 1.6})] + arrow(7, 12, 3, 12, "lbl", 2.8, 1.1) + arrow(6, 12, 10, 12, "lbl", 2.8, 1.1) + \
           [("t", 6.5, 20.5, "3.2", "lbl", 5.4, "middle")]

@icon("FD Ties", "line_table", "Add Lines to Table")
def _():
    return [("r", 2.5, 3.5, 19, 17, "ink", {"w": 1.3}), ("l", 2.5, 8.5, 21.5, 8.5, "ink", {"w": 1.3}),
            ("l", 8, 3.5, 8, 20.5, "ink", {"w": 1.0}), ("l", 2.5, 14.5, 21.5, 14.5, "ghost", {"w": 0.9}),
            ("t", 5.2, 13, "L1", "lbl", 4.6, "middle"), ("l", 10, 12, 19, 12, "new", {"w": 1.3})]

@icon("FD Ties", "curvy_leader", "Curvy Leader")
def _():
    return [("p", "M20 20 C18 9 8 17 5 6", "ink", {"w": 1.5})] + arrowhead(5, 6, -105, "lbl", 4) + [("l", 20, 20, 23, 20, "ink", {"w": 1.5})]

@icon("FD Text Edit", "rot180", "Rotate Text 180°")
def _():
    return [("t", 12, 11, "AB", "lbl", 8, "middle"),
            ("p", "M4 14 A9 9 0 0 0 20 14", "ink", {"w": 1.3})] + arrowhead(20, 14, -60, "ink", 3)

@icon("FD Text Edit", "rot_line", "Rotate Text to Line")
def _():
    return [("l", 2, 21, 21, 6, "ink", {"w": 1.6}),
            ("t", 0, 0, "AB", "lbl", 7.5, "middle", {"rot": -38, "at": (10, 11.5)})]

@icon("FD Layer", "layer_iso", "Layer Isolate")
def _():
    return [("p", "M12 3 L22 8 L12 13 L2 8 Z", "new", {"w": 1.4, "fill": "newfill"}),
            ("p", "M2 12 L12 17 L22 12", "ghost", {"w": 1.2, "dash": True}),
            ("p", "M2 16 L12 21 L22 16", "ghost", {"w": 1.2, "dash": True})]

@icon("FD Layer", "layer_freeze", "Layer Freeze")
def _():
    return [("p", "M10 3 L20 8 L10 13 L0.5 8 Z", "ink", {"w": 1.3}),
            ("p", "M0.5 13 L10 18 L14 16", "ink", {"w": 1.3})] + \
           [("l", 18, 11, 18, 23, "new", {"w": 1.4}), ("l", 12.8, 14, 23.2, 20, "new", {"w": 1.4}), ("l", 12.8, 20, 23.2, 14, "new", {"w": 1.4})]

@icon("Dimensioning", "dim_aligned", "Aligned Dimension")
def _():
    return [("l", 4, 20, 18, 6, "ink", {"w": 1.1}), ("l", 18, 6, 21, 9, "ghost", {"w": 1}), ("l", 4, 20, 7, 23, "ghost", {"w": 1}),
            ("l", 7.5, 21, 20, 8.5, "new", {"w": 1.2})] + arrowhead(20, 8.5, -45, "new", 3) + arrowhead(7.5, 21, 135, "new", 3) + \
           [("t", 0, 0, DST, "lbl", 5.6, "middle", {"rot": -45, "at": (10.5, 11)})]

@icon("Dimensioning", "dim_angular", "Angular Dimension")
def _():
    return [("l", 3, 20, 22, 20, "ink", {"w": 1.4}), ("l", 3, 20, 16, 5, "ink", {"w": 1.4}),
            ("p", "M17 20 A14 14 0 0 0 12.2 9.4", "new", {"w": 1.2})] + arrowhead(17, 20, 90, "new", 3) + arrowhead(12.2, 9.4, -140, "new", 3)


# =====================================================================================
# Helpers for the full set
# =====================================================================================
def node(x, y, role="ink", r=1.6):
    return [("c", x, y, r, role, {"w": 1.3})]

def layers(role_top="ink", ghost_lower=False, y0=0.0, x0=0.0, scale=1.0):
    """Three stacked layer sheets (isometric diamonds), top one first."""
    f = lambda v: round(v, 2)
    def dia(dy, role, dash=False, fill=None):
        o = {"w": 1.3}
        if dash: o["dash"] = True
        if fill: o["fill"] = fill
        return ("p", f"M{f(x0+11*scale)} {f(y0+dy)} L{f(x0+20*scale)} {f(y0+dy+4.5*scale)} L{f(x0+11*scale)} {f(y0+dy+9*scale)} L{f(x0+2*scale)} {f(y0+dy+4.5*scale)} Z", role, o)
    lo = "ghost" if ghost_lower else "ink"
    return [("p", f"M{f(x0+2*scale)} {f(y0+13.5*scale)} L{f(x0+11*scale)} {f(y0+18*scale)} L{f(x0+20*scale)} {f(y0+13.5*scale)}", lo, {"w": 1.2, "dash": ghost_lower}),
            ("p", f"M{f(x0+2*scale)} {f(y0+9*scale)} L{f(x0+11*scale)} {f(y0+13.5*scale)} L{f(x0+20*scale)} {f(y0+9*scale)}", lo, {"w": 1.2, "dash": ghost_lower}),
            dia(0.5*scale, role_top)]

def small_layers():
    """Compact layer stack at the top-left, leaving the bottom-right corner for a badge."""
    return [("p", "M1.5 11 L9 14.8 L16.5 11", "ink", {"w": 1.2}),
            ("p", "M1.5 7.5 L9 11.3 L16.5 7.5", "ink", {"w": 1.2}),
            ("p", "M9 0.5 L16.5 4.3 L9 8 L1.5 4.3 Z", "ink", {"w": 1.3})]

def magnifier(cx=10, cy=10, r=7, role="new"):
    k = r * 0.7071
    return [("c", cx, cy, r, role, {"w": 1.7}), ("l", round(cx + k, 2), round(cy + k, 2), round(cx + k + 5.5, 2), round(cy + k + 5.5, 2), role, {"w": 2.4})]

def table(x=2.5, y=3.5, w=19, h=17, rows=3):
    out = [("r", x, y, w, h, "ink", {"w": 1.3}), ("l", x, y + 5, x + w, y + 5, "ink", {"w": 1.3}), ("l", x + 5.5, y, x + 5.5, y + h, "ink", {"w": 1.0})]
    step = (h - 5) / rows
    for i in range(1, rows):
        out.append(("l", x, round(y + 5 + i * step, 2), x + w, round(y + 5 + i * step, 2), "ghost", {"w": 0.9}))
    return out

def pencil(x, y, role="snap"):
    """A small pencil whose tip is at (x, y), pointing down-left."""
    f = lambda v: round(v, 2)
    return [("p", f"M{x} {y} L{f(x+1.2)} {f(y-3.6)} L{f(x+7.6)} {f(y-10)} L{f(x+10)} {f(y-7.6)} L{f(x+3.6)} {f(y-1.2)} Z", role, {"w": 1.2}),
            ("l", f(x+6.2), f(y-8.6), f(x+8.6), f(y-6.2), role, {"w": 1.0})]

def badge_plus(x, y, role="ok"):
    return [("l", x - 3, y, x + 3, y, role, {"w": 1.8}), ("l", x, y - 3, x, y + 3, role, {"w": 1.8})]

def badge_x(x, y, role="lbl", s=2.8):
    return [("l", x - s, y - s, x + s, y + s, role, {"w": 1.8}), ("l", x + s, y - s, x - s, y + s, role, {"w": 1.8})]

def check(x, y, role="ok"):
    return [("p", f"M{x-3.5} {y} L{x-1} {y+2.6} L{x+3.8} {y-3}", role, {"w": 1.9})]

def disk(x, y, role="new"):
    """A small floppy 8x8 with its top-left at (x, y)."""
    return [("p", f"M{x} {y} L{x+6} {y} L{x+8} {y+2} L{x+8} {y+8} L{x} {y+8} Z", role, {"w": 1.2}),
            ("r", x + 2, y, 3.5, 2.6, role, {"w": 1.0}), ("r", x + 1.8, y + 4.6, 4.4, 3.4, role, {"w": 1.0})]

def undo_arrow(x, y, s=1.0, role="new", flip=False):
    # a hook arrow; flip for redo
    if not flip:
        return [("p", f"M{x+10*s} {y+9*s} A5 5 0 0 0 {x+5*s} {y+2*s} L{x+1*s} {y+2*s}", role, {"w": 1.6})] + arrowhead(x + 0.5 * s, y + 2 * s, 180, role, 3.4)
    return [("p", f"M{x} {y+9*s} A5 5 0 0 1 {x+5*s} {y+2*s} L{x+9*s} {y+2*s}", role, {"w": 1.6})] + arrowhead(x + 9.5 * s, y + 2 * s, 0, role, 3.4)

def dimline(x1, y1, x2, y2, role="new", head=2.8):
    a = math.degrees(math.atan2(y2 - y1, x2 - x1))
    return [("l", x1, y1, x2, y2, role, {"w": 1.2})] + arrowhead(x2, y2, a, role, head) + arrowhead(x1, y1, a + 180, role, head)

def points_cloud(role="ink"):
    return node(4, 18) + node(8, 9) + node(14, 14) + node(19, 5)

def gear(cx, cy, r=6.5, role="ink"):
    out = [("c", cx, cy, r * 0.55, role, {"w": 1.5}), ("c", cx, cy, r * 0.2, role, {"w": 1.2})]
    for i in range(8):
        a = i * math.pi / 4
        out.append(("l", round(cx + r * 0.62 * math.cos(a), 2), round(cy + r * 0.62 * math.sin(a), 2),
                    round(cx + r * math.cos(a), 2), round(cy + r * math.sin(a), 2), role, {"w": 2.2}))
    return out

def sheet(x=3.5, y=2, w=13, h=18, role="ink"):
    return [("p", f"M{x} {y} L{x+w-4} {y} L{x+w} {y+4} L{x+w} {y+h} L{x} {y+h} Z", role, {"w": 1.3}),
            ("p", f"M{x+w-4} {y} L{x+w-4} {y+4} L{x+w} {y+4}", role, {"w": 1.0})]

def textA(x, y, size=12, role="lbl"):
    return [("t", x, y, "A", role, size, "middle")]

# =====================================================================================
# Standard / Draw / Survey
# =====================================================================================
@icon("Standard", "draft", "Draft FD-Pro Job")
def _():
    return sheet(2.5, 2, 15, 20) + [("p", "M6 16 L8 8 L14 7 L15 15 Z", "new", {"w": 1.5})] + node(6, 16, "lbl", 1.4) + node(8, 8, "lbl", 1.4) + node(14, 7, "lbl", 1.4) + node(15, 15, "lbl", 1.4) + \
           [("r", 11, 18, 11, 4.5, "ghost", {"w": 1.0}), ("l", 11, 20.3, 22, 20.3, "ghost", {"w": 0.8})]

@icon("Standard", "new", "New from Template")
def _():
    return sheet(3, 2, 14, 19) + badge_plus(18, 17)

@icon("Standard", "open", "Open")
def _():
    return [("p", "M2 19 L2 5 L8 5 L10 7.5 L19 7.5 L19 10", "ink", {"w": 1.4}),
            ("p", "M2 19 L6 10.5 L22.5 10.5 L18.5 19 Z", "new", {"w": 1.5, "fill": "newfill"})]

@icon("Standard", "save", "Save")
def _():
    return [("p", "M3 3 L17 3 L21 7 L21 21 L3 21 Z", "new", {"w": 1.5}),
            ("r", 7, 3, 8, 5.5, "new", {"w": 1.2}), ("r", 6.5, 13, 11, 8, "ink", {"w": 1.2}), ("l", 8.5, 16, 15.5, 16, "ghost", {"w": 0.9}), ("l", 8.5, 18.5, 15.5, 18.5, "ghost", {"w": 0.9})]

@icon("Standard", "print", "Print / Plot")
def _():
    return [("r", 6.5, 2.5, 11, 6, "ink", {"w": 1.2}),
            ("p", "M6.5 16 L3 16 L3 8.5 L21 8.5 L21 16 L17.5 16", "ink", {"w": 1.5}),
            ("r", 6.5, 13, 11, 8.5, "new", {"w": 1.3}), ("l", 8.5, 16.5, 15.5, 16.5, "ghost", {"w": 0.9}), ("l", 8.5, 19, 13.5, 19, "ghost", {"w": 0.9}),
            ("c", 18, 11.3, 0.8, "ok", {"fill": True, "w": 0.5})]

@icon("Standard", "undo", "Undo")
def _():
    return [("p", "M20 19 A7.5 7.5 0 0 0 12.5 8 L5 8", "new", {"w": 1.9})] + arrowhead(3.5, 8, 180, "new", 4.4)

@icon("Standard", "redo", "Redo")
def _():
    return [("p", "M4 19 A7.5 7.5 0 0 1 11.5 8 L19 8", "new", {"w": 1.9})] + arrowhead(20.5, 8, 0, "new", 4.4)

@icon("Draw", "line", "Line (bearing & distance)")
def _():
    return [("l", 4.5, 19.5, 19.5, 4.5, "new", {"w": 1.8})] + node(4.5, 19.5, "ink", 1.8) + node(19.5, 4.5, "ink", 1.8) + \
           [("t", 16.5, 19, "N45°", "lbl", 5.2, "middle")]

@icon("Draw", "arc", "Arc (3 points)")
def _():
    return [("p", "M3 19 A12 12 0 0 1 21 19", "new", {"w": 1.8})] + node(3, 19, "ink", 1.8) + node(12, 10.2, "ink", 1.8) + node(21, 19, "ink", 1.8)

@icon("Draw", "text", "Text")
def _():
    return textA(11, 18, 17, "lbl") + [("l", 3, 20.5, 21, 20.5, "ghost", {"w": 1.0, "dash": True})]

@icon("Draw", "leader", "Leader")
def _():
    return [("l", 3.5, 20, 13, 9, "ink", {"w": 1.5}), ("l", 13, 9, 16, 9, "ink", {"w": 1.5})] + arrowhead(3.5, 20, 130, "ink", 4.2) + \
           [("l", 16.5, 6, 22.5, 6, "lbl", {"w": 1.8}), ("l", 16.5, 9.5, 22.5, 9.5, "lbl", {"w": 1.8}), ("l", 16.5, 13, 20.5, 13, "lbl", {"w": 1.8})]

@icon("Survey", "inverse", "Inverse")
def _():
    return node(4, 19, "snap", 2.2) + node(20, 5, "snap", 2.2) + [("l", 5.6, 17.4, 18.4, 6.6, "new", {"w": 1.3, "dash": True}),
            ("t", 7.5, 8.5, "45°", "lbl", 5.6, "middle"), ("t", 17, 18.5, "12.5", "new", 5.6, "middle")]

@icon("Survey", "area", "Area")
def _():
    return [("p", "M3 18 L6 5 L19 3.5 L21 16 Z", "new", {"w": 1.5, "fill": "newfill"}), ("t", 12.5, 14, "m²", "lbl", 7.5, "middle")]

@icon("Survey", "id", "ID Point (N, E)")
def _():
    return [("l", 12, 2, 12, 22, "ghost", {"w": 1}), ("l", 2, 12, 22, 12, "ghost", {"w": 1})] + node(12, 12, "snap", 2.8) + \
           [("t", 5.2, 7.5, "N", "lbl", 6.5, "middle"), ("t", 19, 20.5, "E", "new", 6.5, "middle")]

@icon("Survey", "label", "Label Selection")
def _():
    return [("p", "M2 20 L10 7 L22 12", "ink", {"w": 1.6}), ("t", 0, 0, "45°", "lbl", 5, "middle", {"rot": -58, "at": (4.6, 11.2)}),
            ("t", 0, 0, "12.5", "new", 5, "middle", {"rot": 22.6, "at": (16.5, 7.4)})]

@icon("Survey", "flip", "Flip Label to Other Side")
def _():
    return [("l", 2, 12, 22, 12, "ink", {"w": 1.6}), ("t", 7, 9, "45°", "ghost", 5.6, "middle"), ("t", 7, 20, "45°", "lbl", 5.6, "middle"),
            ("p", "M16 7 A5 5 0 0 1 16 17", "new", {"w": 1.3})] + arrowhead(16, 17, 170, "new", 3)

# =====================================================================================
# View extras and snaps
# =====================================================================================
@icon("View", "regen", "Regen")
def _():
    return [("p", "M19.5 9 A8 8 0 0 0 5 7", "new", {"w": 1.7}), ("p", "M4.5 15 A8 8 0 0 0 19 17", "new", {"w": 1.7})] + \
           arrowhead(5, 7, 150, "new", 3.6) + arrowhead(19, 17, -30, "new", 3.6) + [("r", 9, 9, 6, 6, "ink", {"w": 1.4})]

@icon("View", "zoomprev", "Zoom Previous")
def _():
    return magnifier(10, 10, 7) + [("p", "M13 12.5 A3.5 3.5 0 0 0 10 6.5 L7 6.5", "ink", {"w": 1.4})] + arrowhead(6, 6.5, 180, "ink", 2.8)

def north_arrow(cx, cy, ang, role="new", L=8.5):
    import math as m
    a = m.radians(ang)
    u = (m.sin(a), -m.cos(a))           # pointing "up" on screen, turned clockwise by ang
    n = (-u[1], u[0])
    tip = (cx + u[0] * L, cy + u[1] * L); tail = (cx - u[0] * L * 0.8, cy - u[1] * L * 0.8)
    f = lambda v: round(v, 2)
    left = (cx - n[0] * 3.2 - u[0] * 1.5, cy - n[1] * 3.2 - u[1] * 1.5)
    return [("p", f"M{f(tip[0])} {f(tip[1])} L{f(left[0])} {f(left[1])} L{f(cx)} {f(cy)} Z", role, {"fill": True, "w": 1.0}),
            ("p", f"M{f(tip[0])} {f(tip[1])} L{f(cx + n[0] * 3.2 - u[0] * 1.5)} {f(cy + n[1] * 3.2 - u[1] * 1.5)} L{f(cx)} {f(cy)} Z", role, {"w": 1.0}),
            ("l", f(cx), f(cy), f(tail[0]), f(tail[1]), role, {"w": 1.4})]

@icon("View", "surveyor_view", "Surveyor View")
def _():
    return north_arrow(10, 12, 35, "new") + [("t", 0, 0, "N", "lbl", 6.5, "middle", {"rot": 35, "at": (17.2, 5.2)}),
            ("p", "M4 21 A10 10 0 0 0 16 21.5", "snap", {"w": 1.4})] + arrowhead(16, 21.5, -20, "snap", 3)

@icon("View", "world_view", "World View (North Up)")
def _():
    return north_arrow(12, 13, 0, "new") + [("t", 12, 5.5, "N", "lbl", 6.5, "middle"), ("l", 3, 21.5, 21, 21.5, "ghost", {"w": 1.1})]

@icon("View", "return_sv", "Return to Surveyor View")
def _():
    return north_arrow(13, 11, 35, "ghost") + [("p", "M21 20 A9 9 0 0 1 6 18", "new", {"w": 1.6})] + arrowhead(6, 18, 200, "new", 3.4)

@icon("View", "zoomout", "Zoom Out")
def _():
    return magnifier(10, 10, 7) + [("l", 6.5, 10, 13.5, 10, "ink", {"w": 1.7})]

@icon("Snaps", "snap_quad", "Quadrant")
def _():
    return [("c", 12, 13, 8.5, "ink", {"w": 1.4}), ("p", "M12 1 L16 4.5 L12 8 L8 4.5 Z", "snap", {"w": 1.8})]

@icon("Snaps", "snap_near", "Nearest")
def _():
    return [("l", 2, 19, 22, 7, "ink", {"w": 1.4}), ("p", "M8 7 L16 7 L8 19 L16 19 Z", "snap", {"w": 1.7})]

@icon("Snaps", "snap_none", "Snaps Off")
def _():
    return [("r", 5, 5, 14, 14, "ghost", {"w": 1.3, "dash": True})] + badge_x(12, 12, "lbl", 5)

# =====================================================================================
# MS Ties (rest)
# =====================================================================================
@icon("FD Ties", "house_tie_plain", "Auto House Tie")
def _():
    return [("l", 2, 3, 2, 21, "ink", {"w": 1.6}), ("r", 10.5, 7.5, 11, 9, "new", {"w": 1.6}),
            ("l", 2, 12, 10.5, 12, "lbl", {"w": 1.1, "dash": True}), ("t", 6.3, 20.5, "3.2", "lbl", 5.4, "middle")]

@icon("FD Ties", "mhouse_tie_a", "Manual House Tie with Arrows")
def _():
    return [("l", 2, 3, 2, 21, "ink", {"w": 1.6}), ("r", 10.5, 7.5, 11, 9, "new", {"w": 1.6})] + arrow(7, 12, 3, 12, "lbl", 2.8, 1.1) + arrow(6, 12, 10, 12, "lbl", 2.8, 1.1) + \
           [("l", 10.5, 19, 10.5, 23, "snap", {"w": 1.4}), ("l", 8.5, 21, 12.5, 21, "snap", {"w": 1.4})]

@icon("FD Ties", "mhouse_tie", "Manual House Tie")
def _():
    return [("l", 2, 3, 2, 21, "ink", {"w": 1.6}), ("r", 10.5, 7.5, 11, 9, "new", {"w": 1.6}),
            ("l", 2, 12, 10.5, 12, "lbl", {"w": 1.1, "dash": True}),
            ("l", 10.5, 19, 10.5, 23, "snap", {"w": 1.4}), ("l", 8.5, 21, 12.5, 21, "snap", {"w": 1.4})]

@icon("FD Ties", "leader_scale", "Leader Arrow Size")
def _():
    return arrowhead(9, 12, 180, "lbl", 7.5) + [("l", 9, 12, 21, 12, "ink", {"w": 1.4})] + dimline(3, 20.5, 9.5, 20.5, "new", 2.2) + [("l", 3, 17.5, 3, 22.5, "ghost", {"w": 0.9}), ("l", 9.5, 17.5, 9.5, 22.5, "ghost", {"w": 0.9})]

@icon("FD Ties", "straight_leader", "Straight Leader")
def _():
    return [("l", 3.5, 20, 19, 5, "ink", {"w": 1.6})] + arrowhead(3.5, 20, 136, "lbl", 4.6) + [("l", 19, 5, 22.5, 5, "ink", {"w": 1.6})]

@icon("FD Ties", "qpost1", "Quick Posts - Set 1")
def _():
    return [("r", 6, 6, 12, 12, "ink", {"w": 1.6}), ("c", 12, 12, 2.2, "lbl", {"fill": True, "w": 1}), ("t", 20.5, 23, "1", "new", 6.5, "middle")]

@icon("FD Ties", "qpost2", "Quick Posts - Set 2")
def _():
    return [("c", 12, 12, 6.5, "ink", {"w": 1.6}), ("l", 12, 3, 12, 21, "lbl", {"w": 1.3}), ("l", 3, 12, 21, 12, "lbl", {"w": 1.3}), ("t", 20.5, 23, "2", "new", 6.5, "middle")]

@icon("FD Ties", "block_line", "Draw Line of Blocks")
def _():
    out = [("l", 2, 19, 22, 5, "ghost", {"w": 1, "dash": True})]
    for x, y in [(4, 17.6), (10, 13.4), (16, 9.2)]:
        out.append(("r", x - 2.2, y - 2.2, 4.4, 4.4, "new", {"w": 1.4}))
    return out + node(21, 5.7, "ink", 1.3)

@icon("FD Ties", "curve_table", "Add Curves to Table")
def _():
    return table() + [("t", 5.2, 13, "C1", "lbl", 4.6, "middle"), ("p", "M10 14 A7 7 0 0 1 20 14", "new", {"w": 1.3})]

@icon("FD Ties", "tie_table", "Add Multi-ties to Table")
def _():
    return table() + [("t", 5.2, 13, "T1", "lbl", 4.6, "middle")] + node(10.5, 18, "ink", 1.1) + \
           [("l", 10.5, 18, 20, 11, "new", {"w": 1.1}), ("l", 10.5, 18, 20, 17, "new", {"w": 1.1})]

# =====================================================================================
# MS Text Edit (rest)
# =====================================================================================
def leroy(n, label):
    @icon("FD Text Edit", "leroy" + n, "Leroy " + label)
    def _():
        return [("t", 6.5, 16, "L", "lbl", 13, "middle"), ("t", 17.2, 21, label, "ink", 7.2 if len(label) < 3 else 6.2, "middle"),
                ("l", 11.5, 22.5, 23, 22.5, "ghost", {"w": 0.9})]
for n, label in [("050", "50"), ("060", "60"), ("080", "80"), ("100", "100"), ("120", "120"), ("140", "140"), ("175", "175"), ("200", "200"), ("240", "240")]:
    leroy(n, label)

@icon("FD Text Edit", "text_style", "Fonts / Text Style")
def _():
    return [("t", 8, 17, "A", "lbl", 15, "middle"), ("t", 18, 17, "a", "new", 11, "middle"), ("l", 2, 20.5, 22, 20.5, "ghost", {"w": 1})]

@icon("FD Text Edit", "arrows_pts", "Arrows Between Points")
def _():
    return node(3, 18, "snap", 1.8) + node(21, 6, "snap", 1.8) + dimline(5.3, 16.5, 18.7, 7.5, "lbl", 3.6)

@icon("FD Text Edit", "scale_one", "Scale One Text Size to Another")
def _():
    return [("t", 6, 17, "A", "ghost", 9, "middle"), ("t", 17, 19, "A", "lbl", 16, "middle")] + arrow(8, 7, 12, 7, "new", 2.4, 1.1)

@icon("FD Text Edit", "scale_factor", "Scale Text by Factor")
def _():
    return [("t", 9, 19, "A", "lbl", 17, "middle"), ("t", 19.5, 9.5, "×2", "new", 7, "middle")]

@icon("FD Text Edit", "slide_text", "Slide Text Along Axis")
def _():
    return [("t", 0, 0, "AB", "ghost", 7.5, "middle", {"rot": -30, "at": (6.8, 12.2)}), ("t", 0, 0, "AB", "lbl", 7.5, "middle", {"rot": -30, "at": (15.5, 7.2)})] + \
           arrow(6, 20, 20, 12, "new", 3, 1.2)

@icon("FD Text Edit", "text_edit", "Edit Text")
def _():
    return [("t", 8, 15, "A", "lbl", 14, "middle"), ("l", 2, 19, 13, 19, "ghost", {"w": 1})] + pencil(12, 22, "snap")

# =====================================================================================
# MS Layer / Layer Tools
# =====================================================================================
@icon("FD Layer", "lay_copy", "Copy to Layer of Object")
def _():
    return small_layers() + [("r", 14, 13.5, 8, 8, "new", {"w": 1.4})] + badge_plus(18, 17.5)

@icon("FD Layer", "lay_erase", "Erase Layer")
def _():
    return small_layers() + badge_x(18.5, 18, "lbl", 3.4)

@icon("FD Layer", "lay_thaw", "Thaw Layers")
def _():
    out = small_layers() + [("c", 18, 18, 2.6, "snap", {"w": 1.5})]
    for i in range(8):
        a = i * math.pi / 4
        out.append(("l", round(18 + 4 * math.cos(a), 2), round(18 + 4 * math.sin(a), 2), round(18 + 5.6 * math.cos(a), 2), round(18 + 5.6 * math.sin(a), 2), "snap", {"w": 1.3}))
    return out

@icon("FD Layer", "lay_move", "Move to Layer of Object")
def _():
    return small_layers() + arrow(12, 19.5, 22, 19.5, "new", 3.4, 1.7)

@icon("FD Layer", "lay_set", "Set Current Layer to Object's")
def _():
    return small_layers() + check(18.5, 18.5, "ok")

@icon("FD Layer", "lay_what", "What Layer?")
def _():
    return small_layers() + [("t", 18.5, 23, "?", "snap", 11, "middle")]

@icon("FD Layer", "lay_uniso", "Layer Unisolate")
def _():
    return [("p", "M12 3 L22 8 L12 13 L2 8 Z", "new", {"w": 1.4}), ("p", "M2 12 L12 17 L22 12", "ink", {"w": 1.3}), ("p", "M2 16 L12 21 L22 16", "ink", {"w": 1.3})]

@icon("FD Layer", "lay_group", "Layer Groups")
def _():
    return small_layers() + [("p", "M14 12 L12.5 12 L12.5 23 L14 23", "snap", {"w": 1.3}), ("p", "M21 12 L22.5 12 L22.5 23 L21 23", "snap", {"w": 1.3}),
                             ("l", 15.5, 15, 20, 15, "snap", {"w": 1.2}), ("l", 15.5, 18, 20, 18, "snap", {"w": 1.2}), ("l", 15.5, 21, 20, 21, "snap", {"w": 1.2})]

@icon("FD Layer", "lay_state", "Layer States")
def _():
    return small_layers() + disk(14, 14.5, "new")

@icon("Layer Tools", "lay_explore", "Layers Panel")
def _():
    out = []
    for i, y in enumerate([5, 11, 17]):
        out += [("r", 2.5, y - 2, 4, 4, ["ok", "ok", "ghost"][i], {"w": 1.2, "fill": True}), ("l", 9, y, 21, y, "ink", {"w": 1.5})]
    return out

@icon("Layer Tools", "set_bylayer", "Set to ByLayer")
def _():
    return [("r", 2.5, 3, 8, 8, "lbl", {"w": 1.2, "fill": True})] + arrow(11.5, 7, 15, 11.5, "ink", 2.6, 1.2) + [("p", "M14 12.5 L22 16.5 L14 20.5 L6 16.5 Z", "new", {"w": 1.4})]

@icon("Layer Tools", "lay_cur", "Change to Current Layer")
def _():
    return small_layers() + [("r", 13.5, 13.5, 9, 9, "new", {"w": 1.4}), ("l", 16, 18, 18, 20, "ok", {"w": 1.6}), ("l", 18, 20, 21.5, 15.5, "ok", {"w": 1.6})]

def bulb(on):
    role = "snap" if on else "ghost"
    out = [("p", "M8.5 15 A6.5 6.5 0 1 1 15.5 15 L15.5 18 L8.5 18 Z", role, {"w": 1.5}), ("l", 9, 20.5, 15, 20.5, "ink", {"w": 1.4}), ("l", 10, 23, 14, 23, "ink", {"w": 1.4})]
    if on:
        for a in [-150, -110, -70, -30]:
            r = math.radians(a)
            out.append(("l", round(12 + 8.4 * math.cos(r), 2), round(9.5 + 8.4 * math.sin(r), 2), round(12 + 10.6 * math.cos(r), 2), round(9.5 + 10.6 * math.sin(r), 2), "snap", {"w": 1.2}))
    else:
        out += [("l", 3, 3, 21, 21, "lbl", {"w": 1.6})]
    return out

@icon("Layer Tools", "lay_off", "Layer Off")
def _():
    return bulb(False)

@icon("Layer Tools", "lay_on", "All Layers On")
def _():
    return bulb(True)

def padlock(open_):
    body = [("r", 5, 11, 14, 11, "snap", {"w": 1.6, "fill": None}), ("c", 12, 16, 1.3, "snap", {"fill": True, "w": 0.8}), ("l", 12, 16.5, 12, 19, "snap", {"w": 1.4})]
    shackle = ("p", "M8 11 L8 7 A4 4 0 0 1 16 7 L16 11", "ink", {"w": 1.8}) if not open_ else ("p", "M8 11 L8 7 A4 4 0 0 1 16 7 L16 8", "ink", {"w": 1.8})
    return [shackle] + body

@icon("Layer Tools", "lay_lock", "Layer Lock")
def _():
    return padlock(False)

@icon("Layer Tools", "lay_unlock", "Layer Unlock")
def _():
    return [("p", "M8 11 L8 6 A4 4 0 0 1 16 6", "ink", {"w": 1.8})] + [("r", 5, 11, 14, 11, "ok", {"w": 1.6}), ("c", 12, 16, 1.3, "ok", {"fill": True, "w": 0.8}), ("l", 12, 16.5, 12, 19, "ok", {"w": 1.4})]

@icon("Layer Tools", "lay_fade", "Locked Layer Fade")
def _():
    return [("p", "M8 11 L8 7 A4 4 0 0 1 16 7 L16 11", "ghost", {"w": 1.6}), ("r", 5, 11, 14, 11, "ghost", {"w": 1.5, "dash": True}), ("c", 12, 16, 1.3, "ghost", {"fill": True, "w": 0.8})]

@icon("Layer Tools", "lay_state_save", "Save Layer State")
def _():
    return small_layers() + disk(14, 14.5, "ok")

@icon("Layer Tools", "lay_prev", "Layer Previous")
def _():
    return small_layers() + [("p", "M22 22 A4.5 4.5 0 0 0 17.5 15 L14 15", "new", {"w": 1.6})] + arrowhead(13, 15, 180, "new", 3.2)

# =====================================================================================
# Dimensioning (rest)
# =====================================================================================
def ext(x1, y1, x2, y2):
    return [("l", x1, y1, x2, y2, "ghost", {"w": 1.0})]

@icon("Dimensioning", "qdim", "Quick Dimension")
def _():
    return [("p", "M2 21 L2 16 L9 16 L9 12 L16 12 L16 16 L22 16", "ink", {"w": 1.4})] + ext(2, 15, 2, 5) + ext(9, 11, 9, 5) + ext(16, 11, 16, 5) + ext(22, 15, 22, 5) + \
           dimline(2, 7, 9, 7, "new", 2.3) + dimline(9, 7, 16, 7, "new", 2.3) + dimline(16, 7, 22, 7, "new", 2.3) + [("p", "M12 1 L10.5 4 L13 4 L11.5 7", "snap", {"w": 1.2})]

@icon("Dimensioning", "dim_linear", "Linear Dimension")
def _():
    return [("l", 4, 20, 20, 13, "ink", {"w": 1.4})] + ext(4, 19, 4, 5) + ext(20, 12, 20, 5) + dimline(4, 7, 20, 7, "new", 3) + [("t", 12, 5.2, DST, "lbl", 5.4, "middle")]

@icon("Dimensioning", "dim_rotated", "Rotated Dimension")
def _():
    return [("l", 3, 20, 21, 17, "ink", {"w": 1.4})] + ext(3, 19, 5.2, 7) + ext(21, 16, 22.2, 10) + dimline(4.8, 9, 21.8, 12, "new", 3) + \
           [("p", "M6 14 A4 4 0 0 1 9.2 13.2", "snap", {"w": 1.1})]

@icon("Dimensioning", "dim_arc", "Arc Length Dimension")
def _():
    return [("p", "M3 21 A11 11 0 0 1 21 21", "ink", {"w": 1.4}), ("p", "M3.5 14.5 A14 14 0 0 1 20.5 14.5", "new", {"w": 1.2})] + \
           arrowhead(3.5, 14.5, 125, "new", 2.8) + arrowhead(20.5, 14.5, 55, "new", 2.8) + [("p", "M9 5 A4 4 0 0 1 15 5", "lbl", {"w": 1.2})]

@icon("Dimensioning", "dim_baseline", "Baseline Dimension")
def _():
    return ext(3, 22, 3, 4) + ext(12, 22, 12, 12) + ext(21, 22, 21, 6) + dimline(3, 14.5, 12, 14.5, "new", 2.5) + dimline(3, 8, 21, 8, "new", 2.5) + [("l", 2, 22, 22, 22, "ink", {"w": 1.4})]

@icon("Dimensioning", "dim_continue", "Continue Dimension")
def _():
    return ext(3, 20, 3, 8) + ext(12, 20, 12, 8) + ext(21, 20, 21, 8) + dimline(3, 11, 12, 11, "new", 2.5) + dimline(12, 11, 21, 11, "new", 2.5) + [("l", 2, 20, 22, 20, "ink", {"w": 1.4})]

@icon("Dimensioning", "dim_ordinate", "Ordinate Dimension")
def _():
    return [("l", 3, 3, 3, 21, "ink", {"w": 1.3}), ("l", 3, 21, 21, 21, "ink", {"w": 1.3}), ("p", "M3 12 L10 12 L13 7 L21 7", "new", {"w": 1.2}), ("t", 17, 5.5, DST, "lbl", 5.2, "middle")]

@icon("Dimensioning", "tolerance", "Tolerance")
def _():
    return [("r", 2.5, 7, 19, 10, "ink", {"w": 1.3}), ("l", 9, 7, 9, 17, "ink", {"w": 1.1}), ("c", 5.7, 12, 2, "new", {"w": 1.2}), ("t", 15, 14.5, "0.1", "lbl", 6, "middle")]

@icon("Dimensioning", "center_mark", "Center Mark")
def _():
    return [("c", 12, 12, 8, "ink", {"w": 1.4}), ("l", 9, 12, 15, 12, "new", {"w": 1.4}), ("l", 12, 9, 12, 15, "new", {"w": 1.4}),
            ("l", 1.5, 12, 3.5, 12, "new", {"w": 1.4}), ("l", 20.5, 12, 22.5, 12, "new", {"w": 1.4}), ("l", 12, 1.5, 12, 3.5, "new", {"w": 1.4}), ("l", 12, 20.5, 12, 22.5, "new", {"w": 1.4})]

@icon("Dimensioning", "center_line", "Center Line")
def _():
    return [("l", 5, 2, 5, 22, "ink", {"w": 1.5}), ("l", 19, 2, 19, 22, "ink", {"w": 1.5}),
            ("l", 12, 1.5, 12, 6, "new", {"w": 1.3}), ("l", 12, 8, 12, 9.5, "new", {"w": 1.3}), ("l", 12, 11.5, 12, 16, "new", {"w": 1.3}), ("l", 12, 18, 12, 19.5, "new", {"w": 1.3}), ("l", 12, 21.5, 12, 22.5, "new", {"w": 1.3})]

@icon("Dimensioning", "oblique", "Make Oblique")
def _():
    return [("l", 3, 21, 18, 21, "ink", {"w": 1.4}), ("l", 3, 20, 7, 4, "ghost", {"w": 1}), ("l", 18, 20, 22, 4, "ghost", {"w": 1})] + dimline(5.7, 9.3, 20.7, 9.3, "new", 2.8)

@icon("Dimensioning", "dim_text_edit", "Edit Dimension Text")
def _():
    return ext(3, 22, 3, 12) + ext(21, 22, 21, 12) + dimline(3, 15, 21, 15, "new", 2.6) + [("t", 9.5, 12, "<>", "lbl", 6.5, "middle")] + pencil(13, 11, "snap")

@icon("Dimensioning", "dim_text_rotate", "Rotate Dimension Text")
def _():
    return ext(3, 22, 3, 14) + ext(21, 22, 21, 14) + dimline(3, 17, 21, 17, "new", 2.6) + [("t", 0, 0, DST, "lbl", 6, "middle", {"rot": -35, "at": (12, 10)}),
            ("p", "M17.5 3 A7 7 0 0 1 20.5 9", "ink", {"w": 1.2})] + arrowhead(20.5, 9, 70, "ink", 2.6)

@icon("Dimensioning", "dim_text_move", "Reposition Dimension Text")
def _():
    return ext(3, 22, 3, 14) + ext(21, 22, 21, 14) + dimline(3, 17, 21, 17, "new", 2.6) + [("t", 7, 11, DST, "ghost", 5.6, "middle"), ("t", 16.5, 5.5, DST, "lbl", 5.6, "middle")] + arrow(10.5, 10, 14.5, 7.5, "ink", 2.3, 1)

@icon("Dimensioning", "dim_text_home", "Restore Dimension Text Position")
def _():
    return ext(3, 22, 3, 14) + ext(21, 22, 21, 14) + dimline(3, 17, 21, 17, "new", 2.6) + [("t", 12, 14.5, DST, "lbl", 5.6, "middle"), ("t", 17, 5.5, DST, "ghost", 5.6, "middle")] + arrow(15, 7.3, 13, 10, "ink", 2.3, 1)

@icon("Dimensioning", "dim_inspect", "Inspection Dimension")
def _():
    return [("r", 2.5, 8, 19, 8.5, "new", {"w": 1.3}), ("l", 8, 8, 8, 16.5, "new", {"w": 1}), ("l", 16, 8, 16, 16.5, "new", {"w": 1}),
            ("t", 12, 14.3, DST, "lbl", 5.2, "middle"), ("c", 5.2, 12.25, 1.2, "ink", {"fill": True, "w": 0.5}), ("c", 18.8, 12.25, 1.2, "ink", {"fill": True, "w": 0.5})]

@icon("Dimensioning", "reassoc", "Reassociate Dimension")
def _():
    return [("l", 3, 20, 21, 20, "ink", {"w": 1.4})] + dimline(3, 9, 21, 9, "new", 2.6) + ext(3, 19, 3, 7) + ext(21, 19, 21, 7) + node(3, 20, "snap", 2) + node(21, 20, "snap", 2)

@icon("Dimensioning", "disassoc", "Disassociate Dimension")
def _():
    return [("l", 3, 20, 21, 20, "ink", {"w": 1.4})] + dimline(3, 9, 21, 9, "new", 2.6) + ext(3, 16, 3, 7) + ext(21, 16, 21, 7) + badge_x(12, 16.5, "lbl", 2.4)

@icon("Dimensioning", "dim_jog", "Dimension Jog Line")
def _():
    return ext(3, 22, 3, 8) + ext(21, 22, 21, 8) + [("p", "M3 12 L10 12 L12 9 L12.5 15 L14.5 12 L21 12", "new", {"w": 1.3})] + arrowhead(3, 12, 180, "new", 2.6) + arrowhead(21, 12, 0, "new", 2.6)

@icon("Dimensioning", "dim_space", "Adjust Dimension Spacing")
def _():
    return ext(3, 22, 3, 3) + ext(21, 22, 21, 3) + dimline(3, 7, 21, 7, "new", 2.3) + dimline(3, 12.5, 21, 12.5, "new", 2.3) + dimline(3, 18, 21, 18, "new", 2.3) + \
           [("l", 12, 8.5, 12, 11, "snap", {"w": 1.1}), ("l", 12, 14, 12, 16.5, "snap", {"w": 1.1})]

@icon("Dimensioning", "dim_break", "Dimension Break")
def _():
    return ext(3, 22, 3, 8) + ext(21, 22, 21, 8) + [("l", 3, 12, 9, 12, "new", {"w": 1.2}), ("l", 15, 12, 21, 12, "new", {"w": 1.2}), ("l", 12, 3, 12, 22, "ink", {"w": 1.5})] + \
           arrowhead(3, 12, 180, "new", 2.6) + arrowhead(21, 12, 0, "new", 2.6)

@icon("Dimensioning", "dimstyle", "Dimension Styles")
def _():
    return dimline(2.5, 6, 21.5, 6, "new", 2.6) + ext(2.5, 3, 2.5, 9) + ext(21.5, 3, 21.5, 9) + [("r", 3, 12, 18, 10, "ink", {"w": 1.2}), ("t", 12, 20, "Std", "lbl", 6.5, "middle")]

@icon("Dimensioning", "dimstyle_save", "Save Dimension Style")
def _():
    return dimline(2.5, 6, 21.5, 6, "new", 2.6) + ext(2.5, 3, 2.5, 9) + ext(21.5, 3, 21.5, 9) + disk(8, 12.5, "ok")

@icon("Dimensioning", "dimstyle_restore", "Restore Dimension Style")
def _():
    return dimline(2.5, 6, 21.5, 6, "new", 2.6) + ext(2.5, 3, 2.5, 9) + ext(21.5, 3, 21.5, 9) + [("p", "M19 21 A5 5 0 0 0 14 13 L8 13", "snap", {"w": 1.6})] + arrowhead(7, 13, 180, "snap", 3.2)

@icon("Dimensioning", "dim_status", "Dimension Variable Status")
def _():
    return dimline(2.5, 6, 21.5, 6, "new", 2.6) + ext(2.5, 3, 2.5, 9) + ext(21.5, 3, 21.5, 9) + [("l", 4, 13, 20, 13, "ink", {"w": 1.3}), ("l", 4, 17, 17, 17, "ink", {"w": 1.3}), ("l", 4, 21, 19, 21, "ink", {"w": 1.3})]

@icon("Dimensioning", "dim_update", "Update Dimensions to Style")
def _():
    return ext(3, 22, 3, 13) + ext(21, 22, 21, 13) + dimline(3, 16, 21, 16, "new", 2.6) + [("p", "M17 9 A6 6 0 1 0 12.5 11", "ok", {"w": 1.5})] + arrowhead(17, 9, 60, "ok", 3)

# =====================================================================================
# Text
# =====================================================================================
@icon("Text", "mtext", "Multiline Text")
def _():
    return [("r", 2.5, 2.5, 19, 19, "ghost", {"w": 1, "dash": True}), ("t", 7.5, 10, "A", "lbl", 9, "middle"),
            ("l", 12, 7, 19, 7, "ink", {"w": 1.4}), ("l", 12, 10, 19, 10, "ink", {"w": 1.4}), ("l", 5, 14, 19, 14, "ink", {"w": 1.4}), ("l", 5, 17.5, 15, 17.5, "ink", {"w": 1.4})]

@icon("Text", "txt2mtxt", "Text to Multiline Text")
def _():
    return [("l", 2, 5, 9, 5, "lbl", {"w": 1.6}), ("l", 2, 9, 9, 9, "lbl", {"w": 1.6}), ("l", 2, 13, 7, 13, "lbl", {"w": 1.6})] + arrow(10, 9, 13.5, 9, "ink", 2.4, 1.2) + \
           [("r", 14.5, 3, 8, 12, "new", {"w": 1.3}), ("l", 16, 6, 21, 6, "lbl", {"w": 1.2}), ("l", 16, 9, 21, 9, "lbl", {"w": 1.2}), ("l", 16, 12, 19.5, 12, "lbl", {"w": 1.2})]

@icon("Text", "explore_styles", "Text Styles")
def _():
    return [("t", 7, 11, "A", "lbl", 11, "middle"), ("t", 7, 21.5, "A", "new", 9, "middle"),
            ("l", 13, 5.5, 22, 5.5, "ink", {"w": 1.4}), ("l", 13, 9, 20, 9, "ghost", {"w": 1.1}), ("l", 13, 15.5, 22, 15.5, "ink", {"w": 1.4}), ("l", 13, 19, 20, 19, "ghost", {"w": 1.1})]

# =====================================================================================
# MS Main Control / Calcs / Coordinate
# =====================================================================================
@icon("FD Main Control", "assistant", "Assistant")
def _():
    return [("p", "M3 4 L21 4 L21 16 L11 16 L6 21 L6 16 L3 16 Z", "new", {"w": 1.5}), ("t", 12, 14, "?", "snap", 11, "middle")]

@icon("FD Main Control", "project", "Project Manager")
def _():
    return [("p", "M2 20 L2 5 L8 5 L10 7.5 L22 7.5 L22 20 Z", "new", {"w": 1.5})] + node(8, 16, "lbl", 1.4) + node(12, 11.5, "lbl", 1.4) + node(17, 15, "lbl", 1.4) + \
           [("p", "M8 16 L12 11.5 L17 15", "ink", {"w": 1.1})]

@icon("FD Main Control", "config", "General Configuration")
def _():
    return gear(12, 12, 9.5, "ink") + [("c", 12, 12, 2, "new", {"fill": True, "w": 0.8})]

@icon("FD Main Control", "toggles", "Toggles / Preferences")
def _():
    return [("r", 2.5, 4, 19, 7, "new", {"w": 1.4}), ("c", 17.5, 7.5, 2.2, "new", {"fill": True, "w": 0.8}),
            ("r", 2.5, 13.5, 19, 7, "ghost", {"w": 1.4}), ("c", 6.5, 17, 2.2, "ghost", {"fill": True, "w": 0.8})]

@icon("FD Main Control", "hot_toggles", "Hot Toggles")
def _():
    return [("r", 2.5, 8.5, 15, 7, "new", {"w": 1.4}), ("c", 13.5, 12, 2.2, "new", {"fill": True, "w": 0.8}), ("p", "M20 2 L17 11 L21 11 L18 21", "snap", {"w": 1.5})]

@icon("FD Main Control", "info", "Line / Curve / Text Information")
def _():
    return [("l", 2, 21, 13, 13, "ghost", {"w": 1.4, "dash": True}), ("c", 16, 8, 6.5, "new", {"w": 1.6}), ("l", 16, 7.5, 16, 12, "new", {"w": 2}), ("c", 16, 4.8, 0.9, "new", {"fill": True, "w": 0.5})]

@icon("FD Main Control", "grips", "Grips On/Off")
def _():
    return [("l", 3, 20, 21, 5, "ink", {"w": 1.4}), ("r", 1, 18, 4, 4, "new", {"w": 1, "fill": True}), ("r", 10, 10.5, 4, 4, "new", {"w": 1, "fill": True}), ("r", 19, 3, 4, 4, "new", {"w": 1, "fill": True})]

@icon("FD Main Control", "cogo", "COGO (Bearing & Distance)")
def _():
    return [("p", "M3 20 L9 8 L20 12", "new", {"w": 1.6})] + node(3, 20, "ink", 1.6) + node(9, 8, "ink", 1.6) + node(20, 12, "ink", 1.6) + \
           [("p", "M7.3 11.4 A3.8 3.8 0 0 0 12.6 9.3", "lbl", {"w": 1.2}), ("t", 17, 21.5, "∠", "lbl", 8, "middle")]

@icon("FD Main Control", "automap", "AutoMap Code Library")
def _():
    return [("p", "M3 4 L10 4 A2 2 0 0 1 12 6 L12 21 A2 2 0 0 0 10 19 L3 19 Z", "ink", {"w": 1.3}), ("p", "M21 4 L14 4 A2 2 0 0 0 12 6 L12 21 A2 2 0 0 1 14 19 L21 19 Z", "ink", {"w": 1.3}),
            ("t", 7, 13, "IB", "lbl", 4.8, "middle"), ("t", 16.8, 13, "FH", "new", 4.8, "middle")]

@icon("FD Main Control", "store_points", "Store and Edit Points")
def _():
    return node(5, 6, "snap", 2.3) + [("t", 14.5, 8, "101", "lbl", 6, "middle")] + table(2.5, 11, 19, 11, 2)

@icon("FD Main Control", "coord_editor", "Active Coordinate Editor")
def _():
    return table(2, 3, 17, 15, 3) + [("t", 4.8, 12, "N", "lbl", 4.4, "middle")] + pencil(13, 22.5, "snap")

@icon("FD Main Control", "traverse_editor", "Active Traverse Editor")
def _():
    return [("p", "M3 18 L6 6 L16 4 L13 13 Z", "new", {"w": 1.4})] + node(3, 18) + node(6, 6) + node(16, 4) + node(13, 13) + pencil(13, 22.5, "snap")

@icon("FD Main Control", "rescale", "Re-scale Drawing")
def _():
    return sheet(2.5, 2, 15, 20) + [("t", 10, 14, "1:n", "lbl", 6, "middle")] + arrow(16, 19, 22, 13, "new", 3, 1.3)

@icon("FD Main Control", "add_points", "Add Points to Objects")
def _():
    return [("p", "M2 19 L9 6 L22 11", "ink", {"w": 1.4})] + node(2.5, 18.5, "snap", 2) + node(9, 6, "snap", 2) + node(21.5, 11, "snap", 2) + badge_plus(17, 19.5)

@icon("FD Main Control", "label_defaults", "Labeling Defaults")
def _():
    return [("t", 7, 11, "45°", "lbl", 6.5, "middle"), ("l", 1.5, 13.5, 13, 13.5, "ink", {"w": 1.3})] + gear(17, 17, 6, "ink")

@icon("FD Main Control", "view_log", "View Log File")
def _():
    return sheet(4, 2, 16, 20) + [("l", 7, 9, 16, 9, "ink", {"w": 1.1}), ("l", 7, 12, 17, 12, "ink", {"w": 1.1}), ("l", 7, 15, 14, 15, "ink", {"w": 1.1}), ("l", 7, 18, 16, 18, "lbl", {"w": 1.1})]

@icon("FD Main Control", "scale_z", "Scale Z Values")
def _():
    return [("t", 10, 18, "Z", "lbl", 16, "middle")] + arrow(19.5, 12, 19.5, 3, "new", 3, 1.3) + arrow(19.5, 12, 19.5, 21, "new", 3, 1.3)

@icon("FD Main Control", "calculator", "Calculator")
def _():
    out = [("r", 4, 2, 16, 20, "ink", {"w": 1.4}), ("r", 6.5, 4.5, 11, 4.5, "new", {"w": 1.1})]
    for r in range(3):
        for c in range(3):
            out.append(("r", 6.5 + c * 4, 11 + r * 3.6, 2.6, 2.2, "ghost" if (r, c) != (2, 2) else "snap", {"w": 0.8, "fill": True}))
    return out

@icon("FD Calcs", "pts_on_obj", "Compute Points on an Object")
def _():
    return [("p", "M2 19 A18 18 0 0 1 22 9", "ink", {"w": 1.4})] + node(2.5, 18.5, "snap", 1.7) + node(8.4, 13, "snap", 1.7) + node(15, 9.6, "snap", 1.7) + node(21.5, 9, "snap", 1.7)

@icon("FD Calcs", "turned_angle", "Compute Points by Turned Angle")
def _():
    return [("l", 5, 18, 21, 18, "ink", {"w": 1.4}), ("l", 5, 18, 14, 5, "new", {"w": 1.4, "dash": True})] + node(5, 18, "ink", 1.8) + node(14, 5, "snap", 2) + \
           [("p", "M12 18 A7 7 0 0 0 9 12.3", "lbl", {"w": 1.3})] + arrowhead(9, 12.3, -125, "lbl", 2.6)

@icon("FD Calcs", "sta_off", "Compute Points by Station / Offset")
def _():
    return [("l", 2, 18, 22, 18, "ink", {"w": 1.6}), ("l", 14, 15.5, 14, 20.5, "ink", {"w": 1.3}), ("l", 14, 16, 14, 7, "new", {"w": 1.2, "dash": True})] + node(14, 6, "snap", 2) + \
           [("t", 7, 13.5, "0+50", "lbl", 4.8, "middle")]

@icon("FD Calcs", "dot2dot", "Lines by Inversing (dot to dot)")
def _():
    return [("p", "M3 19 L8 7 L15 14 L21 4", "new", {"w": 1.5})] + node(3, 19, "snap", 2) + node(8, 7, "snap", 2) + node(15, 14, "snap", 2) + node(21, 4, "snap", 2)

@icon("FD Calcs", "tan_line", "Line Tangent to Curve")
def _():
    return [("c", 14, 14, 6.5, "ink", {"w": 1.4}), ("l", 2.5, 3.3, 20.1, 9.2, "new", {"w": 1.5})] + node(2.5, 3.3, "snap", 1.7) + node(16.06, 7.84, "lbl", 1.4)

@icon("FD Calcs", "connect_desc", "Connect by Description")
def _():
    return [("p", "M3 19 L9 10 L16 15 L21 5", "new", {"w": 1.3})] + node(3, 19) + node(9, 10) + node(16, 15) + node(21, 5) + \
           [("t", 4.5, 8, "FC", "lbl", 5, "middle"), ("t", 18.5, 21.5, "FC", "lbl", 5, "middle")]

@icon("FD Calcs", "bestfit_line", "Best Fit a Line")
def _():
    return [("l", 2, 19, 22, 5, "new", {"w": 1.6})] + node(4, 15, "ink", 1.3) + node(8, 17.5, "ink", 1.3) + node(11, 11, "ink", 1.3) + node(15, 12.5, "ink", 1.3) + node(18, 6, "ink", 1.3) + node(20.5, 9.5, "ink", 1.3)

@icon("FD Calcs", "curve_calc", "COGO Curve Calculations")
def _():
    return [("p", "M2 16 A12 12 0 0 1 16 4", "new", {"w": 1.6}), ("l", 14, 16, 2, 16, "ghost", {"w": 1, "dash": True}), ("l", 14, 16, 16, 4, "ghost", {"w": 1, "dash": True}),
            ("t", 8, 22.5, "R", "lbl", 6, "middle"), ("r", 15, 13, 8, 10, "ink", {"w": 1.1}), ("l", 16.5, 15.5, 21.5, 15.5, "snap", {"w": 1}), ("t", 19, 21.5, "=", "snap", 6, "middle")]

@icon("FD Calcs", "bestfit_curve", "Best Fit a Curve")
def _():
    return [("p", "M2 19 A13 13 0 0 1 22 19", "new", {"w": 1.6})] + node(3.5, 15.5, "ink", 1.3) + node(6.5, 9.5, "ink", 1.3) + node(11.5, 8, "ink", 1.3) + node(16, 7, "ink", 1.3) + node(20, 12, "ink", 1.3)

@icon("FD Calcs", "curve_solver", "Curve Problem Solver")
def _():
    return [("p", "M2 19 A13 13 0 0 1 22 19", "new", {"w": 1.6}), ("t", 12, 17.5, "?", "snap", 11, "middle")]

@icon("FD Calcs", "row", "Right of Way Design")
def _():
    return [("p", "M2 22 C8 16 8 8 14 2", "ink", {"w": 1.4}), ("p", "M10 22 C16 16 16 8 22 2", "ink", {"w": 1.4}), ("p", "M6 22 C12 16 12 8 18 2", "snap", {"w": 1.1, "dash": True})]

@icon("FD Calcs", "curve_one_tan", "Curve off One Tangent")
def _():
    return [("l", 2, 19, 11, 19, "ink", {"w": 1.7}), ("p", "M11 19 A9 9 0 0 0 20 10", "new", {"w": 1.7}), ("l", 11, 19, 11, 10, "ghost", {"w": 0.9, "dash": True}), ("l", 11, 10, 20, 10, "ghost", {"w": 0.9, "dash": True})] + node(11, 19, "snap", 1.6)

@icon("FD Calcs", "curve_two_tan", "Curve on Two Tangents")
def _():
    return [("l", 2, 20, 9, 20, "ink", {"w": 1.7}), ("l", 20, 9, 20, 2, "ink", {"w": 1.7}), ("p", "M9 20 L20 20 L20 9", "ghost", {"w": 1, "dash": True}), ("p", "M9 20 A11 11 0 0 0 20 9", "new", {"w": 1.7})]

@icon("FD Calcs", "reverse_curve", "Reverse or Compound Curve")
def _():
    return [("p", "M2 20 A9 9 0 0 1 12 12 A9 9 0 0 0 22 4", "new", {"w": 1.7})] + node(12, 12, "snap", 1.6)

@icon("FD Coordinate", "ascii_out", "Export ASCII Points")
def _():
    return sheet(9, 2, 13, 19) + [("t", 15.5, 14, "csv", "lbl", 5, "middle")] + node(4, 9, "snap", 2) + arrow(4, 12.5, 4, 19.5, "new", 3, 1.4)

@icon("FD Coordinate", "ascii_in", "Import ASCII Points")
def _():
    return sheet(2, 2, 13, 19) + [("t", 8.5, 14, "csv", "lbl", 5, "middle")] + arrow(16, 14, 21, 9, "new", 3, 1.4) + node(20.5, 17, "snap", 2)

@icon("FD Coordinate", "del_points", "Delete Points")
def _():
    return node(6, 7, "ink", 2) + node(15, 5, "ink", 2) + node(8, 17, "ink", 2) + badge_x(17.5, 17, "lbl", 4)

@icon("FD Coordinate", "list_points", "List Points")
def _():
    out = []
    for i, y in enumerate([5, 10.5, 16, 21]):
        out += node(4, y, "snap", 1.6) + [("l", 8, y, 21, y, "ink" if i < 3 else "ghost", {"w": 1.4})]
    return out

@icon("FD Coordinate", "update_db", "Update Drawing from Database")
def _():
    return [("p", "M5 6 A7 2.5 0 0 0 19 6 A7 2.5 0 0 0 5 6 L5 18 A7 2.5 0 0 0 19 18 L19 6", "ink", {"w": 1.3}), ("p", "M5 12 A7 2.5 0 0 0 19 12", "ghost", {"w": 1.0}),
            ("p", "M9 16 A4 4 0 1 0 12 9", "new", {"w": 1.5})] + arrowhead(12, 9, 0, "new", 2.8)

@icon("FD Coordinate", "renumber", "Renumber Points")
def _():
    return node(5, 12, "snap", 2) + [("t", 12, 9, "12", "ghost", 6.5, "middle"), ("t", 12, 20, "37", "lbl", 6.5, "middle")] + arrow(19, 7, 19, 17, "new", 2.8, 1.3)

@icon("FD Coordinate", "rotate_pts", "Rotate Points")
def _():
    return node(6, 18, "ink", 1.7) + node(17, 7, "snap", 1.7) + node(12, 5, "ghost", 1.5) + [("p", "M12.5 4.6 A13 13 0 0 1 16.2 6.1", "new", {"w": 1.2})] + \
           [("p", "M19 18 A13 13 0 0 0 17 9.2", "new", {"w": 1.3})] + arrowhead(17, 9.2, -110, "new", 2.6) + [("l", 6, 18, 17, 7, "ghost", {"w": 0.9, "dash": True})]

@icon("FD Coordinate", "shift_pts", "Shift Points")
def _():
    return node(5, 17, "ghost", 1.8) + node(9, 20, "ghost", 1.8) + node(15, 7, "snap", 1.8) + node(19, 10, "snap", 1.8) + arrow(7, 16, 14, 9.5, "new", 3, 1.3)

@icon("FD Coordinate", "scale_pts", "Scale Points")
def _():
    return [("r", 2.5, 12.5, 7, 7, "ghost", {"w": 1.2, "dash": True}), ("r", 2.5, 3.5, 16, 16, "new", {"w": 1.4})] + node(2.5, 19.5, "snap", 1.6) + arrow(9.5, 12.5, 16.5, 5.5, "ink", 2.8, 1.1)

@icon("FD Coordinate", "stakeout", "Stake Out Points")
def _():
    return [("l", 9, 22, 9, 3, "ink", {"w": 1.8}), ("p", "M9 3 L20 6.5 L9 10 Z", "lbl", {"w": 1.2, "fill": True}), ("l", 5, 22, 13, 22, "ink", {"w": 1.4})]

@icon("FD Coordinate", "transfer", "Transfer Points Between Jobs")
def _():
    cyl = lambda x: [("p", f"M{x} 6 A3.5 1.6 0 0 0 {x+7} 6 A3.5 1.6 0 0 0 {x} 6 L{x} 18 A3.5 1.6 0 0 0 {x+7} 18 L{x+7} 6", "ink", {"w": 1.2})]
    return cyl(1.5) + cyl(15.5) + arrow(9.5, 10, 14.5, 10, "new", 2.6, 1.3) + arrow(14.5, 15, 9.5, 15, "snap", 2.6, 1.3)

@icon("FD Coordinate", "zoom_point", "Zoom to a Point")
def _():
    return magnifier(10, 10, 7) + node(10, 10, "snap", 2) + [("t", 20, 7, "#", "lbl", 7, "middle")]

@icon("FD Coordinate", "graphic_editor", "Graphic Coordinate Editor")
def _():
    return [("l", 3, 20, 12, 11, "ink", {"w": 1.3}), ("l", 12, 11, 21, 17, "ink", {"w": 1.3}), ("r", 9.5, 8.5, 5, 5, "snap", {"w": 1.4})] + arrow(12, 11, 16, 4, "new", 2.6, 1.1)

@icon("FD Coordinate", "helmert", "Helmert Transformation")
def _():
    return [("p", "M2 20 L8 9 L12 19 Z", "ghost", {"w": 1.2, "dash": True}), ("p", "M12 13 L19 3 L22 14 Z", "new", {"w": 1.4})] + arrow(8, 16, 14, 11, "snap", 2.6, 1.1)
