# mcml-fixer

CLI e GUI em Rust (egui/eframe) para **My Car My Life** no Linux. O jogo é
GameMaker. O fixer faz duas coisas:

1. Abre o jogo em Linux moderno: bibliotecas, resolução, cursor e a interface
   do PC dentro do jogo.
2. Instala um mod dentro do `game.unx`: física longitudinal, câmbio,
   distribuidor, carburador, forja e a regra do Noguiintech.

Tudo que o fixer grava no jogo tem backup e desfaz. Nada é escrito fora da
pasta do jogo, do `~/.config` e do cache do UndertaleModTool.

O detalhe de cada solda no bytecode está em [`docs/MOD.md`](docs/MOD.md). A
fase 2 do combustível está em [`mod/MOD.md`](mod/MOD.md). Este README explica
o comportamento.

## Instalar e tirar o mod

O `--mod-install` sempre parte de `assets/game.unx.orig`. O UndertaleModTool
recusa um `game.unx` que já foi alterado. Se o mod já está instalado, o
comando recusa. A ordem para atualizar é desinstalar e instalar de novo.

```bash
cargo build --release
./target/release/mcml-fixer --mod-uninstall
./target/release/mcml-fixer --mod-install
./target/release/mcml-fixer --mod-check
```

Depois de editar `mod/gen_mod.py`, `mod/phys_v28.gml` ou `mod/fuel_model.gml`:

```bash
python3 mod/build.py && cargo build --release
```

`mod/build.py` gera `mod/mod_dist2.csx` e embute esse texto em `src/main.rs`.
Não edite o `.csx` nem o `main.rs` à mão.

Testes, sem o jogo:

```bash
python3 tools/verify_phys_v28.py    # rua ~500 hp, sem modelo de combustível
python3 tools/verify_fuel_model.py  # distribuidor, carburador e fase 2
```

O gate da rua é 60 pés entre 1,9 e 2,1 s, quarto de milha entre 11,5 e 12,0 s,
trap perto de 190 km/h. A última passagem ficou em 2,00 s, 11,51 s e 194 km/h.
Com o modelo de combustível ligado, o mesmo quarto ficou em 1,95 s e 11,66 s.

## O que o mod muda no carro

| Sistema | Onde mexe | O que o jogador vê |
|---|---|---|
| Física v2.8 | jogador e NPCs | aceleração, giro, pneu, arrasto e o topo da marcha |
| Câmbio `cg` | caderno do Tião | relações efetivas, relação final, ±70% |
| Forja | oficina do Tião | peças internas duram 1,7× |
| Distribuidor e carburador | F6, só carburado | ponto, mistura, bomba, afogador, curva |
| Noguiintech | peça no carro | o motor vira injetado e o F6 não abre |

Peso do jogador no jogo está em toneladas. A física multiplica por 1000. Nos
NPCs o peso já está em kg.

## Física v2.8

O integrador está em `mod/phys_v28.py` (referência) e `mod/phys_v28.gml`
(o que entra no jogo). É longitudinal: força no sentido do carro, pitch e a
roda dianteira solta. Não há força lateral nova. A ré continua no modelo
antigo do jogo.

Cada passo de física tem quatro subpassos. A ordem e o atraso de um subpasso
fazem parte do modelo. Não reordene.

O torque de combustão vem de `global.torque`. Se o modelo de combustível está
ligado, esse torque é multiplicado pela eficiência do distribuidor e do
carburador (`fuel_sys.eff`). O corte de giro tira torque acima da linha
vermelha. Não corta a velocidade por conta própria: a velocidade para porque
o giro chegou no corte daquela marcha, ou porque o arrasto igualou o empuxo.

Arrasto do jogador: `Cd` e área frontal do próprio carro, com piso de CdA 0,30.
Nos NPCs o CdA fica em 1,30.

### Relação e topo da marcha

O número que o caderno mostra é a razão do jogo, a mesma de
`_gearlimit = (rpm / razão) × 60 / 1000`.

```
topo em km/h = (rpm de corte / razão) × 0,06
rpm           = km/h × razão × 16,6667
```

