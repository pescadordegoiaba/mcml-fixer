import pathlib

GML_INIT = r'''
// =====================================================================
//  MOD DISTRIBUIDOR v2  -  item de carro (0 nenhum, 1 original, 2 performance)
//  instalado por mcml-fixer --mod-install
// =====================================================================
global.mod_dist_versao = "2.0";
global.mod_dist_preco = [0, 0, 12000];
global.mod_dist_nome = ["Nenhum", "Original", "Competicao"];
global.mod_dist_def = {
    avanco_inicial: 10, cent_total: 22, rpm_inicio: 1000, rpm_fim: 3200,
    curva: 1, vac_total: 12, vac_limiar: 0, dwell: 45, desgaste: 0, ganho: 100
};
// padroes de fisica: o ini [fisica] sobrescreve; cada carro copia ao criar
// globais do jogo que o mod le: garante que existem em qualquer sala
if (!variable_global_exists("turbo_pressure")) global.turbo_pressure = 0;
if (!variable_global_exists("turbo")) global.turbo = 0;
if (!variable_global_exists("iat")) global.iat = 25;
if (!variable_global_exists("temp")) global.temp = 25;
if (!variable_global_exists("engine")) global.engine = 0;
if (!variable_global_exists("rpm")) global.rpm = 900;
if (!variable_global_exists("kmh")) global.kmh = 0;
if (!variable_global_exists("gear")) global.gear = 0;
if (!variable_global_exists("ignicao")) global.ignicao = 0;
if (!variable_global_exists("afr")) global.afr = 14.7;
if (!variable_global_exists("cash")) global.cash = 0;
if (!variable_global_exists("dt")) global.dt = 1;

global.mod_rev = 0;
// ---- dinheiro (oculto): [dev] cash=N ----
global.dev_cash = 0;
global.dev_cash_modo = 0;   // 0 = piso (nunca abaixo de N), 1 = valor exato
global.dev_cash_rev = -1;

global.dev_cash_aplicar = function() {
    if (global.dev_cash <= 0) return 0;
    if (global.dev_cash_modo == 1) {
        if (global.dev_cash_rev == global.mod_rev) return 0;
        global.cash = global.dev_cash;
        global.dev_cash_rev = global.mod_rev;
        return 1;
    }
    if (global.cash < global.dev_cash) {
        global.cash = global.dev_cash;
        return 1;
    }
    return 0;
};

// dinheiro nunca fica negativo
global.cash_guard = function() {
    if (!variable_global_exists("cash")) return 0;
    if (global.cash < 0) {
        global.cash = 0;
        return 1;
    }
    return 0;
};
global.phys_def = { raio: 0.31, eficiencia: 0.90, mu: 1.00, share: 1.00, embreagem: 0, inercia: 0.15 };
global.mod_dist_estado = { menu: 0, sel: 0, aviso: "", spr: -1, spr_try: 0, rpm: 800, map: 0, cent: 0, vac: 0, avanco: 10, ideal: 0, bonus: 0, q: 1, knock: 0, dwell_ef: 45, dwell_ms: 5, tok: -1, icon: -1, icon_try: 0 };
global.mod_dist_ativo = 0;

global.mod_dist_car = function() {
    if (!is_array(global.car_data)) return undefined;
    if (global.vehicles_array < 0 || global.vehicles_array >= array_length(global.car_data)) return undefined;
    return global.car_data[global.vehicles_array];
};

// tipo do motor: -1 = indefinido, 0 = injetado, 1 = carburado.
// carburador = -1 e o valor de fabrica (EstruturaSalvamento:129) e o jogo so
// resolve para 1/3 dentro da Garage (obj_player Step:78-93). Ler -1 como
// injetado era o BUG 14: gravava distribuidor=0 no carro salvo e matava o F6.
global.mod_dist_tipo = function(car) {
    if (is_undefined(car)) return -1;
    if (!variable_struct_exists(car, "carburador")) return -1;
    var _c = car.carburador;
    if (is_undefined(_c)) return -1;
    // bicos instalados = injetado, nao importa o que diga o carburador
    if (variable_struct_exists(car, "bicos") && car.bicos > 0) return 0;
    if (_c > 0) return 1;
    if (_c == 0) return 0;
    // indeterminado: fail-open, o unico efeito do mod e instalar distribuidor
    return 1;
};

global.mod_dist_modelo = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    var _t = global.mod_dist_tipo(car);
    if (_t < 0) return 0;
    // todo motor carburado sai de fabrica com o distribuidor original
    if (!variable_struct_exists(car, "distribuidor") || is_undefined(car.distribuidor)) {
        car.distribuidor = (_t == 1) ? 1 : 0;
    }
    // cura carro estragado pela versao antiga (gravou 0 com carburador -1)
    else if (_t == 1 && car.distribuidor == 0) {
        car.distribuidor = 1;
    }
    return car.distribuidor;
};

// limites fisicos por modelo: [ini_min, ini_max, cent_max, rpm_fim_max, vac_max, dwell_min, dwell_max, ganho_max]
// [ini_min, ini_max, cent_max, rpm_fim_max, vac_max, dwell_min, dwell_max, ganho_max, corte_rpm]
global.mod_dist_limites = function(modelo) {
    if (modelo == 1) return [0, 12, 20, 5500, 14, 38, 52, 45, 5500];
    return [0, 20, 30, 7500, 20, 30, 60, 100, 7500];
};

// configuracao salva junto com o carro (persiste no save do jogo)
global.mod_dist_cfg = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return global.mod_dist_def;
    if (!variable_struct_exists(car, "dist_cfg")) car.dist_cfg = undefined;
    if (is_undefined(car.dist_cfg)) {
        car.dist_cfg = {
            avanco_inicial: global.mod_dist_def.avanco_inicial,
            cent_total: global.mod_dist_def.cent_total,
            rpm_inicio: global.mod_dist_def.rpm_inicio,
            rpm_fim: global.mod_dist_def.rpm_fim,
            curva: global.mod_dist_def.curva,
            vac_total: global.mod_dist_def.vac_total,
            vac_limiar: global.mod_dist_def.vac_limiar,
            dwell: global.mod_dist_def.dwell,
            desgaste: 0,
            ganho: global.mod_dist_def.ganho,
            ver: 2
        };
    }
    // config antiga (ganho vinha 50 e diluia o bonus): sobe uma vez
    if (!variable_struct_exists(car.dist_cfg, "ver")) {
        car.dist_cfg.ver = 2;
        car.dist_cfg.ganho = 100;
    }
    return car.dist_cfg;
};

// Aplica o ini ao carro SALVO quando o arquivo muda (compara data de modificacao).
// Assim o que voce grava com --phys/--mod-set vale no proximo boot, sem comprar
// carro novo e sem apagar o que foi ajustado no F6 (o F6 nao toca no ini).
global.mod_cfg_tick = 0;
global.mod_cfg_aplicar = function() {
    if (current_time < global.mod_cfg_tick) return 0;
    global.mod_cfg_tick = current_time + 1000;
    var _st = variable_global_exists("mod_rev") ? global.mod_rev : 0;
    if (_st <= 0) return 0;
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    if (variable_struct_exists(car, "cfg_stamp") && car.cfg_stamp == _st) return 0;

    var _tinha = (variable_struct_exists(car, "phys_cfg") && !is_undefined(car.phys_cfg)) ? 1 : 0;
    car.phys_cfg = { raio: global.phys_def.raio, eficiencia: global.phys_def.eficiencia,
                     mu: global.phys_def.mu, share: global.phys_def.share,
                     embreagem: global.phys_def.embreagem, inercia: global.phys_def.inercia };

    var _dist = 0;
    if (global.mod_dist_modelo() > 0) {
        var _c = global.mod_dist_cfg();
        _c.avanco_inicial = global.mod_dist_def.avanco_inicial;
        _c.cent_total = global.mod_dist_def.cent_total;
        _c.rpm_inicio = global.mod_dist_def.rpm_inicio;
        _c.rpm_fim = global.mod_dist_def.rpm_fim;
        _c.curva = global.mod_dist_def.curva;
        _c.vac_total = global.mod_dist_def.vac_total;
        _c.vac_limiar = global.mod_dist_def.vac_limiar;
        _c.dwell = global.mod_dist_def.dwell;
        _c.ganho = global.mod_dist_def.ganho;
        _c.ver = 2;
        _dist = 1;
    }
    car.cfg_stamp = _st;

    var _f = file_text_open_write("mod_cfg_diag.txt");
    file_text_write_string(_f, "aplicado=1 phys_antes=" + string(_tinha) + " dist=" + string(_dist));
    file_text_write_string(_f, " raio=" + string(car.phys_cfg.raio) + " mu=" + string(car.phys_cfg.mu));
    file_text_write_string(_f, " inercia=" + string(car.phys_cfg.inercia) + " ganho=" + string(global.mod_dist_def.ganho));
    file_text_close(_f);
    return 1;
};

global.mod_dist_comprar = function(modelo) {
    var car = global.mod_dist_car();
    var e = global.mod_dist_estado;
    if (is_undefined(car)) { e.aviso = "sem carro selecionado"; return; }
    if (global.mod_dist_tipo(car) != 1) { e.aviso = "so instala em motor carburado"; return; }
    if (global.mod_dist_modelo() == modelo) { e.aviso = "este modelo ja esta instalado"; return; }
    if (modelo == 1) {
        // voltar ao original de fabrica: nao custa nada, mas nao devolve o dinheiro
        car.distribuidor = 1;
        car.dist_cfg = undefined;
        global.mod_dist_cfg();
        e.aviso = "distribuidor original reinstalado";
        global.mod_dist_ativo = 1;
        return;
    }
    var preco = global.mod_dist_preco[modelo];
    if (global.cash < preco) { e.aviso = "faltam $" + string(preco - global.cash); return; }
    global.cash -= preco;
    car.distribuidor = modelo;
    car.dist_cfg = undefined;
    global.mod_dist_cfg();
    e.aviso = "comprado: " + global.mod_dist_nome[modelo];
    global.mod_dist_ativo = 1;
};

global.mod_dist_remover = function() {
    var car = global.mod_dist_car();
    var e = global.mod_dist_estado;
    if (is_undefined(car)) return;
    var m = global.mod_dist_modelo();
    if (m <= 1) { e.aviso = "ja esta com o original de fabrica"; return; }
    global.cash += floor(global.mod_dist_preco[m] * 0.5);
    car.distribuidor = 1;
    car.dist_cfg = undefined;
    e.aviso = "voltou ao original (+$" + string(floor(global.mod_dist_preco[m] * 0.5)) + ")";
    global.mod_dist_ativo = 1;
};

global.mod_dist_padrao_modelo = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return;
    car.dist_cfg = undefined;
    global.mod_dist_cfg();
};

// limite de detonacao depende do carro: compressao (pistao), cabecote e combustivel
global.mod_dist_knock_limite = function(car) {
    var lim = 34;
    if (car.pistao >= 3) lim -= 3;
    if (car.pistao >= 4) lim -= 1;
    if (car.cabecote >= 3) lim += 1;
    if (global.fuel == 2) lim += 3;
    if (global.fuel == 3) lim += 5;
    if (global.fuel == 4) lim += 7;
    return lim;
};

global.mod_dist_calcular = function() {
    var e = global.mod_dist_estado;
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return 0;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _rpm = clamp(global.rpm, 800, 8000);
    var _map = clamp(global.turbo_pressure, -0.8, 4);
    var _ini = d.rpm_inicio;
    var _fim = max(_ini + 100, min(d.rpm_fim, lim[3]));
    // curva de dois estagios: duas molas - a primaria entra cedo, a secundaria depois
    var _faixa = max(100, _fim - _ini);
    var _div = 0.62;
    if (d.curva == 0) _div = 0.5;
    if (d.curva == 2) _div = 0.75;
    var t1 = clamp((_rpm - _ini) / (_faixa * _div), 0, 1);
    var t2 = clamp((_rpm - _ini - (_faixa * _div)) / (_faixa * (1 - _div)), 0, 1);
    var area = min(d.cent_total, lim[2]);
    var cent = area * ((_div * t1) + ((1 - _div) * t2));
    // vacuo medido em inHg, como nas curvas de distribuidor: comeca em ~5 inHg e
    // satura em ~15 inHg (1 inHg = 0.0339 bar). Ported vacuum: nada acima de 0 bar.
    var _vac_inhg = clamp((0 - _map) / 0.0339, 0, 30);
    var vac = min(d.vac_total, lim[4]) * clamp((_vac_inhg - 5) / 10, 0, 1);
    e.rpm = _rpm;
    e.map = _map;
    e.cent = cent;
    e.vac = vac;
    e.dwell_ef = clamp(d.dwell - (d.desgaste * 0.1), lim[5], lim[6]);
    e.avanco = clamp(min(d.avanco_inicial, lim[1]) + cent + vac, lim[0], 45);
    return e.avanco;
};

global.mod_dist_fator = function() {
    var e = global.mod_dist_estado;
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 1;
    if (global.mod_dist_tipo(car) != 1) return 1;
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return 1;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    // corte de giro do modelo: acima disso o distribuidor corta a faisca
    var _corte = lim[8];
    var _rpm_max_carro = 99999;
    if (variable_struct_exists(car, "rpm")) _rpm_max_carro = car.rpm;
    if (global.rpm > min(_corte, _rpm_max_carro)) return 0;
    var adv = global.mod_dist_calcular();
    var tn = clamp((e.rpm - 800) / 7200, 0, 1);
    var ideal = lerp(14, 38, tn) - (e.map * 3.5);
    var pos = (clamp((adv - ideal) / 8, -2, 2) + 1) / 2;
    var bonus = 0;
    if (pos >= 0.9) bonus = lerp(0.55, 0.6, (pos - 0.9) / 0.1);
    else if (pos >= 0.7) bonus = lerp(0.4, 0.55, (pos - 0.7) / 0.2);
    else if (pos >= 0.4) bonus = lerp(0.2, 0.4, (pos - 0.4) / 0.3);
    else bonus = -power((0.4 - pos) / 0.4, 1.3) * 0.25;
    // o modelo define o teto de ganho (Original 45%, Competicao 100%) e o
    // parametro ganho regula quanto desse teto o carro usa. Antes isso era
    // multiplicado E limitado, o que derrubava o bonus maximo para ~25%.
    var _cap = lim[7] / 100;
    if (bonus > _cap) bonus = _cap;
    bonus *= clamp(d.ganho, 0, 100) / 100;
    // o angulo de dwell vira tempo de saturacao da bobina: o distribuidor gira a
    // metade do motor, entao uma volta leva 120000/rpm ms
    var _dwell_ms = (e.dwell_ef / 360) * (120000 / max(800, e.rpm));
    // abaixo de ~1.2 ms a faisca enfraquece; acima de ~3.2 ms a bobina satura
    var q = clamp((_dwell_ms - 1.2) / 2.0, 0, 1);
    e.dwell_ms = _dwell_ms;
    e.ideal = ideal;
    e.bonus = bonus;
    e.q = q;
    // igual ao nativo: bonus cheio, sem diluicao. A faisca fraca (q<1) so piora.
    var f = (1 + bonus);
    if (q < 1) f *= (0.90 + 0.10 * q);
    var falha = max(0, (1 - q) * 0.6 + (d.desgaste / 100) * 0.4);
    if (random(1) < falha * clamp(e.rpm / 6000, 0, 1) * 0.05) f *= 0.55;
    return f;
};

// dano de detonacao: mesma curva do ECU do jogo, mas calculada aqui dentro
// (o mod roda fora do evento do player e nao enxerga funcoes locais dele)
global.mod_dist_dano_mult = function(peca, turbo) {
    var mult = 0;
    switch (peca) {
        case 0: mult = 0.75; break;
        case 1: mult = 0.5; break;
        case 2: mult = 0.25; break;
        case 3: mult = turbo ? 2 : 0.5; break;
        case 4: mult = turbo ? 3.5 : 0.25; break;
    }
    return mult * 0.35;
};

global.mod_dist_dano = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return;
    if (global.engine != 1) return;
    if (global.mod_dist_tipo(car) != 1) return;
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return;
    var d = global.mod_dist_cfg();
    var e = global.mod_dist_estado;
    var dt = global.dt;
    d.desgaste = min(100, d.desgaste + (0.0015 + abs(e.avanco) * 0.00002) * dt);
    var _iat = global.iat ?? 25;
    var _ect = global.temp ?? 90;
    var limite = global.mod_dist_knock_limite(car) - (e.map * 3.5);
    if (e.map > 0) limite -= (e.map * 2);
    limite -= max(0, _iat - 25) * 0.15;          // ar quente = menos avanco
    limite -= max(0, _ect - 95) * 0.20;          // motor quente = menos avanco
    e.knock = max(0, e.avanco - limite);
    // detonacao castiga cabecote e pistao (entra em motor_desgaste)
    if (e.knock > 0.6) {
        if (variable_struct_exists(car, "vcabecote")) {
            car.vcabecote = max(0, car.vcabecote - (e.knock * dt * 0.5));
        }
        if (variable_struct_exists(car, "vpistao")) {
            car.vpistao = max(0, car.vpistao - (e.knock * dt * 0.25));
        }
    }
    // dwell grande em baixa rotacao satura a bobina: esquenta e falha
    if (e.dwell_ms > 8 && e.rpm < 2500) {
        global.temp += (e.dwell_ms - 8) * 0.01 * dt;
        if (random(1) < 0.002 * (e.dwell_ms - 8)) d.desgaste = min(100, d.desgaste + 0.2);
    }
    if (e.knock > 0 && global.kmh >= 20) {
        car.vpistao -= e.knock * global.mod_dist_dano_mult(car.pistao, car.turbo) * 0.35 * dt;
        car.vbielas -= e.knock * global.mod_dist_dano_mult(car.bielas, car.turbo) * 0.30 * dt;
        global.temp += e.knock * 0.25 * dt;
    }
};

global.mod_dist_input = function() {
    var e = global.mod_dist_estado;
    var car = global.mod_dist_car();
    if (keyboard_check_pressed(vk_f6)) { e.menu = (e.menu == 0) ? 1 : 0; e.sel = 0; e.aviso = ""; }
    if (e.menu == 0) return;
    if (is_undefined(car)) return;
    if (keyboard_check_pressed(vk_tab)) {
        if (e.menu == 1) e.menu = 2;
        else if (e.menu == 2) e.menu = 3;
        else e.menu = 1;
        e.sel = 0;
        e.aviso = "";
    }
    if (e.menu == 2) {
        if (keyboard_check_pressed(ord("1"))) global.mod_dist_comprar(1);
        if (keyboard_check_pressed(ord("2"))) global.mod_dist_comprar(2);
        return;
    }
    if (e.menu == 3) {
        var _cc = global.mod_carb_cfg();
        if (!is_undefined(_cc) && global.mod_dist_tipo(car) == 1) {
            if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod 3;
            if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + 2) mod 3;
            var _d3 = 0;
            if (keyboard_check_pressed(vk_right)) _d3 = 1;
            if (keyboard_check_pressed(vk_left)) _d3 = -1;
            if (_d3 != 0) {
                switch (e.sel) {
                    case 0: _cc.lenta = clamp(_cc.lenta + _d3, 20, 60); break;
                    case 1: _cc.agulha = clamp(_cc.agulha + _d3, 20, 80); break;
                    case 2: _cc.borboleta = clamp(_cc.borboleta + (_d3 * 5), 0, 100); break;
                }
                e.aviso = "";
            }
            if (keyboard_check_pressed(vk_f7)) {
                var _gi7 = global.mod_carb_gicle_ideal(max(1, car.carburador));
                _cc.lenta = round(_gi7 * 0.35);
                _cc.agulha = round(_gi7 * 0.90);
                _cc.borboleta = 50;
                e.aviso = "carburador no padrao";
            }
        }
        return;
    }
    if (keyboard_check_pressed(ord("3"))) global.mod_dist_remover();
    if (keyboard_check_pressed(vk_f7)) { global.mod_dist_padrao_modelo(); e.aviso = "padrao do modelo"; }
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _n = 10;
    if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod _n;
    if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + _n - 1) mod _n;
    var _dir = 0;
    if (keyboard_check_pressed(vk_right)) _dir = 1;
    if (keyboard_check_pressed(vk_left)) _dir = -1;
    if (_dir != 0) {
        switch (e.sel) {
            case 0: d.avanco_inicial = clamp(d.avanco_inicial + _dir, lim[0], lim[1]); break;
            case 1: d.cent_total = clamp(d.cent_total + (_dir * 2), 0, lim[2]); break;
            case 2: d.rpm_inicio = clamp(d.rpm_inicio + (_dir * 100), 600, 4000); break;
            case 3: d.rpm_fim = clamp(d.rpm_fim + (_dir * 100), 1200, lim[3]); break;
            case 4: d.curva = clamp(d.curva + _dir, 0, 2); break;
            case 5: d.vac_total = clamp(d.vac_total + _dir, 0, lim[4]); break;
            case 6: d.vac_limiar = clamp(d.vac_limiar + (_dir * 0.1), -0.8, 1); break;
            case 7: d.dwell = clamp(d.dwell + _dir, lim[5], lim[6]); break;
            case 8: d.desgaste = clamp(d.desgaste + (_dir * 5), 0, 100); break;
            case 9: d.ganho = clamp(d.ganho + (_dir * 5), 0, lim[7]); break;
        }
        e.aviso = "";
    }
};

// roda o modelo com um parametro no valor dado e devolve o bonus resultante
global.mod_dist_sim = function(_sel, _val) {
    var d = global.mod_dist_cfg();
    var _k = ["avanco_inicial", "cent_total", "rpm_inicio", "rpm_fim", "curva", "vac_total", "vac_limiar", "dwell", "desgaste", "ganho"];
    var _old = variable_struct_get(d, _k[_sel]);
    variable_struct_set(d, _k[_sel], _val);
    global.mod_dist_calcular();
    global.mod_dist_fator();
    var _b = global.mod_dist_estado.bonus;
    variable_struct_set(d, _k[_sel], _old);
    global.mod_dist_calcular();
    global.mod_dist_fator();
    return _b;
};

// tela do carburador: circuitos de lenta, agulha e borboleta
global.mod_carb_ui = function() {
    var e = global.mod_dist_estado;
    var car = global.mod_dist_car();
    draw_set_font(-1);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    var _x = 250;
    var _y = 40;
    var _w = 780;
    var _h = 420;
    draw_set_alpha(0.93);
    draw_set_color(c_black);
    draw_rectangle(_x, _y, _x + _w, _y + _h, false);
    draw_set_alpha(1);
    draw_set_color(make_color_rgb(60, 130, 90));
    draw_rectangle(_x, _y, _x + _w, _y + 34, false);
    draw_set_color(c_white);
    if (is_undefined(car)) {
        draw_text(_x + 12, _y + 8, "CARBURADOR");
        draw_text(_x + 20, _y + 60, "sem carro selecionado");
        return;
    }
    draw_text(_x + 12, _y + 8, "CARBURADOR - tipo " + string(max(1, car.carburador)));
    var cfg = global.mod_carb_cfg();
    if (is_undefined(cfg) || global.mod_dist_tipo(car) != 1) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 20, _y + 60, "Este motor e injetado: nao tem carburador para ajustar.");
        draw_set_color(c_white);
        return;
    }
    var _labels = ["Gicle de lenta", "Agulha (media)", "Borboleta"];
    var _vals = [cfg.lenta, cfg.agulha, cfg.borboleta];
    for (var i = 0; i < 3; i++) {
        var _ry = _y + 54 + (i * 30);
        if (i == e.sel) {
            draw_set_color(make_color_rgb(50, 70, 60));
            draw_rectangle(_x + 20, _ry - 2, _x + _w - 20, _ry + 24, false);
        }
        draw_set_color(c_white);
        draw_text(_x + 30, _ry + 4, _labels[i]);
        draw_set_color(make_color_rgb(255, 210, 120));
        draw_text(_x + _w - 120, _ry + 4, string(_vals[i]));
    }
    var _d1 = ["Mistura em marcha lenta e na saida.",
               "Mistura na faixa media (cruzeiro).",
               "Abertura inicial da borboleta."];
    var _d2 = ["Baixo = pobre, falha frio. Alto = rico, afoga e suja.",
               "Baixo = pobre em media. Alto = rico, perde potencia e gasta.",
               "Baixo = lenta baixa. Alto = lenta alta e resposta brusca."];
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 30, _y + 158, _d1[e.sel]);
    draw_text(_x + 30, _y + 176, _d2[e.sel]);
    var _gi = global.mod_carb_gicle_ideal(max(1, car.carburador));
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 30, _y + 214, "gicle principal ideal deste modelo: " + string(_gi));
    draw_set_color(make_color_rgb(170, 200, 255));
    if (variable_global_exists("afr")) {
        draw_text(_x + 30, _y + 240, "AFR atual: " + string(round(global.afr * 10) / 10) + "   (14.7 = estequeometrico)");
    }
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 30, _y + _h - 60, "setas ajustam   F7 padrao   TAB proxima aba   F6 fecha");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 30, _y + _h - 36, e.aviso);
    draw_set_color(c_white);
};

// categoria de compra exclusiva de distribuidores
global.mod_dist_loja = function() {
    var e = global.mod_dist_estado;
    var modelo = global.mod_dist_modelo();
    draw_set_font(-1);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    var _x = 250;
    var _y = 40;
    var _w = 780;
    var _h = 400;
    draw_set_alpha(0.93);
    draw_set_color(c_black);
    draw_rectangle(_x, _y, _x + _w, _y + _h, false);
    draw_set_alpha(1);
    draw_set_color(make_color_rgb(200, 60, 60));
    draw_rectangle(_x, _y, _x + _w, _y + 34, false);
    draw_set_color(c_white);
    draw_text(_x + 12, _y + 8, "LOJA - DISTRIBUIDORES");
    draw_set_halign(fa_right);
    draw_text(_x + _w - 12, _y + 8, "dinheiro: $" + string(global.cash));
    draw_set_halign(fa_left);
    global.mod_dist_draw_icon(_x + 150, _y + 210, 300);
    for (var i = 0; i < 2; i++) {
        var _m = i + 1;
        var _ry = _y + 56 + (i * 112);
        var _inst = (modelo == _m);
        draw_set_color(_inst ? make_color_rgb(45, 80, 55) : make_color_rgb(32, 32, 40));
        draw_rectangle(_x + 300, _ry, _x + _w - 20, _ry + 98, false);
        draw_set_color(c_white);
        draw_text(_x + 312, _ry + 8, "[" + string(_m) + "]  " + global.mod_dist_nome[_m]);
        var _p = global.mod_dist_preco[_m];
        draw_text(_x + 312, _ry + 28, (_p <= 0) ? "incluso de fabrica" : "$" + string(_p));
        var _lim = global.mod_dist_limites(_m);
        draw_set_color(make_color_rgb(180, 190, 210));
        draw_text(_x + 312, _ry + 50, "avanco ate " + string(_lim[1]) + "   centrifugo " + string(_lim[2]) + "   ate " + string(_lim[3]) + " rpm");
        draw_text(_x + 312, _ry + 68, "vacuo ate " + string(_lim[4]) + "   dwell " + string(_lim[5]) + "-" + string(_lim[6]) + "   ganho ate " + string(_lim[7]) + "%");
        if (_inst) {
            draw_set_color(make_color_rgb(120, 255, 140));
            draw_text(_x + _w - 140, _ry + 8, "INSTALADO");
        }
    }
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 300, _y + _h - 62, "[1] original   [2] competicao   TAB ajustar   F6 fechar");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 300, _y + _h - 40, e.aviso);
    draw_set_color(c_white);
};

// motor carburado = unico que aceita distribuidor
global.mod_dist_pode = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return false;
    return (global.mod_dist_tipo(car) == 1);
};

// ---- potencia real: hp = torque(Nm) * rpm / K ----
// A potencia deixa de ser uma curva propria e passa a ser derivada do torque
// instantaneo e do giro, como num motor de verdade (P = T * w).
// Em unidades imperiais K = 5252 (lb-ft). Aqui o torque esta em Nm e o jogo
// mede hp, entao K = 7121 em unidades puras; 0.626 e a calibracao contra a
// propria curva de torque do jogo, para o pico de torque*rpm cair exatamente
// no maxhp declarado do motor (medido nas tres faixas: 45, 99 e 171 hp).
global.pot_hp = function(_torque, _rpm, _maxtorque, _maxrpm, _maxhp) {
    if (_maxhp <= 0 || _maxtorque <= 0 || _maxrpm <= 0) return 0;
    var _k = 0.626 * _maxtorque * _maxrpm / _maxhp;
    return (_torque * _rpm) / _k;
};

// =====================================================================
//  MOTOR: um lugar so para todo efeito de peca/ajuste
// =====================================================================
// leitura segura de campo (save antigo pode nao ter a chave)
global.motor_campo = function(_car, _nome) {
    if (is_undefined(_car)) return 0;
    if (!variable_struct_exists(_car, _nome)) return 0;
    var _v = variable_struct_get(_car, _nome);
    if (is_undefined(_v)) return 0;
    return _v;
};

// efeito conjunto das pecas no torque (ganho pequeno e cumulativo)
global.motor_pecas = function(_car) {
    var _m = 1;
    _m *= 1 + (0.010 * min(global.motor_campo(_car, "pistao"), 3));
    _m *= 1 + (0.005 * min(global.motor_campo(_car, "bielas"), 3));
    _m *= 1 + (0.008 * min(global.motor_campo(_car, "bloco"), 3));
    _m *= 1 + (0.006 * min(global.motor_campo(_car, "vira"), 3));
    _m *= 1 + (0.015 * min(global.motor_campo(_car, "filtro"), 2));
    _m *= 1 + (0.020 * min(global.motor_campo(_car, "admissao"), 2));
    _m *= 1 + (0.018 * min(global.motor_campo(_car, "escape"), 2));
    _m *= 1 + (0.018 * min(global.motor_campo(_car, "pipe"), 2));
    return _m;
};

// cabecote do carro do jogador (evita aspas dentro do CSX)
global.motor_cabecote_player = function() {
    return global.motor_campo(global.mod_dist_car(), "cabecote");
};

// cabecote troca o conjunto de valvulas (+4 por nivel, teto 24)
global.motor_valvulas = function(_base, _cab) {
    if (is_undefined(_base)) _base = 8;
    return clamp(_base + (4 * min(_cab, 3)), 8, 24);
};

// boost / deslocamento: o que hoje vive em _engine_multiplier
global.phys_engine_fator = function(_turbo, _piston, _litros) {
    if (_turbo > 0) {
        return clamp(1 + (power(_turbo, 1.3) * 0.35), 1, 2);
    }
    var _cil = _piston / max(1, _piston);
    return clamp(0.6 + (_cil * 0.35) + (_litros * 0.05), 0.9, 1.35);
};

// faixa ideal de AFR por combustivel e giro (mesma tabela do jogo)
global.motor_afr_ideal = function(_rpm, _rpm_max, _turbo, _fuel) {
    var _ratio = clamp(_rpm / max(1, _rpm_max), 0, 1);
    var _bmin = 12.35, _amin = (_turbo > 0) ? 11.76 : 12.35;
    if (_fuel == 2) { _bmin = 8.82; _amin = (_turbo > 0) ? 7.02 : 7.47; }
    else if (_fuel == 3) { _bmin = 6.08; _amin = (_turbo > 0) ? 4.74 : 4.99; }
    else if (_fuel == 4) { _bmin = 1.53; _amin = (_turbo > 0) ? 1.12 : 1.19; }
    return lerp(_bmin, _amin, power(_ratio, 1.8));
};

// AFR do carburador: gicle ideal por tipo + blend do 2o corpo + circuitos do mod
global.motor_afr = function(_car, _rpm, _rpm_max, _turbo, _fuel) {
    if (is_undefined(_car)) return 14.7;
    var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, "carburador")) : 0;
    if (_carb <= 0) return 14.7;
    var _tabela_ideal = [38, 42, 46, 48, 50, 53, 56, 60];
    var _tabela_sens = [0.018, 0.022, 0.026, 0.030, 0.034, 0.038, 0.042, 0.048];
    var _i = clamp(_carb, 1, 8) - 1;
    var _ratio = clamp(_rpm / max(1, _rpm_max), 0, 1);
    var _min = global.motor_afr_ideal(_rpm, _rpm_max, _turbo, _fuel);
    var _bmax = 14.99;
    var _amax = (_turbo > 0) ? 12.35 : 12.94;
    if (_fuel == 2) { _bmax = 9.18; _amax = (_turbo > 0) ? 7.38 : 7.74; }
    else if (_fuel == 3) { _bmax = 6.4; _amax = (_turbo > 0) ? 4.99 : 5.25; }
    else if (_fuel == 4) { _bmax = 1.61; _amax = (_turbo > 0) ? 1.19 : 1.26; }
    var _bmin = 12.35;
    if (_fuel == 2) { _bmin = 8.82; } else if (_fuel == 3) { _bmin = 6.08; } else if (_fuel == 4) { _bmin = 1.53; }
    var _max = lerp(_bmax, _amax, power(_ratio, 1.8));
    var _mid = (_min + _max) / 2;
    var _gicle = global.motor_campo(_car, "gicle");
    var _gicle2 = global.motor_campo(_car, "gicle_2nd");
    var _ativo = _gicle;
    if (_carb >= 3 && _rpm >= (_rpm_max / 2)) {
        var _blend = power(clamp((_rpm - (_rpm_max / 2)) / (_rpm_max / 2), 0, 1), 0.7);
        _ativo = lerp(_gicle, _gicle2, _blend);
    }
    var _desvio = (_ativo - _tabela_ideal[_i]) + global.mod_carb_desvio(_ratio, global.mod_carb_cfg(), _tabela_ideal[_i]);
    return max(_mid - (_desvio * _tabela_sens[_i] * _mid), 0.5);
};

// AFR dos injetados: duracao de injecao define o desvio (70 = referencia)
global.motor_afr_inj = function(_rpm, _rpm_max, _turbo, _fuel, _injecao) {
    var _ideal = global.motor_afr_ideal(_rpm, _rpm_max, _turbo, _fuel);
    var _desvio = clamp((_injecao - 70) / 30, -1, 1);
    return max(_ideal * (1 - (_desvio * 0.18)), 0.5);
};

// mistura -> multiplicador (mesma curva do jogo: 1.45 ideal, 0.70 pessimo)
global.motor_mistura_mult = function(_afr, _rpm, _rpm_max, _turbo, _fuel) {
    var _ideal_min = global.motor_afr_ideal(_rpm, _rpm_max, _turbo, _fuel);
    var _dist = abs(_afr - _ideal_min) / max(0.001, _ideal_min * 0.5);
    return lerp(1.45, 0.70, clamp(_dist, 0, 1));
};

// ignicao: ~28 graus e o ponto bom; longe disso perde torque
global.motor_ignicao_mult = function(_ignicao) {
    if (is_undefined(_ignicao)) return 1;
    if (_ignicao <= 0) return 1;   // sem leitura de ignicao: neutro, nao penaliza
    var _desvio = (_ignicao - 28) / 10;
    return clamp(1 - (abs(_desvio) * 0.045), 0.85, 1.04);
};

// desgaste: 70 = peca boa; abaixo perde torque ate -40%
global.motor_desgaste = function(_car) {
    if (is_undefined(_car)) return 1;
    var _campos = ["vpistao", "vbielas", "vcabecote", "vcomando", "vvira", "vbloco"];
    var _m = 1;
    for (var _i = 0; _i < array_length(_campos); _i++) {
        // 0 e desgaste total, nao "campo ausente": checar existencia antes
        if (variable_struct_exists(_car, _campos[_i])) {
            var _v = variable_struct_get(_car, _campos[_i]);
            if (!is_undefined(_v) && _v < 70) {
                _m *= clamp(1 - ((1 - (_v / 70)) * 0.06), 0.60, 1);
            }
        }
    }
    return _m;
};

// compositor: TODO efeito de peca/ajuste entra aqui, uma vez
global.motor_torque = function(_car, _rpm, _rpm_max, _afr) {
    var _m = global.motor_pecas(_car);
    _m *= global.phys_engine_fator(global.turbo, global.piston, global.litros);
    var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, "carburador")) : 0;
    var _afr_eff = _afr;
    if (_carb <= 0) _afr_eff = global.motor_afr_inj(_rpm, _rpm_max, global.turbo, global.fuel, global.injecao);
    _m *= global.motor_mistura_mult(_afr_eff, _rpm, _rpm_max, global.turbo, global.fuel);
    _m *= global.motor_ignicao_mult(global.ignicao);
    _m *= global.motor_desgaste(_car);
    return _m;
};

// =====================================================================
//  FISICA DE TRACAO (modelo derivado do Rigs of Rods)
//  F = T * relacao * efic / raio ; limite = mu * N ; excedente = patinacao
// =====================================================================
global.phys_slip = 0;

global.phys_cfg = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return undefined;
    if (!variable_struct_exists(car, "phys_cfg") || is_undefined(car.phys_cfg)) {
        var _d = global.phys_def;
        car.phys_cfg = { raio: _d.raio, eficiencia: _d.eficiencia, mu: _d.mu, share: _d.share, embreagem: _d.embreagem, inercia: _d.inercia };
    }
    return car.phys_cfg;
};

// calibracao: reproduz o termo antigo (torque*hp)/(weight*750) em 1a marcha.
// weight esta em TONELADAS, entao a massa em kg entra x1000. Usa o hp CORRENTE
// (nao o maxhp) para casar exatamente com a formula antiga; o 0.90 e a
// eficiencia de referencia, entao abaixar a eficiencia realmente tira forca.
global.phys_k = function(_hp, _raio, _g1) {
    if (_g1 <= 0 || _raio <= 0 || _hp <= 0) return 0;
    return (_hp * _raio * 1000) / (750 * _g1 * 0.90);
};

global.phys_forca = function(_torque, _relacao, _raio, _efic) {
    if (_raio <= 0 || _relacao <= 0) return 0;
    return (_torque * _relacao * _efic) / _raio;
};

global.phys_limite = function(_massa_kg, _mu, _share) {
    return _massa_kg * 9.81 * _mu * _share;
};

// aderencia por composto e temperatura
global.phys_mu = function(_pneu, _temp) {
    var _base = 0.92;
    switch (_pneu) {
        case 1: _base = 1.05; break;
        case 2: _base = 1.25; break;
        case 3: _base = 0.78; break;
    }
    var _ideal = (_pneu == 3) ? 55 : 80;
    if (is_undefined(_temp) || _temp <= 0) return _base;
    var _desvio = abs(_temp - _ideal) / 60;
    return _base * clamp(1 - (_desvio * 0.35), 0.6, 1);
};

// termo base de aceleracao, na mesma escala do jogo; atualiza global.phys_slip
global.phys_base = function(_torque, _relacao, _raio, _massa_kg, _k, _efic, _mu, _share) {
    global.phys_slip = 0;
    if (_massa_kg <= 0 || _raio <= 0 || _relacao <= 0 || _k <= 0) return 0;
    var _f = global.phys_forca(_torque, _relacao, _raio, _efic);
    var _lim = global.phys_limite(_massa_kg, _mu, _share);
    if (_lim > 0 && _f > _lim) {
        global.phys_slip = clamp((_f - _lim) / _f, 0, 1);
        _f = _lim;
    }
    return (_f / _massa_kg) * _k;
};

// inercia: gira livre com a embreagem aberta (rpm += dt * T / I)
global.phys_rev = function(_torque, _response, _dt) {
    var _i = max(0.02, global.phys_cfg().inercia) / clamp(_response, 0.5, 2);
    return ((_torque / _i) * 9.549) * (_dt / 60);
};

// embreagem: capacidade e torque transmitido (opt-in via phys_cfg.embreagem)
global.phys_clutch_cap = function(_maxtorque, _g1) {
    return 1.5 * _maxtorque * abs(_g1);
};

global.phys_clutch = function(_torque_motor, _slip, _abertura, _cap) {
    if (_cap <= 0) return _torque_motor;
    var _t = clamp(_slip * _abertura * _cap, -_cap, _cap);
    _t *= 1 - exp(-abs(_slip));
    if (abs(_t) > abs(_torque_motor)) _t = _torque_motor;
    return _t;
};

// ---- carburador: circuitos de lenta, agulha e borboleta ----
global.mod_carb_gicle_ideal = function(_carb) {
    switch (_carb) {
        case 1: return 38;
        case 2: return 42;
        case 3: return 46;
        case 4: return 48;
        case 5: return 50;
        case 6: return 53;
        case 7: return 56;
        case 8: return 60;
    }
    return 50;
};

// configuracao propria do carro; padrao = gicle ideal do modelo (neutro)
global.mod_carb_cfg = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return undefined;
    if (!variable_struct_exists(car, "carb_cfg") || is_undefined(car.carb_cfg)) {
        var _gi = global.mod_carb_gicle_ideal(max(1, car.carburador));
        car.carb_cfg = {
            lenta: round(_gi * 0.35),
            agulha: round(_gi * 0.90),
            borboleta: 50
        };
    }
    return car.carb_cfg;
};

// desvio equivalente em pontos de gicle, somado ao desvio do gicle principal
global.mod_carb_desvio = function(_rpm_ratio, _cfg, _gicle_ideal) {
    if (is_undefined(_cfg)) return 0;
    var _il = round(_gicle_ideal * 0.35);
    var _ia = round(_gicle_ideal * 0.90);
    // janelas separadas: lenta domina abaixo de 0.08, agulha no meio, borboleta na saida
    var _w_l = 1 - clamp((_rpm_ratio - 0.08) / 0.12, 0, 1);
    var _w_a = clamp(1 - abs(_rpm_ratio - 0.45) / 0.30, 0, 1);
    var _w_b = 1 - clamp(_rpm_ratio / 0.12, 0, 1);
    var _d = 0;
    _d += (_cfg.lenta - _il) * _w_l * 0.80;
    _d += (_cfg.agulha - _ia) * _w_a * 0.60;
    _d += (_cfg.borboleta - 50) * _w_b * 0.40;
    return _d;
};

// sprite do distribuidor: caminho relativo no GMS resolve para a pasta de SAVE,
// por isso tentamos working_directory (pasta do jogo) antes do relativo.
global.mod_dist_spr = function() {
    var e = global.mod_dist_estado;
    if (e.spr >= 0) return e.spr;
    if (e.spr_try == 0) {
        e.spr_try = 1;
        var _p = working_directory + "assets/Mod_assets/distribuidor.png";
        if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        if (e.spr < 0) {
            _p = "assets/Mod_assets/distribuidor.png";
            if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        }
        if (e.spr < 0) {
            _p = "Mod_assets/distribuidor.png";
            if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        }
    }
    return e.spr;
};

// icone quadrado (256x256) recortado da ilustracao
global.mod_dist_icon = function() {
    var e = global.mod_dist_estado;
    if (e.icon >= 0) return e.icon;
    if (e.icon_try == 0) {
        e.icon_try = 1;
        // este runner so resolve arquivos na raiz da pasta de save
        // (subpasta e caminho absoluto retornam -1)
        if (file_exists("distribuidor_icon.png")) e.icon = sprite_add("distribuidor_icon.png", 1, false, false, 0, 0);
        if (e.icon < 0 && file_exists("distribuidor.png")) e.icon = sprite_add("distribuidor.png", 1, false, false, 0, 0);
    }
    return e.icon;
};

// desenha centralizado em (_cx,_cy) com altura _h em pixels. Nunca passa sprite -1:
// se nenhuma arte carregar, cai no icone do carburador.
global.mod_dist_draw_icon = function(_cx, _cy, _h) {
    var _sp = global.mod_dist_icon();
    if (_sp < 0) _sp = global.mod_dist_spr();
    var _sw = 0;
    var _sh = 0;
    if (_sp >= 0) {
        _sw = sprite_get_width(_sp);
        _sh = sprite_get_height(_sp);
    }
    if (_sw > 0 && _sh > 0) {
        var _sc = _h / _sh;
        draw_sprite_ext(_sp, 0, _cx - (_sw * _sc) / 2, _cy - _h / 2, _sc, _sc, 0, c_white, 1);
    } else {
        // sem arte: marcador alinhado, sem sprite deslocado
        draw_set_color(make_color_rgb(70, 70, 90));
        draw_roundrect(_cx - (_h * 0.42), _cy - (_h * 0.42), _cx + (_h * 0.42), _cy + (_h * 0.42), false);
        draw_set_color(c_white);
        draw_set_halign(fa_center);
        draw_set_valign(fa_middle);
        draw_text(_cx, _cy, "DIST");
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
    }
};

// registra os dois itens na base do inventario (idempotente)
global.mod_dist_db = function() {
    if (!variable_global_exists("item_db")) return;
    if (!is_struct(global.item_db)) return;
    if (variable_struct_exists(global.item_db, "distribuidor_original")) return;
    variable_struct_set(global.item_db, "distribuidor_original", {
        id: "distribuidor_original", name: "Distribuidor Original", category: "peca_externa",
        value_label: "Original", value_default: 1, sprite_name: "spr_venture_externa", image_index: 17,
        campo: "distribuidor", vcampo: "", campo_val: 1
    });
    variable_struct_set(global.item_db, "distribuidor_competicao", {
        id: "distribuidor_competicao", name: "Distribuidor Competicao", category: "peca_externa",
        value_label: "Competicao", value_default: 2, sprite_name: "spr_venture_externa", image_index: 17,
        campo: "distribuidor", vcampo: "", campo_val: 2
    });
};

global.mod_dist_ui = function() {
    var e = global.mod_dist_estado;
    global.mod_dist_db();
    global.mod_dist_ativo = (global.mod_dist_modelo() > 0) ? 1 : 0;
    if (!global.mod_dist_pode()) { e.menu = 0; return; }
    global.mod_dist_input();
    if (e.menu == 0 && !global.mod_dist_ativo) return;
    if (variable_global_exists("mod_dist_debug") && global.mod_dist_debug == 1) {
        var _f = file_text_open_write("mod_diag.txt");
        file_text_write_string(_f, "room=" + string(room) + " player=" + string(instance_exists(obj_player)));
        file_text_write_string(_f, " carros=" + string(array_length(global.car_data)) + " cash=" + string(global.cash));
        file_text_write_string(_f, " modelo=" + string(global.mod_dist_modelo()) + " menu=" + string(e.menu) + " ativo=" + string(global.mod_dist_ativo));
        file_text_write_string(_f, " avanco=" + string(global.mod_dist_calcular()) + " fator=" + string(global.mod_dist_fator()));
        file_text_close(_f);
    }
    var modelo = global.mod_dist_modelo();
    var car = global.mod_dist_car();
    draw_set_font(-1);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    if (e.menu == 0) {
        if (modelo <= 0) return;
        draw_set_alpha(0.55);
        draw_set_color(c_black);
        draw_rectangle(8, 8, 360, 46, false);
        draw_set_alpha(1);
        draw_set_color(c_white);
        draw_text(14, 12, global.mod_dist_nome[modelo] + "  " + string(round(e.avanco)) + " graus");
        draw_set_color(make_color_rgb(170, 200, 255));
        draw_text(14, 27, "cent " + string(round(e.cent)) + "  vac " + string(round(e.vac)) + "  dwell " + string(round(e.dwell_ef)) + "  F6 menu");
        draw_set_color(c_white);
        return;
    }
    if (e.menu == 2) { global.mod_dist_loja(); return; }
    if (e.menu == 3) { global.mod_carb_ui(); return; }
    var _x = 250;
    var _y = 40;
    var _w = 780;
    var _h = 620;
    draw_set_alpha(0.9);
    draw_set_color(c_black);
    draw_rectangle(_x, _y, _x + _w, _y + _h, false);
    draw_set_alpha(1);
    draw_set_color(make_color_rgb(200, 60, 60));
    draw_rectangle(_x, _y, _x + _w, _y + 34, false);
    draw_set_color(c_white);
    draw_text(_x + 12, _y + 8, "DISTRIBUIDOR - " + global.mod_dist_nome[modelo]);
    global.mod_dist_draw_icon(_x + 150, _y + 200, 300);
    if (modelo <= 0) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 310, _y + 60, "Este motor e injetado: distribuidor so serve em carburador.");
        draw_set_color(c_white);
        return;
    }
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _labels = ["Avanco inicial (graus)", "Centrifugo total (graus)", "RPM de inicio", "RPM de fim", "Curva (0 linear, 1 progressiva, 2 rapida)", "Vacuo total (graus)", "Limiar de vacuo (bar)", "Dwell (graus)", "Desgaste (%)", "Ganho (%)"];
    var _vals = [d.avanco_inicial, d.cent_total, d.rpm_inicio, d.rpm_fim, d.curva, d.vac_total, d.vac_limiar, d.dwell, d.desgaste, d.ganho];
    var _i;
    for (_i = 0; _i < 10; _i++) {
        var _ry = _y + 50 + (_i * 30);
        if (_i == e.sel) {
            draw_set_color(make_color_rgb(60, 60, 90));
            draw_rectangle(_x + 300, _ry - 2, _x + _w - 14, _ry + 24, false);
        }
        draw_set_color(c_white);
        draw_text(_x + 310, _ry + 4, _labels[_i]);
        draw_set_color(make_color_rgb(255, 210, 120));
        draw_text(_x + _w - 120, _ry + 4, string(_vals[_i]));
    }
    // explicacao do parametro selecionado + efeito real de min/atual/max
    var _d1 = ["Avanco inicial: ponto base da ignicao.",
               "Centrifugo total: avanco extra que sobe com o giro.",
               "RPM de inicio: giro onde a curva comeca a entrar.",
               "RPM de fim: giro onde o avanco satura.",
               "Curva: formato da entrada das duas molas.",
               "Vacuo total: avanco extra em carga leve.",
               "Limiar de vacuo: MAP onde o vacuo comeca.",
               "Dwell: tempo de carga da bobina.",
               "Desgaste do platinado e do rotor.",
               "Ganho: quanto do teto do modelo o carro usa."];
    var _d2 = ["Baixo = fraco e lento. Alto = torque, mas detona.",
               "Baixo = morre em alta. Alto = potencia, risco de knock.",
               "Baixo = antecipa cedo. Alto = amarrado ate subir.",
               "Baixo = trava cedo e perde alta. Alto = estica a curva.",
               "0 linear, 1 progressiva, 2 rapida (mola leve).",
               "Baixo = consumo pior. Alto = economia, mas detona.",
               "Baixo = entra cedo. Alto = so em vacuo forte.",
               "Baixo = faisca fraca. Alto = forte, bobina esquenta.",
               "Alto = faisca irregular e perda de potencia.",
               "Baixo = pouco proveito. Alto = 100% do potencial."];
    var _pmin = [lim[0], 0, 600, 1200, 0, 0, -0.8, lim[5], 0, 0];
    var _pmax = [lim[1], lim[2], 4000, lim[3], 2, lim[4], 1, lim[6], 100, lim[7]];
    var _bmin = global.mod_dist_sim(e.sel, _pmin[e.sel]);
    var _bmax = global.mod_dist_sim(e.sel, _pmax[e.sel]);
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 310, _y + 352, _d1[e.sel]);
    draw_text(_x + 310, _y + 368, _d2[e.sel]);
    draw_set_color(make_color_rgb(255, 150, 150));
    draw_text(_x + 310, _y + 394, "min " + string(round(_bmin * 100)) + "%");
    draw_set_color(c_white);
    draw_text(_x + 380, _y + 394, "atual " + string(round(e.bonus * 100)) + "%");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 480, _y + 394, "max " + string(round(_bmax * 100)) + "%");
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 310, _y + 412, "(bonus de potencia do motor neste giro/carga)");
    draw_set_color(make_color_rgb(170, 200, 255));
    draw_text(_x + 310, _y + _h - 110, "avanco " + string(round(e.avanco)) + " (ideal " + string(round(e.ideal)) + ")  limite knock " + string(round(global.mod_dist_knock_limite(car))) + "  knock " + string(round(e.knock)));
    draw_text(_x + 310, _y + _h - 92, "bonus " + string(round(e.bonus * 100)) + "%   faisca " + string(round(e.q * 100)) + "% (" + string(round(e.dwell_ms * 10) / 10) + "ms)   dinheiro $" + string(global.cash));
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 310, _y + _h - 70, "setas ajustam (limite do modelo)   TAB loja   F7 padrao   [3] voltar ao original");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 310, _y + _h - 50, e.aviso);
    draw_set_color(c_white);
};

// ---- cambio: relacao final, limites de ajuste e teto de velocidade ----
global.cambio_rel_min = 2.60;
global.cambio_rel_max = 5.10;
global.cambio_rel_ref = 3.73;
global.cambio_passo = 0.01;
global.cambio_limite_pct = 0.70;
global.cambio_vmax = 500;
global.cambio_custo = 775;

global.cambio_cfg = function() {
    return { rel_min: global.cambio_rel_min, rel_max: global.cambio_rel_max,
             rel_ref: global.cambio_rel_ref, passo: global.cambio_passo,
             custo: global.cambio_custo };
};

global.cambio_rel_valor = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return global.cambio_rel_ref;
    if (!variable_struct_exists(car, "relacao_final")) return global.cambio_rel_ref;
    var _r = car.relacao_final;
    if (!is_real(_r) || _r <= 0) return global.cambio_rel_ref;
    return clamp(_r, global.cambio_rel_min, global.cambio_rel_max);
};

global.cambio_rel_mult = function() {
    if (global.cambio_rel_ref <= 0) return 1;
    return global.cambio_rel_valor() / global.cambio_rel_ref;
};

global.cambio_txt_rel = function() {
    switch (global.idioma) {
        case 1: return "FINAL DRIVE";
        case 2: return "RELACION FINAL";
        case 3: return "RAPPORTO FINALE";
        case 4: return "RAPPORT FINAL";
        case 5: return "主减速比";
        case 6: return "ГЛАВНАЯ ПАРА";
        case 7: return "ファイナル";
        default: return "RELAÇÃO FINAL";
    }
};


// ================= CAMBIO: camada unica (cg v3) =================
global.cg_versao = 3;
global.cg_migrado = 0;
global.cg_probe_t = 0;
global.cg_acc = 0;
global.cg_drag = 0;
global.cg_fv = 1;

global.cg_num = function(_v, _def) {
    if (!is_real(_v)) return _def;
    if (_v != _v) return _def;
    if (abs(_v) > 100000) return _def;
    return _v;
};

global.cg_get = function(_st, _k, _def) {
    if (!is_struct(_st)) return _def;
    if (!variable_struct_exists(_st, _k)) return _def;
    return variable_struct_get(_st, _k);
};

global.cg_ref = function() { return global.cambio_rel_ref; };

global.cg_rel = function(_car) {
    var _r = global.cg_num(global.cg_get(_car, "relacao_final", global.cambio_rel_ref), global.cambio_rel_ref);
    return clamp(_r, global.cambio_rel_min, global.cambio_rel_max);
};

global.cg_base = function(_car, _i) {
    var _g = global.cg_get(_car, "gears_mod", -1);
    if (!is_array(_g)) return -1;
    if (_i < 0 || _i >= array_length(_g)) return -1;
    var _v = global.cg_num(_g[_i], -1);
    if (_v <= 0) return -1;
    return _v;
};

global.cg_ratio = function(_car, _i) {
    var _b = global.cg_base(_car, _i);
    if (_b < 0) return -1;
    return _b;
};

global.cg_topo = function(_car, _cand, _i) {
    var _r = global.cg_num(_cand[_i], 0);
    if (_r <= 0) return 0;
    var _corte = global.cg_num(global.cg_get(_car, "rpm", 8500), 8500);
    return ((_corte / _r) * 60) / 1000;
};

// "" = ok, senao a descricao do problema
global.cg_validar = function(_car, _cand, _rel, _fab) {
    var _n = array_length(_fab);
    if (!is_array(_cand)) return "marchas nao carregadas";
    if (array_length(_cand) != _n) return "esperava " + string(_n) + " marchas e veio " + string(array_length(_cand));
    for (var i = 0; i < _n; i++) {
        var _v = global.cg_num(_cand[i], -1);
        if (_v < 0.20) return "marcha " + string(i + 1) + " invalida";
        if (_v > 15.0) return "marcha " + string(i + 1) + " longa demais";
        if (abs(_v - _fab[i]) > 0.70 * _fab[i]) {
            return "marcha " + string(i + 1) + " fora de +-70% de " + string(round(_fab[i] * 100) / 100);
        }
    }
    var _r = global.cg_num(_rel, -1);
    if (_r < global.cambio_rel_min || _r > global.cambio_rel_max) {
        return "relacao final fora de " + string(global.cambio_rel_min) + " a " + string(global.cambio_rel_max);
    }
    return "";
};

// caixa irreal NAO bloqueia: devolve aviso para pintar de amarelo
global.cg_avisos = function(_car, _cand, _rel, _fab) {
    var _n = array_length(_fab);
    var _m = "";
    for (var i = 1; i < _n; i++) {
        var _a = global.cg_num(_cand[i - 1], 0);
        var _b = global.cg_num(_cand[i], 0);
        if (_b >= _a) _m += "marcha " + string(i + 1) + " nao e mais longa que a " + string(i) + ". ";
        if (_a > 0 && _b > 0) {
            var _salto = 1 - (_b / _a);
            if (_salto < 0.08) _m += "marcha " + string(i + 1) + " quase igual a " + string(i) + ". ";
            if (_salto > 0.60) _m += "salto enorme da " + string(i) + " para a " + string(i + 1) + ". ";
        }
    }
    var _topo = global.cg_topo(_car, _cand, _n - 1);
    if (_topo > global.cambio_vmax * 1.5) _m += "ultima marcha longa demais (topo " + string(round(_topo)) + " km/h). ";
    if (_topo > 0 && _topo < 60) _m += "ultima marcha curta demais (topo " + string(round(_topo)) + " km/h). ";
    return _m;
};

global.cg_conferir = function(_car, _cand, _rel) {
    if (global.cg_num(global.cg_get(_car, "cambio_mod", 0), 0) != 1) return false;
    var _g = global.cg_get(_car, "gears_mod", -1);
    if (!is_array(_g)) return false;
    if (array_length(_g) != array_length(_cand)) return false;
    for (var i = 0; i < array_length(_cand); i++) {
        if (abs(global.cg_num(_g[i], -999) - _cand[i]) > 0.0001) return false;
    }
    if (abs(global.cg_num(global.cg_get(_car, "relacao_final", -999), -999) - _rel) > 0.0001) return false;
    return true;
};

// UNICO lugar que escreve gear_ratios / original_ratios / max_gears
global.cg_init_car = function(_inst, _car, _fab) {
    if (global.cg_num(global.cg_get(_car, "cg_versao", 0), 0) != global.cg_versao) {
        // esquema antigo guardava a razao BASE: converte para efetiva (x rel/ref)
        var _gmig = global.cg_get(_car, "gears_mod", -1);
        if (global.cg_num(global.cg_get(_car, "cambio_mod", 0), 0) == 1 && is_array(_gmig)) {
            var _cm = global.cg_rel(_car) / global.cg_ref();
            for (var _mi = 0; _mi < array_length(_gmig); _mi++) {
                var _mv = global.cg_num(_gmig[_mi], -1);
                if (_mv > 0) _gmig[_mi] = _mv * _cm;
            }
            _car.gears_mod = _gmig;
            if (global.cg_migrado != 1) {
                global.cg_migrado = 1;
                global.cg_diag("MIGRADO razoes base -> efetivas (x" + string(_cm) + ")");
            }
        }
        _car.cg_versao = global.cg_versao;
    }
    var _n = array_length(_fab);
    var _mult = global.cg_rel(_car) / global.cg_ref();
    var _gm = global.cg_get(_car, "gears_mod", -1);
    var _usar = (global.cg_num(global.cg_get(_car, "cambio_mod", 0), 0) == 1)
        && is_array(_gm) && array_length(_gm) == _n;
    // cambio_mod == 1 e a FLAG de "cambio liberado para editar" no Tiao
    // (05221:6771 tem_cambio). NAO zerar: sem ela o menu CAMBIO desaparece.
    var _gr = array_create(_n);
    var _or = array_create(_n);
    for (var i = 0; i < _n; i++) {
        _or[i] = _fab[i] * _mult;
        var _b = _usar ? global.cg_base(_car, i) : -1;
        _gr[i] = (_b > 0) ? _b : _or[i];
    }
    with (_inst) {
        gear_ratios = _gr;
        original_ratios = _or;
        max_gears = _n;
    }
    return _n;
};

global.cg_aplicar = function(_inst, _car, _cand, _rel, _fab) {
    var _erro = global.cg_validar(_car, _cand, _rel, _fab);
    if (_erro != "") return _erro;
    var _om = global.cg_get(_car, "cambio_mod", 0);
    var _og = global.cg_get(_car, "gears_mod", -1);
    var _orr = global.cg_get(_car, "relacao_final", global.cambio_rel_ref);
    _car.cambio_mod = 1;
    _car.gears_mod = CloneArray(_cand);
    _car.relacao_final = _rel;
    _car.cg_versao = global.cg_versao;
    if (!global.cg_conferir(_car, _cand, _rel)) {
        _car.cambio_mod = _om;
        _car.gears_mod = _og;
        _car.relacao_final = _orr;
        global.cg_init_car(_inst, _car, _fab);
        return "verificacao pos-gravacao falhou (rollback)";
    }
    global.cg_init_car(_inst, _car, _fab);
    return "";
};

global.cg_resumo = function(_car) {
    var _gm = global.cg_get(_car, "gears_mod", -1);
    if (!is_array(_gm)) _gm = [];
    var _s = "CG|v=" + string(global.cg_versao)
        + "|mod=" + string(global.cg_num(global.cg_get(_car, "cambio_mod", 0), 0))
        + "|rel=" + string(global.cg_rel(_car))
        + "|n=" + string(array_length(_gm));
    for (var i = 0; i < array_length(_gm); i++) {
        _s += "|r" + string(i) + "=" + string(global.cg_num(_gm[i], -1));
    }
    return _s;
};

global.cg_diag = function(_s) {
    var _f = file_text_open_append("cambio_save.txt");
    if (_f == -1) return 0;
    file_text_write_string(_f, _s);
    file_text_writeln(_f);
    file_text_close(_f);
    return 1;
};

global.cambio_erro_t = 0;
global.cambio_erro_txt = "";
global.cambio_erro = function(_txt) {
    global.cambio_erro_t = current_time;
    global.cambio_erro_txt = _txt;
};

global.cambio_aviso_t = 0;
global.cambio_aviso_txt = "";
global.cg_avisar = function(_txt) {
    global.cambio_aviso_t = current_time;
    global.cambio_aviso_txt = _txt;
};

global.cambio_gratis = 0;
global.cambio_pode_pagar = function(_custo) {
    var _ok = (global.cash >= _custo) || (global.cambio_gratis == 1);
    global.cg_diag((_ok ? "PODE PAGAR" : "SEM DINHEIRO")
        + " custo=" + string(floor(_custo)) + " cash=" + string(floor(global.cash)));
    return _ok;
};

global.cg_confirmar = function(_custo) {
    var _car = global.car_data[global.vehicles_array];
    var _inst = instance_find(obj_player, 0);
    if (!instance_exists(_inst)) return "sem player na cena";
    var _n = _inst.max_gears;
    if (!variable_global_exists("gear_temp_array") || !is_array(global.gear_temp_array)) {
        return "marchas nao carregadas no caderno";
    }
    if (array_length(global.gear_temp_array) != _n) {
        return "marchas nao carregadas no caderno";
    }
    var _cand = array_create(_n);
    for (var i = 0; i < _n; i++) _cand[i] = global.cg_num(global.gear_temp_array[i], -1);
    var _rel = global.cg_rel(_car);
    if (variable_global_exists("gear_temp_rel")) {
        _rel = global.cg_num(global.gear_temp_rel, _rel);
    }
    var _fab = _inst.original_ratios;

    var _erro = global.cg_validar(_car, _cand, _rel, _fab);
    if (_erro != "") {
        global.cambio_erro(_erro);
        global.cg_diag("RECUSADO " + _erro);
        return _erro;
    }
    var _av = global.cg_avisos(_car, _cand, _rel, _fab);
    if (_av != "") global.cg_avisar("CAMBIO IRREAL: " + _av);

    if (!global.cambio_pode_pagar(_custo)) {
        global.cambio_erro("FALTA DINHEIRO: $" + string(floor(_custo - global.cash)));
        return "sem dinheiro";
    }
    global.cash -= _custo;
    _erro = global.cg_aplicar(_inst, _car, _cand, _rel, _fab);
    if (_erro != "") {
        global.cash += _custo;
        global.cambio_erro(_erro);
        global.cg_diag("FALHOU " + _erro);
        return _erro;
    }
    SaveAllCars();
    SaveGame();
    global.cg_diag("GRAVOU " + global.cg_resumo(_car));
    return "";
};

// ================= POWERTRAIN (base: Rigs of Rods Engine.cpp) =================
// RoR: torque = max_torque * curva(rpm) * throttle ; clutch = clamp(slip * forca, +/-limite) ;
// forca na roda = T * relacao / raio ; o corte de giro corta torque, nao a velocidade.
// O rpm continua vindo da roda (o jogo nao separa eixo de motor), mas a curva de torque
// passa a ser a do RoR - cheia embaixo, o que faz relacao longa ainda puxar.
global.pt_shift_t = 0;
global.pt_shift_time = 0.22;

global.pt_pico = function(_cmd) {
    switch (_cmd) {
        case 1: return 0.35;
        case 2: return 0.50;
        case 3: return 0.65;
        case 4: return 0.55;
        default: return 0.45;
    }
};

global.pt_curva = function(_x, _pico) {
    _x = clamp(_x, 0, 1);
    var _d;
    if (_x < _pico) _d = (_pico - _x) / max(0.05, _pico);
    else _d = (_x - _pico) / max(0.05, 1 - _pico);
    return max(0.45, 1 - power(clamp(_d, 0, 1), 1.6));
};

global.pt_torque = function(_base, _rpm, _redline, _cmd) {
    var _x = _rpm / max(1, _redline);
    var _alvo = global.maxtorque * (0.5 + 0.125 * global.litros) * global.pt_curva(_x, global.pt_pico(_cmd));
    return max(_base, _alvo);
};

global.pt_ratio = function(_gear) {
    if (_gear <= 0) return 0;
    if (!instance_exists(obj_player)) return 0;
    var _i = _gear - 1;
    if (_i >= array_length(obj_player.gear_ratios)) return 0;
    return obj_player.gear_ratios[_i];
};

global.pt_rpm_roda = function() {
    var _r = global.pt_ratio(global.gear);
    if (_r <= 0) return 0;
    return global.kmh * _r * 16.667;
};

global.pt_gear_limit = function() {
    var _r = global.pt_ratio(global.gear);
    if (_r <= 0) return 0;
    return ((global.car_data[global.vehicles_array].rpm / _r) * 60) / 1000;
};

global.pt_em_corte = function() {
    // usa o rpm implicado pela velocidade na marcha ATUAL (nao o global.rpm,
    // que pode estar velho depois de uma troca)
    var _red = global.car_data[global.vehicles_array].rpm;
    return (global.pt_rpm_roda() >= _red - 50) ? 1 : 0;
};

global.pt_step = function() {
    if (global.pt_shift_t > 0) {
        global.pt_shift_t = max(0, global.pt_shift_t - global.dt);
    }
};
// ==============================================================================

global.mod_dist_carregar = function() {
    // o ini define os PADROES de uma compra nova; cada carro guarda a sua config
    if (file_exists("mod_distribuidor.ini")) {
        ini_open("mod_distribuidor.ini");
        global.mod_dist_def.avanco_inicial = ini_read_real("distribuidor", "avanco_inicial", global.mod_dist_def.avanco_inicial);
        global.mod_dist_def.cent_total = ini_read_real("distribuidor", "cent_total", global.mod_dist_def.cent_total);
        global.mod_dist_def.rpm_inicio = ini_read_real("distribuidor", "rpm_inicio", global.mod_dist_def.rpm_inicio);
        global.mod_dist_def.rpm_fim = ini_read_real("distribuidor", "rpm_fim", global.mod_dist_def.rpm_fim);
        global.mod_dist_def.curva = ini_read_real("distribuidor", "curva", global.mod_dist_def.curva);
        global.mod_dist_def.vac_total = ini_read_real("distribuidor", "vac_total", global.mod_dist_def.vac_total);
        global.mod_dist_def.vac_limiar = ini_read_real("distribuidor", "vac_limiar", global.mod_dist_def.vac_limiar);
        global.mod_dist_def.dwell = ini_read_real("distribuidor", "dwell", global.mod_dist_def.dwell);
        global.mod_dist_def.ganho = ini_read_real("distribuidor", "ganho", global.mod_dist_def.ganho);
        global.phys_def.raio = ini_read_real("fisica", "raio", global.phys_def.raio);
        global.phys_def.eficiencia = ini_read_real("fisica", "eficiencia", global.phys_def.eficiencia);
        global.phys_def.mu = ini_read_real("fisica", "mu", global.phys_def.mu);
        global.phys_def.share = ini_read_real("fisica", "share", global.phys_def.share);
        global.phys_def.embreagem = ini_read_real("fisica", "embreagem", global.phys_def.embreagem);
        global.phys_def.inercia = ini_read_real("fisica", "inercia", global.phys_def.inercia);
        global.cambio_rel_min = ini_read_real("cambio", "rel_min", global.cambio_rel_min);
        global.cambio_rel_max = ini_read_real("cambio", "rel_max", global.cambio_rel_max);
        global.cambio_rel_ref = ini_read_real("cambio", "rel_ref", global.cambio_rel_ref);
        global.cambio_passo = ini_read_real("cambio", "passo", global.cambio_passo);
        global.cambio_limite_pct = ini_read_real("cambio", "limite_pct", global.cambio_limite_pct);
        global.cambio_vmax = ini_read_real("cambio", "vmax", global.cambio_vmax);
        global.cambio_custo = ini_read_real("cambio", "custo", global.cambio_custo);
        global.mod_rev = ini_read_real("mod", "rev", 0);
        global.dev_cash = ini_read_real("dev", "cash", 0);
        global.dev_cash_modo = ini_read_real("dev", "cash_modo", 0);
        global.cambio_gratis = ini_read_real("dev", "cambio_gratis", 0);
        ini_close();
    }
};

// le o ini uma vez no boot (antes so era definida, nunca chamada)
global.mod_dist_carregar();
'''

