# Catálogo de bugs — My Car My Life no Linux

Documento técnico dos dez defeitos encontrados no jogo (seis de plataforma e
quatro que apareceram ao construir o mod Distribuidor + Carburador) e das duas
armadilhas de processo que surgiram durante o desenvolvimento do `mcml-fixer`.

Cada item traz: **sintoma** (o que o jogador via), **evidência** (como foi medido),
**causa raiz** (o que no jogo/binário causava), **correção** e **verificação**.

Ambiente de referência: Manjaro/Arch, KDE Plasma Wayland, monitor 1280x1024,
gamescope 3.16. Jogo: GameMaker Studio 1.4, YoYo Linux Runner V1.3, ELF x86-64.

---

## BUG 1 — O jogo não inicia

**Sintoma.** Duplo clique (ou `steam`/`./My_Car_My_Life`) não abre nada. Nenhuma
janela, nenhuma mensagem.

**Evidência.**

```
$ ldd My_Car_My_Life | grep "not found"
    libcrypto.so.1.0.0 => not found
    libssl.so.1.0.0 => not found
    libcurl-gnutls.so.4 => not found
```

**Causa raiz.** O jogo é de 2016 e foi linkado contra **OpenSSL 1.0** e
`libcurl-gnutls` da época. Distros atuais (Arch/Manjaro) só têm OpenSSL 3, e os
sonames antigos não existem. O loader aborta antes de qualquer código do jogo
rodar, por isso não há nem mensagem.

**Correção.** Copiar 8 bibliotecas do **Steam Runtime** (`ubuntu12_32/steam-runtime`,
variante x86_64) para a própria pasta do jogo. O binário tem `RPATH=$ORIGIN`, então
ele resolve as libs localmente e **nada no sistema é alterado**.

```
libcrypto.so.1.0.0   libssl.so.1.0.0     libcurl-gnutls.so.4
libidn.so.11         libgcrypt.so.11     librtmp.so.0
libnettle.so.6       libhogweed.so.4
```

**Verificação.** `ldd` sem nenhum `not found`; o log do runner chega a
`Entering main loop.`

**Onde.** `fix_libs()` / `revert_libs()` / `missing_libs()`; CLI `--fix-libs`.

---

## BUG 2 — Resolução inconsistente (janela ≠ device ≠ display)

**Sintoma.** Imagem esticada/borrada e cursor desalinhado. Dependendo de como o
jogo era iniciado, a janela nascia num tamanho e o input era calculado em outro.

**Evidência.** Log do runner mostrando três tamanhos diferentes ao mesmo tempo:

```
Display Size 1280,1024
Setting dimensions to 1280 by 720      <- janela
DeviceWidth/h 1024 by 768              <- device (input)
ClientWidth/h 1280 by 1024
```

**Causa raiz.** Dois valores independentes, em dois lugares diferentes:

| valor | onde |
|---|---|
| tamanho de janela (1280x720) | chunk `GEN8` do `game.unx`, offsets `+0x3c` / `+0x40` |
| device do runner (1024x768) | **`.data` do binário**, vaddr `0xf422f8` → file offset `0x9422f8` |

O segundo foi localizado com watchpoint (`watch *(int*)0xf422f8` → `Old value = 1024`),
ou seja é inicializador estático em `.data`, não cálculo em runtime. Patchar só o
GEN8 muda a janela e deixa o device dessincronizado — pior que o original.

**Correção.** `--native-res WxH` grava **os dois**: o par `(w,h)` em `0x9422f8`
(com backup `My_Car_My_Life.orig`) e o GEN8. Depois disso
janela == device == display, input 1:1, sem escala.

**Verificação.**

```
Display Size 1280,1024 / DeviceWidth/h 1280 by 1024
RealDeviceWidth/h 1280 by 1024 / Window Size w=1280 h=1024
Entering main loop.
```

