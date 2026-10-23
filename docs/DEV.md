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
