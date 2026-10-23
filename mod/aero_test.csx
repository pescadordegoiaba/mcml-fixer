using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""aet"")) {
    global.aet = 1;
    global.aet_p = 0;
    global.aet_f = 0;
    var _chk = function(_nome, _ok, _exp, _veio) {
        var _h = file_text_open_append(""aero_test.txt"");
        if (_ok) { file_text_write_string(_h, ""PASS "" + _nome); global.aet_p += 1; }
        else { file_text_write_string(_h, ""FAIL "" + _nome + "" esperado "" + string(_exp) + "" veio "" + string(_veio)); global.aet_f += 1; }
        file_text_writeln(_h);
        file_text_close(_h);
    };

    // guarda estado (cx/area/weight/hp ainda nao existem neste ponto do boot)
    var _kmh_old = global.kmh;
    var _cx_old = variable_global_exists(""cx"") ? global.cx : 0.32;
    var _ar_old = variable_global_exists(""area_frontal"") ? global.area_frontal : 2.0;
    var _w_old = variable_global_exists(""weight"") ? global.weight : 1.3;
    var _hp_old = variable_global_exists(""hp"") ? global.hp : 100;
    var _mhp_old = variable_global_exists(""maxhp"") ? global.maxhp : 100;
    var _cl_old = global.aero_cl;
    var _ind_old = global.aero_forca_ind; var _crr_old = global.aero_crr;

    // carro de referencia: Cd 0.32, A 2.0, 1300 kg
    global.cx = 0.32; global.area_frontal = 2.0; global.weight = 1.3; global.hp = 600;
    global.maxhp = 600;
    global.aero_cl = 0; global.aero_forca_ind = 0; global.aero_crr = 0;

    // 1 e 2: arrasto cresce com v^2
    global.kmh = 100;
    _chk(""fdrag_100"", abs(global.aero_f_drag() - 302.5) < 2, 302.5, global.aero_f_drag());
    global.kmh = 200;
    _chk(""fdrag_200"", abs(global.aero_f_drag() - 1210) < 8, 1210, global.aero_f_drag());

    // 3: downforce a 300 km/h (Cl 0.45)
    global.aero_cl = 0.45;
    global.kmh = 300;
    _chk(""fdown_300"", abs(global.aero_f_down() - 3828) < 40, 3828, global.aero_f_down());

    // 4: rolamento = Crr * (m*g + downforce)
    global.aero_crr = 0.012;
    _chk(""froll"", abs(global.aero_f_roll() - 199) < 4, 199, global.aero_f_roll());

    // 5: Cd efetivo com arrasto induzido Cl^2/(pi*e*AR)
    global.aero_forca_ind = 1;
    _chk(""cd_induzido"", abs(global.aero_cd() - 0.350) < 0.002, 0.350, global.aero_cd());

    // 6: empuxo limitado pela potencia a 400 km/h (600 hp, 1300 kg -> 3.1 m/s2 -> 3.72 un)
    global.kmh = 400;
    _chk(""thrust_400"", abs(global.aero_thrust_units() - 3.72) < 0.25, 3.72, global.aero_thrust_units());

    // 7: topo analitico 0.5*rho*CdA*v^3 = P  (CdA 0.64, 600 hp)
    var _p = 600 * 745.7;
    var _cda = 0.32 * 2.0;
    var _v_an = power((2 * _p) / (1.225 * _cda), 1 / 3);
    _chk(""topo_analitico"", abs((_v_an * 3.6) - 376) < 8, 376, _v_an * 3.6);

    // 8: topo simulado pelas duas escalas do jogo (sem induzido/rolamento) tem que bater
    global.aero_forca_ind = 0; global.aero_crr = 0;
    var _topo = 0;
    for (var _v = 1; _v < 250; _v += 0.5) {
        global.kmh = _v * 3.6;
        if ((0.05 * min(21, global.aero_thrust_units())) <= global.aero_drag_units()) break;
        _topo = _v;
    }
    _chk(""topo_simulado"", abs((_topo * 3.6) - (_v_an * 3.6)) < 6, _v_an * 3.6, _topo * 3.6);

    // 9: com arrasto induzido e rolamento o topo cai
    global.aero_forca_ind = 1; global.aero_crr = 0.012;
    var _topo2 = 0;
    for (var _v = 1; _v < 250; _v += 0.5) {
        global.kmh = _v * 3.6;
        if ((0.05 * min(21, global.aero_thrust_units())) <= global.aero_drag_units()) break;
        _topo2 = _v;
    }
    _chk(""topo_com_perdas"", (_topo2 * 3.6) < (_topo * 3.6) - 5 && (_topo2 * 3.6) > 330, ""<"" + string(_topo * 3.6), _topo2 * 3.6);

    // 9b: separacao motor/aero - a potencia define o TOPO, nao a aceleracao
    // 570 hp, 1300 kg, Cd 0.35, A 2.0, Cl 0.45, Crr 0.012 -> P = b*v^3 + c*v
    global.maxhp = 570; global.hp = 570;
    global.cx = 0.35; global.area_frontal = 2.0; global.weight = 1.3;
    global.aero_cl = 0.45; global.aero_forca_ind = 1; global.aero_crr = 0.012;
    var _vm = global.aero_vmax_kmh();
    _chk(""vmax_570hp"", abs(_vm - 343) < 6, 343, _vm);

    // 9c: governador - abaixo do joelho o empuxo nao e tocado
    _chk(""gov_livre"", global.aero_gov_mult(0.5 * _vm) == 1, 1, global.aero_gov_mult(0.5 * _vm));
    _chk(""gov_joelho"", abs(global.aero_gov_mult(0.92 * _vm) - 1) < 0.01, 1, global.aero_gov_mult(0.92 * _vm));
    _chk(""gov_meio"", abs(global.aero_gov_mult(0.96 * _vm) - 0.5) < 0.03, 0.5, global.aero_gov_mult(0.96 * _vm));
    _chk(""gov_topo"", abs(global.aero_gov_mult(_vm)) < 0.01, 0, global.aero_gov_mult(_vm));

    // 9d: 3a marcha a 120 km/h esta MUITO abaixo do topo -> aceleracao intacta
    _chk(""terceira_livre"", global.aero_gov_mult(120) == 1, 1, global.aero_gov_mult(120));

    // 10: armadilha de realimentacao - hp instantaneo baixo (logo apos a troca) NAO pode
    // derrubar o limite de potencia, senao o motor nao sobe de giro de novo
    global.aero_forca_ind = 0; global.aero_crr = 0;
    global.maxhp = 600; global.hp = 60;      // 10% do pico: como fica em giro baixo
    global.kmh = 150;
    _chk(""potencia_pico"", abs(global.aero_thrust_units() - 9.91) < 0.5, 9.91, global.aero_thrust_units());

    // 11: e o hp instantaneo baixo nao pode fazer o topo desabar
    _chk(""topo_nao_desaba"", abs(global.aero_power_w() - 447420) < 100, 447420, global.aero_power_w());

    // 12: sem dado de potencia a camada falha ABERTO (nunca trava o carro)
    global.maxhp = 0; global.hp = 0;
    global.kmh = 200;
    _chk(""falha_aberto"", global.aero_thrust_units() >= 999, ""999+"", global.aero_thrust_units());

    // restaura
    global.kmh = _kmh_old; global.cx = _cx_old; global.area_frontal = _ar_old;
    global.weight = _w_old; global.hp = _hp_old; global.maxhp = _mhp_old; global.aero_cl = _cl_old;
    global.aero_forca_ind = _ind_old; global.aero_crr = _crr_old;

    var _h2 = file_text_open_append(""aero_test.txt"");
    file_text_write_string(_h2, ""RESUMO pass="" + string(global.aet_p) + "" fail="" + string(global.aet_f));
    file_text_writeln(_h2);
    file_text_close(_h2);
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK aero-test");