**Onde.** `patch_native()` / `revert_native()` / `read_native()` / `patch_gen8()`;
CLI `--native-res`, `--revert-native`, `--patch-gen8`, `--revert-gen8`.

---

## BUG 3 — Cursor impreciso (~20 px)

**Sintoma.** O clique acontece acima/esquerda de onde a ponta da chave está
desenhada. Erro de ~20 px, constante.

**Evidência.** `spr_mouse` é 512x512 com origin **(256,256)** — o centro. A ponta
da chave, no bitmap, fica em **(332,1)**. O objeto desenha com
`image_xscale = -0.08` / `image_yscale = 0.08` (espelhado e reduzido), então o
erro em tela é proporcional à distância entre origem e ponta.

**Causa raiz.** O hotspot do sprite está no centro do bitmap, não na ponta do
desenho. O GameMaker usa a origin como ponto de clique.

**Correção.** `spr_mouse.OriginX = 332; OriginY = 1;` via UTMT.

**Verificação.** Hover das bandeiras do menu acende exatamente o item apontado
(medido por brilho: 144,8 no item 0 contra ~93 nos vizinhos).

**Onde.** `PATCH_CSX` (`spr.OriginX/OriginY`); marcador `spr_mouse_origin=332,1`.

---

## BUG 4 — O cursor do jogo nunca aparece (usa o do sistema)

**Sintoma.** O jogo desenha a chave? Não — o ponteiro visível é o do sistema
operacional, que não combina com a arte do jogo.

**Evidência.** `obj_mouse` existe nos assets, tem sprite, tem código de desenho,
mas **não está em nenhuma room** e **nenhuma entrada de código** faz
`instance_create*` dele. Confirmado por varredura das 5498 entradas decompiladas.

**Causa raiz.** Objeto órfão — provavelmente era criado por um sistema que foi
removido, ou o autor esquecia de colocá-lo na room.

**Correção.** `obj_mouse.Persistent = true` (para sobreviver à troca de rooms) +
`instance_create_depth(0, 0, -9999, obj_mouse)` no RoomCC da room `Languages`
(primeira room que sempre roda).

**Verificação.** Marcador `obj_mouse_persistent=True` + `cursor_instance=set`; a
chave aparece no lugar do ponteiro do sistema.

**Onde.** `PATCH_CSX` (`obj.Persistent`, `QueueAppend` em
`gml_RoomCC_Languages_0_Create`).

---

## BUG 5 — Barra de tarefas do PC não responde ao clique

**Sintoma.** Dentro do PC simulado, clicar no ícone da barra de tarefas não faz
nada, e clicar ~230 px acima do ícone funciona. O hover "acende" no lugar errado.

**Evidência.** Instrumentação gravada de dentro do GML (o jogo escreve um arquivo
em `~/.config/My_Car_My_Life/`):

```
antes:  GUI_my=534  ROOM_my=375  GUISIZE=1280x1024  WIN=1280x1024
depois: GUI_my=554  ROOM_my=554  GUISIZE=1280x720   WIN=1280x1024
```

`obj_pc_tb` / `obj_pc_icon` testam clique com `device_mouse_x_to_gui()` /
`device_mouse_y_to_gui()` mas **desenham em coordenadas de room**. Isso só
funciona se `display_get_gui_width/height()` == tamanho da view/port (1280x720).

**Causa raiz.** O GameMaker **não** define o tamanho da GUI pelo tamanho da view:
por padrão a GUI tem o tamanho da **janela**. Como a janela virou 1280x1024
(BUG 2) e a view continua 1280x720, todo `device_mouse_*_to_gui()` devolvia
coordenadas numa escala 1.422x maior na vertical — ~230 px de erro.

Nenhuma entrada de código do jogo chama `display_set_gui_size()` (confirmado por
grep nas 5498 entradas), então o valor ficava sempre no default.

**Correção.** `display_set_gui_size(1280, 720);` no RoomCC da room `Start`.

