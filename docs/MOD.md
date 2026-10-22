# Mod Distribuidor + Carburador

Mod de conteúdo para **My Car My Life**, aplicado dentro do `game.unx` pelo
`mcml-fixer` (`--mod-install`). Não substitui arquivos do jogo: adiciona código
GML novo por cima do existente, grava um backup e um marcador, e volta atrás
com `--mod-uninstall`.

Duas partes independentes que compartilham o mesmo menu:

1. **Distribuidor** — peça real do carro (`car.distribuidor`), com física de
   avanço de ignição e efeito direto em torque e ignição.
2. **Carburador** — ajuste dos circuitos de lenta, agulha e borboleta nos
   carburadores que o jogo já tem.

---

## 1. Onde o mod se pendura no código nativo

Três pontos de solda, todos por `QueueFindReplace` com âncora única verificada
(nada de byte-patch de bytecode):

| Arquivo GML | Âncora | O que o mod faz |
|---|---|---|
| `obj_player_Step_0` | `var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);` | antes dela: `global.torque *= mod_dist_fator()` e `global.ignicao = avanço` |
| `obj_player_Step_0` | `var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd;` | injeta `_mcarb = mod_carb_cfg()` |
| `obj_player_Step_0` | `var _gicle_desvio = _gicle_ativo - _gicle_ideal;` | soma `mod_carb_desvio(...)` ao desvio |
| `obj_noguiin_Draw_64` | append | UI do F6 (Draw GUI, um ponto único) |
| `obj_pc_window_Draw_0` | trecho da grade + produto do TOTORTO | categoria 18 e os dois tiles |

O resto é código novo em `obj_noguiin_Create_0` (as funções `global.mod_*`).

### Por que `global.torque` e não a aceleração

O caminho nativo da ignição para carros com ECU é:

```gml
global.torque *= (1 + bonus_ignicao_torque);   // obj_player_Step_0, ~linha 3302
```

Multiplicar `_base_acceleration` (como na primeira versão) mudava só uma
variável local, derivada. Multiplicar `global.torque` reproduz o nativo e
propaga para tudo que lê torque: aceleração, dinamômetro e HUD. E publicar
`global.ignicao` faz a ignição existir de fato, não só no desenho do menu.

---

## 2. Física do distribuidor

### Avanço total

```
avanço = clamp(inicial + centrífugo(rpm) + vácuo(MAP), 0, 45)
```

**Centrífugo em dois estágios** (mola leve + mola pesada, como num
distribuidor real):

```
faixa = max(100, rpm_fim - rpm_inicio)
div   = 0.50 (curva 0) | 0.62 (curva 1) | 0.75 (curva 2)
t1    = clamp((rpm - inicio) / (faixa * div), 0, 1)
t2    = clamp((rpm - inicio - faixa * div) / (faixa * (1 - div)), 0, 1)
cent  = cent_total * (div * t1 + (1 - div) * t2)
```

O primeiro estágio ataca rápido e o segundo complementa — é o que dá o
"degrau" na curva em vez de uma reta.

**Avanço a vácuo** (ported vacuum):

```
inHg = clamp(-MAP / 0.0339, 0, 30)        // 1 inHg = 0,0339 bar
vácuo = vac_total * clamp((inHg - 5) / 10, 0, 1)
```

Só entra entre 5 e 15 inHg e satura aí: em carga plena (MAP ≥ 0) o vácuo é
zero, como num distribuidor com tomada acima da borboleta.

### Ponto ótimo e bônus

```
tn    = clamp((rpm - 800) / 7200, 0, 1)
ideal = lerp(14, 38, tn) - MAP * 3.5
desvio = (avanço - ideal) / 8
pos   = (clamp(desvio, -2, 2) + 1) / 2
```

| `pos` | bônus bruto |
|---|---|
| ≥ 0,90 | +55% … +60% |
| 0,70 – 0,90 | +40% … +55% |
| 0,40 – 0,70 | +20% … +40% |
| < 0,40 | até −25% (avanço longe do ideal) |

O teto é do modelo (`ganho` máximo): **Original 45%**, **Competição 100%**. O
parâmetro `ganho` do carro diz quanto desse teto ele usa (padrão 100).

### Faísca e detonação

```
dwell_ms = (dwell° / 360) * (120000 / rpm)
q        = clamp((dwell_ms - 1.2) / 2.0, 0, 1)      // 0 = faísca fraca
fator    = (1 + bônus)
if (q < 1) fator *= (0.90 + 0.10 * q)
```

Acima de ~8 ms em rotação baixa a bobina esquenta. Avanço além do limite com
IAT/temperatura/pressão altas gera detonação, que danifica pistão e biela e
sobe a temperatura.

> Correção importante: a versão 1 aplicava `ganho` **e** o teto do modelo,
> derrubando o bônus máximo para ~22%. Agora o teto é o do modelo e `ganho`
> só regula quanto dele é usado.

---

## 3. Potência real (torque × rpm)

Até aqui, `hp` e `torque` eram duas curvas independentes, ambas escritas à mão.
Agora `hp` é **derivado** do torque instantâneo e do giro — a relação física
`P = T · ω` — e é esse valor que o jogo usa para acelerar.

