// phys_step v2.8 — mesmo modelo de mod/phys_v28.py (spec MCML v2.8).
// Longitudinal + pitch + DOF dianteiro. F_y = 0. Sem marcha a re.
// A ordem dos passos e os lags de 1 substep fazem parte do modelo.

global.phys_v28_on = 1;
global.phys_moved = 0;
global.phys_hit = 0;
global.phys_scroll = 0;
global.phys_kmh_used = 0;
global.phys_in_up = 0;
global.phys_in_down = 0;
global.phys_in_clutch = 0;
global.phys_in_brake = 0;
global.phys_in_clutch_t = 0;
global.phys_in_borra = 0;

global.phys_v28_sgn = function(_x) {
    if (_x > 0) return 1;
    if (_x < 0) return -1;
    return 0;
};

global.phys_v28_tempf = function(_t) {
    if (_t <= 20 || _t >= 140) return 0.70;
    if (_t < 90) return lerp(0.70, 1.00, (_t - 20) / 70);
    return lerp(1.00, 0.70, (_t - 90) / 50);
};

global.phys_v28_psin = function(_psi, _peak) {
    return clamp((_psi - _peak + 15) / 15, 0, 1);
};

global.phys_v28_mupsi = function(_psi, _peak) {
    return lerp(0.85, 1.05, global.phys_v28_psin(_psi, _peak));
};

global.phys_v28_crr = function(_psi, _peak) {
    return lerp(0.015, 0.008, global.phys_v28_psin(_psi, _peak));
};

global.phys_v28_axle = function(_name, _driven, _radius, _mu, _mu_s, _peak, _mu_u, _k, _c, _tbrake) {
    return {
        name: _name, driven: _driven, radius: _radius,
        mu_base: _mu, mu_static: _mu_s, load_sens: (_name == "rear") ? 0.10 : 0.12,
        psi_peak: _peak, k_heat: 0.000135, k_cool: 0.042,
        temp: 25, psi: _peak,
        mass_u: _mu_u, i_wheel: 2.0, t_brake_max: _tbrake,
        i_drive: _driven ? 0.10 : 0,
        spring_k: _k, damp_c: _c, l_free: _radius + 0.3,
        travel_max: 0.14, travel_min: -0.10,
        k_bump: 100 * _k, k_droop: 100 * _k,
        omega: 0, travel: 0.08, travel_vel: 0,
        normal: 0, fx: 0, fy: 0,
        grounded: 1, stick: 1, z_u: _radius, v_zu: 0,
        t_brake: 0, projected: 0, kappa: 0,
        mu_eff: _mu, mu_static_eff: _mu_s
    };
};