**Verificação.** `GUI_my` passa a bater com `ROOM_my` (554 == 554). São **33
arquivos** do jogo que usam `device_mouse_*_to_gui()` — todos passam a bater de
uma vez, não só a barra.

**Onde.** `PATCH_CSX` (`QueueAppend` em `gml_RoomCC_Start_0_Create`); marcador
`gui_fix=set`.

---

## BUG 6 — Barra de tarefas e botões do app XLO não aparecem (efeito CRT)

**Sintoma.** Com o efeito CRT ligado (padrão do jogo), a barra de tarefas, o
relógio e o rodapé das janelas do PC — incluindo o botão **COMPRAR** do app
"XLO" — simplesmente não são desenhados. Com o efeito desligado tudo aparece.

**Evidência.** Primeiro: desligar o passe CRT na room `Computer` deixa a tela
**preta** — prova de que, com `global.balacobaco == 1`, a `application_surface`
só é desenhada por esse passe. Depois, medição em runtime: `app=1280x1024`.

**Causa raiz.** `obj_crt_Draw_77` (Post Draw) recorta a `application_surface`
com coordenadas **fixas** de espaço 1280x720:

```gml
var base_w = 1280; var base_h = 720;
var x1 = 333; var y1 = 109; var x2 = 940; var y2 = 581;
draw_surface_part(application_surface, x1, y1, surf_w, surf_h, 0, 0);
```

Mas a surface real tem **1280x1024** (o GameMaker dimensiona a
`application_surface` pelo tamanho da **janela**). Fazendo a conta:

- escala vertical da surface para a room: 1024 / 720 = 1.4222
- recorte = linhas 109..581 da surface = **room 76..408**

Esse pedaço é redesenhado **por cima** de tudo. Como nada mais desenha a surface,
tudo abaixo de room y≈408 dentro de x 333..940 desaparece:

- barra de tarefas (room y 543..580) e relógio;
- rodapé das janelas do PC — o botão COMPRAR do XLO fica nessa faixa.

A prova de que é descuido: a **outra** ramificação do mesmo evento
(`room != Computer`) usa `surface_get_width/height(application_surface)`.
O autor fez certo num lado e chumbou o número no outro.

**Correção.** Amostrar a origem com a mesma escala do destino:

```gml
var _kx = surface_get_width(application_surface) / base_w;
var _ky = surface_get_height(application_surface) / base_h;
var _sw = surf_w * _kx;
var _sh = surf_h * _ky;
...
draw_surface_part(application_surface, x1 * _kx, y1 * _ky, _sw, _sh, 0, 0);
```

**Verificação.** Com o CRT ligado: barra de tarefas, botão Iniciar, relógio e o
botão COMPRAR do XLO voltam. Marcador `crt_scale=fixed`.

**Onde.** `PATCH_CSX` (bloco único de `gml_Object_obj_crt_Draw_77`).

---

## BUG 7 — Sprite externo do mod não carrega (e derruba o jogo)

**Sintoma.** No catálogo de peças externas do TOTORTO o ícone do distribuidor
não aparecia e o fallback do carburador saía desalinhado. Em outra tentativa, o
jogo abortava com:

```
draw sprite ext argument 1 invalid reference to (sprite) - requested -1 max is 889
```

**Evidência.** Instrumentando `obj_crt_Draw_77` (roda na tela de idiomas, sem
precisar de input) para escrever um arquivo no save:

```
wd=/home/.../My Car My Life/assets/      gs=/home/gullin/.config/My_Car_My_Life//
file_exists("diag.txt")                              -> 1
file_exists("assets/Mod_assets/x.png")               -> 0
file_exists("/caminho/absoluto/x.png")               -> 0
sprite_add("distribuidor_icon.png")  (raiz do save)  -> sprite 1625, 256x256
```

**Causa raiz.** Duas coisas somadas:

