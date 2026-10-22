using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = Data.ToolInfo.DecompilerSettings;
string[] alvos = { "gml_Object_obj_player_Create_0", "gml_Object_obj_noguiin_Draw_0", "gml_GlobalScript_GetNameParts", "gml_GlobalScript_Inventory", "gml_GlobalScript_Delivery", "gml_GlobalScript_minigames_noguiintech" };
foreach (var nome in alvos) {
    var c = Data.Code.ByName(nome);
    string t = "";
    try { t = GetDecompiledText(c, ctx, st); } catch (Exception e) { t = "ERR " + e.Message; }
    ScriptMessage("DIAG " + nome + " len=" + t.Length
        + " c8=" + t.Contains("cambio == 8")
        + " c9=" + t.Contains("cambio == 9")
        + " sp=" + t.Contains("max_gears > 6")
        + " m7=" + t.Contains("manual_7m")
        + " n7=" + t.Contains("Manual 7 marchas")
        + " p45=" + t.Contains("45000"));
}