GML_STEP_DANO = r'''
global.pt_step();
if (global.mod_dist_ativo) {
    global.mod_dist_dano();
}
'''

GML_DRAW = r'''
// aplica o ini ao carro salvo quando o arquivo muda (1x por segundo, no maximo)
global.mod_cfg_aplicar();
global.dev_cash_aplicar();
global.cash_guard();
// desenha so no Draw GUI: fica acima do passe CRT, sem piscar
global.mod_dist_ui();

// telemetria do cambio: marcha, giro, relacao total e topo teorico
if (instance_exists(obj_player) && global.kmh > 5) {
    var _g = global.gear;
    var _rr = global.pt_ratio(_g);
    var _topo = 0;
    if (_rr > 0) _topo = ((global.car_data[global.vehicles_array].rpm / _rr) * 60) / 1000;
    var _txt = "G" + string(_g) + "/" + string(obj_player.max_gears)
        + "   " + string(round(global.rpm)) + " rpm"
        + "   rel " + string(round(_rr * 100) / 100)
        + "   topo " + string(round(_topo))
        + "   " + string(round(global.kmh)) + " km/h"
        + "   mod" + string(global.cg_num(global.car_data[global.vehicles_array].cambio_mod, 0))
        + " gm" + string(array_length(global.car_data[global.vehicles_array].gears_mod));
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    draw_set_alpha(0.8);
    draw_set_color(c_white);
    draw_text_transformed(12, 12, _txt, 0.6, 0.6, 0);
    // sonda do topo: registra o que limita acima de 300 km/h
    if (global.kmh > 300 && current_time - global.cg_probe_t > 400) {
        global.cg_probe_t = current_time;
        global.cg_diag("TOPO kmh=" + string(round(global.kmh))
            + " rpm=" + string(round(global.rpm))
            + " gear=" + string(global.gear) + "/" + string(obj_player.max_gears)
            + " r=" + string(global.pt_ratio(global.gear))
            + " rpm_roda=" + string(round(global.pt_rpm_roda()))
            + " corte=" + string(global.pt_em_corte())
            + " acc=" + string(round(global.cg_acc * 100) / 100)
            + " drag=" + string(round(global.cg_drag * 1000) / 1000)
            + " fv=" + string(global.cg_fv)
            + " vmax=" + string(global.cambio_vmax)
            + " torque=" + string(round(global.torque))
            + " slip=" + string(round(global.phys_slip * 100))
            + " at=" + string(global.car_data[global.vehicles_array].at));
    }
    draw_set_alpha(1);
}

// cambio: aviso amarelo (caixa irreal) e erro vermelho
if (global.cambio_erro_t > 0 && current_time - global.cambio_erro_t < 3000) {
    draw_set_halign(fa_center);
    draw_set_valign(fa_top);
    draw_set_alpha(0.95);
    draw_set_color(c_red);
    draw_text_transformed(display_get_gui_width() / 2, 84, global.cambio_erro_txt, 0.75, 0.75, 0);
    draw_set_alpha(1);
    draw_set_color(c_white);
}
if (global.cambio_aviso_t > 0 && current_time - global.cambio_aviso_t < 5000) {
    draw_set_halign(fa_center);
    draw_set_valign(fa_top);
    draw_set_alpha(0.95);
    draw_set_color(c_yellow);
    draw_text_transformed(display_get_gui_width() / 2, 108, global.cambio_aviso_txt, 0.65, 0.65, 0);
    draw_set_alpha(1);
    draw_set_color(c_white);
}

// telemetria do noguintech (item comprado) + placa + FIPE
if (variable_global_exists("car_data") && is_array(global.car_data)
    && global.vehicles_array >= 0 && global.vehicles_array < array_length(global.car_data)) {
    var _ntc = global.car_data[global.vehicles_array];
    if (variable_struct_exists(_ntc, "noguiintech") && _ntc.noguiintech != 0) {
        var _tx = display_get_gui_width() - 250;
        var _ty = 90;
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        draw_set_alpha(0.85);
        draw_text_transformed(_tx, _ty, "NOGUITECH v" + string(_ntc.noguiintech), 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 16, "torque " + string(round(global.torque)) + " Nm", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 30, "potencia " + string(round(global.hp)) + " hp", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 44, "AFR " + string(round(global.afr * 10) / 10), 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 58, "avanco " + string(round(global.ignicao)) + " graus", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 72, "patinacao " + string(round(global.phys_slip * 100)) + "%", 0.5, 0.5, 0);
        var _pl = "SEM PLACA";
        if (variable_struct_exists(_ntc, "plate")) _pl = string(_ntc.plate);
        draw_text_transformed(_tx, _ty + 86, "placa " + _pl, 0.5, 0.5, 0);
        if (variable_global_exists("carro")) {
            draw_text_transformed(_tx, _ty + 100, "FIPE $" + string(ValueCars()), 0.5, 0.5, 0);
        }
        if (global.mod_dist_estado.knock > 0) {
            draw_set_color(make_color_rgb(255, 90, 60));
            draw_text_transformed(_tx, _ty + 118, "KNOCK", 0.6, 0.6, 0);
            draw_set_color(c_white);
        }
        draw_set_alpha(1);
    }
}
'''

