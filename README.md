# mcml-fixer

CLI + GUI em Rust (egui/eframe) que faz **My Car My Life** (GameMaker Studio
1.4, YoYo Linux Runner V1.3) rodar direito em Linux moderno: launch, resolução,
cursor e a UI do PC simulado dentro do jogo — e que instala o **mod
Distribuidor + Carburador** dentro do `game.unx`.

Tudo é aplicável e revertível, sempre com backup, e sem alterar nada fora da
pasta do jogo.

## Os seis bugs que isto conserta

| # | Sintoma | Causa raiz | Correção |
|---|---|---|---|
| 1 | o jogo não abre | libs de ABI antiga (`OpenSSL 1.0`, `libcurl-gnutls`) ausentes no sistema | copia 8 `.so` do Steam Runtime (o binário tem `RPATH=$ORIGIN`) |
| 2 | imagem esticada, input deslocado | janela vem do `GEN8` do `game.unx`, device vem do `.data` do runner — valores independentes | `--native-res` grava **os dois** |
| 3 | clique ~20 px fora do cursor | hotspot do `spr_mouse` no centro (256,256), ponta da chave em (332,1) | origin → (332,1) |
| 4 | cursor do sistema em vez do cursor do jogo | `obj_mouse` nunca era instanciado em nenhuma room | `Persistent` + `instance_create_depth` |
| 5 | barra de tarefas do PC não responde | testa clique em GUI mas desenha em room; a GUI herdava o tamanho da **janela** (1280x1024) e não da view (1280x720) | `display_set_gui_size(1280,720)` |
| 6 | barra e botões do app XLO não aparecem | o passe CRT recorta a `application_surface` com coordenadas fixas 1280x720, mas a surface é 1280x1024 — recorta a metade de cima e redesenha por cima | escala a origem do recorte pela razão surface/design |

O detalhamento de cada um — sintoma, evidência medida, causa raiz, correção e
verificação — está em **[`docs/BUGS.md`](docs/BUGS.md)**.

## Documentação

- [`docs/BUGS.md`](docs/BUGS.md) — catálogo dos bugs, com as contas que explicam
  cada um e duas armadilhas de processo (GML de escopo de evento e o checksum do
  GEN8 no UTMT).
- [`docs/SOURCE.md`](docs/SOURCE.md) — passeio pela source inteira: constantes,
  cada função, os scripts CSX embutidos, fluxo de dados, GUI e artefatos em
  runtime.
- [`docs/MOD.md`](docs/MOD.md) — o mod Distribuidor + Carburador: onde ele se
  pendura no código nativo, as fórmulas de avanço e de mistura, o menu, a
  integração no TOTORTO e a regra de sandbox que decide onde a arte pode ficar.

## Mod Distribuidor + Carburador

Mod de conteúdo aplicado **dentro** do `game.unx`, com backup e marcador:

```bash
mcml-fixer --mod-install                 # aplica (backup automatico)
mcml-fixer --mod-check                   # 14 marcadores dentro do decompilado
mcml-fixer --mod-status                  # estado + caminho do ini
mcml-fixer --mod-set ganho=100 dwell=48  # grava defaults para carros novos
mcml-fixer --mod-uninstall               # restaura o backup
```

### Distribuidor

Peça real do carro (`car.distribuidor` 0/1/2) com física de avanço:

```
avanço = inicial + centrífugo(rpm, 2 estágios) + vácuo(MAP, 5–15 inHg)
```

O bônus segue a mesma curva por partes que o jogo usa na ignição dos carros
com ECU, e é aplicado **no mesmo lugar**:

```gml
if (global.mod_dist_ativo) {
    global.torque *= global.mod_dist_fator();          // igual ao nativo
    global.ignicao = global.mod_dist_estado.avanco;    // ignição de verdade
}
```

Ou seja: mexe em torque (aceleração, dinamômetro, HUD) e na ignição, não só num
número de tela. Dois modelos com limites próprios:

| | Original | Competição |
|---|---|---|
| avanço inicial | 0–12° | 0–20° |
| centrífugo | 0–20° | 0–30° |
| rpm de fim | 5500 | 7500 |
| vácuo | 0–14° | 0–20° |
| dwell | 38–52 | 30–60 |
| ganho máximo | 45% | 100% |
| preço | $800 (reposição) | $12.000 |

Todo motor **carburado** sai de fábrica com o Original; injetado não recebe.

### Carburador

O jogo já tinha `gicle`, `gicle_2nd` e oito tipos de carburador. O mod soma os
circuitos que faltavam **na linha do desvio de mistura**, então tudo que o
jogo já faz (hp, torque, aquecimento por mistura rica, corte por mistura
pobre) passa a reagir:

