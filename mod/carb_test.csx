using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""carb_t"")) {
    global.carb_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""carb_test.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    global._log(""inicio"");

    global.car_data = [ { carburador: -1, bicos: 0, motor: 1, piston: 4,
                          distribuidor: undefined, dist_cfg: undefined } ];
    global.vehicles_array = 0;
    global.piston = 4;

    var _tem = variable_global_exists(""mod_dist_tipo"");
    global._log(""tem_tipo="" + string(_tem));
    global._log(""pode_-1="" + string(global.mod_dist_pode()));
    global._log(""modelo_-1="" + string(global.mod_dist_modelo()) + "" dist="" + string(global.car_data[0].distribuidor));

    if (_tem) {
        global._log(""tipo_-1="" + string(global.mod_dist_tipo(global.car_data[0])));
        global.car_data[0].carburador = 0;
        global._log(""tipo_0="" + string(global.mod_dist_tipo(global.car_data[0])) + "" pode_0="" + string(global.mod_dist_pode()));
        global.car_data[0].carburador = 3;
        global._log(""tipo_3="" + string(global.mod_dist_tipo(global.car_data[0])) + "" pode_3="" + string(global.mod_dist_pode()));
        global.car_data[0].carburador = -1;
        global.car_data[0].distribuidor = 0;
        global._log(""cura="" + string(global.mod_dist_modelo()) + "" dist="" + string(global.car_data[0].distribuidor));
    }
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK carb-test");