1. este runner é **sandboxed**: `file_exists` e `sprite_add` só enxergam
   arquivos na **raiz** da pasta de save — subpasta e caminho absoluto
   retornam `-1`/`0`;
2. `working_directory` aponta para `.../assets/`, não para a raiz do jogo, então
   `working_directory + "assets/Mod_assets/..."` virava `assets/assets/...`.

Com o `sprite_add` devolvendo `-1`, o desenho passava `-1` para
`draw_sprite_ext` e o runner abortava.

**Correção.** O instalador copia `distribuidor.png` e `distribuidor_icon.png`
para `~/.config/My_Car_My_Life/` e o mod carrega
`sprite_add("distribuidor_icon.png")`. Todo desenho passa por
`mod_dist_draw_icon()`, que centraliza por altura e, sem arte, desenha um
marcador alinhado — nunca um índice inválido.

**Verificação.** `file_exists` e `sprite_add` retornando 1 e sprite válido no
diag; boot sem `ERROR in`; 14 marcadores verdes.

---

## BUG 8 — O menu do mod (F6) não abria

**Sintoma.** F6 não fazia nada em carro carburado.

**Causa raiz.** A UI estava atrás de `if (global.mod_dist_ativo || menu == 1)`,
e `mod_dist_ativo` só virava 1 quando o carro **já tinha** distribuidor — ou
seja, era impossível comprar o primeiro.

**Correção.** `mod_dist_pode()` (carro existe e `carburador > 0`) decide a UI, e
o hook chama a função sempre; `mod_dist_ativo` é recalculado dentro dela.

---

## BUG 9 — UI do mod piscando

**Sintoma.** O painel aparecia e desaparecia a cada frame.

**Causa raiz.** O desenho estava no `Draw_0` (espaço de sala), e o passe CRT
(`obj_crt_Draw_77`, depth 400) redesenha a `application_surface` por cima no
mesmo frame — o painel era sobrescrito.

**Correção.** Desenhar **só** no Draw GUI (`obj_noguiin_Draw_64`), um ponto
único por frame, sem token de frame.

---

## BUG 10 — O distribuidor rendia pouco (bônus limitado duas vezes)

**Sintoma.** Ajustar o distribuidor mudava quase nada no carro.

**Evidência.** Rodando o modelo com os valores padrão:

```
Original, 3000 rpm: bônus bruto +56,4%  ->  aplicado +25,4%
```

**Causa raiz.** O parâmetro `ganho` era multiplicado **e** limitado ao teto do
modelo: `bonus *= min(ganho, lim[7]) / 100` com `ganho = 50` e `lim[7] = 45`
para o Original. Resultado: 50% × 45% = 22,5% de bônus máximo. Além disso o
fator de faísca `(0,94 + 0,08·q)` penalizava mesmo com `q = 1`.

**Correção.** O teto é o do modelo (Original 45%, Competição 100%) e `ganho`
regula quanto dele é usado (padrão 100); o fator de faísca só penaliza quando
`q < 1`. Config antiga migra uma vez (`ver: 2`).

**Verificação.** Mesmo cenário passa a +45,0% (Original) e +62,1%
(Competição), comparável ao nativo (−25% a +60% em `global.torque`).

---

## BUG 11 — Upgrades sem consequência (e um que atrapalhava)

**Sintoma.** Instalar cabeçote, filtro, escape, admissão, bloco, pistão ou virabrequim não
mudava a força do carro; mexer no carburador mudava o AFR no HUD, mas não a aceleração.

**Evidência (auditoria no decompilado).**

- `hp`/`torque` são recalculados do zero a cada frame (linha 864). Tudo que multiplica
  esses globais **depois** disso é descartado antes da física usar:
  - `_carb_mult` em 3924–3925 (carburador);
  - `mult_pistao` 1726–1727, `mult_cmd` 1767, `mult_vira` 1817 (desgaste).
