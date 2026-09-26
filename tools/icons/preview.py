import html, sys
from icons import ICONS

THEMES = {
    "light": dict(bg="#eef0f3", btn="#fbfbfc", ink="#2a2d33", new="#1a66d2", lbl="#d1352b", snap="#e0820b", ghost="#8d939c", ok="#1f9d4c", newfill="#1a66d233"),
    "dark":  dict(bg="#2b2d31", btn="#35383d", ink="#e3e5e8", new="#5aa2ff", lbl="#ff6b5f", snap="#ffa733", ghost="#8b9098", ok="#4cc37a", newfill="#5aa2ff33"),
}

def svg(prims, th, size):
    out = [f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg" fill="none" stroke-linecap="round" stroke-linejoin="round">']
    for p in prims:
        k = p[0]
        if k == "t":
            _, x, y, txt, role, sz, anchor = p[:7]
            o = p[7] if len(p) > 7 else {}
            tr = ""
            if "rot" in o:
                ax, ay = o["at"]; x, y = ax, ay; tr = f' transform="rotate({o["rot"]} {ax} {ay})"'
            out.append(f'<text x="{x}" y="{y}" font-family="Segoe UI, Arial, sans-serif" font-weight="700" font-size="{sz}" text-anchor="{anchor}" fill="{th[role]}"{tr}>{html.escape(txt)}</text>')
            continue
        role, o = p[-2], p[-1]
        col = th[role]; w = o.get("w", 1.5)
        fill = o.get("fill")
        fcol = th[fill] if isinstance(fill, str) else (col if fill else "none")
        dash = ' stroke-dasharray="2 1.6"' if o.get("dash") else ""
        st = f'stroke="{col}" stroke-width="{w}" fill="{fcol}"{dash}'
        if k == "l": out.append(f'<line x1="{p[1]}" y1="{p[2]}" x2="{p[3]}" y2="{p[4]}" {st}/>')
        elif k == "p": out.append(f'<path d="{p[1]}" {st}/>')
        elif k == "c": out.append(f'<circle cx="{p[1]}" cy="{p[2]}" r="{p[3]}" {st}/>')
        elif k == "r": out.append(f'<rect x="{p[1]}" y="{p[2]}" width="{p[3]}" height="{p[4]}" {st}/>')
    out.append("</svg>")
    return "".join(out)

def main():
    bars = {}
    for key, ic in ICONS.items():
        bars.setdefault(ic["bar"], []).append((key, ic))

    def section(theme):
        th = THEMES[theme]
        parts = [f'<div class="theme" style="background:{th["bg"]};color:{th["ink"]}"><h2>{theme.title()} toolbar</h2>']
        for bar, items in bars.items():
            parts.append(f'<div class="bar"><div class="barname">{bar}</div><div class="strip" style="background:{th["btn"]}">')
            for key, ic in items:
                parts.append(f'<span class="b" title="{ic["name"]}">{svg(ic["prims"], th, 24)}</span>')
            parts.append('</div></div>')
        parts.append('</div>')
        return "".join(parts)

    def big():
        th = THEMES["dark"]
        parts = ['<div class="theme" style="background:#1f2124;color:#e3e5e8"><h2>Close-up (2×) with names</h2>']
        for bar, items in bars.items():
            parts.append(f'<h3>{bar}</h3><div class="grid">')
            for key, ic in items:
                parts.append(f'<div class="cell">{svg(ic["prims"], th, 48)}<div>{ic["name"]}</div></div>')
            parts.append('</div>')
        parts.append('</div>')
        return "".join(parts)

    page = f"""<!doctype html><html><head><meta charset="utf-8"><title>FD-Draft Icon Samples</title><style>
    body{{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#fff}}
    .theme{{padding:14px 18px}} h2{{margin:0 0 10px;font-size:15px}} h3{{font-size:13px;margin:14px 0 6px;color:#9aa0a8}}
    .bar{{display:flex;align-items:center;gap:10px;margin:6px 0}} .barname{{width:110px;font-size:12px;opacity:.8}}
    .strip{{display:flex;gap:2px;padding:3px;border-radius:4px;box-shadow:0 0 0 1px #0002}}
    .b{{width:30px;height:30px;display:flex;align-items:center;justify-content:center;border-radius:3px}}
    .grid{{display:flex;flex-wrap:wrap;gap:10px}} .cell{{width:92px;text-align:center;font-size:10.5px;padding:6px;border:1px solid #3a3d42;background:#2b2d31;border-radius:6px}}
    .key{{font-size:12px;padding:10px 18px;background:#fafafa;border-bottom:1px solid #eee}} .sw{{display:inline-block;width:10px;height:10px;border-radius:2px;margin:0 4px 0 12px;vertical-align:-1px}}
    </style></head><body>
    <div class="key"><b>Colour key:</b><span class="sw" style="background:#2a2d33"></span>existing geometry<span class="sw" style="background:#1a66d2"></span>result / distance<span class="sw" style="background:#d1352b"></span>annotation / bearing<span class="sw" style="background:#e0820b"></span>snap marker<span class="sw" style="background:#8d939c"></span>before / guide (dashed)<span class="sw" style="background:#1f9d4c"></span>create</div>
    {section("dark")}{big()}</body></html>"""
    open(sys.argv[1] if len(sys.argv) > 1 else "preview.html", "w").write(page)
    print(len(ICONS), "icons")

if __name__ == "__main__":
    main()