global.phys_v28_reset = function() {
    var _m = global.weight;
    if (is_undefined(_m) || _m <= 0) _m = 1.4;
    if (_m < 50) _m *= 1000;
    if (_m < 400) _m = 1400;
    var _tr = 2;
    if (variable_global_exists("car_data") && variable_global_exists("vehicles_array")) {
        _tr = global.car_data[global.vehicles_array].tracao;
    }
    var _uf = 70;
    var _ur = 80;
    var _ms = _m - _uf - _ur;
    var _L = 2.60;
    var _a = (_tr == 1) ? (_L * 0.46) : (_L * 0.55);
    var _b = _L - _a;
    var _h = 0.50;
    var _r = 0.31;
    if (variable_global_exists("phys_def")) _r = global.phys_def.raio;
    var _g = 9.81;
    var _ff = _ms * _g * _b / _L;
    var _fr = _ms * _g * _a / _L;
    var _kf = _ff / 0.08;
    var _kr = _fr / 0.08;
    var _cf = 0.4 * 2 * sqrt(_kf * _ms * _b / _L);
    var _cr = 0.4 * 2 * sqrt(_kr * _ms * _a / _L);
    var _pneu = 0;
    if (variable_global_exists("car_data")) _pneu = global.car_data[global.vehicles_array].pneu;
    var _mu_f = 0.95;
    var _ms_f = 1.15;
    var _mu_r = 0.95;
    var _ms_r = 1.15;
    var _pk_r = 32;
    if (_pneu == 1) { _mu_f = 1.05; _ms_f = 1.25; _mu_r = 1.05; _ms_r = 1.25; }
    if (_pneu == 2) { _mu_r = 1.70; _ms_r = 2.00; _pk_r = 12; }
    if (_pneu == 3) { _mu_f = 0.70; _ms_f = 0.85; _mu_r = 0.70; _ms_r = 0.85; }
    var _drv_f = (_tr == 1 || _tr == 3) ? 1 : 0;
    var _drv_r = (_tr != 1) ? 1 : 0;
    var _front = global.phys_v28_axle("front", _drv_f, _r, _mu_f, _ms_f, 32, _uf, _kf, _cf, 3600);
    var _rear = global.phys_v28_axle("rear", _drv_r, _r, _mu_r, _ms_r, _pk_r, _ur, _kr, _cr, 2400);
    _rear.psi = _pk_r;
    var _hbar = _h - 0.05;
    global.phys = {
        held: 1, car: global.vehicles_array, mass: _m, sprung: _ms,
        ix: _ms * (_L * _L + 0.25) / 12, h: _h, a: _a, b: _b, L: _L,
        has: 0, seng: (_tr == 1) ? -1 : 0, haero: _h, xcp: _a,
        cda: 1.0, cla: 0,
        z: _h, theta: 0, vz: 0, wp: 0, vx: 0, ax: 0, capotou: 0,
        front: _front, rear: _rear,
        bar_on: 1, d_bar: 0.35, h_bar: _hbar, k_bar: 300000, c_bar: 45000,
        bar_g: 0, f_bar: 0, pen: 0,
        omega: 83.8, i_eng: 0.28, eng: 0, cap: 800, gear: 1,
        ratios: [8], eff: 0.90, phase: "none", stimer: 0,
        t_open: 0.08, t_swap: 0.04, t_close: 0.12,
        mode: "open", w_stall: 36.7, stalled: 0, t_start: 80,
        tcl: 0, teng: 0, tload: 0, red: 712, slip_next: 0, eng0: 0,
        thr: 0, twf: 0, twr: 0,
        pedal: 1, thr_in: 0, brake: 0, lock: 0, hb: 0,
        gear_target: 1, shift_req: 0, ign: 1, engine_was: 0, psi_f: 32, psi_r: _pk_r,
        g: 9.81, rho: 1.225, k_fmep: 0.05, k_pump: 25, k_idle: 0.015,
        w_idle: 83.8, t_amb: 25, k_rev: 0.01,
        eps_c: 1.0, eps_s: 0.2, eps_l: 0.5, eps_up: 10,
        n_driven: _drv_f + _drv_r
    };
    global.phys_v28_settle();
};

global.phys_v28_settle = function() {
    var p = global.phys;
    var _ff = p.sprung * p.g * p.b / p.L;
    var _fr = p.sprung * p.g * p.a / p.L;
    var _ax = p.front;
    var _ay = p.rear;
    _ax.l_free = (_ff / _ax.spring_k) + p.h - _ax.radius;
    _ay.l_free = (_fr / _ay.spring_k) + p.h - _ay.radius;
    _ax.travel = _ff / _ax.spring_k;
    _ay.travel = _fr / _ay.spring_k;
    _ax.normal = _ff + _ax.mass_u * p.g;
    _ay.normal = _fr + _ay.mass_u * p.g;
    _ax.z_u = _ax.radius;
    _ay.z_u = _ay.radius;
    _ax.grounded = 1;
    _ay.grounded = 1;
    _ax.stick = 1;
    _ay.stick = 1;
    p.z = p.h;
    p.theta = 0;
};

global.phys_v28_ratio = function() {
    var p = global.phys;
    if (p.gear < 1) return 0;
    if (p.gear > array_length(p.ratios)) return 0;
    return p.ratios[p.gear - 1];
};

global.phys_v28_iw = function(_a) {
    var _i = _a.i_wheel + _a.i_drive;
    if (_i < 0.05) return 0.05;
    return _i;
};

