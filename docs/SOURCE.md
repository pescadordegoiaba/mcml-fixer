# Documentação da source — `mcml-fixer`

`src/main.rs` (~1250 linhas, arquivo único) é ao mesmo tempo **CLI** e **GUI**.
As duas interfaces chamam exatamente as mesmas funções de ação — a GUI só
empacota em botões o que a CLI expõe em flags. Não há estado compartilhado além
de `Vec<String>` de log.

```
                        main()
                          |
        +-----------------+------------------+
        |                                    |
  args vazios -> GUI (eframe/egui)      args -> run_cli()
        |                                    |
        +------------- chamam ---------------+
                          |
   detect_game_dir, fix_libs, revert_libs, patch_gen8,
   patch_native, revert_native, data_patch, data_revert,
   data_state, data_check, launch
```

---

## 1. Constantes

| constante | valor | por quê |
|---|---|---|
| `BIN_NAME` | `"My_Car_My_Life"` | nome do executável; usado para achar a pasta do jogo |
| `CANVAS_W` / `CANVAS_H` | 1024 / 768 | canvas histórico do jogo; usado pelo modo gamescope legado |
| `NATIVE_OFF` | `0x9422f8` | offset **em arquivo** do par `(device_w, device_h)` do runner (vaddr `0xf422f8` menos o mapa de `.data`) |
| `NATIVE_KNOWN` | 5 pares | lista de valores aceitos antes de patchar (trava de segurança: se o valor lido não estiver na lista, aborta em vez de corromper) |
| `LIBS` | 8 pares | `(caminho dentro do Steam Runtime, nome no jogo)` |
| `CURSOR_TIP` | `(332, 1)` | ponta da chave no bitmap do `spr_mouse` |
| `GUI_W` / `GUI_H` | 1280 / 720 | tamanho da view/port; a GUI precisa ser forçada a esse valor |
| `MARKER_FILE` | `assets/.mcml-data-patches` | marcador em texto que diz quais patches foram aplicados, sem abrir o `game.unx` de 135 MB |

---

## 2. Detecção e leitura de estado

### `home() -> PathBuf`
Atalho para `$HOME`; cai em `"."` se a variável não existir.

### `steam_runtimes() -> Vec<PathBuf>` / `first_steam_runtime() -> Option<PathBuf>`
Três caminhos possíveis do Steam Runtime (`~/.local/share/Steam`, `~/.steam/root`,
`~/.steam/steam`). Retorna o primeiro que existir.

### `detect_game_dir() -> Option<PathBuf>`
Varre `steamapps/common` (dois roots) procurando uma pasta que contenha
`BIN_NAME`. Se não achar, tenta o diretório atual. É o que alimenta o campo
"Pasta do jogo" da GUI.

### `missing_libs(bin: &Path) -> Vec<String>`
Roda `ldd` e devolve as linhas com `not found`. É a fonte de verdade sobre o
estado do BUG 1 — não há lista hardcoded de "o que deveria existir", a decisão é
sempre do loader.

### `read_gen8(data: &Path) -> Option<(u32, u32)>`
Lê o `game.unx` e valida a assinatura: `FORM` em `0x00` e `GEN8` em `0x08`.
Largura/altura em `0x4c` / `0x50` (little endian). Se algo não bater, devolve
`None` em vez de lixo — todas as gravações checam isso antes de escrever.

> Nota: `read_gen8` usa `0x4c/0x50` (offset do **conteúdo** do chunk), enquanto
> `patch_gen8` grava em `0x4c/0x50` também. Os `+0x3c/+0x40` citados na
> investigação são relativos ao início do chunk, que começa em `0x10`.

### `edid_modes() -> Vec<String>`
Lê `/sys/class/drm/*/modes` dos conectores com `status == connected`. Escolha
deliberada: o `xrandr` no XWayland mostra só o modo virtual, não os modos reais
do monitor. Ordena por área (largura × altura).

### `parse_wh(s: &str) -> Option<(u32, u32)>`
`"1280x1024"` → `(1280, 1024)`.

### `load_cfg() / save_cfg() / cfg_path()`
Persistem a resolução de saída escolhida em `~/.config/mcml-res.json`. O parser
é manual (procura `"width"` / `"height"` e lê os dígitos seguintes) para não
puxar uma dependência de JSON por dois inteiros.

### `log_path()`
`~/.config/mcml-fixer-launch.log` — stdout/stderr do jogo quando iniciado pelo fixer.

---

## 3. Correção das libs (BUG 1)

### `fix_libs(game_dir, log)`
1. localiza o Steam Runtime;
2. para cada item de `LIBS`: pula se o destino já existe, avisa se a origem não
   existe, copia;
3. no fim roda `missing_libs()` de novo e informa se ainda falta algo.

Nunca sobrescreve e nunca mexe fora da pasta do jogo — é por isso que funciona
com `RPATH=$ORIGIN` e não exige root.

