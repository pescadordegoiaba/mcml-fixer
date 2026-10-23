# Opções ocultas

## Dinheiro

Nada aparece no `--help`: é de propósito.

```bash
mcml-fixer --dev-cash 500000                   # piso: nunca fica abaixo disso
mcml-fixer --dev-cash 500000 --dev-cash-exato  # força exatamente (1x por rev do ini)
mcml-fixer --dev-cash off                      # desliga
```

Grava `[dev] cash=N` e `cash_modo` no `~/.config/My_Car_My_Life/mod_distribuidor.ini`;
o jogo lê no boot (`mod_dist_carregar`) e aplica no primeiro frame do HUD
(`dev_cash_aplicar`).

- **piso** (padrão): `global.cash = max(cash, N)` — gastar continua funcionando, o valor
  nunca cai abaixo de N.
- **exato**: `global.cash = N` uma vez por `rev` do ini.

O save é cifrado (`ssave`/sphinx), então nada é gravado no save: o valor é aplicado em
runtime a cada execução.

## Câmbio: limites de ajuste

A página de câmbio do caderno do Tião lê estes valores na abertura
(`mod_dist_carregar`) — dá para afrouxar ou apertar sem recompilar:

```ini
[cambio]
limite_pct=0.70     ; quanto cada marcha pode variar (fracao do valor original)
rel_min=2.60        ; relacao final minima
rel_max=5.10        ; relacao final maxima
rel_ref=3.73        ; valor neutro (nao altera nada)
passo=0.01          ; passo das setas na relacao final
vmax=500            ; teto de velocidade (km/h)
custo=775           ; custo por marcha alterada (relacao final conta como 1)
```

Editar o ini **não** altera carros que já têm `car.relacao_final` gravada: o
valor do carro vence. Para zerar, apague o campo do save ou volte para 3.73 no
caderno.

## Verificação

```bash
grep -A2 '\[dev\]' ~/.config/My_Car_My_Life/mod_distribuidor.ini
mcml-fixer --mod-check | grep dev_cash      # MARK dev_cash=ok
mcml-fixer --mod-check | grep cambio_       # rel_schema, rel_create, rel_ui,
                                            # rel_save, limite, vmax = ok
```

## Câmbio: validação e diagnóstico (v3)

```bash
# V1 — estática: 18 invariantes sobre o GML instalado
cd /home/gullin/projetos/mcml-fixer
# dump (copia + GEN8 720 porque o UTMT recusa 1280x1024)
G=/home/gullin/.local/share/Steam/steamapps/common/"My Car My Life"
cp "$G/assets/game.unx" mod/dbg.unx
python3 -c "p='mod/dbg.unx'; b=bytearray(open(p,'rb').read()); b[0x4c:0x50]=(1280).to_bytes(4,'little'); b[0x50:0x54]=(720).to_bytes(4,'little'); open(p,'wb').write(b)"
~/.local/share/mcml-fixer/utmt/UndertaleModCli load mod/dbg.unx -s mod/dump_v3.csx -o mod/dbg_out.unx -f
python3 tools/verify_cambio.py          # V1 OK 18/18

# V2 — runtime: 15 casos com assert
./mod/run_csx.sh cg_test
(cd "$G" && timeout -k 3 15 ./My_Car_My_Life)
cat ~/.config/My_Car_My_Life/cg_test.txt    # RESUMO pass=15 fail=0

# V2b — criação real do player (pega crash que só acontece na rua)
./mod/run_csx.sh cg_create_test
(cd "$G" && timeout -k 3 15 ./My_Car_My_Life)
cat ~/.config/My_Car_My_Life/cg_create.txt  # CREATE ok max_gears=8 ...

# V3 — arquivo: round-trip gravar -> SaveAllCars -> LoadAllCars
./mod/run_csx.sh cg_save_test
(cd "$G" && timeout -k 3 15 ./My_Car_My_Life)
python3 tools/verify_cg_log.py          # V3 OK ... roundtrip=1
```

Depois de qualquer harness, reinstalar limpo:
```bash
~/.local/bin/mcml-fixer --mod-uninstall && ~/.local/bin/mcml-fixer --mod-install
```

### `cambio_save.txt` (em `~/.config/My_Car_My_Life/`)

| Linha | Significado |
|---|---|
| `PODE PAGAR custo=… cash=…` | o confirm passou pela checagem de dinheiro |
| `SEM DINHEIRO custo=… cash=…` | não tinha saldo (nada gravado) |
| `RECUSADO <motivo>` | validação dura barrou (janela ±70 %, nº de marchas, relação final) |
| `GRAVOU CG\|v=3\|mod=1\|rel=…\|n=8\|r0=…` | gravou; snapshot completo das razões efetivas |
| `CREATE CG\|…` | player criado lendo o câmbio alterado |
| `MIGRADO cambio_mod zerado` | save do esquema antigo; voltou ao de fábrica |
| `SAVE IGUAL=1` | round-trip do `.ssave` conferiu |

### `[dev] cambio_gratis`

```ini
[dev]
cambio_gratis=1
```
Libera o confirm sem debitar (só para teste; default 0).

### Armadilhas já pagas

- Campo opcional de struct **sem** `variable_struct_exists` derruba o runner
  (`Variable <obj>.x not set`). Use `global.cg_get(st, "chave", padrao)`.
- `instance_create` não existe neste runner: use `instance_create_depth`.
- Função anônima com `var` local do escopo externo falha (`obj_x._p not set`):
  use globais nos contadores do harness.
- `sqrt(-1)` derruba o runner antes do `_chk`; use `infinity`/`undefined` para testar
  o saneamento.