global.phys_v28_fsusp = function(_a) {
    var _f = _a.spring_k * _a.travel;
    if (_a.travel > _a.travel_max) {
        var _d = _a.travel - _a.travel_max;
        _f += _a.k_bump * _d * _d;
    }
    if (_a.travel < _a.travel_min) {
        var _e = _a.travel_min - _a.travel;
        _f -= _a.k_droop * _e * _e;
    }
    return _f + _a.damp_c * _a.travel_vel;
};

global.phys_v28_bdemand = function(_a) {
    var p = global.phys;
    var _ped = p.brake;
    if (_a.name == "front" && p.lock && !_a.driven) _ped = max(_ped, 1);
    if (_a.name == "rear" && p.lock && !_a.driven) _ped = max(_ped, 1);
    if (_a.name == "rear" && p.hb) _ped = max(_ped, 1);
    return _ped * _a.t_brake_max;
};

global.phys_v28_sub = function(_dt) {
    var p = global.phys;
    var _f = p.front;
    var _r = p.rear;
    _f.psi = p.psi_f;
    _r.psi = p.psi_r;
    _f.projected = 0;
    _r.projected = 0;

    // 2 troca
    var _pedal_tgt = clamp(1 - p.pedal, 0, 1);
    var _thr = clamp(p.thr_in, 0, 1);
    if (p.shift_req && p.phase == "none" && p.gear != p.gear_target && p.gear_target >= 1) {
        if (p.gear_target <= array_length(p.ratios)) {
            p.phase = "opening";
            p.stimer = 0;
            p.eng0 = p.eng;
        }
    }
    if (p.phase == "opening") {
        p.stimer += _dt;
        var _u = clamp(p.stimer / max(p.t_open, 0.0001), 0, 1);
        p.eng = p.eng0 * (1 - _u);
        _thr = 0;
        if (_u >= 1) { p.phase = "swapping"; p.stimer = 0; }
    } else if (p.phase == "swapping") {
        p.stimer += _dt;
        p.gear = p.gear_target;
        p.eng = 0;
        _thr = 0;
        if (p.stimer >= p.t_swap) { p.phase = "closing"; p.stimer = 0; p.eng0 = 0; }
    } else if (p.phase == "closing") {
        p.stimer += _dt;
        var _u2 = clamp(p.stimer / max(p.t_close, 0.0001), 0, 1);
        p.eng = lerp(0, _pedal_tgt, _u2);
        var _gg = global.phys_v28_ratio();
        if (_gg > 0) {
            var _wd = _r.driven ? _r.omega : _f.omega;
            _thr = clamp(p.k_rev * (_wd * _gg - p.omega), 0, 1);
        }
        if (_u2 >= 1) { p.phase = "none"; p.stimer = 0; p.eng = _pedal_tgt; }
    } else {
        p.eng = _pedal_tgt;
    }
    if (p.phase == "none" && (p.eng < 0.05 || p.gear < 1)) {
        var _idle = clamp(p.k_idle * (p.w_idle - p.omega), 0, 0.15);
        _thr = max(_thr, _idle);
    }
    p.thr = _thr;

    // 3 torque
    if (!p.stalled && p.mode != "open" && p.omega <= p.w_stall) p.stalled = 1;
    var _tcomb = 0;
    if (!p.stalled) {
        _tcomb = global.torque * _thr;
        if (variable_global_exists("fuel_model_on") && global.fuel_model_on) {
            global.fuel_sys_step(p.omega * 9.5492965855, _thr, _dt);
            _tcomb = global.torque * _thr * global.fuel_sys.eff;
        }
        if (is_undefined(_tcomb)) _tcomb = 0;
        if (p.omega > p.red) {
            var _cut = clamp((p.omega - p.red) / (0.08 * p.red), 0, 1);
            _tcomb *= (1 - _cut);
        }
    }
    var _tfric = p.k_fmep * p.omega + p.k_pump * (1 - _thr);
    var _teng = _tcomb - _tfric;
    if (p.stalled) {
        _teng = -_tfric;
        if (p.ign && p.pedal > 0.5) {
            _teng += p.t_start;
            if (p.omega > p.w_stall * 1.2) p.stalled = 0;
        }
    }
    p.teng = _teng;

    // 4 regime
    var _g = global.phys_v28_ratio();
    var _nd = (_f.driven ? 1 : 0) + (_r.driven ? 1 : 0);
    p.n_driven = _nd;
    var _ie = max(p.i_eng, 0.02);
    if (p.stalled || p.eng < 0.02 || _g <= 0 || _nd == 0) {
        p.mode = "open";
        p.tcl = 0;
        p.tload = 0;
    } else {
        var _text = 0;
        if (_f.driven) _text += _f.fx * _f.radius + _f.t_brake;
        if (_r.driven) _text += _r.fx * _r.radius + _r.t_brake;
        p.tload = _text / (_g * max(p.eff, 0.05));
        var _wref = _r.driven ? _r.omega : _f.omega;
        var _delta = p.omega - _wref * _g;
        var _cap = p.eng * p.cap;
        // pe fora, a embreagem segura o torque deste giro. se a capacidade
        // ficar abaixo do motor, o giro foge para o corte com o carro ainda lento.
        if (p.eng >= 0.9 && abs(p.teng) > _cap) _cap = abs(p.teng) * 1.2;
        var _slip = p.slip_next || abs(_delta) > p.eps_c || abs(p.tload) > _cap;
        p.slip_next = 0;
        if (_slip) {
            p.mode = "slip";
            p.tcl = _cap * global.phys_v28_sgn(_delta);
            if (abs(_delta) <= 0.000001 && abs(p.tload) > _cap) p.tcl = _cap * global.phys_v28_sgn(p.tload);
        } else {
            p.mode = "stick";
        }
    }

    // 5 motor
    if (p.mode == "open" || _g <= 0 || _nd == 0) {
        p.mode = "open";
        p.tcl = 0;
        p.omega = max(0, p.omega + (p.teng / _ie) * _dt);
    } else if (p.mode == "slip") {
        var _axr = _r.driven ? _r : _f;
        var _iw = global.phys_v28_iw(_axr);
        var _k = _g * p.eff / _nd;
        var _del = p.omega - _axr.omega * _g;
        var _ext = _axr.fx * _axr.radius + _axr.t_brake;
        var _coeff = _dt * (1 / _ie + (_g * _k) / _iw);
        var _rhs = _del + p.teng * _dt / _ie + _g * _ext * _dt / _iw;
        var _tsync = (_coeff > 0.0000001) ? (_rhs / _coeff) : 0;
        if (abs(_del) < 0.000001) p.tcl = 0;
        else if (global.phys_v28_sgn(_tsync) == global.phys_v28_sgn(_del) && abs(_tsync) <= abs(p.tcl)) p.tcl = _tsync;
        p.omega = max(0, p.omega + ((p.teng - p.tcl) / _ie) * _dt);
        if (abs(_del) >= 0.000001 && abs(p.tcl - _tsync) < 0.000001) {
            var _ws = p.omega / _g;
            if (_f.driven && !(_f.stick && _f.grounded)) { _f.omega = _ws; _f.projected = 1; }
            if (_r.driven && !(_r.stick && _r.grounded)) { _r.omega = _ws; _r.projected = 1; }
        }
    } else {
        var _iwsum = 0;
        if (_f.driven) _iwsum += global.phys_v28_iw(_f);
        if (_r.driven) _iwsum += global.phys_v28_iw(_r);
        var _ieq = _ie + _iwsum / (_g * _g);
        var _dom = (p.teng - p.tload) / _ieq;
        var _cap2 = p.eng * p.cap;
        if (p.eng >= 0.9 && abs(p.teng) > _cap2) _cap2 = abs(p.teng) * 1.2;
        p.tcl = p.teng - _ie * _dom;
        if (abs(p.tcl) > _cap2 + 0.000001) {
            p.slip_next = 1;
            p.tcl = clamp(p.tcl, -_cap2, _cap2);
        }
        p.omega = max(0, p.omega + _dom * _dt);
        var _proj = p.omega / _g;
        if (_f.driven && !(_f.stick && _f.grounded)) { _f.omega = _proj; _f.projected = 1; }
        if (_r.driven && !(_r.stick && _r.grounded)) { _r.omega = _proj; _r.projected = 1; }
    }

    // 6 contato
    var _ct = cos(p.theta);
    var _st = sin(p.theta);
    var _zmf = p.z + p.a * _st;
    var _fsup_f = 0;
    if (_f.grounded) {
        _f.z_u = _f.radius;
        _f.v_zu = 0;
        _f.travel = _f.l_free - (_zmf - _f.radius);
        _f.travel_vel = -(p.vz + p.a * _ct * p.wp);
        _fsup_f = global.phys_v28_fsusp(_f);
        var _neta = _fsup_f + _f.mass_u * p.g;
        if (_neta < -p.eps_up) {
            _f.grounded = 0;
            _f.stick = 0;
            _f.normal = 0;
            _f.fx = 0;
            _f.v_zu = 0;
        } else {
            _f.normal = max(0, _neta);
        }
    } else {
        _f.travel = _f.l_free - (_zmf - _f.z_u);
        _f.travel_vel = -(p.vz + p.a * _ct * p.wp - _f.v_zu);
        _fsup_f = global.phys_v28_fsusp(_f);
        _f.normal = 0;
        _f.fx = 0;
        _f.stick = 0;
    }
    var _zmr = p.z - p.b * _st;
    _r.z_u = _r.radius;
    _r.grounded = 1;
    _r.travel = _r.l_free - (_zmr - _r.radius);
    _r.travel_vel = -(p.vz - p.b * _ct * p.wp);
    var _fsup_r = global.phys_v28_fsusp(_r);
    _r.normal = max(0, _fsup_r + _r.mass_u * p.g);
    if (_r.normal <= 0) { _r.fx = 0; _r.stick = 0; }

    // 7 pneu — sem funcao anonima (o runner nao captura var de fora)
    p.twf = 0;
    p.twr = 0;
    if (p.n_driven > 0 && _g > 0 && p.mode != "open") {
        var _share = (p.tcl * _g * p.eff) / p.n_driven;
        if (_f.driven) p.twf = _share;
        if (_r.driven) p.twr = _share;
    }
    global.phys_v28_tire(_f, p.twf);
    global.phys_v28_tire(_r, p.twr);

    // 8 calor
    global.phys_v28_heat(_f, _dt);
    global.phys_v28_heat(_r, _dt);

    // 9 roda
    global.phys_v28_wheel(_f, p.twf, _dt);
    global.phys_v28_wheel(_r, p.twr, _dt);
    // pe fora, pneu grudado e carro ja andando: corta giro que fugiu da marcha
    if (p.eng >= 0.9 && !p.stalled && _g > 0 && abs(p.vx) >= 2.2) {
        var _axl = _r.driven ? _r : _f;
        if (_axl.stick && _axl.grounded) {
            var _alvo = (p.vx / _axl.radius) * _g;
            if (p.omega > _alvo + 41.9) {
                if (_alvo < p.w_idle) _alvo = p.w_idle;
                p.omega = _alvo;
                _axl.omega = p.vx / _axl.radius;
            }
        }
    }

    // 10-11 chassi
    var _vd = p.vx * p.vx;
    var _fdrag = 0.5 * p.rho * p.cda * _vd;
    var _faero = 0.5 * p.rho * p.cla * _vd;
    var _froll = 0;
    if (_f.stick) _froll += global.phys_v28_crr(_f.psi, _f.psi_peak) * _f.normal;
    if (_r.stick) _froll += global.phys_v28_crr(_r.psi, _r.psi_peak) * _r.normal;
    var _fbar = 0;
    if (p.bar_on) {
        var _ztip = p.z - (p.b + p.d_bar) * _st - p.h_bar * _ct;
        if (_ztip <= 0) {
            p.bar_g = 1;
            p.pen = -_ztip;
            var _vtip = p.vz - ((p.b + p.d_bar) * _ct - p.h_bar * _st) * p.wp;
            _fbar = max(0, p.k_bar * p.pen - p.c_bar * _vtip);
        } else {
            p.bar_g = 0;
            p.pen = 0;
            _fbar = 0;
        }
    } else {
        p.bar_g = 0;
        _fbar = 0;
        p.pen = 0;
    }
    p.f_bar = _fbar;
    var _fx = _r.fx + _f.fx - global.phys_v28_sgn(p.vx) * (_fdrag + _froll);
    var _axx = _fx / p.mass;
    var _sumz = _fsup_f + _fsup_r - p.sprung * p.g + _faero + _fbar;
    var _az = _sumz / p.sprung;
    var _htr = p.z - p.has;
    var _treact = p.seng * p.tcl;
    var _mp = _r.fx * _htr + _f.fx * p.z + _fsup_f * p.a * _ct - _fsup_r * p.b * _ct + _treact + _fdrag * (p.haero - p.z) - _faero * (p.xcp - p.a) * _ct - _fbar * (p.b + p.d_bar) * _ct;
    var _alph = _mp / p.ix;
    if (!p.capotou) {
        p.vz += _az * _dt;
        p.z += p.vz * _dt;
        p.wp += _alph * _dt;
        p.theta += p.wp * _dt;
        p.vx += _axx * _dt;
        p.ax = _axx;
        if (!p.bar_on && p.theta > 1.0472) {
            p.theta = 1.0472;
            p.wp = 0;
            p.capotou = 1;
        }
    } else {
        p.wp = 0;
        p.ax = 0;
    }

    // 12 DOF dianteiro
    if (!_f.grounded) {
        var _azu = (-_fsup_f - _f.mass_u * p.g) / _f.mass_u;
        _f.v_zu += _azu * _dt;
        _f.z_u += _f.v_zu * _dt;
        if (_f.z_u <= _f.radius && _f.v_zu <= 0) {
            _f.z_u = _f.radius;
            _f.v_zu = 0;
            _f.grounded = 1;
        }
    }
};

