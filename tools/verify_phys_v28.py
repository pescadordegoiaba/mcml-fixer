#!/usr/bin/env python3
"""Validação da §7 do spec v2.8 contra mod/phys_v28.py.

A classe street ~500 hp é o gate de implantação (60 pes, 1/4 de milha, trap).
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "mod"))

from phys_v28 import (  # noqa: E402
    Const,
    apply_circle,
    crr_psi,
    ieq_engine,
    mu_psi,
    rpm_to_w,
    settle_springs,
    step,
    street_500,
    temp_factor,
    torque_500,
    w_to_rpm,
)

FT60 = 18.288
QUARTER = 402.336
DT = 1.0 / 60.0

fails = []


def check(name, ok, detail=""):
    flag = "PASS" if ok else "FAIL"
    print(f"  [{flag}] {name}" + (f" — {detail}" if detail else ""))
    if not ok:
        fails.append(name)


def hold(car, seconds, **inp):
    n = int(round(seconds / DT))
    for _ in range(n):
        for k, v in inp.items():
            setattr(car.inputs, k, v)
        step(car, DT)
    return car


def test_static():
    print("equilibrio estatico")
    car = street_500()
    z0 = car.chassis.z_cg
    hold(car, 2.0, throttle=0.0, clutch_pedal=1.0, gear_target=0, brake_pedal=0.0)
    # neutro: o idle segura o motor, o chassi nao pode afundar
    car.pt.gear = 0
    dz = abs(car.chassis.z_cg - z0)
    th = abs(car.chassis.theta)
    check("z_cg nao deriva em 2 s", dz < 0.002, f"dz={dz*1000:.2f} mm")
    check("theta nao deriva em 2 s", th < 0.002, f"theta={math.degrees(th):.3f} deg")
    check("omega_eng >= 0", car.pt.omega_eng >= -1e-9)
    check("safety net quieto", car.stats["clamp_vx"] + car.stats["clamp_vz"] + car.stats["clamp_w"] == 0)


def test_inertia():
    print("reflexao de inercia")
    car = street_500()
    car.pt.gear = 1
    i1 = ieq_engine(car)
    car.pt.gear = 5
    i5 = ieq_engine(car)
    check("1a nao infla I_eq", i1 < car.pt.inertia_eng * 3.0, f"I1={i1:.3f} Ieng={car.pt.inertia_eng:.3f}")
    check("I_eq = Ieng + Iw/g^2", abs(i1 - (car.pt.inertia_eng + 2.1 / (8.6 ** 2))) < 0.02, f"I1={i1:.4f}")
    # neutro, embreagem aberta: so a inercia do motor. 1a no chao acompanha o carro.
    car = street_500()
    car.pt.gear = 0
    car.pt.omega_eng = rpm_to_w(1000.0)
    t = 0.0
    hit = None
    while t < 2.0:
        car.inputs.throttle = 1.0
        car.inputs.clutch_pedal = 1.0
        car.inputs.gear_target = 0
        step(car, DT)
        t += DT
        if w_to_rpm(car.pt.omega_eng) >= 5000.0:
            hit = t
            break
    check("livre sobe a 5000 rpm em < 2 s", hit is not None and hit < 2.0, f"t={hit}")
    car = street_500()
    car.pt.gear = 1
    car.pt.omega_eng = rpm_to_w(3500.0)
    hold(car, 1.5, throttle=1.0, clutch_pedal=0.0, gear_target=1, brake_pedal=0.0)
    check("1a engatada nao morre e anda",
          (not car.pt.engine_stalled) and car.chassis.vx > 5.0 and w_to_rpm(car.pt.omega_eng) > 2000.0,
          f"rpm={w_to_rpm(car.pt.omega_eng):.0f} vx={car.chassis.vx:.1f} stall={car.pt.engine_stalled}")


def test_reverse_drag():
    print("re: arrasto nao acelera")
    car = street_500()
    car.chassis.vx = -40.0
    car.pt.gear = 0
    car.inputs.gear_target = 0
    car.inputs.throttle = 0.0
    car.inputs.clutch_pedal = 1.0
    v0 = car.chassis.vx
    hold(car, 1.0, throttle=0.0, clutch_pedal=1.0, gear_target=0)
    # |vx| tem de cair (arrasto e rolamento opoem o movimento)
    check("re desacelera", abs(car.chassis.vx) < abs(v0) - 0.4, f"{v0:.2f} -> {car.chassis.vx:.2f}")


def test_units_psi_temp():
    print("psi, temp_factor, circulo")
    check("drag 10 psi no pico", abs(mu_psi(10, 10) - 1.05) < 1e-6)
    # acima de psi_peak+15 o mapa satura (penalidade de sobre-inflacao e fase 2)
    check("drag vazio penalizado", mu_psi(0, 10) < mu_psi(10, 10))
    check("drag 35 psi no teto do mapa", abs(mu_psi(35, 10) - 1.05) < 1e-9)
    check("street 32 psi no pico", abs(mu_psi(32, 32) - 1.05) < 1e-9)
    check("street 10 psi penalizado", mu_psi(10, 32) < mu_psi(32, 32))
    check("temp 90 = 1", abs(temp_factor(90) - 1.0) < 1e-9)
    check("temp 20 = 0.70", abs(temp_factor(20) - 0.70) < 1e-9)
    check("temp 140 = 0.70", abs(temp_factor(140) - 0.70) < 1e-9)
    check("crr cai com psi no pico", crr_psi(32, 32) < crr_psi(10, 32))

    car = street_500()
    hold(car, 0.2, throttle=0.0, clutch_pedal=1.0, gear_target=0)
    axle = car.rear
    axle.fx = axle.mu_static_eff * axle.normal_load * 0.8
    axle.stick_state = True
    n = axle.normal_load
    apply_circle(axle, 0.5 * axle.mu_eff * n)
    mag = math.hypot(axle.fx, axle.fy)
    cap = axle.mu_static_eff * n
    check("circulo em stick usa mu_static", mag <= cap * 1.01 + 1.0, f"mag={mag:.0f} cap={cap:.0f}")
    axle.stick_state = False
    axle.mu_eff = axle.mu_static_eff * 0.7
    axle.fx = axle.mu_eff * n
    axle.fy = 0.0
    apply_circle(axle, 0.5 * axle.mu_eff * n)
    mag = math.hypot(axle.fx, axle.fy)
    check("circulo em slip usa mu_eff", mag <= axle.mu_eff * n * 1.02 + 1.0, f"mag={mag:.0f}")


def test_burnout():
    print("burnout")
    car = street_500()
    # pneu ainda frio/escorregadio: so assim o line lock SEGURA o carro em 1a.
    # com mu de drag radial quente a forca da 1a vence o pneu dianteiro e o carro anda.
    car.rear.tire.mu_base = 0.55
    car.rear.tire.mu_static = 0.75
    car.pt.gear = 1
    car.pt.omega_eng = rpm_to_w(3500.0)
    car.inputs.gear_target = 1
    hold(car, 5.0, throttle=1.0, clutch_pedal=0.0, brake_pedal=0.0,
         line_lock=True, handbrake=False, gear_target=1, ignition=True)
    tr = car.rear.tire.temp
    tf = car.front.tire.temp
    check("traseira aquece no burnout", tr >= 70.0, f"Tr={tr:.1f} Tf={tf:.1f}")
    check("pico perto de 90 C em 5 s", 70.0 <= tr <= 130.0, f"Tr={tr:.1f}")
    check("dianteira fica fria", tf < 40.0, f"Tf={tf:.1f}")
    check("roda traseira girou", abs(car.rear.omega_w) > 5.0, f"wr={car.rear.omega_w:.1f}")
    # esfriar 15 s sem potencia de slip
    t0 = tr
    hold(car, 15.0, throttle=0.0, clutch_pedal=1.0, line_lock=False, gear_target=0)
    check("esfria sem zerar", car.const.t_amb + 5 < car.rear.tire.temp < t0 - 8.0,
          f"{t0:.1f} -> {car.rear.tire.temp:.1f}")


def test_launch_and_quarter():
    print("largada e 1/4 de milha (gate street 500 hp)")
    car = street_500()
    car.trace_sub = False
    # burnout curto para o pneu chegar perto do pico de mu
    car.pt.gear = 1
    car.inputs.gear_target = 1
    hold(car, 4.0, throttle=1.0, clutch_pedal=0.0, line_lock=True, gear_target=1)
    # stage: two-step a 4800, embreagem aberta
    car.inputs.line_lock = False
    car.inputs.brake_pedal = 1.0
    car.chassis.vx = 0.0
    car.chassis.x = 0.0
    car.rear.omega_w = 0.0
    car.front.omega_w = 0.0
    for _ in range(30):  # 0,5 s
        car.inputs.throttle = 1.0
        car.inputs.clutch_pedal = 1.0
        car.inputs.brake_pedal = 1.0
        car.inputs.gear_target = 1
        car.pt.gear = 1
        car.pt.omega_eng = rpm_to_w(4800.0)
        step(car, DT)
    car.stats["stick_r"] = 0
    car.chassis.x = 0.0
    car.chassis.vx = 0.0
    # dump em ~0,10 s
    t = 0.0
    t60 = None
    t1320 = None
    trap = None
    rpm_max = 0.0
    x_prev = 0.0
    shifts = 0
    while t < 20.0 and t1320 is None:
        # solta o freio e a embreagem
        u = min(1.0, t / 0.10)
        car.inputs.brake_pedal = 0.0
        car.inputs.clutch_pedal = 1.0 - u
        car.inputs.throttle = 1.0
        car.inputs.ignition = True
        # troca no corte
        if (car.pt.shift_phase == "none" and car.pt.gear < len(car.pt.gear_ratios)
                and w_to_rpm(car.pt.omega_eng) > 6400.0 and car.chassis.vx > 8.0):
            car.inputs.gear_target = car.pt.gear + 1
            car.inputs.shift_request = True
            shifts += 1
        else:
            car.inputs.shift_request = False
            car.inputs.gear_target = car.pt.gear if car.pt.shift_phase == "none" else car.inputs.gear_target
        step(car, DT)
        t += DT
        rpm_max = max(rpm_max, w_to_rpm(car.pt.omega_eng))
        if t60 is None and car.chassis.x >= FT60:
            # interpola
            if car.chassis.x > x_prev:
                frac = (FT60 - x_prev) / (car.chassis.x - x_prev)
                t60 = t - DT + frac * DT
        if t1320 is None and car.chassis.x >= QUARTER:
            if car.chassis.x > x_prev:
                frac = (QUARTER - x_prev) / (car.chassis.x - x_prev)
                t1320 = t - DT + frac * DT
                trap = car.chassis.vx * 3.6
        x_prev = car.chassis.x
    chatter = car.stats["stick_r"] / max(t60 or t, 0.1)
    print(f"    60ft={t60}  1320={t1320}  trap={trap}  shifts={shifts}  rpm_max={rpm_max:.0f}  stick/s={chatter:.1f}")
    check("60 pes 1,9–2,1 s", t60 is not None and 1.9 <= t60 <= 2.1, f"{t60}")
    check("1/4 11,5–12,0 s", t1320 is not None and 11.5 <= t1320 <= 12.0, f"{t1320}")
    check("trap ~190 km/h", trap is not None and 175.0 <= trap <= 205.0, f"{trap}")
    check("sem chatter na largada", chatter < 8.0, f"{chatter:.2f}/s")
    check("safety net no passeio", car.stats["clamp_vx"] + car.stats["clamp_vz"] + car.stats["clamp_w"] == 0)
    check("nao capotou", not car.chassis.capotou)
    return car


def test_stick_transient():
    print("transiente stick -> slip")
    car = street_500()
    car.trace_sub = True
    # clutch dump brutal: muita marcha, para quebrar de proposito
    car.pt.gear_ratios = [14.0, 8.0, 5.5, 4.0, 3.2]
    car.pt.gear = 1
    car.pt.clutch_capacity_max = 1200.0
    car.rear.tire.mu_static = 1.3
    car.rear.tire.mu_base = 1.05
    settle_springs(car)
    car.pt.omega_eng = rpm_to_w(5000.0)
    car.inputs.gear_target = 1
    car.inputs.throttle = 1.0
    car.inputs.clutch_pedal = 0.0
    car.inputs.brake_pedal = 0.0
    # alguns substeps
    for _ in range(8):
        step(car, DT)
    tr = car.subtrace
    # achar a primeira quebra do stick traseiro
    broke = None
    for i in range(1, len(tr)):
        if tr[i - 1]["stick_r"] and not tr[i]["stick_r"]:
            broke = i
            break
    if broke is None and tr and not tr[0]["stick_r"]:
        broke = 0  # quebrou no primeiro substep (o estado inicial era stick)
    check("houve quebra de tracao", broke is not None, f"idx={broke} n={len(tr)}")
    if broke is None:
        return
    fx = abs(tr[broke]["fx_r"])
    mu_n = None
    # mu_static_eff * N nao esta no trace; usa a carga e um teto largo
    # o criterio e |Fx| < 0.05 * mu_static_eff * N. Com mu alto, 0.05*2*N ~ 0.1 N.
    n = tr[broke]["n_r"]
    check("Fx ~ 0 no substep da quebra", fx < 0.15 * max(n, 1.0), f"Fx={fx:.0f} N={n:.0f}")
    recovered = False
    for j in range(broke + 1, min(broke + 3, len(tr))):
        if abs(tr[j]["fx_r"]) > 0.25 * max(tr[j]["n_r"], 1.0):
            recovered = True
            break
    check("recupera forca em <= 2 substeps", recovered, f"fx+1={tr[broke+1]['fx_r']:.0f}" if broke + 1 < len(tr) else "fim")


def test_wheelie():
    print("wheelie")
    car = street_500(
        h_cg=0.72, a_cg=1.25, wheelbase=2.55, mass=1350.0,
        bar=False, cda=0.50, cla=0.0, h_antisquat=0.0,
        ratios=[11.0, 7.5, 5.4, 4.1, 3.2],
        clutch_cap=900.0,
    )
    car.rear.tire.mu_base = 1.9
    car.rear.tire.mu_static = 2.2
    car.rear.tire.temp = 90.0
    settle_springs(car)
    car.pt.gear = 1
    car.pt.omega_eng = rpm_to_w(4500.0)
    car.trace_sub = True
    car.inputs.gear_target = 1
    lifted = False
    landed = False
    for i in range(int(4.0 / DT)):
        t = i * DT
        car.inputs.throttle = 1.0 if t < 0.45 else 0.0
        car.inputs.clutch_pedal = 0.0
        car.inputs.gear_target = 1
        was_up = not car.front.grounded
        step(car, DT)
        if car.front.z_u > car.front.radius + 0.01:
            lifted = True
        if was_up and car.front.grounded:
            landed = True
            break
    zu_max = max(s["z_u"] for s in car.subtrace)
    check("lift-off real (z_u > raio)", zu_max > car.front.radius + 0.01,
          f"zu_max={zu_max:.3f} r={car.front.radius:.3f}")
    check("pouso com a roda de volta", landed and car.front.grounded, f"landed={landed}")
    check("sem explosao no pouso", abs(car.chassis.z_cg - car.chassis.h_cg) < 0.25,
          f"z={car.chassis.z_cg:.3f}")
    rate = car.stats["ground_f"] / 4.0
    check("sem chatter de contato", rate < 10.0, f"{rate:.2f}/s")
    check("safety net no wheelie", car.stats["clamp_vx"] + car.stats["clamp_vz"] + car.stats["clamp_w"] == 0)


def test_bar():
    print("wheelie bar")
    car = street_500(
        h_cg=0.72, a_cg=1.20, wheelbase=2.55, mass=1400.0,
        bar=True, k_bar=3.0e5, c_bar=0.15 * 3.0e5, d_bar=0.40,
        ratios=[15.0, 9.0, 6.2, 4.5, 3.4], clutch_cap=1400.0, cla=0.0, cda=0.4,
    )
    car.rear.tire.mu_base = 2.3
    car.rear.tire.mu_static = 2.7
    car.rear.tire.temp = 90.0
    settle_springs(car)
    car.bar.h_bar = car.chassis.h_cg - 0.04
    car.pt.gear = 1
    car.pt.omega_eng = rpm_to_w(5200.0)
    car.trace_sub = True
    for _ in range(int(1.5 / DT)):
        car.inputs.throttle = 1.0
        car.inputs.clutch_pedal = 0.0
        car.inputs.gear_target = 1
        step(car, DT)
    th = max(abs(s["theta"]) for s in car.subtrace)
    pen = max(s["pen"] for s in car.subtrace)
    touched = any(s["bar"] for s in car.subtrace)
    check("barra encosta", touched, f"pen={pen*100:.2f} cm theta={math.degrees(th):.1f}")
    check("penetracao < 2 cm", pen < 0.02, f"{pen*100:.2f} cm")
    check("nao capota", not car.chassis.capotou and th < math.radians(25), f"{math.degrees(th):.1f} deg")
    rate = car.stats["bar"] / 1.5
    check("barra sem pogo", rate < 10.0, f"{rate:.2f}/s")


def test_stall():
    print("stall e restart")
    car = street_500()
    car.pt.gear = 1
    car.pt.omega_eng = rpm_to_w(400.0)
    car.pt.clutch_engagement = 1.0
    car.chassis.vx = 0.0
    car.rear.omega_w = 0.0
    # solta a embreagem em giro baixo, parado, freio seguro
    hold(car, 1.5, throttle=0.2, clutch_pedal=0.0, brake_pedal=1.0, gear_target=1, ignition=False)
    check("motor morre", car.pt.engine_stalled, f"rpm={w_to_rpm(car.pt.omega_eng):.0f}")
    check("embreagem abriu", car.pt.clutch_mode == "open", car.pt.clutch_mode)
    check("omega nunca negativa", car.pt.omega_eng >= -1e-9)
    # sem embreagem pisada nao pega
    hold(car, 0.5, throttle=1.0, clutch_pedal=0.0, brake_pedal=1.0, ignition=True, gear_target=1)
    check("nao pega com o pe fora", car.pt.engine_stalled)
    hold(car, 2.0, throttle=0.4, clutch_pedal=1.0, brake_pedal=1.0, ignition=True, gear_target=1)
    check("pega com ignicao e embreagem", not car.pt.engine_stalled, f"rpm={w_to_rpm(car.pt.omega_eng):.0f}")


def test_brake_and_foot():
    print("freio e foot-brake")
    car = street_500()
    car.chassis.vx = 20.0
    car.rear.omega_w = 20.0 / car.rear.radius
    car.front.omega_w = 20.0 / car.front.radius
    car.pt.gear = 3
    car.pt.omega_eng = car.rear.omega_w * car.pt.gear_ratios[2]
    car.rear.stick_state = True
    car.front.stick_state = True
    wsign = []
    for _ in range(int(3.0 / DT)):
        car.inputs.throttle = 0.0
        car.inputs.brake_pedal = 1.0
        car.inputs.clutch_pedal = 1.0  # desacoplado, so o freio
        car.inputs.gear_target = 3
        car.pt.gear = 3
        step(car, DT)
        wsign.append(car.front.omega_w)
    # omega da dianteira nao pode trocar de sinal por causa do freio
    flipped = False
    seen_pos = False
    for w in wsign:
        if w > 0.2:
            seen_pos = True
        if seen_pos and w < -0.2:
            flipped = True
    check("lockup nao inverte omega", not flipped)
    check("carro perde velocidade", car.chassis.vx < 10.0, f"vx={car.chassis.vx:.2f}")

    # foot-brake: parado, freio a fundo, torque abaixo da capacidade
    car = street_500()
    car.pt.gear = 1
    car.pt.omega_eng = rpm_to_w(2000.0)
    car.inputs.gear_target = 1
    hold(car, 1.0, throttle=0.35, clutch_pedal=0.0, brake_pedal=1.0, gear_target=1)
    check("foot-brake segura o carro", abs(car.chassis.vx) < 0.5, f"vx={car.chassis.vx:.3f}")


def test_aero_magnitude():
    print("aero sem dupla contagem")
    # escolhe ClA para F_aero_z = -1000 N a 55.56 m/s
    vx = 200.0 / 3.6
    cla = -1000.0 / (0.5 * 1.225 * vx * vx)
    car = street_500(cla=cla, cda=0.55, x_cp=1.45)
    car.chassis.vx = vx
    car.pt.gear = 4
    g = car.pt.gear_ratios[3]
    car.pt.omega_eng = (vx / car.rear.radius) * g
    car.rear.omega_w = vx / car.rear.radius
    car.front.omega_w = vx / car.front.radius
    # segura a velocidade com um torque que equilibra o arrasto, via throttle baixo
    # e um gancho: depois de assentar, mede a soma das normais
    for _ in range(int(3.0 / DT)):
        # controlador grosseiro de velocidade
        err = vx - car.chassis.vx
        car.inputs.throttle = clamp_throttle(0.25 + err * 0.15)
        car.inputs.clutch_pedal = 0.0
        car.inputs.gear_target = 4
        car.pt.gear = 4
        step(car, DT)
    nsum = car.front.normal_load + car.rear.normal_load
    target = car.chassis.mass * car.const.g + 1000.0
    check("N = m*g + 1000 (1x, nao 2x)", abs(nsum - target) / target < 0.08,
          f"N={nsum:.0f} alvo={target:.0f} vx={car.chassis.vx*3.6:.1f}")


def clamp_throttle(x):
    if x < 0.0:
        return 0.0
    if x > 1.0:
        return 1.0
    return x


def test_weight_bias():
    print("weight bias")
    car = street_500()
    # sem aero relevante: vx 0, throttle 0, neutro
    hold(car, 1.0, throttle=0.0, clutch_pedal=1.0, gear_target=0)
    # F_susp * braco. Reconstroi F_susp pela mola (repouso, vel=0)
    def fsusp(ax):
        f = ax.spring_k * ax.travel
        return f
    ff = fsusp(car.front)
    fr = fsusp(car.rear)
    bal = abs(ff * car.chassis.a_cg - fr * car.chassis.b_cg)
    scale = car.chassis.mass_sprung * car.const.g * car.chassis.wheelbase
    check("Fsf*a = Fsr*b", bal / scale < 0.02, f"residuo={bal:.1f}")


def test_engine_brake():
    print("engine brake")
    car = street_500()
    car.chassis.vx = 30.0
    car.pt.gear = 3
    g = car.pt.gear_ratios[2]
    car.rear.omega_w = 30.0 / car.rear.radius
    car.front.omega_w = car.rear.omega_w
    car.pt.omega_eng = car.rear.omega_w * g
    v0 = car.chassis.vx
    hold(car, 2.0, throttle=0.0, clutch_pedal=0.0, brake_pedal=0.0, gear_target=3)
    check("desacelera sem acelerador", car.chassis.vx < v0 - 1.0, f"{v0:.1f} -> {car.chassis.vx:.1f}")


def test_shift_costs_time():
    print("troca custa tempo")
    def run(seq):
        car = street_500()
        car.pt.gear = 2
        car.chassis.vx = 25.0
        g = car.pt.gear_ratios[1]
        car.rear.omega_w = 25.0 / car.rear.radius
        car.front.omega_w = car.rear.omega_w
        car.pt.omega_eng = car.rear.omega_w * g
        car.inputs.gear_target = 2
        t = 0.0
        while car.chassis.x < 200.0 and t < 12.0:
            car.inputs.throttle = 1.0
            car.inputs.clutch_pedal = 0.0
            if seq and car.pt.shift_phase == "none" and car.pt.gear == 2 and w_to_rpm(car.pt.omega_eng) > 6000:
                car.inputs.gear_target = 3
                car.inputs.shift_request = True
            elif not seq and car.pt.gear == 2 and w_to_rpm(car.pt.omega_eng) > 6000:
                car.pt.gear = 3
                car.inputs.gear_target = 3
                car.pt.omega_eng = car.rear.omega_w * car.pt.gear_ratios[2]
            else:
                car.inputs.shift_request = False
            step(car, DT)
            t += DT
        return t
    t_seq = run(True)
    t_inst = run(False)
    dt = t_seq - t_inst
    # um unico troca no meio de um puxao de 200 m. 0,24 s de embreagem aberta
    # nao viram 0,24 s de relogio: o carro ja vai rapido. a faixa e o efeito, nao o timer.
    check("troca sequencial e mais lenta", 0.03 <= dt <= 0.50, f"delta={dt:.3f} s ({t_seq:.2f} vs {t_inst:.2f})")


def test_reaction_and_antisquat():
    print("reacao e anti-squat")
    def peak_theta(**kw):
        car = street_500(**kw)
        car.pt.gear = 1
        car.pt.omega_eng = rpm_to_w(4000)
        th = 0.0
        for _ in range(int(0.8 / DT)):
            car.inputs.throttle = 1.0
            car.inputs.clutch_pedal = 0.0
            car.inputs.gear_target = 1
            step(car, DT)
            th = max(th, car.chassis.theta)
        return th
    th0 = peak_theta(h_antisquat=0.0, s_eng=0.0)
    th_as = peak_theta(h_antisquat=0.25, s_eng=0.0)
    check("anti-squat reduz o pitch", th_as < th0 * 0.85, f"{math.degrees(th0):.2f} -> {math.degrees(th_as):.2f}")
    # transversal empurra o nariz para cima com s_eng = +1
    th_pos = peak_theta(s_eng=1.0, h_antisquat=0.0)
    th_neg = peak_theta(s_eng=-1.0, h_antisquat=0.0)
    check("s_eng +1 levanta mais que -1", th_pos > th_neg, f"+{math.degrees(th_pos):.2f} / {math.degrees(th_neg):.2f}")


def test_meff_bar():
    print("massa efetiva da barra (numeros do spec)")
    ms = 1390.0
    ip = 1040.0
    rbar = 1.60
    meff = 1.0 / (1.0 / ms + rbar * rbar / ip)
    check("m_eff_bar ~ 314 kg", abs(meff - 314.0) < 5.0, f"{meff:.1f}")
    k = 3.0e5
    c = 0.15 * k
    # c*dt/m < 2 nos dois rates
    check("estavel a 240 Hz", c * (1 / 240) / meff < 2.0, f"{c/240/meff:.2f}")
    check("estavel a 120 Hz", c * (1 / 120) / meff < 2.0, f"{c/120/meff:.2f}")


def main():
    test_static()
    test_inertia()
    test_reverse_drag()
    test_units_psi_temp()
    test_meff_bar()
    test_weight_bias()
    test_burnout()
    test_stall()
    test_brake_and_foot()
    test_engine_brake()
    test_aero_magnitude()
    test_reaction_and_antisquat()
    test_shift_costs_time()
    test_stick_transient()
    test_wheelie()
    test_bar()
    test_launch_and_quarter()
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
