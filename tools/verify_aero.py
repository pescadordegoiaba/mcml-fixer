#!/usr/bin/env python3
"""V1 - validacao estatica da camada aerodinamica nos dumps do decompilador.

Le /home/gullin/.hermes/cache/scratch/v3_init.gml e v3_step.gml, gerados por
mod/dump_v3.csx, e verifica os invariantes da aerodinamica (3.2.0).
"""
import sys
import pathlib

D = pathlib.Path("/home/gullin/.hermes/cache/scratch")
INIT = D / "v3_init.gml"
STEP = D / "v3_step.gml"
DRAW = D / "v3_draw64.gml"

checks = []


def ck(nome, ok):
    checks.append((nome, bool(ok)))


if not INIT.exists() or not STEP.exists() or not DRAW.exists():
    print(f"FALHA dumps ausentes: {INIT} / {STEP} / {DRAW}")
    print("rode primeiro: mcml-fixer --mod-install && dump_v3.csx")
    sys.exit(2)

init = INIT.read_text(encoding="utf-8", errors="ignore")
step = STEP.read_text(encoding="utf-8", errors="ignore")
draw = DRAW.read_text(encoding="utf-8", errors="ignore")

# --- camada aero existe e tem as constantes fisicas ---
ck("aero_modulo", "global.aero_f_drag = function" in init and "global.aero_f_down = function" in init)
ck("aero_rho", "global.aero_rho = 1.225" in init)
ck("aero_induzido", "global.aero_k_ind = 1 / (pi * global.aero_e * global.aero_ar)" in init)
ck("aero_fz", "global.aero_fz = function" in init)
ck("aero_ini", 'ini_read_real("aero", "cl"' in init and 'ini_read_real("aero", "crr"' in init)

# --- arrasto: divisor fixo de 25000 saiu, camada entrou ---
ck("sem_divisor_25000", "/ 25000" not in step)
ck("arrasto_pela_camada", "_desacel_arrasto = global.aero_drag_units();" in step)

# --- downforce na carga do pneu ---
lim = init[init.index("global.phys_limite = function"):]
lim = lim[:lim.index("};")]
ck("downforce_na_carga", "global.aero_f_down()" in lim)

# --- empuxo limitado pela potencia ---
ck("empuxo_por_potencia", "_acceleration = min(_acceleration, global.aero_thrust_units());" in step)

# --- sonda registra as forcas ---
ck("potencia_pico", 'variable_global_exists("maxhp")' in init and "global.maxhp" in init)
ck("potencia_falha_aberto", "_pw <= 0" in init)

ck("sonda_aero", all(k in draw for k in ("fdrag=", "fdown=", "froll=", "pacc=", "cd=")))

falhas = [c for c in checks if not c[1]]
for nome, ok in checks:
    print(("OK    " if ok else "FALHA ") + nome)
print(f"V1 {'OK' if not falhas else 'FALHOU'} {len(checks) - len(falhas)}/{len(checks)}")
sys.exit(0 if not falhas else 1)
