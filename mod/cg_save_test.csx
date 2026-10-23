using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""cgs"")) {
    global.cgs = 1;
    var _car = global.car_data[global.vehicles_array];
    var _fab = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];
    if (global.cg_num(_car.cambio, 0) != 9) {
        global.cg_diag(""SAVE SKIP cambio="" + string(_car.cambio));
    } else {
        // guarda o estado atual para restaurar no fim
        var _bm = _car.cambio_mod;
        var _bg = CloneArray(_car.gears_mod);
        var _br = _car.relacao_final;
        var _bv = 0;
        if (variable_struct_exists(_car, ""cg_versao"")) _bv = _car.cg_versao;
        var _bcf = _car.cambio;

        var _cand = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.20];
        var _rel = 3.73;
        global.cg_diag(""SAVE ANTES rel="" + string(_car.relacao_final) + "" mod="" + string(_car.cambio_mod));
        var _r = global.cg_aplicar(id, _car, _cand, _rel, _fab);
        global.cg_diag(""SAVE APLICOU r="" + _r + "" "" + global.cg_resumo(_car));
        SaveAllCars();
        SaveGame();

        // le de volta do .ssave
        LoadAllCars();
        var _car2 = global.car_data[global.vehicles_array];
        var _igual = 1;
        if (array_length(_car2.gears_mod) != 8) _igual = 0;
        else {
            for (var i = 0; i < 8; i++) {
                if (abs(global.cg_num(_car2.gears_mod[i], -1) - _cand[i]) > 0.0001) _igual = 0;
            }
        }
        if (abs(global.cg_num(_car2.relacao_final, -1) - _rel) > 0.0001) _igual = 0;
        global.cg_diag(""SAVE DEPOIS "" + global.cg_resumo(_car2));
        global.cg_diag(""SAVE IGUAL="" + string(_igual));

        // restaura o estado do jogador
        var _car3 = global.car_data[global.vehicles_array];
        _car3.cambio_mod = _bm;
        _car3.gears_mod = CloneArray(_bg);
        _car3.relacao_final = _br;
        _car3.cg_versao = _bv;
        SaveAllCars();
        SaveGame();
        global.cg_diag(""SAVE RESTAUROU mod="" + string(_car3.cambio_mod) + "" rel="" + string(_car3.relacao_final));
    }
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cg-save-test");
