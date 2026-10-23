using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""pt_t"")) {
    global.pt_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""pt_test.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    global._log(""inicio"");
    global._log(""curva_010="" + string(global.pt_curva(0.10, 0.45)));
    global._log(""curva_019="" + string(global.pt_curva(0.19, 0.45)));
    global._log(""curva_055="" + string(global.pt_curva(0.55, 0.45)));
    global._log(""curva_100="" + string(global.pt_curva(1.00, 0.45)));
    global._log(""pico_cmd0="" + string(global.pt_pico(0)) + "" pico_cmd3="" + string(global.pt_pico(3)));
    global.maxtorque = 400;
    global.litros = 2.0;
    global._log(""torque_1650_base57="" + string(global.pt_torque(57, 1650, 8500, 0)));
    global._log(""torque_4675_base150="" + string(global.pt_torque(150, 4675, 8500, 0)));
    global._log(""torque_8500_base400="" + string(global.pt_torque(400, 8500, 8500, 0)));
    global._log(""shift_time="" + string(global.pt_shift_time));
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK pt-test");
