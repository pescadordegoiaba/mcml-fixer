// Distribuidor + carburador, fase 1. Mesmas contas de mod/fuel_model.py.
// Produz timing, afr e knock. O phys_step multiplica o torque por fuel.eff.

global.fuel_model_on = 1;

global.fuel_sys_clamp = function(_x, _lo, _hi) {
    if (_x < _lo) return _lo;
    if (_x > _hi) return _hi;
    return _x;
};

global.fuel_sys_lookup = function(_rpm, _r, _y) {
    var _n = array_length(_r);
    if (_rpm <= _r[0]) return _y[0];
    if (_rpm >= _r[_n - 1]) return _y[_n - 1];
    for (var _i = 1; _i < _n; _i++) {
        if (_rpm <= _r[_i]) {
            var _u = (_rpm - _r[_i - 1]) / (_r[_i] - _r[_i - 1]);
            return _y[_i - 1] + (_y[_i] - _y[_i - 1]) * _u;
        }
    }
    return _y[_n - 1];
};

global.fuel_sys_ensure = function() {
    if (variable_global_exists("fuel_sys") && !is_undefined(global.fuel_sys)) return;
    global.fuel_sys = {
        base: 10, spring: "street", vac_on: 1, max_vac: 19,
        advance: 0, man_ret: 0, cent_scale: 1, curva: 1, rpm_ini: 1000, rpm_fim: 3200,
        jet: 80, turns: 2.5, pv: 6.5, enrich: 1.0,
        octane: 93, iat: 40, coolant: 90,
        mapb: 0.35, afr: 13, timing: 10, knock: 0, retard: 0,
        load: 0, mech: 0, vac: 0, mbt: 28, eff: 1,
        prev_thr: 0, injected: 0
    };
    global.telemetry_afr = 13;
    global.telemetry_timing = 10;
    global.telemetry_knock = 0;
    global.telemetry_MAP = 0.35;
    global.telemetry_octane_effective = 93;
};

global.fuel_sys_sync = function() {
    global.fuel_sys_ensure();
    var _car = global.mod_dist_car();
    if (!is_undefined(_car) && variable_struct_exists(_car, "dist_cfg") && !is_undefined(_car.dist_cfg)) {
        var _d = _car.dist_cfg;
        if (!variable_struct_exists(_d, "avanco_extra")) _d.avanco_extra = 0;
        if (!variable_struct_exists(_d, "retardo")) _d.retardo = 0;
        global.fuel_sys.base = global.fuel_sys_clamp(_d.avanco_inicial, 0, 20);
        global.fuel_sys.advance = global.fuel_sys_clamp(_d.avanco_extra, 0, 15);
        global.fuel_sys.man_ret = global.fuel_sys_clamp(_d.retardo, 0, 15);
        global.fuel_sys.curva = global.fuel_sys_clamp(_d.curva, 0, 2);
        global.fuel_sys.cent_scale = global.fuel_sys_clamp(_d.cent_total, 0, 40) / 22;
        global.fuel_sys.rpm_ini = _d.rpm_inicio;
        global.fuel_sys.rpm_fim = _d.rpm_fim;
        global.fuel_sys.vac_on = (_d.vac_total > 0) ? 1 : 0;
        // 12 graus no menu = os 19 do modelo; o passo fino mexe nessa escala
        global.fuel_sys.max_vac = _d.vac_total * (19 / 12);
    }
    var _c = global.mod_carb_cfg();
    if (!is_undefined(_c)) {
        if (!variable_struct_exists(_c, "jet")) _c.jet = 80;
        if (!variable_struct_exists(_c, "idle_turns")) _c.idle_turns = 2.5;
        if (!variable_struct_exists(_c, "pv")) _c.pv = 6.5;
        global.fuel_sys.jet = global.fuel_sys_clamp(_c.jet, 60, 100);
        global.fuel_sys.turns = global.fuel_sys_clamp(_c.idle_turns, 1, 4);
        global.fuel_sys.pv = global.fuel_sys_clamp(_c.pv, 3.5, 10.5);
    }
    global.fuel_sys.injected = 0;
    if (!is_undefined(_car) && global.mod_dist_tipo(_car) != 1) global.fuel_sys.injected = 1;
    if (variable_global_exists("iat")) global.fuel_sys.iat = global.iat;
    if (variable_global_exists("temp") && global.temp > 40) global.fuel_sys.coolant = global.temp;
};

