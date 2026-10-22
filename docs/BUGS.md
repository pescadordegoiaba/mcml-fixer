# Catálogo de bugs — My Car My Life no Linux

Documento técnico dos seis defeitos encontrados no jogo e das duas armadilhas de
processo que apareceram durante o desenvolvimento do `mcml-fixer`.

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

Todos aplicáveis/revertíveis por `mcml-fixer`, sempre a partir do
`game.unx.orig`, com backup e verificação.
