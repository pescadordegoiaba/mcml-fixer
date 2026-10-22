using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"
if (!variable_global_exists(""mtx_done"")) {
    global.mtx_done = 1;
    var _f = file_text_open_write(""phys_matrix.txt"");
    var _car = { pistao: 0, bielas: 0, bloco: 0, vira: 0, filtro: 0, admissao: 0, escape: 0, pipe: 0,
                 cabecote: 0, carburador: 5, gicle: 50, gicle_2nd: 50,
                 vpistao: 70, vbielas: 70, vcabecote: 70, vcomando: 70, vvira: 70, vbloco: 70 };
    var _R = 6000;
    var _rpm = 4000;
    global.turbo = 0;
    global.piston = 4;
    global.litros = 1.8;
    global.fuel = 1;
    global.ignicao = 28;
    global.injecao = 70;

    var _b = global.motor_torque(_car, _rpm, _R, 14.7);
    file_text_write_string(_f, ""base|"" + string(_b) + ""\n"");

    _car.pistao = 3; _car.bielas = 3; _car.bloco = 3; _car.vira = 3;
    _car.filtro = 2; _car.admissao = 2; _car.escape = 2; _car.pipe = 2;
    var _p = global.motor_torque(_car, _rpm, _R, 14.7);
    file_text_write_string(_f, ""pecas|"" + string(_p) + ""\n"");

    file_text_write_string(_f, ""valvulas8|"" + string(global.motor_valvulas(8, 0)) + ""\n"");
    file_text_write_string(_f, ""valvulas20|"" + string(global.motor_valvulas(8, 3)) + ""\n"");

    global.turbo = 2;
    var _t = global.motor_torque(_car, _rpm, _R, 14.7);
    file_text_write_string(_f, ""turbo2|"" + string(_t) + ""\n"");
    global.turbo = 0;

    global.ignicao = 10;
    var _i = global.motor_torque(_car, _rpm, _R, 14.7);
    file_text_write_string(_f, ""ign10|"" + string(_i) + ""\n"");
    global.ignicao = 28;

    _car.vpistao = 0; _car.vbielas = 0; _car.vcabecote = 0;
    _car.vcomando = 0; _car.vvira = 0; _car.vbloco = 0;
    var _d = global.motor_torque(_car, _rpm, _R, 14.7);
    file_text_write_string(_f, ""desgaste|"" + string(_d) + ""\n"");

    _car.vpistao = 70; _car.vbielas = 70; _car.vcabecote = 70;
    _car.vcomando = 70; _car.vvira = 70; _car.vbloco = 70;
    _car.gicle = 50;
    var _c0 = global.motor_torque(_car, _rpm, _R, global.motor_afr(_car, _rpm, _R, 0, 1));
    _car.gicle = 60;
    var _c1 = global.motor_torque(_car, _rpm, _R, global.motor_afr(_car, _rpm, _R, 0, 1));
    file_text_write_string(_f, ""carb50|"" + string(_c0) + ""\n"");
    file_text_write_string(_f, ""carb60|"" + string(_c1) + ""\n"");

    _car.carburador = 0;
    global.injecao = 70;
    var _j0 = global.motor_torque(_car, _rpm, _R, 14.7);
    global.injecao = 84;
    var _j1 = global.motor_torque(_car, _rpm, _R, 14.7);
    file_text_write_string(_f, ""inj70|"" + string(_j0) + ""\n"");
    file_text_write_string(_f, ""inj84|"" + string(_j1) + ""\n"");

    var _k99 = global.phys_k(97, 0.31, 9);
    var _base_nova = global.phys_base(138.0, 9, 0.31, 1200, _k99, 0.90, 1.0, 1.0);
    var _base_antiga = (138.0 * 97.0) / (1.2 * 750);
    file_text_write_string(_f, ""k99|"" + string(_k99) + ""\n"");
    file_text_write_string(_f, ""base_nova|"" + string(_base_nova) + ""\n"");
    file_text_write_string(_f, ""base_antiga|"" + string(_base_antiga) + ""\n"");
    file_text_write_string(_f, ""razao|"" + string(_base_nova / _base_antiga) + ""\n"");
    file_text_close(_f);
}
";
g.QueueAppend("gml_Object_obj_crt_Draw_77", gml);
g.Import();
ScriptMessage("MARK phys-matrix");
