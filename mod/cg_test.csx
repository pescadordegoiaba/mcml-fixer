using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""cgt"")) {
    global.cgt = 1;
    global.cgt_p = 0;
    global.cgt_f = 0;
    var _chk = function(_nome, _ok, _exp, _veio) {
        var _h = file_text_open_append(""cg_test.txt"");
        if (_ok) { file_text_write_string(_h, ""PASS "" + _nome); global.cgt_p += 1; }
        else { file_text_write_string(_h, ""FAIL "" + _nome + "" (esperado "" + string(_exp) + "", veio "" + string(_veio) + "")""); global.cgt_f += 1; }
        file_text_writeln(_h);
        file_text_close(_h);
    };

    var _fab = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];
    var _cand = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.20];

    // 1 e 2: saneamento de numeros
    _chk(""num_inf"", global.cg_num(infinity, 3.73) == 3.73, 3.73, global.cg_num(infinity, 3.73));
    _chk(""num_undef"", global.cg_num(undefined, 1.5) == 1.5, 1.5, global.cg_num(undefined, 1.5));

    // 3: caixa valida passa
    var _car = { cambio: 9, cambio_mod: 0, gears_mod: [], relacao_final: 2.80, rpm: 8500, cg_versao: 3 };
    var _v1 = global.cg_validar(_car, _cand, 2.80, _fab);
    _chk(""validar_ok"", _v1 == """", """", _v1);

    // 4: numero de marchas errado
    var _v2 = global.cg_validar(_car, [9.35, 6.4, 4.9, 3.8, 3.0, 2.4], 2.80, _fab);
    _chk(""validar_tamanho"", string_pos(""esperava 8"", _v2) > 0, ""esperava 8"", _v2);

    // 5: valor nan numa marcha
    var _c2 = CloneArray(_cand);
    _c2[3] = -1;
    var _v3 = global.cg_validar(_car, _c2, 2.80, _fab);
    _chk(""validar_nan"", string_pos(""invalida"", _v3) > 0, ""invalida"", _v3);

    // 6: fora da janela de +-70%
    var _c3 = CloneArray(_cand);
    _c3[7] = _fab[7] * 2;
    var _v4 = global.cg_validar(_car, _c3, 2.80, _fab);
    _chk(""validar_janela"", string_pos(""fora de"", _v4) > 0, ""fora de"", _v4);

    // 7: relacao final fora da faixa
    var _v5 = global.cg_validar(_car, _cand, 9.0, _fab);
    _chk(""validar_rel_fora"", string_pos(""relacao final fora"", _v5) > 0, ""relacao final fora"", _v5);

    // 8: caixa irreal gera AVISO (nao bloqueia)
    var _c4 = CloneArray(_cand);
    _c4[1] = _c4[0] + 1;
    var _w1 = global.cg_avisos(_car, _c4, 2.80, _fab);
    _chk(""aviso_monotonico"", _w1 != """", ""aviso nao vazio"", _w1);

    // 9: caixa sadia nao gera aviso
    var _w2 = global.cg_avisos(_car, _cand, 2.80, _fab);
    _chk(""aviso_ok"", _w2 == """", """", _w2);

    // 10: grava, confere e aplica
    var _r1 = global.cg_aplicar(id, _car, _cand, 2.80, _fab);
    _chk(""aplicar_ok"", _r1 == """" && _car.gears_mod[7] == 1.20 && global.cg_conferir(_car, _cand, 2.80), """", _r1);
    _chk(""aplicar_max_gears"", max_gears == 8, 8, max_gears);
    _chk(""aplicar_relacao"", gear_ratios[7] == 1.20, 1.20, gear_ratios[7]);

    // 11: clone - mexer em gear_ratios nao corrompe o save
    gear_ratios[0] = 99;
    _chk(""clone"", _car.gears_mod[0] != 99, ""gears_mod intacto"", _car.gears_mod[0]);

    // 12: pagamento
    var _cash_old = global.cash;
    global.cash = 0;
    global.cambio_gratis = 0;
    var _p1 = global.cambio_pode_pagar(6975);
    global.cambio_gratis = 1;
    var _p2 = global.cambio_pode_pagar(6975);
    global.cambio_gratis = 0;
    global.cash = _cash_old;
    _chk(""pagar"", _p1 == false && _p2 == true, ""false/true"", string(_p1) + ""/"" + string(_p2));

    // rollback: valor invalido nao pode gravar
    var _car2 = { cambio: 9, cambio_mod: 0, gears_mod: [], relacao_final: 3.73, rpm: 8500, cg_versao: 3 };
    var _bad = CloneArray(_cand);
    _bad[2] = -5;
    var _r2 = global.cg_aplicar(id, _car2, _bad, 3.73, _fab);
    _chk(""rollback"", _r2 != """" && _car2.cambio_mod == 0 && array_length(_car2.gears_mod) == 0, ""recusado"", _r2);

    var _h = file_text_open_append(""cg_test.txt"");
    file_text_write_string(_h, ""RESUMO pass="" + string(global.cgt_p) + "" fail="" + string(global.cgt_f));
    file_text_writeln(_h);
    file_text_close(_h);
}
";
g.QueueAppend("gml_RoomCC_Start_0_Create", gml);
g.Import();
ScriptMessage("MARK cg-test");