| Razão | 4800 rpm | 6000 rpm |
|---|---|---|
| 8,0 (primeira curta de fábrica) | 36 km/h | 45 km/h |
| 3,0 | 96 km/h | 120 km/h |
| 2,0 | 144 km/h | 180 km/h |

A física converte essa razão para radiano do motor por radiano da roda com
`razão × raio × 2π`. Com a embreagem acoplada, o conta-giros e o hodômetro
caem na mesma conta do caderno.

Com o pé fora da embreagem, sem freio e sem line-lock:

- o giro não fica abaixo da lenta
- assim que a marcha pede mais que a lenta, o giro é a velocidade vezes a razão
- a velocidade não passa do corte dessa razão

Uma primeira em 3,0 num carro fraco, em um segundo, fica perto de 20 km/h e
uns 1100 rpm. Os 6000 rpm só aparecem no topo, 120 km/h se o corte do motor
for 6000. Uma primeira de fábrica perto de 8 continua curta: o corte chega
cedo e a velocidade para ali.

Um lançamento com a embreagem ainda patinando (o giro já entrou no passo mais
de 500 rpm acima da marcha) não é puxado de volta para a lenta. Isso preserva
a largada do quarto de milha.

### Embreagem

Pé fora, a capacidade cobre o torque do motor e a carga que a marcha devolve.
Sem isso, uma marcha longa entrava em patinação com o giro já igual ao da
roda, o torque na roda ia a zero e o carro travava com o conta-giros colado.

Pé dentro, o motor pode subir livre. O timer `clutch` do jogo também abre a
embreagem durante a troca.

### NPCs

`obj_ia_car`, `obj_ia_car_TOUGE`, `obj_ia_opponent` e `obj_ia_car_NPC` usam o
mesmo integrador, cada um com o próprio estado (`ai_phys`). Massa, tração,
pneu, relações, torque e corte saem da instância. O passo do NPC não mexe no
estado nem no AFR do jogador. Se a física estiver desligada, ou se faltar
relação e velocidade, continua o lerp antigo do jogo.

Curva e ré dos NPCs continuam no modelo antigo. Um limitador de tráfego que
corte a velocidade ainda vale: no quadro seguinte o integrador acompanha.

## Câmbio

`global.cg_init_car` é o único lugar que escreve `gear_ratios`,
`original_ratios` e `max_gears`. `car.gears_mod` guarda a razão efetiva, a
mesma que a física lê. Não multiplique a relação final de novo por cima disso.

O caderno do Tião:

- carrega a caixa que está no carro
- ao mudar a relação final, escala as marchas junto
- valida contra o original na relação nova, janela de ±70% mais 0,06
- não cobra de novo uma caixa que só foi reaberta
- `cambio_mod` não é zerado: essa flag é a que mostra a opção de câmbio na oficina

Relação final de 2,60 a 5,10. O neutro de fábrica é 3,73. Menor alonga, maior
encurta. Há câmbio manual de 7 marchas ($45.000) e de 8 ($85.000).

Andando, o canto superior esquerdo mostra marcha, rpm, razão, topo teórico e
km/h.

## Forja

Na oficina do Tião, entre Revisar e Câmbio: **Forjar peças internas no motor**.
Custa $2500. É uma flag, `car.forja_motor`. Pistão, biela, virabrequim, bloco,
cabeçote e comando passam a ter 70% a mais de vida: o dano dessas peças é
multiplicado por `1/1,7`. O câmbio não entra. O dano de calor entre 140 °C e
150 °C também não.

## Noguiintech

`car.noguiintech` diferente de zero, ou `bicos > 0`, classifica o motor como
injetado antes de olhar o carburador. Efeito:

- o F6 não abre; se estava aberto, fecha
- avanço, retardo, vácuo, curva, giclê, voltas da lenta e power valve não entram no cálculo
- o ponto é o da ECU (`global.ignicao`, entre 0° e 55°)
- a mistura volta a ser a da injeção, e o wideband mostra lambda
- o torque e o giro da ECU do Noguiintech continuam valendo

Um carburado de fábrica, sem Noguiintech e sem bicos, continua com distribuidor,
carburador e o menu F6. `global.fuel` continua sendo o tipo de combustível
(gasolina, etanol…). O estado do modelo é `global.fuel_sys`.

