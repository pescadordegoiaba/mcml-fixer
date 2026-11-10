#!/usr/bin/env python3
"""Alvos do plano de distribuidor/carburador (fase 1) e o gate de 1/4 de milha."""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "mod"))

from fuel_model import (  # noqa: E402
    Fuel, eff_afr, eff_timing, fuel_steady, fuel_step, knock_threshold,
    lookup, preset_curve, spring_kit_curve,
)
from phys_v28 import rpm_to_w, step, street_500, w_to_rpm  # noqa: E402

DT = 1.0 / 60.0
FT60 = 18.288
QUARTER = 402.336
fails = []


def check(name, ok, detail=""):
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""))
    if not ok:
        fails.append(name)


def test_points():
    print("pontos de regime")
    st = Fuel()
    fuel_steady(st, 4000, 1.0)
    check("AFR em WOT", 12.5 <= st.afr <= 13.0, f"{st.afr:.2f}")
    check("timing em WOT ~4000", 32.0 <= st.timing <= 36.0, f"{st.timing:.1f}")
    check("sem knock no WOT afinado", st.knock == 0.0, f"{st.knock:.2f}")
    check("eficiencia ~1 no WOT afinado", st.eff > 0.97, f"{st.eff:.3f}")

    fuel_steady(st, 2500, 0.25)
    check("AFR em cruzeiro", 14.0 <= st.afr <= 15.0, f"{st.afr:.2f}")
    check("timing em cruzeiro", 40.0 <= st.timing <= 45.0, f"{st.timing:.1f} map={st.map_bar:.2f}")

    fuel_steady(st, 850, 0.0)
    check("AFR em lenta", 12.5 <= st.afr <= 13.5, f"{st.afr:.2f}")

    thr = knock_threshold(4000, 12.8, 40, 100, 93, 0)
    check("knock threshold ~38–42", 38.0 <= thr <= 42.0, f"{thr:.1f}")

    check("perda ±10° de MBT", 0.10 <= (1 - eff_timing(44, 34)) <= 0.16, f"{1-eff_timing(44,34):.3f}")
    check("perda AFR ±2", 0.08 <= (1 - eff_afr(14.8)) <= 0.12, f"{1-eff_afr(14.8):.3f}")


def test_knock_retard():
    print("retard de knock")
    st = Fuel(base_timing=36.0, spring="street", vacuum_on=False)
    # WOT com base alta: comando passa do threshold e o retard sobe
    fuel_steady(st, 4000, 1.0)
    grew = False
    peak = 0.0
    for _ in range(int(6.0 / 0.05)):
        fuel_step(st, 4000, 1.0, 0.05)
        peak = max(peak, st.knock_retard)
        if st.knock > 0.2:
            grew = True
    check("knock aparece com avanco demais", grew, f"k={st.knock:.2f} ret={st.knock_retard:.1f}")
    check("retard acumula e respeita o teto", 14.0 <= peak <= 15.0, f"pico={peak:.1f}")
    # folga: tira o avanco e o retard volta
    st.base_timing = 8.0
    for _ in range(int(4.0 / 0.05)):
        fuel_step(st, 2000, 0.3, 0.05)
    check("retard recupera", st.knock_retard < peak - 1.0, f"{peak:.1f} -> {st.knock_retard:.1f}")


def test_power_valve():
    print("power valve")
    st = Fuel()
    fuel_steady(st, 3000, 0.3)
    cruise = st.afr
    fuel_steady(st, 4000, 1.0)
    wot = st.afr
    check("WOT mais rico que o cruzeiro", wot < cruise - 0.4, f"cruise={cruise:.2f} wot={wot:.2f}")
    check("enriquecimento na faixa", 0.5 <= (cruise - wot) <= 1.2, f"{cruise-wot:.2f}")