def csx_str(s):
    return '@"' + s.replace('"', '""') + '"'

# ---- cambios 7 e 8 marchas: textos dos patches ----
# _cq transforma GML normal (newline real, aspas) em literal C# de uma linha
def _cq(t):
    return t.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")

# ---- cambio: tabela de fabrica + camada cg (substitui a cadeia antiga) ----
CR3_OLD = _cq("""if (global.car_data[global.vehicles_array].cambio == 1 || global.car_data[global.vehicles_array].cambio == 4)
{
    gear_ratios = [9, 6.1, 3.7, 2.1];
    original_ratios = [9, 6.1, 3.7, 2.1];
}
else if (global.car_data[global.vehicles_array].cambio == 2 || global.car_data[global.vehicles_array].cambio == 5)
{
    gear_ratios = [9, 6.1, 3.7, 2.5, 1.8];
    original_ratios = [9, 6.1, 3.7, 2.5, 1.8];
}
else if (global.car_data[global.vehicles_array].cambio == 3 || global.car_data[global.vehicles_array].cambio == 6 || global.car_data[global.vehicles_array].cambio == 7)
{
    gear_ratios = [9.35, 6, 3.6, 2.7, 2.1, 1.7];
    original_ratios = [9.35, 6, 3.6, 2.7, 2.1, 1.7];
}
if (global.car_data[global.vehicles_array].cambio_mod == 1 && array_length(global.car_data[global.vehicles_array].gears_mod) > 0)
{
    gear_ratios = global.car_data[global.vehicles_array].gears_mod;
}
max_gears = array_length(gear_ratios);""")
CR3_NEW = _cq("""var _cg_fab = [9.35, 6, 3.6, 2.7, 2.1, 1.7];
if (global.car_data[global.vehicles_array].cambio == 1 || global.car_data[global.vehicles_array].cambio == 4) _cg_fab = [9, 6.1, 3.7, 2.1];
else if (global.car_data[global.vehicles_array].cambio == 2 || global.car_data[global.vehicles_array].cambio == 5) _cg_fab = [9, 6.1, 3.7, 2.5, 1.8];
else if (global.car_data[global.vehicles_array].cambio == 8) _cg_fab = [9.35, 6.2, 4.5, 3.5, 2.75, 2.15, 1.7];
else if (global.car_data[global.vehicles_array].cambio == 9) _cg_fab = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];
global.cg_init_car(self, global.car_data[global.vehicles_array], _cg_fab);""")