## Distribuidor e carburador

Só com `global.fuel_model_on` e o carro ainda carburado. A chamada não mudou:
`fuel_sys_step(rpm, throttle, dt)`. O retorno é a eficiência que multiplica o
torque.

O ponto comandado no carburado é:

```
base + centrífugo × escala + vácuo + avanço extra − retardo
```

O knock ainda tira grau depois disso. Os padrões (12° de vácuo no menu, 22° de
centrífugo, curva 1) reproduzem o modelo antigo. O passo do F6 é quatro vezes
mais fino que o primeiro menu.

| Ajuste | Passo | Faixa |
|---|---|---|
| Avanço inicial | 0,25° | limites do modelo |
| Centrífugo | 0,5° | 0 até o limite |
| RPM de início e de fim | 25 rpm | 600–4000 e 1200 até o limite |
| Curva | 0,25 | 0 rua, 1 mista, 2 pista |
| Vácuo | 0,25° | 0 até o limite |
| Avanço extra e retardo | 0,25° | 0–15° |
| Giclê | 0,25 | 60–100 |
| Voltas da lenta | 0,0625 | 1–4 |
| Power valve | 0,125 inHg | 3,5–10,5 |

F6 abre o menu. TAB cicla ajuste, loja e carburador. Setas escolhem e ajustam.
1 e 2 compram. 3 volta o distribuidor ao original. F7 restaura o padrão. F8,
na aba do distribuidor, abre a curva de 8 pontos.

### Fase 2

Isto só aparece quando o tempo anda. No regime permanente, com acelerador
firme, motor quente e curva automática, AFR e ponto ficam os da fase 1.

| Peça | Comportamento | Onde se mexe |
|---|---|---|
| Bomba de aceleração | tip-in acima de 2/s enriquece e o extra some em linha. Padrão: 2 pontos de AFR em 0,35 s | F6, carburador |
| Afogador | liga abaixo de 40 °C com o motor parado ou a lenta baixa. AFR −2 e lenta em 1100 rpm. Desliga acima de 60 °C depois de 15 s | automático |
| Dano de knock | sobe 0,02/s com knock acima de 0,7. No máximo tira 30% do torque. Acima de 0,9 o motor apaga em até 30 s. Recupera 0,001/s depois de 60 s limpos | automático, gravado em `car.fuel_phase2` |
| IAT | o ar do cofre esquenta com motor quente em carga e esfria com a velocidade. Parado, a 100 °C e em WOT, sobe cerca de 28 °C em 10 s | automático |
| Curva de 8 pontos | F8. 1 Street, 2 Strip, 3 Race, 0 volta para a mistura automática | F6 |
| Kit de molas | na mesma tela F8. Quatro valores reescrevem os 8 pontos | F6 |

Motor injetado não usa bomba, afogador nem a curva. IAT e dano de knock valem
nos dois. Telemetria: `global.telemetry_afr`, `telemetry_timing`,
`telemetry_knock`, `telemetry_accel_pump`, `telemetry_accel_afr_delta`,
`telemetry_choke_active`, `telemetry_knock_damage`, `telemetry_iat`.

## Menu e teclas do mod

| Tecla | Função |
|---|---|
| F6 | abre ou fecha o menu. Não abre com Noguiintech ou bicos |
| TAB | ajuste, loja, carburador |
| setas | escolhe a linha e muda o valor |
| 1 e 2 | compram distribuidor, na loja |
| 3 | tira o distribuidor |
| F7 | padrão do modelo ou do carburador |
| F8 | editor da curva de avanço, na aba do distribuidor |

O Noguiintech do jogo continua no clique direito (menu) e no clique do meio
(painel). O objeto passa a existir também fora da garagem quando a peça está
no carro. Na garagem o motor precisa estar ligado, senão o jogo fecha o menu.

## Como o patch entra no jogo

Os patches de sprite, flag e GML passam pelo UndertaleModCli. Dois scripts
CSX vão embutidos no binário: um aplica, o outro só confere.

