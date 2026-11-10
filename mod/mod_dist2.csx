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
    // Noguiintech troca o carburador por injecao: distribuidor e gicle ficam fora
    if (variable_struct_exists(car, ""noguiintech"") && is_real(car.noguiintech) && car.noguiintech != 0) return 0;
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
global.mod_num_txt = function(_v) {
    if (!is_real(_v)) return string(_v);
    var _r = round(_v * 1000) / 1000;
    if (abs(_r - round(_r)) < 0.001) return string(round(_r));
    return string(_r);
};

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
            avanco_extra: 0,
            retardo: 0,
            ver: 2
        };
    }
    // config antiga (ganho vinha 50 e diluia o bonus): sobe uma vez
    if (!variable_struct_exists(car.dist_cfg, ""ver"")) {
        car.dist_cfg.ver = 2;
        car.dist_cfg.ganho = 100;
    }
    if (!variable_struct_exists(car.dist_cfg, ""avanco_extra"")) car.dist_cfg.avanco_extra = 0;
    if (!variable_struct_exists(car.dist_cfg, ""retardo"")) car.dist_cfg.retardo = 0;
    if (!variable_struct_exists(car.dist_cfg, ""mech_custom"")) {
        car.dist_cfg.mech_custom = 0;
        car.dist_cfg.mech_rpm = [800, 1200, 1600, 2000, 2600, 3400, 4500, 6500];
        car.dist_cfg.mech_deg = [0, 6, 12, 17, 21, 24, 24, 24];
        car.dist_cfg.spring_l0 = 800;
        car.dist_cfg.spring_l1 = 1600;
        car.dist_cfg.spring_h0 = 2600;
        car.dist_cfg.spring_h1 = 4500;
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
    // o modelo fisico de distribuidor/carburador ja entrou no torque via fuel.eff
    if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) return 1;
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
    // fase 1 do modelo novo: knock so reduz torque. dano de pistao fica para a fase 2
    if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) return;
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
    if (keyboard_check_pressed(vk_f6)) {
        if (!is_undefined(car) && global.mod_dist_tipo(car) != 1) {
            e.menu = 0;
            e.aviso = ""Noguiintech: motor injetado"";
            return;
        }
        e.menu = (e.menu == 0) ? 1 : 0;
        e.sel = 0;
        e.aviso = """";
    }
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
            var _cn = (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) ? 5 : 3;
            if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod _cn;
            if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + _cn - 1) mod _cn;
            var _d3 = 0;
            if (keyboard_check_pressed(vk_right)) _d3 = 1;
            if (keyboard_check_pressed(vk_left)) _d3 = -1;
            if (_d3 != 0) {
                if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) {
                    if (!variable_struct_exists(_cc, ""jet"")) _cc.jet = 80;
                    if (!variable_struct_exists(_cc, ""idle_turns"")) _cc.idle_turns = 2.5;
                    if (!variable_struct_exists(_cc, ""pv"")) _cc.pv = 6.5;
                    if (!variable_struct_exists(_cc, ""pump_enrich"")) _cc.pump_enrich = 2;
                    if (!variable_struct_exists(_cc, ""pump_dur"")) _cc.pump_dur = 0.35;
                    switch (e.sel) {
                        case 0: _cc.jet = clamp(_cc.jet + (_d3 * 0.25), 60, 100); break;
                        case 1: _cc.idle_turns = clamp(_cc.idle_turns + (_d3 * 0.0625), 1, 4); break;
                        case 2: _cc.pv = clamp(_cc.pv + (_d3 * 0.125), 3.5, 10.5); break;
                        case 3: _cc.pump_enrich = clamp(_cc.pump_enrich + (_d3 * 0.25), 1, 3); break;
                        case 4: _cc.pump_dur = clamp(_cc.pump_dur + (_d3 * 0.05), 0.2, 0.5); break;
                    }
                } else {
                    switch (e.sel) {
                        case 0: _cc.lenta = clamp(_cc.lenta + _d3, 20, 60); break;
                        case 1: _cc.agulha = clamp(_cc.agulha + _d3, 20, 80); break;
                        case 2: _cc.borboleta = clamp(_cc.borboleta + (_d3 * 5), 0, 100); break;
                    }
                }
                e.aviso = """";
            }
            if (keyboard_check_pressed(vk_f7)) {
                var _gi7 = global.mod_carb_gicle_ideal(max(1, car.carburador));
                _cc.lenta = round(_gi7 * 0.35);
                _cc.agulha = round(_gi7 * 0.90);
                _cc.borboleta = 50;
                _cc.jet = 80;
                _cc.idle_turns = 2.5;
                _cc.pv = 6.5;
                _cc.pump_enrich = 2;
                _cc.pump_dur = 0.35;
                e.aviso = ""carburador no padrao"";
            }
        }
        return;
    }
    if (keyboard_check_pressed(ord(""3""))) global.mod_dist_remover();
    if (keyboard_check_pressed(vk_f7)) { global.mod_dist_padrao_modelo(); e.aviso = ""padrao do modelo""; }
    if (!variable_struct_exists(e, ""curve_edit"")) e.curve_edit = 0;
    if (keyboard_check_pressed(vk_f8)) {
        e.curve_edit = e.curve_edit ? 0 : 1;
        e.sel = 0;
        return;
    }
    if (e.curve_edit) {
        var _cd = global.mod_dist_cfg();
        if (!variable_struct_exists(_cd, ""mech_deg"")) return;
        if (keyboard_check_pressed(ord(""1""))) { global.mod_fuel_preset(""street""); e.aviso = ""curva street""; }
        if (keyboard_check_pressed(ord(""2""))) { global.mod_fuel_preset(""strip""); e.aviso = ""curva strip""; }
        if (keyboard_check_pressed(ord(""3""))) { global.mod_fuel_preset(""race""); e.aviso = ""curva race""; }
        if (keyboard_check_pressed(ord(""0""))) { _cd.mech_custom = 0; e.aviso = ""curva automatica""; }
        var _nc = 12;
        if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod _nc;
        if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + _nc - 1) mod _nc;
        var _dc = 0;
        if (keyboard_check_pressed(vk_right)) _dc = 1;
        if (keyboard_check_pressed(vk_left)) _dc = -1;
        if (_dc != 0) {
            if (e.sel < 8) {
                _cd.mech_deg[e.sel] = clamp(_cd.mech_deg[e.sel] + (_dc * 0.5), 0, 30);
                _cd.mech_custom = 1;
            } else if (e.sel == 8) _cd.spring_l0 = clamp(_cd.spring_l0 + (_dc * 50), 800, 1500);
            else if (e.sel == 9) _cd.spring_l1 = clamp(_cd.spring_l1 + (_dc * 50), 1500, 2500);
            else if (e.sel == 10) _cd.spring_h0 = clamp(_cd.spring_h0 + (_dc * 50), 2000, 3500);
            else _cd.spring_h1 = clamp(_cd.spring_h1 + (_dc * 50), 3000, 5000);
            if (e.sel >= 8) global.mod_fuel_spring_kit();
            e.aviso = """";
        }
        return;
    }
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    if (!variable_struct_exists(d, ""avanco_extra"")) d.avanco_extra = 0;
    if (!variable_struct_exists(d, ""retardo"")) d.retardo = 0;
    var _n = 12;
    if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod _n;
    if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + _n - 1) mod _n;
    var _dir = 0;
    if (keyboard_check_pressed(vk_right)) _dir = 1;
    if (keyboard_check_pressed(vk_left)) _dir = -1;
    if (_dir != 0) {
        // passo 4x menor que o antigo: 1 vira 0,25, 2 vira 0,5, 100 vira 25
        switch (e.sel) {
            case 0: d.avanco_inicial = clamp(d.avanco_inicial + (_dir * 0.25), lim[0], lim[1]); break;
            case 1: d.cent_total = clamp(d.cent_total + (_dir * 0.5), 0, lim[2]); break;
            case 2: d.rpm_inicio = clamp(d.rpm_inicio + (_dir * 25), 600, 4000); break;
            case 3: d.rpm_fim = clamp(d.rpm_fim + (_dir * 25), 1200, lim[3]); break;
            case 4: d.curva = clamp(d.curva + (_dir * 0.25), 0, 2); break;
            case 5: d.vac_total = clamp(d.vac_total + (_dir * 0.25), 0, lim[4]); break;
            case 6: d.vac_limiar = clamp(d.vac_limiar + (_dir * 0.025), -0.8, 1); break;
            case 7: d.dwell = clamp(d.dwell + (_dir * 0.25), lim[5], lim[6]); break;
            case 8: d.desgaste = clamp(d.desgaste + (_dir * 1.25), 0, 100); break;
            case 9: d.ganho = clamp(d.ganho + (_dir * 1.25), 0, lim[7]); break;
            case 10: d.avanco_extra = clamp(d.avanco_extra + (_dir * 0.25), 0, 15); break;
            case 11: d.retardo = clamp(d.retardo + (_dir * 0.25), 0, 15); break;
        }
        e.aviso = """";
    }
};

// roda o modelo com um parametro no valor dado e devolve o bonus resultante
global.mod_dist_sim = function(_sel, _val) {
    var d = global.mod_dist_cfg();
    var _k = [""avanco_inicial"", ""cent_total"", ""rpm_inicio"", ""rpm_fim"", ""curva"", ""vac_total"", ""vac_limiar"", ""dwell"", ""desgaste"", ""ganho"", ""avanco_extra"", ""retardo""];
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
    var _h = 480;
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
    if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) {
        if (!variable_struct_exists(cfg, ""jet"")) cfg.jet = 80;
        if (!variable_struct_exists(cfg, ""idle_turns"")) cfg.idle_turns = 2.5;
        if (!variable_struct_exists(cfg, ""pv"")) cfg.pv = 6.5;
        if (!variable_struct_exists(cfg, ""pump_enrich"")) cfg.pump_enrich = 2;
        if (!variable_struct_exists(cfg, ""pump_dur"")) cfg.pump_dur = 0.35;
        _labels = [""Gicle principal"", ""Mistura de lenta (voltas)"", ""Power valve (inHg)"", ""Bomba de aceleracao (AFR)"", ""Duracao da bomba (s)""];
        _vals = [cfg.jet, cfg.idle_turns, cfg.pv, cfg.pump_enrich, cfg.pump_dur];
    }
    var _nrows = array_length(_labels);
    for (var i = 0; i < _nrows; i++) {
        var _ry = _y + 54 + (i * 30);
        if (i == e.sel) {
            draw_set_color(make_color_rgb(50, 70, 60));
            draw_rectangle(_x + 20, _ry - 2, _x + _w - 20, _ry + 24, false);
        }
        draw_set_color(c_white);
        draw_text(_x + 30, _ry + 4, _labels[i]);
        draw_set_color(make_color_rgb(255, 210, 120));
        draw_text(_x + _w - 140, _ry + 4, global.mod_num_txt(_vals[i]));
    }
    var _d1 = [""Mistura em marcha lenta e na saida."",
               ""Mistura na faixa media (cruzeiro)."",
               ""Abertura inicial da borboleta.""];
    var _d2 = [""Baixo = pobre, falha frio. Alto = rico, afoga e suja."",
               ""Baixo = pobre em media. Alto = rico, perde potencia e gasta."",
               ""Baixo = lenta baixa. Alto = lenta alta e resposta brusca.""];
    if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) {
        _d1 = [""Tamanho do gicle principal. Maior = mais rico."",
               ""Parafuso de mistura da lenta, em voltas."",
               ""Power valve abre no vacuo abaixo deste valor."",
               ""Bomba: enriquecimento no tip-in, em pontos de AFR."",
               ""Quanto tempo o jato da bomba leva para sumir.""];
        _d2 = [""80 e o neutro (WOT ~13). 100 afoga. 60 empobrece."",
               ""2,5 voltas ~ 13,0 de AFR. Menos volta = mais pobre."",
               ""No WOT o vacuo cai e a valvula enriquece ~1 ponto."",
               ""2 pontos por acelerada cheia. 1 fraco, 3 forte."",
               ""0,35 s e o padrao. 0,20 curto, 0,50 longo.""];
    }
    draw_set_color(make_color_rgb(255, 235, 160));
    var _help = e.sel;
    if (_help < 0 || _help >= array_length(_d1)) _help = 0;
    draw_text(_x + 30, _y + 220, _d1[_help]);
    draw_text(_x + 30, _y + 238, _d2[_help]);
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
    var _usa_fuel = (variable_global_exists(""fuel_model_on"") && global.fuel_model_on);
    var _usa_phys = (variable_global_exists(""phys_v28_on"") && global.phys_v28_on);
    if (_usa_fuel && _usa_phys) {
        // efficiency_afr / efficiency_timing rodam dentro do phys_step, por substep
    } else if (_usa_fuel) {
        var _thr = 0;
        if (key_up) _thr = 1;
        global.fuel_sys_step(_rpm, _thr, global.dt / 60);
        _m *= global.fuel_sys.eff;
    } else {
        var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, ""carburador"")) : 0;
        var _afr_eff = _afr;
        if (_carb <= 0) _afr_eff = global.motor_afr_inj(_rpm, _rpm_max, global.turbo, global.fuel, global.injecao);
        _m *= global.motor_mistura_mult(_afr_eff, _rpm, _rpm_max, global.turbo, global.fuel);
        _m *= global.motor_ignicao_mult(global.ignicao);
    }
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
    var _fz = _massa_kg * 9.81;
    if (global.aero_ativo == 1) _fz += global.aero_f_down();
    return _fz * _mu * _share;
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

// ================= AERODINAMICA (SI -> escalas do jogo) =================
// global.dt = (delta_time/1e6)*60 -> 1.0 por frame a 60 fps (04888).
//   _acceleration e aplicada por lerp(0.05) -> 1 unidade = 0.8333 m/s2
//   _desacel_arrasto e subtraida direto  -> 1 unidade = 16.667 m/s2
global.aero_rho = 1.225;
global.aero_g = 9.81;
global.aero_crr = 0.012;
global.aero_ar = 2.5;
global.aero_e = 0.85;
global.aero_k_ind = 1 / (pi * global.aero_e * global.aero_ar);
global.aero_cl = 0.45;
global.aero_ativo = 1;
global.aero_forca_ind = 1;

