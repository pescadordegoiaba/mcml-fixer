"""Distribuidor e carburador — fase 1 do plano.

Rodam antes do torque. Produzem timing (BTDC), afr e knock.
Nao movem o carro. A eficiencia entra no torque de combustao.

MAP do distribuidor e o do carburador do passo anterior.
"""

from __future__ import annotations

from dataclasses import dataclass, field


MAP_ATM = 1.013
INHG = 0.0339
K_DEPRESSION = 0.70
K_TIMING = 0.15
K_AFR = 0.10
AFR_PEAK = 12.8
KNOCK_RATE = 3.0       # graus/s de retard
KNOCK_RECOVER = 1.0    # graus/s de volta
KNOCK_CAP = 15.0

# kit de molas: 8 pontos (rpm, graus de avanco mecanico)
SPRINGS = {
    "street": ((800, 0), (1200, 6), (1600, 12), (2000, 17), (2600, 21), (3400, 24), (4500, 24), (6500, 24)),
    "strip":  ((800, 0), (1400, 3), (2000, 9), (2600, 15), (3200, 19), (4000, 22), (5000, 24), (6500, 24)),
    "race":   ((800, 0), (1800, 1), (2400, 6), (3000, 12), (3800, 16), (4600, 19), (5600, 21), (7000, 22)),
}
SPRING_BY_CURVA = ("street", "strip", "race")

# MBT: linhas = rpm, colunas = MAP (bar)
MBT_RPM = (800, 2000, 4000, 6000)
MBT_MAP = (0.3, 0.7, 1.0, 1.5)
MBT = (
    (15, 25, 20, 12),
    (20, 30, 28, 18),
    (25, 34, 34, 22),
    (28, 36, 38, 25),
)


def clamp(x, lo, hi):
    if x < lo:
        return lo
    if x > hi:
        return hi
    return x


def lerp(a, b, t):
    return a + (b - a) * t


def lookup(curve, rpm):
    if rpm <= curve[0][0]:
        return float(curve[0][1])
    if rpm >= curve[-1][0]:
        return float(curve[-1][1])
    for i in range(1, len(curve)):
        r0, y0 = curve[i - 1]
        r1, y1 = curve[i]
        if rpm <= r1:
            u = (rpm - r0) / (r1 - r0)
            return y0 + (y1 - y0) * u
    return float(curve[-1][1])


def _axis(v, knots):
    if v <= knots[0]:
        return 0, 0.0
    if v >= knots[-1]:
        return len(knots) - 2, 1.0
    for i in range(1, len(knots)):
        if v <= knots[i]:
            u = (v - knots[i - 1]) / (knots[i] - knots[i - 1])
            return i - 1, u
    return len(knots) - 2, 1.0


def mbt_at(rpm, map_bar):
    ir, ur = _axis(rpm, MBT_RPM)
    ic, uc = _axis(map_bar, MBT_MAP)
    y00 = MBT[ir][ic]
    y10 = MBT[ir + 1][ic]
    y01 = MBT[ir][ic + 1]
    y11 = MBT[ir + 1][ic + 1]
    y0 = y00 + (y10 - y00) * ur
    y1 = y01 + (y11 - y01) * ur
    return y0 + (y1 - y0) * uc


def ve_at(rpm):
    # pico ~0.92 por volta de 3800 rpm (faixa de torque de um street)
    x = (rpm - 3800.0) / 2400.0
    return clamp(0.92 - 0.16 * x * x, 0.62, 0.92)


VE_MAX = 0.92


def eff_timing(timing, mbt):
    d = (timing - mbt) / 10.0
    return max(0.40, 1.0 - K_TIMING * d * d)


def eff_afr(afr):
    d = (afr - AFR_PEAK) / 2.0
    return max(0.30, 1.0 - K_AFR * d * d)


def knock_threshold(rpm, afr, iat, coolant, octane, boost_psi):
    # 93 → 42°, 110 → 50°. O ponto de conferencia da §6.1 e ~4000 rpm, sem os extras.
    base = 42.0 + (octane - 93.0) * (8.0 / 17.0)
    f_rpm = clamp((rpm - 4000.0) / 2000.0, -2.0, 2.0)
    return (base + f_rpm
            - 1.0 * max(0.0, boost_psi)
            - 0.5 * max(0.0, afr - AFR_PEAK)
            - 0.2 * max(0.0, iat - 40.0)
            - 0.1 * max(0.0, coolant - 100.0)
            - 0.4 * max(0.0, 93.0 - octane))


