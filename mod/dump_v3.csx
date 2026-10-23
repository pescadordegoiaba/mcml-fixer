using System;
using System.IO;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
void Dump(string obj, string file) =>
    File.WriteAllText("/home/gullin/.hermes/cache/scratch/" + file,
        GetDecompiledText(Data.Code.ByName(obj), ctx, st));
Dump("gml_Object_obj_player_Create_0", "v3_create.gml");
Dump("gml_Object_obj_player_Step_0", "v3_step.gml");
Dump("gml_Object_obj_noguiin_Draw_0", "v3_draw.gml");
Dump("gml_RoomCC_Start_0_Create", "v3_init.gml");
Dump("gml_Object_obj_player_Collision_obj_ia_car_TOUGE", "v3_col_touge.gml");
Dump("gml_Object_obj_ia_car_NPC_Collision_obj_player", "v3_col_npc.gml");
ScriptMessage("MARK dump-v3");