global.fuel_sys_mech_tab = function(_rpm, _kind) {
    var _r;
    var _y;
    if (_kind >= 2) {
        _r = [800, 1800, 2400, 3000, 3800, 4600, 5600, 7000];
        _y = [0, 1, 6, 12, 16, 19, 21, 22];
    } else if (_kind == 1) {
        _r = [800, 1400, 2000, 2600, 3200, 4000, 5000, 6500];
        _y = [0, 3, 9, 15, 19, 22, 24, 24];
    } else {
        _r = [800, 1200, 1600, 2000, 2600, 3400, 4500, 6500];
        _y = [0, 6, 12, 17, 21, 24, 24, 24];
    }
    return global.fuel_sys_lookup(_rpm, _r, _y);
};

global.fuel_sys_mech = function(_rpm) {
    var f = global.fuel_sys;
    var _span = f.rpm_fim - f.rpm_ini;
    if (_span < 200) _span = 200;
    var _rpm2 = 1000 + (_rpm - f.rpm_ini) * (2200 / _span);
    var _c = f.curva;
    var _lo = 0;
    var _hi = 1;
    var _u = _c;
    if (_c > 1) { _lo = 1; _hi = 2; _u = _c - 1; }
    if (_u < 0) _u = 0;
    if (_u > 1) _u = 1;
    var _m = global.fuel_sys_mech_tab(_rpm2, _lo) + (global.fuel_sys_mech_tab(_rpm2, _hi) - global.fuel_sys_mech_tab(_rpm2, _lo)) * _u;
    return _m * f.cent_scale;
};

global.fuel_sys_mbt = function(_rpm, _map) {
    var _rr = [800, 2000, 4000, 6000];
    var _mm = [0.3, 0.7, 1.0, 1.5];
    // tabela achatada: linha*4 + coluna
    var _t = [15, 25, 20, 12,  20, 30, 28, 18,  25, 34, 34, 22,  28, 36, 38, 25];
    var _ir = 0;
    var _ur = 0;
    if (_rpm >= 6000) { _ir = 2; _ur = 1; }
    else {
        for (var _i = 1; _i < 4; _i++) {
            if (_rpm <= _rr[_i]) {
                _ir = _i - 1;
                _ur = (_rpm - _rr[_i - 1]) / (_rr[_i] - _rr[_i - 1]);
                break;
            }
        }
    }
    var _ic = 0;
    var _uc = 0;
    if (_map >= 1.5) { _ic = 2; _uc = 1; }
    else {
        for (var _j = 1; _j < 4; _j++) {
            if (_map <= _mm[_j]) {
                _ic = _j - 1;
                _uc = (_map - _mm[_j - 1]) / (_mm[_j] - _mm[_j - 1]);
                break;
            }
        }
    }
    var _y00 = _t[_ir * 4 + _ic];
    var _y10 = _t[(_ir + 1) * 4 + _ic];
    var _y01 = _t[_ir * 4 + _ic + 1];
    var _y11 = _t[(_ir + 1) * 4 + _ic + 1];
    var _y0 = _y00 + (_y10 - _y00) * _ur;
    var _y1 = _y01 + (_y11 - _y01) * _ur;
    return _y0 + (_y1 - _y0) * _uc;
};