### `revert_libs(game_dir, log)`
Remove os 8 arquivos da pasta do jogo.

---

## 4. GEN8 (BUG 2, metade "janela")

### `patch_gen8(data, w, h, log)`
- cria `game.unx.orig` se ainda não existe (backup único, nunca sobrescrito);
- lê o arquivo inteiro, **valida `FORM`/`GEN8`** e aborta sem escrever se o
  layout não for o esperado (proteção contra corromper 135 MB);
- grava `w` em `0x4c` e `h` em `0x50`.

### `unpatch_gen8(data, log)`
Restaura `game.unx` a partir do `.orig`.

---

## 5. Resolução nativa (BUG 2, metade "device")

### `read_native(bin) -> Option<(u32,u32)>`
Abre o binário, dá `seek(NATIVE_OFF)` e lê 8 bytes. Não carrega o ELF inteiro.

### `patch_native(game_dir, w, h, log)`
Faz **as duas** metades de propósito:
1. lê o device atual; se não estiver em `NATIVE_KNOWN`, **aborta** (evita patchar
   um binário diferente/atualizado com offset errado);
2. backup `My_Car_My_Life.orig`;
3. grava `(w,h)` em `NATIVE_OFF`;
4. chama `patch_gen8()` — sem isso a janela nasce menor que o device.

### `revert_native(game_dir, log)`
Restaura o binário do `.orig` e chama `unpatch_gen8()`.

---

## 6. Patches no `game.unx` via UTMT (BUGs 3, 4, 5, 6)

Esta é a parte central. Nenhum desses patches é byte-patch: mexem em sprite,
flag de objeto e código GML. Byte-patch de GML exigiria recompilar bytecode à
mão; em vez disso o fixer dirige o **UndertaleModCli**, que edita o arquivo com o
serializador oficial do formato.

### `utmt_dir() -> Option<PathBuf>`
`$MCML_UTMT` (se contiver `UndertaleModCli`) ou `~/.local/share/mcml-fixer/utmt`.

### `PATCH_CSX` (script C# embutido)
Executado **dentro** do UTMT. Faz, em ordem:

1. `spr_mouse.OriginX/Y = 332,1` → BUG 3;
2. `obj_mouse.Persistent = true` → BUG 4;
3. `QueueAppend("gml_RoomCC_Languages_0_Create", "instance_create_depth(0,0,-9999,obj_mouse);")` → BUG 4;
4. `QueueAppend("gml_RoomCC_Start_0_Create", "display_set_gui_size(1280,720);")` → BUG 5;
5. `QueueFindReplace` no bloco único de `gml_Object_obj_crt_Draw_77` → BUG 6;
6. `group.Import()` aplica tudo de uma vez.

Cada passo emite um `MARK ...` no stdout; o fixer filtra essas linhas e as
mostra no log — é a evidência de que o patch entrou.

### `CHECK_CSX` (script C# embutido)
Modo somente-leitura: relê sprite, flag e descompila as três entradas de código
para conferir `display_set_gui_size`, `obj_mouse` e `_kx`. Emite cinco `MARK`.

### `run_utmt(script, input, output) -> Result<String, String>`
- grava o script num temporário com PID no nome (permite execuções concorrentes);
- roda `UndertaleModCli load <input> -s <script> [-o <output> -f]` com
  `current_dir` no diretório do UTMT (ele resolve o runtime por caminho relativo);
- remove o script;
- devolve stdout+stderr; em falha, concatena as 3 últimas linhas da saída na
  mensagem de erro (o UTMT cospe stack traces longos).

### `data_patch(game_dir, log)`
Fluxo completo, e a razão de ele sempre partir do `.orig`:

```
game.unx.orig --(UTMT: PATCH_CSX)--> game.unx.new
     game.unx  --(backup)-->  game.unx.before-datafix.bak   (só se ainda não existe)
game.unx.new --(cópia)--> game.unx
                          -> patch_native() reaplica GEN8 + device
                          -> escreve o marcador .mcml-data-patches
```

Se o UTMT falhar, o `game.unx` atual **não** é tocado.

### `data_revert(game_dir, log)`
Restaura `game.unx` do `.before-datafix.bak` (ou do `.orig` se não houver) e
apaga o marcador.

### `data_state(game_dir) -> Option<String>`
Lê o marcador. Instantâneo — existe justamente para a GUI não abrir 135 MB a
cada frame.

### `data_check(game_dir, log)`
Verificação de verdade:
1. copia `game.unx` para `$TMPDIR/mcml_check.unx`;
2. **restaura o GEN8 original** na cópia (senão o UTMT recusa — ver ARMADILHA 2
   no `BUGS.md`);
3. roda `CHECK_CSX` sem saída;
4. imprime os `MARK`.

---

## 7. Início do jogo

### `launch(game_dir, out_w, out_h, use_gamescope) -> Result<u32, String>`
- redireciona stdout/stderr para `~/.config/mcml-fixer-launch.log`;
- monta `setsid gamescope -w 1024 -h 768 -W <out_w> -H <out_h> -f -- ./My_Car_My_Life`
  (ou só o binário, sem gamescope);