global.aero_massa = function() {
    var _m = global.weight * 1000;
    if (is_undefined(_m) || _m < 100) return 1200;
    return _m;
};

global.aero_v = function() { return max(0, global.kmh) / 3.6; };

// Cd efetivo = Cd do carro + arrasto induzido do downforce (Cl^2/(pi*e*AR))
global.aero_cd = function() {
    var _cd = global.cx;
    if (is_undefined(_cd) || _cd <= 0) _cd = 0.35;
    if (global.aero_forca_ind == 1) {
        _cd += global.aero_k_ind * global.aero_cl * global.aero_cl;
    }
    return _cd;
};

global.aero_f_drag = function() {
    var _v = global.aero_v();
    return 0.5 * global.aero_rho * global.aero_cd() * global.area_frontal * _v * _v;
};

global.aero_f_down = function() {
    if (global.aero_ativo != 1) return 0;
    var _v = global.aero_v();
    return 0.5 * global.aero_rho * global.aero_cl * global.area_frontal * _v * _v;
};

// carga vertical no pneu: peso + downforce
global.aero_fz = function() {
    return global.aero_massa() * global.aero_g + global.aero_f_down();
};

global.aero_f_roll = function() {
    return global.aero_crr * global.aero_fz();
};

// arrasto total convertido para a escala de _desacel_arrasto
global.aero_drag_units = function() {
    if (global.aero_ativo != 1) return 0;
    return (global.aero_f_drag() + global.aero_f_roll()) / (16.667 * global.aero_massa());
};

// potencia em W. Usa a potencia de PICO (global.maxhp), nao a instantanea
// (global.hp): o jogo faz hp = lerp(0, maxhp, rpm/red), entao logo depois de uma
// troca o hp cai junto com o giro, o limite de potencia caia com ele e o motor
// nao conseguia subir de volta (armadilha de realimentacao na 3a marcha em diante).
global.aero_power_w = function() {
    var _p = 0;
    if (variable_global_exists(""maxhp"")) _p = global.maxhp;
    if (is_undefined(_p) || _p < 20) {
        if (is_undefined(global.hp) || global.hp < 20) return 0;
        _p = global.hp;
    }
    return _p * 745.7;
};

// TOPO aerodinamico: resolve P = b*v^3 + c*v (empuxo = arrasto + rolamento)
//   b = 0.5*rho*(Cd_ef*A + Cl*A*Crr)   c = Crr*m*g
// A potencia entra so aqui: e ela que define a velocidade maxima, nao a aceleracao.
global.aero_vmax_ms = function() {
    var _pw = global.aero_power_w();
    if (_pw <= 0) return 300;
    var _a = global.area_frontal;
    if (is_undefined(_a) || _a <= 0) return 300;
    var _b = 0.5 * global.aero_rho * ((global.aero_cd() * _a) + (global.aero_cl * _a * global.aero_crr));
    var _c = global.aero_crr * global.aero_massa() * global.aero_g;
    if (_b <= 0) return 300;
    var _lo = 1;
    var _hi = 300;
    repeat (40) {
        var _mid = (_lo + _hi) / 2;
        if ((_b * _mid * _mid * _mid) + (_c * _mid) < _pw) _lo = _mid; else _hi = _mid;
    }
    return _lo;
};

global.aero_vmax_kmh = function() { return global.aero_vmax_ms() * 3.6; };

// Governador do topo: devolve o multiplicador do EMPUXO conforme a velocidade.
// Abaixo de 92% do topo aerodinamico = 1 (motor entrega tudo, nada de limitar
// aceleracao); de 92% a 100% cai linearmente ate 0. E a aerodinamica que fecha a
// velocidade, nao o motor.
global.aero_gov_mult = function(_kmh) {
    var _vm = global.aero_vmax_kmh();
    if (_vm <= 1) return 1;
    var _knee = 0.92 * _vm;
    if (_kmh <= _knee) return 1;
    return clamp((_vm - _kmh) / max(1, _vm - _knee), 0, 1);
};

// empuxo maximo que a potencia permite na velocidade atual, em unidades de _acceleration
global.aero_thrust_units = function() {
    if (global.aero_ativo != 1) return 999;
    var _v = global.aero_v();
    if (_v < 1) return 999;
    var _pw = global.aero_power_w();
    if (_pw <= 0) return 999;   // sem dado de potencia: falha aberto (nunca trava o carro)
    var _a = _pw / (global.aero_massa() * _v);
    return _a / 0.8333;
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
            borboleta: 50,
            jet: 80,
            idle_turns: 2.5,
            pv: 6.5
        };
    }
    if (!variable_struct_exists(car.carb_cfg, ""jet"")) car.carb_cfg.jet = 80;
    if (!variable_struct_exists(car.carb_cfg, ""idle_turns"")) car.carb_cfg.idle_turns = 2.5;
    if (!variable_struct_exists(car.carb_cfg, ""pv"")) car.carb_cfg.pv = 6.5;
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
    draw_text(_x + 12, _y + 8, ""DISTRIBUIDOR - "" + global.mod_dist_nome[modelo] + ""   F8 curva"");
    global.mod_dist_draw_icon(_x + 150, _y + 200, 300);
    if (modelo <= 0) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 310, _y + 60, ""Este motor e injetado: distribuidor so serve em carburador."");
        draw_set_color(c_white);
        return;
    }
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    if (!variable_struct_exists(e, ""curve_edit"")) e.curve_edit = 0;
    if (e.curve_edit && variable_struct_exists(d, ""mech_deg"")) {
        draw_text(_x + 310, _y + 46, ""CURVA   1 street  2 strip  3 race  0 automatica"");
        var _cl = [""800 rpm"", ""1200"", ""1600"", ""2000"", ""2600"", ""3400"", ""4500"", ""6500"", ""mola leve inicio"", ""mola leve fim"", ""mola pesada inicio"", ""mola pesada fim""];
        var _cv = [d.mech_deg[0], d.mech_deg[1], d.mech_deg[2], d.mech_deg[3], d.mech_deg[4], d.mech_deg[5], d.mech_deg[6], d.mech_deg[7], d.spring_l0, d.spring_l1, d.spring_h0, d.spring_h1];
        for (var _ci = 0; _ci < 12; _ci++) {
            var _ryc = _y + 74 + (_ci * 28);
            if (_ci == e.sel) {
                draw_set_color(make_color_rgb(60, 60, 90));
                draw_rectangle(_x + 300, _ryc - 2, _x + _w - 14, _ryc + 24, false);
            }
            draw_set_color(c_white);
            var _lab = _cl[_ci];
            if (_ci < 8 && array_length(d.mech_rpm) > _ci) _lab = string(d.mech_rpm[_ci]) + "" rpm"";
            draw_text(_x + 310, _ryc + 4, _lab);
            draw_set_color(make_color_rgb(255, 210, 120));
            draw_text(_x + _w - 140, _ryc + 4, global.mod_num_txt(_cv[_ci]));
        }
        draw_set_color(c_white);
        draw_text(_x + 310, _y + _h - 36, d.mech_custom ? ""curva propria ligada"" : ""curva automatica"");
        return;
    }
    if (!variable_struct_exists(d, ""avanco_extra"")) d.avanco_extra = 0;
    if (!variable_struct_exists(d, ""retardo"")) d.retardo = 0;
    var _labels = [""Avanco inicial (graus)"", ""Centrifugo total (graus)"", ""RPM de inicio"", ""RPM de fim"", ""Curva (0 rua, 1 mista, 2 pista)"", ""Vacuo total (graus)"", ""Limiar de vacuo (bar)"", ""Dwell (graus)"", ""Desgaste (%)"", ""Ganho (%)"", ""Avanco extra (graus)"", ""Retardo (graus)""];
    var _vals = [d.avanco_inicial, d.cent_total, d.rpm_inicio, d.rpm_fim, d.curva, d.vac_total, d.vac_limiar, d.dwell, d.desgaste, d.ganho, d.avanco_extra, d.retardo];
    var _i;
    for (_i = 0; _i < 12; _i++) {
        var _ry = _y + 46 + (_i * 24);
        if (_i == e.sel) {
            draw_set_color(make_color_rgb(60, 60, 90));
            draw_rectangle(_x + 300, _ry - 2, _x + _w - 14, _ry + 24, false);
        }
        draw_set_color(c_white);
        draw_text(_x + 310, _ry + 4, _labels[_i]);
        draw_set_color(make_color_rgb(255, 210, 120));
        draw_text(_x + _w - 140, _ry + 4, global.mod_num_txt(_vals[_i]));
    }
    // explicacao do parametro selecionado + efeito real de min/atual/max
    var _d1 = [""Avanco inicial: ponto base da ignicao."",
               ""Centrifugo total: avanco extra que sobe com o giro."",
               ""RPM de inicio: giro onde a curva comeca a entrar."",
               ""RPM de fim: giro onde o avanco satura."",
               ""Curva: formato da entrada das duas molas. Passo 0,25."",
               ""Vacuo total: avanco extra em carga leve."",
               ""Limiar de vacuo: MAP onde o vacuo comeca."",
               ""Dwell: tempo de carga da bobina."",
               ""Desgaste do platinado e do rotor."",
               ""Ganho: quanto do teto do modelo o carro usa."",
               ""Avanco extra: soma graus em qualquer giro e carga."",
               ""Retardo: tira graus do ponto, em qualquer giro e carga.""];
    var _d2 = [""Baixo = fraco e lento. Alto = torque, mas detona."",
               ""Baixo = morre em alta. Alto = potencia, risco de knock."",
               ""Baixo = antecipa cedo. Alto = amarrado ate subir."",
               ""Baixo = trava cedo e perde alta. Alto = estica a curva."",
               ""0 rua, 1 mista, 2 pista. O meio fica entre as duas molas."",
               ""Baixo = consumo pior. Alto = economia, mas detona."",
               ""Baixo = entra cedo. Alto = so em vacuo forte."",
               ""Baixo = faisca fraca. Alto = forte, bobina esquenta."",
               ""Alto = faisca irregular e perda de potencia."",
               ""Baixo = pouco proveito. Alto = 100% do potencial."",
               ""0 nao mexe. Alto adianta o ponto alem da curva."",
               ""0 nao mexe. Alto atrasa o ponto e segura a detonacao.""];
    var _pmin = [lim[0], 0, 600, 1200, 0, 0, -0.8, lim[5], 0, 0, 0, 0];
    var _pmax = [lim[1], lim[2], 4000, lim[3], 2, lim[4], 1, lim[6], 100, lim[7], 15, 15];
    var _bmin = global.mod_dist_sim(e.sel, _pmin[e.sel]);
    var _bmax = global.mod_dist_sim(e.sel, _pmax[e.sel]);
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 310, _y + 348, _d1[e.sel]);
    draw_text(_x + 310, _y + 364, _d2[e.sel]);
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
global.cambio_vmax = 1000;
global.cambio_custo = 775;
global.forja_motor_custo = 2500;

// status da oficina: +70% de resistencia em relacao a peca original.
// o desgaste (ja multiplicado pelo tipo da peca) cai para 1/1.7.
global.forja_motor_tem = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    if (!variable_struct_exists(car, ""forja_motor"")) return 0;
    if (car.forja_motor != 1) return 0;
    if (!variable_struct_exists(car, ""forja_pistao"")) {
        car.forja_pistao = 1;
        car.forja_bielas = 1;
        car.forja_cabecote = 1;
        car.forja_comando = 1;
        car.forja_vira = 1;
        car.forja_bloco = 1;
    }
    return 1;
};
// flag na peca: +70% de resistencia sobre o valor que ela ja tinha (dano / 1.7)
global.forja_motor_mult = function() {
    if (global.forja_motor_tem() != 1) return 1;
    return 1 / 1.7;
};
global.forja_motor_aplicar = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    if (global.forja_motor_tem() == 1) return 0;
    car.forja_motor = 1;
    car.forja_pistao = 1;
    car.forja_bielas = 1;
    car.forja_cabecote = 1;
    car.forja_comando = 1;
    car.forja_vira = 1;
    car.forja_bloco = 1;
    SaveAllCars();
    SaveGame();
    return 1;
};
global.forja_motor_txt = function() {
    if (global.forja_motor_tem() == 1) {
        switch (global.idioma) {
            case 1: return ""INTERNAL PARTS FORGED"";
            case 2: return ""PIEZAS INTERNAS FORJADAS"";
            case 3: return ""PEZZI INTERNI FORGIATI"";
            case 4: return ""PIECES INTERNES FORGEES"";
            default: return ""PEÇAS INTERNAS FORJADAS"";
        }
    }
    switch (global.idioma) {
        case 1: return ""FORGE INTERNAL ENGINE PARTS"";
        case 2: return ""FORJAR PIEZAS INTERNAS DEL MOTOR"";
        case 3: return ""FORGIARE PEZZI INTERNI DEL MOTORE"";
        case 4: return ""FORGER LES PIECES INTERNES"";
        default: return ""FORJAR PEÇAS INTERNAS NO MOTOR"";
    }
};
global.forja_motor_custo_txt = function() {
    var _p = ""$"" + string(floor(global.forja_motor_custo));
    switch (global.idioma) {
        case 1: return ""LABOR COST IS "" + _p + ""\n(+70% PART LIFE)"";
        case 2: return ""LA MANO DE OBRA ES "" + _p + ""\n(+70% DE RESISTENCIA)"";
        case 3: return ""LA MANODOPERA E "" + _p + ""\n(+70% DI RESISTENZA)"";
        case 4: return ""LA MAIN-D'OEUVRE EST "" + _p + ""\n(+70% DE RESISTANCE)"";
        default: return ""A MÃO DE OBRA É "" + _p + ""\n(+70% SOBRE O VALOR ORIGINAL)"";
    }
};

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


