using System;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;

EnsureDataLoaded();

var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };

g.QueueAppend("gml_RoomCC_Start_0_Create", @"
// =====================================================================
//  MOD DISTRIBUIDOR v2  -  item de carro (0 nenhum, 1 original, 2 performance)
//  instalado por mcml-fixer --mod-install
// =====================================================================
global.mod_dist_versao = ""2.0"";
global.mod_dist_preco = [0, 0, 12000];
global.mod_dist_nome = [""Nenhum"", ""Original"", ""Competicao""];
global.mod_dist_def = {
    avanco_inicial: 10, cent_total: 22, rpm_inicio: 1000, rpm_fim: 3200,
    curva: 1, vac_total: 12, vac_limiar: 0, dwell: 45, desgaste: 0, ganho: 100
};
// padroes de fisica: o ini [fisica] sobrescreve; cada carro copia ao criar
// globais do jogo que o mod le: garante que existem em qualquer sala
if (!variable_global_exists(""turbo_pressure"")) global.turbo_pressure = 0;
if (!variable_global_exists(""turbo"")) global.turbo = 0;
if (!variable_global_exists(""iat"")) global.iat = 25;
if (!variable_global_exists(""temp"")) global.temp = 25;
if (!variable_global_exists(""engine"")) global.engine = 0;
if (!variable_global_exists(""rpm"")) global.rpm = 900;
if (!variable_global_exists(""kmh"")) global.kmh = 0;
if (!variable_global_exists(""gear"")) global.gear = 0;
if (!variable_global_exists(""ignicao"")) global.ignicao = 0;
if (!variable_global_exists(""afr"")) global.afr = 14.7;
if (!variable_global_exists(""cash"")) global.cash = 0;
if (!variable_global_exists(""dt"")) global.dt = 1;

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
    if (!variable_global_exists(""cash"")) return 0;
    if (global.cash < 0) {
        global.cash = 0;
        return 1;
    }
    return 0;
};
global.phys_def = { raio: 0.31, eficiencia: 0.90, mu: 1.00, share: 1.00, embreagem: 0, inercia: 0.15 };
global.mod_dist_estado = { menu: 0, sel: 0, aviso: """", spr: -1, spr_try: 0, rpm: 800, map: 0, cent: 0, vac: 0, avanco: 10, ideal: 0, bonus: 0, q: 1, knock: 0, dwell_ef: 45, dwell_ms: 5, tok: -1, icon: -1, icon_try: 0 };
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
    if (!variable_struct_exists(car, ""carburador"")) return -1;
    var _c = car.carburador;
    if (is_undefined(_c)) return -1;
    // bicos instalados = injetado, nao importa o que diga o carburador
    if (variable_struct_exists(car, ""bicos"") && car.bicos > 0) return 0;
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
    if (!variable_struct_exists(car, ""distribuidor"") || is_undefined(car.distribuidor)) {
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
    if (!variable_struct_exists(car, ""dist_cfg"")) car.dist_cfg = undefined;
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
    if (!variable_struct_exists(car.dist_cfg, ""ver"")) {
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
    var _st = variable_global_exists(""mod_rev"") ? global.mod_rev : 0;
    if (_st <= 0) return 0;
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    if (variable_struct_exists(car, ""cfg_stamp"") && car.cfg_stamp == _st) return 0;

    var _tinha = (variable_struct_exists(car, ""phys_cfg"") && !is_undefined(car.phys_cfg)) ? 1 : 0;
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

    var _f = file_text_open_write(""mod_cfg_diag.txt"");
    file_text_write_string(_f, ""aplicado=1 phys_antes="" + string(_tinha) + "" dist="" + string(_dist));
    file_text_write_string(_f, "" raio="" + string(car.phys_cfg.raio) + "" mu="" + string(car.phys_cfg.mu));
    file_text_write_string(_f, "" inercia="" + string(car.phys_cfg.inercia) + "" ganho="" + string(global.mod_dist_def.ganho));
    file_text_close(_f);
    return 1;
};

global.mod_dist_comprar = function(modelo) {
    var car = global.mod_dist_car();
    var e = global.mod_dist_estado;
    if (is_undefined(car)) { e.aviso = ""sem carro selecionado""; return; }
    if (global.mod_dist_tipo(car) != 1) { e.aviso = ""so instala em motor carburado""; return; }
    if (global.mod_dist_modelo() == modelo) { e.aviso = ""este modelo ja esta instalado""; return; }
    if (modelo == 1) {
        // voltar ao original de fabrica: nao custa nada, mas nao devolve o dinheiro
        car.distribuidor = 1;
        car.dist_cfg = undefined;
        global.mod_dist_cfg();
        e.aviso = ""distribuidor original reinstalado"";
        global.mod_dist_ativo = 1;
        return;
    }
    var preco = global.mod_dist_preco[modelo];
    if (global.cash < preco) { e.aviso = ""faltam $"" + string(preco - global.cash); return; }
    global.cash -= preco;
    car.distribuidor = modelo;
    car.dist_cfg = undefined;
    global.mod_dist_cfg();
    e.aviso = ""comprado: "" + global.mod_dist_nome[modelo];
    global.mod_dist_ativo = 1;
};

global.mod_dist_remover = function() {
    var car = global.mod_dist_car();
    var e = global.mod_dist_estado;
    if (is_undefined(car)) return;
    var m = global.mod_dist_modelo();
    if (m <= 1) { e.aviso = ""ja esta com o original de fabrica""; return; }
    global.cash += floor(global.mod_dist_preco[m] * 0.5);
    car.distribuidor = 1;
    car.dist_cfg = undefined;
    e.aviso = ""voltou ao original (+$"" + string(floor(global.mod_dist_preco[m] * 0.5)) + "")"";
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
    if (variable_struct_exists(car, ""rpm"")) _rpm_max_carro = car.rpm;
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
        if (variable_struct_exists(car, ""vcabecote"")) {
            car.vcabecote = max(0, car.vcabecote - (e.knock * dt * 0.5));
        }
        if (variable_struct_exists(car, ""vpistao"")) {
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
    if (keyboard_check_pressed(vk_f6)) { e.menu = (e.menu == 0) ? 1 : 0; e.sel = 0; e.aviso = """"; }
    if (e.menu == 0) return;
    if (is_undefined(car)) return;
    if (keyboard_check_pressed(vk_tab)) {
        if (e.menu == 1) e.menu = 2;
        else if (e.menu == 2) e.menu = 3;
        else e.menu = 1;
        e.sel = 0;
        e.aviso = """";
    }
    if (e.menu == 2) {
        if (keyboard_check_pressed(ord(""1""))) global.mod_dist_comprar(1);
        if (keyboard_check_pressed(ord(""2""))) global.mod_dist_comprar(2);
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
                e.aviso = """";
            }
            if (keyboard_check_pressed(vk_f7)) {
                var _gi7 = global.mod_carb_gicle_ideal(max(1, car.carburador));
                _cc.lenta = round(_gi7 * 0.35);
                _cc.agulha = round(_gi7 * 0.90);
                _cc.borboleta = 50;
                e.aviso = ""carburador no padrao"";
            }
        }
        return;
    }
    if (keyboard_check_pressed(ord(""3""))) global.mod_dist_remover();
    if (keyboard_check_pressed(vk_f7)) { global.mod_dist_padrao_modelo(); e.aviso = ""padrao do modelo""; }
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
        e.aviso = """";
    }
};

// roda o modelo com um parametro no valor dado e devolve o bonus resultante
global.mod_dist_sim = function(_sel, _val) {
    var d = global.mod_dist_cfg();
    var _k = [""avanco_inicial"", ""cent_total"", ""rpm_inicio"", ""rpm_fim"", ""curva"", ""vac_total"", ""vac_limiar"", ""dwell"", ""desgaste"", ""ganho""];
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
        draw_text(_x + 12, _y + 8, ""CARBURADOR"");
        draw_text(_x + 20, _y + 60, ""sem carro selecionado"");
        return;
    }
    draw_text(_x + 12, _y + 8, ""CARBURADOR - tipo "" + string(max(1, car.carburador)));
    var cfg = global.mod_carb_cfg();
    if (is_undefined(cfg) || global.mod_dist_tipo(car) != 1) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 20, _y + 60, ""Este motor e injetado: nao tem carburador para ajustar."");
        draw_set_color(c_white);
        return;
    }
    var _labels = [""Gicle de lenta"", ""Agulha (media)"", ""Borboleta""];
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
    var _d1 = [""Mistura em marcha lenta e na saida."",
               ""Mistura na faixa media (cruzeiro)."",
               ""Abertura inicial da borboleta.""];
    var _d2 = [""Baixo = pobre, falha frio. Alto = rico, afoga e suja."",
               ""Baixo = pobre em media. Alto = rico, perde potencia e gasta."",
               ""Baixo = lenta baixa. Alto = lenta alta e resposta brusca.""];
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 30, _y + 158, _d1[e.sel]);
    draw_text(_x + 30, _y + 176, _d2[e.sel]);
    var _gi = global.mod_carb_gicle_ideal(max(1, car.carburador));
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 30, _y + 214, ""gicle principal ideal deste modelo: "" + string(_gi));
    draw_set_color(make_color_rgb(170, 200, 255));
    if (variable_global_exists(""afr"")) {
        draw_text(_x + 30, _y + 240, ""AFR atual: "" + string(round(global.afr * 10) / 10) + ""   (14.7 = estequeometrico)"");
    }
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 30, _y + _h - 60, ""setas ajustam   F7 padrao   TAB proxima aba   F6 fecha"");
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
    draw_text(_x + 12, _y + 8, ""LOJA - DISTRIBUIDORES"");
    draw_set_halign(fa_right);
    draw_text(_x + _w - 12, _y + 8, ""dinheiro: $"" + string(global.cash));
    draw_set_halign(fa_left);
    global.mod_dist_draw_icon(_x + 150, _y + 210, 300);
    for (var i = 0; i < 2; i++) {
        var _m = i + 1;
        var _ry = _y + 56 + (i * 112);
        var _inst = (modelo == _m);
        draw_set_color(_inst ? make_color_rgb(45, 80, 55) : make_color_rgb(32, 32, 40));
        draw_rectangle(_x + 300, _ry, _x + _w - 20, _ry + 98, false);
        draw_set_color(c_white);
        draw_text(_x + 312, _ry + 8, ""["" + string(_m) + ""]  "" + global.mod_dist_nome[_m]);
        var _p = global.mod_dist_preco[_m];
        draw_text(_x + 312, _ry + 28, (_p <= 0) ? ""incluso de fabrica"" : ""$"" + string(_p));
        var _lim = global.mod_dist_limites(_m);
        draw_set_color(make_color_rgb(180, 190, 210));
        draw_text(_x + 312, _ry + 50, ""avanco ate "" + string(_lim[1]) + ""   centrifugo "" + string(_lim[2]) + ""   ate "" + string(_lim[3]) + "" rpm"");
        draw_text(_x + 312, _ry + 68, ""vacuo ate "" + string(_lim[4]) + ""   dwell "" + string(_lim[5]) + ""-"" + string(_lim[6]) + ""   ganho ate "" + string(_lim[7]) + ""%"");
        if (_inst) {
            draw_set_color(make_color_rgb(120, 255, 140));
            draw_text(_x + _w - 140, _ry + 8, ""INSTALADO"");
        }
    }
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 300, _y + _h - 62, ""[1] original   [2] competicao   TAB ajustar   F6 fechar"");
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
    _m *= 1 + (0.010 * min(global.motor_campo(_car, ""pistao""), 3));
    _m *= 1 + (0.005 * min(global.motor_campo(_car, ""bielas""), 3));
    _m *= 1 + (0.008 * min(global.motor_campo(_car, ""bloco""), 3));
    _m *= 1 + (0.006 * min(global.motor_campo(_car, ""vira""), 3));
    _m *= 1 + (0.015 * min(global.motor_campo(_car, ""filtro""), 2));
    _m *= 1 + (0.020 * min(global.motor_campo(_car, ""admissao""), 2));
    _m *= 1 + (0.018 * min(global.motor_campo(_car, ""escape""), 2));
    _m *= 1 + (0.018 * min(global.motor_campo(_car, ""pipe""), 2));
    return _m;
};

// cabecote do carro do jogador (evita aspas dentro do CSX)
global.motor_cabecote_player = function() {
    return global.motor_campo(global.mod_dist_car(), ""cabecote"");
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
    var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, ""carburador"")) : 0;
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
    var _gicle = global.motor_campo(_car, ""gicle"");
    var _gicle2 = global.motor_campo(_car, ""gicle_2nd"");
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
    var _campos = [""vpistao"", ""vbielas"", ""vcabecote"", ""vcomando"", ""vvira"", ""vbloco""];
    var _m = 1;
    for (var _i = 0; _i < array_length(_campos); _i++) {
        // 0 e desgaste total, nao ""campo ausente"": checar existencia antes
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
    var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, ""carburador"")) : 0;
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
    if (!variable_struct_exists(car, ""phys_cfg"") || is_undefined(car.phys_cfg)) {
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
    if (!variable_struct_exists(car, ""carb_cfg"") || is_undefined(car.carb_cfg)) {
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
        var _p = working_directory + ""assets/Mod_assets/distribuidor.png"";
        if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        if (e.spr < 0) {
            _p = ""assets/Mod_assets/distribuidor.png"";
            if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        }
        if (e.spr < 0) {
            _p = ""Mod_assets/distribuidor.png"";
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
        if (file_exists(""distribuidor_icon.png"")) e.icon = sprite_add(""distribuidor_icon.png"", 1, false, false, 0, 0);
        if (e.icon < 0 && file_exists(""distribuidor.png"")) e.icon = sprite_add(""distribuidor.png"", 1, false, false, 0, 0);
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
        draw_text(_cx, _cy, ""DIST"");
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
    }
};

// registra os dois itens na base do inventario (idempotente)
global.mod_dist_db = function() {
    if (!variable_global_exists(""item_db"")) return;
    if (!is_struct(global.item_db)) return;
    if (variable_struct_exists(global.item_db, ""distribuidor_original"")) return;
    variable_struct_set(global.item_db, ""distribuidor_original"", {
        id: ""distribuidor_original"", name: ""Distribuidor Original"", category: ""peca_externa"",
        value_label: ""Original"", value_default: 1, sprite_name: ""spr_venture_externa"", image_index: 17,
        campo: ""distribuidor"", vcampo: """", campo_val: 1
    });
    variable_struct_set(global.item_db, ""distribuidor_competicao"", {
        id: ""distribuidor_competicao"", name: ""Distribuidor Competicao"", category: ""peca_externa"",
        value_label: ""Competicao"", value_default: 2, sprite_name: ""spr_venture_externa"", image_index: 17,
        campo: ""distribuidor"", vcampo: """", campo_val: 2
    });
};

global.mod_dist_ui = function() {
    var e = global.mod_dist_estado;
    global.mod_dist_db();
    global.mod_dist_ativo = (global.mod_dist_modelo() > 0) ? 1 : 0;
    if (!global.mod_dist_pode()) { e.menu = 0; return; }
    global.mod_dist_input();
    if (e.menu == 0 && !global.mod_dist_ativo) return;
    if (variable_global_exists(""mod_dist_debug"") && global.mod_dist_debug == 1) {
        var _f = file_text_open_write(""mod_diag.txt"");
        file_text_write_string(_f, ""room="" + string(room) + "" player="" + string(instance_exists(obj_player)));
        file_text_write_string(_f, "" carros="" + string(array_length(global.car_data)) + "" cash="" + string(global.cash));
        file_text_write_string(_f, "" modelo="" + string(global.mod_dist_modelo()) + "" menu="" + string(e.menu) + "" ativo="" + string(global.mod_dist_ativo));
        file_text_write_string(_f, "" avanco="" + string(global.mod_dist_calcular()) + "" fator="" + string(global.mod_dist_fator()));
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
        draw_text(14, 12, global.mod_dist_nome[modelo] + ""  "" + string(round(e.avanco)) + "" graus"");
        draw_set_color(make_color_rgb(170, 200, 255));
        draw_text(14, 27, ""cent "" + string(round(e.cent)) + ""  vac "" + string(round(e.vac)) + ""  dwell "" + string(round(e.dwell_ef)) + ""  F6 menu"");
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
    draw_text(_x + 12, _y + 8, ""DISTRIBUIDOR - "" + global.mod_dist_nome[modelo]);
    global.mod_dist_draw_icon(_x + 150, _y + 200, 300);
    if (modelo <= 0) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 310, _y + 60, ""Este motor e injetado: distribuidor so serve em carburador."");
        draw_set_color(c_white);
        return;
    }
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _labels = [""Avanco inicial (graus)"", ""Centrifugo total (graus)"", ""RPM de inicio"", ""RPM de fim"", ""Curva (0 linear, 1 progressiva, 2 rapida)"", ""Vacuo total (graus)"", ""Limiar de vacuo (bar)"", ""Dwell (graus)"", ""Desgaste (%)"", ""Ganho (%)""];
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
    var _d1 = [""Avanco inicial: ponto base da ignicao."",
               ""Centrifugo total: avanco extra que sobe com o giro."",
               ""RPM de inicio: giro onde a curva comeca a entrar."",
               ""RPM de fim: giro onde o avanco satura."",
               ""Curva: formato da entrada das duas molas."",
               ""Vacuo total: avanco extra em carga leve."",
               ""Limiar de vacuo: MAP onde o vacuo comeca."",
               ""Dwell: tempo de carga da bobina."",
               ""Desgaste do platinado e do rotor."",
               ""Ganho: quanto do teto do modelo o carro usa.""];
    var _d2 = [""Baixo = fraco e lento. Alto = torque, mas detona."",
               ""Baixo = morre em alta. Alto = potencia, risco de knock."",
               ""Baixo = antecipa cedo. Alto = amarrado ate subir."",
               ""Baixo = trava cedo e perde alta. Alto = estica a curva."",
               ""0 linear, 1 progressiva, 2 rapida (mola leve)."",
               ""Baixo = consumo pior. Alto = economia, mas detona."",
               ""Baixo = entra cedo. Alto = so em vacuo forte."",
               ""Baixo = faisca fraca. Alto = forte, bobina esquenta."",
               ""Alto = faisca irregular e perda de potencia."",
               ""Baixo = pouco proveito. Alto = 100% do potencial.""];
    var _pmin = [lim[0], 0, 600, 1200, 0, 0, -0.8, lim[5], 0, 0];
    var _pmax = [lim[1], lim[2], 4000, lim[3], 2, lim[4], 1, lim[6], 100, lim[7]];
    var _bmin = global.mod_dist_sim(e.sel, _pmin[e.sel]);
    var _bmax = global.mod_dist_sim(e.sel, _pmax[e.sel]);
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 310, _y + 352, _d1[e.sel]);
    draw_text(_x + 310, _y + 368, _d2[e.sel]);
    draw_set_color(make_color_rgb(255, 150, 150));
    draw_text(_x + 310, _y + 394, ""min "" + string(round(_bmin * 100)) + ""%"");
    draw_set_color(c_white);
    draw_text(_x + 380, _y + 394, ""atual "" + string(round(e.bonus * 100)) + ""%"");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 480, _y + 394, ""max "" + string(round(_bmax * 100)) + ""%"");
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 310, _y + 412, ""(bonus de potencia do motor neste giro/carga)"");
    draw_set_color(make_color_rgb(170, 200, 255));
    draw_text(_x + 310, _y + _h - 110, ""avanco "" + string(round(e.avanco)) + "" (ideal "" + string(round(e.ideal)) + "")  limite knock "" + string(round(global.mod_dist_knock_limite(car))) + ""  knock "" + string(round(e.knock)));
    draw_text(_x + 310, _y + _h - 92, ""bonus "" + string(round(e.bonus * 100)) + ""%   faisca "" + string(round(e.q * 100)) + ""% ("" + string(round(e.dwell_ms * 10) / 10) + ""ms)   dinheiro $"" + string(global.cash));
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 310, _y + _h - 70, ""setas ajustam (limite do modelo)   TAB loja   F7 padrao   [3] voltar ao original"");
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
    if (!variable_struct_exists(car, ""relacao_final"")) return global.cambio_rel_ref;
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
        case 1: return ""FINAL DRIVE"";
        case 2: return ""RELACION FINAL"";
        case 3: return ""RAPPORTO FINALE"";
        case 4: return ""RAPPORT FINAL"";
        case 5: return ""主减速比"";
        case 6: return ""ГЛАВНАЯ ПАРА"";
        case 7: return ""ファイナル"";
        default: return ""RELAÇÃO FINAL"";
    }
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
    if (file_exists(""mod_distribuidor.ini"")) {
        ini_open(""mod_distribuidor.ini"");
        global.mod_dist_def.avanco_inicial = ini_read_real(""distribuidor"", ""avanco_inicial"", global.mod_dist_def.avanco_inicial);
        global.mod_dist_def.cent_total = ini_read_real(""distribuidor"", ""cent_total"", global.mod_dist_def.cent_total);
        global.mod_dist_def.rpm_inicio = ini_read_real(""distribuidor"", ""rpm_inicio"", global.mod_dist_def.rpm_inicio);
        global.mod_dist_def.rpm_fim = ini_read_real(""distribuidor"", ""rpm_fim"", global.mod_dist_def.rpm_fim);
        global.mod_dist_def.curva = ini_read_real(""distribuidor"", ""curva"", global.mod_dist_def.curva);
        global.mod_dist_def.vac_total = ini_read_real(""distribuidor"", ""vac_total"", global.mod_dist_def.vac_total);
        global.mod_dist_def.vac_limiar = ini_read_real(""distribuidor"", ""vac_limiar"", global.mod_dist_def.vac_limiar);
        global.mod_dist_def.dwell = ini_read_real(""distribuidor"", ""dwell"", global.mod_dist_def.dwell);
        global.mod_dist_def.ganho = ini_read_real(""distribuidor"", ""ganho"", global.mod_dist_def.ganho);
        global.phys_def.raio = ini_read_real(""fisica"", ""raio"", global.phys_def.raio);
        global.phys_def.eficiencia = ini_read_real(""fisica"", ""eficiencia"", global.phys_def.eficiencia);
        global.phys_def.mu = ini_read_real(""fisica"", ""mu"", global.phys_def.mu);
        global.phys_def.share = ini_read_real(""fisica"", ""share"", global.phys_def.share);
        global.phys_def.embreagem = ini_read_real(""fisica"", ""embreagem"", global.phys_def.embreagem);
        global.phys_def.inercia = ini_read_real(""fisica"", ""inercia"", global.phys_def.inercia);
        global.cambio_rel_min = ini_read_real(""cambio"", ""rel_min"", global.cambio_rel_min);
        global.cambio_rel_max = ini_read_real(""cambio"", ""rel_max"", global.cambio_rel_max);
        global.cambio_rel_ref = ini_read_real(""cambio"", ""rel_ref"", global.cambio_rel_ref);
        global.cambio_passo = ini_read_real(""cambio"", ""passo"", global.cambio_passo);
        global.cambio_limite_pct = ini_read_real(""cambio"", ""limite_pct"", global.cambio_limite_pct);
        global.cambio_vmax = ini_read_real(""cambio"", ""vmax"", global.cambio_vmax);
        global.cambio_custo = ini_read_real(""cambio"", ""custo"", global.cambio_custo);
        global.mod_rev = ini_read_real(""mod"", ""rev"", 0);
        global.dev_cash = ini_read_real(""dev"", ""cash"", 0);
        global.dev_cash_modo = ini_read_real(""dev"", ""cash_modo"", 0);
        ini_close();
    }
};

// le o ini uma vez no boot (antes so era definida, nunca chamada)
global.mod_dist_carregar();
");

g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    "if (global.mod_dist_ativo) { global.torque *= global.mod_dist_fator(); global.ignicao = global.mod_dist_estado.avanco; } var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    false);

// --- FASE 1: motor com consequencia ---
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "    var _camshaft_low = 1;",
    "    global.afr = global.motor_afr(global.car_data[global.vehicles_array], global.rpm, global.car_data[global.vehicles_array].rpm, global.turbo, global.fuel); var _camshaft_low = 1;",
    false);

// todo efeito de peca/ajuste entra no torque aqui, antes do hp
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.hp = clamp(global.hp, 0, global.maxhp * 1.2);",
    "global.torque *= global.motor_torque(global.car_data[global.vehicles_array], global.rpm, global.car_data[global.vehicles_array].rpm, global.afr);\nglobal.torque = global.pt_torque(global.torque, global.rpm, global.car_data[global.vehicles_array].rpm, global.comando); global.hp = clamp(global.pot_hp(global.torque, global.rpm, global.maxtorque, global.car_data[global.vehicles_array].rpm, global.maxhp), 0, global.maxhp * 1.2);",
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
    "global.rpm = clamp(global.rpm, global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm); if (global.engine == 1 && key_up && (global.gear == 0 || key_clutch)) { global.rpm = clamp(global.rpm + global.phys_rev(global.torque, _camshaft_response, global.dt), global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm); }",
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

g.QueueAppend("gml_Object_obj_player_Step_0", @"
global.pt_step();
if (global.mod_dist_ativo) {
    global.mod_dist_dano();
}
");
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd;",
    "var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd; var _mcarb = global.mod_carb_cfg();",
    false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _gicle_desvio = _gicle_ativo - _gicle_ideal;",
    "var _gicle_desvio = _gicle_ativo - _gicle_ideal + global.mod_carb_desvio(_rpm_ratio, _mcarb, _gicle_ideal);",
    false);

g.QueueAppend("gml_Object_obj_noguiin_Draw_64", @"
// aplica o ini ao carro salvo quando o arquivo muda (1x por segundo, no maximo)
global.mod_cfg_aplicar();
global.dev_cash_aplicar();
global.cash_guard();
// desenha so no Draw GUI: fica acima do passe CRT, sem piscar
global.mod_dist_ui();

// telemetria do noguintech (item comprado) + placa + FIPE
if (variable_global_exists(""car_data"") && is_array(global.car_data)
    && global.vehicles_array >= 0 && global.vehicles_array < array_length(global.car_data)) {
    var _ntc = global.car_data[global.vehicles_array];
    if (variable_struct_exists(_ntc, ""noguiintech"") && _ntc.noguiintech != 0) {
        var _tx = display_get_gui_width() - 250;
        var _ty = 90;
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        draw_set_alpha(0.85);
        draw_text_transformed(_tx, _ty, ""NOGUITECH v"" + string(_ntc.noguiintech), 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 16, ""torque "" + string(round(global.torque)) + "" Nm"", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 30, ""potencia "" + string(round(global.hp)) + "" hp"", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 44, ""AFR "" + string(round(global.afr * 10) / 10), 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 58, ""avanco "" + string(round(global.ignicao)) + "" graus"", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 72, ""patinacao "" + string(round(global.phys_slip * 100)) + ""%"", 0.5, 0.5, 0);
        var _pl = ""SEM PLACA"";
        if (variable_struct_exists(_ntc, ""plate"")) _pl = string(_ntc.plate);
        draw_text_transformed(_tx, _ty + 86, ""placa "" + _pl, 0.5, 0.5, 0);
        if (variable_global_exists(""carro"")) {
            draw_text_transformed(_tx, _ty + 100, ""FIPE $"" + string(ValueCars()), 0.5, 0.5, 0);
        }
        if (global.mod_dist_estado.knock > 0) {
            draw_set_color(make_color_rgb(255, 90, 60));
            draw_text_transformed(_tx, _ty + 118, ""KNOCK"", 0.6, 0.6, 0);
            draw_set_color(c_white);
        }
        draw_set_alpha(1);
    }
}
");


// ---- CAMBIOS 7 E 8 MARCHAS ----
// razoes: cambio 8 = 7 marchas, cambio 9 = 8 marchas. max_gears sai do array_length,
// entao troca, automatico, HUD e IA (que copia as razoes do jogador) ja funcionam.
g.QueueFindReplace("gml_Object_obj_player_Create_0", "max_gears = array_length(gear_ratios);", "if (global.car_data[global.vehicles_array].cambio == 8)\n{\n    gear_ratios = [9.35, 6.2, 4.5, 3.5, 2.75, 2.15, 1.7];\n    original_ratios = [9.35, 6.2, 4.5, 3.5, 2.75, 2.15, 1.7];\n}\nelse if (global.car_data[global.vehicles_array].cambio == 9)\n{\n    gear_ratios = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];\n    original_ratios = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];\n}\nmax_gears = array_length(gear_ratios);", false);

// nomes: 7 terminadores cobrem as 5 tabelas (case "cambio" indexado por cambio-1,
// GetPartLevelsArray/2 e GetNamePartsVenture/2 indexados por nivel)
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6 marchas\"]", "\"GSD 6 marchas\", \"Manual 7 marchas\", \"Manual 8 marchas\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"6-speed GSD\"]", "\"6-speed GSD\", \"7-speed Manual\", \"8-speed Manual\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6 marce\"]", "\"GSD 6 marce\", \"Manuale 7 marce\", \"Manuale 8 marce\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6 rapports\"]", "\"GSD 6 rapports\", \"Manuelle 7 rapports\", \"Manuelle 8 rapports\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"6速自动\", \"GSD 6速\"]", "\"6速自动\", \"GSD 6速\", \"7速手动\", \"8速手动\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"6速オートマ\", \"GSD 6速\"]", "\"6速オートマ\", \"GSD 6速\", \"7速マニュアル\", \"8速マニュアル\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6-ст.\"]", "\"GSD 6-ст.\", \"7-ст. механика\", \"8-ст. механика\"]", true);

// precos: nivel 10 = 45.000, nivel 11 = 85.000 (as duas tabelas paralelas)
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "[250, 750, 1500, 6500, 10500, 17500, 16750, 23450, 32500, 65000]", "[250, 750, 1500, 6500, 10500, 17500, 16750, 23450, 32500, 65000, 45000, 85000]", false);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "[250, 750, 6500, 6500, 10500, 17500, 16750, 23450, 32500, 65000]", "[250, 750, 6500, 6500, 10500, 17500, 16750, 23450, 32500, 65000, 45000, 85000]", false);

// item no banco de inventario
g.QueueFindReplace("gml_GlobalScript_Inventory", "_r(\"gsd_6m\", \"GSD 6 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");", "_r(\"gsd_6m\", \"GSD 6 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");\n_r(\"manual_7m\", \"Manual 7 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");\n_r(\"manual_8m\", \"Manual 8 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");", false);

// instalacao pelo minigame (manual: at = 0)
g.QueueFindReplace("gml_GlobalScript_minigames_noguiintech", "case \"gsd_6m\":\n                    _cambio_val = 7;\n                    _car.at = 1;\n                    break;", "case \"gsd_6m\":\n                    _cambio_val = 7;\n                    _car.at = 1;\n                    break;\n                case \"manual_7m\":\n                    _cambio_val = 8;\n                    _car.at = 0;\n                    break;\n                case \"manual_8m\":\n                    _cambio_val = 9;\n                    _car.at = 0;\n                    break;", false);

// entrega: (categoria 6, nivel 10/11) -> id do item
g.QueueFindReplace("gml_GlobalScript_Delivery", "case 9:\n                    return \"gsd_6m\";", "case 9:\n                    return \"gsd_6m\";\n                case 10:\n                    return \"manual_7m\";\n                case 11:\n                    return \"manual_8m\";", false);

// caderno de marchas: 8 linhas nao podem invadir o texto de custo
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0",
    "var _start_y = _notebook_y - 80;",
    "var _start_y = _notebook_y - 80 - ((obj_player.max_gears > 6) ? 18 : 0);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0",
    "var _spacing = 35;",
    "var _spacing = (obj_player.max_gears > 6) ? 26 : 35;", false);

// ---- BUG 17: itens novos nao herdam use_func (_r nao define) ----
// o laco _cambio_keys e o unico lugar que atribui item_use_dinamometro aos cambios
g.QueueFindReplace("gml_GlobalScript_Inventory", "var _cambio_keys = [\"transmissao_original\", \"transmissao_forjada\", \"transmissao_desempenho\", \"manual_4m\", \"manual_5m\", \"manual_6m\", \"auto_4m\", \"auto_5m\", \"auto_6m\", \"gsd_6m\"];", "var _cambio_keys = [\"transmissao_original\", \"transmissao_forjada\", \"transmissao_desempenho\", \"manual_4m\", \"manual_5m\", \"manual_6m\", \"auto_4m\", \"auto_5m\", \"auto_6m\", \"gsd_6m\", \"manual_7m\", \"manual_8m\"];", false);

// ---- BUG 18: campo_val real (1..9) para _inventory_get_item_by_campo("cambio") ----
// sem campo_val ele compara posicao 0-based com cambio 1-based e nunca acha
g.QueueFindReplace("gml_GlobalScript_Inventory", "_r(\"manual_8m\", \"Manual 8 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");", "_r(\"manual_8m\", \"Manual 8 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_4m\")).campo_val = 1;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_5m\")).campo_val = 2;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_6m\")).campo_val = 3;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"auto_4m\")).campo_val = 4;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"auto_5m\")).campo_val = 5;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"auto_6m\")).campo_val = 6;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"gsd_6m\")).campo_val = 7;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_7m\")).campo_val = 8;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_8m\")).campo_val = 9;", false);

// ================= CAMBIO: relacao final + limites =================
g.QueueFindReplace("gml_GlobalScript_EstruturaSalvamento", "data.gears_mod = [];", "data.gears_mod = [];\ndata.relacao_final = 3.73;", false);
g.QueueFindReplace("gml_Object_obj_player_Create_0", "max_gears = array_length(gear_ratios);", "max_gears = array_length(gear_ratios);\nvar _rel_final = 1;\nif (variable_struct_exists(global.car_data[global.vehicles_array], \"relacao_final\")) {\n    var _rf_tmp = global.car_data[global.vehicles_array].relacao_final;\n    if (is_real(_rf_tmp) && _rf_tmp > 0) {\n        _rel_final = clamp(_rf_tmp, global.cambio_rel_min, global.cambio_rel_max) / global.cambio_rel_ref;\n    }\n}\nif (_rel_final != 1) {\n    for (var _ri = 0; _ri < max_gears; _ri++) {\n        gear_ratios[_ri] = gear_ratios[_ri] * _rel_final;\n        if (_ri < array_length(original_ratios)) {\n            original_ratios[_ri] = original_ratios[_ri] * _rel_final;\n        }\n    }\n}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                if (arg0 == 0)\n                {\n                    return 1;\n                }\n                else if (arg0 >= 4)\n                {\n                    return 0.2;\n                }\n                else\n                {\n                    return 0.5;\n                }", "                return global.cambio_limite_pct;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                var _limite = get_limite_marcha(i, obj_player.max_gears);\n                var _valor_min = _gear_original[i] - _limite;\n                var _valor_max = _gear_original[i] + _limite;", "                var _limite = max(0.3, _gear_original[i] * get_limite_marcha(i, obj_player.max_gears));\n                var _valor_min = max(0.3, _gear_original[i] - _limite);\n                var _valor_max = _gear_original[i] + _limite;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                global.gear_selecionada = 0;\n                global.gear_editando = false;\n                global.gear_area_botoes = false;\n                global.gear_botao_selecionado = 0;\n            }", "                global.gear_selecionada = 0;\n                global.gear_editando = false;\n                global.gear_area_botoes = false;\n                global.gear_botao_selecionado = 0;\n                global.gear_temp_rel = global.cambio_rel_valor();\n                global.gear_rel_original = global.gear_temp_rel;\n            }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "            var _custo_cambio = 775 * _marchas_modificadas;", "            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)\n            {\n                _marchas_modificadas++;\n            }\n            var _custo_cambio = global.cambio_custo * _marchas_modificadas;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears - 1);", "global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    draw_set_halign(fa_center);\n                    draw_set_color(c_yellow);\n                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n                }\n            }\n            draw_set_halign(fa_left);\n            draw_set_color(c_white);\n            draw_text(_notebook_x - 290, _notebook_y + 150, txt_custo);", "                    draw_set_halign(fa_center);\n                    draw_set_color(c_yellow);\n                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n                }\n            }\n            var _rel_idx = obj_player.max_gears;\n            var _y_rel = _start_y + (obj_player.max_gears * _spacing) + 8;\n            var _rel_min = global.cambio_cfg().rel_min;\n            var _rel_max = global.cambio_cfg().rel_max;\n            var _rel_cor = (!global.gear_area_botoes && global.gear_selecionada == _rel_idx && !global.gear_editando) ? global.red : 16777215;\n            if (global.gear_selecionada == _rel_idx && global.gear_editando) _rel_cor = global.gold;\n            draw_set_halign(fa_left);\n            draw_set_color(_rel_cor);\n            draw_set_font(NotoSans);\n            draw_text(_marcha_x, _y_rel, global.cambio_txt_rel() + \":\");\n            draw_set_color(c_gray);\n            draw_text(_notebook_x - 180, _y_rel, string_format(global.gear_rel_original, 2, 2));\n            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)\n            {\n                draw_set_color(c_yellow);\n                draw_text(_notebook_x - 130, _y_rel, \">\");\n            }\n            draw_set_color((abs(global.gear_temp_rel - global.gear_rel_original) > 0.005) ? 65280 : 16777215);\n            draw_text(_notebook_x - 100, _y_rel, string_format(global.gear_temp_rel, 2, 2));\n            var _rbar_x = _notebook_x + 50;\n            draw_set_color(c_dkgray);\n            draw_roundrect(_rbar_x, _y_rel - 6, _rbar_x + 200, _y_rel + 6, false);\n            var _rpct = (global.gear_temp_rel - _rel_min) / (_rel_max - _rel_min);\n            draw_set_color(_rel_cor);\n            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, false);\n            draw_set_color(c_black);\n            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, true);\n            var _rhover = point_in_rectangle(mouse_x, mouse_y, _rbar_x, _y_rel - 15, _rbar_x + 200, _y_rel + 15);\n            if (mouse_check_button_pressed(mb_left) && _rhover)\n            {\n                global.gear_selecionada = _rel_idx;\n                global.gear_editando = true;\n                global.gear_area_botoes = false;\n            }\n            if (global.gear_editando && global.gear_selecionada == _rel_idx && mouse_check_button(mb_left) && _rhover)\n            {\n                var _rn = _rel_min + (clamp((mouse_x - _rbar_x) / 200, 0, 1) * (_rel_max - _rel_min));\n                global.gear_temp_rel = round(_rn * 100) / 100;\n            }\n            if (global.gear_editando && global.gear_selecionada == _rel_idx)\n            {\n                if (keyboard_check_pressed(vk_left)) global.gear_temp_rel = max(global.gear_temp_rel - global.cambio_passo, _rel_min);\n                if (keyboard_check_pressed(vk_right)) global.gear_temp_rel = min(global.gear_temp_rel + global.cambio_passo, _rel_max);\n                draw_set_halign(fa_center);\n                draw_set_color(c_yellow);\n                draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n            }\n            draw_set_halign(fa_left);\n            draw_set_color(c_white);\n            draw_text(_notebook_x - 290, _notebook_y + 176, txt_custo);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "draw_text(_notebook_x + 80, _notebook_y + 150, \"$\" + string(floor(_custo_cambio)));", "draw_text(_notebook_x + 80, _notebook_y + 176, \"$\" + string(floor(_custo_cambio)));", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "draw_text_transformed(_notebook_x, _notebook_y + 110, t + string(global.car_data[global.vehicles_array].gears_mod), 1, 1, 0);", "draw_text_transformed(_notebook_x, _notebook_y + 200, t + string(global.car_data[global.vehicles_array].gears_mod) + \"  |  \" + global.cambio_txt_rel() + \": \" + string(global.car_data[global.vehicles_array].relacao_final), 1, 1, 0);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i];", "global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i] * global.cambio_rel_ref / global.gear_rel_original;", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "obj_player.gear_ratios[i] = global.gear_temp_array[i];", "obj_player.gear_ratios[i] = (global.gear_temp_array[i] * global.cambio_rel_ref / global.gear_rel_original) * (global.gear_temp_rel / global.cambio_rel_ref);", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.car_data[global.vehicles_array].cambio_mod = 1;", "global.car_data[global.vehicles_array].cambio_mod = 1;\nglobal.car_data[global.vehicles_array].relacao_final = global.gear_temp_rel;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (_gearlimit > 400)\n{\n    _gearlimit = 400;\n}\nif (global.kmh > 400)\n{\n    global.kmh = 400;\n}", "if (_gearlimit > global.cambio_vmax)\n{\n    _gearlimit = global.cambio_vmax;\n}\nif (global.kmh > global.cambio_vmax)\n{\n    global.kmh = global.cambio_vmax;\n}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.kmh = clamp(global.kmh, 0, 400);", "global.kmh = clamp(global.kmh, 0, global.cambio_vmax);", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "else if (global.kmh >= 300)\n    {\n        _fator_velocidade = 3.2;\n    }", "else if (global.kmh >= 400)\n    {\n        _fator_velocidade = 3.2;\n    }", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "else if (global.kmh >= 250 && global.kmh < 300)", "else if (global.kmh >= 250 && global.kmh < 400)", false);
// ===================================================================

// ---- powertrain: corte de giro corta torque, corte de troca corta torque, sem parede ----
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_target_speed = min(global.kmh + _acceleration, _gearlimit);\n        if (global.kmh < _gearlimit)", "_acceleration *= (1 - global.pt_em_corte());\n        if (global.pt_shift_t > 0) _acceleration *= 0.15;\n        _target_speed = global.kmh + _acceleration;\n        if (global.kmh < global.cambio_vmax)", false);
// upshift nao recalculava _gearlimit: o rpm nao caia na troca (bug do jogo)
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (key_next && global.gear < max_gears)\n        {\n            global.gear += 1;", "if (key_next && global.gear < max_gears)\n        {\n            global.gear += 1;\n            _gearlimit = global.pt_gear_limit();", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (global.gear < max_gears)\n                {\n                    global.gear += 1;\n                    if (_gsd)", "if (global.gear < max_gears)\n                {\n                    global.gear += 1;\n                    _gearlimit = global.pt_gear_limit();\n                    if (_gsd)", false);
// limitadores artificiais que brigavam com a curva de torque
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_base_acceleration *= _power_band_mult;", "_power_band_mult = 1;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "var _speed_resistance = lerp(1, 0.5, _smooth_ratio);", "var _speed_resistance = 1;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "play_audio(snd_gearchange);", "global.pt_shift_t = global.pt_shift_time; play_audio(snd_gearchange);", true);

g.Import();
ScriptMessage("MARK distribuidor=v2 instalado");
