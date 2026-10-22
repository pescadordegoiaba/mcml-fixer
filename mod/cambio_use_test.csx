using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""cuse_t"")) {
    global.cuse_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""cambio_use.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    global._log(""inicio"");

    var _d6 = inventory_get_item_data(""manual_6m"");
    var _d7 = inventory_get_item_data(""manual_7m"");
    var _d8 = inventory_get_item_data(""manual_8m"");
    global._log(""use6="" + string(!is_undefined(_d6) && variable_struct_exists(_d6, ""use_func"")));
    global._log(""use7="" + string(!is_undefined(_d7) && variable_struct_exists(_d7, ""use_func"")));
    global._log(""use8="" + string(!is_undefined(_d8) && variable_struct_exists(_d8, ""use_func"")));

    global.inventory = array_create(32);
    for (var i = 0; i < 32; i++) global.inventory[i] = { item_id: """", value: 0 };
    global.inventory[0] = { item_id: ""manual_7m"", value: 1 };
    global._log(""clique7="" + string(inventory_use_item(0)));
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cambio-use-test");
