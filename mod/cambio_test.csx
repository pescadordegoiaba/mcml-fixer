using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""camb_t"")) {
    global.camb_t = 1;
    global._log = function(_s) {
        var _f = file_text_open_append(""cambio_test.txt"");
        file_text_write_string(_f, _s + ""\n"");
        file_text_close(_f);
    };
    var _l2 = GetPartLevelsArray2(6);
    var _l1 = GetPartLevelsArray(12);
    global._log(""lv2_len="" + string(array_length(_l2)) + "" lv1_len="" + string(array_length(_l1)));
    global._log(""n7="" + string(GetNamePartsVenture2(6, 10)) + "" | "" + string(GetNamePartsVenture(12, 10)));
    global._log(""n8="" + string(GetNamePartsVenture2(6, 11)) + "" | "" + string(GetNamePartsVenture(12, 11)));
    global._log(""p7="" + string(GetPartValue2(6, 10)) + "" | "" + string(GetPartValue(12, 10)));
    global._log(""p8="" + string(GetPartValue2(6, 11)) + "" | "" + string(GetPartValue(12, 11)));
    global._log(""it7="" + string(inventory_get_item_data(""manual_7m"") != undefined)
              + "" it8="" + string(inventory_get_item_data(""manual_8m"") != undefined));
    global._log(""dlv="" + string(DeliveryGetItemId(6, 10, 0)) + "" | "" + string(DeliveryGetItemId(6, 11, 0)));
    global._log(""fim"");
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cambio-test");