IT_OLD = _cq('_r("gsd_6m", "GSD 6 Marchas", "cambio", "un", 1, "spr_venture_externa", 6, "cambio", "");')
IT_NEW = _cq("""_r("gsd_6m", "GSD 6 Marchas", "cambio", "un", 1, "spr_venture_externa", 6, "cambio", "");
_r("manual_7m", "Manual 7 Marchas", "cambio", "un", 1, "spr_venture_externa", 6, "cambio", "");
_r("manual_8m", "Manual 8 Marchas", "cambio", "un", 1, "spr_venture_externa", 6, "cambio", "");""")

MG_OLD = _cq("""case "gsd_6m":
                    _cambio_val = 7;
                    _car.at = 1;
                    break;""")
MG_NEW = _cq("""case "gsd_6m":
                    _cambio_val = 7;
                    _car.at = 1;
                    break;
                case "manual_7m":
                    _cambio_val = 8;
                    _car.at = 0;
                    break;
                case "manual_8m":
                    _cambio_val = 9;
                    _car.at = 0;
                    break;""")

DL_OLD = _cq("""case 9:
                    return "gsd_6m";""")
DL_NEW = _cq("""case 9:
                    return "gsd_6m";
                case 10:
                    return "manual_7m";
                case 11:
                    return "manual_8m";""")

