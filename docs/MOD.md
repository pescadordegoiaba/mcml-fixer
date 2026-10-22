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

## 4. Motor: um só lugar para todo upgrade

Antes, cada peça era aplicada em pontos diferentes do Step — e a maioria em lugares
onde o valor já tinha sido consumido. `hp` e `torque` são **recalculados do zero** a cada
frame (linha 864), então qualquer multiplicação feita depois da derivação de `hp` (1142)
é descartada no frame seguinte, antes de a física usar.

Agora existe um compositor único:

```gml
global.motor_torque = function(_car, _rpm, _rpm_max, _afr) {
    var _m = global.motor_pecas(_car);                  // filtro, pipe, escape, admissao, bloco...
    _m *= global.phys_engine_fator(global.turbo, global.piston, global.litros);
    _m *= global.motor_mistura_mult(_afr_eff, ...);     // carburador OU injeção
    _m *= global.motor_ignicao_mult(global.ignicao);
    _m *= global.motor_desgaste(_car);
    return _m;
};
```

Plugado na linha da derivação de `hp`, **antes** de `pot_hp` e antes da aceleração:

```gml
global.torque *= global.motor_torque(car, rpm, rpm_max, global.afr);
global.hp = clamp(global.pot_hp(global.torque, ...), 0, maxhp * 1.2);
```

Consequências medidas no harness `mod/phys_matrix.csx` (motor de referência, 4000 rpm):

| Caso | Fator sobre o torque |
|---|---|
| base (sem peça) | 1.210 |
| todas as peças no máximo | 1.520 (+25,6%) |
| cabeçote nível 3 | válvulas 8 → 20 |
| turbo nível 2 | ×1,72 sobre o base com peças |
| avanço 10° (ideal 28°) | ×0,914 |
| desgaste total | ×0,690 |
| gicle 50 → 60 | ×0,93 |
| injeção 70 → 84 | ×0,92 |

O que mudou de lugar:

- `_engine_multiplier` (boost/deslocamento) virou `phys_engine_fator` dentro do torque;
- `mult_pistao` / `mult_cmd` / `mult_vira` (aplicados tarde) viraram `motor_desgaste`;
- `_carb_mult` (aplicado tarde) virou `motor_mistura_mult` no torque — o carburador passou
  a afetar aceleração, não só o AFR do HUD;
- `global.injecao` virou `motor_afr_inj` (injetados);
- `global.ignicao` virou `motor_ignicao_mult` (o distribuidor do mod já escreve nele);
- `cabecote` passou a mudar `switch (global.valvulas)` via `motor_valvulas`;
- `_max_acc = 12` para aspirados com mais de 5 peças foi removido — instalar peça não
  derruba mais o teto de aceleração.

### Trem de força (RoR)

A aceleração deixou de sair de `(torque·hp)/(peso·750)` e passou a sair de força na roda:

```gml
F = T · gear_ratios[gear-1] · eficiência / raio        // N
F = min(F, μ · m · g · share)                          // limite de aderência
base = (F / m) · k
```

com `m = weight * 1000` (**`weight` está em toneladas**) e

```gml
k = hp · raio · 1000 / (750 · gear1 · 0.90)
```

`k` usa o **hp corrente** e a eficiência de referência 0.90, o que faz a 1ª marcha
reproduzir exatamente a fórmula antiga (medido: `base_nova = base_antiga = 14,87`,
razão 1,00) e, ao mesmo tempo, deixa `eficiencia` e `raio` com efeito real quando mudados.

Consequências:

- `_gear_resistance` (1 / 0.4 / 0.25 / …) saiu da cadeia: quem multiplica agora é a relação
  real de cada marcha, inclusive as editadas no câmbio da garagem.
- `μ` vem de `phys_mu(pneu, heat_tire)`: composto (rua 0,92 / sport 1,05 / slick 1,25 /
  off-road 0,78) e temperatura (penalidade de até −40% fora da faixa).
- O excedente de força vira `global.phys_slip` (patinação) em vez de acelerar o carro.
- Embreagem: `phys_clutch_cap` / `phys_clutch` prontos, **desligados** por padrão
  (`phys_cfg.embreagem = 0`).
- Inércia: em neutro ou com a embreagem pisada o motor gira livre
  (`phys_rev`, `phys_cfg.inercia = 0.15`), modulado por `_camshaft_response` — a variável
  do comando que era escrita e nunca lida.
- NPCs usam as mesmas funções com suas variáveis de instância.

## 5. Carburador: circuitos novos

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

## 6. Menu (F6)

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

## 7. Loja TOTORTO

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

## 8. Arte (sprite externo)

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

## 9. Estado e arquivos

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

## 10. Configuração (ini)

`~/.config/My_Car_My_Life/mod_distribuidor.ini` é lido **uma vez no boot**
(`global.mod_dist_carregar()`) e define os padrões de carro novo:

```ini
[distribuidor]
ativo=1
avanco_inicial=10
cent_total=22
rpm_inicio=1000
rpm_fim=3200
curva=1
vac_total=12
vac_limiar=0
dwell=45
desgaste=0
ganho=100

[fisica]
raio=0.31
eficiencia=0.9
mu=1
share=1
embreagem=0
inercia=0.15
```

O CLI grava sem apagar o resto:

```bash
mcml-fixer --mod-set ganho=80             # mantem a secao [fisica]
mcml-fixer --phys raio=0.33 mu=0.85       # mantem a secao [distribuidor]
mcml-fixer --mod-status                   # mostra os dois blocos
```

`[fisica]`: `raio` do pneu (m), `eficiencia` da transmissão, `mu` global (multiplica o μ
do composto), `share` de peso no eixo motriz, `embreagem` (0 = desligada) e `inercia`
(quanto o motor demora a subir de giro livre).

### Vale também para carro já salvo

O CLI grava `[mod] rev=<epoch>` a cada escrita. No boot o jogo lê esse `rev`; se ele for
diferente do `cfg_stamp` gravado no carro, a config é **aplicada ao carro salvo**
(`global.mod_cfg_aplicar`, no máximo 1×/s):

- `phys_cfg` é refeito com `[fisica]`;
- `dist_cfg` é refeito com `[distribuidor]` — só se o carro tem distribuidor instalado
  (não força peça em quem não comprou);
- `desgaste` do platinado não é tocado (é estado, não configuração).

Depois disso o `cfg_stamp` acompanha o `rev`, então rodar o jogo de novo não reaplica nada.
Ajustes feitos no F6 continuam valendo: o F6 não escreve no ini, logo não muda o `rev`.
Quando você quiser que o ini volte a mandar, basta um `--phys`/`--mod-set` (qualquer
chave), que gera um `rev` novo.

## 10. Verificação

`mcml-fixer --mod-check` abre o `game.unx` com o UTMT e procura marcadores no
decompilado. Os 25 atuais:

```
versao=v2.1-fisica  item  limites  dano  potencia  ui
loja  shop_tile  shop_preco  shop_entrega  shop_instala
carb  carb_hook  torque  potencia_real  pot_npc
motor_upg  motor_hook  cabecote  maxacc
phys_base  phys_gear  phys_npc  phys_inercia  phys_cfg
```

`carb` confere as funções `mod_carb_cfg`/`mod_carb_desvio`; `carb_hook` confere
a injeção `_mcarb` no Step; `torque` confere
`global.torque *= global.mod_dist_fator()` **e**
`global.ignicao = global.mod_dist_estado.avanco`.