- `cabecote` só aparecia em `_get_damage_multiplier`; nada mudava `global.valvulas`.
- `filtro`, `pipe`, `intercooler`, `embreagem`, `transmissao` e `vira` só apareciam no
  bloco de `_mod_points`; `escape` não aparecia em lugar nenhum fora do save.
- `_mod_points > 5` num aspirado trocava o teto de aceleração de **21 para 12** — instalar
  peça deixava o carro mais lento.

**Correção.** Um compositor único (`global.motor_torque`) aplicado na derivação de `hp`,
antes da física, absorvendo peças, boost, mistura (carburador **ou** injeção), ignição e
desgaste; `cabecote` passou a mudar as válvulas; os multiplicadores tardios foram
removidos; e `_max_acc = 12` virou 21.

**Verificação.** `mcml-fixer --mod-check` → `motor_upg`, `motor_hook`, `cabecote`,
`maxacc`; matriz `mod/phys_matrix.csx` com as razões de `docs/MOD.md`.

---

## BUG 12 — O ini do mod nunca era lido

**Sintoma.** `--mod-set ganho=80` gravava o arquivo, mas o jogo ignorava.

**Causa.** `global.mod_dist_carregar` era definida no `Start_0_Create` e **nunca chamada**.
Nenhuma chave do ini chegava ao jogo — nem as do distribuidor, nem as da física.

**Correção.** A função é chamada no fim do bloco de init (`global.mod_dist_carregar();`) e
a linha `global.mod_dist_ativo = ...` saiu dela (o gate é do `mod_dist_pode`, em runtime —
no boot ainda não existe carro). A leitura da seção `[fisica]` entrou junto.

**Verificação.** Harness `mod/phys_matrix.csx` imprime `global.phys_def`:
com `--phys raio=0.33 mu=0.85` o jogo lê `ini_raio|0.33` e `ini_mu|0.85`.

---

## BUG 13 — `file_datetime` não existe no runner 1.4

**Sintoma.** Aplicar o ini ao carro salvo abortava com
`Variable obj_start_car.file_datetime not set before reading it`.

**Causa.** O plano era comparar a data de modificação do ini com a última aplicada, mas
`file_datetime` é de versões posteriores do GameMaker; o runner 1.4 não a expõe.

**Correção.** O CLI passou a gravar `[mod] rev=<epoch>` no ini a cada escrita e o GML
compara `global.mod_rev` (lido no boot) com `car.cfg_stamp`.

**Verificação.** Teste em runtime com carro simulado: `ret=1 raio=0.35 mu=0.90 ganho=77
stamp=1792693693` e, na chamada seguinte, `ret2=0` (idempotente).

---

## O que foi investigado e **não** é bug

Registrado para não ser reaberto:

- **`ui_scale_x = win_w / 1280` / `ui_scale_y = win_h / 720`** (11 ocorrências em
  `obj_pc_window`): é escala de **conteúdo da janela**. O conteúdo das janelas é
  autorado em 1280x720 e reduzido para o tamanho da janela. Correto.
- **`global.transition_radius = 720`** (espalhado por vários objetos): raio do
  efeito de transição, sem relação com resolução.
- **Mapeamento do mouse no menu**: sempre esteve certo — hover das bandeiras
  acende exatamente o item apontado.

---

## ARMADILHA 1 (processo) — `QueueFindReplace` que pega as duas ramificações

**Sintoma.** Depois de aplicar o fix do BUG 6, o jogo subia e logo morria com:

```
ERROR in action number 1
of Draw Event for object obj_crt:
local variable base_w(107360) not set before reading it.
at gml_Object_obj_crt_Draw_77
```

**Causa.** A primeira versão do patch trocava linhas isoladas
(`var surf_w = x2 - x1;`, `draw_surface_part(...)`) que aparecem **nas duas
ramificações** do evento. A ramificação `room != Computer` não declara
`base_w`/`base_h` → referência a variável local inexistente.

