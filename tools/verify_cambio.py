#!/usr/bin/env python3
"""V1 - validacao estatica do cambio sobre o GML instalado.

Le os dumps gerados por mod/dump_v3.csx e falha (exit 1) se qualquer
invariante do cambio for violada.
"""
import re
import sys
import pathlib

D = pathlib.Path("/home/gullin/.hermes/cache/scratch")


def read(name):
    p = D / name
    if not p.exists():
        sys.exit(f"FALTA {p} (rode: ./mod/run_csx.sh dump_v3)")
    return p.read_text(encoding="utf-8", errors="ignore")


init = read("v3_init.gml")
create = read("v3_create.gml")
step = read("v3_step.gml")
draw = read("v3_draw.gml")

checks = []


def ck(nome, ok, detalhe=""):
    checks.append((nome, bool(ok), detalhe))


# 1. camada unica
ck("cg_init_car_definido", "global.cg_init_car = function" in init)
ck("cg_aplicar_definido", "global.cg_aplicar = function" in init)
ck("cg_confirmar_definido", "global.cg_confirmar = function" in init)
ck("cg_validar_definido", "global.cg_validar = function" in init)
ck("cg_avisos_definido", "global.cg_avisos = function" in init)

# 2. create usa a camada e nao tem tabela antiga
ck("create_usa_cg", create.count("global.cg_init_car(self") == 1,
   f"count={create.count('global.cg_init_car(self')}")
ck("create_sem_tabela_antiga", "original_ratios = [9" not in create)
ck("create_sem_max_gears_antigo", "max_gears = array_length(gear_ratios);" not in create)

# 3. confirmacao unica nas duas copias, sem escrita direta
ck("confirm_duas_copias", draw.count("global.cg_confirmar(_custo_cambio)") == 2,
   f"count={draw.count('global.cg_confirmar(_custo_cambio)')}")
ck("confirm_sem_cash_direto", "global.cash >= _custo_cambio" not in draw)
ck("sem_escrita_direta_gears_mod",
   not re.search(r"gears_mod\s*\[\s*i\s*\]\s*=", draw + step))

# 4. upshift recalcula o limite (manual e automatico)
ups = [m.start() for m in re.finditer(r"global\.gear \+= 1;", step)]
ok_ups = len(ups) == 2 and all(
    "_gearlimit = global.pt_gear_limit();" in step[i:i + 220] for i in ups)
ck("upshift_recalcula", ok_ups, f"ocorrencias={len(ups)}")

# 5. limitadores artificiais fora
ck("sem_power_band", "_base_acceleration *= _power_band_mult" not in step)
ck("sem_speed_resistance", "lerp(1, 0.5, _smooth_ratio)" not in step)

# 6. corte por rpm da roda + gravacao em disco
ck("corte_por_roda", "global.pt_rpm_roda()" in init)
ck("salva_no_confirmar", "SaveAllCars();" in init and "SaveGame();" in init)
ck("migracao_esquema", "cg_versao" in init and "MIGRADO" in init)
_col = ""
for _f in ("v3_col_touge.gml", "v3_col_npc.gml"):
    _p = D / _f
    if _p.exists():
        _col += _p.read_text(encoding="utf-8", errors="ignore")
ck("sem_teto_400_colisao", _col != "" and ", 400)" not in _col, "colisoes sem clamp 400")

_ini = init[init.index("global.cg_init_car = function"):init.index("global.cg_aplicar = function")]
ck("flag_edicao_preservada", "_car.cambio_mod = 0" not in _ini)

# 7. relacao final embutida uma unica vez (sem multiplicacao dupla)
ck("create_sem_escrita_direta",
   "gear_ratios[i] =" not in create and "gear_ratios[_ri]" not in create)


ck("sem_fator_velocidade_alto", "_fator_velocidade = 3.2" not in step and "_fator_velocidade = 2.5" not in step)
ck("sem_punicao_arrasto", "_desacel_arrasto *= _mod_multiplier" not in step)
ck("vmax_configuravel", "global.cambio_vmax" in init and "cambio_vmax = ini_read_real" in init)

falhas = [c for c in checks if not c[1]]
for nome, ok, det in checks:
    print(("OK    " if ok else "FALHA ") + nome + (f"  {det}" if det else ""))
print(f"V1 {'OK' if not falhas else 'FALHOU'} {len(checks) - len(falhas)}/{len(checks)}")
sys.exit(0 if not falhas else 1)