```gml
global.pot_hp = function(_torque, _rpm, _maxtorque, _maxrpm, _maxhp) {
    if (_maxhp <= 0 || _maxtorque <= 0 || _maxrpm <= 0) return 0;
    var _k = 0.626 * _maxtorque * _maxrpm / _maxhp;
    return (_torque * _rpm) / _k;
};
```

**Por que 0,626.** Em unidades imperiais a constante é 5252 (lb-ft). O torque do
jogo está em Nm e a potência em hp, então a constante pura seria 7121 —
`1 hp = 550 ft·lb/s`, `1 Nm = 0,7376 lb-ft` → `hp = Nm · rpm / 7121`. Só que a
curva de torque do próprio jogo cai para 0,52·maxtorque no corte, então usar
7121 faria o pico ficar em ~85% do `maxhp` declarado. O 0,626 é a calibração
contra a curva do jogo: medido em três motores (45, 99 e 171 hp), o pico de
`torque·rpm` cai exatamente no valor declarado.

**Quem usa.** Cinco pontos de solda, todos na linha de clamp do `hp` (o fim do
bloco de curvas, antes da física):

| Objeto | Papel |
|---|---|
| `obj_player_Step_0` | jogador |
| `obj_ia_car_Step_0` | trânsito/rival comum |
| `obj_ia_car_TOUGE_Step_0` | modo touge |
| `obj_ia_opponent_Step_0` | oponentes de corrida |
| `obj_ia_car_NPC_Step_0` | NPC de trânsito |

```gml
// antes
hp = clamp(hp, 0, maxhp * 1.2);
// depois
hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.2);
```

Como o torque já é multiplicado pelo carburador, pelo distribuidor e pelos
mods de motor, a potência acompanha tudo isso sem código extra: mistura pobre
derruba `torque`, que derruba `hp`, que derruba a aceleração.

**Consequência de comportamento.** A curva fica "pontuda", como motor de
verdade: sobe com o giro até o pico e cai depois.

```
rpm      45 hp   99 hp   171 hp
1500       9,2    18,6     45,6
2200      19,4    39,3     96,2
3000      32,2    64,5    134,7
4200      44,9    98,3    170,5   <- pico
5000      41,7    96,7    158,5
5500      37,4    91,1    142,0
```

Ou seja: embaixo mais fraco, no pico igual ao declarado, em cima mais forte que
a curva antiga. O carro passa a precisar de giro para render.

---

## 4. Carburador: circuitos novos

O jogo já modela `gicle`, `gicle_2nd` (segundo corpo) e oito tipos de
carburador, com `_gicle_ideal` e `_sensibilidade` por tipo, e converte o
desvio em AFR:

```
global.afr = afr_mid - (gicle_desvio * sensibilidade * afr_mid)
carb_mult  = lerp(1.45, 0.70, dist_norm)      // dist_norm = |afr - afr_ideal_min| / metade da faixa
global.hp     *= carb_mult / 1.25
global.torque *= carb_mult / 1.8
```

O mod entra **na linha do desvio**, somando os circuitos que faltavam:

```gml
var _gicle_desvio = _gicle_ativo - _gicle_ideal
                  + global.mod_carb_desvio(_rpm_ratio, _mcarb, _gicle_ideal);
```

Assim tudo que o jogo já faz — multiplicador de hp/torque, aquecimento por
mistura rica, corte de rpm por mistura pobre, falha aleatória — continua
valendo, agora reagindo aos circuitos novos.

| Parâmetro | Campo | Faixa | Peso por rotação | Padrão |
|---|---|---|---|---|
| Giclê de lenta | `carb_cfg.lenta` | 20–60 | 1,0 abaixo de 8%, zero em 20% | 35% do giclê ideal |
| Agulha (média) | `carb_cfg.agulha` | 20–80 | pico em 45%, zero em 15% e 75% | 90% do giclê ideal |
| Borboleta | `carb_cfg.borboleta` | 0–100 | 1,0 na saída, zero em 12% | 50 (neutro) |

```gml
_w_l = 1 - clamp((rpm_ratio - 0.08) / 0.12, 0, 1);
_w_a = clamp(1 - abs(rpm_ratio - 0.45) / 0.30, 0, 1);
_w_b = 1 - clamp(rpm_ratio / 0.12, 0, 1);

desvio = (lenta - ideal_lenta) * _w_l * 0.80
       + (agulha - ideal_agulha) * _w_a * 0.60
       + (borboleta - 50) * _w_b * 0.40;
```

As janelas são separadas de propósito: sem isso, em rotação média lenta e
agulha somavam e o ajuste ficava grosseiro demais (AFR caía para ~9 com +10 em
cada).

### Efeito medido (fórmulas nativas, gasolina, carburador tipo 5)