PV_OLD = "[250, 750, 1500, 6500, 10500, 17500, 16750, 23450, 32500, 65000]"
PV_NEW = "[250, 750, 1500, 6500, 10500, 17500, 16750, 23450, 32500, 65000, 45000, 85000]"
PV2_OLD = "[250, 750, 6500, 6500, 10500, 17500, 16750, 23450, 32500, 65000]"
PV2_NEW = "[250, 750, 6500, 6500, 10500, 17500, 16750, 23450, 32500, 65000, 45000, 85000]"

# terminadores de idioma -> nomes novos (replace_all)
NOMES = [
    ('"GSD 6 marchas"]',          '"GSD 6 marchas", "Manual 7 marchas", "Manual 8 marchas"]'),
    ('"6-speed GSD"]',            '"6-speed GSD", "7-speed Manual", "8-speed Manual"]'),
    ('"GSD 6 marce"]',            '"GSD 6 marce", "Manuale 7 marce", "Manuale 8 marce"]'),
    ('"GSD 6 rapports"]',         '"GSD 6 rapports", "Manuelle 7 rapports", "Manuelle 8 rapports"]'),
    ('"6速自动", "GSD 6速"]',      '"6速自动", "GSD 6速", "7速手动", "8速手动"]'),
    ('"6速オートマ", "GSD 6速"]',   '"6速オートマ", "GSD 6速", "7速マニュアル", "8速マニュアル"]'),
    ('"GSD 6-ст."]',              '"GSD 6-ст.", "7-ст. механика", "8-ст. механика"]'),
]

