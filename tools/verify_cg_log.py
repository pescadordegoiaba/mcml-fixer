#!/usr/bin/env python3
"""V3 - valida o snapshot gravado pelo jogo em cambio_save.txt.

Checa cada linha CG|v=3|... e, se existir, o bloco SAVE ... IGUAL=1
(round-trip: gravar -> SaveAllCars -> LoadAllCars -> comparar).
"""
import math
import pathlib
import sys

LOG = pathlib.Path.home() / ".config/My_Car_My_Life/cambio_save.txt"
if not LOG.exists():
    sys.exit("FALTA cambio_save.txt (rode o jogo e confirme uma alteracao)")

linhas = [l.strip() for l in LOG.read_text(errors="ignore").splitlines() if l.strip()]
snaps = [l[l.index("CG|"):] for l in linhas if "CG|" in l]
erros = []

for s in snaps:
    d = dict(p.split("=", 1) for p in s.split("|")[1:])
    n = int(d["n"])
    rel = float(d["rel"])
    r = [float(d[f"r{i}"]) for i in range(n)]
    if not (2.60 - 1e-9 <= rel <= 5.10 + 1e-9):
        erros.append(f"rel {rel} fora de 2.60-5.10")
    if any(math.isnan(x) or math.isinf(x) for x in r):
        erros.append("nan/inf nas razoes")
    if any(x <= 0 for x in r):
        erros.append("razao <= 0")
    if any(r[i] >= r[i - 1] for i in range(1, n)):
        erros.append("razoes nao decrescentes")

igual = [l for l in linhas if l.startswith("SAVE IGUAL=")]
if igual and not any(l.endswith("=1") for l in igual):
    erros.append("round-trip do save falhou (IGUAL=0)")

print("\n".join(linhas[-14:]))
print(f"V3 {'OK' if not erros else 'FALHOU'} snapshots={len(snaps)} roundtrip={len(igual)} erros={erros}")
sys.exit(0 if not erros else 1)
