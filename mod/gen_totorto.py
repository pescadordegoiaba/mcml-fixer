#!/usr/bin/env python3
"""Gera os patches do TOTORTO: categoria 18 = DISTRIBUIDOR (2 itens).

Cada âncora é extraída do GML decompilado e verificada como única antes de
virar QueueFindReplace, para não repetir o bug do `base_w` (replace pegando
duas ramificações)."""

import pathlib
import sys

D = pathlib.Path("/home/gullin/projetos/mcml-decompiled/CodeEntries")
GP = "03932_gml_GlobalScript_GetNameParts.gml"
DL = "00492_gml_GlobalScript_Delivery.gml"
PC = "05284_gml_Object_obj_pc_window_Draw_0.gml"
INV = "03949_gml_GlobalScript_Inventory.gml"


def read(name):
    return (D / name).read_text(encoding="utf-8", errors="ignore")


def csx_str(s):
    return '@"' + s.replace('"', '""') + '"'


def rep(code, entry, old, new, label):
    if code.count(old) != 1:
        sys.exit("ancora nao unica (%d) em %s: %s" % (code.count(old), label, entry))
    return (f'g.QueueFindReplace("{entry}",\n    {csx_str(old)},\n    {csx_str(new)},\n    false);\n')


def main():
    gp, dl, pc, inv = read(GP), read(DL), read(PC), read(INV)
    out = []

    # 1) GetPartLevelsArray2: niveis da categoria nova (2 itens)
    old = "        default:\n            return [];\n    }\n}\n\nfunction GetPressureTurbo(arg0)"
    new = ('        case 18:\n'
           '            return (global.idioma == 0) ? ["Original", "Competicao"] : ["Original", "Competition"];\n'
           + old)
    out.append(rep(gp, "gml_GlobalScript_GetNameParts", old, new, "levels2"))

    # 2) GetNamePartsVenture2: nomes que aparecem no carrinho/confirmacao
    old = ('                return @@array_get@@(["シングルボディ 2E", "シングルボディ 3E", "デュアルボディ VFD 446", '
           '"デュアルボディ Vever h34", "デュアルボディ Vever 40", "デュアルボディ Vever 44", '
           '"デュアルボディ Vever 48", "デュアルボディ Vever 55"], arg1);\n'
           '            }\n            break;\n        default:')
    new = ('                return @@array_get@@(["シングルボディ 2E", "シングルボディ 3E", "デュアルボディ VFD 446", '
           '"デュアルボディ Vever h34", "デュアルボディ Vever 40", "デュアルボディ Vever 44", '
           '"デュアルボディ Vever 48", "デュアルボディ Vever 55"], arg1);\n'
           '            }\n            break;\n'
           '        case 18:\n'
           '            if (idioma == 0)\n            {\n'
           '                return @@array_get@@(["Distribuidor Original", "Distribuidor Competicao"], arg1);\n'
           '            }\n'
           '            return @@array_get@@(["Original Distributor", "Competition Distributor"], arg1);\n'
           '            break;\n'
           '        default:')
    out.append(rep(gp, "gml_GlobalScript_GetNameParts", old, new, "venture2"))

    # 3) GetPartValue2: precos (original e reposicao, competicao caro)
    old = "            return @@array_get@@([600, 1500, 950, 1300, 2600, 3200, 6600, 9600, 9600], arg1);"
    new = old + "\n        case 18:\n            return @@array_get@@([800, 12000], arg1);"
    out.append(rep(gp, "gml_GlobalScript_GetNameParts", old, new, "value2"))

    # 4) SetPartOnCar2: categoria -> campo do carro
    old = '        case 17:\n            return "carburador";\n        default:\n            return "parte_invalida";'
    new = ('        case 17:\n            return "carburador";\n'
           '        case 18:\n            return "distribuidor";\n'
           '        default:\n            return "parte_invalida";')
    out.append(rep(gp, "gml_GlobalScript_GetNameParts", old, new, "setpart2"))

    # 5) DeliveryGetItemId: itens que viajam na entrega
    old = '                case 7:\n                    return "carburador_8";\n            }\n    }\n    return "";'
    new = ('                case 7:\n                    return "carburador_8";\n            }\n'
           '        case 18:\n            switch (arg1)\n            {\n'
           '                case 0:\n                    return "distribuidor_original";\n'
           '                case 1:\n                    return "distribuidor_competicao";\n'
           '            }\n'
           '    }\n    return "";')
    out.append(rep(dl, "gml_GlobalScript_Delivery", old, new, "delivery"))

    # 6) grade de categorias: mais altura de rolagem
    out.append(rep(pc, "gml_Object_obj_pc_window_Draw_0",
                   "        var content_height_ext2 = 1120;",
                   "        var content_height_ext2 = 1260;", "content_h"))

    # 7/8) 19a posicao na grade de categorias
    # ad_coords_ext usa 40/220/400 como x, ad_sprite_ext usa 120/300/480.
    # Anexa no FIM: os indices 0..17 continuam sendo as categorias originais.
    out.append(rep(pc, "gml_Object_obj_pc_window_Draw_0",
                   "[win_x + 400, win_y + 820 + _venture_y]];",
                   "[win_x + 400, win_y + 820 + _venture_y], [win_x + 40, win_y + 950 + _venture_y]];",
                   "coords_ext_tail"))
    out.append(rep(pc, "gml_Object_obj_pc_window_Draw_0",
                   "[win_x + 480, win_y + 978 + _venture_y]];",
                   "[win_x + 480, win_y + 978 + _venture_y], [win_x + 120, win_y + 1133 + _venture_y]];",
                   "sprite_ext_tail"))

    # 9) sprite do tile da categoria 18
    old = ("                if (i == 15)\n                {\n"
           "                    draw_sprite_ext(spr_venture_wb, i, sx, sy, scale, scale, 0, -1, 1);\n"
           "                }")
    new = old + ("\n                else if (i == 18)\n                {\n"
                 "                    global.mod_dist_draw_icon(sx, sy + 10, 96);\n"
                 "                }")
    out.append(rep(pc, "gml_Object_obj_pc_window_Draw_0", old, new, "grid_spr"))

    # 10) rotulo do tile
    old = ('                            else\n                            {\n'
           '                                label_ext = "キャブレター";\n'
           '                            }\n                            break;\n                    }')
    new = ('                            else\n                            {\n'
           '                                label_ext = "キャブレター";\n'
           '                            }\n                            break;\n'
           '                        case 18:\n                            label_ext = "DISTRIBUIDOR";\n'
           '                            break;\n                    }')
    out.append(rep(pc, "gml_Object_obj_pc_window_Draw_0", old, new, "grid_label"))

    # 11) sprite na pagina de produto da categoria 18
    old = ("                if (global.venture_pecas == 15)\n                {\n"
           "                    draw_sprite_ext(spr_venture_wb, 0, sx, sy, scale, scale, 0, -1, 1);\n"
           "                }")
    new = ("                if (global.venture_pecas == 18)\n                {\n"
           "                    global.mod_dist_draw_icon(sx, sy + 10, 96);\n"
           "                }\n"
           "                else if (global.venture_pecas == 15)\n                {\n"
           "                    draw_sprite_ext(spr_venture_wb, 0, sx, sy, scale, scale, 0, -1, 1);\n"
           "                }")
    out.append(rep(pc, "gml_Object_obj_pc_window_Draw_0", old, new, "prod_spr"))

    # 12) instalar respeitando campo_val (1=original, 2=competicao)
    old = "    variable_struct_set(@@array_get@@(global.car_data, idx), campo, _inventory_campo_value(arg1.item_id, campo));"
    new = ("    var _pv_inst = variable_struct_exists(data, \"campo_val\") ? data.campo_val : _inventory_campo_value(arg1.item_id, campo);\n"
           "    variable_struct_set(@@array_get@@(global.car_data, idx), campo, _pv_inst);")
    out.append(rep(inv, "gml_GlobalScript_Inventory", old, new, "install"))

    pathlib.Path(__file__).parent.joinpath("totorto_patch.txt").write_text(
        "".join(out), encoding="utf-8")
    print("patches totorto:", len(out))


if __name__ == "__main__":
    main()
