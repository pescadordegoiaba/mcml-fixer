using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""cgc"")) {
    global.cgc = 1;
    var _f = file_text_open_append(""cg_create.txt"");
    var _n0 = instance_number(obj_player);
    file_text_write_string(_f, ""players_antes="" + string(_n0));
    file_text_writeln(_f);
    file_text_close(_f);
    var _p = instance_create_depth(320, 320, -100, obj_player);
    var _d = file_text_open_append(""cg_create.txt"");
    if (!instance_exists(_p)) {
        file_text_write_string(_d, ""CREATE FALHOU"");
        file_text_writeln(_d);
        file_text_close(_d);
    } else {
        file_text_write_string(_d, ""CREATE ok max_gears="" + string(_p.max_gears)
            + "" ratios="" + string(_p.gear_ratios)
            + "" orig="" + string(_p.original_ratios)
            + "" cambio_mod="" + string(global.cg_num(global.cg_get(global.car_data[global.vehicles_array], ""cambio_mod"", 0), 0))
            + "" cgv="" + string(global.cg_num(global.cg_get(global.car_data[global.vehicles_array], ""cg_versao"", 0), 0)));
        file_text_writeln(_d);
        file_text_close(_d);
        with (_p) instance_destroy();
    }
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cg-create-test");