# ---- fix do use_func dos cambios novos (BUG 17) e campo_val (BUG 18) ----
CK_OLD = _cq('var _cambio_keys = ["transmissao_original", "transmissao_forjada", "transmissao_desempenho", "manual_4m", "manual_5m", "manual_6m", "auto_4m", "auto_5m", "auto_6m", "gsd_6m"];')
CK_NEW = _cq('var _cambio_keys = ["transmissao_original", "transmissao_forjada", "transmissao_desempenho", "manual_4m", "manual_5m", "manual_6m", "auto_4m", "auto_5m", "auto_6m", "gsd_6m", "manual_7m", "manual_8m"];')

CV_OLD = _cq('_r("manual_8m", "Manual 8 Marchas", "cambio", "un", 1, "spr_venture_externa", 6, "cambio", "");')
CV_NEW = _cq("""_r("manual_8m", "Manual 8 Marchas", "cambio", "un", 1, "spr_venture_externa", 6, "cambio", "");
struct_get_from_hash(global.item_db, variable_get_hash("manual_4m")).campo_val = 1;
struct_get_from_hash(global.item_db, variable_get_hash("manual_5m")).campo_val = 2;
struct_get_from_hash(global.item_db, variable_get_hash("manual_6m")).campo_val = 3;
struct_get_from_hash(global.item_db, variable_get_hash("auto_4m")).campo_val = 4;
struct_get_from_hash(global.item_db, variable_get_hash("auto_5m")).campo_val = 5;
struct_get_from_hash(global.item_db, variable_get_hash("auto_6m")).campo_val = 6;
struct_get_from_hash(global.item_db, variable_get_hash("gsd_6m")).campo_val = 7;
struct_get_from_hash(global.item_db, variable_get_hash("manual_7m")).campo_val = 8;
struct_get_from_hash(global.item_db, variable_get_hash("manual_8m")).campo_val = 9;""")

