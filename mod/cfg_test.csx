using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };
string gml = @"if (!variable_global_exists(""cfgt"")) {
    global.cfgt = 1;
    // carro salvo simulado: phys_cfg antigo e dist_cfg ja ajustado
    global.car_data = [
        { carburador: 5, gicle: 50, gicle_2nd: 50, cabecote: 1,
          phys_cfg: { raio: 0.28, eficiencia: 0.9, mu: 1.0, share: 1.0, embreagem: 0, inercia: 0.15 },
          dist_cfg: { avanco_inicial: 5, cent_total: 10, rpm_inicio: 1000, rpm_fim: 3000,
                      curva: 0, vac_total: 0, vac_limiar: 0, dwell: 30, desgaste: 0, ganho: 40, ver: 2 } }
    ];
    global.vehicles_array = 0;
    global.mod_cfg_tick = 0;
    var _r = global.mod_cfg_aplicar();
    var _c = global.car_data[0];
    var _f = file_text_open_write(""cfg_test.txt"");
    file_text_write_string(_f, ""ret="" + string(_r));
    file_text_write_string(_f, "" raio="" + string(_c.phys_cfg.raio));
    file_text_write_string(_f, "" mu="" + string(_c.phys_cfg.mu));
    file_text_write_string(_f, "" inercia="" + string(_c.phys_cfg.inercia));
    file_text_write_string(_f, "" ganho="" + string(_c.dist_cfg.ganho));
    file_text_write_string(_f, "" avanco="" + string(_c.dist_cfg.avanco_inicial));
    file_text_write_string(_f, "" stamp="" + string(_c.cfg_stamp));
    file_text_close(_f);
    global.mod_cfg_tick = 0;
    var _r2 = global.mod_cfg_aplicar();
    var _f2 = file_text_open_write(""cfg_test2.txt"");
    file_text_write_string(_f2, ""ret2="" + string(_r2));
    file_text_close(_f2);
}
";
g.QueueAppend("gml_Object_obj_crt_Draw_77", gml);
g.Import();
ScriptMessage("MARK cfg-test");
