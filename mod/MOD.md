# Fase 2 — transientes do distribuidor e do carburador

A fase 1 (ponto, vácuo, knock e os três circuitos do carburador) continua igual
no regime permanente. A fase 2 acrescenta o que só aparece quando o tempo anda:
bomba de aceleração, afogador, dano de knock, temperatura do ar no cofre e uma
curva de avanço editável.

A chamada não mudou: `fuel_sys_step(rpm, throttle, dt)` no GML e
`fuel_step(st, rpm, throttle, dt)` no Python. O retorno continua sendo a
eficiência. Os campos novos ficam no estado (`global.fuel_sys` / `Fuel`) e na
telemetria.

Motor injetado (Noguiintech ou bicos) não usa bomba, afogador nem a curva do
distribuidor. IAT e dano de knock valem nos dois.

## Parâmetros

| Nome | Faixa | Padrão | Onde se ajusta |
|---|---|---|---|
| `pump_enrich` | 1–3 pontos de AFR | 2 | F6, aba do carburador |
| `pump_dur` | 0,20–0,50 s | 0,35 s | F6, aba do carburador |
| limiar de tip-in | > 2 /s e acelerador > 0,1 | fixo | — |
| afogador liga | coolant < 40 °C e motor parado ou lenta < 720 rpm | fixo | — |
| afogador desliga | coolant > 60 °C e tempo > 15 s | fixo | — |
| enriquecimento do afogador | −2 AFR, lenta +300 rpm | fixo | — |
| dano de knock | +0,02/s com knock > 0,7 | — | — |
| recuperação | −0,001/s depois de 60 s limpos | — | — |
| penalidade | até 30% do torque com dano 1 | — | — |
| falha | dano > 0,9 mata o motor em até 30 s | — | — |
| `K_IAT_HEAT` | 6 °C/s no WOT com o motor no topo quente | — | — |
| `K_IAT_COOL` | 0,08 /s, vezes o vento (`vx/20`, 0,2–1) | — | — |
| curva | 8 pontos rpm/grau, 0–30° | mola street/strip/race | F6, F8 |
| kit de molas | leve 800–2500, pesada 2000–5000 | gera os 8 pontos | F6, F8 |

O 15 °C/s do rascunho, com o fator de coolant, passava de 30 °C em 10 s.
O 6 °C/s fecha a checagem de 20–30 °C parado, em WOT, com o motor a 100 °C.

Enquanto `mech_custom` for 0, a curva continua a mistura 0 rua / 1 mista / 2 pista.
F8 abre o editor. 1, 2 e 3 carregam Street, Strip e Race. 0 volta para a curva
automática. As quatro molas reescrevem os 8 pontos.

## Telemetria

| Global | Conteúdo |
|---|---|
| `telemetry_accel_pump` | 1 enquanto a bomba está decaindo |
| `telemetry_accel_afr_delta` | pontos de AFR deste passo (negativo = mais rico) |
| `telemetry_choke_active` | afogador ligado |
| `telemetry_idle_target` | 800 ou 1100 |
| `telemetry_knock_damage` | 0–1, gravado em `car.fuel_phase2` |
| `telemetry_iat` | °C do cofre |
| `telemetry_engine_failure` | 1 quando o dano estourou o prazo |

## Validação

`python3 tools/verify_fuel_model.py` cobre os seis itens e o quarto de milha
com o modelo ligado. `python3 tools/verify_phys_v28.py` continua o gate do
carro de rua sem o modelo de combustível: 60 pés 1,9–2,1 s, quarto 11,5–12,0 s.
