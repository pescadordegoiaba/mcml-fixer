using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""ctune_t"")) {
    global.ctune_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""cambio_tune.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    global._log(""inicio"");
    global._log(""rel_min="" + string(global.cambio_rel_min));
    global._log(""rel_max="" + string(global.cambio_rel_max));
    global._log(""rel_ref="" + string(global.cambio_rel_ref));
    global._log(""pct="" + string(global.cambio_limite_pct));
    global._log(""vmax="" + string(global.cambio_vmax));
    global._log(""custo="" + string(global.cambio_custo));

    var _save_cars = global.car_data;
    var _save_idx = global.vehicles_array;
    global.car_data = [ { cambio: 9, cambio_mod: 0, gears_mod: [], rpm: 7500 } ];
    global.vehicles_array = 0;
    global._log(""rel_default="" + string(global.cambio_rel_valor()));
    global._log(""mult_default="" + string(global.cambio_rel_mult()));
    global.car_data[0].relacao_final = 2.60;
    global._log(""rel_260="" + string(global.cambio_rel_valor()));
    global._log(""mult_260="" + string(global.cambio_rel_mult()));
    global.car_data[0].relacao_final = 9;
    global._log(""rel_clamp="" + string(global.cambio_rel_valor()));
    var _r8 = 1.55 * (2.60 / 3.73);
    global._log(""oitava_260="" + string((7500 / _r8) * 60 / 1000));
    global.car_data = _save_cars;
    global.vehicles_array = _save_idx;
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cambio-tune-test");