**Correção.** Trocar um **bloco único**, ancorado nas coordenadas que só existem
na ramificação certa (`var x1 = 333; ... var y2 = 581;`). Ainda assim, validar
sempre com `--data-check` **e** rodando o jogo em rooms diferentes (o erro só
aparecia fora da room `Computer`).

**Lição.** Em GML, `var` é escopo de evento: um replace que atinge dois blocos
parecidos pode compilar e quebrar só em runtime, em outro contexto.

---

## ARMADILHA 2 (processo) — UTMT recusa arquivo com GEN8 alterado

**Sintoma.** `UndertaleModCli load game.unx` falha com
`Unexpected random UID info`.

**Causa.** O checksum/UID do chunk `GEN8` é calculado sobre
`DefaultWindowWidth/Height`. Qualquer patch de GEN8 feito direto no arquivo
invalida o checksum e o UndertaleModTool recusa abrir.

**Correção.** Sempre operar a partir de `assets/game.unx.orig` (intacto) e
reaplicar o GEN8 **depois** do UTMT — é exatamente o que `--data-fix` faz:
patches no pristine → copia por cima → `patch_native()` de novo.

Para conferir um arquivo já patchado, `--data-check` copia para um temporário e
**restaura o GEN8 original** antes de abrir.

**Efeito colateral.** `UMT_DUMP_ALL` (dump de código) também aborta
silenciosamente em ~1043 entradas e perde as filhas (`gml_Script_*`); foi
necessário um CSX próprio com `try/catch` para extrair as 5498 entradas.

---

## Resumo

| # | Bug | Camada | Correção |
|---|---|---|---|
| 1 | não inicia | loader / libs | copiar 8 libs do Steam Runtime |
| 2 | resolução inconsistente | `.data` + GEN8 | patchar device **e** GEN8 |
| 3 | cursor ~20 px | sprite | origin (332,1) |
| 4 | cursor do sistema | objeto órfão | `Persistent` + `instance_create_depth` |
| 5 | barra não clica | GUI ≠ view | `display_set_gui_size(1280,720)` |
| 6 | barra/botões somem | passe CRT | escala da origem do recorte |
| 7 | sprite do mod não carrega | sandbox do runner | arte na raiz do save + fallback alinhado |
| 8 | F6 não abria | gate em `mod_dist_ativo` | `mod_dist_pode()` |
| 9 | UI piscando | draw de sala + CRT | desenhar no Draw GUI |
| 10 | distribuidor rendia pouco | `ganho` limitado duas vezes | teto do modelo + `ganho` como fração |
| 11 | upgrades sem consequência | multiplicadores aplicados após o recálculo | compositor único `motor_torque` |
| 12 | ini do mod ignorado | `mod_dist_carregar` nunca chamada | chamada no init + seção `[fisica]` |

Todos aplicáveis/revertíveis por `mcml-fixer`, sempre a partir do
`game.unx.orig`, com backup e verificação.

## BUG 14 — F6 morto em carro carburado (distribuidor gravado como 0)

**Sintoma:** carro com carburador, mas o menu F6 nao abre ("este motor e injetado").

**Causa:** `carburador = -1` e o valor de fabrica (`EstruturaSalvamento.gml:129`). O jogo
so resolve para `1` (4 cil) ou `3` (6 cil) dentro da **Garage**, so quando `bicos == 0`
e so para 15 motores de uma lista branca (`obj_player Step:78-93`).

O mod lia o carro em qualquer sala (via `mod_cfg_aplicar` no Draw GUI), via `-1 <= 0`,
concluia "injetado" e **gravava `car.distribuidor = 0`** — que vai para o save. Quando o
jogo finalmente resolvia o carburador, `distribuidor` ja estava 0 para sempre, e
`mod_dist_pode()` desligava a UI inteira.

**Fix:** `mod_dist_tipo(car)` resolve `-1/0/1` num lugar so (bicos > 0 = injetado;
`carburador > 0` = carburado; `-1` = fail-open carburado). `mod_dist_modelo` nao grava
decisao desconhecida e auto-cura carro com `distribuidor == 0` + carburador.