global.phys_v28_tire = function(_a, _tw) {
    var p = global.phys;
    if (!_a.grounded || _a.normal <= 0) {
        _a.stick = 0;
        _a.fx = 0;
        _a.kappa = 0;
        return;
    }
    var _vw = _a.omega * _a.radius;
    var _k = (_vw - p.vx) / max(abs(p.vx), p.eps_s);
    _a.kappa = _k;
    var _fz0 = p.sprung * p.g * ((_a.name == "front") ? p.b : p.a) / p.L + _a.mass_u * p.g;
    if (_fz0 < 1) _fz0 = 1;
    var _ml = max(1 - _a.load_sens * (_a.normal / _fz0 - 1), 0.05);
    var _tf = global.phys_v28_tempf(_a.temp);
    var _mp = global.phys_v28_mupsi(_a.psi, _a.psi_peak);
    var _mue = _a.mu_base * _mp * _ml * _tf;
    var _mus = _a.mu_static * _mp * _ml * _tf;
    _a.mu_eff = _mue;
    _a.mu_static_eff = _mus;
    var _iw = global.phys_v28_iw(_a);
    var _dem = (_tw - _a.t_brake - _iw * p.ax / _a.radius) / _a.radius;
    if (_a.stick) {
        if (abs(_dem) > _mus * _a.normal) _a.stick = 0;
    } else if (abs(_vw - p.vx) < p.eps_s && abs(_dem) < 0.9 * _mus * _a.normal) {
        _a.stick = 1;
    }
    if (_a.stick) {
        _a.omega = p.vx / _a.radius;
        var _lim = _mus * _a.normal;
        _a.fx = clamp(_dem, -_lim, _lim);
        _a.kappa = 0;
    } else {
        var _sa = abs(_k);
        var _mc;
        if (_sa < 0.2) _mc = _mus * (_sa / 0.2);
        else {
            var _ratio = clamp((_sa - 0.2) / 0.5, 0, 1);
            _mc = _mus * (1 - _ratio) + _mue * _ratio;
        }
        _a.fx = _mc * _a.normal * global.phys_v28_sgn(_k);
    }
    _a.fy = 0;
};