- `setsid` desliga o processo do terminal — sem isso, fechar o fixer derruba o jogo;
- devolve o PID.

`-w/-h` é o canvas interno, `-W/-H` é a saída no monitor: o gamescope escala.
Com o patch nativo aplicado os quatro valores coincidem e a escala é 1:1.

---

## 8. GUI (`struct App` + `impl eframe::App`)

### `struct App`
Cache de tudo que a tela mostra: pasta, modos EDID, seleção, `gen8`, `native`,
`missing`, presença de gamescope/Steam Runtime, log e os valores dos campos
numéricos. `refresh()` repopula o cache — nenhum widget lê disco no meio do frame.

### `App::new()`
Detecta a pasta, carrega a config salva, tenta pré-selecionar o modo EDID
correspondente e faz o primeiro `refresh()`.

### Seções da tela (`update`)
1. **Pasta do jogo** — editável + "Detectar" + "Atualizar status".
2. **Status** — binário, libs, GEN8, device, Steam Runtime, gamescope. Verde/vermelho.
3. **Fix de launch** — "Corrigir libs" / "Reverter libs".
4. **Fix de resolução** — combo dos modos EDID reais, "Salvar escolha", "Jogar".
5. **Resolução NATIVA fixa** — largura/altura + "Aplicar nativo"/"Reverter".
6. **Cursor e GUI do PC** — estado (lido do marcador), estado do UTMT,
   "Aplicar cursor + GUI" / "Reverter" / "Verificar no arquivo".
7. **Patch estático do GEN8** — recolhido, marcado como opcional.
8. **Log** — rolável, com botão limpar.

`'<-'` Toda ação chama a mesma função da CLI e depois `refresh()`.

---

## 9. `main()` e `run_cli()`

`main()` decide: sem argumentos, abre a GUI; com argumentos, chama `run_cli()`.

`run_cli()`:
- **restaura `SIGPIPE`** (`unsafe { libc::signal(SIGPIPE, SIG_DFL) }`) — sem isso
  `mcml-fixer --check | head` entra em panic, porque o Rust ignora SIGPIPE por
  padrão;
- extrai `--dir PASTA` de qualquer posição;
- faz um `match` no primeiro argumento restante.

| comando | função |
|---|---|
| `--check` | status completo (libs, GEN8, device, data, UTMT, SteamRT, EDID) |
| `--fix-libs` / `--revert-libs` | `fix_libs` / `revert_libs` |
| `--patch-gen8 WxH` / `--revert-gen8` | `patch_gen8` / `unpatch_gen8` |
| `--native-res WxH` / `--revert-native` | `patch_native` / `revert_native` |
| `--data-fix` | `data_patch` |
| `--data-revert` | `data_revert` |
| `--data-check` | `data_check` |
| `--play [WxH] [--no-gamescope]` | `launch` |
| `--help` | texto de ajuda |
| *(vazio)* | GUI |

Códigos de saída: `0` sempre (as falhas são reportadas na saída padrão com
`ERRO:`); isso é intencional para uso em scripts que só leem o texto.

---

## 10. Dependências (`Cargo.toml`)

| crate | papel |
|---|---|
| `eframe` / `egui` | janela e widgets |
| `libc` | só o `signal(SIGPIPE, SIG_DFL)` |

Nada de `serde`, `regex` ou `clap`: os três seriam convenientes, mas o código
tem 1250 linhas e o parse manual de 2 inteiros e de flags com `split_once` não
justifica três árvores de dependências.

---

## 11. Onde ficam os artefatos em runtime

```
<game_dir>/                          libs .so, My_Car_My_Life, My_Car_My_Life.orig
<game_dir>/assets/game.unx           arquivo patchado
<game_dir>/assets/game.unx.orig      pristine (entrada obrigatória do --data-fix)
<game_dir>/assets/game.unx.before-datafix.bak
<game_dir>/assets/.mcml-data-patches marcador de estado
~/.config/mcml-res.json              resolução de saída escolhida
~/.config/mcml-fixer-launch.log      log do jogo
~/.local/share/mcml-fixer/utmt/      UndertaleModCli + runtime .NET
```

---

## 12. Como validar depois de mexer no código

```bash
cargo build --release
./target/release/mcml-fixer --check          # não escreve nada
./target/release/mcml-fixer --data-check     # ~1 min, relê o arquivo
./target/release/mcml-fixer --data-fix       # ~1 min, regera do .orig
cd "<game_dir>" && ./My_Car_My_Life          # deve chegar em "Entering main loop."
```

Regra: **patch não validado não é entregue**. `--data-check` confirma o texto
dentro do arquivo, mas não substitui rodar o jogo — um erro de GML (como o da
ARMADILHA 1) só aparece em runtime, e às vezes só numa room específica.