# ================= CAMBIO: relacao final + limites =================
REL_SCHEMA_OLD = _cq("data.gears_mod = [];")
REL_SCHEMA_NEW = _cq("data.gears_mod = [];\ndata.relacao_final = 3.73;")

# ---- confirmacao do cambio: uma chamada so (teclado e mouse) ----
CFK_OLD = _cq("""                        if (global.cash >= _custo_cambio)
                        {
                            global.cash -= _custo_cambio;
                            global.car_data[global.vehicles_array].cambio_mod = 1;
                            global.car_data[global.vehicles_array].gears_mod = [];
                            for (var i = 0; i < obj_player.max_gears; i++)
                            {
                                global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i];
                                obj_player.gear_ratios[i] = global.gear_temp_array[i];
                            }
                            global.tiao_confirmacao_cambio = 0;
                            global.modificando_cambio = true;
                            global.tempo_modificacao = 0;
                            play_audio(snd_conserto_motor);
                            global.gear_temp_array = -1;
                        }
                        else
                        {
                            global.tiao_confirmacao_cambio = 0;
                        }""")
CFK_NEW = _cq("""                        if (global.cg_confirmar(_custo_cambio) == "")
                        {
                            global.tiao_confirmacao_cambio = 0;
                            global.modificando_cambio = true;
                            global.tempo_modificacao = 0;
                            play_audio(snd_conserto_motor);
                            global.gear_temp_array = -1;
                        }
                        else
                        {
                            global.tiao_confirmacao_cambio = 0;
                        }""")

CFM_OLD = _cq("""                    if (global.cash >= _custo_cambio)
                    {
                        global.cash -= _custo_cambio;
                        global.car_data[global.vehicles_array].cambio_mod = 1;
                        global.car_data[global.vehicles_array].gears_mod = [];
                        for (var i = 0; i < obj_player.max_gears; i++)
                        {
                            global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i];
                            obj_player.gear_ratios[i] = global.gear_temp_array[i];
                        }
                        global.tiao_confirmacao_cambio = 0;
                        global.modificando_cambio = true;
                        global.tempo_modificacao = 0;
                        play_audio(snd_conserto_motor);
                        global.gear_temp_array = -1;
                    }
                    else
                    {
                        global.tiao_confirmacao_cambio = 0;
                    }""")
CFM_NEW = _cq("""                    if (global.cg_confirmar(_custo_cambio) == "")
                    {
                        global.tiao_confirmacao_cambio = 0;
                        global.modificando_cambio = true;
                        global.tempo_modificacao = 0;
                        play_audio(snd_conserto_motor);
                        global.gear_temp_array = -1;
                    }
                    else
                    {
                        global.tiao_confirmacao_cambio = 0;
                    }""")

LIM_OLD = _cq("""                if (arg0 == 0)
                {
                    return 1;
                }
                else if (arg0 >= 4)
                {
                    return 0.2;
                }
                else
                {
                    return 0.5;
                }""")
LIM_NEW = _cq("""                return global.cambio_limite_pct;""")

LIM_USE_OLD = _cq("""                var _limite = get_limite_marcha(i, obj_player.max_gears);
                var _valor_min = _gear_original[i] - _limite;
                var _valor_max = _gear_original[i] + _limite;""")
LIM_USE_NEW = _cq("""                var _limite = max(0.3, _gear_original[i] * get_limite_marcha(i, obj_player.max_gears));
                var _valor_min = max(0.3, _gear_original[i] - _limite);
                var _valor_max = _gear_original[i] + _limite;""")

REL_INIT_OLD = _cq("""                global.gear_selecionada = 0;
                global.gear_editando = false;
                global.gear_area_botoes = false;
                global.gear_botao_selecionado = 0;
            }""")
REL_INIT_NEW = _cq("""                global.gear_selecionada = 0;
                global.gear_editando = false;
                global.gear_area_botoes = false;
                global.gear_botao_selecionado = 0;
                global.gear_temp_rel = global.cambio_rel_valor();
                global.gear_rel_original = global.gear_temp_rel;
            }""")

REL_COUNT_OLD = _cq("            var _custo_cambio = 775 * _marchas_modificadas;")
REL_COUNT_NEW = _cq("""            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)
            {
                _marchas_modificadas++;
            }
            var _custo_cambio = global.cambio_custo * _marchas_modificadas;""")

REL_NAV_OLD = _cq("global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears - 1);")
REL_NAV_NEW = _cq("global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears);")

REL_ROW_OLD = _cq("""                    draw_set_halign(fa_center);
                    draw_set_color(c_yellow);
                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);
                }
            }
            draw_set_halign(fa_left);
            draw_set_color(c_white);
            draw_text(_notebook_x - 290, _notebook_y + 150, txt_custo);""")

REL_ROW_NEW = _cq("""                    draw_set_halign(fa_center);
                    draw_set_color(c_yellow);
                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);
                }
            }
            var _rel_idx = obj_player.max_gears;
            var _y_rel = _start_y + (obj_player.max_gears * _spacing) + 8;
            var _rel_min = global.cambio_cfg().rel_min;
            var _rel_max = global.cambio_cfg().rel_max;
            var _rel_cor = (!global.gear_area_botoes && global.gear_selecionada == _rel_idx && !global.gear_editando) ? global.red : 16777215;
            if (global.gear_selecionada == _rel_idx && global.gear_editando) _rel_cor = global.gold;
            draw_set_halign(fa_left);
            draw_set_color(_rel_cor);
            draw_set_font(NotoSans);
            draw_text(_marcha_x, _y_rel, global.cambio_txt_rel() + ":");
            draw_set_color(c_gray);
            draw_text(_notebook_x - 180, _y_rel, string_format(global.gear_rel_original, 2, 2));
            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)
            {
                draw_set_color(c_yellow);
                draw_text(_notebook_x - 130, _y_rel, ">");
            }
            draw_set_color((abs(global.gear_temp_rel - global.gear_rel_original) > 0.005) ? 65280 : 16777215);
            draw_text(_notebook_x - 100, _y_rel, string_format(global.gear_temp_rel, 2, 2));
            var _rbar_x = _notebook_x + 50;
            draw_set_color(c_dkgray);
            draw_roundrect(_rbar_x, _y_rel - 6, _rbar_x + 200, _y_rel + 6, false);
            var _rpct = (global.gear_temp_rel - _rel_min) / (_rel_max - _rel_min);
            draw_set_color(_rel_cor);
            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, false);
            draw_set_color(c_black);
            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, true);
            var _rhover = point_in_rectangle(mouse_x, mouse_y, _rbar_x, _y_rel - 15, _rbar_x + 200, _y_rel + 15);
            if (mouse_check_button_pressed(mb_left) && _rhover)
            {
                global.gear_selecionada = _rel_idx;
                global.gear_editando = true;
                global.gear_area_botoes = false;
            }
            if (global.gear_editando && global.gear_selecionada == _rel_idx && mouse_check_button(mb_left) && _rhover)
            {
                var _rn = _rel_min + (clamp((mouse_x - _rbar_x) / 200, 0, 1) * (_rel_max - _rel_min));
                global.gear_temp_rel = round(_rn * 100) / 100;
            }
            if (global.gear_editando && global.gear_selecionada == _rel_idx)
            {
                if (keyboard_check_pressed(vk_left)) global.gear_temp_rel = max(global.gear_temp_rel - global.cambio_passo, _rel_min);
                if (keyboard_check_pressed(vk_right)) global.gear_temp_rel = min(global.gear_temp_rel + global.cambio_passo, _rel_max);
                draw_set_halign(fa_center);
                draw_set_color(c_yellow);
                draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);
            }
            draw_set_halign(fa_left);
            draw_set_color(c_white);
            draw_text(_notebook_x - 290, _notebook_y + 176, txt_custo);""")

REL_COST_OLD = _cq('draw_text(_notebook_x + 80, _notebook_y + 150, "$" + string(floor(_custo_cambio)));')
REL_COST_NEW = _cq('draw_text(_notebook_x + 80, _notebook_y + 176, "$" + string(floor(_custo_cambio)));')

REL_DBG_OLD = _cq('draw_text_transformed(_notebook_x, _notebook_y + 110, t + string(global.car_data[global.vehicles_array].gears_mod), 1, 1, 0);')
REL_DBG_NEW = _cq('draw_text_transformed(_notebook_x, _notebook_y + 200, t + string(global.car_data[global.vehicles_array].gears_mod) + "  |  " + global.cambio_txt_rel() + ": " + string(global.car_data[global.vehicles_array].relacao_final), 1, 1, 0);')

VM1_OLD = _cq("""if (_gearlimit > 400)
{
    _gearlimit = 400;
}
if (global.kmh > 400)
{
    global.kmh = 400;
}""")
VM1_NEW = _cq("""if (_gearlimit > global.cambio_vmax)
{
    _gearlimit = global.cambio_vmax;
}
if (global.kmh > global.cambio_vmax)
{
    global.kmh = global.cambio_vmax;
}""")
VM2_OLD = _cq("global.kmh = clamp(global.kmh, 0, 400);")
VM2_NEW = _cq("global.kmh = clamp(global.kmh, 0, global.cambio_vmax);")
VM4_OLD = _cq("else if (global.kmh >= 250 && global.kmh < 300)")
VM4_NEW = _cq("else if (global.kmh >= 250 && global.kmh < 400)")
VM3_OLD = _cq("""else if (global.kmh >= 300)
    {
        _fator_velocidade = 3.2;
    }""")