@dataclass
class Fuel:
    base_timing: float = 10.0
    spring: str = "street"
    vacuum_on: bool = True
    max_vac: float = 19.0
    advance: float = 0.0            # graus somados ao ponto
    manual_retard: float = 0.0     # graus tirados do ponto
    cent_scale: float = 1.0
    rpm_ini: float = 1000.0
    rpm_fim: float = 3200.0
    main_jet: float = 80.0          # 60–100, maior = mais rico
    idle_turns: float = 2.5        # 1–4
    power_valve_inhg: float = 6.5  # abre quando o vacuo cai abaixo disto (WOT)
    pv_enrich: float = 1.0         # pontos de AFR a menos no WOT
    octane: float = 93.0
    iat: float = 40.0
    coolant: float = 90.0
    # estado
    map_bar: float = 0.35
    afr: float = 13.0
    timing: float = 10.0
    knock: float = 0.0
    knock_retard: float = 0.0
    load: float = 0.0
    mech: float = 0.0
    vac: float = 0.0
    mbt: float = 28.0
    eff: float = 1.0
    prev_throttle: float = 0.0
    tps_rate: float = 0.0


def _afr_idle(turns):
    # 2,5 voltas → 13,0. Faixa 1–4 cobre ~12,5–13,5.
    return 13.0 + (2.5 - turns) * 0.35


def _afr_main(jet):
    # 80 → 14,0 (cruzeiro, valvula fechada). Maior gicle, mais rico.
    return 14.0 + (80.0 - jet) * 0.045


def carb_step(st: Fuel, rpm, throttle):
    thr = clamp(throttle, 0.0, 1.0)
    ve = ve_at(rpm)
    st.load = thr * ve / VE_MAX
    st.map_bar = MAP_ATM - (1.0 - st.load) * MAP_ATM * K_DEPRESSION
    afr_i = _afr_idle(st.idle_turns)
    afr_m = _afr_main(st.main_jet)
    if thr < 0.05:
        afr = afr_i
    else:
        blend = clamp((thr - 0.05) / 0.20, 0.0, 1.0)
        afr = lerp(afr_i, afr_m, blend)
    vac_inhg = max(0.0, (MAP_ATM - st.map_bar) / INHG)
    if vac_inhg < st.power_valve_inhg and thr > 0.5:
        afr -= st.pv_enrich
    st.afr = afr
    return afr


def _mapped_rpm(st: Fuel, rpm: float) -> float:
    span = st.rpm_fim - st.rpm_ini
    if span < 200.0:
        span = 200.0
    return 1000.0 + (rpm - st.rpm_ini) * (2200.0 / span)


def distributor_step(st: Fuel, rpm, dt):
    curve = SPRINGS.get(st.spring, SPRINGS["street"])
    st.mech = lookup(curve, _mapped_rpm(st, rpm)) * st.cent_scale
    st.vac = 0.0
    if st.vacuum_on and st.map_bar < MAP_ATM:
        st.vac = st.max_vac * (1.0 - st.map_bar / MAP_ATM)
        st.vac = clamp(st.vac, 0.0, st.max_vac)
    commanded = st.base_timing + st.mech + st.vac + st.advance - st.manual_retard
    timing = commanded - st.knock_retard
    thr = knock_threshold(rpm, st.afr, st.iat, st.coolant, st.octane, 0.0)
    margin = thr - timing
    if margin < 0.0:
        st.knock = clamp(-margin / 5.0, 0.0, 1.0)
        st.knock_retard = min(KNOCK_CAP, st.knock_retard + KNOCK_RATE * dt)
    else:
        st.knock = 0.0
        st.knock_retard = max(0.0, st.knock_retard - KNOCK_RECOVER * dt)
    st.timing = commanded - st.knock_retard
    st.mbt = mbt_at(rpm, st.map_bar)
    e_t = eff_timing(st.timing, st.mbt)
    e_a = eff_afr(st.afr)
    knock_mult = 1.0 - 0.4 * st.knock
    st.eff = max(0.25, e_t * e_a * knock_mult)
    return st.timing


def fuel_step(st: Fuel, rpm, throttle, dt):
    """Ordem 3a depois 3b, com o MAP/AFR do passo anterior no distribuidor.

    O carburador atualiza no fim. Para o primeiro passo de um regime novo,
    chame duas vezes ou use fuel_steady.
    """
    if dt < 0.0:
        dt = 0.0
    st.tps_rate = (throttle - st.prev_throttle) / dt if dt > 1e-6 else 0.0
    distributor_step(st, rpm, dt)
    carb_step(st, rpm, throttle)
    # a eficiencia deste passo usa o AFR que o distribuidor viu (anterior).
    # recalcula com o AFR novo para o torque deste passo: o lag de AFR seria
    # 1 substep e o torque precisa do circuito que acabou de abrir (WOT).
    e_t = eff_timing(st.timing, mbt_at(rpm, st.map_bar))
    e_a = eff_afr(st.afr)
    st.mbt = mbt_at(rpm, st.map_bar)
    st.eff = max(0.25, e_t * e_a * (1.0 - 0.4 * st.knock))
    st.prev_throttle = throttle
    return st.eff


def fuel_steady(st: Fuel, rpm, throttle, n=8):
    """Assenta MAP e AFR sem deixar o knock integrar (dt = 0)."""
    for _ in range(n):
        fuel_step(st, rpm, throttle, 0.0)
    return st