O GML mora em `mod/gen_mod.py`. Cada `QueueFindReplace` descompila o bytecode
atual, troca o texto e recompila. Uma âncora que não existe não faz nada e
não avisa. A mesma entrada, no replace seguinte, já está redecompilada: um
segundo `var` no mesmo nome some. A âncora tem de casar com esse texto.

`--data-fix` e `--mod-install` partem de `assets/game.unx.orig`.

## Os bugs de plataforma

Seis são do Linux, não do carro:

| # | Sintoma | Correção |
|---|---|---|
| 1 | o jogo não abre | copia 8 `.so` do Steam Runtime. O binário tem `RPATH=$ORIGIN` |
| 2 | imagem esticada, input deslocado | `--native-res` grava a janela no `GEN8` e o device no `.data` do runner |
| 3 | clique cerca de 20 px fora | origem do `spr_mouse` vai para (332, 1), a ponta da chave |
| 4 | cursor do sistema | `obj_mouse` persistente e criado na room Languages |
| 5 | barra de tarefas do PC não responde | `display_set_gui_size(1280, 720)` |
| 6 | botões do XLO somem | o passe CRT amostra a surface na escala certa |

O catálogo com a conta de cada um está em [`docs/BUGS.md`](docs/BUGS.md).

## CLI

```bash
mcml-fixer                              # GUI
mcml-fixer --check
mcml-fixer --fix-libs
mcml-fixer --revert-libs
mcml-fixer --native-res 1280x1024
mcml-fixer --revert-native
mcml-fixer --data-fix
mcml-fixer --data-revert
mcml-fixer --data-check
mcml-fixer --play 1920x1080
mcml-fixer --play --no-gamescope
mcml-fixer --mod-install
mcml-fixer --mod-uninstall
mcml-fixer --mod-check
mcml-fixer --mod-status
mcml-fixer --mod-set ganho=80
mcml-fixer --phys raio=0.33 mu=0.85
mcml-fixer --dir "/caminho/do/jogo" --check
```

A GUI detecta a pasta da Steam, mostra libs, GEN8 e gamescope, e tem os
botões de corrigir libs, cursor, GUI e jogar.

## Resolução

1024×768 não está no `game.unx`. É o default do runner, dois u32 em `.data`
no offset de arquivo `0x9422f8`. `--native-res` grava esse par e o `GEN8`.
Sem os dois, a janela e o device nascem de tamanhos diferentes e o clique
desloca.

O gamescope separa o canvas do jogo do modo do monitor:

```bash
gamescope -w 1024 -h 768 -W <SAIDA_W> -H <SAIDA_H> -f -- ./My_Car_My_Life
```

Os modos saem de `/sys/class/drm/card*-*/modes`. Config em
`~/.config/mcml-res.json`. Log do último launch em
`~/.config/mcml-fixer-launch.log`.

## Documentação

| Arquivo | Conteúdo |
|---|---|
| [`docs/MOD.md`](docs/MOD.md) | cada `QueueFindReplace`, câmbio, loja, ini |
| [`mod/MOD.md`](mod/MOD.md) | fase 2: parâmetros, telemetria, validação |
| [`docs/BUGS.md`](docs/BUGS.md) | bugs de plataforma e de conteúdo, com a evidência |
| [`docs/DEV.md`](docs/DEV.md) | opções ocultas, inclusive dinheiro |
| [`docs/SOURCE.md`](docs/SOURCE.md) | passeio pelo fonte do fixer |
| [`docs/RESEARCH.md`](docs/RESEARCH.md) | o que a varredura do decompilado achou e o que foi descartado |

Varredura do decompilado: `python3 tools/scan.py`.

## Limitações

- O canvas do jogo é o dos rooms. Mudar a resolução muda a saída. O jogo é escalado.
- Sem gamescope, o clique volta a errar quando o monitor não é o canvas.
- A física nova é longitudinal. Curva e ré ficam no modelo antigo.
- O dyno e uma corrida completa não são o teste automático. O gate é o Python.

## Licença

MIT. Veja [LICENSE](LICENSE). Sem afiliação com os autores do jogo. A
ferramenta não distribui nenhum arquivo do jogo.
