using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""st_t"")) {
    global.st_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""shift_test.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    var _corte = 8500;
    var _r7 = 1.9;
    var _r8_curto = 0.30;
    var _r8_stock = 1.55;
    var _kmh = 250;
    global._log(""inicio"");
    global._log(""lim7="" + string((_corte/_r7)*60/1000) + "" rpm7@"" + string(_kmh) + ""="" + string((_kmh/((_corte/_r7)*60/1000))*_corte));
    global._log(""lim8_curto="" + string((_corte/_r8_curto)*60/1000) + "" rpm8@"" + string(_kmh) + ""="" + string(max(800,(_kmh/((_corte/_r8_curto)*60/1000))*_corte)));
    global._log(""lim8_stock="" + string((_corte/_r8_stock)*60/1000) + "" rpm8@"" + string(_kmh) + ""="" + string(max(800,(_kmh/((_corte/_r8_stock)*60/1000))*_corte)));
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK shift-test");