def quarter(fuel):
    car = street_500()
    car.fuel = fuel
    car.pt.gear = 1
    car.inputs.gear_target = 1
    car.rear.tire.temp = 90.0
    car.pt.omega_eng = rpm_to_w(4800)
    t = 0.0
    t60 = None
    tq = None
    trap = None
    x_prev = 0.0
    while t < 20.0 and tq is None:
        u = min(1.0, t / 0.10)
        car.inputs.brake_pedal = 0.0
        car.inputs.clutch_pedal = 1.0 - u
        car.inputs.throttle = 1.0
        car.inputs.ignition = True
        if (car.pt.shift_phase == "none" and car.pt.gear < len(car.pt.gear_ratios)
                and w_to_rpm(car.pt.omega_eng) > 6400.0 and car.chassis.vx > 8.0):
            car.inputs.gear_target = car.pt.gear + 1
            car.inputs.shift_request = True
        else:
            car.inputs.shift_request = False
            if car.pt.shift_phase == "none":
                car.inputs.gear_target = car.pt.gear
        step(car, DT)
        t += DT
        if t60 is None and car.chassis.x >= FT60 and car.chassis.x > x_prev:
            frac = (FT60 - x_prev) / (car.chassis.x - x_prev)
            t60 = t - DT + frac * DT
        if tq is None and car.chassis.x >= QUARTER and car.chassis.x > x_prev:
            frac = (QUARTER - x_prev) / (car.chassis.x - x_prev)
            tq = t - DT + frac * DT
            trap = car.chassis.vx * 3.6
        x_prev = car.chassis.x
    return t60, tq, trap


def test_accel_pump():
    print("bomba de aceleracao")
    quiet = Fuel()
    fuel_steady(quiet, 3000, 1.0)
    a0 = quiet.afr
    fuel_step(quiet, 3000, 1.0, 0.05)
    check("sem tip-in a bomba fica parada", quiet.accel_timer == 0.0 and abs(quiet.afr - a0) < 0.05,
          f"afr={quiet.afr:.2f} t={quiet.accel_timer:.2f}")

    tip = Fuel()
    fuel_steady(tip, 3000, 0.0)
    fuel_step(tip, 3000, 1.0, 0.05)
    wot = Fuel()
    fuel_steady(wot, 3000, 1.0)
    drop = wot.afr - tip.afr
    check("tip-in derruba o AFR ~1.5 a 2.5", 1.5 <= drop <= 2.5, f"{drop:.2f} ({tip.afr:.2f} vs {wot.afr:.2f})")
    check("bomba fica ativa", tip.accel_timer > 0.0, f"{tip.accel_timer:.2f}")
    for _ in range(int(0.6 / 0.05)):
        fuel_step(tip, 3000, 1.0, 0.05)
    check("AFR volta depois da bomba", abs(tip.afr - wot.afr) < 0.15 and tip.accel_timer == 0.0,
          f"{tip.afr:.2f} vs {wot.afr:.2f}")


def test_choke():
    print("afogador")
    st = Fuel(coolant=20.0, engine_on=False)
    fuel_step(st, 800, 0.0, 0.05)
    check("frio liga o afogador", st.choke_active, f"afr={st.afr:.2f}")
    check("AFR de partida ~11", 10.5 <= st.afr <= 11.5, f"{st.afr:.2f}")
    check("lenta sobe para 1100", st.idle_target == 1100, f"{st.idle_target:.0f}")
    st.coolant = 70.0
    st.engine_on = True
    for _ in range(int(20.0 / 0.05)):
        fuel_step(st, 1200, 0.2, 0.05)
    check("quente e depois do tempo minimo desliga", not st.choke_active, f"t={st.choke_timer:.1f} afr={st.afr:.2f}")


def test_knock_damage():
    print("dano de knock")
    st = Fuel(base_timing=36.0, vacuum_on=False)
    fuel_steady(st, 4000, 1.0)
    for _ in range(int(2.5 / 0.05)):
        fuel_step(st, 4000, 1.0, 0.05)
    check("2 s de knock sobem o dano", st.knock_damage > 0.03, f"{st.knock_damage:.3f} k={st.knock:.2f}")
    base = Fuel()
    fuel_steady(base, 4000, 1.0)
    e0 = base.eff
    base.knock_damage = 1.0
    fuel_step(base, 4000, 1.0, 0.0)
    check("dano total tira torque", base.eff < e0 * 0.75, f"{e0:.3f} -> {base.eff:.3f}")
    hurt = st.knock_damage
    st.base_timing = 8.0
    st.knock_clean = 61.0
    for _ in range(int(8.0 / 0.05)):
        fuel_step(st, 2000, 0.25, 0.05)
    check("depois do cooldown o dano cai", st.knock <= 0.0 and st.knock_damage < hurt - 0.004,
          f"{hurt:.3f} -> {st.knock_damage:.3f} k={st.knock:.2f}")


