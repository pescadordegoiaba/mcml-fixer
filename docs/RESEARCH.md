# Pesquisa no decompilado — achados e decisões

Gerado a partir de `tools/scan.py` sobre os 5.498 arquivos de
`/home/gullin/projetos/mcml-decompiled/CodeEntries/`.

Reproduzir:

```bash
python3 tools/scan.py
```

Saída de referência (22/10/2026):

```
arquivos: 5498
funcoes definidas: 579
funcoes sem chamada direta: 139
  despachadas por string (nao sao mortas): 15
  realmente sem referencia: 124

--- dinheiro ---
clamps em global.cash: 0
comparacoes: {'<': 9, '>': 32, '>=': 25}
```

## Método

1. Toda função definida com `function nome(` é coletada.
2. Conta-se `nome(` em todos os arquivos. `<= 1` (só a definição) entra na lista.
3. **Filtro obrigatório:** se o nome aparece como **string** (`"nome"`), a função pode ser
   despachada em runtime e **não é morta**. No inventário, `use_func` guarda o nome e
   `item_use_*` é chamada assim — 15 funções caem nesse caso.
4. Globais: compara-se número de escritas (`global.x =`) com leituras. Escrito muito e lido
   zero é suspeito — mas há falso positivo: `transition_radius`/`transition_type` são lidos
   por efeito colateral do runner (já investigado antes, não é bug).

## Achados de código

| # | Achado | Evidência | Decisão |
|---|---|---|---|
| 1 | `_traction_efficiency` derruba a tração a **0,5** abaixo de 60 km/h para carro com >5 peças e pneu de rua | `05322_obj_player_Step_0.gml:1277–1360` | **removido** (T1) |
| 2 | `_launch_grip = 0,1` (10% de grip) para `tracao=1` e `torque>1000`, abaixo de 80 km/h em 1ª–3ª | `05322:1362–1416` (linha 1370) | **removido** (T2) |
| 3 | `_max_acc = 12/16` já eliminado nos 3 sites | `05322:1474, 3257, 3757` | feito na Fase 1 |
| 4 | `global.cash` sem clamp; 32 comparações `>` contra 25 `>=` | varredura | **`cash_guard` + `>=` no dino** (T3) |
| 5 | Placa gerada e nunca exibida; `FormatPlate()` é identidade | `03870:256`, `04155_Plates.gml` | **placa no HUD** (T5) |
| 6 | `ValueCars()` (FIPE) nunca chamada | `04467_GlobalScript_ValorCarros.gml` | **FIPE no HUD** (T6) |
| 7 | `draw_car_suspension()` nunca chamada | `03007_draw_car.gml:5503` | vestígio documentado (T8) |
| 8 | `Delivery*` (3 funções) nunca chamadas | `00492_GlobalScript_Delivery.gml` | investigado na T8 |
| 9 | `LoadCarColor()` é no-op (atribui o campo a ele mesmo) | `03870_EstruturaSalvamento.gml` | vestígio; sem impacto |
| 10 | `GetCarEngineLimit()` nunca chamada (duplicada por `global.espaco_litros`) | `00391_BigAttack.gml` | vestígio; sem impacto |
| 11 | `CloudSave/CloudLoad` (Google Play) mortas no desktop | `04151_NuvemGooglePlay.gml` | esperado |
| 12 | `Get*Boses` (9 funções de tabela de história) nunca chamadas | `04440_TabelasHistoria.gml` | conteúdo não usado |

## Achado 13 — `_gear_resistance` na linha 820 NÃO é proxy de marcha

O plano previa trocar `_gear_resistance` pela relação real no ganho de giro. **Não foi
feito**, e o motivo é concreto:

```gml
// 05322:780
if ((key_up && clutch == 0 && global.gear == 0) || ... || _borrachao == 1)
{
    ...
    var _rpm_gain = (global.torque / global.weight) * _gear_resistance * _speed_resistance;
    global.rpm += (_rpm_gain * global.dt);
}
```

Esse bloco é o **giro livre** (neutro, embreagem pisada ou burnout). Com `gear == 0`, o
`switch` cai no `default` → `_gear_resistance = 0,05`. Trocar por
`gear_ratios[gear-1]/gear_ratios[0]` daria `1,0` em neutro: **20× mais rápido** para subir
o giro parado. Seria uma mudança de sensação enorme, não validável sem dirigir.

A relação real já entrou na **força** (Fase 2); aqui o valor é amortecimento de rotação
livre, e a Fase 2 ainda adicionou `phys_rev` por cima. Fica como está.

## Achado 14 — funções "mortas" que não são

`item_use_peca`, `item_use_motor`, `item_use_noguiintech`, `item_use_dinamometro` e mais 11
são chamadas por string via `use_func` no banco de itens (`03949_Inventory.gml:56+`). O
analisador sem o filtro de string acusa 139 mortas; com o filtro, 124.

## Pendências que continuam abertas

- `_mod_points < 10` (linha 603) dá `_fator_velocidade = -6` a carro de fábrica — sinal
  estranho, precisa de teste em jogo antes de mexer.
- `Delivery`: **sistema ativo**, ver abaixo.

## Achado 15 — Delivery está implementado e é chamado

`DeliveryPlaceOrder` é chamada de `obj_pc_window_Draw_0.gml:581` (a loja do PC) e
`DeliverySave`/`DeliveryLoad` são chamadas em 7 e 5 lugares. O sistema de entrega com
prazo **funciona**. As três funções "mortas" (`DeliveryGetPending`, `DeliveryHasArrived`,
`DeliveryCleanCollected`) são API não usada — provavelmente o laço da loja faz a checagem
inline. **Nada a corrigir.**

## Achado 16 — suspensão visual

`susp_positions` aparece 278 vezes, 65 delas em `draw_car.gml`, e é usada por
`draw_car_wheels`. A função `draw_car_suspension` é uma variante especializada
(`has_individual_susp`) que nunca é chamada — arte/feature vestigial, não um bug. Ligá-la
exigiria `susp_sprite` por carro, que nem todo modelo tem. **Não mexer.**
