#!/usr/bin/env python3
"""Regenera o CSX do mod e injeta em src/main.rs (const MOD_PATCH_CSX)."""
import pathlib, subprocess, sys

HERE = pathlib.Path(__file__).parent
MAIN = HERE.parent / "src" / "main.rs"
OPEN = 'const MOD_PATCH_CSX: &str = r#"'
CLOSE = '"#;'

subprocess.run([sys.executable, str(HERE / "gen_totorto.py")], check=True, cwd=HERE)
subprocess.run([sys.executable, str(HERE / "gen_mod.py")], check=True, cwd=HERE)

csx = (HERE / "mod_dist2.csx").read_text(encoding="utf-8")
tot = (HERE / "totorto_patch.txt").read_text(encoding="utf-8")
if "g.Import();" not in csx:
    sys.exit("gen_mod.py deveria terminar com g.Import();")
csx = csx.replace("g.Import();", tot + "\ng.Import();", 1)

src = MAIN.read_text(encoding="utf-8")
start = src.index(OPEN)
end = src.index(CLOSE, start) + len(CLOSE)
MAIN.write_text(src[:start] + OPEN + csx + CLOSE + src[end:], encoding="utf-8")
print(f"main.rs atualizado ({len(csx)} bytes de CSX)")
