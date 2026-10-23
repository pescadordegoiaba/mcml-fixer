using System;
using System.IO;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
File.WriteAllText("/home/gullin/.hermes/cache/scratch/v2_step.gml", GetDecompiledText(Data.Code.ByName("gml_Object_obj_player_Step_0"), ctx, st));
ScriptMessage("MARK dump-ok");