**Teste:** `mod/carb_test.csx` (roda no `Start_0_Create`).
Antes: `tem_tipo=0 pode_-1=0 modelo_-1=0 dist=0`.
Depois: `tem_tipo=1 pode_-1=1 modelo_-1=1 dist=1 tipo_0=0 pode_0=0 cura=1 dist=1`.

## BUG 17 — item de câmbio novo não podia ser usado (sem `use_func`)

**Sintoma:** o câmbio de 7 ou 8 marchas aparece no inventário, o jogador clica em "use" e
nada acontece.

**Causa:** o helper `_r(...)` (`03949_Inventory.gml:12-26`) cria o item com 9 campos e
**sem `use_func`**. Os câmbios 1..7 ganham esse campo num laço explícito, no array
`_cambio_keys` (linha 521). `manual_7m` e `manual_8m` ficaram fora da lista, então
`inventory_use_item` (`:1098`) saía no guarda `if (!variable_struct_exists(data, "use_func"))`
— com um `show_debug_message` que ninguém vê.

**Fix:** os dois ids entram em `_cambio_keys`, que é o único lugar que atribui
`item_use_dinamometro` aos câmbios.

**Teste:** `mod/cambio_use_test.csx`.
Antes: `use6=1 use7=0 use8=0 clique7=0`. Depois: `use6=1 use7=1 use8=1 clique7=1`.

## BUG 18 — `_inventory_get_item_by_campo("cambio")` sempre vazio

**Causa:** os itens de câmbio não têm `campo_val`, então a função cai em
`_inventory_campo_value`, que devolve a **posição 0-based** do item na lista de campo
`"cambio"`; ela é comparada com `car.cambio`, que é **1-based** (1..9) e conta só os 7
câmbios originais. Nunca casa.

**Fix:** `campo_val` 1..9 nos 12 itens, via `struct_get_from_hash` + `variable_get_hash`
(a forma que o próprio jogo usa nas linhas 321-336).

**Nota:** o valor devolvido (`_cambio_atual` em `_dino_executar_swap`, linha 2985) é
atribuído e **não é usado** depois — é código morto do jogo. Ou seja, o `campo_val`
conserta a busca, mas **não** faz o câmbio antigo voltar ao inventário (isso teria que ser
implementado à parte).

**Teste:** com `car.cambio = 3`, a função devolve `manual_6m` (antes: vazio).

## BUG 19 — confirmar o câmbio sem dinheiro fechava o diálogo sem avisar

`05221_gml_Object_obj_noguiin_Draw_0.gml:8267` (teclado) e `:8315` (mouse):
`if (global.cash >= _custo_cambio) { ...grava... } else { global.tiao_confirmacao_cambio = 0; }`.
Uma caixa completa custa 775 × 9 = **$6.975**; sem saldo nada era gravado e nada era
dito. O jogador acreditava ter alterado.

**Correção (3.0.0):** `global.cg_confirmar()` avisa na tela (`FALTA DINHEIRO`), grava a
recusa em `cambio_save.txt` e existe `[dev] cambio_gratis=1` para testar sem saldo.

## BUG 20 — `nan` no `gears_mod` por divisão sem guarda

O patch 2.7 gravava `gears_mod[i] = temp * rel_ref / gear_rel_original`, e
`gear_rel_original` só existia se o bloco de inicialização do caderno tivesse rodado.
Com valor `0`/`undefined`, a divisão produzia `nan` → `gear_ratios` inválido → o carro
andava como se nada tivesse mudado.

**Correção (3.0.0):** não existe mais divisão; `gears_mod` guarda a razão efetiva e
todo número passa por `global.cg_num` (rejeita `nan`, `inf` e `undefined`).

## BUG 21 — `gears_mod` compartilhado com `gear_ratios`