VM3_NEW = _cq("""else if (global.kmh >= 400)
    {
        _fator_velocidade = 3.2;
    }""")
# ===================================================================

PT_UP_MAN_OLD = _cq("""if (key_next && global.gear < max_gears)
        {
            global.gear += 1;""")
PT_UP_MAN_NEW = _cq("""if (key_next && global.gear < max_gears)
        {
            global.gear += 1;
            _gearlimit = global.pt_gear_limit();""")

PT_UP_AUTO_OLD = _cq("""if (global.gear < max_gears)
                {
                    global.gear += 1;
                    if (_gsd)""")
PT_UP_AUTO_NEW = _cq("""if (global.gear < max_gears)
                {
                    global.gear += 1;
                    _gearlimit = global.pt_gear_limit();
                    if (_gsd)""")

PT_BAND_OLD = _cq("_base_acceleration *= _power_band_mult;")
PT_BAND_NEW = _cq("_power_band_mult = 1;")

PT_SPEEDRES_OLD = _cq("var _speed_resistance = lerp(1, 0.5, _smooth_ratio);")
PT_SPEEDRES_NEW = _cq("var _speed_resistance = 1;")

CG_DRAG_OLD = _cq("global.kmh -= (_desacel_arrasto * global.dt);")
CG_DRAG_NEW = _cq("global.kmh -= (_desacel_arrasto * global.dt);\nglobal.cg_drag = _desacel_arrasto;\nglobal.cg_fv = _fator_velocidade;")

# ---- teto de 400 nas colisoes: usa o teto do cambio ----
COL_OLD = _cq("clamp(global.kmh, 0, 400)")
COL_NEW = _cq("clamp(global.kmh, 0, global.cambio_vmax)")
NPC_OLD = _cq("global.kmh = clamp(global.kmh - (_fecha * random_range(0.5, 1)), 0, 400);")
NPC_NEW = _cq("global.kmh = clamp(global.kmh - (_fecha * random_range(0.5, 1)), 0, global.cambio_vmax);")
TOUGE_OLD = _cq("kmh = clamp(kmh + (_fecha * 0.3), 0, 400);")
TOUGE_NEW = _cq("kmh = clamp(kmh + (_fecha * 0.3), 0, global.cambio_vmax);")

PT_TARGET_OLD = _cq("""_target_speed = min(global.kmh + _acceleration, _gearlimit);
        if (global.kmh < _gearlimit)""")
PT_TARGET_NEW = _cq("""_acceleration *= (1 - global.pt_em_corte());
        if (global.pt_shift_t > 0) _acceleration *= 0.15;
        global.cg_acc = _acceleration;
        _target_speed = global.kmh + _acceleration;
        if (global.kmh < global.cambio_vmax)""")

PT_SHIFT_OLD = _cq("play_audio(snd_gearchange);")
PT_SHIFT_NEW = _cq("global.pt_shift_t = global.pt_shift_time; play_audio(snd_gearchange);")

PATCH = f'''using System;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;

EnsureDataLoaded();

var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) {{ MainThreadAction = MainThreadAction }};

g.QueueAppend("gml_RoomCC_Start_0_Create", {csx_str(GML_INIT)});

g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    "if (global.mod_dist_ativo) {{ global.torque *= global.mod_dist_fator(); global.ignicao = global.mod_dist_estado.avanco; }} var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    false);

// --- FASE 1: motor com consequencia ---
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "    var _camshaft_low = 1;",
    "    global.afr = global.motor_afr(global.car_data[global.vehicles_array], global.rpm, global.car_data[global.vehicles_array].rpm, global.turbo, global.fuel); var _camshaft_low = 1;",
    false);

// todo efeito de peca/ajuste entra no torque aqui, antes do hp
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.hp = clamp(global.hp, 0, global.maxhp * 1.2);",
    "global.torque *= global.motor_torque(global.car_data[global.vehicles_array], global.rpm, global.car_data[global.vehicles_array].rpm, global.afr);\\nglobal.torque = global.pt_torque(global.torque, global.rpm, global.car_data[global.vehicles_array].rpm, global.comando); global.hp = clamp(global.pot_hp(global.torque, global.rpm, global.maxtorque, global.car_data[global.vehicles_array].rpm, global.maxhp), 0, global.maxhp * 1.2);",
    false);

// cabecote muda o conjunto de valvulas
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "switch (global.valvulas)",
    "switch (global.motor_valvulas(global.valvulas, global.motor_cabecote_player()))",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);

// multiplicadores tardios: agora tudo vem de motor_torque
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= mult_pistao;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= mult_pistao;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= mult_cmd;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= mult_cmd;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= mult_vira;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= mult_vira;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= (_carb_mult / 1.25);", "// mistura: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= (_carb_mult / 1.8);", "// mistura: motor_torque", false);

// teto de aceleracao deixa de punir quem instala peca
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_max_acc = 12;", "_max_acc = 21;", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_max_acc = 16;", "_max_acc = 25;", false);

g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.2);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.2);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.2);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.2);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.1);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.1);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.1);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.1);",
    false);

// --- FASE 2: trem de forca ---
// 1) base do jogador: forca na roda com relacao real e limite de aderencia
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    "var _pc = global.phys_cfg(); var _g1 = gear_ratios[0]; var _gr = gear_ratios[max(0, global.gear - 1)]; var _base_acceleration = global.phys_base(global.torque, _gr, _pc.raio, global.weight * 1000, global.phys_k(global.hp, _pc.raio, _g1), _pc.eficiencia, global.phys_mu(global.car_data[global.vehicles_array].pneu, global.heat_tire), _pc.share);",
    false);

// 2) re: mesma fisica, relacao da re
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 375);",
    "var _base_acceleration = global.phys_base(global.torque, gear_ratios[2], _pc.raio, global.weight * 1000, global.phys_k(global.hp, _pc.raio, gear_ratios[0]), _pc.eficiencia, global.phys_mu(global.car_data[global.vehicles_array].pneu, global.heat_tire), _pc.share);",
    false);

// 3) _gear_resistance sai da cadeia (relacao real ja entrou na base)
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "_acceleration = _base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    "_acceleration = _base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "_acceleration = (_base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty) / _vezes;",
    "_acceleration = (_base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty) / _vezes;",
    false);

// 4) inercia: gira livre em neutro ou com a embreagem pisada
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.rpm = clamp(global.rpm, global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm);",
    "global.rpm = clamp(global.rpm, global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm); if (global.engine == 1 && key_up && (global.gear == 0 || key_clutch)) {{ global.rpm = clamp(global.rpm + global.phys_rev(global.torque, _camshaft_response, global.dt), global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm); }}",
    false);

// 5) IAs: mesma base fisica
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);

// 6) IAs: fora o proxy de relacao
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "_acceleration = _base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    "_acceleration = _base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "_acceleration = _base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    "_acceleration = _base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "_acceleration = (_base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    "_acceleration = (_base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "_acceleration = (_base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    "_acceleration = (_base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    false);

// --- FASE 3: fim das punicoes por tunar ---
// a fisica (mu * N + phys_slip) ja limita a tracao; esses descontos so sabotavam
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "        if (_mod_points > 5)", "        if (false)", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "        if (global.kmh < 80 && global.gear <= 3 && _mod_points > 5)",
    "        if (false)", false);

// compra falhava quando o dinheiro era exatamente o preco
g.QueueFindReplace("gml_Object_obj_ntc_dinamometro_confirm_Step_0",
    "if (global.cash > global.dinamometro_custo)",
    "if (global.cash >= global.dinamometro_custo)", false);

g.QueueAppend("gml_Object_obj_player_Step_0", {csx_str(GML_STEP_DANO)});
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd;",
    "var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd; var _mcarb = global.mod_carb_cfg();",
    false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _gicle_desvio = _gicle_ativo - _gicle_ideal;",
    "var _gicle_desvio = _gicle_ativo - _gicle_ideal + global.mod_carb_desvio(_rpm_ratio, _mcarb, _gicle_ideal);",
    false);

g.QueueAppend("gml_Object_obj_noguiin_Draw_64", {csx_str(GML_DRAW)});


// ---- CAMBIOS 7 E 8 MARCHAS ----
// razoes: cambio 8 = 7 marchas, cambio 9 = 8 marchas. max_gears sai do array_length,
// entao troca, automatico, HUD e IA (que copia as razoes do jogador) ja funcionam.
g.QueueFindReplace("gml_Object_obj_player_Create_0", "{CR3_OLD}", "{CR3_NEW}", false);

// nomes: 7 terminadores cobrem as 5 tabelas (case "cambio" indexado por cambio-1,
// GetPartLevelsArray/2 e GetNamePartsVenture/2 indexados por nivel)
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[0][0])}", "{_cq(NOMES[0][1])}", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[1][0])}", "{_cq(NOMES[1][1])}", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[2][0])}", "{_cq(NOMES[2][1])}", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[3][0])}", "{_cq(NOMES[3][1])}", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[4][0])}", "{_cq(NOMES[4][1])}", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[5][0])}", "{_cq(NOMES[5][1])}", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{_cq(NOMES[6][0])}", "{_cq(NOMES[6][1])}", true);

// precos: nivel 10 = 45.000, nivel 11 = 85.000 (as duas tabelas paralelas)
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{PV_OLD}", "{PV_NEW}", false);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "{PV2_OLD}", "{PV2_NEW}", false);

// item no banco de inventario
g.QueueFindReplace("gml_GlobalScript_Inventory", "{IT_OLD}", "{IT_NEW}", false);

// instalacao pelo minigame (manual: at = 0)
g.QueueFindReplace("gml_GlobalScript_minigames_noguiintech", "{MG_OLD}", "{MG_NEW}", false);

// entrega: (categoria 6, nivel 10/11) -> id do item
g.QueueFindReplace("gml_GlobalScript_Delivery", "{DL_OLD}", "{DL_NEW}", false);

// caderno de marchas: 8 linhas nao podem invadir o texto de custo
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0",
    "var _start_y = _notebook_y - 80;",
    "var _start_y = _notebook_y - 80 - ((obj_player.max_gears > 6) ? 18 : 0);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0",
    "var _spacing = 35;",
    "var _spacing = (obj_player.max_gears > 6) ? 26 : 35;", false);

// ---- BUG 17: itens novos nao herdam use_func (_r nao define) ----
// o laco _cambio_keys e o unico lugar que atribui item_use_dinamometro aos cambios
g.QueueFindReplace("gml_GlobalScript_Inventory", "{CK_OLD}", "{CK_NEW}", false);

// ---- BUG 18: campo_val real (1..9) para _inventory_get_item_by_campo("cambio") ----
// sem campo_val ele compara posicao 0-based com cambio 1-based e nunca acha
g.QueueFindReplace("gml_GlobalScript_Inventory", "{CV_OLD}", "{CV_NEW}", false);

// ================= CAMBIO: relacao final + limites =================
g.QueueFindReplace("gml_GlobalScript_EstruturaSalvamento", "{REL_SCHEMA_OLD}", "{REL_SCHEMA_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{LIM_OLD}", "{LIM_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{CFK_OLD}", "{CFK_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{CFM_OLD}", "{CFM_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{LIM_USE_OLD}", "{LIM_USE_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{REL_INIT_OLD}", "{REL_INIT_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{REL_COUNT_OLD}", "{REL_COUNT_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{REL_NAV_OLD}", "{REL_NAV_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{REL_ROW_OLD}", "{REL_ROW_NEW}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{REL_COST_OLD}", "{REL_COST_NEW}", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "{REL_DBG_OLD}", "{REL_DBG_NEW}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{VM1_OLD}", "{VM1_NEW}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{VM2_OLD}", "{VM2_NEW}", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{VM3_OLD}", "{VM3_NEW}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{VM4_OLD}", "{VM4_NEW}", false);
// ===================================================================

// ---- powertrain: corte de giro corta torque, corte de troca corta torque, sem parede ----
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{PT_TARGET_OLD}", "{PT_TARGET_NEW}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{CG_DRAG_OLD}", "{CG_DRAG_NEW}", false);
g.QueueFindReplace("gml_Object_obj_player_Collision_obj_ia_car_TOUGE", "{COL_OLD}", "{COL_NEW}", true);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Collision_obj_player", "{COL_OLD}", "{COL_NEW}", true);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Collision_obj_ia_car_NPC", "{COL_OLD}", "{COL_NEW}", true);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Collision_obj_player", "{NPC_OLD}", "{NPC_NEW}", true);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Collision_obj_player", "{TOUGE_OLD}", "{TOUGE_NEW}", true);
// upshift nao recalculava _gearlimit: o rpm nao caia na troca (bug do jogo)
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{PT_UP_MAN_OLD}", "{PT_UP_MAN_NEW}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{PT_UP_AUTO_OLD}", "{PT_UP_AUTO_NEW}", false);
// limitadores artificiais que brigavam com a curva de torque
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{PT_BAND_OLD}", "{PT_BAND_NEW}", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{PT_SPEEDRES_OLD}", "{PT_SPEEDRES_NEW}", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "{PT_SHIFT_OLD}", "{PT_SHIFT_NEW}", true);

g.Import();
ScriptMessage("MARK distribuidor=v2 instalado");
'''

pathlib.Path(__file__).parent.joinpath("mod_dist2.csx").write_text(PATCH, encoding="utf-8")
print("csx v2:", len(PATCH), "bytes")
