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

## Verificação

```bash
grep -A2 '\[dev\]' ~/.config/My_Car_My_Life/mod_distribuidor.ini
mcml-fixer --mod-check | grep dev_cash      # MARK dev_cash=ok
```