`05317:702` fazia `gear_ratios = global.car_data[...].gears_mod`; o patch da relação
final multiplicava `gear_ratios` **no lugar**. Se a atribuição compartilhasse o array,
o save era multiplicado a cada criação do player (a cada troca de sala).

**Correção (3.0.0):** `cg_init_car` monta arrays novos (`array_create`) e nunca
devolve `car.gears_mod` por referência. Teste `clone` do `cg_test` cobre isso.

## BUG 22 — `cg_migrado` lido antes de existir (crash ao entrar na rua)

`global.cg_migrado` era lido em `cg_init_car` sem inicialização. O jogo só quebrava ao
**criar o player** (entrar na rua), não no boot — o `--mod-check` e o boot passavam.

**Correção (3.0.0):** `global.cg_migrado = 0;` no `GML_INIT`. Descoberto pelo harness
`mod/cg_create_test.csx`, que instancia `obj_player` e roda o Create de verdade.

**Lição:** ler campo opcional de struct (`car.cg_versao`, `car.cambio_mod`) sem
`variable_struct_exists` derruba o runner. Use sempre `global.cg_get(struct, chave, padrao)`.

## BUG 23 — o menu CÂMBIO do Tião desapareceu (regressão da 3.0.0)

`05221:6771`:

```gml
var tem_cambio = global.car_data[global.vehicles_array].cambio_mod == 1;
```

A opção **CÂMBIO** na oficina só existe quando `cambio_mod == 1` — e quem liga essa
flag é o item **"Forjar Cambio"** (`transmissao_desempenho`, `03996:3004`), que grava
`cambio_mod = 1; gears_mod = [];`.

A camada `cg` da 3.0.0 tratava "flag ligada + razões vazias" como estado inválido e
zerava `cambio_mod` no `cg_init_car`. Resultado: forjar o câmbio deixava de abrir o
editor, e o jogador ficava sem onde ajustar as marchas.

**Correção (3.0.1):** `cg_init_car` **nunca** escreve `cambio_mod = 0`. A flag é do
jogo, não da camada: ela significa "câmbio liberado para editar". Com razões vazias o
carro usa a tabela de fábrica e o menu continua aparecendo.

**Migração corrigida:** em vez de zerar, converte as razões do esquema antigo
(base → efetiva, `× rel/ref`) e mantém a flag.

**Provas:** `cg_test` casos `flag_edicao` e `migracao`;
`tools/verify_cambio.py` → `flag_edicao_preservada` (19 invariantes).

**Lição:** antes de "limpar" um estado que parece inconsistente, procure **quem lê**
esse estado (`tem_cambio`) — ele pode ser a chave de uma tela.

## BUG 24 — teto de 400 km/h nas colisões

Quatro scripts de colisão gravavam o teto **400** literal na velocidade do jogador:

| Arquivo | Linha |
|---|---|
| `05044_gml_Object_obj_ia_car_NPC_Collision_obj_player.gml` | 38 e 39 |
| `05332_gml_Object_obj_player_Collision_obj_ia_car_TOUGE.gml` | 31 e 37 |
| `05053_gml_Object_obj_ia_car_TOUGE_Collision_obj_player.gml` | 9 |
| `05043_gml_Object_obj_ia_car_NPC_Collision_obj_ia_car_NPC.gml` | 9 |

No `obj_player_Step_0` o teto já tinha virado `global.cambio_vmax` (500), mas estas
colisões continuavam clampando em 400 — e elas rodam justamente **na rodovia**, onde o
carro passa dos 400. Resultado: qualquer toque em carro de tráfego puxava o velocímetro
de volta para 400, dando a impressão de "limitador em 400" com o motor sobrando.

**Correção (3.0.2):** os quatro usam `global.cambio_vmax`. Provas: dump dos dois objetos
de colisão do jogador sem `, 400)` (`verify_cambio.py` → `sem_teto_400_colisao`, 20/20).