def test_iat():
    print("IAT dinamico")
    st = Fuel(coolant=100.0, iat=40.0, iat_ambient=40.0, vx=0.0)
    for _ in range(int(10.0 / 0.05)):
        fuel_step(st, 4000, 1.0, 0.05)
    rise = st.iat - 40.0
    check("parado e quente o cofre sobe 20–30 C em 10 s", 20.0 <= rise <= 30.0, f"{rise:.1f}")
    st.vx = 30.0
    for _ in range(int(40.0 / 0.05)):
        fuel_step(st, 2000, 0.0, 0.05)
    check("em velocidade o ar volta perto do ambiente", st.iat < 48.0, f"{st.iat:.1f}")


def test_curve_editor():
    print("curva de avanco")
    street = preset_curve("street")
    manual = Fuel(base_timing=0.0, vacuum_on=False, cent_scale=1.0, advance=0.0, manual_retard=0.0)
    manual.mech_curve = street
    fuel_steady(manual, 3000, 0.3)
    auto = Fuel(base_timing=0.0, vacuum_on=False, cent_scale=1.0, spring="street")
    fuel_steady(auto, 3000, 0.3)
    check("preset street reproduz a mola street", abs(manual.timing - auto.timing) < 0.05,
          f"{manual.timing:.2f} vs {auto.timing:.2f}")
    pts = [(800, 0), (1200, 5), (2000, 10), (3000, 20), (4000, 20), (5000, 20), (6000, 20), (7000, 20)]
    edited = Fuel(base_timing=0.0, vacuum_on=False, cent_scale=1.0)
    edited.mech_curve = pts
    fuel_steady(edited, 3000, 0.3)
    t0 = edited.timing
    edited.mech_curve = [(800, 0), (1200, 5), (2000, 10), (3000, 25), (4000, 20), (5000, 20), (6000, 20), (7000, 20)]
    fuel_steady(edited, 3000, 0.3)
    check("ponto em 3000 rpm sobe o ponto 5 graus", abs((edited.timing - t0) - 5.0) < 0.05,
          f"{t0:.2f} -> {edited.timing:.2f}")
    light = spring_kit_curve(800, 1500, 4000, 5000)
    check("mola leve comeca em 800", light[0][0] == 800 and lookup(light, 900) > lookup(light, 800),
          f"{lookup(light, 800):.2f} -> {lookup(light, 900):.2f}")
    heavy = spring_kit_curve(2000, 2500, 3500, 5000)
    check("mola pesada so satura em 5000", lookup(heavy, 5000) > lookup(heavy, 3500) + 5.0,
          f"3500={lookup(heavy, 3500):.1f} 5000={lookup(heavy, 5000):.1f}")


def test_quarter():
    print("1/4 de milha com o modelo ligado")
    t60, tq, trap = quarter(Fuel())
    print(f"    otimo  60ft={t60}  1320={tq}  trap={trap}")
    check("otimo 60 pes", t60 is not None and 1.9 <= t60 <= 2.1, f"{t60}")
    check("otimo 1/4", tq is not None and 11.5 <= tq <= 12.0, f"{tq}")
    check("otimo trap", trap is not None and 175.0 <= trap <= 205.0, f"{trap}")

    # base 5° em vez de 10°, gicle 60 (o mais pobre da faixa) → WOT mais magro
    poor = Fuel(base_timing=5.0, main_jet=60.0)
    p60, pq, ptrap = quarter(poor)
    print(f"    pobre  60ft={p60}  1320={pq}  trap={ptrap}")
    check("afinado pobre e mais lento", pq is not None and tq is not None and pq > tq + 0.25, f"{tq} -> {pq}")


def main():
    test_points()
    test_power_valve()
    test_knock_retard()
    test_accel_pump()
    test_choke()
    test_knock_damage()
    test_iat()
    test_curve_editor()
    test_quarter()
    print()
    if fails:
        print(f"{len(fails)} falharam:")
        for f in fails:
            print(" -", f)
        return 1
    print("todos os checks passaram")
    return 0


if __name__ == "__main__":
    sys.exit(main())
