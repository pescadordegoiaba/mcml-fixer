using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""upgt"")) {
    global.upgt = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""upg_test.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    global._log(""inicio"");

    global.mod_rev = 100;
    global.dev_cash = 500000;
    global.dev_cash_modo = 0;
    global.dev_cash_rev = -1;
    global.cash = 100;
    global._log(""piso_sobe="" + string(global.dev_cash_aplicar()) + "" cash="" + string(global.cash));
    global.cash = 900000;
    global._log(""piso_nao_abaixa="" + string(global.dev_cash_aplicar()) + "" cash="" + string(global.cash));
    global.dev_cash_modo = 1;
    global.cash = 900000;
    global._log(""exato="" + string(global.dev_cash_aplicar()) + "" cash="" + string(global.cash));
    global._log(""exato_repete="" + string(global.dev_cash_aplicar()));
    global.cash = -500;
    global._log(""guard="" + string(global.cash_guard()) + "" cash="" + string(global.cash));

    global.car_data = [ {
        carburador: 5, distribuidor: 1, gicle: 50, gicle_2nd: 50, cabecote: 1,
        turbo: 0, pistao: 0, bielas: 0, comando: 0, vira: 0, bloco: 0,
        rpm: 6000, minrpm: 900, valvulas: 8, peso: 1.2,
        vcabecote: 80, vpistao: 80, vbielas: 80, vcomando: 80, vvira: 80, vbloco: 80,
        dist_cfg: { avanco_inicial: 10, cent_total: 22, rpm_inicio: 1000, rpm_fim: 3200,
                    curva: 1, vac_total: 12, vac_limiar: 0, dwell: 45, desgaste: 0, ganho: 100, ver: 2 }
    } ];
    global.vehicles_array = 0;
    global._log(""carro ok"");

    global.engine = 1;
    global.turbo_pressure = 0;
    global.turbo = 0;
    global.piston = 4;
    global.litros = 1.8;
    global.fuel = 1;
    global.ignicao = 28;
    global.injecao = 70;
    global.temp = 25;
    global.temp_max = 120;
    global.iat = 25;
    global.heat_tire = 80;
    global.weight = 1.2;
    global.hp = 99;
    global.torque = 150;
    global.kmh = 60;
    global.gear = 1;
    global.maxtorque = 150;
    global.maxhp = 99;
    global.dt = 1;
    global._log(""globais ok"");

    global.rpm = 5600;
    global._log(""fator_5600="" + string(global.mod_dist_fator()));
    global.rpm = 5000;
    global._log(""fator_5000="" + string(global.mod_dist_fator()));

    var _vc0 = global.car_data[0].vcabecote;
    var _vp0 = global.car_data[0].vpistao;
    global.rpm = 3000;
    global.mod_dist_estado.avanco = 60;
    global.mod_dist_dano();
    global._log(""knock="" + string(global.mod_dist_estado.knock)
        + "" vc="" + string(_vc0) + ""->"" + string(global.car_data[0].vcabecote)
        + "" vp="" + string(_vp0) + ""->"" + string(global.car_data[0].vpistao));
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK upg-test");
