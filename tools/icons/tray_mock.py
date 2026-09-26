"""Mock-up of FD-Draft's toolbar tray, from ToolbarCatalog.cs + icons.py (for review without Windows).
    python3 tools/icons/tray_mock.py out.html"""
import os, re, sys, html
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from icons import ICONS
from preview import svg, THEMES
src = open(os.path.join(HERE, "..", "..", "src", "FdDraft.View", "Toolbars", "ToolbarCatalog.cs"), encoding="utf-8").read()
body = src[src.index("private static List<ToolbarDef> Build()"):]
bars = []
for m in re.finditer(r'Bar\("([a-z_]+)", "([^"]+)", (\d), (true|false),(.*?)\)\),?\s*\n\s*(?=(?://|Bar\(|\};))', body, re.S):
    items = []
    for it in re.finditer(r'\b(B|NA|T)\("([a-z0-9_]+)", "([^"]+)"|\bSep\b|Kind = ToolButtonKind\.Custom', m.group(5)):
        if it.group(0) == "Sep": items.append(("sep",)); continue
        if it.group(0).startswith("Kind"): items.append(("combo",)); continue
        items.append((it.group(1), it.group(2), it.group(3)))
    bars.append((m.group(1), m.group(2), int(m.group(3)), items))
th = THEMES["dark"]
rows = {}
for b in bars: rows.setdefault(b[2], []).append(b)
out = ['<!doctype html><meta charset="utf-8"><title>FD-Draft Toolbars</title><style>body{margin:0;background:#1b1c1f;font-family:Segoe UI,Arial,sans-serif;color:#ddd}',
       '.tray{background:#232427;padding:1px 0;width:max-content;min-width:100%}.row{display:flex;gap:2px;margin:1px 0}.bar{display:flex;align-items:center;background:#2b2d31;border-radius:3px;padding:1px 3px 1px 0;margin-left:1px}',
       '.grip{width:6px;height:24px;margin:0 3px;background:repeating-linear-gradient(#5a5e66 0 1px,transparent 1px 3px)}.b{width:28px;height:28px;display:flex;align-items:center;justify-content:center;border-radius:3px;margin:0 1px}',
       '.b.na{opacity:.3}.b.on{background:#274d7a;box-shadow:inset 0 0 0 1px #5aa2ff}.sep{width:1px;height:20px;background:#3d4148;margin:0 3px}.combo{display:flex;align-items:center;gap:4px;font-size:12px;padding:0 4px}',
       '.combo span{background:#fff;color:#000;padding:2px 6px;width:150px;border:1px solid #999;font-size:12px}h1{font-size:14px;margin:10px 12px}.note{font-size:12px;margin:4px 12px;color:#aaa}</style>',
       '<h1>FD-Draft toolbars (dark) - as the app lays them out, 6 rows</h1><div class="tray">']
widths = {}
for r in sorted(rows):
    out.append('<div class="row">')
    for key, name, band, items in rows[r]:
        out.append(f'<div class="bar" title="{html.escape(name)}"><div class="grip"></div>')
        for it in items:
            if it[0] == "sep": out.append('<div class="sep"></div>'); continue
            if it[0] == "combo": out.append('<div class="combo">Layer: <span>PLAN-Boundary ▾</span></div>'); continue
            kind, icon, label = it
            cls = "b na" if kind == "NA" else ("b on" if kind == "T" and icon in ("snap_end", "snap_int", "snap_cen", "snap_node") else "b")
            out.append(f'<div class="{cls}" title="{html.escape(label)}">{svg(ICONS[icon]["prims"], th, 24)}</div>')
        out.append('</div>')
    out.append('</div>')
out.append('</div><p class="note">Dimmed buttons have no FD-Draft equivalent yet (hover shows why). Snap buttons show on/off state. Hover any button for its name.</p>')
open(sys.argv[1] if len(sys.argv) > 1 else "tray.html", "w", encoding="utf-8").write("".join(out))
print(len(bars), "bars")