global.phys_v28_heat = function(_a, _dt) {
    var p = global.phys;
    var _vs = _a.omega * _a.radius - p.vx;
    var _pw = abs(_a.fx * _vs);
    _a.temp += (_a.k_heat * _pw - _a.k_cool * (_a.temp - p.t_amb)) * _dt;
    if (_a.temp < p.t_amb) _a.temp = p.t_amb;
    if (_a.temp > 220) _a.temp = 220;
};

global.phys_v28_wheel = function(_a, _tw, _dt) {
    var p = global.phys;
    if (_a.projected) {
        var _demp = global.phys_v28_bdemand(_a);
        if (_demp > 0 && abs(_a.omega) >= p.eps_l) _a.t_brake = _demp * global.phys_v28_sgn(_a.omega);
        else _a.t_brake = 0;
        return;
    }
    var _iw = global.phys_v28_iw(_a);
    if (_a.stick && _a.grounded) {
        _a.omega = p.vx / _a.radius;
        var _dem = global.phys_v28_bdemand(_a);
        if (abs(p.vx) > p.eps_s) {
            var _s = (abs(_a.omega) > 0.000001) ? global.phys_v28_sgn(_a.omega) : global.phys_v28_sgn(p.vx);
            _a.t_brake = _dem * _s;
        } else {
            _a.t_brake = clamp(_tw - _iw * p.ax / _a.radius, -_dem, _dem);
        }
        return;
    }
    if (_a.driven && p.mode == "stick") {
        var _dems = global.phys_v28_bdemand(_a);
        if (_dems > 0 && abs(_a.omega) >= p.eps_l) _a.t_brake = _dems * global.phys_v28_sgn(_a.omega);
        else _a.t_brake = 0;
        return;
    }
    var _demand = global.phys_v28_bdemand(_a);
    var _pred = _a.omega + (_tw - _a.fx * _a.radius) / _iw * _dt;
    var _tstop = _iw * _pred / _dt;
    if (_demand > 0 && _demand >= abs(_tstop)) {
        _a.omega = 0;
        _a.t_brake = _tstop;
    } else if (_demand > 0 && abs(_pred) > 0.000000001) {
        var _sg = global.phys_v28_sgn(_pred);
        _a.t_brake = _demand * _sg;
        _a.omega = _pred - _sg * _demand * _dt / _iw;
    } else {
        _a.t_brake = 0;
        _a.omega = _pred;
    }
    if (_demand > 0 && abs(_a.omega) < p.eps_l) _a.omega = 0;
};

