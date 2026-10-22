using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""cswap_t"")) {
    global.cswap_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""cambio_swap.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    global._log(""inicio"");

    global.car_data = [ { cambio: 3, cambio_mod: 0, gears_mod: [], at: 0, vcambio: 100,
                          motor: 1, pistao: 0, bielas: 0, cabecote: 0, comando: 0, vira: 0, bloco: 0,
                          vpistao: 100, vbielas: 100, vcabecote: 100, vcomando: 100, vvira: 100, vbloco: 100,
                          turbo: 0, vturbo: 100, turbo_max: 0, roda_dian: 0, admissao: 0, filtro: 0, intercooler: 0, pipe: 0,
                          embreagem: 0, pneu: 0, transmissao: 0, radiador: 0, oleo: 0, nitro: 0,
                          nitro_charge: 0, carcolor: 0, noguiintech: 0, shutdown: 0, shiftlight: 0,
                          fuel: 100, flex: 0, ajuste_ignicao: 0, ajuste_injecao: 0, carroceria: 0,
                          escape: 0, corfarol: 0, corinsufilm: 0, roda: 0, aero: 0, susp: 0,
                          roda_tam: 0, odd: 0, fuel_capacity: 50, turbo_nt: [0,0,0,0,0,0,0,0],
                          vinjecao: [0,0], vignicao: [0,0], compensacao_map: [0], compensacao_rpm: [0],
                          n_pistao: 4 } ];
    global.vehicles_array = 0;
    global.inventory = array_create(32);
    for (var i = 0; i < 32; i++) global.inventory[i] = { item_id: """", value: 0 };
    global.inventory[0] = { item_id: ""manual_8m"", value: 1 };

    global._log(""por_campo="" + string(_inventory_get_item_by_campo(""cambio"")));

    global.dinamometro_slot_struct = { item_id: ""manual_8m"" };
    global.dinamometro_slot_index = 0;
    global.dinamometro_categoria = ""cambio"";
    global._dino_executar_swap();
    global._log(""cambio_final="" + string(global.car_data[0].cambio)
        + "" mod="" + string(global.car_data[0].cambio_mod)
        + "" gears="" + string(array_length(global.car_data[0].gears_mod))
        + "" at="" + string(global.car_data[0].at));
    global._log(""inv0="" + string(global.inventory[0].item_id));
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cambio-swap-test");