| Parâmetro | Faixa | Onde age |
|---|---|---|
| Giclê de lenta | 20–60 | abaixo de 8% do giro |
| Agulha (média) | 20–80 | pico em 45% do giro |
| Borboleta | 0–100 | saída / baixa |

Efeito medido nas fórmulas nativas (gasolina, carburador tipo 5):

```
cenario                   AFR     hp    torque
neutro, 3000rpm         13.52  1.039  0.722
agulha +10              12.14  1.146  0.796   <- melhor
agulha +20              10.76  1.011  0.702   <- passou, afogou
agulha -10              14.90  0.904  0.628
lenta +10                9.95  0.927  0.644
lenta -10               17.38  0.671  0.466
```

### Potência real (torque × rpm)

`hp` deixou de ser uma curva escrita à mão: agora é derivado do torque
instantâneo e do giro, `P = T · ω`, e é isso que o jogo usa para acelerar —
no jogador e nos cinco objetos de IA (trânsito, touge, oponentes, NPC).

```gml
hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.2);
```

A constante é calibrada por motor para o pico de `torque·rpm` cair exatamente
no `maxhp` declarado (medido em 45, 99 e 171 hp). Efeito prático: a curva fica
pontuda como motor de verdade — mais fraca embaixo, igual no pico e mais forte
no corte.

```
rpm      45 hp   99 hp   171 hp
1500       9,2    18,6     45,6
3000      32,2    64,5    134,7
4200      44,9    98,3    170,5   <- pico
5500      37,4    91,1    142,0
```

Como o torque já carrega carburador, distribuidor e mods de motor, a potência
acompanha tudo sem código extra.

### Menu

`F6` abre · `TAB` cicla Ajuste → Loja → Carburador · `↑↓` escolhe · `←→`
ajusta · `1`/`2` compram · `3` volta ao original · `F7` padrão.

Cada parâmetro traz duas linhas de explicação e, no distribuidor,
**mín / atual / máx** calculados rodando o modelo com o parâmetro nos extremos.

A loja fica no **TOTORTO**, categoria própria no fim da lista (índice 18), com
os dois itens e entrega normal do jogo. O detalhe completo está em
[`docs/MOD.md`](docs/MOD.md).

## Como funciona por dentro

Os patches que não são bytes (sprite, flag de objeto, código GML) passam pelo
**UndertaleModCli**, que edita o `game.unx` com o serializador oficial do
formato — nada de byte-patch de bytecode. Dois scripts CSX ficam embutidos no
binário: `PATCH_CSX` (aplica) e `CHECK_CSX` (só lê e confere).

O `--data-fix` sempre parte de `assets/game.unx.orig`, porque o UTMT recusa
arquivo com o `GEN8` alterado (o checksum do chunk usa
`DefaultWindowWidth/Height`).

## Compilar

```bash
cargo build --release
# binário: target/release/mcml-fixer
```

## GUI

```bash
mcml-fixer
```

- Detecta a instalação do Steam automaticamente (ou use "Detectar").
- Status ao vivo: binário, `ldd` sem `not found`, GEN8, canvas, Steam Runtime,
  gamescope.
- Botões: **Corrigir libs**, **Reverter libs**, **Salvar escolha**, **Jogar**,
  **Aplicar cursor + GUI**, além do patch estático opcional do GEN8.

## CLI (mesmo binário, sem GUI)

```bash
mcml-fixer --check                  # status completo
mcml-fixer --fix-libs               # aplica o fix de launch
mcml-fixer --revert-libs            # remove as libs copiadas
mcml-fixer --native-res 1280x1024   # RESOLUÇÃO NATIVA fixa (binário + GEN8)
mcml-fixer --revert-native          # restaura binário e game.unx
mcml-fixer --data-fix               # cursor (hotspot) + cursor do jogo + GUI 1280x720
mcml-fixer --data-revert            # volta o game.unx de antes do --data-fix
mcml-fixer --data-check             # confere os patches dentro do game.unx (lento)
mcml-fixer --patch-gen8 1280x1024   # patch só no GEN8 (cria game.unx.orig)
mcml-fixer --revert-gen8            # restaura o game.unx
mcml-fixer --play 1920x1080         # inicia via gamescope
mcml-fixer --play --no-gamescope    # inicia direto
mcml-fixer --mod-install            # instala o mod Distribuidor + Carburador
mcml-fixer --mod-check              # confere o mod dentro do game.unx
mcml-fixer --mod-status             # estado do mod + caminho do ini
mcml-fixer --mod-set k=v            # defaults do mod (carros novos)
mcml-fixer --mod-uninstall          # remove o mod (restaura backup)
mcml-fixer --dir "/caminho/do/jogo" --check
```