global.phys_v28_frame = function() {
    if (!variable_global_exists("phys") || is_undefined(global.phys)) global.phys_v28_reset();
    var p = global.phys;
    if (p.car != global.vehicles_array) global.phys_v28_reset();
    p = global.phys;
    if (global.gear < 0) {
        p.held = 1;
        global.phys_moved = 0;
        return;
    }
    // relacoes do jogo -> relacao total (rad do motor / rad da roda)
    var _rr = 0.31;
    if (variable_struct_exists(p.rear, "radius")) _rr = p.rear.radius;
    var _ngear = array_length(obj_player.gear_ratios);
    var _rats = array_create(_ngear);
    for (var _i = 0; _i < _ngear; _i++) _rats[_i] = obj_player.gear_ratios[_i] * _rr * 6.28318530718;
    p.ratios = _rats;
    var _redrpm = global.car_data[global.vehicles_array].rpm;
    if (_redrpm < 1000) _redrpm = 6500;
    p.red = _redrpm * 0.104719755;
    var _cap = 800;
    if (variable_global_exists("maxtorque") && global.maxtorque > 100) _cap = global.maxtorque * 1.15;
    p.cap = _cap;
    // aero do proprio carro. ClA < 0 = downforce.
    var _area = 2.2;
    if (variable_global_exists("area_frontal") && global.area_frontal > 0.2) _area = global.area_frontal;
    var _cd = 0.40;
    if (variable_global_exists("aero_cd")) _cd = global.aero_cd();
    p.cda = max(0.3, _cd * _area);
    var _cl = 0;
    if (variable_global_exists("aero_ativo") && global.aero_ativo == 1 && variable_global_exists("aero_cl")) _cl = global.aero_cl;
    p.cla = -abs(_cl) * _area;
    p.xcp = p.a;

    if (p.held) {
        p.vx = max(0, global.phys_kmh_used) / 3.6;
        if (p.rear.stick) p.rear.omega = p.vx / p.rear.radius;
        if (p.front.stick) p.front.omega = p.vx / p.front.radius;
        if (global.rpm > 500) p.omega = global.rpm * 0.104719755;
        p.held = 0;
    }

    p.thr_in = global.phys_in_up;
    // a embreagem do jogo fica aberta enquanto clutch > 0 (o timer da troca).
    p.pedal = (global.phys_in_clutch || (global.phys_in_clutch_t > 0)) ? 1 : 0;
    p.brake = global.phys_in_down;
    p.hb = (global.phys_in_brake || (variable_global_exists("handbrake") && global.handbrake)) ? 1 : 0;
    p.lock = (global.phys_in_borra == 1) ? 1 : 0;
    p.ign = (global.engine == 1) ? 1 : 0;
    // spawn / ligar o motor: o jogo ja considera o motor aceso, mas o stall
    // ficava preso e so soltava quando a embreagem abria numa troca de marcha.
    if (!variable_struct_exists(p, "engine_was")) p.engine_was = 0;
    if (global.engine == 1 && p.engine_was == 0) {
        p.stalled = 0;
        if (p.omega < p.w_idle) p.omega = p.w_idle;
    }
    p.engine_was = (global.engine == 1) ? 1 : 0;
    if (global.engine == 0) p.stalled = 1;
    // a marcha e a do jogo. o cambio interno devolvia global.gear para 0
    // no meio da troca e o carro ficava em neutro com o giro subindo.
    p.gear = global.gear;
    p.gear_target = global.gear;
    p.phase = "none";
    p.shift_req = 0;

    var _dt = global.dt / 60;
    if (_dt <= 0) _dt = 1 / 60;
    if (_dt > 0.05) _dt = 0.05;
    var _h = _dt / 4;
    var _req = p.shift_req;
    for (var _s = 0; _s < 4; _s++) {
        if (_s > 0) p.shift_req = 0;
        global.phys_v28_sub(_h);
    }
    p.shift_req = 0;
    if (p.vx > 150 || p.vx < -150) p.vx = clamp(p.vx, -150, 150);
    if (p.vz > 30 || p.vz < -30) p.vz = clamp(p.vz, -30, 30);
    if (p.wp > 20 || p.wp < -20) p.wp = clamp(p.wp, -20, 20);
    if (p.vx < 0) p.vx = 0;

    var _old = global.phys_kmh_used;
    global.kmh = p.vx * 3.6;
    global.rpm = p.omega * 9.5492965855;
    if (global.rpm < 0) global.rpm = 0;
    global.phys_slip = clamp(abs(p.rear.kappa), 0, 1);
    global.phys_theta = p.theta;
    global.phys_zu = p.front.z_u;
    global.phys_nf = p.front.normal;
    global.phys_nr = p.rear.normal;
    global.phys_temp_f = p.front.temp;
    global.phys_temp_r = p.rear.temp;
    global.phys_stall = p.stalled;
    global.phys_bar = p.bar_g;
    global.phys_shift = p.phase;

    var _dk = global.kmh - _old;
    if (global.phys_scroll) {
        // Avenida: o carro fica no lugar e a rua rola com o kmh.
        // o bloco da sala ja usou o kmh velho; corrige a roda e o scroll agora.
        global.angle -= _dk * global.dt;
        if (variable_global_exists("angle_frente")) global.angle_frente -= _dk * global.dt;
        var _h = global.kmh / 2.7;
        layer_hspeed("Rua", (-_h / 1.5) * global.dt);
        layer_hspeed("Ceu", (-_h / 2) * global.dt);
        layer_hspeed("Ceu2", (-_h / 2) * global.dt);
        layer_hspeed("Arvores", (-_h / 3) * global.dt);
        layer_hspeed("ArvoresF", (-_h / 2) * global.dt);
        layer_hspeed("ArvoresF_1", (-_h / 2) * global.dt);
    } else if (global.phys_hit) {
        p.vx = 0;
        global.kmh = 0;
    } else {
        var _dx = (_dk / 2.7) * global.dt;
        if (!place_meeting(x + _dx, y, obj_colision)) {
            x += _dx;
            global.angle -= _dk * global.dt;
            if (variable_instance_exists(id, "angle_frente") || variable_global_exists("angle_frente")) global.angle_frente -= _dk * global.dt;
        } else {
            p.vx = 0;
            global.kmh = 0;
        }
    }
    global.phys_moved = 0;
    global.phys_hit = 0;
    p.held = 0;
};