// ================= CAMBIO: camada unica (cg v3) =================
global.cg_versao = 3;
global.cg_migrado = 0;
global.cg_probe_t = 0;
global.cg_car_t = 0;
global.cg_vmax_aero = 999;
global.cg_acel_livre = 0;
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
    var _r = global.cg_num(global.cg_get(_car, ""relacao_final"", global.cambio_rel_ref), global.cambio_rel_ref);
    return clamp(_r, global.cambio_rel_min, global.cambio_rel_max);
};

global.cg_base = function(_car, _i) {
    var _g = global.cg_get(_car, ""gears_mod"", -1);
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
    var _corte = global.cg_num(global.cg_get(_car, ""rpm"", 8500), 8500);
    return ((_corte / _r) * 60) / 1000;
};

// """" = ok, senao a descricao do problema
global.cg_validar = function(_car, _cand, _rel, _fab) {
    var _n = array_length(_fab);
    if (!is_array(_cand)) return ""marchas nao carregadas"";
    if (array_length(_cand) != _n) return ""esperava "" + string(_n) + "" marchas e veio "" + string(array_length(_cand));
    for (var i = 0; i < _n; i++) {
        var _v = global.cg_num(_cand[i], -1);
        if (_v < 0.20) return ""marcha "" + string(i + 1) + "" invalida"";
        // 25 cabe 1a curta no topo do diferencial (9.35 * 1.7 * 5.10/3.73)
        if (_v > 25.0) return ""marcha "" + string(i + 1) + "" longa demais"";
        // 0.06 absorve o arredondamento de 0.1 do slider em cima do limite
        if (abs(_v - _fab[i]) > (0.70 * _fab[i] + 0.06)) {
            return ""marcha "" + string(i + 1) + "" fora de +-70% de "" + string(round(_fab[i] * 100) / 100);
        }
    }
    var _r = global.cg_num(_rel, -1);
    if (_r < global.cambio_rel_min || _r > global.cambio_rel_max) {
        return ""relacao final fora de "" + string(global.cambio_rel_min) + "" a "" + string(global.cambio_rel_max);
    }
    return """";
};

// caixa irreal NAO bloqueia: devolve aviso para pintar de amarelo
global.cg_avisos = function(_car, _cand, _rel, _fab) {
    var _n = array_length(_fab);
    var _m = """";
    for (var i = 1; i < _n; i++) {
        var _a = global.cg_num(_cand[i - 1], 0);
        var _b = global.cg_num(_cand[i], 0);
        if (_b >= _a) _m += ""marcha "" + string(i + 1) + "" nao e mais longa que a "" + string(i) + "". "";
        if (_a > 0 && _b > 0) {
            var _salto = 1 - (_b / _a);
            if (_salto < 0.08) _m += ""marcha "" + string(i + 1) + "" quase igual a "" + string(i) + "". "";
            if (_salto > 0.60) _m += ""salto enorme da "" + string(i) + "" para a "" + string(i + 1) + "". "";
        }
    }
    var _topo = global.cg_topo(_car, _cand, _n - 1);
    if (_topo > global.cambio_vmax * 1.5) _m += ""ultima marcha longa demais (topo "" + string(round(_topo)) + "" km/h). "";
    if (_topo > 0 && _topo < 60) _m += ""ultima marcha curta demais (topo "" + string(round(_topo)) + "" km/h). "";
    return _m;
};

global.cg_conferir = function(_car, _cand, _rel) {
    if (global.cg_num(global.cg_get(_car, ""cambio_mod"", 0), 0) != 1) return false;
    var _g = global.cg_get(_car, ""gears_mod"", -1);
    if (!is_array(_g)) return false;
    if (array_length(_g) != array_length(_cand)) return false;
    for (var i = 0; i < array_length(_cand); i++) {
        if (abs(global.cg_num(_g[i], -999) - _cand[i]) > 0.0001) return false;
    }
    if (abs(global.cg_num(global.cg_get(_car, ""relacao_final"", -999), -999) - _rel) > 0.0001) return false;
    return true;
};