## Patches no `game.unx` (cursor + GUI do PC)

Três correções que **não** são byte-patch, porque mexem em sprite, flag de
objeto e código GML. Passam pelo `UndertaleModCli`:

| patch | o que faz | por quê |
|---|---|---|
| `spr_mouse` origin → (332,1) | hotspot na ponta da chave | o original era o centro (256,256); a ponta desenhada ficava ~20 px acima do clique |
| `obj_mouse.Persistent = true` + `instance_create_depth(...)` na room Languages | ativa o cursor próprio do jogo | o objeto existia mas nunca era instanciado, então o jogo usava o cursor do sistema |
| `display_set_gui_size(1280, 720)` na room Start | GUI no tamanho da view/port | o código do PC testa clique com `device_mouse_*_to_gui()` mas desenha em coordenadas de room; com a GUI herdando o tamanho da janela, a barra de tarefas respondia ~230 px acima do ícone |
| recorte do passe CRT em `obj_crt_Draw_77` | amostra a `application_surface` com a mesma escala do destino | o código usava 333/109/940/581 fixos (espaço 1280x720) numa surface de 1280x1024: recortava só a metade de cima e redesenha por cima, apagando a barra de tarefas e o rodapé das janelas do PC (botões do XLO) |

O `--data-fix` sempre parte de `assets/game.unx.orig` (o UTMT recusa arquivo com
GEN8 alterado), guarda o estado anterior em `assets/game.unx.before-datafix.bak`
e reaplica a resolução nativa no fim.

Requer o `UndertaleModCli` em `~/.local/share/mcml-fixer/utmt/` (ou aponte com
`MCML_UTMT=/caminho`). É opcional: sem ele, o resto do fixer funciona igual.

## Resolução nativa fixa — como foi descoberta

O jogo **não** define 1024x768 em lugar nenhum dos dados:

| Onde procurei | Resultado |
|---|---|
| `GEN8` | 1280x720 (só a janela) |
| `OPTN` | sem resolução/fullscreen |
| `ROOM` (32 rooms) | 640x360 … 2560x720 — **nenhum** 1024x768 |
| `CODE` (bytecode) | 0 pares de constantes 1024/768, 0 doubles, 0 floats |

Ou seja: 1024x768 é **default do runner**, um inicializador estático em `.data`.

Confirmado com gdb:

```
(gdb) watch *(int*)0xf422f8
Old value = 1024
New value = 1280
```

`vaddr 0xf422f8` → file offset **0x9422f8** (`.data` em vaddr 0xedb990 / file 0x8db990).
Lá ficam os dois u32 `(device_w, device_h)` usados por `CreateDesktopWindow` e
pelo `window_set_size` de boot (`Setting dimensions to %d by %d`).

O patch grava `(w,h)` nesse offset **e** no `GEN8` (senão a janela nasce menor
que o device). Resultado verificado em 1280x1024:

```
Display Size(Pixels): 1280,1024
Setting dimensions to 1280 by 1024
DeviceWidth/h 1280 by 1024
ClientWidth/h 1280 by 1024
RealDeviceWidth/h 1280 by 1024
Window Size w=1280 h=1024
Entering main loop.
```

## Como o fix de resolução funciona

```
gamescope -w 1024 -h 768 -W <SAIDA_W> -H <SAIDA_H> -f -- ./My_Car_My_Life
        └── canvas (input 1:1) ──┘ └── modo EDID escolhido ──┘
```

Modos EDID são lidos de `/sys/class/drm/card*-*/modes` (não do `xrandr`, que
no XWayland só mostra o modo virtual).

## Detalhes de implementação

- `ldd` para detectar dependências não resolvidas.
- `fs::copy` segue symlinks, então as libs do Steam Runtime são copiadas como
  arquivos reais (nunca como links quebrados).
- Config da resolução em `~/.config/mcml-res.json`.
- Log da última execução do jogo em `~/.config/mcml-fixer-launch.log`.
- O jogo é lançado com `setsid`, para continuar vivo depois que o fixer sai.
- `SIGPIPE` restaurado no modo CLI para não entrar em panic com `| head`.

## Limitações

- O canvas do jogo é fixo (definido pelos rooms no `game.unx`). "Mudar a
  resolução" significa mudar a resolução de saída; o jogo é escalado.
- O patch GEN8 muda só a janela criada no boot — não conserta input sozinho.
- Sem gamescope, o bug de clique volta quando o display difere do canvas.

## Licença

MIT — veja [LICENSE](LICENSE). Sem afiliação com os autores do jogo; a
ferramenta não distribui nenhum arquivo do jogo.