| Cenário | AFR | hp | torque |
|---|---|---|---|
| neutro, 3000 rpm | 13,52 | ×1,039 | ×0,722 |
| agulha +10 | 12,14 | ×1,146 | ×0,796 |
| agulha +20 | 10,76 | ×1,011 | ×0,702 |
| agulha −10 | 14,90 | ×0,904 | ×0,628 |
| neutro, 900 rpm | 13,66 | ×1,032 | ×0,717 |
| lenta +10 | 9,95 | ×0,927 | ×0,644 |
| lenta −10 | 17,38 | ×0,671 | ×0,466 |
| borboleta +20 | 11,50 | ×1,077 | ×0,748 |

Existe ponto ótimo: enriquecer até ~+10 ganha; passar disso afoga e perde.
Pobre perde muito mais que rico.

---

## 5. Menu (F6)

| Tecla | Ação |
|---|---|
| `F6` | abre / fecha |
| `TAB` | Ajuste → Loja → Carburador |
| `↑` `↓` | escolhe o parâmetro |
| `←` `→` | ajusta (respeitando o limite do modelo) |
| `1` `2` | compra Original / Competição (aba Loja) |
| `3` | volta ao distribuidor Original |
| `F7` | padrão do modelo / do carburador |

Cada parâmetro mostra duas linhas (o que faz, o que melhora e o que piora) e,
no distribuidor, **mín / atual / máx** calculados rodando o modelo de verdade
com o parâmetro nos extremos — não é texto fixo.

A UI desenha **só no Draw GUI** (`obj_noguiin_Draw_64`). Desenhar no `Draw_0`
pisca, porque o passe CRT (`obj_crt_Draw_77`, depth 400) redesenha a
`application_surface` por cima no mesmo frame.

---

## 6. Loja TOTORTO

Categoria própria, **anexada no fim** (índice 18) para não deslocar as
categorias 0–17:

| item | preço | campo | `campo_val` |
|---|---|---|---|
| Distribuidor Original | $800 (reposição) | `distribuidor` | 1 |
| Distribuidor Competição | $12.000 | `distribuidor` | 2 |

- `GetPartValue2` → `[800, 12000]`, `SetPartOnCar2` → campo `"distribuidor"`
- `DeliveryGetItemId` → `distribuidor_original` / `distribuidor_competicao`
- grade: `ad_coords_ext` x 40/220/400, `ad_sprite_ext` x 120/300/480,
  linhas novas em `win_y+950`, `+978`, `+1133`, `content_height_ext2` 1260
- `item_db` registra os dois com `campo`/`campo_val`, e
  `item_use_peca` passa a respeitar `campo_val` (antes usava o índice ordinal)

Todo motor **carburado** sai de fábrica com o Original; a Competição é o
upgrade pago. Motor injetado não recebe distribuidor (`car.carburador > 0`).

---

## 7. Arte (sprite externo)

O ícone é carregado por `sprite_add` em runtime, e **este runner é
sandboxed**: só resolve arquivos na **raiz** da pasta de save.

```
file_exists("distribuidor_icon.png")  → 1     ✔ raiz do save
file_exists("assets/Mod_assets/x.png") → 0    ✘ subpasta
file_exists("/caminho/absoluto/x.png") → 0    ✘ absoluto
```

Medido instrumentando o próprio jogo (o `working_directory` aponta para
`.../assets/`, então `working_directory + "assets/..."` vira `assets/assets/...`).

Por isso o instalador copia `distribuidor.png` e `distribuidor_icon.png` para
`~/.config/My_Car_My_Life/` e o mod chama `sprite_add("distribuidor_icon.png")`.
Se a arte faltar, o desenho cai num marcador alinhado ("DIST") — nunca num
sprite deslocado nem num índice `-1`, que derrubava o jogo com
`draw_sprite_ext argument 1 invalid reference`.

Ícone: 256×256 RGBA, recortado da ilustração 1152×1728, fundo branco
convertido em alfa.

---

## 8. Estado e arquivos

| Caminho | O quê |
|---|---|
| `assets/game.unx` | jogo com o mod aplicado |
| `assets/game.unx.bak-mod-distribuidor` | backup anterior ao mod |
| `.mcml-mod-distribuidor` | marcador (contém a config usada na instalação) |
| `~/.config/My_Car_My_Life/mod_distribuidor.ini` | defaults para carros novos |
| `~/.config/My_Car_My_Life/distribuidor.png` / `distribuidor_icon.png` | arte para o `sprite_add` |
| `car.distribuidor` / `car.dist_cfg` | estado por carro, dentro do save |
| `car.carb_cfg` | ajuste do carburador por carro, dentro do save |

Config antiga (`dist_cfg` sem `ver`) migra uma vez: `ganho` sobe de 50 para 100.

---

## 9. Verificação

`mcml-fixer --mod-check` abre o `game.unx` com o UTMT e procura marcadores no
decompilado. Os 16 atuais:

```
versao=v2.1-fisica  item  limites  dano  potencia  ui
loja  shop_tile  shop_preco  shop_entrega  shop_instala
carb  carb_hook  torque  potencia_real  pot_npc
```

`carb` confere as funções `mod_carb_cfg`/`mod_carb_desvio`; `carb_hook` confere
a injeção `_mcarb` no Step; `torque` confere
`global.torque *= global.mod_dist_fator()` **e**
`global.ignicao = global.mod_dist_estado.avanco`.