// UNICO lugar que escreve gear_ratios / original_ratios / max_gears
global.cg_init_car = function(_inst, _car, _fab) {
    if (global.cg_num(global.cg_get(_car, ""cg_versao"", 0), 0) != global.cg_versao) {
        // esquema antigo guardava a razao BASE: converte para efetiva (x rel/ref)
        var _gmig = global.cg_get(_car, ""gears_mod"", -1);
        if (global.cg_num(global.cg_get(_car, ""cambio_mod"", 0), 0) == 1 && is_array(_gmig)) {
            var _cm = global.cg_rel(_car) / global.cg_ref();
            for (var _mi = 0; _mi < array_length(_gmig); _mi++) {
                var _mv = global.cg_num(_gmig[_mi], -1);
                if (_mv > 0) _gmig[_mi] = _mv * _cm;
            }
            _car.gears_mod = _gmig;
            if (global.cg_migrado != 1) {
                global.cg_migrado = 1;
                global.cg_diag(""MIGRADO razoes base -> efetivas (x"" + string(_cm) + "")"");
            }
        }
        _car.cg_versao = global.cg_versao;
    }
    var _n = array_length(_fab);
    var _mult = global.cg_rel(_car) / global.cg_ref();
    var _gm = global.cg_get(_car, ""gears_mod"", -1);
    var _usar = (global.cg_num(global.cg_get(_car, ""cambio_mod"", 0), 0) == 1)
        && is_array(_gm) && array_length(_gm) == _n;
    // cambio_mod == 1 e a FLAG de ""cambio liberado para editar"" no Tiao
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
    if (_erro != """") return _erro;
    var _om = global.cg_get(_car, ""cambio_mod"", 0);
    var _og = global.cg_get(_car, ""gears_mod"", -1);
    var _orr = global.cg_get(_car, ""relacao_final"", global.cambio_rel_ref);
    _car.cambio_mod = 1;
    _car.gears_mod = CloneArray(_cand);
    _car.relacao_final = _rel;
    _car.cg_versao = global.cg_versao;
    if (!global.cg_conferir(_car, _cand, _rel)) {
        _car.cambio_mod = _om;
        _car.gears_mod = _og;
        _car.relacao_final = _orr;
        global.cg_init_car(_inst, _car, _fab);
        return ""verificacao pos-gravacao falhou (rollback)"";
    }
    global.cg_init_car(_inst, _car, _fab);
    return """";
};

global.cg_resumo = function(_car) {
    var _gm = global.cg_get(_car, ""gears_mod"", -1);
    if (!is_array(_gm)) _gm = [];
    var _s = ""CG|v="" + string(global.cg_versao)
        + ""|mod="" + string(global.cg_num(global.cg_get(_car, ""cambio_mod"", 0), 0))
        + ""|rel="" + string(global.cg_rel(_car))
        + ""|n="" + string(array_length(_gm));
    for (var i = 0; i < array_length(_gm); i++) {
        _s += ""|r"" + string(i) + ""="" + string(global.cg_num(_gm[i], -1));
    }
    return _s;
};

global.cg_diag = function(_s) {
    var _f = file_text_open_append(""cambio_save.txt"");
    if (_f == -1) return 0;
    file_text_write_string(_f, _s);
    file_text_writeln(_f);
    file_text_close(_f);
    return 1;
};

global.cambio_erro_t = 0;
global.cambio_erro_txt = """";
global.cambio_erro = function(_txt) {
    global.cambio_erro_t = current_time;
    global.cambio_erro_txt = _txt;
};

global.cambio_aviso_t = 0;
global.cambio_aviso_txt = """";
global.cg_avisar = function(_txt) {
    global.cambio_aviso_t = current_time;
    global.cambio_aviso_txt = _txt;
};

// sobe ou desce a relacao final e leva as marchas junto (razao efetiva)
global.cg_escala_rel = function(_novo) {
    _novo = global.cg_num(_novo, global.cambio_rel_ref);
    var _velho = _novo;
    if (variable_global_exists(""gear_rel_aplicada"")) _velho = global.cg_num(global.gear_rel_aplicada, _novo);
    if (_velho <= 0) _velho = _novo;
    global.gear_temp_rel = _novo;
    if (variable_global_exists(""gear_temp_array"") && is_array(global.gear_temp_array) && abs(_novo - _velho) > 0.00001) {
        var _f = _novo / _velho;
        var _n = array_length(global.gear_temp_array);
        for (var i = 0; i < _n; i++) {
            var _v = global.cg_num(global.gear_temp_array[i], 0);
            if (_v > 0) global.gear_temp_array[i] = _v * _f;
        }
    }
    global.gear_rel_aplicada = _novo;
};

global.cambio_gratis = 0;
global.cambio_pode_pagar = function(_custo) {
    var _ok = (global.cash >= _custo) || (global.cambio_gratis == 1);
    global.cg_diag((_ok ? ""PODE PAGAR"" : ""SEM DINHEIRO"")
        + "" custo="" + string(floor(_custo)) + "" cash="" + string(floor(global.cash)));
    return _ok;
};

global.cg_confirmar = function(_custo) {
    var _car = global.car_data[global.vehicles_array];
    var _inst = instance_find(obj_player, 0);
    if (!instance_exists(_inst)) return ""sem player na cena"";
    var _n = _inst.max_gears;
    if (!variable_global_exists(""gear_temp_array"") || !is_array(global.gear_temp_array)) {
        return ""marchas nao carregadas no caderno"";
    }
    if (array_length(global.gear_temp_array) != _n) {
        return ""marchas nao carregadas no caderno"";
    }
    var _cand = array_create(_n);
    for (var i = 0; i < _n; i++) _cand[i] = global.cg_num(global.gear_temp_array[i], -1);
    var _rel = global.cg_rel(_car);
    if (variable_global_exists(""gear_temp_rel"")) {
        _rel = global.cg_num(global.gear_temp_rel, _rel);
    }
    var _fab = _inst.original_ratios;
    // original_ratios ja esta na relacao salva. A janela de +-70% acompanha
    // a relacao que o jogador acabou de escolher, senao o diferencial longo
    // recusa uma caixa que o slider deixou passar.
    var _salvo = global.cg_rel(_car);
    if (is_array(_fab) && _salvo > 0.01 && abs(_rel - _salvo) > 0.0001) {
        var _k = _rel / _salvo;
        var _fab2 = array_create(array_length(_fab));
        for (var _fi = 0; _fi < array_length(_fab); _fi++) _fab2[_fi] = _fab[_fi] * _k;
        _fab = _fab2;
    }

    var _erro = global.cg_validar(_car, _cand, _rel, _fab);
    if (_erro != """") {
        global.cambio_erro(_erro);
        global.cg_diag(""RECUSADO "" + _erro);
        return _erro;
    }
    var _av = global.cg_avisos(_car, _cand, _rel, _fab);
    if (_av != """") global.cg_avisar(""CAMBIO IRREAL: "" + _av);

    if (!global.cambio_pode_pagar(_custo)) {
        global.cambio_erro(""FALTA DINHEIRO: $"" + string(floor(_custo - global.cash)));
        return ""sem dinheiro"";
    }
    global.cash -= _custo;
    _erro = global.cg_aplicar(_inst, _car, _cand, _rel, _fab);
    if (_erro != """") {
        global.cash += _custo;
        global.cambio_erro(_erro);
        global.cg_diag(""FALHOU "" + _erro);
        return _erro;
    }
    SaveAllCars();
    SaveGame();
    global.cg_diag(""GRAVOU "" + global.cg_resumo(_car));
    return """";
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
        global.cambio_gratis = ini_read_real(""dev"", ""cambio_gratis"", 0);
        global.aero_cl = ini_read_real(""aero"", ""cl"", global.aero_cl);
        global.aero_ativo = ini_read_real(""aero"", ""ativo"", global.aero_ativo);
        global.aero_forca_ind = ini_read_real(""aero"", ""induzido"", global.aero_forca_ind);
        global.aero_crr = ini_read_real(""aero"", ""crr"", global.aero_crr);
        ini_close();
    }
};

// fase 2: curva de 8 pontos. mech_custom 0 continua na mistura rua/mista/pista.
global.mod_fuel_preset = function(_nome) {
    var d = global.mod_dist_cfg();
    var _rpm;
    var _deg;
    if (_nome == ""strip"") {
        _rpm = [800, 1400, 2000, 2600, 3200, 4000, 5000, 6500];
        _deg = [0, 3, 9, 15, 19, 22, 24, 24];
    } else if (_nome == ""race"") {
        _rpm = [800, 1800, 2400, 3000, 3800, 4600, 5600, 7000];
        _deg = [0, 1, 6, 12, 16, 19, 21, 22];
    } else {
        _rpm = [800, 1200, 1600, 2000, 2600, 3400, 4500, 6500];
        _deg = [0, 6, 12, 17, 21, 24, 24, 24];
    }
    d.mech_rpm = _rpm;
    d.mech_deg = _deg;
    d.mech_custom = 1;
};

global.mod_fuel_smooth = function(_u) {
    if (_u < 0) _u = 0;
    if (_u > 1) _u = 1;
    return _u * _u * (3 - (2 * _u));
};

global.mod_fuel_spring_kit = function() {
    var d = global.mod_dist_cfg();
    var _rpm = [800, 1200, 1600, 2000, 2600, 3400, 4500, 6500];
    var _deg = array_create(8);
    var _sl = d.spring_l1 - d.spring_l0;
    var _sh = d.spring_h1 - d.spring_h0;
    if (_sl < 1) _sl = 1;
    if (_sh < 1) _sh = 1;
    for (var _i = 0; _i < 8; _i++) {
        var _ul = global.mod_fuel_smooth((_rpm[_i] - d.spring_l0) / _sl);
        var _uh = global.mod_fuel_smooth((_rpm[_i] - d.spring_h0) / _sh);
        _deg[_i] = 24 * ((0.45 * _ul) + (0.55 * _uh));
    }
    d.mech_rpm = _rpm;
    d.mech_deg = _deg;
    d.mech_custom = 1;
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
    "var _pcr = global.phys_cfg(); var _base_acceleration = global.phys_base(global.torque, gear_ratios[2], _pcr.raio, global.weight * 1000, global.phys_k(global.hp, _pcr.raio, gear_ratios[0]), _pcr.eficiencia, global.phys_mu(global.car_data[global.vehicles_array].pneu, global.heat_tire), _pcr.share);",
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
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0", "    _target_speed = min(kmh + _acceleration, _gearlimit);\n    if (kmh < _gearlimit && clutch <= 0)\n    {\n        kmh = lerp(kmh, _target_speed, dt_lerp_factor(0.05));\n    }", "    _target_speed = min(kmh + _acceleration, _gearlimit);\n    if (!global.phys_v28_ai()) {\n    if (kmh < _gearlimit && clutch <= 0)\n    {\n        kmh = lerp(kmh, _target_speed, dt_lerp_factor(0.05));\n    }\n    }", false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0", "    _target_speed = min(kmh + _acceleration, _gearlimit);\n    if (kmh < _gearlimit && clutch <= 0)\n    {\n        kmh = lerp(kmh, _target_speed, dt_lerp_factor(0.05));\n    }", "    _target_speed = min(kmh + _acceleration, _gearlimit);\n    if (!global.phys_v28_ai()) {\n    if (kmh < _gearlimit && clutch <= 0)\n    {\n        kmh = lerp(kmh, _target_speed, dt_lerp_factor(0.05));\n    }\n    }", false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0", "    _target_speed = min(kmh + _acceleration, _gearlimit);\n    if (kmh < _gearlimit)\n    {\n        if (clutch <= 0)\n        {\n            kmh = lerp(kmh, _target_speed, 0.05);\n        }\n    }", "    _target_speed = min(kmh + _acceleration, _gearlimit);\n    if (!global.phys_v28_ai()) {\n    if (kmh < _gearlimit)\n    {\n        if (clutch <= 0)\n        {\n            kmh = lerp(kmh, _target_speed, 0.05);\n        }\n    }\n    }", false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0", "    _target_speed = min(kmh + (_acceleration * global.dt), _gearlimit);\n    if (kmh < _gearlimit)\n    {\n        if (clutch <= 0)\n        {\n            kmh = lerp(kmh, _target_speed, dt_lerp_factor(0.05));\n        }\n    }", "    _target_speed = min(kmh + (_acceleration * global.dt), _gearlimit);\n    if (!global.phys_v28_ai()) {\n    if (kmh < _gearlimit)\n    {\n        if (clutch <= 0)\n        {\n            kmh = lerp(kmh, _target_speed, dt_lerp_factor(0.05));\n        }\n    }\n    }", false);
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

// telemetria do cambio: marcha, giro, relacao total e topo teorico
if (instance_exists(obj_player) && global.kmh > 5) {
    var _g = global.gear;
    var _rr = global.pt_ratio(_g);
    var _topo = 0;
    if (_rr > 0) _topo = ((global.car_data[global.vehicles_array].rpm / _rr) * 60) / 1000;
    var _txt = ""G"" + string(_g) + ""/"" + string(obj_player.max_gears)
        + ""   "" + string(round(global.rpm)) + "" rpm""
        + ""   rel "" + string(round(_rr * 100) / 100)
        + ""   topo "" + string(round(_topo))
        + ""   "" + string(round(global.kmh)) + "" km/h""
        + ""   mod"" + string(global.cg_num(global.car_data[global.vehicles_array].cambio_mod, 0))
        + "" gm"" + string(array_length(global.car_data[global.vehicles_array].gears_mod));
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    draw_set_alpha(0.8);
    draw_set_color(c_white);
    draw_text_transformed(12, 12, _txt, 0.6, 0.6, 0);
    // sonda do carro: uma linha por sessao, com o que a aero usa de verdade
    if (instance_exists(obj_player) && global.cg_car_t == 0 && global.kmh > 2) {
        global.cg_car_t = 1;
        var _s_mhp = variable_global_exists(""maxhp"") ? global.maxhp : 0;
        var _s_mtq = variable_global_exists(""maxtorque"") ? global.maxtorque : 0;
        global.cg_diag(""CAR hp="" + string(round(global.hp)) + "" maxhp="" + string(round(_s_mhp))
            + "" torque="" + string(round(global.torque)) + "" maxtorque="" + string(round(_s_mtq))
            + "" peso="" + string(global.weight) + "" cx="" + string(global.cx)
            + "" area="" + string(global.area_frontal) + "" m="" + string(round(global.aero_massa()))
            + "" cd="" + string(round(global.aero_cd() * 1000) / 1000)
            + "" pacc_livre="" + string(round(global.aero_thrust_units() * 10) / 10)
            + "" v_topo="" + string(round(power((2 * global.aero_power_w()) / (global.aero_rho * global.aero_cd() * global.area_frontal), 1 / 3) * 3.6))
            + "" vmax_aero="" + string(round(global.aero_vmax_kmh())));
    }
    // sonda do topo: registra o que limita acima de 300 km/h
    if (global.kmh > 300 && current_time - global.cg_probe_t > 400) {
        global.cg_probe_t = current_time;
        global.cg_diag(""TOPO kmh="" + string(round(global.kmh))
            + "" rpm="" + string(round(global.rpm))
            + "" gear="" + string(global.gear) + ""/"" + string(obj_player.max_gears)
            + "" r="" + string(global.pt_ratio(global.gear))
            + "" rpm_roda="" + string(round(global.pt_rpm_roda()))
            + "" corte="" + string(global.pt_em_corte())
            + "" acc="" + string(round(global.cg_acc * 100) / 100)
            + "" drag="" + string(round(global.cg_drag * 1000) / 1000)
            + "" fv="" + string(global.cg_fv)
            + "" vmax="" + string(global.cambio_vmax)
            + "" torque="" + string(round(global.torque))
            + "" slip="" + string(round(global.phys_slip * 100))
            + "" fdrag="" + string(round(global.aero_f_drag()))
            + "" fdown="" + string(round(global.aero_f_down()))
            + "" froll="" + string(round(global.aero_f_roll()))
            + "" pacc="" + string(round(global.aero_thrust_units() * 100) / 100)
            + "" cd="" + string(round(global.aero_cd() * 1000) / 1000)
            + "" vmax_aero="" + string(round(global.cg_vmax_aero))
            + "" acel_livre="" + string(round(global.cg_acel_livre * 100) / 100)
            + "" at="" + string(global.car_data[global.vehicles_array].at));
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
        if (global.mod_dist_estado.knock > 0.3 || (variable_global_exists(""telemetry_knock"") && global.telemetry_knock > 0.3)) {
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
g.QueueFindReplace("gml_Object_obj_player_Create_0", "if (global.car_data[global.vehicles_array].cambio == 1 || global.car_data[global.vehicles_array].cambio == 4)\n{\n    gear_ratios = [9, 6.1, 3.7, 2.1];\n    original_ratios = [9, 6.1, 3.7, 2.1];\n}\nelse if (global.car_data[global.vehicles_array].cambio == 2 || global.car_data[global.vehicles_array].cambio == 5)\n{\n    gear_ratios = [9, 6.1, 3.7, 2.5, 1.8];\n    original_ratios = [9, 6.1, 3.7, 2.5, 1.8];\n}\nelse if (global.car_data[global.vehicles_array].cambio == 3 || global.car_data[global.vehicles_array].cambio == 6 || global.car_data[global.vehicles_array].cambio == 7)\n{\n    gear_ratios = [9.35, 6, 3.6, 2.7, 2.1, 1.7];\n    original_ratios = [9.35, 6, 3.6, 2.7, 2.1, 1.7];\n}\nif (global.car_data[global.vehicles_array].cambio_mod == 1 && array_length(global.car_data[global.vehicles_array].gears_mod) > 0)\n{\n    gear_ratios = global.car_data[global.vehicles_array].gears_mod;\n}\nmax_gears = array_length(gear_ratios);", "var _cg_fab = [9.35, 6, 3.6, 2.7, 2.1, 1.7];\nif (global.car_data[global.vehicles_array].cambio == 1 || global.car_data[global.vehicles_array].cambio == 4) _cg_fab = [9, 6.1, 3.7, 2.1];\nelse if (global.car_data[global.vehicles_array].cambio == 2 || global.car_data[global.vehicles_array].cambio == 5) _cg_fab = [9, 6.1, 3.7, 2.5, 1.8];\nelse if (global.car_data[global.vehicles_array].cambio == 8) _cg_fab = [9.35, 6.2, 4.5, 3.5, 2.75, 2.15, 1.7];\nelse if (global.car_data[global.vehicles_array].cambio == 9) _cg_fab = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];\nglobal.cg_init_car(self, global.car_data[global.vehicles_array], _cg_fab);", false);

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
g.QueueFindReplace("gml_GlobalScript_EstruturaSalvamento", "data.gears_mod = [];", "data.gears_mod = [];\ndata.relacao_final = 3.73;\ndata.forja_motor = 0;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                if (arg0 == 0)\n                {\n                    return 1;\n                }\n                else if (arg0 >= 4)\n                {\n                    return 0.2;\n                }\n                else\n                {\n                    return 0.5;\n                }", "                return global.cambio_limite_pct;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                        if (global.cash >= _custo_cambio)\n                        {\n                            global.cash -= _custo_cambio;\n                            global.car_data[global.vehicles_array].cambio_mod = 1;\n                            global.car_data[global.vehicles_array].gears_mod = [];\n                            for (var i = 0; i < obj_player.max_gears; i++)\n                            {\n                                global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i];\n                                obj_player.gear_ratios[i] = global.gear_temp_array[i];\n                            }\n                            global.tiao_confirmacao_cambio = 0;\n                            global.modificando_cambio = true;\n                            global.tempo_modificacao = 0;\n                            play_audio(snd_conserto_motor);\n                            global.gear_temp_array = -1;\n                        }\n                        else\n                        {\n                            global.tiao_confirmacao_cambio = 0;\n                        }", "                        if (global.cg_confirmar(_custo_cambio) == \"\")\n                        {\n                            global.tiao_confirmacao_cambio = 0;\n                            global.modificando_cambio = true;\n                            global.tempo_modificacao = 0;\n                            play_audio(snd_conserto_motor);\n                            global.gear_temp_array = -1;\n                        }\n                        else\n                        {\n                            global.tiao_confirmacao_cambio = 0;\n                        }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    if (global.cash >= _custo_cambio)\n                    {\n                        global.cash -= _custo_cambio;\n                        global.car_data[global.vehicles_array].cambio_mod = 1;\n                        global.car_data[global.vehicles_array].gears_mod = [];\n                        for (var i = 0; i < obj_player.max_gears; i++)\n                        {\n                            global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i];\n                            obj_player.gear_ratios[i] = global.gear_temp_array[i];\n                        }\n                        global.tiao_confirmacao_cambio = 0;\n                        global.modificando_cambio = true;\n                        global.tempo_modificacao = 0;\n                        play_audio(snd_conserto_motor);\n                        global.gear_temp_array = -1;\n                    }\n                    else\n                    {\n                        global.tiao_confirmacao_cambio = 0;\n                    }", "                    if (global.cg_confirmar(_custo_cambio) == \"\")\n                    {\n                        global.tiao_confirmacao_cambio = 0;\n                        global.modificando_cambio = true;\n                        global.tempo_modificacao = 0;\n                        play_audio(snd_conserto_motor);\n                        global.gear_temp_array = -1;\n                    }\n                    else\n                    {\n                        global.tiao_confirmacao_cambio = 0;\n                    }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                var _limite = get_limite_marcha(i, obj_player.max_gears);\n                var _valor_min = _gear_original[i] - _limite;\n                var _valor_max = _gear_original[i] + _limite;", "                var _limite = max(0.3, _gear_original[i] * get_limite_marcha(i, obj_player.max_gears));\n                var _valor_min = max(0.3, _gear_original[i] - _limite);\n                var _valor_max = _gear_original[i] + _limite;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                global.gear_selecionada = 0;\n                global.gear_editando = false;\n                global.gear_area_botoes = false;\n                global.gear_botao_selecionado = 0;\n            }", "                global.gear_selecionada = 0;\n                global.gear_editando = false;\n                global.gear_area_botoes = false;\n                global.gear_botao_selecionado = 0;\n                global.gear_temp_rel = global.cambio_rel_valor();\n                global.gear_rel_original = global.gear_temp_rel;\n                global.gear_rel_aplicada = global.gear_temp_rel;\n                global.gear_aberto = array_create(obj_player.max_gears);\n                for (var _ia = 0; _ia < obj_player.max_gears; _ia++) global.gear_aberto[_ia] = global.gear_temp_array[_ia];\n            }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "            var _custo_cambio = 775 * _marchas_modificadas;", "            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)\n            {\n                _marchas_modificadas++;\n            }\n            var _custo_cambio = global.cambio_custo * _marchas_modificadas;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears - 1);", "global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    draw_set_halign(fa_center);\n                    draw_set_color(c_yellow);\n                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n                }\n            }\n            draw_set_halign(fa_left);\n            draw_set_color(c_white);\n            draw_text(_notebook_x - 290, _notebook_y + 150, txt_custo);", "                    draw_set_halign(fa_center);\n                    draw_set_color(c_yellow);\n                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n                }\n            }\n            var _rel_idx = obj_player.max_gears;\n            var _y_rel = _start_y + (obj_player.max_gears * _spacing) + 8;\n            var _rel_min = global.cambio_cfg().rel_min;\n            var _rel_max = global.cambio_cfg().rel_max;\n            var _rel_cor = (!global.gear_area_botoes && global.gear_selecionada == _rel_idx && !global.gear_editando) ? global.red : 16777215;\n            if (global.gear_selecionada == _rel_idx && global.gear_editando) _rel_cor = global.gold;\n            draw_set_halign(fa_left);\n            draw_set_color(_rel_cor);\n            draw_set_font(NotoSans);\n            draw_text(_marcha_x, _y_rel, global.cambio_txt_rel() + \":\");\n            draw_set_color(c_gray);\n            draw_text(_notebook_x - 180, _y_rel, string_format(global.gear_rel_original, 2, 2));\n            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)\n            {\n                draw_set_color(c_yellow);\n                draw_text(_notebook_x - 130, _y_rel, \">\");\n            }\n            draw_set_color((abs(global.gear_temp_rel - global.gear_rel_original) > 0.005) ? 65280 : 16777215);\n            draw_text(_notebook_x - 100, _y_rel, string_format(global.gear_temp_rel, 2, 2));\n            var _rbar_x = _notebook_x + 50;\n            draw_set_color(c_dkgray);\n            draw_roundrect(_rbar_x, _y_rel - 6, _rbar_x + 200, _y_rel + 6, false);\n            var _rpct = (global.gear_temp_rel - _rel_min) / (_rel_max - _rel_min);\n            draw_set_color(_rel_cor);\n            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, false);\n            draw_set_color(c_black);\n            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, true);\n            var _rhover = point_in_rectangle(mouse_x, mouse_y, _rbar_x, _y_rel - 15, _rbar_x + 200, _y_rel + 15);\n            if (mouse_check_button_pressed(mb_left) && _rhover)\n            {\n                global.gear_selecionada = _rel_idx;\n                global.gear_editando = true;\n                global.gear_area_botoes = false;\n            }\n            if (global.gear_editando && global.gear_selecionada == _rel_idx && mouse_check_button(mb_left) && _rhover)\n            {\n                var _rn = _rel_min + (clamp((mouse_x - _rbar_x) / 200, 0, 1) * (_rel_max - _rel_min));\n                global.cg_escala_rel(round(_rn * 100) / 100);\n            }\n            if (global.gear_editando && global.gear_selecionada == _rel_idx)\n            {\n                if (keyboard_check_pressed(vk_left)) global.cg_escala_rel(max(global.gear_temp_rel - global.cambio_passo, _rel_min));\n                if (keyboard_check_pressed(vk_right)) global.cg_escala_rel(min(global.gear_temp_rel + global.cambio_passo, _rel_max));\n                draw_set_halign(fa_center);\n                draw_set_color(c_yellow);\n                draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n            }\n            draw_set_halign(fa_left);\n            draw_set_color(c_white);\n            draw_text(_notebook_x - 290, _notebook_y + 176, txt_custo);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "draw_text(_notebook_x + 80, _notebook_y + 150, \"$\" + string(floor(_custo_cambio)));", "draw_text(_notebook_x + 80, _notebook_y + 176, \"$\" + string(floor(_custo_cambio)));", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "draw_text_transformed(_notebook_x, _notebook_y + 110, t + string(global.car_data[global.vehicles_array].gears_mod), 1, 1, 0);", "draw_text_transformed(_notebook_x, _notebook_y + 200, t + string(global.car_data[global.vehicles_array].gears_mod) + \"  |  \" + global.cambio_txt_rel() + \": \" + string(global.car_data[global.vehicles_array].relacao_final), 1, 1, 0);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    global.gear_temp_array[i] = obj_player.original_ratios[i];", "                    global.gear_temp_array[i] = obj_player.gear_ratios[i];", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "            var _gear_original = array_create(obj_player.max_gears);\n            for (var i = 0; i < obj_player.max_gears; i++)\n            {\n                _gear_original[i] = obj_player.original_ratios[i];\n            }", "            if (!variable_global_exists(\"gear_temp_rel\")) global.gear_temp_rel = global.cambio_rel_valor();\n            if (!variable_global_exists(\"gear_rel_original\")) global.gear_rel_original = global.gear_temp_rel;\n            if (!variable_global_exists(\"gear_rel_aplicada\")) global.gear_rel_aplicada = global.gear_temp_rel;\n            var _gear_original = array_create(obj_player.max_gears);\n            var _rel_salvo = global.gear_rel_original;\n            if (!is_real(_rel_salvo) || _rel_salvo <= 0.01) _rel_salvo = global.cambio_rel_ref;\n            var _krel = global.gear_temp_rel / _rel_salvo;\n            for (var i = 0; i < obj_player.max_gears; i++)\n            {\n                _gear_original[i] = obj_player.original_ratios[i] * _krel;\n            }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "            var _marchas_modificadas = 0;\n            for (var i = 0; i < obj_player.max_gears; i++)\n            {\n                if (global.gear_temp_array[i] != _gear_original[i])\n                {\n                    _marchas_modificadas++;\n                }\n            }", "            if (!variable_global_exists(\"gear_aberto\") || !is_array(global.gear_aberto))\n            {\n                global.gear_aberto = array_create(obj_player.max_gears);\n                for (var _ia = 0; _ia < obj_player.max_gears; _ia++) global.gear_aberto[_ia] = global.gear_temp_array[_ia];\n            }\n            var _marchas_modificadas = 0;\n            var _rel_ab = global.gear_rel_original;\n            if (!is_real(_rel_ab) || _rel_ab <= 0.01) _rel_ab = global.gear_temp_rel;\n            for (var i = 0; i < obj_player.max_gears; i++)\n            {\n                var _agora = global.cg_num(global.gear_temp_array[i], 0) / max(0.01, global.gear_temp_rel);\n                var _antes = _agora;\n                if (i < array_length(global.gear_aberto)) _antes = global.cg_num(global.gear_aberto[i], 0) / max(0.01, _rel_ab);\n                if (abs(_agora - _antes) > 0.002) _marchas_modificadas++;\n            }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                if (global.gear_temp_array[i] != _gear_original[i])\n                {\n                    draw_set_color(c_yellow);\n                    draw_text(_notebook_x - 130, _y, \">\");\n                }\n                var _cor_valor = (global.gear_temp_array[i] != _gear_original[i]) ? 65280 : 16777215;", "                if (abs(global.gear_temp_array[i] - _gear_original[i]) > 0.02)\n                {\n                    draw_set_color(c_yellow);\n                    draw_text(_notebook_x - 130, _y, \">\");\n                }\n                var _cor_valor = (abs(global.gear_temp_array[i] - _gear_original[i]) > 0.02) ? 65280 : 16777215;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    global.gear_temp_array[i] = round(_new_value * 10) / 10;", "                    global.gear_temp_array[i] = clamp(round(_new_value * 10) / 10, _valor_min, _valor_max);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "        var spacing = 90;", "        var spacing = 68;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "        var tem_cambio = global.car_data[global.vehicles_array].cambio_mod == 1;\n        var total_opcoes = tem_cambio ? 4 : 3;", "        var tem_cambio = global.car_data[global.vehicles_array].cambio_mod == 1;\n        var total_opcoes = tem_cambio ? 5 : 4;\n        var forja_opcao = 1;\n        var limites_opcao = tem_cambio ? 3 : 2;\n        var cancelar_opcao = tem_cambio ? 4 : 3;\n        var txt_forja = global.forja_motor_txt();\n        var txt_confirma_custo_forja = global.forja_motor_custo_txt();", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    if (global.tiao_opcao_selecionada == 0)\n                    {\n                        global.tiao_confirmacao = 1;\n                        global.tiao_confirma_opcao = 0;\n                    }\n                    else if (global.tiao_opcao_selecionada == 1 && tem_cambio)\n                    {\n                        global.tiao_confirmacao = 2;\n                        global.tiao_confirma_opcao = 0;\n                    }\n                    else if ((global.tiao_opcao_selecionada == 2 && tem_cambio) || (global.tiao_opcao_selecionada == 1 && !tem_cambio))\n                    {\n                        global.tiao_confirmacao = 3;\n                        global.tiao_confirma_opcao = 0;\n                    }\n                    else if ((global.tiao_opcao_selecionada == 3 && tem_cambio) || (global.tiao_opcao_selecionada == 2 && !tem_cambio))\n                    {\n                        global.tiao_revisao = 0;\n                        play_audio(snd_gaveta_tiao_reverse);\n                    }", "                    if (global.tiao_opcao_selecionada == 0)\n                    {\n                        global.tiao_confirmacao = 1;\n                        global.tiao_confirma_opcao = 0;\n                    }\n                    else if (global.tiao_opcao_selecionada == forja_opcao)\n                    {\n                        if (global.forja_motor_tem() == 1)\n                        {\n                            play_audio(snd_feedback_esc);\n                        }\n                        else\n                        {\n                            global.tiao_confirmacao = 4;\n                            global.tiao_confirma_opcao = 0;\n                        }\n                    }\n                    else if (global.tiao_opcao_selecionada == 2 && tem_cambio)\n                    {\n                        global.tiao_confirmacao = 2;\n                        global.tiao_confirma_opcao = 0;\n                    }\n                    else if (global.tiao_opcao_selecionada == limites_opcao)\n                    {\n                        global.tiao_confirmacao = 3;\n                        global.tiao_confirma_opcao = 0;\n                    }\n                    else if (global.tiao_opcao_selecionada == cancelar_opcao)\n                    {\n                        global.tiao_revisao = 0;\n                        play_audio(snd_gaveta_tiao_reverse);\n                    }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                else if (global.tiao_confirmacao == 3)\n                {\n                    txt_custo = txt_confirma_custo_limites;\n                }", "                else if (global.tiao_confirmacao == 3)\n                {\n                    txt_custo = txt_confirma_custo_limites;\n                }\n                else if (global.tiao_confirmacao == 4)\n                {\n                    txt_custo = txt_confirma_custo_forja;\n                }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                        var custo_necessario = (global.tiao_confirmacao == 3) ? 3500 : 500;\n                        if (global.cash >= custo_necessario)", "                        var custo_necessario = 500;\n                        if (global.tiao_confirmacao == 3) custo_necessario = 3500;\n                        if (global.tiao_confirmacao == 4) custo_necessario = global.forja_motor_custo;\n                        if (global.cash >= custo_necessario)", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    var custo_necessario = (global.tiao_confirmacao == 3) ? 3500 : 500;\n                    if (global.cash >= custo_necessario)", "                    var custo_necessario = 500;\n                    if (global.tiao_confirmacao == 3) custo_necessario = 3500;\n                    if (global.tiao_confirmacao == 4) custo_necessario = global.forja_motor_custo;\n                    if (global.cash >= custo_necessario)", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                            else if (global.tiao_confirmacao == 3)\n                            {\n                                global.cash -= 3500;\n                                global.tiao_revisao = 4;\n                            }", "                            else if (global.tiao_confirmacao == 3)\n                            {\n                                global.cash -= 3500;\n                                global.tiao_revisao = 4;\n                            }\n                            else if (global.tiao_confirmacao == 4)\n                            {\n                                if (global.forja_motor_aplicar() == 1) global.cash -= global.forja_motor_custo;\n                            }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                        else if (global.tiao_confirmacao == 3)\n                        {\n                            global.cash -= 3500;\n                            global.tiao_revisao = 4;\n                        }", "                        else if (global.tiao_confirmacao == 3)\n                        {\n                            global.cash -= 3500;\n                            global.tiao_revisao = 4;\n                        }\n                        else if (global.tiao_confirmacao == 4)\n                        {\n                            if (global.forja_motor_aplicar() == 1) global.cash -= global.forja_motor_custo;\n                        }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                var cancelar_y = tem_cambio ? (revisar_y + (spacing * 3)) : (revisar_y + (spacing * 2));\n                cancelar_opcao = tem_cambio ? 3 : 2;", "                var cancelar_y = limites_y + spacing;\n                cancelar_opcao = tem_cambio ? 4 : 3;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                if (mouse_check_button_pressed(mb_left) && hover_revisar)\n                {\n                    global.tiao_confirmacao = 1;\n                    global.tiao_confirma_opcao = 0;\n                    play_audio(snd_touch);\n                }\n                if (tem_cambio)\n                {\n                    var cambio_y = revisar_y + spacing;\n                    var hover_cambio = point_in_rectangle(mouse_x, mouse_y, center_x - 150, cambio_y - 20, center_x + 150, cambio_y + 20);\n                    var cor_cambio = (global.tiao_opcao_selecionada == 1 || hover_cambio) ? global.red : 16777215;\n                    draw_text_outline_transformed(center_x, cambio_y, txt_cambio, 0, cor_cambio, 2, font_scale, font_scale);\n                    if (hover_cambio)\n                    {\n                        global.tiao_opcao_selecionada = 1;\n                    }\n                    if (mouse_check_button_pressed(mb_left) && hover_cambio)\n                    {\n                        global.tiao_confirmacao = 2;\n                        global.tiao_confirma_opcao = 0;\n                        play_audio(snd_touch);\n                    }\n                }\n                var limites_y = tem_cambio ? (revisar_y + (spacing * 2)) : (revisar_y + spacing);\n                limites_opcao = tem_cambio ? 2 : 1;", "                if (mouse_check_button_pressed(mb_left) && hover_revisar)\n                {\n                    global.tiao_confirmacao = 1;\n                    global.tiao_confirma_opcao = 0;\n                    play_audio(snd_touch);\n                }\n                var forja_y = revisar_y + spacing;\n                var hover_forja = point_in_rectangle(mouse_x, mouse_y, center_x - 240, forja_y - 22, center_x + 240, forja_y + 22);\n                var cor_forja = (global.tiao_opcao_selecionada == forja_opcao || hover_forja) ? global.red : 16777215;\n                if (global.forja_motor_tem() == 1) cor_forja = (global.tiao_opcao_selecionada == forja_opcao || hover_forja) ? global.gold : 65280;\n                draw_text_outline_transformed(center_x, forja_y, txt_forja, 0, cor_forja, 2, font_scale * 0.78, font_scale * 0.78);\n                if (hover_forja) global.tiao_opcao_selecionada = forja_opcao;\n                if (mouse_check_button_pressed(mb_left) && hover_forja)\n                {\n                    if (global.forja_motor_tem() == 1)\n                    {\n                        play_audio(snd_feedback_esc);\n                    }\n                    else\n                    {\n                        global.tiao_confirmacao = 4;\n                        global.tiao_confirma_opcao = 0;\n                        play_audio(snd_touch);\n                    }\n                }\n                if (tem_cambio)\n                {\n                    var cambio_y = revisar_y + (spacing * 2);\n                    var hover_cambio = point_in_rectangle(mouse_x, mouse_y, center_x - 150, cambio_y - 20, center_x + 150, cambio_y + 20);\n                    var cor_cambio = (global.tiao_opcao_selecionada == 2 || hover_cambio) ? global.red : 16777215;\n                    draw_text_outline_transformed(center_x, cambio_y, txt_cambio, 0, cor_cambio, 2, font_scale, font_scale);\n                    if (hover_cambio)\n                    {\n                        global.tiao_opcao_selecionada = 2;\n                    }\n                    if (mouse_check_button_pressed(mb_left) && hover_cambio)\n                    {\n                        global.tiao_confirmacao = 2;\n                        global.tiao_confirma_opcao = 0;\n                        play_audio(snd_touch);\n                    }\n                }\n                var limites_y = revisar_y + (spacing * (tem_cambio ? 3 : 2));\n                limites_opcao = tem_cambio ? 3 : 2;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_64", "            if (global.car_data[global.vehicles_array].carburador <= 0)\n            {\n                draw_text_transformed(783, 672, \"\" + string(_lbd), 1.1, 1.1, 0);\n            }\n            else\n            {\n                draw_set_halign(fa_center);\n                draw_text_transformed(801, 672, \"\" + string(global.afr), 0.95, 0.95, 0);\n            }", "            if (global.mod_dist_tipo(global.car_data[global.vehicles_array]) == 1)\n            {\n                draw_set_halign(fa_center);\n                draw_text_transformed(801, 672, string_format(global.ignicao, 0, 1), 0.85, 0.85, 0);\n            }\n            else\n            {\n                draw_text_transformed(783, 672, \"\" + string(_lbd), 1.1, 1.1, 0);\n            }", false);
g.QueueFindReplace("gml_Object_obj_noguiintech_Draw_0", "        if (global.car_data[global.vehicles_array].carburador <= 0)\n        {\n            draw_text_transformed(_x - (42 * _scale), _y - (210 * _scale), string(_lbd), 2.4 * _scale, 2.4 * _scale, 0);\n        }\n        else\n        {\n            draw_set_halign(fa_center);\n            draw_text_transformed(_x - (3 * _scale), _y - (210 * _scale), string(global.afr), 2.1 * _scale, 2.1 * _scale, 0);\n        }", "        if (global.mod_dist_tipo(global.car_data[global.vehicles_array]) == 1)\n        {\n            draw_set_halign(fa_center);\n            draw_text_transformed(_x - (3 * _scale), _y - (210 * _scale), string_format(global.ignicao, 0, 1), 1.8 * _scale, 1.8 * _scale, 0);\n        }\n        else\n        {\n            draw_text_transformed(_x - (42 * _scale), _y - (210 * _scale), string(_lbd), 2.4 * _scale, 2.4 * _scale, 0);\n        }", false);
g.QueueFindReplace("gml_GlobalScript_desgate_proporcional", "    return arg2 * fator;", "    var _fm = 1;\n    if (variable_global_exists(\"forja_motor_mult\")) _fm = global.forja_motor_mult();\n    return arg2 * fator * _fm;", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "        return mult * 0.35;", "        return mult * 0.35 * global.forja_motor_mult();", false);
g.QueueFindReplace("gml_Object_obj_player_Alarm_3", "            car.vcambio -= desgaste_proporcional(global.torque, global.maxtorque, multiply * 1.5);", "            car.vcambio -= desgaste_proporcional(global.torque, global.maxtorque, multiply * 1.5) / global.forja_motor_mult();", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (_gearlimit > 400)\n{\n    _gearlimit = 400;\n}\nif (global.kmh > 400)\n{\n    global.kmh = 400;\n}", "if (_gearlimit > global.cambio_vmax)\n{\n    _gearlimit = global.cambio_vmax;\n}\nif (global.kmh > global.cambio_vmax)\n{\n    global.kmh = global.cambio_vmax;\n}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.kmh = clamp(global.kmh, 0, 400);", "global.kmh = clamp(global.kmh, 0, global.cambio_vmax);", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "    else if (global.kmh >= 120 && global.kmh < 160)\n    {\n        _fator_velocidade = 1.5;\n    }\n    else if (global.kmh >= 160 && global.kmh < 200)\n    {\n        _fator_velocidade = 1.75;\n    }\n    else if (global.kmh >= 200 && global.kmh < 250)\n    {\n        _fator_velocidade = 2;\n    }\n    else if (global.kmh >= 250 && global.kmh < 300)\n    {\n        _fator_velocidade = 2.5;\n    }\n    else if (global.kmh >= 300)\n    {\n        _fator_velocidade = 3.2;\n    }", "", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_desacel_arrasto *= _mod_multiplier;", "", false);
// ===================================================================

// ---- powertrain: corte de giro corta torque, corte de troca corta torque, sem parede ----
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_target_speed = min(global.kmh + _acceleration, _gearlimit);\n        if (global.kmh < _gearlimit)", "if (variable_global_exists(\"phys_v28_on\") && global.phys_v28_on && (room == Avenida || room == Drag || room == Rua || room == Autodromo || room == Online_Rua || room == Online_Autodromo)) _acceleration = 0;\n        _acceleration *= (1 - global.pt_em_corte());\n        if (global.pt_shift_t > 0) _acceleration *= 0.15;\n        global.cg_vmax_aero = global.aero_vmax_kmh();\n        global.cg_acel_livre = _acceleration;\n        _acceleration *= global.aero_gov_mult(global.kmh);\n        global.cg_acc = _acceleration;\n        _target_speed = min(global.kmh + _acceleration, global.cg_vmax_aero);\n        if (global.kmh < global.cambio_vmax)", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "    var _forca_arrasto = 0.5 * _cx * _area_frontal * _densidade_ar * (_velocidade_ms * _velocidade_ms);\n    var _desacel_arrasto = _forca_arrasto / 25000;", "    var _desacel_arrasto = global.aero_drag_units();", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.kmh -= (_desacel_arrasto * global.dt);", "global.kmh -= (_desacel_arrasto * global.dt);\nglobal.cg_drag = _desacel_arrasto;\nglobal.cg_fv = _fator_velocidade;", false);
g.QueueFindReplace("gml_Object_obj_player_Collision_obj_ia_car_TOUGE", "clamp(global.kmh, 0, 400)", "clamp(global.kmh, 0, global.cambio_vmax)", true);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Collision_obj_player", "clamp(global.kmh, 0, 400)", "clamp(global.kmh, 0, global.cambio_vmax)", true);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Collision_obj_ia_car_NPC", "clamp(global.kmh, 0, 400)", "clamp(global.kmh, 0, global.cambio_vmax)", true);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Collision_obj_player", "global.kmh = clamp(global.kmh - (_fecha * random_range(0.5, 1)), 0, 400);", "global.kmh = clamp(global.kmh - (_fecha * random_range(0.5, 1)), 0, global.cambio_vmax);", true);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Collision_obj_player", "kmh = clamp(kmh + (_fecha * 0.3), 0, 400);", "kmh = clamp(kmh + (_fecha * 0.3), 0, global.cambio_vmax);", true);
// upshift nao recalculava _gearlimit: o rpm nao caia na troca (bug do jogo)
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (key_next && global.gear < max_gears)\n        {\n            global.gear += 1;", "if (key_next && global.gear < max_gears)\n        {\n            global.gear += 1;\n            _gearlimit = global.pt_gear_limit();", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (global.gear < max_gears)\n                {\n                    global.gear += 1;\n                    if (_gsd)", "if (global.gear < max_gears)\n                {\n                    global.gear += 1;\n                    _gearlimit = global.pt_gear_limit();\n                    if (_gsd)", false);
// limitadores artificiais que brigavam com a curva de torque
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_base_acceleration *= _power_band_mult;", "_power_band_mult = 1;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "var _speed_resistance = lerp(1, 0.5, _smooth_ratio);", "var _speed_resistance = 1;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "play_audio(snd_gearchange);", "global.pt_shift_t = global.pt_shift_time; play_audio(snd_gearchange);", true);

// fisica v2.8: o passo real substitui a aceleracao artificial do jogador
g.QueueAppend("gml_RoomCC_Start_0_Create", @"// Distribuidor + carburador, fase 1. Mesmas contas de mod/fuel_model.py.
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
    if (variable_global_exists(""fuel_sys"") && !is_undefined(global.fuel_sys)) return;
    global.fuel_sys = {
        base: 10, spring: ""street"", vac_on: 1, max_vac: 19,
        advance: 0, man_ret: 0, cent_scale: 1, curva: 1, rpm_ini: 1000, rpm_fim: 3200,
        jet: 80, turns: 2.5, pv: 6.5, enrich: 1.0,
        octane: 93, iat: 40, coolant: 90,
        mapb: 0.35, afr: 13, timing: 10, knock: 0, retard: 0,
        load: 0, mech: 0, vac: 0, mbt: 28, eff: 1,
        prev_thr: 0, injected: 0,
        mech_custom: 0,
        pump_dur: 0.35, pump_enrich: 2.0, accel_timer: 0, accel_shot: 0, accel_delta: 0,
        choke_on: 0, choke_timer: 0, idle_target: 800, thr_floor: 0,
        knock_damage: 0, knock_clean: 0, fail_timer: -1, engine_failure_pending: 0,
        iat_amb: 40, vx: 0, car_id: -1
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
    var _inj = 0;
    if (!is_undefined(_car) && global.mod_dist_tipo(_car) != 1) _inj = 1;
    global.fuel_sys.injected = _inj;
    if (_inj) {
        // injecao (Noguiintech ou bicos): o ponto e o da ECU, nao do distribuidor
        var _ign = 15;
        if (variable_global_exists(""ignicao"") && is_real(global.ignicao) && global.ignicao > 0) _ign = global.ignicao;
        global.fuel_sys.base = global.fuel_sys_clamp(_ign, 0, 55);
        global.fuel_sys.advance = 0;
        global.fuel_sys.man_ret = 0;
        global.fuel_sys.curva = 0;
        global.fuel_sys.cent_scale = 0;
        global.fuel_sys.vac_on = 0;
        global.fuel_sys.max_vac = 0;
        global.fuel_sys.rpm_ini = 1000;
        global.fuel_sys.rpm_fim = 3200;
    } else if (!is_undefined(_car) && variable_struct_exists(_car, ""dist_cfg"") && !is_undefined(_car.dist_cfg)) {
        var _d = _car.dist_cfg;
        if (!variable_struct_exists(_d, ""avanco_extra"")) _d.avanco_extra = 0;
        if (!variable_struct_exists(_d, ""retardo"")) _d.retardo = 0;
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
        if (!variable_struct_exists(_c, ""jet"")) _c.jet = 80;
        if (!variable_struct_exists(_c, ""idle_turns"")) _c.idle_turns = 2.5;
        if (!variable_struct_exists(_c, ""pv"")) _c.pv = 6.5;
        global.fuel_sys.jet = global.fuel_sys_clamp(_c.jet, 60, 100);
        global.fuel_sys.turns = global.fuel_sys_clamp(_c.idle_turns, 1, 4);
        global.fuel_sys.pv = global.fuel_sys_clamp(_c.pv, 3.5, 10.5);
        if (!variable_struct_exists(_c, ""pump_enrich"")) _c.pump_enrich = 2;
        if (!variable_struct_exists(_c, ""pump_dur"")) _c.pump_dur = 0.35;
        global.fuel_sys.pump_enrich = global.fuel_sys_clamp(_c.pump_enrich, 1, 3);
        global.fuel_sys.pump_dur = global.fuel_sys_clamp(_c.pump_dur, 0.2, 0.5);
    }
    if (!is_undefined(_car) && variable_struct_exists(_car, ""dist_cfg"") && !is_undefined(_car.dist_cfg)) {
        var _dc = _car.dist_cfg;
        global.fuel_sys.mech_custom = (variable_struct_exists(_dc, ""mech_custom"") && _dc.mech_custom) ? 1 : 0;
        if (global.fuel_sys.mech_custom && variable_struct_exists(_dc, ""mech_rpm"") && variable_struct_exists(_dc, ""mech_deg"")) {
            global.fuel_sys.mech_rpm = _dc.mech_rpm;
            global.fuel_sys.mech_deg = _dc.mech_deg;
        }
    } else {
        global.fuel_sys.mech_custom = 0;
    }
    // iat da pista e o ambiente. o valor dinamico fica no fuel_sys, nao e
    // reescrito todo passo (senao o cofre nao esquenta).
    var _amb = 40;
    if (variable_global_exists(""iat"") && is_real(global.iat)) _amb = global.iat;
    if (global.fuel_sys.car_id != global.vehicles_array) {
        global.fuel_sys.car_id = global.vehicles_array;
        global.fuel_sys.iat_amb = _amb;
        global.fuel_sys.iat = _amb;
        global.fuel_sys.accel_timer = 0;
        global.fuel_sys.choke_on = 0;
        global.fuel_sys.choke_timer = 0;
        global.fuel_sys.knock_damage = 0;
        global.fuel_sys.knock_clean = 0;
        global.fuel_sys.fail_timer = -1;
        global.fuel_sys.engine_failure_pending = 0;
        if (!is_undefined(_car) && variable_struct_exists(_car, ""fuel_phase2"") && !is_undefined(_car.fuel_phase2)) {
            var _p2 = _car.fuel_phase2;
            if (variable_struct_exists(_p2, ""knock_damage"")) global.fuel_sys.knock_damage = _p2.knock_damage;
            if (variable_struct_exists(_p2, ""iat"")) global.fuel_sys.iat = _p2.iat;
        }
    }
    if (variable_global_exists(""temp"") && is_real(global.temp)) global.fuel_sys.coolant = global.temp;
    if (variable_global_exists(""phys"") && !is_undefined(global.phys) && variable_struct_exists(global.phys, ""vx"")) global.fuel_sys.vx = global.phys.vx;
    else if (variable_global_exists(""kmh"") && is_real(global.kmh)) global.fuel_sys.vx = global.kmh / 3.6;
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
    if (f.mech_custom && is_array(f.mech_rpm) && is_array(f.mech_deg) && array_length(f.mech_rpm) >= 2) {
        return global.fuel_sys_lookup(_rpm, f.mech_rpm, f.mech_deg) * f.cent_scale;
    }
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
    // IAT dinamico antes do knock: cofre quente e WOT esquentam o ar
    var _cn = global.fuel_sys_clamp((f.coolant - 80) / 40, 0, 1);
    var _spd = global.fuel_sys_clamp(f.vx / 20, 0.2, 1);
    var _diat = (6 * _cn * _thr) - (0.08 * (f.iat - f.iat_amb) * _spd);
    f.iat = global.fuel_sys_clamp(f.iat + (_diat * _dt), f.iat_amb, f.iat_amb + 60);
    var _tps = 0;
    if (_dt > 0.000001) _tps = (_thr - f.prev_thr) / _dt;
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
        if (!is_undefined(_car) && variable_struct_exists(_car, ""rpm"")) _rpm_max = _car.rpm;
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
        // bomba de aceleracao: tip-in enriquece e o extra decai
        if (_tps > 2 && _thr > 0.1) {
            f.accel_timer = f.pump_dur;
            f.accel_shot = -f.pump_enrich * max(0, _thr - f.prev_thr);
        }
        var _ad = 0;
        if (f.accel_timer > 0 && f.pump_dur > 0) {
            _ad = f.accel_shot * (f.accel_timer / f.pump_dur);
            f.accel_timer = max(0, f.accel_timer - _dt);
        }
        f.accel_delta = _ad;
        _afr += _ad;
        // afogador: frio e motor parado ou lenta baixa
        var _engon = (variable_global_exists(""engine"") && global.engine == 1) ? 1 : 0;
        var _cold = (f.coolant < 40) && ((_engon == 0) || (_rpm < 720));
        if (!f.choke_on && _cold) f.choke_on = 1;
        if (f.choke_on) {
            f.choke_timer += _dt;
            if (f.coolant > 60 && f.choke_timer > 15) f.choke_on = 0;
        }
        if (f.choke_on) {
            _afr -= 2;
            f.idle_target = 1100;
            f.thr_floor = 0.12;
        } else {
            f.idle_target = 800;
            f.thr_floor = 0;
        }
        f.afr = _afr;
    }
    if (f.injected) {
        f.accel_delta = 0;
        f.choke_on = 0;
        f.idle_target = 800;
        f.thr_floor = 0;
    }
    f.mbt = global.fuel_sys_mbt(_rpm, f.mapb);
    var _dtm = (f.timing - f.mbt) / 10;
    var _et = max(0.40, 1 - 0.15 * _dtm * _dtm);
    var _da = (f.afr - 12.8) / 2;
    var _ea = max(0.30, 1 - 0.10 * _da * _da);
    f.eff = max(0.25, _et * _ea * (1 - 0.4 * f.knock));
    f.eff = max(0.05, f.eff * (1 - f.knock_damage * 0.3));
    if (f.knock > 0.7) {
        f.knock_damage = min(1, f.knock_damage + (0.02 * f.knock * _dt));
        f.knock_clean = 0;
    } else if (f.knock <= 0) {
        f.knock_clean += _dt;
        if (f.knock_clean > 60) f.knock_damage = max(0, f.knock_damage - (0.001 * _dt));
    }
    if (f.knock_damage > 0.9) {
        if (f.fail_timer < 0) f.fail_timer = 30;
        else {
            f.fail_timer -= _dt;
            if (f.fail_timer <= 0) f.engine_failure_pending = 1;
        }
    } else if (!f.engine_failure_pending) {
        f.fail_timer = -1;
    }
    if (f.engine_failure_pending && variable_global_exists(""engine"")) global.engine = 0;
    f.prev_thr = _thr;
    var _car2 = global.mod_dist_car();
    if (!is_undefined(_car2)) {
        if (!variable_struct_exists(_car2, ""fuel_phase2"") || is_undefined(_car2.fuel_phase2)) _car2.fuel_phase2 = {};
        _car2.fuel_phase2.knock_damage = f.knock_damage;
        _car2.fuel_phase2.iat = f.iat;
    }

    global.afr = f.afr;
    global.ignicao = f.timing;
    global.telemetry_afr = f.afr;
    global.telemetry_timing = f.timing;
    global.telemetry_knock = f.knock;
    global.telemetry_MAP = f.mapb;
    global.telemetry_octane_effective = f.octane - max(0, f.iat - 40) * 0.5 - max(0, f.coolant - 100) * 0.25;
    global.telemetry_accel_pump = (f.accel_timer > 0) ? 1 : 0;
    global.telemetry_accel_afr_delta = f.accel_delta;
    global.telemetry_choke_active = f.choke_on;
    global.telemetry_knock_damage = f.knock_damage;
    global.telemetry_iat = f.iat;
    global.telemetry_idle_target = f.idle_target;
    global.telemetry_engine_failure = f.engine_failure_pending;
    if (variable_global_exists(""mod_dist_estado"")) {
        global.mod_dist_estado.knock = f.knock;
        global.mod_dist_estado.avanco = f.timing;
        global.mod_dist_estado.map = f.mapb;
    }
    return f.eff;
};

// combustivel do jogo pode nao existir ainda
global.fuel_sys_field_or_zero = function() {
    // global.fuel do jogo e o TIPO de combustivel (1 gasolina, 2 etanol...), nao o modelo
    if (variable_global_exists(""fuel"") && is_real(global.fuel)) return global.fuel;
    return 2;
};
");
g.QueueAppend("gml_RoomCC_Start_0_Create", @"// phys_step v2.8 — mesmo modelo de mod/phys_v28.py (spec MCML v2.8).
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
        mu_base: _mu, mu_static: _mu_s, load_sens: (_name == ""rear"") ? 0.10 : 0.12,
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
    var _tr = 2;
    var _pneu_ai = -1;
    var _car_id = global.vehicles_array;
    if (variable_global_exists(""phys_ai_spec"") && !is_undefined(global.phys_ai_spec)) {
        _m = global.phys_ai_spec.mass;
        _tr = global.phys_ai_spec.tr;
        _pneu_ai = global.phys_ai_spec.pneu;
        _car_id = -1000;
    }
    if (is_undefined(_m) || _m <= 0) _m = 1.4;
    if (_m < 50) _m *= 1000;
    if (_m < 400) _m = 1400;
    if (_pneu_ai < 0 && variable_global_exists(""car_data"") && variable_global_exists(""vehicles_array"")) {
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
    if (variable_global_exists(""phys_def"")) _r = global.phys_def.raio;
    var _g = 9.81;
    var _ff = _ms * _g * _b / _L;
    var _fr = _ms * _g * _a / _L;
    var _kf = _ff / 0.08;
    var _kr = _fr / 0.08;
    var _cf = 0.4 * 2 * sqrt(_kf * _ms * _b / _L);
    var _cr = 0.4 * 2 * sqrt(_kr * _ms * _a / _L);
    var _pneu = 0;
    if (_pneu_ai >= 0) _pneu = _pneu_ai;
    else if (variable_global_exists(""car_data"")) _pneu = global.car_data[global.vehicles_array].pneu;
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
    var _front = global.phys_v28_axle(""front"", _drv_f, _r, _mu_f, _ms_f, 32, _uf, _kf, _cf, 3600);
    var _rear = global.phys_v28_axle(""rear"", _drv_r, _r, _mu_r, _ms_r, _pk_r, _ur, _kr, _cr, 2400);
    _rear.psi = _pk_r;
    var _hbar = _h - 0.05;
    global.phys = {
        held: 1, car: _car_id, mass: _m, sprung: _ms,
        ix: _ms * (_L * _L + 0.25) / 12, h: _h, a: _a, b: _b, L: _L,
        has: 0, seng: (_tr == 1) ? -1 : 0, haero: _h, xcp: _a,
        cda: 1.0, cla: 0,
        z: _h, theta: 0, vz: 0, wp: 0, vx: 0, ax: 0, capotou: 0,
        front: _front, rear: _rear,
        bar_on: 1, d_bar: 0.35, h_bar: _hbar, k_bar: 300000, c_bar: 45000,
        bar_g: 0, f_bar: 0, pen: 0,
        omega: 83.8, i_eng: 0.28, eng: 0, cap: 800, gear: 1,
        ratios: [8], eff: 0.90, phase: ""none"", stimer: 0,
        t_open: 0.08, t_swap: 0.04, t_close: 0.12,
        mode: ""open"", w_stall: 36.7, stalled: 0, t_start: 80,
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
    if (_a.name == ""front"" && p.lock && !_a.driven) _ped = max(_ped, 1);
    if (_a.name == ""rear"" && p.lock && !_a.driven) _ped = max(_ped, 1);
    if (_a.name == ""rear"" && p.hb) _ped = max(_ped, 1);
    return _ped * _a.t_brake_max;
};

global.phys_v28_sub = function(_dt) {
    var p = global.phys;
    p.omega_antes = p.omega;
    var _f = p.front;
    var _r = p.rear;
    _f.psi = p.psi_f;
    _r.psi = p.psi_r;
    _f.projected = 0;
    _r.projected = 0;

    // 2 troca
    var _pedal_tgt = clamp(1 - p.pedal, 0, 1);
    var _thr = clamp(p.thr_in, 0, 1);
    if (p.shift_req && p.phase == ""none"" && p.gear != p.gear_target && p.gear_target >= 1) {
        if (p.gear_target <= array_length(p.ratios)) {
            p.phase = ""opening"";
            p.stimer = 0;
            p.eng0 = p.eng;
        }
    }
    if (p.phase == ""opening"") {
        p.stimer += _dt;
        var _u = clamp(p.stimer / max(p.t_open, 0.0001), 0, 1);
        p.eng = p.eng0 * (1 - _u);
        _thr = 0;
        if (_u >= 1) { p.phase = ""swapping""; p.stimer = 0; }
    } else if (p.phase == ""swapping"") {
        p.stimer += _dt;
        p.gear = p.gear_target;
        p.eng = 0;
        _thr = 0;
        if (p.stimer >= p.t_swap) { p.phase = ""closing""; p.stimer = 0; p.eng0 = 0; }
    } else if (p.phase == ""closing"") {
        p.stimer += _dt;
        var _u2 = clamp(p.stimer / max(p.t_close, 0.0001), 0, 1);
        p.eng = lerp(0, _pedal_tgt, _u2);
        var _gg = global.phys_v28_ratio();
        if (_gg > 0) {
            var _wd = _r.driven ? _r.omega : _f.omega;
            _thr = clamp(p.k_rev * (_wd * _gg - p.omega), 0, 1);
        }
        if (_u2 >= 1) { p.phase = ""none""; p.stimer = 0; p.eng = _pedal_tgt; }
    } else {
        p.eng = _pedal_tgt;
    }
    if (p.phase == ""none"" && (p.eng < 0.05 || p.gear < 1)) {
        var _idle = clamp(p.k_idle * (p.w_idle - p.omega), 0, 0.15);
        _thr = max(_thr, _idle);
    }
    p.thr = _thr;

    // 3 torque
    if (!p.stalled && p.mode != ""open"" && p.omega <= p.w_stall) p.stalled = 1;
    var _tcomb = 0;
    if (!p.stalled) {
        _tcomb = global.torque * _thr;
        if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on && !(variable_global_exists(""phys_ai_run"") && global.phys_ai_run)) {
            global.fuel_sys_step(p.omega * 9.5492965855, _thr, _dt);
            _tcomb = global.torque * _thr * global.fuel_sys.eff;
            if (global.fuel_sys.engine_failure_pending) {
                p.stalled = 1;
                _tcomb = 0;
            } else if (global.fuel_sys.choke_on) {
                p.w_idle = 115.2;
            } else {
                p.w_idle = 83.8;
            }
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
        p.mode = ""open"";
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
        // pe fora, a embreagem fica travada na marcha. tem de cobrir o motor
        // e a carga refletida: numa relacao longa a carga passa da capacidade,
        // o regime vira slip com delta ~ 0 e o carro trava com o giro colado.
        if (p.eng >= 0.9) _cap = max(_cap, abs(p.teng) * 1.2, abs(p.tload) * 1.2);
        var _slip = p.slip_next || abs(_delta) > p.eps_c || abs(p.tload) > _cap;
        p.slip_next = 0;
        if (_slip) {
            p.mode = ""slip"";
            p.tcl = _cap * global.phys_v28_sgn(_delta);
            if (abs(_delta) <= 0.000001 && abs(p.tload) > _cap) p.tcl = _cap * global.phys_v28_sgn(p.tload);
        } else {
            p.mode = ""stick"";
        }
    }

    // 5 motor
    if (p.mode == ""open"" || _g <= 0 || _nd == 0) {
        p.mode = ""open"";
        p.tcl = 0;
        p.omega = max(0, p.omega + (p.teng / _ie) * _dt);
    } else if (p.mode == ""slip"") {
        var _axr = _r.driven ? _r : _f;
        var _iw = global.phys_v28_iw(_axr);
        var _k = _g * p.eff / _nd;
        var _del = p.omega - _axr.omega * _g;
        var _ext = _axr.fx * _axr.radius + _axr.t_brake;
        var _coeff = _dt * (1 / _ie + (_g * _k) / _iw);
        var _rhs = _del + p.teng * _dt / _ie + _g * _ext * _dt / _iw;
        var _tsync = (_coeff > 0.0000001) ? (_rhs / _coeff) : 0;
        // delta ~ 0: o torque ja foi definido pela carga. zerar apagava a forca.
        if (abs(_del) >= 0.000001 && global.phys_v28_sgn(_tsync) == global.phys_v28_sgn(_del) && abs(_tsync) <= abs(p.tcl)) p.tcl = _tsync;
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
        if (p.eng >= 0.9) _cap2 = max(_cap2, abs(p.teng) * 1.2, abs(p.tload) * 1.2);
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
    if (p.n_driven > 0 && _g > 0 && p.mode != ""open"") {
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
        // pe fora: o giro nao ultrapassa a marcha, e o corte e o topo dela.
        // so corta o que SUBIU neste passo, para um dump ja alto nao cair na lenta.
        if (p.eng >= 0.9 && !p.stalled && p.mode != ""open"" && _g > 0 && p.brake < 0.2 && !p.hb && !p.lock) {
            var _axl = _r.driven ? _r : _f;
            var _vx_top = (p.red / _g) * _axl.radius;
            if (p.vx > _vx_top) p.vx = _vx_top;
            var _alvo = (abs(p.vx) / _axl.radius) * _g;
            var _teto = max(p.w_idle, _alvo);
            var _antes = variable_struct_exists(p, ""omega_antes"") ? p.omega_antes : p.omega;
            // dump ja alto: nao puxa para a lenta. 500 rpm ~ 52 rad/s.
            if (_antes > _teto + 52) {
            } else {
                if (p.omega < p.w_idle) p.omega = p.w_idle;
                else if (_alvo > p.w_idle) {
                    p.omega = _alvo;
                    _axl.omega = abs(p.vx) / _axl.radius;
                }
            }
        }
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
    var _fz0 = p.sprung * p.g * ((_a.name == ""front"") ? p.b : p.a) / p.L + _a.mass_u * p.g;
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
    if (_a.driven && p.mode == ""stick"") {
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
    if (!variable_global_exists(""phys"") || is_undefined(global.phys)) global.phys_v28_reset();
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
    if (variable_struct_exists(p.rear, ""radius"")) _rr = p.rear.radius;
    var _ngear = array_length(obj_player.gear_ratios);
    var _rats = array_create(_ngear);
    for (var _i = 0; _i < _ngear; _i++) _rats[_i] = obj_player.gear_ratios[_i] * _rr * 6.28318530718;
    p.ratios = _rats;
    var _redrpm = global.car_data[global.vehicles_array].rpm;
    if (_redrpm < 1000) _redrpm = 6500;
    p.red = _redrpm * 0.104719755;
    var _cap = 800;
    if (variable_global_exists(""maxtorque"") && global.maxtorque > 100) _cap = global.maxtorque * 1.15;
    p.cap = _cap;
    // aero do proprio carro. ClA < 0 = downforce.
    var _area = 2.2;
    if (variable_global_exists(""area_frontal"") && global.area_frontal > 0.2) _area = global.area_frontal;
    var _cd = 0.40;
    if (variable_global_exists(""aero_cd"")) _cd = global.aero_cd();
    p.cda = max(0.3, _cd * _area);
    var _cl = 0;
    if (variable_global_exists(""aero_ativo"") && global.aero_ativo == 1 && variable_global_exists(""aero_cl"")) _cl = global.aero_cl;
    p.cla = -abs(_cl) * _area;
    p.xcp = p.a;

    if (p.held) {
        p.vx = max(0, global.phys_kmh_used) / 3.6;
        if (p.rear.stick) p.rear.omega = p.vx / p.rear.radius;
        if (p.front.stick) p.front.omega = p.vx / p.front.radius;
        if (global.rpm > 500) {
            var _wgame = global.rpm * 0.104719755;
            if (_wgame > p.omega) p.omega = _wgame;
        }
        p.held = 0;
    }

    p.thr_in = global.phys_in_up;
    // a embreagem do jogo fica aberta enquanto clutch > 0 (o timer da troca).
    p.pedal = (global.phys_in_clutch || (global.phys_in_clutch_t > 0)) ? 1 : 0;
    p.brake = global.phys_in_down;
    p.hb = (global.phys_in_brake || (variable_global_exists(""handbrake"") && global.handbrake)) ? 1 : 0;
    p.lock = (global.phys_in_borra == 1) ? 1 : 0;
    p.ign = (global.engine == 1) ? 1 : 0;
    // spawn / ligar o motor: o jogo ja considera o motor aceso, mas o stall
    // ficava preso e so soltava quando a embreagem abria numa troca de marcha.
    if (!variable_struct_exists(p, ""engine_was"")) p.engine_was = 0;
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
    p.phase = ""none"";
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
        if (variable_global_exists(""angle_frente"")) global.angle_frente -= _dk * global.dt;
        var _h = global.kmh / 2.7;
        layer_hspeed(""Rua"", (-_h / 1.5) * global.dt);
        layer_hspeed(""Ceu"", (-_h / 2) * global.dt);
        layer_hspeed(""Ceu2"", (-_h / 2) * global.dt);
        layer_hspeed(""Arvores"", (-_h / 3) * global.dt);
        layer_hspeed(""ArvoresF"", (-_h / 2) * global.dt);
        layer_hspeed(""ArvoresF_1"", (-_h / 2) * global.dt);
    } else if (global.phys_hit) {
        p.vx = 0;
        global.kmh = 0;
    } else {
        var _dx = (_dk / 2.7) * global.dt;
        if (!place_meeting(x + _dx, y, obj_colision)) {
            x += _dx;
            global.angle -= _dk * global.dt;
            if (variable_instance_exists(id, ""angle_frente"") || variable_global_exists(""angle_frente"")) global.angle_frente -= _dk * global.dt;
        } else {
            p.vx = 0;
            global.kmh = 0;
        }
    }
    global.phys_moved = 0;
    global.phys_hit = 0;
    p.held = 0;
};

// NPC: o mesmo integrador do jogador, com o estado guardado na instancia.
// Nao mexe no global.phys do player e nao roda o carburador/distribuidor dele.
global.phys_v28_ai = function() {
    if (!variable_global_exists(""phys_v28_on"") || !global.phys_v28_on) return 0;
    if (!variable_instance_exists(id, ""gear_ratios"") || !is_array(gear_ratios)) return 0;
    if (!variable_instance_exists(id, ""kmh"") || !is_real(kmh)) return 0;
    var _bak_p = global.phys;
    var _bak_tq = variable_global_exists(""torque"") ? global.torque : 0;
    var _had_mt = variable_global_exists(""maxtorque"");
    var _bak_mt = _had_mt ? global.maxtorque : 0;
    global.phys_ai_run = 1;
    var _mass = weight;
    if (!is_real(_mass) || _mass <= 0) _mass = 1400;
    var _tr = 2;
    if (variable_instance_exists(id, ""drive"") && is_real(drive)) _tr = drive;
    var _pn = 0;
    if (variable_instance_exists(id, ""pneu"") && is_real(pneu)) _pn = pneu;
    if (!variable_instance_exists(id, ""ai_phys"") || is_undefined(ai_phys)) {
        global.phys_ai_spec = { mass: _mass, tr: _tr, pneu: _pn };
        global.phys_v28_reset();
        ai_phys = global.phys;
        global.phys_ai_spec = undefined;
    }
    global.phys = ai_phys;
    var p = global.phys;
    var _rr = p.rear.radius;
    var _ngear = array_length(gear_ratios);
    var _rats = array_create(_ngear);
    for (var _i = 0; _i < _ngear; _i++) _rats[_i] = gear_ratios[_i] * _rr * 6.28318530718;
    p.ratios = _rats;
    var _redrpm = 6500;
    if (variable_instance_exists(id, ""max_rpm"") && max_rpm > 1000) _redrpm = max_rpm;
    p.red = _redrpm * 0.104719755;
    var _cap = 800;
    if (variable_instance_exists(id, ""maxtorque"") && maxtorque > 100) _cap = maxtorque * 1.15;
    p.cap = _cap;
    p.cda = 1.30;
    p.cla = 0;
    var _vk = max(0, kmh) / 3.6;
    if (abs(_vk - p.vx) > 0.8) {
        p.vx = _vk;
        if (p.rear.stick) p.rear.omega = p.vx / p.rear.radius;
        if (p.front.stick) p.front.omega = p.vx / p.front.radius;
    }
    var _eng = 0;
    if (variable_instance_exists(id, ""engine"") && engine == 1) _eng = 1;
    p.thr_in = _eng;
    p.pedal = (variable_instance_exists(id, ""clutch"") && clutch > 0) ? 1 : 0;
    p.brake = 0;
    p.hb = 0;
    p.lock = 0;
    p.ign = _eng;
    if (!variable_struct_exists(p, ""engine_was"")) p.engine_was = 0;
    if (_eng == 1 && p.engine_was == 0) {
        p.stalled = 0;
        if (p.omega < p.w_idle) p.omega = p.w_idle;
    }
    p.engine_was = _eng;
    if (_eng == 0) p.stalled = 1;
    var _gear = 0;
    if (variable_instance_exists(id, ""gear"") && is_real(gear)) _gear = gear;
    p.gear = _gear;
    p.gear_target = _gear;
    p.phase = ""none"";
    p.shift_req = 0;
    global.torque = 0;
    if (variable_instance_exists(id, ""torque"") && is_real(torque)) global.torque = torque;
    if (variable_instance_exists(id, ""maxtorque"") && is_real(maxtorque)) global.maxtorque = maxtorque;
    var _dt = global.dt / 60;
    if (_dt <= 0) _dt = 1 / 60;
    if (_dt > 0.05) _dt = 0.05;
    var _h = _dt / 4;
    for (var _s = 0; _s < 4; _s++) global.phys_v28_sub(_h);
    if (p.vx < 0) p.vx = 0;
    if (p.vx > 150) p.vx = 150;
    kmh = p.vx * 3.6;
    rpm = p.omega * 9.5492965855;
    if (rpm < 0) rpm = 0;
    global.phys = _bak_p;
    global.torque = _bak_tq;
    if (_had_mt) global.maxtorque = _bak_mt;
    global.phys_ai_run = 0;
    global.phys_ai_spec = undefined;
    return 1;
};
");
g.QueueAppend("gml_Object_obj_player_Step_0", @"
// o objeto do menu so era criado na Garage. na rua o clique direito nao tinha alvo.
if (variable_global_exists(""car_data"") && is_array(global.car_data) && global.vehicles_array >= 0 && global.vehicles_array < array_length(global.car_data)) {
    var _ntspawn = global.car_data[global.vehicles_array];
    if (variable_struct_exists(_ntspawn, ""noguiintech"") && is_real(_ntspawn.noguiintech) && _ntspawn.noguiintech != 0 && variable_struct_exists(_ntspawn, ""vignicao"") && !instance_exists(obj_noguiintech)) {
        instance_create_layer(0, 0, ""System"", obj_noguiintech);
    }
}
if (variable_global_exists(""phys_v28_on"") && global.phys_v28_on) {
    if (variable_global_exists(""phys_moved"") && global.phys_moved) {
        // a funcao nasceu na room Start e nao enxerga as variaveis do player.
        // copia aqui, no Step, e amarra self no obj_player.
        global.phys_in_up = key_up ? 1 : 0;
        global.phys_in_down = key_down ? 1 : 0;
        global.phys_in_clutch = key_clutch ? 1 : 0;
        global.phys_in_brake = key_brake ? 1 : 0;
        global.phys_in_clutch_t = clutch;
        global.phys_in_borra = _borrachao;
        var _passo = method(id, global.phys_v28_frame);
        _passo();
    } else if (variable_global_exists(""phys"") && !is_undefined(global.phys)) global.phys.held = 1;
}
// carburado fora da fisica: a ECU de injecao tinha escrito o mapa no campo
// de ignicao, e o wideband mostrava AFR. O ponto do distribuidor entra aqui,
// depois desse mapa. Na sala com fisica o phys_step ja gravou o ponto.
if (variable_global_exists(""fuel_model_on"") && global.fuel_model_on) {
    var _nt_rodou = (variable_global_exists(""phys_v28_on"") && global.phys_v28_on && variable_global_exists(""phys_moved"") && global.phys_moved);
    if (!_nt_rodou && variable_global_exists(""car_data"") && is_array(global.car_data)) {
        var _ntc = global.car_data[global.vehicles_array];
        if (global.mod_dist_tipo(_ntc) == 1) {
            var _nt_thr = 0;
            if (variable_instance_exists(id, ""key_up"") && key_up) _nt_thr = 1;
            global.fuel_sys_step(global.rpm, _nt_thr, global.dt / 60);
        }
    }
}
");
g.QueueFindReplace("gml_Object_obj_player_Step_0", "    if (global.rpm > 0)\n    {\n        var half_rpm = global.car_data[global.vehicles_array].rpm / 2;", "    if (variable_global_exists(\"phys_v28_on\") && global.phys_v28_on && variable_global_exists(\"phys\") && !is_undefined(global.phys)) global.rpm = global.phys.omega * 9.5492965855;\n    if (global.rpm > 0)\n    {\n        var half_rpm = global.car_data[global.vehicles_array].rpm / 2;", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (room == Drag || room == Rua || room == Autodromo || room == Online_Rua || room == Online_Autodromo)\n{\n    if (launch_activ == 0)\n    {\n        move = key_down - key_up;\n        hspd = global.kmh / 2.7;", "if (room == Drag || room == Rua || room == Autodromo || room == Online_Rua || room == Online_Autodromo)\n{\n    if (launch_activ == 0)\n    {\n        move = key_down - key_up;\n        global.phys_kmh_used = global.kmh;\n        global.phys_moved = 1;\n        global.phys_scroll = 0;\n        hspd = global.kmh / 2.7;", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (room == Avenida)\n{\n    move = key_down - key_up;\n    hspd = global.kmh / 2.7;", "if (room == Avenida)\n{\n    move = key_down - key_up;\n    global.phys_kmh_used = global.kmh;\n    global.phys_moved = 1;\n    global.phys_scroll = 1;\n    hspd = global.kmh / 2.7;", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "            hspd = 0;\n            global.kmh = 0;\n        }\n        if (room == Autodromo && global.kmh > 100)", "            hspd = 0;\n            global.kmh = 0;\n            global.phys_hit = 1;\n        }\n        if (room == Autodromo && global.kmh > 100)", false);
// ECU de injecao nao pode reescrever giro nem torque de carro carburado:
// o ponto e do distribuidor, e esse desconto deixava o giro cravado em 800
// com a roda ja andando.
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.torque *= (1 + bonus_ignicao_torque);",
    "if (!(variable_global_exists(\"fuel_model_on\") && global.fuel_model_on && global.mod_dist_tipo(global.car_data[global.vehicles_array]) == 1)) global.torque *= (1 + bonus_ignicao_torque);",
    false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.rpm = min(global.rpm * power(mult_rpm, global.dt), global.car_data[global.vehicles_array].rpm);",
    "if (!(variable_global_exists(\"fuel_model_on\") && global.fuel_model_on && global.mod_dist_tipo(global.car_data[global.vehicles_array]) == 1)) global.rpm = min(global.rpm * power(mult_rpm, global.dt), global.car_data[global.vehicles_array].rpm);",
    false);

g.Import();
ScriptMessage("MARK distribuidor=v2 instalado");
