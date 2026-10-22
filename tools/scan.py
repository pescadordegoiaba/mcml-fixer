#!/usr/bin/env python3
"""Varredura do decompilado do My Car My Life.

Uso:
    python3 tools/scan.py [DIR]

DIR padrao: /home/gullin/projetos/mcml-decompiled/CodeEntries

Relata:
  - funcoes definidas e nunca chamadas (ignorando despacho por string)
  - globais escritos e nunca lidos / lidos e nunca escritos
  - clamps e comparacoes em global.cash
"""
import collections
import pathlib
import re
import sys

D = pathlib.Path(
    sys.argv[1] if len(sys.argv) > 1 else "/home/gullin/projetos/mcml-decompiled/CodeEntries"
)
texts = {f.name: f.read_text(encoding="utf-8", errors="ignore") for f in sorted(D.glob("*.gml"))}
print(f"arquivos: {len(texts)}")

# ---------------------------------------------------------------- funcoes
defs = {}
for name, t in texts.items():
    for m in re.finditer(r"^function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", t, re.M):
        defs.setdefault(m.group(1), name)

calls = collections.Counter()
for t in texts.values():
    for m in re.finditer(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(", t):
        calls[m.group(1)] += 1

all_text = "\n".join(texts.values())
dead, dispatched = [], []
for fn, src in defs.items():
    if calls.get(fn, 0) > 1:
        continue
    if f'"{fn}"' in all_text or f"'{fn}'" in all_text:
        dispatched.append(fn)
        continue
    dead.append((fn, src))

print(f"funcoes definidas: {len(defs)}")
print(f"funcoes sem chamada direta: {len(dead) + len(dispatched)}")
print(f"  despachadas por string (nao sao mortas): {len(dispatched)}")
print(f"  realmente sem referencia: {len(dead)}")
for fn, src in sorted(dead):
    print(f"    {fn:36s} {src}")

# ---------------------------------------------------------------- globais
writes = collections.Counter()
reads = collections.Counter()
for t in texts.values():
    for m in re.finditer(r"global\.([A-Za-z_][A-Za-z0-9_]*)\s*=[^=]", t):
        writes[m.group(1)] += 1
    for m in re.finditer(r"global\.([A-Za-z_][A-Za-z0-9_]*)", t):
        reads[m.group(1)] += 1

so_escrita = [(g, writes[g]) for g in writes if writes[g] >= 3 and reads[g] - writes[g] <= 0]
print(f"\nglobais escritos e nunca lidos: {len(so_escrita)}")
for g, n in sorted(so_escrita)[:25]:
    print(f"    {g:28s} {n} escritas, 0 leituras")

# ---------------------------------------------------------------- dinheiro
clamps = sum(len(re.findall(r"clamp\(\s*global\.cash", t)) for t in texts.values())
ops = collections.Counter()
for t in texts.values():
    for m in re.finditer(r"global\.cash\s*(>=|>|<=|<|==)", t):
        ops[m.group(1)] += 1
print(f"\n--- dinheiro ---")
print(f"clamps em global.cash: {clamps}")
print(f"comparacoes: {dict(sorted(ops.items()))}")