global.fuel_sys_step = function(_rpm, _thr, _dt) {
    global.fuel_sys_sync();
    var f = global.fuel_sys;
    if (_dt < 0) _dt = 0;
    _thr = global.fuel_sys_clamp(_thr, 0, 1);
    // 3a — distribuidor ve o MAP e o AFR do passo anterior
    f.mech = global.fuel_sys_mech(_rpm);
    f.vac = 0;
    if (f.vac_on && f.mapb < 1.013) f.vac = f.max_vac * (1 - f.mapb / 1.013);
    if (f.vac < 0) f.vac = 0;
    if (f.vac > f.max_vac) f.vac = f.max_vac;
    var _cmd = f.base + f.mech + f.vac + f.advance - f.man_ret;
    var _tim = _cmd - f.retard;
    var _basek = 42 + (f.octane - 93) * (8 / 17);
    var _frpm = global.fuel_sys_clamp((_rpm - 4000) / 2000, -2, 2);
    var _kthr = _basek + _frpm - 0.5 * max(0, f.afr - 12.8) - 0.2 * max(0, f.iat - 40) - 0.1 * max(0, f.coolant - 100) - 0.4 * max(0, 93 - f.octane);
    var _margin = _kthr - _tim;
    if (_margin < 0) {
        f.knock = global.fuel_sys_clamp(-_margin / 5, 0, 1);
        f.retard = min(15, f.retard + 3 * _dt);
    } else {
        f.knock = 0;
        f.retard = max(0, f.retard - 1 * _dt);
    }
    f.timing = _cmd - f.retard;

    // 3b — carburador atualiza MAP e AFR para o torque deste passo
    var _x = (_rpm - 3800) / 2400;
    var _ve = global.fuel_sys_clamp(0.92 - 0.16 * _x * _x, 0.62, 0.92);
    f.load = _thr * _ve / 0.92;
    f.mapb = 1.013 - (1 - f.load) * 1.013 * 0.70;
    if (f.injected) {
        var _car = global.mod_dist_car();
        var _rpm_max = 6500;
        if (!is_undefined(_car) && variable_struct_exists(_car, "rpm")) _rpm_max = _car.rpm;
        f.afr = global.motor_afr_inj(_rpm, _rpm_max, global.turbo, global.fuel_sys_field_or_zero(), global.injecao);
    } else {
        var _idle = 13.0 + (2.5 - f.turns) * 0.35;
        var _main = 14.0 + (80 - f.jet) * 0.045;
        var _afr = _idle;
        if (_thr >= 0.05) {
            var _blend = global.fuel_sys_clamp((_thr - 0.05) / 0.20, 0, 1);
            _afr = _idle + (_main - _idle) * _blend;
        }
        var _inhg = max(0, (1.013 - f.mapb) / 0.0339);
        if (_inhg < f.pv && _thr > 0.5) _afr -= f.enrich;
        f.afr = _afr;
    }
    f.mbt = global.fuel_sys_mbt(_rpm, f.mapb);
    var _dtm = (f.timing - f.mbt) / 10;
    var _et = max(0.40, 1 - 0.15 * _dtm * _dtm);
    var _da = (f.afr - 12.8) / 2;
    var _ea = max(0.30, 1 - 0.10 * _da * _da);
    f.eff = max(0.25, _et * _ea * (1 - 0.4 * f.knock));
    f.prev_thr = _thr;

    global.afr = f.afr;
    global.ignicao = f.timing;
    global.telemetry_afr = f.afr;
    global.telemetry_timing = f.timing;
    global.telemetry_knock = f.knock;
    global.telemetry_MAP = f.mapb;
    global.telemetry_octane_effective = f.octane - max(0, f.iat - 40) * 0.5 - max(0, f.coolant - 100) * 0.25;
    if (variable_global_exists("mod_dist_estado")) {
        global.mod_dist_estado.knock = f.knock;
        global.mod_dist_estado.avanco = f.timing;
        global.mod_dist_estado.map = f.mapb;
    }
    return f.eff;
};

// combustivel do jogo pode nao existir ainda
global.fuel_sys_field_or_zero = function() {
    // global.fuel do jogo e o TIPO de combustivel (1 gasolina, 2 etanol...), nao o modelo
    if (variable_global_exists("fuel") && is_real(global.fuel)) return global.fuel;
    return 2;
};
