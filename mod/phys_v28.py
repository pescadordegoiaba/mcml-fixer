"""phys_step v2.8 — longitudinal + pitch + DOF dianteiro.

Fonte: MCML_Physics_Spec v2.8. A ordem dos passos e os lags de 1 substep
sao parte do modelo. Nao reordenar.

Unidades SI. F_y e sempre 0 (fase 1, arrancada). Marcha a re nao existe.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

from fuel_model import fuel_step


def clamp(x, lo, hi):
    if x < lo:
        return lo
    if x > hi:
        return hi
    return x


def lerp(a, b, t):
    return a + (b - a) * t


def sgn(x):
    if x > 0.0:
        return 1.0
    if x < 0.0:
        return -1.0
    return 0.0


TWOPI = math.tau


def rpm_to_w(rpm):
    return rpm * TWOPI / 60.0


def w_to_rpm(w):
    return w * 60.0 / TWOPI


def temp_factor(temp):
    # 20 C -> 0.70, 90 C -> 1.00, 140 C -> 0.70; fora da faixa, 0.70
    if temp <= 20.0 or temp >= 140.0:
        return 0.70
    if temp < 90.0:
        return lerp(0.70, 1.00, (temp - 20.0) / 70.0)
    return lerp(1.00, 0.70, (temp - 90.0) / 50.0)


def psi_norm(psi, psi_peak):
    return clamp((psi - psi_peak + 15.0) / 15.0, 0.0, 1.0)


def mu_psi(psi, psi_peak):
    return lerp(0.85, 1.05, psi_norm(psi, psi_peak))


def crr_psi(psi, psi_peak):
    return lerp(0.015, 0.008, psi_norm(psi, psi_peak))


@dataclass
class Const:
    k_fmep: float = 0.05
    k_pump: float = 25.0
    fade_termico: float = 1.0
    k_rev: float = 0.01
    k_idle: float = 0.015
    omega_idle: float = field(default_factory=lambda: rpm_to_w(800.0))
    t_amb: float = 25.0
    rho: float = 1.225
    g: float = 9.81
    eps_clutch: float = 1.0
    eps_stick: float = 0.2
    eps_lock: float = 0.5
    eps_liftoff: float = 10.0


@dataclass
class Tire:
    mu_base: float
    mu_static: float
    load_sensitivity: float
    psi_peak: float
    k_heat: float
    k_cool: float
    temp: float = 25.0
    psi: float = 32.0


@dataclass
class Axle:
    name: str
    driven: bool
    radius: float
    tire: Tire
    mass_unsprung: float
    inertia_wheel: float
    t_brake_max: float
    inertia_drivetrain: float
    spring_k: float
    damp_c: float
    l_free: float
    travel_max: float
    travel_min: float
    k_bump: float
    k_droop: float
    omega_w: float = 0.0
    travel: float = 0.0
    travel_vel: float = 0.0
    normal_load: float = 0.0
    fx: float = 0.0
    fy: float = 0.0
    grounded: bool = True
    stick_state: bool = True
    z_u: float = 0.3
    v_zu: float = 0.0
    t_brake_eff: float = 0.0
    projected: bool = False
    kappa: float = 0.0
    mu_eff: float = 0.0
    mu_static_eff: float = 0.0


@dataclass
class WheelieBar:
    equipped: bool = True
    d_bar: float = 0.15
    h_bar: float = 0.45
    k_bar: float = 3.0e5
    c_bar: float = 4.5e4
    bar_grounded: bool = False
    f_bar: float = 0.0
    penetration: float = 0.0


@dataclass
class Inputs:
    throttle: float = 0.0
    clutch_pedal: float = 1.0
    brake_pedal: float = 0.0
    line_lock: bool = False
    handbrake: bool = False
    gear_target: int = 1
    shift_request: bool = False
    ignition: bool = True
    psi_front: float = 32.0
    psi_rear: float = 12.0


@dataclass
class Powertrain:
    omega_eng: float = field(default_factory=lambda: rpm_to_w(800.0))
    inertia_eng: float = 0.25
    clutch_engagement: float = 0.0
    clutch_capacity_max: float = 900.0
    gear: int = 0
    gear_ratios: list = field(default_factory=list)
    efficiency: float = 0.90
    shift_phase: str = "none"
    shift_timer: float = 0.0
    t_open: float = 0.08
    t_swap: float = 0.04
    t_close: float = 0.12
    clutch_mode: str = "open"
    omega_stall: float = field(default_factory=lambda: rpm_to_w(350.0))
    engine_stalled: bool = False
    t_starter: float = 80.0
    t_clutch: float = 0.0
    t_eng: float = 0.0
    t_load_eng: float = 0.0
    redline: float = field(default_factory=lambda: rpm_to_w(6800.0))
    force_slip_next: bool = False
    eng0: float = 0.0  # engagement no inicio da fase de troca


@dataclass
class Chassis:
    mass: float
    mass_sprung: float
    h_body: float
    inertia_pitch: float
    h_cg: float
    a_cg: float
    b_cg: float
    h_antisquat: float
    s_eng: float
    h_aero: float
    x_cp: float
    wheelbase: float
    cda: float
    cla: float
    z_cg: float = 0.5
    theta: float = 0.0
    vz: float = 0.0
    omega_pitch: float = 0.0
    vx: float = 0.0
    x: float = 0.0
    a_x: float = 0.0
    capotou: bool = False


@dataclass
class Car:
    const: Const
    chassis: Chassis
    front: Axle
    rear: Axle
    pt: Powertrain
    bar: WheelieBar
    inputs: Inputs
    torque_fn: object
    fuel: object = None
    n_sub: int = 4
    synced: bool = True
    subtrace: list = field(default_factory=list)
    trace_sub: bool = False
    stats: dict = field(default_factory=lambda: {
        "stick_f": 0, "stick_r": 0, "ground_f": 0, "bar": 0,
        "clamp_vx": 0, "clamp_vz": 0, "clamp_w": 0,
    })
    # ultimo substep, para telemetria
    throttle_eff: float = 0.0
    t_wheel_rear: float = 0.0
    t_wheel_front: float = 0.0
    f_drag: float = 0.0
    f_aero_z: float = 0.0
    f_rolling: float = 0.0

    def ratio(self):
        g = self.pt.gear
        ratios = self.pt.gear_ratios
        if g < 1 or g > len(ratios):
            return 0.0
        return ratios[g - 1]

    def driven_axles(self):
        out = []
        if self.front.driven:
            out.append(self.front)
        if self.rear.driven:
            out.append(self.rear)
        return out


def _susp_force(axle):
    travel = axle.travel
    f_spring = axle.spring_k * travel
    if travel > axle.travel_max:
        d = travel - axle.travel_max
        f_spring += axle.k_bump * d * d
    if travel < axle.travel_min:
        d = axle.travel_min - travel
        f_spring -= axle.k_droop * d * d
    return f_spring + axle.damp_c * axle.travel_vel


def _fz0(car, axle, other_arm):
    # other_arm: braco do OUTRO eixo (b para a dianteira, a para a traseira)
    c = car.chassis
    return c.mass_sprung * car.const.g * other_arm / c.wheelbase + axle.mass_unsprung * car.const.g


def _brake_demand(car, axle):
    inp = car.inputs
    pedal = inp.brake_pedal
    if axle.name == "front" and inp.line_lock and not axle.driven:
        pedal = max(pedal, 1.0)
    if axle.name == "rear" and inp.line_lock and not axle.driven:
        # FWD: line lock no eixo nao motriz. RWD nao entra aqui.
        pedal = max(pedal, 1.0)
    if axle.name == "rear" and inp.handbrake:
        pedal = max(pedal, 1.0)
    # RWD: line lock trava a dianteira mesmo se a flag driven estiver errada?
    # Spec fase 1: line lock = dianteira (nao motriz). Ja coberto acima.
    if axle.name == "front" and inp.line_lock and car.rear.driven and not car.front.driven:
        pedal = max(pedal, 1.0)
    return pedal * axle.t_brake_max


def _ieq_wheel(axle):
    i = axle.inertia_wheel
    if axle.driven:
        i += axle.inertia_drivetrain
    if i < 0.05:
        i = 0.05
    return i


def _shift_machine(car, dt):
    """Passo 2. Devolve throttle_eff (ainda sem idle)."""
    pt = car.pt
    inp = car.inputs
    pedal_target = clamp(1.0 - inp.clutch_pedal, 0.0, 1.0)
    throttle_eff = clamp(inp.throttle, 0.0, 1.0)

    if inp.shift_request and pt.shift_phase == "none" and pt.gear != inp.gear_target and inp.gear_target >= 1:
        if 1 <= inp.gear_target <= len(pt.gear_ratios):
            pt.shift_phase = "opening"
            pt.shift_timer = 0.0
            pt.eng0 = pt.clutch_engagement
    # consome a borda: o caller pode deixar True o frame inteiro
    # so a primeira aceitacao conta; frames seguintes com phase != none ignoram

    if pt.shift_phase == "opening":
        pt.shift_timer += dt
        u = clamp(pt.shift_timer / max(pt.t_open, 1e-4), 0.0, 1.0)
        pt.clutch_engagement = pt.eng0 * (1.0 - u)
        throttle_eff = 0.0
        if u >= 1.0:
            pt.shift_phase = "swapping"
            pt.shift_timer = 0.0
    elif pt.shift_phase == "swapping":
        pt.shift_timer += dt
        pt.gear = inp.gear_target
        pt.clutch_engagement = 0.0
        throttle_eff = 0.0
        if pt.shift_timer >= pt.t_swap:
            pt.shift_phase = "closing"
            pt.shift_timer = 0.0
            pt.eng0 = 0.0
    elif pt.shift_phase == "closing":
        pt.shift_timer += dt
        u = clamp(pt.shift_timer / max(pt.t_close, 1e-4), 0.0, 1.0)
        pt.clutch_engagement = lerp(0.0, pedal_target, u)
        g = car.ratio()
        if g > 0.0:
            mismatch = car.rear.omega_w * g - pt.omega_eng if car.rear.driven else car.front.omega_w * g - pt.omega_eng
            # rev-match: o eixo driven que esta no chao. Usa a media dos driven no ar/chao.
            w_driven = 0.0
            n = 0
            for a in car.driven_axles():
                w_driven += a.omega_w
                n += 1
            if n:
                w_driven /= n
            mismatch = w_driven * g - pt.omega_eng
            throttle_eff = clamp(car.const.k_rev * mismatch, 0.0, 1.0)
        if u >= 1.0:
            pt.shift_phase = "none"
            pt.shift_timer = 0.0
            pt.clutch_engagement = pedal_target
    else:
        pt.clutch_engagement = pedal_target

    return throttle_eff


def _engine_torque(car, throttle_eff, dt=0.0):
    """Passo 3. Distribuidor e carburador (3a/3b) antes da combustao."""
    pt = car.pt
    const = car.const
    if (not pt.engine_stalled) and pt.clutch_mode != "open" and pt.omega_eng <= pt.omega_stall:
        pt.engine_stalled = True

    t_comb = 0.0
    if not pt.engine_stalled:
        t_comb = float(car.torque_fn(w_to_rpm(pt.omega_eng), throttle_eff))
        if car.fuel is not None:
            t_comb *= fuel_step(car.fuel, w_to_rpm(pt.omega_eng), throttle_eff, dt)
        if pt.omega_eng > pt.redline:
            cut = clamp((pt.omega_eng - pt.redline) / (0.08 * pt.redline), 0.0, 1.0)
            t_comb *= (1.0 - cut)
    t_fric = const.k_fmep * pt.omega_eng + const.k_pump * (1.0 - throttle_eff)
    t_eng = t_comb - t_fric

    if pt.engine_stalled:
        t_comb = 0.0
        t_eng = -t_fric
        if car.inputs.ignition and car.inputs.clutch_pedal > 0.5:
            t_eng += pt.t_starter
            if pt.omega_eng > pt.omega_stall * 1.2:
                pt.engine_stalled = False
    pt.t_eng = t_eng
    return t_eng


def _clutch(car, throttle_eff_open_idle_already_applied):
    """Passo 4 e 5. Regime, T_clutch, integra omega_eng, projeta omega_w."""
    pt = car.pt
    const = car.const
    g = car.ratio()
    driven = car.driven_axles()

    # idle: embreagem aberta ou neutro. Roda DEPOIS da troca (throttle_eff ja veio de la).
    # O caller passa throttle_eff; aplicamos o piso de idle aqui se couber.
    # (feito no substep, antes desta funcao)

    if pt.engine_stalled or pt.clutch_engagement < 0.02 or g <= 0.0 or not driven:
        pt.clutch_mode = "open"
        pt.t_clutch = 0.0
        pt.t_load_eng = 0.0
        return "open"

    # T_load do substep anterior
    t_ext = 0.0
    for a in driven:
        t_ext += a.fx * a.radius + a.t_brake_eff
    pt.t_load_eng = t_ext / (g * max(pt.efficiency, 0.05))

    # omega do eixo driven de referencia (traseiro se houver)
    w_ref = driven[-1].omega_w
    delta = pt.omega_eng - w_ref * g
    capacity = pt.clutch_engagement * pt.clutch_capacity_max * const.fade_termico

    slip = pt.force_slip_next or abs(delta) > const.eps_clutch or abs(pt.t_load_eng) > capacity
    pt.force_slip_next = False

    if slip:
        pt.clutch_mode = "slip"
        pt.t_clutch = capacity * sgn(delta) if abs(delta) > 1e-9 else 0.0
        # se delta ~ 0 mas a carga estourou, o sinal da carga diz o sentido
        if abs(delta) <= 1e-9 and abs(pt.t_load_eng) > capacity:
            pt.t_clutch = capacity * sgn(pt.t_load_eng)
        return "slip"

    pt.clutch_mode = "stick"
    return "stick"


def _integrate_engine(car, dt):
    pt = car.pt
    g = car.ratio()
    driven = car.driven_axles()
    i_e = max(pt.inertia_eng, 0.02)

    for a in (car.front, car.rear):
        a.projected = False

    if pt.clutch_mode == "open" or g <= 0.0 or not driven:
        pt.clutch_mode = "open"
        pt.t_clutch = 0.0
        pt.omega_eng = max(0.0, pt.omega_eng + (pt.t_eng / i_e) * dt)
        return

    if pt.clutch_mode == "slip":
        # Complementaridade da embreagem (mesmo espírito do freio na §3.5):
        # o atrito cinético nao pode inverter delta_omega dentro do substep.
        # Euler explicito com T = capacity fazia a roda ultrapassar o motor e
        # o sinal virar no substep seguinte — a embreagem "motorizava" os dois lados.
        axle = driven[-1]
        n = len(driven)
        i_w = _ieq_wheel(axle)
        k = g * pt.efficiency / n
        delta = pt.omega_eng - axle.omega_w * g
        t_ext = axle.fx * axle.radius + axle.t_brake_eff
        coeff = dt * (1.0 / i_e + (g * k) / i_w)
        rhs = delta + pt.t_eng * dt / i_e + g * t_ext * dt / i_w
        t_sync = rhs / coeff if coeff > 1e-12 else 0.0
        if abs(delta) < 1e-9:
            pt.t_clutch = 0.0
        elif sgn(t_sync) == sgn(delta) and abs(t_sync) <= abs(pt.t_clutch):
            pt.t_clutch = t_sync
        pt.omega_eng = max(0.0, pt.omega_eng + ((pt.t_eng - pt.t_clutch) / i_e) * dt)
        # se este T zera o delta, crava a roda no motor e nao deixa o passo 9 cruzar
        if abs(delta) >= 1e-9 and abs(pt.t_clutch - t_sync) < 1e-6:
            w_sync = pt.omega_eng / g
            for a in driven:
                if not (a.stick_state and a.grounded):
                    a.omega_w = w_sync
                    a.projected = True
        return

    # stick
    i_w = 0.0
    for a in driven:
        i_w += _ieq_wheel(a)
    i_eq = i_e + i_w / (g * g)
    domega = (pt.t_eng - pt.t_load_eng) / i_eq
    pt.t_clutch = pt.t_eng - i_e * domega
    capacity = pt.clutch_engagement * pt.clutch_capacity_max * car.const.fade_termico
    if abs(pt.t_clutch) > capacity + 1e-6:
        pt.force_slip_next = True
        pt.t_clutch = clamp(pt.t_clutch, -capacity, capacity)
    pt.omega_eng = max(0.0, pt.omega_eng + domega * dt)

    proj = pt.omega_eng / g
    for a in driven:
        # duplo stick: a projecao NAO define omega_w (o solo ganha no passo 9)
        if a.stick_state and a.grounded:
            a.projected = False
        else:
            a.omega_w = proj
            a.projected = True


def _lock_driveline(car):
    """Pe fora da embreagem e pneu grudado: o giro nao foge da marcha.

    So corta o excesso com o carro ja andando. Parado, um snap para a lenta
    apagava o torque da largada.
    """
    pt = car.pt
    if pt.clutch_engagement < 0.9 or pt.engine_stalled:
        return
    g = car.ratio()
    driven = car.driven_axles()
    if g <= 0.0 or not driven:
        return
    ax = driven[-1]
    if not (ax.stick_state and ax.grounded):
        return
    if abs(car.chassis.vx) < 2.2:
        return
    target = (car.chassis.vx / ax.radius) * g
    if pt.omega_eng > target + rpm_to_w(400.0):
        pt.omega_eng = max(car.const.omega_idle, target)
        ax.omega_w = car.chassis.vx / ax.radius


def _t_wheel_for(car, axle):
    pt = car.pt
    g = car.ratio()
    n = len(car.driven_axles())
    if not axle.driven or n == 0 or g <= 0.0 or pt.clutch_mode == "open":
        return 0.0
    return (pt.t_clutch * g * pt.efficiency) / n


def _contact_front(car):
    """Passo 6, eixo dianteiro. Maquina de contato persistente."""
    c = car.chassis
    a = car.front
    const = car.const
    z_mount = c.z_cg + c.a_cg * math.sin(c.theta)
    prev_ground = a.grounded
    if a.grounded:
        a.z_u = a.radius
        a.v_zu = 0.0
        a.travel = a.l_free - (z_mount - a.radius)
        a.travel_vel = -(c.vz + c.a_cg * math.cos(c.theta) * c.omega_pitch)
        f_susp = _susp_force(a)
        f_neta = f_susp + a.mass_unsprung * const.g
        if f_neta < -const.eps_liftoff:
            a.grounded = False
            a.stick_state = False
            a.normal_load = 0.0
            a.fx = 0.0
            a.v_zu = 0.0
            # z_u permanece radius; o DOF integra no passo 12
        else:
            a.normal_load = max(0.0, f_neta)
    else:
        a.travel = a.l_free - (z_mount - a.z_u)
        a.travel_vel = -(c.vz + c.a_cg * math.cos(c.theta) * c.omega_pitch - a.v_zu)
        f_susp = _susp_force(a)
        a.normal_load = 0.0
        a.fx = 0.0
        a.stick_state = False
    if a.grounded != prev_ground:
        car.stats["ground_f"] += 1
    return f_susp


def _contact_rear(car):
    c = car.chassis
    a = car.rear
    const = car.const
    z_mount = c.z_cg - c.b_cg * math.sin(c.theta)
    a.z_u = a.radius
    a.grounded = True
    a.travel = a.l_free - (z_mount - a.radius)
    a.travel_vel = -(c.vz - c.b_cg * math.cos(c.theta) * c.omega_pitch)
    f_susp = _susp_force(a)
    a.normal_load = max(0.0, f_susp + a.mass_unsprung * const.g)
    if a.normal_load <= 0.0:
        a.fx = 0.0
        a.stick_state = False
    return f_susp


def _tire_force(car, axle, t_wheel, dt_unused):
    """Passo 7 de um eixo grounded com carga. Atualiza stick e fx."""
    const = car.const
    c = car.chassis
    tire = axle.tire
    prev = axle.stick_state
    vx = c.vx
    v_wheel = axle.omega_w * axle.radius
    kappa = (v_wheel - vx) / max(abs(vx), const.eps_stick)
    axle.kappa = kappa

    n = axle.normal_load
    if axle.name == "front":
        fz0 = _fz0(car, axle, c.b_cg)
    else:
        fz0 = _fz0(car, axle, c.a_cg)
    fz0 = max(fz0, 1.0)
    mu_load = 1.0 - tire.load_sensitivity * (n / fz0 - 1.0)
    mu_load = max(mu_load, 0.05)
    tf = temp_factor(tire.temp)
    mp = mu_psi(tire.psi, tire.psi_peak)
    mu_eff = tire.mu_base * mp * mu_load * tf
    mu_static_eff = tire.mu_static * mp * mu_load * tf
    axle.mu_eff = mu_eff
    axle.mu_static_eff = mu_static_eff

    i_w = _ieq_wheel(axle)
    t_demand = t_wheel - axle.t_brake_eff - i_w * c.a_x / axle.radius
    demand_f = t_demand / axle.radius

    if not axle.grounded or n <= 0.0:
        axle.stick_state = False
        axle.fx = 0.0
    else:
        if axle.stick_state:
            if abs(demand_f) > mu_static_eff * n:
                axle.stick_state = False
        else:
            v_slip = v_wheel - vx
            if abs(v_slip) < const.eps_stick and abs(demand_f) < 0.9 * mu_static_eff * n:
                axle.stick_state = True

        if axle.stick_state and axle.grounded:
            axle.omega_w = vx / axle.radius
            lim = mu_static_eff * n
            axle.fx = clamp(demand_f, -lim, lim)
            axle.kappa = 0.0
        else:
            slip_abs = abs(kappa)
            if slip_abs < 0.2:
                mu_curve = mu_static_eff * (slip_abs / 0.2)
            else:
                ratio = clamp((slip_abs - 0.2) / 0.5, 0.0, 1.0)
                mu_curve = mu_static_eff * (1.0 - ratio) + mu_eff * ratio
            axle.fx = mu_curve * n * sgn(kappa)

    # combined slip: F_y = 0 na fase 1, scale = 1. A formula fica para o teste.
    axle.fy = 0.0
    _apply_circle(axle)

    if axle.stick_state != prev:
        key = "stick_f" if axle.name == "front" else "stick_r"
        car.stats[key] += 1


def apply_circle(axle, fy):
    """Interface do traction circle. fy sintetico no teste; no jogo, 0."""
    axle.fy = fy
    _apply_circle(axle)


def _apply_circle(axle):
    mu_circle = axle.mu_static_eff if axle.stick_state else axle.mu_eff
    mag = math.sqrt(axle.fx * axle.fx + axle.fy * axle.fy + 1e-6)
    cap = mu_circle * axle.normal_load
    if mag > 1e-9:
        scale = min(1.0, cap / mag)
        axle.fx *= scale
        axle.fy *= scale


def _heat(car, axle, dt):
    tire = axle.tire
    v_slip = axle.omega_w * axle.radius - car.chassis.vx
    # stick: omega acabou de ser cravado em vx/r, v_slip ~ 0
    slip_power = abs(axle.fx * v_slip)
    tire.temp += (tire.k_heat * slip_power - tire.k_cool * (tire.temp - car.const.t_amb)) * dt
    if tire.temp < car.const.t_amb:
        tire.temp = car.const.t_amb
    if tire.temp > 220.0:
        tire.temp = 220.0


def _brake_integrate(car, axle, t_wheel, dt):
    """Passo 9. Stick, projecao, ou complementaridade."""
    const = car.const
    i_w = _ieq_wheel(axle)
    if axle.projected:
        # passo 5 ja cravou omega_w (embreagem colada, ou slip que zerou o delta).
        # o freio ainda entra na carga do motor no proximo substep.
        demand = _brake_demand(car, axle)
        if demand > 0.0 and abs(axle.omega_w) >= const.eps_lock:
            axle.t_brake_eff = demand * sgn(axle.omega_w)
        else:
            axle.t_brake_eff = 0.0
        return

    if axle.stick_state and axle.grounded:
        axle.omega_w = car.chassis.vx / axle.radius
        demand = _brake_demand(car, axle)
        if abs(car.chassis.vx) > const.eps_stick:
            axle.t_brake_eff = demand * sgn(axle.omega_w if abs(axle.omega_w) > 1e-9 else car.chassis.vx)
        else:
            raw = t_wheel - i_w * car.chassis.a_x / axle.radius
            axle.t_brake_eff = clamp(raw, -demand, demand)
        return

    if axle.driven and car.pt.clutch_mode == "stick":
        # embreagem colada: omega_w so muda pela projecao do passo 5 (§3.2/§3.5).
        # no substep em que o pneu ACABOU de quebrar, o passo 5 nao projetou
        # (o pneu ainda estava colado) — omega_w fica em vx/r e F_x deste
        # substep ja saiu ~0. o proximo substep projeta com T_load ~0 e o
        # giro sobe. nao integrar aqui: a projecao seguinte desfazia o spin
        # e o carro ficava sem forca.
        demand = _brake_demand(car, axle)
        if demand > 0.0 and abs(axle.omega_w) >= const.eps_lock:
            axle.t_brake_eff = demand * sgn(axle.omega_w)
        else:
            axle.t_brake_eff = 0.0
        return

    demand = _brake_demand(car, axle)
    omega_pred = axle.omega_w + (t_wheel - axle.fx * axle.radius) / i_w * dt
    t_stop = i_w * omega_pred / dt if dt > 0.0 else 0.0
    if demand > 0.0 and demand >= abs(t_stop):
        axle.omega_w = 0.0
        axle.t_brake_eff = t_stop
    else:
        if demand > 0.0 and abs(omega_pred) > 1e-12:
            s = sgn(omega_pred)
            axle.t_brake_eff = demand * s
            axle.omega_w = omega_pred - s * demand * dt / i_w
        else:
            axle.t_brake_eff = 0.0
            axle.omega_w = omega_pred
    if demand > 0.0 and abs(axle.omega_w) < const.eps_lock:
        axle.omega_w = 0.0


def _wheelie_bar(car):
    c = car.chassis
    b = car.bar
    if not b.equipped:
        b.bar_grounded = False
        b.f_bar = 0.0
        b.penetration = 0.0
        return 0.0
    z_tip = c.z_cg - (c.b_cg + b.d_bar) * math.sin(c.theta) - b.h_bar * math.cos(c.theta)
    prev = b.bar_grounded
    if z_tip <= 0.0:
        b.bar_grounded = True
        b.penetration = -z_tip
        v_tip = c.vz - ((c.b_cg + b.d_bar) * math.cos(c.theta) - b.h_bar * math.sin(c.theta)) * c.omega_pitch
        b.f_bar = max(0.0, b.k_bar * b.penetration - b.c_bar * v_tip)
    else:
        b.bar_grounded = False
        b.f_bar = 0.0
        b.penetration = 0.0
    if b.bar_grounded != prev:
        car.stats["bar"] += 1
    return b.f_bar


def _chassis_forces(car, f_susp_f, f_susp_r):
    c = car.chassis
    const = car.const
    vx = c.vx
    f_drag = 0.5 * const.rho * c.cda * vx * vx
    f_aero = 0.5 * const.rho * c.cla * vx * vx
    f_roll_f = crr_psi(car.front.tire.psi, car.front.tire.psi_peak) * car.front.normal_load if car.front.stick_state else 0.0
    f_roll_r = crr_psi(car.rear.tire.psi, car.rear.tire.psi_peak) * car.rear.normal_load if car.rear.stick_state else 0.0
    f_roll = f_roll_f + f_roll_r
    f_bar = _wheelie_bar(car)
    f_x = car.rear.fx + car.front.fx - sgn(vx) * (f_drag + f_roll)
    a_x = f_x / c.mass

    sum_fz = f_susp_f + f_susp_r - c.mass_sprung * const.g + f_aero + f_bar
    az = sum_fz / c.mass_sprung

    h_trac = c.z_cg - c.h_antisquat
    ct = math.cos(c.theta)
    t_react = c.s_eng * car.pt.t_clutch
    m_pitch = (
        car.rear.fx * h_trac
        + car.front.fx * c.z_cg
        + f_susp_f * c.a_cg * ct
        - f_susp_r * c.b_cg * ct
        + t_react
        + f_drag * (c.h_aero - c.z_cg)
        - f_aero * (c.x_cp - c.a_cg) * ct
        - f_bar * (c.b_cg + car.bar.d_bar) * ct
    )
    alpha = m_pitch / c.inertia_pitch
    car.f_drag = f_drag
    car.f_aero_z = f_aero
    car.f_rolling = f_roll
    return a_x, az, alpha


def _integrate_chassis(car, a_x, az, alpha, dt):
    c = car.chassis
    if c.capotou:
        c.omega_pitch = 0.0
        c.a_x = 0.0
        return
    c.vz += az * dt
    c.z_cg += c.vz * dt
    c.omega_pitch += alpha * dt
    c.theta += c.omega_pitch * dt
    c.vx += a_x * dt
    c.x += c.vx * dt
    c.a_x = a_x

    if (not car.bar.equipped) and c.theta > math.radians(60.0):
        c.theta = math.radians(60.0)
        c.omega_pitch = 0.0
        c.capotou = True


def _integrate_front_dof(car, f_susp_f, dt):
    a = car.front
    if a.grounded:
        return
    const = car.const
    # m*dv/dt = -F_susp - m*g   (F_susp do passo 6, mesmo no substep do lift-off)
    a_zu = (-f_susp_f - a.mass_unsprung * const.g) / a.mass_unsprung
    a.v_zu += a_zu * dt
    a.z_u += a.v_zu * dt
    if a.z_u <= a.radius and a.v_zu <= 0.0:
        a.z_u = a.radius
        a.v_zu = 0.0
        a.grounded = True
        car.stats["ground_f"] += 1


def substep(car, dt):
    # 1 entradas ja estao em car.inputs (psi aplicado aqui)
    car.front.tire.psi = car.inputs.psi_front
    car.rear.tire.psi = car.inputs.psi_rear

    # 2 troca
    throttle_eff = _shift_machine(car, dt)

    # idle por cima, so em ponto morto ou embreagem aberta, e fora de uma troca
    # (na abertura da troca o throttle_eff tem de ficar em 0 — §3.10)
    pt = car.pt
    if pt.shift_phase == "none" and (pt.clutch_engagement < 0.05 or pt.gear < 1):
        idle = clamp(car.const.k_idle * (car.const.omega_idle - pt.omega_eng), 0.0, 0.15)
        throttle_eff = max(throttle_eff, idle)
    car.throttle_eff = throttle_eff

    # 3 torque (3a distribuidor, 3b carburador, 3c combustao)
    _engine_torque(car, throttle_eff, dt)

    # 4 regime (usa fx e freio do substep anterior)
    _clutch(car, throttle_eff)

    # 5 integra motor e projeta roda se nao for duplo stick
    _integrate_engine(car, dt)

    # 6 contato
    f_susp_f = _contact_front(car)
    f_susp_r = _contact_rear(car)

    # 7 pneu
    t_front = _t_wheel_for(car, car.front)
    t_rear = _t_wheel_for(car, car.rear)
    car.t_wheel_front = t_front
    car.t_wheel_rear = t_rear
    if car.front.grounded and car.front.normal_load > 0.0:
        _tire_force(car, car.front, t_front, dt)
    else:
        car.front.fx = 0.0
        car.front.kappa = 0.0
    if car.rear.normal_load > 0.0:
        _tire_force(car, car.rear, t_rear, dt)
    else:
        car.rear.fx = 0.0
        car.rear.kappa = 0.0
        car.rear.stick_state = False

    # 8 temperatura (fx deste substep)
    _heat(car, car.front, dt)
    _heat(car, car.rear, dt)

    # 9 roda + freio
    _brake_integrate(car, car.front, t_front, dt)
    _brake_integrate(car, car.rear, t_rear, dt)
    _lock_driveline(car)

    # 10-11 chassi
    a_x, az, alpha = _chassis_forces(car, f_susp_f, f_susp_r)
    _integrate_chassis(car, a_x, az, alpha, dt)

    # 12 DOF dianteiro
    _integrate_front_dof(car, f_susp_f, dt)

    if car.trace_sub:
        car.subtrace.append({
            "vx": car.chassis.vx,
            "x": car.chassis.x,
            "rpm": w_to_rpm(car.pt.omega_eng),
            "fx_r": car.rear.fx,
            "fx_f": car.front.fx,
            "n_r": car.rear.normal_load,
            "n_f": car.front.normal_load,
            "stick_r": car.rear.stick_state,
            "stick_f": car.front.stick_state,
            "ground_f": car.front.grounded,
            "z_u": car.front.z_u,
            "theta": car.chassis.theta,
            "z_cg": car.chassis.z_cg,
            "bar": car.bar.bar_grounded,
            "f_bar": car.bar.f_bar,
            "pen": car.bar.penetration,
            "clutch": car.pt.clutch_mode,
            "t_clutch": car.pt.t_clutch,
            "t_wheel_r": t_rear,
            "gear": car.pt.gear,
            "phase": car.pt.shift_phase,
            "stall": car.pt.engine_stalled,
            "temp_r": car.rear.tire.temp,
            "temp_f": car.front.tire.temp,
            "w_r": car.rear.omega_w,
            "w_f": car.front.omega_w,
            "kappa_r": car.rear.kappa,
            "a_x": car.chassis.a_x,
        })


def step(car, dt):
    """dt em segundos. Ate n_sub substeps (teto da spec: 4)."""
    if dt <= 0.0:
        return
    n = car.n_sub
    if n < 1:
        n = 1
    if n > 4:
        n = 4
    h = dt / n
    # a borda de troca vale o frame, nao cada substep
    requested = car.inputs.shift_request
    for i in range(n):
        if i > 0:
            car.inputs.shift_request = False
        substep(car, h)
    car.inputs.shift_request = False
    if requested:
        pass
    _safety(car)


def _safety(car):
    c = car.chassis
    if c.vx > 150.0 or c.vx < -150.0:
        car.stats["clamp_vx"] += 1
        c.vx = clamp(c.vx, -150.0, 150.0)
    if c.vz > 30.0 or c.vz < -30.0:
        car.stats["clamp_vz"] += 1
        c.vz = clamp(c.vz, -30.0, 30.0)
    if c.omega_pitch > 20.0 or c.omega_pitch < -20.0:
        car.stats["clamp_w"] += 1
        c.omega_pitch = clamp(c.omega_pitch, -20.0, 20.0)


def settle_springs(car):
    """L_free para o equilibrio estatico sentar em z_cg = h_cg, theta = 0."""
    c = car.chassis
    g = car.const.g
    f_front = c.mass_sprung * g * c.b_cg / c.wheelbase
    f_rear = c.mass_sprung * g * c.a_cg / c.wheelbase
    # travel de equilibrio dentro do curso, longe dos batentes
    for axle, f_eq in ((car.front, f_front), (car.rear, f_rear)):
        travel = f_eq / axle.spring_k
        # se o curso nao comporta, avisa pelo proprio travel (o teste de equilibrio pega)
        axle.l_free = travel + c.h_cg - axle.radius
        axle.travel = travel
        axle.z_u = axle.radius
        axle.normal_load = f_eq + axle.mass_unsprung * g
        axle.grounded = True
        axle.stick_state = True
        axle.v_zu = 0.0
        axle.fx = 0.0
        axle.t_brake_eff = 0.0
    c.z_cg = c.h_cg
    c.theta = 0.0
    c.vz = 0.0
    c.omega_pitch = 0.0


def make_axle(name, driven, radius, tire, mass_u, spring_k, **kw):
    travel_max = kw.get("travel_max", 0.14)
    travel_min = kw.get("travel_min", -0.10)
    return Axle(
        name=name,
        driven=driven,
        radius=radius,
        tire=tire,
        mass_unsprung=mass_u,
        inertia_wheel=kw.get("inertia_wheel", 2.0),
        t_brake_max=kw.get("t_brake_max", 3600.0 if name == "front" else 2400.0),
        inertia_drivetrain=kw.get("inertia_drivetrain", 0.10 if driven else 0.0),
        spring_k=spring_k,
        damp_c=kw.get("damp_c", 0.35 * 2.0 * math.sqrt(spring_k * max(mass_u, 50.0))),
        l_free=radius + 0.3,
        travel_max=travel_max,
        travel_min=travel_min,
        k_bump=kw.get("k_bump", 100.0 * spring_k),
        k_droop=kw.get("k_droop", 100.0 * spring_k),
        z_u=radius,
    )


def street_500(torque_fn=None, **overrides):
    """Classe de aceitacao: street ~500 hp, drag radial atras, ~1500 kg, RWD."""
    const = Const()
    mass = overrides.get("mass", 1500.0)
    mu_f = overrides.get("mu_front", 70.0)
    mu_r = overrides.get("mu_rear", 80.0)
    sprung = mass - mu_f - mu_r
    L = overrides.get("wheelbase", 2.65)
    a = overrides.get("a_cg", 1.45)  # CG mais para tras: mais peso na traseira
    b = L - a
    h = overrides.get("h_cg", 0.48)
    h_body = overrides.get("h_body", 0.50)
    radius = overrides.get("radius", 0.33)
    i_pitch = overrides.get("inertia_pitch", sprung * (L * L + h_body * h_body) / 12.0)

    # rigidez para ~8 cm de curso estatico no eixo mais carregado
    f_rear = sprung * const.g * a / L
    f_front = sprung * const.g * b / L
    k_r = f_rear / 0.08
    k_f = f_front / 0.08
    # amortecimento no corpo, nao na massa nao suspensa (evita c*dt/m estourar)
    c_f = 0.4 * 2.0 * math.sqrt(k_f * sprung * b / L)
    c_r = 0.4 * 2.0 * math.sqrt(k_r * sprung * a / L)

    # k_heat calibrado para burnout PARADO (pneu frio, mu baixo) chegar a ~90 C em 5 s.
    # k_cool: em ~15 s sem slip a temp cai de forma perceptivel e nao zera.
    k_heat = overrides.get("k_heat", 1.35e-4)
    k_cool = overrides.get("k_cool", 0.042)

    tire_f = Tire(0.95, 1.15, 0.12, 32.0, k_heat, k_cool, temp=const.t_amb, psi=32.0)
    tire_r = Tire(1.70, 2.00, 0.10, 10.0, k_heat, k_cool, temp=const.t_amb, psi=12.0)

    front = make_axle("front", False, radius, tire_f, mu_f, k_f, damp_c=c_f, t_brake_max=3600.0)
    rear = make_axle("rear", True, radius, tire_r, mu_r, k_r, damp_c=c_r, t_brake_max=2400.0,
                      inertia_drivetrain=0.10)

    # relacoes totais (cambio * diferencial), 1a curta o bastante para 60 pes
    # sem passar do mu do drag radial num clutch dump a ~4500 rpm
    ratios = overrides.get("ratios", [8.6, 5.9, 4.35, 3.35, 2.70])
    pt = Powertrain(
        omega_eng=rpm_to_w(800.0),
        inertia_eng=overrides.get("inertia_eng", 0.28),
        clutch_capacity_max=overrides.get("clutch_cap", 780.0),
        gear=1,
        gear_ratios=list(ratios),
        efficiency=0.90,
        redline=rpm_to_w(overrides.get("redline_rpm", 6800.0)),
    )
    bar = WheelieBar(
        equipped=overrides.get("bar", True),
        d_bar=overrides.get("d_bar", 0.35),
        h_bar=h - 0.05,
        k_bar=overrides.get("k_bar", 3.0e5),
        c_bar=overrides.get("c_bar", 0.15 * 3.0e5),
    )
    chassis = Chassis(
        mass=mass,
        mass_sprung=sprung,
        h_body=h_body,
        inertia_pitch=i_pitch,
        h_cg=h,
        a_cg=a,
        b_cg=b,
        h_antisquat=overrides.get("h_antisquat", 0.0),
        s_eng=overrides.get("s_eng", 0.0),
        h_aero=overrides.get("h_aero", h),
        x_cp=overrides.get("x_cp", a),
        wheelbase=L,
        # CdA de um street com drag radial e asa: o gate da §7 (11,5–12,0 s @ ~190)
        # sai daqui. CdA menor deixa o mesmo motor rapido demais no quarto de milha.
        cda=overrides.get("cda", 1.30),
        cla=overrides.get("cla", -0.20),
        z_cg=h,
    )
    if torque_fn is None:
        torque_fn = torque_500
    car = Car(
        const=const,
        chassis=chassis,
        front=front,
        rear=rear,
        pt=pt,
        bar=bar,
        inputs=Inputs(psi_front=32.0, psi_rear=12.0, gear_target=1, clutch_pedal=1.0),
        torque_fn=torque_fn,
    )
    settle_springs(car)
    # h_bar depende de h_cg; a ponta fica ~5 cm acima do solo em repouso
    car.bar.h_bar = car.chassis.h_cg - 0.05
    return car


def torque_500(rpm, throttle):
    """Curva WOT ~500 hp a 6000 rpm, pico de torque ~680 Nm a 3500."""
    pts = (
        (0.0, 0.0),
        (600.0, 80.0),
        (800.0, 210.0),
        (2000.0, 540.0),
        (3500.0, 680.0),
        (5000.0, 650.0),
        (6000.0, 593.0),
        (6800.0, 470.0),
        (7600.0, 180.0),
    )
    if rpm <= pts[0][0]:
        t = pts[0][1]
    elif rpm >= pts[-1][0]:
        t = pts[-1][1]
    else:
        t = pts[-1][1]
        for i in range(1, len(pts)):
            r0, t0 = pts[i - 1]
            r1, t1 = pts[i]
            if rpm <= r1:
                u = (rpm - r0) / (r1 - r0)
                t = t0 + (t1 - t0) * u
                break
    return t * clamp(throttle, 0.0, 1.0)


def ieq_engine(car):
    g = car.ratio()
    if g <= 0.0:
        return car.pt.inertia_eng
    i_w = 0.0
    for a in car.driven_axles():
        i_w += _ieq_wheel(a)
    return car.pt.inertia_eng + i_w / (g * g)
