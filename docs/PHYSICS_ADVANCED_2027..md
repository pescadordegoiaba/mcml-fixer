# MCML Realistic Physics Spec (2D Drag Racing)

**Versão:** v2.8 — 8ª rodada (2026-11-10). Errata de precisão + defesa em profundidade — nenhuma fórmula de física muda desde a v2.7: **(1)** o transiente de 1 substep da quebra stick→slip é DECLARADO (§3.4): a quebra fura o `stick_state` com a roda ainda em `omega_w = vx/radius` ⇒ `kappa ≈ 0` ⇒ `F_x ≈ 0` (medido: 0,0%–2,7% do pico), recupera a ≥ 0.3·pico em 1 substep — o "sem degrau" da v2.5 era inexato; a Opção A garante o PATAMAR (pico = clamp = `mu_static_eff`) e o critério da §7 foi reescrito (o antigo falhava como escrito). **(2)** estabilidade da wheelie bar recalculada com a MASSA EFETIVA do contato (§6): `m_eff_bar = 1/(1/mass_sprung + r_bar²/inertia_pitch)` ≈ 314 kg no carro-teste (a v2.7 usava a massa total — "folga confortável" não se sustentava): `c_bar = 0.3·k_bar` → 60% do limite @240 Hz e **INSTÁVEL no lite @120 Hz (2.39 > 2)**; sugerido `c_bar ≤ 0.15·k_bar` (§2.5), verificado numericamente (lite: 8 re-engajes da barra vs 2). **(3)** `eps_liftoff ≈ 10 N` no lift-off (§3.6) — ruído em torno de `F_neta = 0` não descola a roda; wheelies reais (F_neta ≈ −m_u·g) não são afetados. **(4)** safety net estendido a `vz` e `omega_pitch` (§4.13). Verificação numérica V1–V4 + regressão S1–S4b: `scripts/verify_v28.py`. Histórico completo na §9.  
**Status:** Implementável — mas NÃO marcar "pronta" sem o teste de integração do §7 rodando no motor (lição v2.7: 6 rodadas de auditoria de fórmula não pegaram o ramo de contato morto).  
**Alvo:** My Car My Life (GameMaker 1.4) via injeção GML/CSX do `mcml-fixer`.  
**Objetivo:** Substituir a física artificial de tração/velocidade/RPM por um sistema emergente (torque → roda → pneu → chassi), sem clamps de velocidade ou redline duro.  
**Escopo principal:** Longitudinal + pitch (side-view de arrancada) + DOF vertical da massa não suspensa **dianteira** (wheelie com perda de contato real). **Sem lateral na fase 1** (`F_y ≡ 0`) — a Ignorantes Highway é um modo físico separado (decisão v2.4, §1.1).

---

## 0. Convenções de sinal (travadas — ler antes de codar)

| Grandeza | Convenção |
|---|---|
| Eixos | `x` positivo para **frente**; `z` positivo para **cima**; solo em `z = 0` |
| `theta` | positivo = **nariz sobe** (wheelie) |
| `travel` | positivo = **compressão** da suspensão |
| `F_susp` | força da suspensão **no chassi**, positivo para **cima** |
| `F_aero_z` | força aerodinâmica vertical, positivo = lift para cima. **Downforce ⇒ `ClA < 0` ⇒ `F_aero_z < 0`** |
| `F_drag` | magnitude do arrasto (o vetor aponta para trás) |
| `F_x` | força longitudinal do pneu no contato, positivo = empurra o carro para frente |
| `normal_load` | reação normal do solo, sempre ≥ 0 |
| `z_u` | altura do centro da roda (massa não suspensa) |
| Momentos | `M_pitch` positivo aumenta `theta` (nariz sobe) |

Regra prática de momento (CG como pivô): força vertical ↑ aplicada **à frente** do CG → nariz sobe; ↑ **atrás** do CG → nariz desce. Força horizontal → para frente **abaixo** do CG → nariz sobe; arrasto para trás **acima** do CG → nariz sobe.

> **Nota v2.1:** além dos bugs de sinal apontados na auditoria (`F_down`, `T_wheel_reaction`), a v2.0 tinha inversões internas que tornavam as equações auto-inconsistentes: `F_susp = -k*travel` era incompatível com `travel` positivo = comprimido (na v2.0, comprimir a mola *reduzia* a carga do pneu e acelerava o chassi para baixo), os momentos de suspensão no pitch estavam trocados (+traseira/−dianteira) e o termo de tração dianteira tinha sinal invertido. Tudo normalizado nesta seção e aplicado em §3.6–§3.8. Não "corrigir de volta".

---

## 1. Decisões Arquiteturais (travadas)

| Item | Decisão | Motivo |
|------|---------|--------|
| Modelo de chassi | **2-DOF: heave (`z_cg`) + pitch (`theta`)** + **DOF vertical da massa não suspensa dianteira** (`z_u`); eixo traseiro permanece cinemático | Wheelie real com perda de contato (feature central). 1 DOF extra é barato em GML. Auditoria 1.2 — **decisão: opção (b)**. |
| Contato com o solo | **Unilateral completo**: POUSO por geometria (`z_u ≤ radius` com `v_zu ≤ 0`); LIFT-OFF por força (`F_susp + m_u·g < 0` — o contato não puxa). `grounded` = ESTADO PERSISTENTE (máquina §3.6 — v2.7) | Auditoria 1.1: checar "no ar" por `normal_load == 0` eliminava o wheelie real. **v2.7: geometria SOZINHA também eliminava — do outro lado** (clamp `z_u = radius` ⇒ a roda nunca DESCOLAVA). Faltando qualquer das metades, o wheelie não existe. |
| Suspensão | Half-car cinemático no eixo traseiro; dianteiro com `m_u` dinâmico. **Bump/droop stops na equação de força** | Auditoria 1.4: sem stops, o launch comprime além do curso, `F_susp` explode e o chassi dá "snap". |
| Rodas | 2 eixos. Cada eixo tem `omega_w`, suspensão relativa, contato unilateral e **`stick_state` com histerese** | Auditoria 1.3: sem máquina de estados, o burnout morre no 1º frame e o launch treme (chatter). |
| Modelo de pneu | **PER-AXLE — `tire_front`/`tire_rear` (v2.4)** + curva de aderência simplificada + load sensitivity (com clamp) + **`temp_factor` explícito** + efeito de PSI | Auditorias 2.4 e 2.5. v2.4: os eixos NUNCA compartilham pneu (street na frente, drag atrás) — PSI/burnout são o tuning central. Pacejka completo é pesado. |
| Embreagem | Stick/slip com regimes explícitos, inércia efetiva e **`T_load_eng` definido** | Auditoria 3.3. |
| Câmbio | **Sequência de troca com `shift_timer`**: abrir → trocar → fechar → rev-match (opcional) | Auditoria 2.3: troca instantânea distorce o ET em 0,2–0,5 s por troca. |
| Motor | **Stall + restart por ignição** (starter); `omega_eng ≥ 0` garantido em todo substep | Auditoria 2.2: sem stall, `omega_eng` fica negativa e inverte todos os sinais acoplados. |
| Wheelie bar | **Fórmula explícita de contato** (ponta da barra vs. solo, mola + damping unilaterais) | Auditoria 2.1: dispositivo de segurança central; sem fórmula o carro capota ou a barra atravessa o chão. |
| Freio | **Restrição (complementaridade)**, por eixo: pedal + **line lock** + handbrake | Auditoria 4: freio força-bruta oscila entre travar/destravar. Line lock/handbrake = mecânica de burnout. |
| Anti-squat | Parâmetro `h_antisquat` (ou %) por carro/suspensão traseira. | Tuning central de drag racing. |
| Lateral | **Nenhum na fase 1** — `F_y ≡ 0` na Arrancada; o traction circle (§3.4) permanece como INTERFACE. Highway = modo físico separado (§1.1 — v2.4). | Este `phys_step` é de trajetória reta: não há `delta`, yaw nem `F_y(α)`; embuti-los agora reabriria a ordem de passos estabilizada na §4. |
| Integração | Euler semi-implícito + substeps limitados (2–4). | Estabilidade com molas, embreagem e wheelie bar. |
| Soft-body RoR | **Fora de escopo** | Desnecessário em 2D de arrancada. |
| Unidades | Física em SI (m, kg, N, rad/s). Conversão para pixels só na renderização. | Evita bugs de sinal e escala. |

### 1.1 Decisão de escopo (v2.4): Ignorantes Highway = modo físico SEPARADO

O `phys_step` deste documento é **estritamente longitudinal** (side-view, heading fixo, trajetória reta). A auditoria v2.4 apontou que a Highway exige `steer_angle (delta)`, estado de yaw/beta e `F_y(α)` — nenhum deles existe neste modelo, e embuti-los reabriria a ordem de passos e os lags estabilizados na §4. Decisão:

- **Arrancada (este spec):** `F_y ≡ 0` por definição. O traction circle da §3.4 permanece como **interface** — a fórmula de escala está correta e degrada para o limite longitudinal com `F_y = 0`.
- **Highway:** modo separado com física lateral simplificada (modelo bicicleta: input `delta`, yaw rate, `F_y(α)` linear com saturação pelo MESMO traction circle) — especificação própria na fase 2, reaproveitando as structs de pneu per-axle (§2.4).
- Consequência na §7: o teste de combined slip passa a ser estrutural (injeção de `F_y` sintético) — com `F_y = 0` sempre, o teste antigo passava trivialmente sem validar nada.

---

## 2. Estruturas de Estado

### 2.1 Chassis (corpo rígido 2-DOF)
```
chassis:
  mass                // kg (de global.weight) — massa TOTAL (sprung + unsprung)
  mass_sprung         // kg = mass - axle_front.mass_unsprung - axle_rear.mass_unsprung
                      //     (derivado no load do carro; heave/pitch são do corpo SPRUNG — §3.7;
                      //      mass_unsprung é LUMPED por eixo — §2.2)
  H_body              // m (altura da caixa do corpo — só p/ a aproximação de inércia)
  inertia_pitch       // kg·m²  ≈ mass_sprung * (wheelbase² + H_body²) / 12
                      //     (v2.4: era (wheelbase²/12 + h_cg²) — h_cg é ALTURA DO CG, não
                      //      dimensão do corpo; a caixa correta usa a altura H_body.
                      //      Aproximação de caixa uniforme; carro com massa traseira
                      //      concentrada subestima — CAD/calibração SUBSTITUI este default)
  h_cg                // m (altura do CG, default 0.45–0.55)
  a_cg                // m (distância horizontal CG → eixo dianteiro; default L/2)
  b_cg                // m (distância horizontal CG → eixo traseiro; a_cg + b_cg = L)
                      //     a_cg ≠ b_cg permite weight bias (essencial em drag racing)
  h_antisquat         // m (altura efetiva da linha de anti-squat; 0 = 0% anti-squat)
  s_eng               // sinal da reação do trem de força, POR LAYOUT (v2.3 — §3.7):
                      //     0 = motor longitudinal (RWD clássico; reação do crank é roll)
                      //    ±1 = motor transversal (conforme sentido do crank + final drive)
  h_aero              // m (altura de aplicação do arrasto; default = h_cg)
  x_cp                // m (posição longitudinal do centro de pressão, medida do eixo
                      //     dianteiro; default = a_cg → sem momento aerodinâmico de pitch)
  wheelbase           // m (L)
  z_cg                // m (heave, positivo para cima; init = equilíbrio estático, §6)
  theta               // rad (pitch, positivo = frente sobe)
  vz                  // m/s
  omega_pitch         // rad/s
  vx                  // m/s (longitudinal)
  x                   // m
```

### 2.2 Eixo (front / rear)
```
axle:                 // CONVENÇÃO LUMPED (v2.3): TODOS os campos representam as DUAS
                      // rodas do eixo SOMADAS (modelo side-view: 1 DOF por eixo).
                      //   mass_unsprung = 2 rodas + meia suspensão | inertia_wheel = 2 rodas
                      //   | T_brake_max = freio das 2 rodas | F_x = força TOTAL do eixo no
                      //   solo | omega_w = rotação comum do eixo. Um erro de 2× no launch
                      //   nasce exatamente de ler estes campos como per-wheel.
  driven              // bool
  radius              // m (R_e)
  tire                // referência à struct de pneu DESTE eixo (tire_front / tire_rear —
                      //     v2.4, §2.4): mu_base, mu_static, load_sensitivity, psi_peak,
                      //     k_heat/k_cool e os ESTADOS psi/temp são do pneu do eixo
                      //     (LUMPED como o resto — side-view: 1 pneu por eixo)
  mass_unsprung       // kg (dianteiro: massa do DOF dinâmico; traseiro: só carga estática)
  inertia_wheel       // kg·m² (roda)
  T_brake_max         // N·m (torque de freio máx. deste EIXO a pedal cheio; LUMPED —
                      //      as 2 rodas; default 3600 dianteiro / 2400 traseiro —
                      //      dimensiona ~1,2 g para ~1500 kg; ajustável por carro)
  inertia_drivetrain  // kg·m² (cardã + diferencial + pontas, medidos NO LADO DE SAÍDA
                      //        do câmbio — já giram à rotação da roda; NÃO é inércia do
                      //        motor refletida; SÓ no eixo driven;
                      //        default 0.10, faixa 0.05–0.2 — auditoria 4)
  omega_w             // rad/s
  spring_k            // N/m (rigidez efetiva na roda)
  damp_c              // N·s/m
  L_free              // m (distância montagem↔centro da roda com a mola livre)
  travel              // m (compressão relativa, positivo = comprimido; ver §3.6)
  travel_vel          // m/s
  travel_max          // m (curso máx. de compressão — bump stop)
  travel_min          // m (curso mín. / extensão máx. — droop stop)
  k_bump              // N/m² (batente de compressão QUADRÁTICO: F = k_bump·Δ²;
                      //       ~50–150 × spring_k em valor numérico — p.ex. 100× equivale
                      //       à força da mola no fim do curso com ~3 cm de ultrapassagem)
  k_droop             // N/m² (batente de extensão quadrático; mesma faixa do k_bump)
  normal_load         // N (F_z dinâmico)
  fx                  // N (força longitudinal no solo)
  fy                  // N (força lateral — SEMPRE 0 na fase 1, §1.1; interface p/ a Highway)
  grounded            // bool (ESTADO PERSISTENTE da máquina de contato §3.6 — v2.7:
                      //      init true §6; transiciona SÓ por lift-off/pouso, NUNCA
                      //      recomputado por `z_u <= radius` a cada substep)
  stick_state         // bool (máquina de estados do pneu, §3.4; init = true)
  z_u                 // m (altura do centro da roda; estado dinâmico no eixo dianteiro,
                      //     fixo em `radius` no traseiro durante a fase 1)
  v_zu                // m/s (velocidade vertical da massa não suspensa)
```

### 2.3 Powertrain
```
powertrain:
  omega_eng           // rad/s (nunca < 0 — clamp §3.1)
  inertia_eng         // kg·m²
  clutch_engagement   // 0..1 (pressão)
  clutch_capacity_max // N·m — v2.6: a máquina de embreagem pressupõe CÂMBIO MANUAL
                      //     (pedal 0–1, §3.2); automático/conversor = fase 2 (§8)
  gear                // int
  gear_ratios[]       // final já incluída; todas POSITIVAS — ré fora do escopo na
                      //     fase 1 (gear_ratio < 0 não modelado — v2.4, §8)
  efficiency          // 0.85–0.95
  shift_phase         // "none" | "opening" | "swapping" | "closing"  (§3.10)
  shift_timer         // s (tempo dentro da fase atual)
  t_open              // s (rampa de abertura da embreagem; ~0.06–0.10)
  t_swap              // s (troca do engate; ~0.02–0.05)
  t_close             // s (rampa de fechamento; ~0.10–0.15)
  clutch_mode         // "open" | "slip" | "stick"   (stick da EMBREAGEM — §3.2)
  omega_stall         // rad/s (~36 ≈ 350 rpm)
  engine_stalled      // bool
  T_starter           // N·m (~80)
```

### 2.4 Parâmetros de pneu (POR EIXO — v2.4)
```
tire_front, tire_rear:   // DUAS structs separadas — v2.4: drag racing NUNCA tem os
                         //  4 pneus iguais (street radial na frente, drag radial/slick
                         //  atrás); PSI/temp são o tuning central do jogo. Cada `axle`
                         //  referencia a sua (§2.2) e a §3.4 roda POR EIXO.
  mu_base             // pico (tire_front: street radial ~0.95 | tire_rear: drag radial
                      //      ~1.7, slick até ~2.0)
  mu_static           // ≥ mu_base (alimenta `mu_static_eff` — §3.4, v2.5)
  load_sensitivity    // 0.1–0.2 (μ cai com F_z)
  temp                // °C (estado dinâmico POR EIXO — o burnout aquece SÓ a traseira)
  psi                 // pressão (estado POR EIXO; afeta μ e Crr — input §2.6)
  psi_peak            // pressão de pico de mu_psi POR TIPO de pneu (v2.3):
                      //     street ~32 | drag ~10 — pneu drag PENALIZA pressão alta
  k_heat              // coeficiente de aquecimento por potência de slip — SEM default de
                      //      fábrica (v2.6): calibrar pelo §7 (burnout atinge o pico de μ
                      //      ~90 °C em ~5 s; quase nada = lento demais, 100 °C+ = instantâneo)
  k_cool              // coeficiente de resfriamento — idem (v2.6): entre o burnout e a
                      //      largada (~10–30 s) a temp deve CAIR perceptivelmente, não zerar
// v2.4: `T_amb` SAIU do struct — é da PISTA, não do pneu (default 25 °C na §2.7;
// não duplicar estado)
```

### 2.5 Wheelie bar
```
wheelie_bar:
  d_bar               // m (distância horizontal da ponta da barra ALÉM do eixo traseiro)
  h_bar               // m (altura da ponta da barra abaixo do CG, carro nivelado;
                      //     ponta fica alguns cm acima do solo em repouso)
  k_bar               // N/m (rigidez do contato; 2e5–5e5)
  c_bar               // N·s/m (damping do contato; ≤ 0.15 × k_bar — v2.8: limite de
                      //     estabilidade c_bar·dt/m_eff_bar < 2 (§6); valor maior
                      //     exige checagem por carro E rate (full 240 Hz)
  bar_grounded        // bool (estado)
```

### 2.6 Entradas (novas / alteradas)
```
inputs:
  throttle            // 0..1
  clutch_pedal        // 0..1 (1 = totalmente aberta)
  brake_pedal         // 0..1 (age nos 2 eixos)
  line_lock           // bool — trava SÓ o eixo dianteiro = eixo NÃO motriz (burnout,
                      //     auditoria 4; pressupõe RWD — em FWD seria traseiro, fase 2 v2.4)
  handbrake           // bool — trava SÓ o eixo traseiro
  gear_target         // int
  shift_request       // bool (borda de subida dispara a sequência §3.10)
  ignition            // bool (aciona starter quando engine_stalled — §3.9)
  psi_front, psi_rear // pressão ajustada pelo jogador POR EIXO (v2.4 — tire per-axle
                      //     §2.4: PSI traseiro baixo + burnout é o tuning central)
```

### 2.7 Constantes centralizadas (defaults — v2.2)
```
constantes:
  // Motor / embreagem
  k_fmep              // N·m·s/rad — atrito rotativo do motor (default 0.05; faixa 0.03–0.08)
  k_pump              // N·m — perdas de bombeamento a throttle FECHADO (default 25;
                      //        faixa 15–40; por construção → 0 em throttle cheio)
  fade_termico        // fator térmico da embreagem (§3.2) — FASE 1: constante 1.0
  // Troca de marcha
  k_rev               // s/rad — ganho do rev-match (§3.10); default 0.01
                      //        (throttle cheio com mismatch ≥ ~100 rad/s)
  // Idle (§3.1)
  k_idle              // 1/(rad/s) — ganho P do idle (default 0.015 — v2.4: 0.002 não
                      //        vencia o atrito ~29 N·m com erro típico de 10 rad/s;
                      //        0.015 satura o cap 0.15 nesse erro; calibrar no §7)
  omega_idle          // rad/s — referência do idle (default ~84 ≈ 800 rpm)
  T_amb               // °C — ambiente da pista (default 25; ler da pista se o jogo expuser)
  // Ambiente / gravidade (v2.6 — sem default, cada implementador escolheria um valor)
  rho                 // kg/m³ — densidade do ar p/ F_drag/F_aero_z (§3.8); default 1.225
                      //        (ISA, nível do mar, 15 °C); PISTA E DYNO usam o MESMO valor
  g                   // m/s² — gravidade; default 9.81 — ÚNICA fonte p/ §3.4 (F_z0),
                      //        §3.6 (peso m_u), §3.7 (peso sprung), §3.8
  // Tolerâncias numéricas
  eps_clutch          // rad/s — stick da embreagem (1.0, lado motor)
  eps_stick           // m/s — stick do pneu (0.2) e piso do kappa (v2.5 — §3.4)
  eps_lock            // rad/s — freio considerado travado (0.5)
  eps_liftoff         // N — threshold de lift-off do contato (≈ 10 N ou 0.01·m_u·g
                      //     — v2.8, §3.6): cruzações de ruído em torno de F_neta = 0
                      //     não descolam a roda; wheelie real passa direto
```
- Fonte única destes valores — não espalhar magic numbers pelo `phys_step`.

---

## 3. Fórmulas Concretas

### 3.1 Torque do motor (interface com o mod existente)
```
T_comb = motor_torque(rpm, throttle_eff, afr, timing, boost, temp)   // já existe no mod
T_friction = k_fmep * omega_eng + k_pump * (1 - throttle_eff)   // defaults: §2.7
T_eng = T_comb - T_friction
```
- `throttle_eff` = throttle pós-corte/rev-match da troca de marcha (§3.10).
- Abaixo do idle: torque negativo (motor tenta morrer → §3.9).
- Acima do corte: redução progressiva (fuel cut / retard), **não clamp duro**.
- Idle controller (v2.3, P simples): `throttle_idle = clamp(k_idle*(omega_idle - omega_eng), 0, 0.15)`; usar `throttle_eff = max(throttle_eff, throttle_idle)` com embreagem aberta ou ponto morto. Defaults na §2.7. **v2.4:** `k_idle` 0.002 → **0.015** — com erro de 10 rad/s o ganho antigo dava ~2% de throttle, insuficiente contra o atrito de ~29 N·m (`k_fmep·omega_idle + k_pump`); o idle podia cair sozinho.
- **Clamp global (auditoria 2.2): `omega_eng = max(0, omega_eng)` ao final de cada substep, em qualquer regime.**

### 3.2 Embreagem — regimes e equações de `omega_eng`

**Capacidade e carga refletida:**
```
capacity = clutch_engagement * clutch_capacity_max * fade_termico   // fase 1: fade_termico = 1.0 (§2.7)
delta_omega = omega_eng - omega_w * gear_ratio

// Carga refletida ao motor (DEFINE auditoria 3.3; usa F_x e T_brake_eff
// do SUBSTEP ANTERIOR — evita dependência circular embreagem ↔ pneu)
// v2.2: com REDUÇÃO (g > 1) a carga vista pelo motor é MENOR — divide por (g·eff):
// conservação de potência: T_eng·omega_eng = T_wheel·omega_w/eff.
T_load_eng = Σ_driven(F_x_axle * radius + T_brake_eff_axle) / (gear_ratio * efficiency)
```

**Regimes (explícitos):**

1. **Open** (`engagement ≈ 0`):
   ```
   I_e * domega_eng/dt = T_eng
   T_clutch = 0
   ```

2. **Slip** (`|delta_omega| > eps_clutch` (~1.0 rad/s no lado motor — §2.7) **ou** `|T_load_eng| > capacity`):
   ```
   T_clutch = capacity * sign(delta_omega)
   I_e * domega_eng/dt = T_eng - T_clutch
   ```

3. **Stick** (`|delta_omega| ≤ eps_clutch` **e** `|T_load_eng| ≤ capacity`):
   - Acoplamento rígido: inércia efetiva.
   ```
   // Reflexão de inércia (v2.2 — BUG CRÍTICO da v2.1 corrigido): conservação de
   // energia com omega_eng = g·omega_w (g > 1 na redução):
   //   ½·I_w·omega_w² = ½·(I_w/g²)·omega_eng²  →  a roda reflete para o MOTOR como I_w/g².
   // (v2.1 multiplicava por g² — inércia ~750× inflada na 1ª marcha: o motor não
   // subia de giro e marcha alta ficava "mais pesada" que a baixa.)
   I_eq = inertia_eng + Σ_driven(inertia_wheel + inertia_drivetrain) / gear_ratio²
   I_eq * domega_eng/dt = T_eng - T_load_eng
   T_clutch = T_eng - inertia_eng * domega_eng/dt   // v2.3: torque TRANSMITIDO — antes
                                                    //   indefinido no stick; §3.3 e §3.7
                                                    //   dependem dele. Deve respeitar
                                                    //   |T_clutch| ≤ capacity; se violar,
                                                    //   migrar p/ slip no substep seguinte
   omega_w = omega_eng / gear_ratio         // PROJEÇÃO — única atualização de omega_w do
                                            //   eixo driven neste substep; NÃO re-integrar
                                            //   no passo 9 (v2.3 — §3.5/§4).
                                            //   v2.4: em DUPLO STICK (pneu também em stick)
                                            //   esta projeção NÃO define omega_w — prevalece
                                            //   a constraint do solo vx/radius (§3.4); a
                                            //   projeção serve SÓ para fechar a equação do
                                            //   motor, nunca p/ slip/F_x do passo 7
   ```
   - `F_x` e `T_brake_eff` de `T_load_eng` são os do **substep anterior** (lag de 1 substep — declarado e aceitável).
   - Não atribuir `omega_eng` diretamente sem ajustar o torque líquido (evita salto de energia).
   - AWD: o `Σ_driven` aparece DUAS vezes — na inércia refletida de `I_eq` (2 eixos) e na carga `T_load_eng` (soma de `F_x_axle·radius + T_brake_eff_axle` dos 2 eixos driven); `T_wheel` continua dividido por `n_driven_axles` (§3.3).
   - `T_load_eng` soma sobre EIXOS driven com valores LUMPED (§2.2) — nunca por roda.

**Cadeia do DUPLO STICK (v2.3 — embreagem travada + pneu travado: o regime que define o 60-foot. Os dois sticks são acoplados via `F_x`/`T_wheel`; resolver NESTA ordem, sem tentar fechar a circularidade dentro do mesmo substep):**
1. `a_x` do substep anterior (lag declarado, §4).
2. Pneu em stick: `F_x = (T_wheel - T_brake_eff - I_eq_wheel*a_x/radius)/radius` (§3.4).
3. Embreagem em stick: `omega_eng` integra com `I_eq`; `omega_w = omega_eng/gear_ratio` é PROJEÇÃO — o passo 9 NÃO re-integra (§3.5). Em duplo stick, o valor VIGENTE de `omega_w` é `vx/radius` (o solo ganha — v2.4); a projeção existe apenas para o fechamento do motor.
4. `T_load_eng` do próximo substep fecha a conta com o `F_x` recém-gerado.
- Fechamento exato (RWD, eff = 1): `T_wheel − F_x·r − T_brake_eff = I_eq_wheel·domega_w/dt`; com `eff < 1` fecha a menos do termo dissipado. A cadeia com lags de 1 substep converge a 240 Hz; iteração extra opcional máx. 1× (§6).

- Se `engine_stalled` → combustão cortada E regime FORÇADO para `open` (§3.9). A máquina de regimes NÃO opera normal neste estado: sem isso o stick com o carro parado força `omega_eng = 0` e o starter nunca religa.
- **Dois "sticks" distintos e independentes:** o da **embreagem** (`clutch_mode`, esta seção) e o do **pneu** (`stick_state`, §3.4). São flags separadas — não mesclar.

### 3.3 Torque na roda
```
T_wheel = (T_clutch * gear_ratio * efficiency) / n_driven_axles   // LUMPED: total do eixo (§2.2)
```
- Eixo driven **no ar**: `T_wheel` continua atuando (a roda gira livre, acelerada pelo motor) — só `F_x = 0`. Isso é o que faz a roda "cantar" no wheelie/burnout.
- **v2.3:** em `clutch_mode == "stick"`, `T_clutch` é o torque transmitido definido no §3.2 (`T_eng − inertia_eng·domega_eng/dt`) — o mesmo valor alimenta `T_reaction_motor` (§3.7).
- A reação no chassi gera momento de pitch via `T_reaction_motor` (§3.7), modulado por anti-squat na força de contato.

### 3.4 Slip ratio, temperatura, PSI, stiction e força do pneu

**POR EIXO (v2.4):** todas as fórmulas desta seção rodam com a `tire` do PRÓPRIO eixo (`axle.tire`, §2.2/§2.4) — `psi`, `psi_peak`, `mu_base`, `mu_static`, `load_sensitivity`, `temp`, `k_heat`/`k_cool` são do pneu do eixo. O burnout aquece só o pneu traseiro; PSI dianteiro e traseiro são tunados separadamente.
```
v_wheel = omega_w * radius
kappa = (v_wheel - vx) / max(abs(vx), eps_stick)   // piso = 0.2 m/s (§2.7) — v2.5/R3: ver nota no fim da seção

// Efeito de PSI (mapa simples, POR TIPO de pneu — v2.3: o pico fixo em ~35 psi
// penalizava pneu drag esvaziado, o oposto do tuning que o jogo explora)
psi_norm = clamp((psi - psi_peak + 15) / 15, 0, 1)   // rampa 0→1 saturando no pico
mu_psi  = lerp(0.85, 1.05, psi_norm)                 // street: pico ~32 | drag: pico ~10
crr_psi = lerp(0.015, 0.008, psi_norm)
// v2.4 (fase 2): dP/dt = k_psi_heat*(temp - T_amb) — o PSI sobe ~1–2 psi com o
// calor em corridas longas (Highway); na arrancada (~10 s) psi é estático (input)

// mu_eff com referência de carga DO PRÓPRIO EIXO (auditoria 2.4; v2.4: mass →
// mass_sprung + m_u do eixo — a carga estática correta NÃO inclui o peso do
// OUTRO eixo; ~2% de efeito no load sensitivity)
F_z0_front = mass_sprung * g * b_cg / L + mass_unsprung_front * g   // estática dianteira
F_z0_rear  = mass_sprung * g * a_cg / L + mass_unsprung_rear  * g   // estática traseira
mu_load = 1 - load_sensitivity * (normal_load / F_z0_axle - 1)
mu_eff = mu_base * mu_psi * max(mu_load, 0.05)      // clamp: mu_eff NUNCA ≤ 0
mu_eff *= temp_factor(temp)

// v2.5: aderência ESTÁTICA disponível — os mesmos fatores (psi/carga/temp)
// degradam TAMBÉM o regime colado. Um ÚNICO valor alimenta o clamp do stick,
// o threshold de quebra e o pico da curva de slip (abaixo): os três coincidem
// POR CONSTRUÇÃO — a transição stick→slip não dá salto de força.
mu_static_eff = mu_static * mu_psi * max(mu_load, 0.05) * temp_factor(temp)

// temp_factor (auditoria 2.5) — piecewise linear entre âncoras:
//   20 °C → 0.70  |  90 °C → 1.00  |  140 °C → 0.70
//   fora da faixa: clamp em 0.70
//   (fase 2 opcional: acima de 140 °C decai até 0.50 — pneu "gorduroso")

// ODE de temperatura POR EIXO (v2.7 — explícita; antes só prosa no §4/§8):
//   slip_power = abs(F_x * v_slip)                    [W]  (F_x do substep CORRENTE)
//   dtemp/dt  = k_heat * slip_power - k_cool * (temp - T_amb)
//   k_heat [°C/J]: aquece SÓ com slip (stick: v_slip ≈ 0 → sem calor — o burnout
//                  esquenta, o launch colado não); calibrar pelo alvo do §7
//                  (pico ~90 °C em ~5 s de burnout)
//   k_cool [1/s]:  resfriamento de Newton — decai EXPONENCIAL sem zerar
//                  ("cai perceptivelmente" entre burnout e largada, §2.4/§7)

// Curva força×slip (v2.5 — Opção A da auditoria 5): o PICO é a aderência
// ESTÁTICA disponível (mu_static_eff — o MESMO nível do clamp do stick) e a
// cauda decai para o CINÉTICO (mu_eff). A v2.4 tinha o pico em mu_eff·N com o
// clamp em mu_static·N: no instante da quebra a força caía de mu_static·N
// para ~0 e só voltava a subir até mu_eff·N — "bog" de tração no launch/60-ft
// (pneu real vai continuamente de mu_s para mu_k, sem vazio de força).
// Nota: o slip sustentado passa de 0.6·mu_eff (cauda da v2.4) para mu_eff
// (cinético pleno) — decisão explícita da Opção A, não acidente.
mu_peak  = mu_static_eff                        // pico em slip ≈ 0.2 (nível estático)
mu_slide = mu_eff                               // cauda cinética (computado acima)
slip_abs = abs(kappa)
if slip_abs < 0.2:
    mu_curve = mu_peak * (slip_abs / 0.2)                    // rampa linear ao pico
else:
    ratio    = clamp((slip_abs - 0.2) / 0.5, 0, 1)
    mu_curve = mu_peak * (1 - ratio) + mu_slide * ratio      // decai p/ o cinético

F_x_ss = mu_curve * normal_load * sign(kappa)
```

**Stiction — máquina de estados POR EIXO com histerese (auditoria 1.3); quebra e re-adesão por DEMANDA TOTAL (v2.5):**
```
v_slip = v_wheel - vx

// v2.5: T_demand_stick é a MESMA grandeza do clamp do F_x em stick — a demanda
// TOTAL na interface pneu-solo (drive + freio + inércia da roda acompanhando o
// carro). A quebra da v2.4 testava só T_wheel/r: IGNORAVA o freio (frenagem
// forte nunca saía do stick — sem lockup, sem transição p/ o cinético) e o
// termo de inércia (eixo dianteiro leve em wheelie não quebrava aderência).
// T_brake_eff e a_x: do substep anterior (lags declarados — §4).
T_demand_stick = T_wheel - T_brake_eff - I_eq_wheel * a_x / radius

if stick_state:
    if (not grounded) or abs(T_demand_stick / radius) > mu_static_eff * normal_load:
        stick_state = false                        // quebrou aderência
else:
    if grounded
       and abs(v_slip) < eps_stick                 // roda e solo sincronizados (0.2 m/s — §2.7)
       and abs(T_demand_stick / radius) < 0.9 * mu_static_eff * normal_load:
        stick_state = true                         // recuperou (histerese 0.9× sobre a MESMA
                                                   // grandeza da quebra — v2.5/R2: se a
                                                   // re-adesão testasse só T_wheel, frenagem
                                                   // com T_wheel = 0 re-colidia e quebrava a
                                                   // cada substep: chatter)
if stick_state and grounded:
    omega_w = vx / radius                          // constraint cinemática — v2.4: em
                                                   // DUPLO STICK prevalece sobre a projeção
                                                   // omega_eng/gear_ratio (§3.2); o F_x desta
                                                   // linha NUNCA usa omega_eng/g
    // v2.2: a constraint completa é domega_w/dt = a_x/r. Isolando F_x na equação
    // da roda (§3.5), esta é a força que mantém a roda sincronizada com o solo
    // ENQUANTO o carro acelera. v2.5: clamp na aderência ESTÁTICA DISPONÍVEL
    // (mu_static_eff — o MESMO valor do pico da curva: sem salto na transição).
    F_x = clamp(T_demand_stick / radius,
                -mu_static_eff*normal_load, +mu_static_eff*normal_load)
else:
    F_x = F_x_ss
```
- **Não** usar `abs(vx) < 0.3` como condição de stick. Burnout (`vx = 0`, `omega_w = 100`): `v_slip = 100*R` → grande → slip → `F_x = F_x_ss` → burnout vive.
- Sem estado + histerese, o launch oscila stick/slip a cada substep e o carro "tremedeia" na largada (chatter).
- **Termo de inércia no stick (v2.2):** sem `- I_eq_wheel·a_x/r`, `F_x = T_wheel/r` assume `a_x = 0` e subestima a força exatamente no launch (60-foot mais lento — viés sistemático na fase crítica).
- `slip_power = abs(F_x * v_slip)` usa o `F_x` **deste substep** — a nova ordem do `phys_step` (§4) calcula `F_x` ANTES da temperatura (resolve a circularidade da auditoria 3.1).
- **Continuidade da transição (v2.5; errata v2.8):** o que a Opção A garante é o **PATAMAR DE REGIME** — o pico da curva coincide com o clamp do stick (`mu_peak = mu_static_eff`), com quebra/clamp/pico na MESMA aderência disponível: sem perda PERMANENTE de tração (o "bog" da v2.4). **Há, sim, um transiente de 1 substep (v2.8 — declarado e medido, V1):** a quebra fura o `stick_state` ANTES do cálculo de `F_x`, e a roda chega ao ramo slip ainda com `omega_w = vx/radius` do substep anterior de stick ⇒ `kappa ≈ 0` ⇒ `mu_curve(0) = 0` ⇒ **`F_x ≈ 0` no substep da quebra** (medido no carro-teste: 0,0% do pico em launch parado; −2,7% a 5 m/s; −0,1% a 27 m/s — a roda fica um instante ATRÁS do solo e a força aponta levemente para trás). No substep seguinte o passo 9 já integrou `omega_w` com o excesso de torque ⇒ `kappa` salta a rampa e a força retorna — medido: ≥ 0.3·pico em **1 substep** (4,2 ms @240 Hz) no clutch dump; quebras SUAVES em velocidade (excesso de torque pequeno) levam 2–3 substeps — sempre < 15 ms @240 Hz. Depois o wheelspin sustenta em `mu_slide = mu_eff` (≈ 0,92·pico no carro-teste) — a queda estático→cinético de um pneu que "arrebentou" é física real. O que a v2.4 produzia de errado era esse vazio durando 0,05–0,15 s com teto em `mu_eff` — bog permanente, não os ~4 ms de atravessamento do pico. Teste da §7 reescrito (v2.8): valida o transiente + a recuperação, não a "ausência de degrau".
- **R3 — piso do `kappa` (v2.5):** o piso `max(abs(vx), 0.5)` deixava uma roda TRAVADA em creep (line lock + burnout) dentro da RAMPA (`|kappa| < 0.2`): a frente "travada" segurava com uma fração da aderência e o carro rastejava ≈6 cm/s através do freio. Com o piso = `eps_stick` (0.2 m/s) o creep residual cai para ≈2–3 cm/s; se o burnout exigir imobilidade total, trocar por modelo de slip por VELOCIDADE abaixo de `eps_stick` (anotado na §8).

**Combined slip (quando lateral ativo):**
```
// v2.6: o círculo usa o μ DO REGIME. Em stick o clamp de F_x é mu_static_eff·N
// (> mu_eff·N); com mu_eff na escala, F_y ≠ 0 rebaixava F_x p/ mu_eff·N com a
// roda AINDA COLADA — perda de tração em curva sem motivo físico. Na Arrancada
// F_y ≡ 0 ⇒ scale = 1 sempre (zero efeito na fase 1 — §1.1).
mu_circle = stick_state ? mu_static_eff : mu_eff
scale = min(1.0, mu_circle * normal_load / sqrt(F_x² + F_y² + 1e-6))
F_x *= scale
F_y *= scale
```
- `mu_circle` é a ponte entre os regimes do pneu e o traction circle: em stick a roda sustenta até `mu_static_eff·N` em QUALQUER direção (é o clamp do stick, §3.4); em slip, `mu_eff·N` (a cauda cinética da curva). Afeta SÓ a Highway (`F_y ≠ 0`, fase 2 — §1.1); teste da §7 atualizado (confere o μ do regime com `F_y` sintético).

### 3.5 Dinâmica da roda + freio como restrição
```
// v2.2: esta inércia vive NO LADO DE SAÍDA do câmbio — NÃO refletir gear_ratio² aqui
// (v2.1 multiplicava por g² e inflava a roda). Reflexão ÷g² só acontece em I_eq (§3.2).
I_eq_wheel = inertia_wheel + inertia_drivetrain     // eixo NÃO driven: só inertia_wheel
I_eq_wheel * domega_w/dt = T_wheel - F_x * radius - T_brake_eff
```
- Eixo em `stick_state` (pneu): pular a integração — `omega_w = vx / radius` (constraint, §3.4); o `T_brake_eff` deste eixo segue a regra do stick (drag rolante / reação estática — quadro v2.5 abaixo).
- **Eixo driven com embreagem em `stick` (`clutch_mode == "stick"`, v2.3): pular a integração TAMBÉM — `omega_w` já foi PROJETADO de `omega_eng` no §3.2.** Integrar 2× no mesmo substep (projeção + passo 9) acumulava erro e podia gerar salto de energia na largada (bug v2.3-2). Eixo NÃO driven e pneu em slip: integra normal com `I_eq_wheel`.
- Eixo no ar: `F_x = 0`; integra só com `T_wheel` e `T_brake_eff`.
- `F_x` e `T_brake_eff` são do EIXO (lumped, §2.2).

**Freio por eixo (auditoria 4 — complementaridade reescrita v2.5):** a demanda usa pedal + line lock + handbrake:
```
T_brake_demand = max(brake_pedal, line_lock (só front), handbrake (só rear)) * T_brake_max   // default por eixo: §2.2
```
Aplicar como **restrição (complementaridade)** — o freio **dissipa** energia: nunca inverte o sentido de `omega_w` e nunca aplica mais torque do que o necessário para segurar a roda:
```
omega_pred = omega_w + (T_wheel - F_x*radius) / I_eq_wheel * dt    // passo SEM freio
T_stop = I_eq_wheel * omega_pred / dt        // torque necessário p/ zerar neste substep

if T_brake_demand >= abs(T_stop):
    omega_w = 0                              // TRAVA neste substep: a demanda segura
    T_brake_eff = T_stop                     // o torque EXATO que mantém ω = 0 (≤ demanda)
else:
    // não trava: opõe-se ao movimento previsto. demanda < |T_stop| ⇒ |ω| cai
    // SEM trocar de sinal — o freio nunca cruza zero nem "motoriza" a roda
    T_brake_eff = T_brake_demand * sign(omega_pred)
    omega_w = omega_pred - sign(omega_pred) * T_brake_demand * dt / I_eq_wheel

if T_brake_demand > 0 and abs(omega_w) < eps_lock (0.5 rad/s — §2.7):
    omega_w = 0                              // snap: mata o pó numérico perto de 0
```
- **v2.5 (por que a reescrita):** o ramo de trava antigo exigia `|omega_pred| < eps_lock` **E** `T_brake_demand ≥ |T_stop|`. Com valores típicos (I ≈ 2 kg·m², T_brake_max ≈ 2400 N·m, dt = 1/240 s), o freio cheio move ω em ~5 rad/s por substep — muito mais que `eps_lock`: entre ~0,5 e ~5 rad/s a roda "não travava" e o ramo ELSE aplicava torque CHEIO na direção oposta à prevista — uma roda a +1 rad/s era lançada a ≈ −4 e devolvida: **o freio motorizava a roda no início do lockup** (chatter na frenagem forte). Agora a trava acontece SEMPRE que a demanda cobre `|T_stop|`.
- **O clamp literal `omega_w = max(0, omega_w)` NÃO é adotado:** rollback/ré tem `ω < 0` legítimo (em stick `ω = vx/radius`; a §3.7 trata ré via `sign(vx)`). A garantia correta é outra — **o freio nunca gera nem inverte rotação**; `ω < 0` só por tração/impulso, nunca por freio.
- **Eixo com o pneu em STICK** (a trava acima não se aplica — roda colada não "trava": o lockup passa pela QUEBRA de aderência via `T_demand_stick`): o `T_brake_eff` ainda precisa existir (alimenta o `F_x` do stick, a quebra e o `T_load_eng`):
```
if abs(vx) > eps_stick:
    // ROLANDO (qualquer sentido): drag conhecido, reage no patch via F_x da §3.4
    T_brake_eff = T_brake_demand * sign(omega_w)
else:
    // PARADO (|vx| ≤ eps_stick): freio ESTÁTICO — reage o torque líquido na roda
    // até a capacidade (v2.5/R5: é o que SEGURA o foot-brake launch; sem isto o
    // torque de drive passava direto e o carro atravessava o freio na largada)
    T_brake_eff = clamp(T_wheel - I_eq_wheel * a_x / radius,
                        -T_brake_demand, +T_brake_demand)
```
  ROLANDO colado, o freio não trava a roda (a reação passa pelo patch: `F_x = (T_wheel − T_brake_eff − I·a_x/r)/r`; se `|F_x| > mu_static_eff·N`, a §3.4 quebra o stick e SÓ ENTÃO a roda pode travar). PARADO, o freio estático segura o carro: com `|T_wheel| ≤ T_d` o `T_demand_stick` dá **zero** (carro imóvel de verdade, motor carregando contra o freio via `T_load_eng` — com risco real de stall, foot-brake launch); excedendo a capacidade, o excedente vira força/rastejo — como um carro real no freio.
- Line lock + handbrake viabilizam o burnout de gameplay: dianteira travada, traseira girando.

### 3.6 Suspensão, contato e massa não suspensa (reescrito)

**Geometria (pontos de montagem no chassi):**
```
z_mount_front = z_cg + a_cg * sin(theta)
z_mount_rear  = z_cg - b_cg * sin(theta)
travel        = L_free - (z_mount - z_u)      // > 0 = compressão (convenção §0)
```
- Chassi sobe → `(z_mount - z_u)` cresce → `travel` cai (droop). Squat traseiro → montagem desce → `travel` cresce (compressão). Sinais consistentes por construção.

**Contato unilateral — máquina de estados (auditoria 1.1 + correção crítica v2.7):**
```
grounded   // ESTADO PERSISTENTE (init = true — §6). NÃO recomputar por geometria:
           // transiciona SÓ por duas regras — LIFT-OFF por FORÇA (ramo grounded
           // abaixo) e POUSO por GEOMETRIA (ramo airborne). Da v2.1 à v2.6 valia
           // `grounded = (z_u <= radius)` recomputado a cada substep ENQUANTO o ramo
           // grounded clampa `z_u = radius` — logo z_u NUNCA excedia radius, o ramo
           // airborne era CÓDIGO MORTO e o wheelie era FISICAMENTE IMPOSSÍVEL
           // (bug v2.7-1: sobreviveu a 6 rodadas de auditoria de fórmula).
```
- `grounded == false` → `normal_load = 0`, `F_x = 0`, `stick_state = false` (roda no ar de verdade — wheelie real).
- **As duas metades do contato unilateral:** o solo não PUXA (lift-off por força: `F_susp + m_u·g < 0`) e o solo não deixa ATRAVESSAR (pouso por geometria: `z_u ≤ radius` com `v_zu ≤ 0`). A auditoria 1.1 (v2.1) removeu a checagem por força que matava o POUSO; a v2.7 conserta o clamp que matava o LIFT-OFF — faltando qualquer das metades, o wheelie não existe.
- A checagem por força da v2.0 (`normal_load == 0`) mantinha a roda presa à cinemática do chassi e eliminava o wheelie.

**Força da suspensão no chassi (↑+; batentes — auditoria 1.4):**
```
F_spring = spring_k * travel
if travel > travel_max:  F_spring += k_bump  * (travel - travel_max)²    // bump stop
if travel < travel_min:  F_spring -= k_droop * (travel_min - travel)²    // droop stop
F_susp = F_spring + damp_c * travel_vel
```
- Sem os stops, o launch comprime além do curso, `travel` vai a valores absurdos, `F_susp` explode e o chassi dá "snap"; no wheelie, a dianteira estende além do droop e a mola puxa de forma irreal. Com `k_bump`/`k_droop` progressivos, a restauração é realista nos dois lados.

**Eixo dianteiro — `m_u` dinâmico (decisão 1.2-b; máquina de contato v2.7):**
```
if grounded:
    z_u = radius
    v_zu = 0                                    // contato: roda fixa ao solo (constraint)
    travel = L_free - (z_mount_front - radius)
    travel_vel = -(vz + a_cg * cos(theta) * omega_pitch)   // v_zu = 0 em contato
    F_neta = F_susp + mass_unsprung * g         // força líquida no eixo (mola sobre o
                                                 // chassi + peso da roda) — v2.6/Opção A:
                                                 // SEM termo aero direto (age no corpo
                                                 // §3.7, chega aqui via F_susp)
    if F_neta < -eps_liftoff:                   // v2.8: threshold eps_liftoff ≈ 10 N
        // LIFT-OFF (v2.7): a restrição unilateral N ≥ 0 foi violada — a suspensão
        // em tração (droop stop) puxa a roda PARA CIMA com mais força que o peso
        // dela. O solo não puxa: a roda DESCOLA. eps_liftoff (§2.7): blips de
        // 1 substep em torno de F_neta = 0 (transferência de peso SEM wheelie
        // real — ruído de integração) NÃO descolam a roda (twitch visual no
        // render); um wheelie real mergulha a F_neta ≈ −m_u·g — atravessa o
        // threshold sem atraso perceptível (V3).
        grounded = false                        // estado persistente — bloco acima
        stick_state = false
        normal_load = 0
        F_x = 0
        v_zu = 0                                // descola do repouso (a constraint
                                                 // libera com v = 0 e acelera daqui)
        // z_u PERMANECE radius neste substep — SEM epsilon GEOMÉTRICO (o
        // eps_liftoff é um threshold na FORÇA, não na geometria): a persistência
        // do estado elimina a checagem geométrica que re-colaria a roda. A
        // integração do DOF acontece no passo 12 DESTE substep (§4): v_zu
        // cresce, z_u sai de radius no mesmo frame do lift-off.
    else:
        normal_load = max(0, F_neta)            // F_neta ≥ 0 aqui; max(0,·) defensivo
else:                                           // NO AR
    travel = L_free - (z_mount_front - z_u)
    travel_vel = -(vz + a_cg * cos(theta) * omega_pitch - v_zu)
    m_u * dv_zu/dt = -F_susp - m_u * g          // reação: mola comprimida empurra a roda
                                                 // para baixo; gravidade age na roda
    normal_load = 0
    F_x = 0
    // Integração semi-implícita no passo 12 (§4):
    //   v_zu += (dv_zu/dt) * dt ;  z_u += v_zu * dt
    // POUSO — geometria + sentido descendente:
    if z_u <= radius and v_zu <= 0:
        z_u = radius
        v_zu = 0                                // pouso INELÁSTICO (sem quique da roda;
                                                 // o quique do CHASSI é do damper)
        grounded = true                         // a carga volta no substep SEGUINTE
                                                 // (lag de 1 substep — padrão da §4)
```
- No ar, a roda sobe puxada pela suspensão em extensão (droop stop), atinge o pico, cai por gravidade e pousa; o impacto é absorvido pelo damper do chassi — comportamento definido, sem zona indefinida.
- **Histerese natural (v2.7; threshold v2.8):** o lift-off exige `F_neta < −eps_liftoff` (cruzações de ruído em torno de 0 não contam — e o critério continua sendo de FORÇA, nunca recomputado por geometria) e o pouso exige o DOF INTEIRO de volta (`z_u ≤ radius` **E** `v_zu ≤ 0` — a roda tem de subir e voltar a cair, ~½ período da suspensão ≈ 100 ms). Chatter de contato em escala de substep é IMPOSSÍVEL por construção — a dinâmica do DOF é a histerese; nenhum flag extra é necessário.
- O guarda `v_zu ≤ 0` do pouso evita re-colidir numa substep em que a roda ainda sobe (z_u ≤ radius residual com v_zu > 0 não ocorre com Euler semi-implícito, mas custa zero).
- No ar a roda acompanha o carro LONGITUDINALMENTE (o eixo é rígido em x — sem DOF horizontal próprio): erro ≈ `mass_unsprung_front/mass` (~2–3%) em `a_x` durante o wheelie; fase 2 se importar (§8).

**Eixo traseiro — cinemático (fase 1):**
```
z_u_rear = radius                                // restrição sempre ativa
travel_rear = L_free - (z_mount_rear - radius)
travel_vel_rear = -(vz - b_cg * cos(theta) * omega_pitch)
normal_load = max(0, F_susp + mass_unsprung * g)                    // v2.6/Opção A: idem dianteiro — sem termo aero direto
```
- **Aero na carga por eixo (v2.6 — Opção A; bug da dupla contagem, §3.8):** `normal_load` NÃO tem termo aero. Da v2.1 à v2.5 o aero entrava 2× na carga (heave §3.7 E `+ F_df_axle` aqui): downforce de 1000 N somava ~2000 N ao solo (grip ~2× em carro com aero). Agora o caminho é o físico: aero no corpo → mola comprime/estende → `F_susp` → carga — com a defasagem real de ~0.1–0.2 s (uma batida da suspensão; irrelevante a substeps de 1/240 s). Lift extremo: o corpo sobe, a mola dianteira estende até o droop stop e o pull-off do DOF `z_u` solta a roda (mesma mecânica do wheelie); no eixo traseiro (cinemático), o `max(0,·)` protege. Se o teste de lift da §7 reclamar da FASE, migrar p/ o FALLBACK B (§3.8).
- Com wheelie bar (§3.11), a traseira não levanta — a restrição cinemática é honesta.
- Sem barra equipada (v2.4: a barra NUNCA "vence" — o modelo não tem stress/fadiga; "barra vencida" = sem barra): se `theta > 60°` → **game event "capotou"**; congela o pitch e encerra a passagem (a fase 1 não simula capotamento completo — ver §8).
- **Nota v2.7 (traseira, 0°–60° sem barra):** `normal_load_rear` PODE ir a 0 em transitórios com o nariz descendo (pouso duro da dianteira, frenagem no fim da passagem — o `max(0,·)` zera a carga) enquanto `z_u_rear = radius`: a traseira não descola VISUALMENTE (a restrição cinemática é sempre ativa). Num wheelie isso não ocorre (a traseira é o pivô e a carga cresce); o caso não coberto é o transitório de descida. Com wheelie bar é honesto (a barra segura o pitch no contato); sem barra, o intervalo 0°–60° fica coberto pelo fallback `theta > 60°` = "capotou". Simplificação declarada da fase 1 — o DOF traseiro (wheel hop) é fase 2 (§8).

### 3.7 Equações do chassi (heave + pitch + longitudinal — sinais normalizados)
```
// v2.2: sign(vx) — em RÉ, arrasto e rolamento empurram PARA FRENTE (opõem o
// movimento). Na v2.1 eram sempre subtraídos e ACELERAVAM o carro em ré (Highway).
// sign(0) = 0 em GML: parado, as resistências não empurram (correto — a largada
// depende só de F_x).
F_total_x = F_x_rear + F_x_front - sign(vx) * (F_drag + F_rolling)
a_x = F_total_x / mass                          // massa TOTAL: sprung+unsprung transladam juntos

// Heave (v2.2 — BUG CRÍTICO corrigido; v2.6: o aero mora AQUI e SÓ aqui): o chassi
// é o corpo SPRUNG. `F_susp` NÃO inclui o peso não suspenso (ele entra na FBD da
// roda: normal_load = F_susp + m_u·g — §3.6). Com `- mass*g` o peso m_u·g era
// contado 2× e, em repouso, sum_F_z = -m_u·g ≠ 0 → o chassi afundava lentamente.
mass_sprung = mass - mass_unsprung_front - mass_unsprung_rear    // derivado no load (§2.1)
sum_F_z = F_susp_front + F_susp_rear - mass_sprung * g + F_aero_z + F_bar
az = sum_F_z / mass_sprung

// Pitch (nariz↑ = +; braços EXATOS v2.7 — cos(theta) e alturas VIVAS)
h_traction = z_cg - h_antisquat          // v2.7: altura VIVA (era h_cg estático). Durante
                                         //   o wheelie o CG SOBE (z_cg cresce via b_cg·sin(theta)
                                         //   na cinemática da §3.6) e o momento de tração
                                         //   cresce junto — a instabilidade real do wheelie
                                         //   além do equilíbrio emerge. Em repouso z_cg = h_cg
                                         //   (init §6) ⇒ coincide com o parâmetro nominal.
M_pitch = + F_x_rear * h_traction        // tração traseira abaixo do CG → nariz↑ (squat/wheelie)
          + F_x_front * z_cg             // v2.7: altura viva (era h_cg) — tração dianteira →
                                         //   nariz↑; freio (F_x_front < 0) → mergulho
          + F_susp_front * a_cg * cos(theta)   // v2.7: braço EXATO (era a_cg puro) — a distância
                                         //   HORIZONTAL do CG ao montante é a_cg·cos(theta):
                                         //   a 60° o braço real cai à METADE. ↑ à frente do
                                         //   CG → nariz↑ (restaura dive)
          - F_susp_rear  * b_cg * cos(theta)   // v2.7: braço exato — ↑ atrás do CG → nariz↓
                                         //   (restaura squat)
          + T_reaction_motor             // reação do trem de força — DEFINIÇÃO v2.3 abaixo
          + F_drag * (h_aero - z_cg)     // v2.7: altura viva (era h_aero − h_cg) — arrasto
                                         //   acima do CG → nariz↑; default h_aero = h_cg
                                         //   zera o termo
          - F_aero_z * (x_cp - a_cg) * cos(theta)  // v2.7: braço exato; sinal CORRIGIDO na v2.3
                                         //   (era +). Regra §0: força ↑ à frente do CG → nariz↑.
                                         //   Testes: lift atrás (F>0, braço>0) → − nariz↓ ✓ |
                                         //   downforce atrás (spoiler) → + nariz↑ ✓ | downforce
                                         //   à frente → − nariz↓ ✓
          - F_bar * (b_cg + d_bar) * cos(theta)   // wheelie bar: ↑ atrás do CG → nariz↓
                                                  //   (braço EXATO: (b_cg+d_bar)*cos(theta) −
                                                  //    h_bar*sin(theta); o termo omitido é < 3%
                                                  //    p/ theta < 10° — fase 2, v2.2)

alpha_pitch = M_pitch / inertia_pitch
```
- **Braços vivos (v2.7):** em equilíbrio estático (`theta = 0`, `z_cg = h_cg` — init §6 com `L_free` escolhido para a altura de projeto) TODOS os braços vivos coincidem com os parâmetros nominais — nenhum teste/cheque existente muda. Eles só divergem no transitório (squat/wheelie), exatamente onde a v2.6 estava inexata: suspensão sem `cos(theta)` (a 30° o modelo resistia ~13% demais; a 60°, ~50% — e a wheelie bar JÁ tinha `cos` desde a v2.2: inconsistência interna agora resolvida) e alturas estáticas de `F_x`/arrasto (o CG sobe no wheelie e o braço de tração cresce — a tendência real de capotar além do equilíbrio agora emerge).

**`T_reaction_motor` — DEFINIÇÃO v2.3 (o sinal NÃO fica a cargo do implementador):**
```
T_reaction_motor = s_eng * T_clutch                 // T_clutch: torque transmitido, §3.2/§3.3
// s_eng POR LAYOUT (§2.1, definido no load do carro):
//   0   → motor LONGITUDINAL (RWD clássico): a reação do virabrequim é de ROLL (eixo x̂),
//         sem pitch. O efeito de pitch do torque de eixo (axle wrap) JÁ entra via
//         h_traction/h_antisquat — somar de novo é dupla contagem.
//   +1  → motor TRANSVERSAL, crank girando no MESMO sentido das rodas: a reação nos
//         mounts empurra o nariz PARA CIMA (mão direita: rodas +ŷ, reação −ŷ no bloco).
//   -1  → motor TRANSVERSAL, crank no sentido INVERSO (final drive que inverte — típico
//         em FWD): reação empurra o nariz PARA BAIXO.
// Verificação rápida (hub motor): acelerar → nariz↑; regeneração/freio motor → nariz↓.
// CONFIRMADO v2.4 (derivação da mão direita, revisor): longitudinal → roll ✓ (s_eng = 0
//   evita dupla contagem com o axle wrap já capturado por h_traction); transversal →
//   pitch com sinal conforme o sentido do crank ✓. Aproximação de 1ª ORDEM: captura a
//   reação dos MOUNTS do motor; a reação da carcaça do diferencial (transaxle) não é
//   modelada — aceitável na fase 1.
```
- Cheque de sanidade estático (reescrito v2.2; extendido ao aero na v2.6): em repouso, `sum_F_z = 0` exige `F_susp_front + F_susp_rear = mass_sprung * g` e o pitch exige `F_susp_front * a_cg = F_susp_rear * b_cg`. Somando as FBDs das rodas (`normal_load = F_susp + m_u·g` — Opção A, §3.6), as cargas no solo fecham `N_front + N_rear = mass * g` e o split segue a geometria (`N_front ≈ m*g*b_cg/L` — exato se a massa não suspensa respeita o mesmo weight bias). COM AERO em equilíbrio (v2.6): a heave exige `F_susp_front + F_susp_rear = mass_sprung*g − F_aero_z` e o momento `−F_aero_z·(x_cp − a_cg)` redistribui as molas ⇒ `N_front + N_rear = mass*g − F_aero_z` — downforce de 1000 N soma 1000 N (não 2000): é o teste de MAGNITUDE da §7.
- A convenção de `F_aero_z` (lift-positivo, `ClA < 0` = downforce) resolve a ambiguidade da v2.0 — sem ela o carro "levitava" em alta velocidade (auditoria 3.2). O momento aerodinâmico vertical usa o braço LONGITUDINAL `x_cp` (força vertical aplicada a uma altura não gera pitch); o arrasto (horizontal) usa a altura `h_aero`.

### 3.8 Resistências e aerodinâmica
```
F_drag   = 0.5 * rho * CdA * vx²                 // MAGNITUDE (≥ 0); o SINAL entra via
                                                 // sign(vx) na §3.7 (v2.2 — vale também em ré)
F_aero_z = 0.5 * rho * ClA * vx²                 // lift-positivo; ClA < 0 = downforce;
                                                 // rho = 1.225 kg/m³ default (§2.7 — v2.6)
// v2.6 (Opção A — DUPLA CONTAGEM corrigida; o bug existia da v2.1 à v2.5): o aero
// age UMA vez, NO CORPO sprung (heave + pitch, §3.7). Até a v2.5 ele entrava
// TAMBÉM na normal_load por eixo (`+ F_df_axle`, §3.6) — a mola já comprimia pelo
// aero da heave e a carga somava de novo: downforce de 1000 N dava +2000 N no
// solo (grip ~2× em carro com aero, trap speed ~3–5% alto; a v2.5, ao dar SINAL
// ao split p/ consertar o lift, estendeu o bug ao lift — correção pela metade).
// Agora a carga herda o aero VIA SUSPENSÃO: aero → corpo → mola comprime/estende
// → F_susp → normal_load. Defasagem física ~0.1–0.2 s (uma batida da suspensão) —
// o preço de ser literal; irrelevante a substeps de 1/240 s. O split abaixo NÃO
// entra na carga: é o valor de EQUILÍBRIO p/ telemetria/assert (teste de
// magnitude da §7) e a fórmula pronta do FALLBACK B.
F_df_front = -F_aero_z * (L - x_cp) / L          // split de EQUILÍBRIO: + = downforce no
                                                 //   dianteiro (em eq. N_front sobe por isso)
F_df_rear  = -F_aero_z * x_cp / L                // idem, traseiro
// FALLBACK B (pré-aprovado — SÓ se o teste de lift da §7 reclamar da fase ~0.15 s):
// aero direto no EIXO — somar `+ F_df_axle` de volta na normal_load (§3.6) e
// REMOVER `F_aero_z` da heave (§3.7). Mesmo equilíbrio (N = mass·g − F_aero_z),
// resposta instantânea; menos literal (o aero deveria comprimir a mola).

// v2.5: rolamento POR EIXO e SÓ com o pneu RODANDO EM ADERÊNCIA (stick). Em
// slip (patinando/travado) o atrito longitudinal já vem da curva mu(kappa) da
// §3.4 — somar crr·N dupla-contaria o cinético (~1–2% na frenagem c/ lockup).
// Roda no ar: stick_state = false (§3.6) → resistência 0.
F_rolling_front = 0;  F_rolling_rear = 0
if stick_state_front:  F_rolling_front = crr_psi_front * normal_load_front
if stick_state_rear:   F_rolling_rear  = crr_psi_rear  * normal_load_rear
F_rolling = F_rolling_front + F_rolling_rear     // MAGNITUDE; sinal via sign(vx) na §3.7 (v2.2)
// crr_psi_front = crr_psi(tire_front.psi, tire_front.psi_peak) — o mapa da §3.4
// com a TIRE DO PRÓPRIO EIXO (v2.5: declarado; antes era só implícito)
```
- `F_rolling` é POR EIXO com a carga real e SÓ com o pneu em stick (v2.5): roda no ar (`stick_state = false`, §3.6) ou em slip não gera resistência — o atrito do slip vem da curva `mu(kappa)` da §3.4 (consequência direta do wheelie e do lockup).
- Re-adesão slip→stick (v2.6, nota cosmética): `F_rolling_axle` salta de 0 para `crr·N` em UM substep (degrau ~1% de μN — imperceptível no gameplay). Se um dia incomodar, interpolar ao longo de ~3 substeps; fase 2.
- Defaults: `h_aero = h_cg`, `x_cp = a_cg` (CP no CG → só carga, sem momento de pitch).

### 3.9 Stall e restart do motor (auditoria 2.2)
```
// Deteção
if (not engine_stalled) and clutch_mode != "open" and omega_eng <= omega_stall:
    engine_stalled = true
    clutch_mode = "open"          // v2.2: MORREU → embreagem abre (piloto pisa ao morrer).
                                  // Sem isto, o stick com o carro parado força
                                  // omega_eng = 0 e o starter NUNCA religa.

// Comportamento
if engine_stalled:
    T_comb = 0                                   // corta combustão
    T_eng = -T_friction                          // só atrito desacelera
    clutch_mode = "open"                         // REGIME FORÇADO todo substep (§3.2);
                                                 // engine braking via stick deixa de existir
    omega_eng = max(0, omega_eng)                // NUNCA negativa

// Restart (input ignition — §2.6): exige embreagem PISADA (comportamento real)
if engine_stalled and ignition and clutch_pedal > 0.5:
    T_eng += T_starter                           // ~80 N·m
    if omega_eng > omega_stall * 1.2:
        engine_stalled = false                   // combustão retoma; regimes voltam ao normal
```
- Com embreagem aberta + idle controller (§3.1), não morre em ponto morto.
- Sem isto, `omega_eng` cruzava zero e ficava negativa, invertendo `delta_omega`, `T_clutch` e todo o acoplamento.
- **v2.2:** sem abrir a embreagem, o restart era impossível — carro parado + stick ativo forçava `omega_eng = 0` contra o `T_starter`: o motor "morria e não voltava" em todo launch mal feito (parecia bug, não física).

### 3.10 Sequência de troca de marcha (auditoria 2.3)
```
if shift_request and shift_phase == "none" and gear != gear_target:
    shift_phase = "opening"; shift_timer = 0

"opening":  clutch_engagement → 0 em t_open (rampa linear); throttle_eff = 0
            ao completar → "swapping"
"swapping": gear = gear_target; dura t_swap → "closing"
"closing":  clutch_engagement → alvo do pedal em t_close (rampa linear)
            rev-match opcional: throttle_eff = clamp(k_rev*(omega_w*gear_ratios[gear] - omega_eng), 0, 1)   // k_rev default 0.01 — §2.7
            ao completar → "none"
```
- Durante `shift_phase != "none"`, o regime da embreagem (§3.2) usa o `clutch_engagement` da rampa (tipicamente "open" durante a troca).
- Perda total típica: `t_open + t_swap + t_close ≈ 0.2–0.4 s` por troca — é exatamente o efeito que a v2.0 (troca instantânea) não capturava no ET.

### 3.11 Wheelie bar (auditoria 2.1)
```
// Ponta da barra (atrás do eixo traseiro, pendurada abaixo do CG)
z_tip = z_cg - (b_cg + d_bar) * sin(theta) - h_bar * cos(theta)

if z_tip <= 0:                                   // contato com o solo (z = 0)
    bar_grounded = true
    penetration = -z_tip
    v_tip = vz - ((b_cg + d_bar) * cos(theta) - h_bar * sin(theta)) * omega_pitch
    F_bar = max(0, k_bar * penetration - c_bar * v_tip)   // v2.7: SINAL do damper CORRIGIDO
                                                   // (era +c_bar·v_tip = anti-amortecimento).
                                                   // Contato viscoelástico padrão F = k·p + c·ṗ
                                                   // com ṗ = −v_tip (a TAXA DE COMPRESSÃO da
                                                   // barra — o mesmo papel de travel_vel na
                                                   // suspensão). Unilateral: só empurra ↑
else:
    bar_grounded = false
    F_bar = 0
```
- `F_bar` entra no **heave** (`+F_bar`) e no **pitch** (`-F_bar * (b_cg + d_bar) * cos(theta)` — força ↑ atrás do CG → nariz↓), conforme §3.7. **Braço exato (anotado v2.2, fase 2):** com theta > 0 o tip desloca-se horizontalmente (`h_bar·sin(theta)`), e o braço real é `(b_cg + d_bar)·cos(theta) − h_bar·sin(theta)` — erro < 3% abaixo de ~10°; só importa em wheelies extremos.
- **Sinal do damper (v2.7 — bug da auditoria teórica):** com `v_tip` = velocidade REAL da ponta (↑+, linha acima), a taxa de compressão do contato é `ṗ = −v_tip`. Com o sinal antigo (`+c·v_tip`): na APROXIMAÇÃO (`v_tip < 0`) o damper REDUZIA a força (lutava contra a mola — a barra ficava mole justamente no impacto) e na SEPARAÇÃO (`v_tip > 0`, corpo subindo) INJETAVA energia (força ↑ extra num corpo subindo = trabalho positivo — a barra "chutava" o carro a cada quique; anti-amortecimento). Com `c_bar ≈ 0.3·k_bar` (sugestão vigente até a v2.7 — a §2.5 agora recomenda `≤ 0.15·k_bar` por estabilidade, §6) e `v_tip ~ 0.1 m/s`, o termo errado dominava a mola em ~3× — pogo na largada. Teste da §7 atualizado (detector de chatter da barra).
- `k_bar` alto + substeps evitam a barra "atravessar" o chão em launches brutais.
- **Artefato do clamp (v2.8 — cosmético, declarado):** na separação rápida (`c_bar·v_tip > k_bar·penetration`) o `max(0,·)` zera `F_bar` mas `penetration > 0` ainda ⇒ `bar_grounded = true` com força zero por 1 substep; o substep seguinte re-engaja naturalmente — sem efeito dinâmico.
- Sem wheelie bar equipada, `F_bar = 0` sempre → launches com torque demais levam ao game event "capotou" (§3.6).

---

## 4. Ordem do `phys_step(dt)` (reordenada)

1. **Entradas**: throttle, clutch_pedal, brake_pedal, line_lock, handbrake, gear_target, shift_request, ignition, psi_front, psi_rear (v2.4).
2. **Máquina de troca de marcha** (§3.10) → `clutch_engagement` alvo, `throttle_eff`, `gear`.
3. **Torque do motor**: `motor_torque` existente + friction; checa stall (§3.9); obtém `T_eng`.
4. **Determina regime da embreagem** e calcula `T_clutch` (§3.2) — usa `F_x` e `T_brake_eff` do substep anterior (lag declarado).
5. **Atualiza `omega_eng`** conforme o regime; clamp `omega_eng ≥ 0` (§3.1).
6. **Geometria e contato por eixo** (§3.6): `z_mount`, `travel`, `travel_vel`, `F_susp` (com bump/droop stops), `normal_load = max(0, F_susp + m_u·g)` — SEM termo aero direto (v2.6/Opção A: o aero chega via `F_susp` da heave/pitch — §3.6/§3.8) — e a **máquina de contato (v2.7)**: `grounded` é ESTADO PERSISTENTE (não recomputar por geometria); no ramo grounded, `F_neta = F_susp + m_u·g < −eps_liftoff` (v2.8) → **LIFT-OFF** (`grounded = false`, `stick_state = false`, `F_x = 0`, `v_zu = 0`; a integração do DOF acontece no passo 12 DESTE substep, sem epsilon GEOMÉTRICO — o `eps_liftoff` é um threshold na força, §3.6).
7. **Para cada eixo grounded**: slip ratio → `mu_eff` e `mu_static_eff` (PSI, `temp_factor`, load sensitivity com clamp — TUDO com a tire do eixo, v2.4/v2.5) → `F_x` via máquina de stiction (§3.4, com termo `- I_eq_wheel·a_x/r` usando o `a_x` do substep anterior; quebra/re-adesão por `T_demand_stick` — v2.5); combined slip se lateral ativo (na Arrancada `F_y ≡ 0` — §1.1).
8. **Atualiza a temperatura do pneu DE CADA EIXO** (`dtemp/dt` via `slip_power`, estado per-axle — v2.4) usando o `F_x` **do passo 7** — sem circularidade (auditoria 3.1).
9. **Integra `omega_w`** (§3.5): freio como restrição de complementaridade pura (trava quando a demanda cobre `|T_stop|`; nunca inverte nem motoriza `omega_w` — v2.5); eixo em stick do pneu segue constraint (`omega_w = vx/radius` — em duplo stick este valor PREVALECE sobre a projeção `omega_eng/g`, v2.4) com `T_brake_eff` de drag rolante / reação estática (§3.5 — v2.5); **eixo driven com embreagem em `stick` NÃO integra (projeção do §3.2 — v2.3)**; eixo no ar gira livre com `T_wheel`.
10. **Calcula forças e momentos no chassi** (§3.7): anti-squat, aerodinâmica (`F_aero_z`, `F_drag`), `F_rolling` por eixo e wheelie bar (§3.11 → `F_bar`).
11. **Integra o chassi** (semi-implícito): `vx, vz, omega_pitch, x, z_cg, theta`.
12. **Integra o DOF dianteiro se no ar** (§3.6 — INCLUINDO o substep do próprio lift-off): `v_zu += (dv_zu/dt)·dt; z_u += v_zu·dt` (semi-implícito); **pouso**: `z_u ≤ radius` E `v_zu ≤ 0` → `z_u = radius; v_zu = 0; grounded = true` (inelástico; a `normal_load` volta no substep seguinte — lag declarado).
13. **Substeps**: repete 2–4 vezes com `dt/substeps` se necessário. Ao SAIR do loop, safety net (v2.6: `vx`; v2.8: estendida): `vx = clamp(vx, -150, 150)` (rede ANTI-BLOWUP, NÃO vmax de gameplay: 150 m/s = 540 km/h fica acima até de Top Fuel ~147 m/s, enquanto o ±100 do rascunho da auditoria viraria vmax acidental das classes extremas); `vz = clamp(vz, -30, 30)` (heave real: |vz| ≤ ~3 m/s no pouso mais duro); `omega_pitch = clamp(omega_pitch, -20, 20)` (pitch real: ω ≤ ~2 rad/s no wheelie; 20 rad/s @ 1/240 s = 0.083 rad/substep — ainda integrável; SEM isto um blowup de `omega_pitch` NaN-eia o estado via `theta` → `z_cg` → `vz`). Qualquer clamp que engajar é bug — logar (V4: 0 engajamentos em todos os cenários de referência, incluindo o lite marginal).
14. **Expõe telemetria**: slip, normal_load, T_wheel, rpm, temp, psi (por eixo — v2.4), `stick_state`, `clutch_mode`, `shift_phase`, `engine_stalled`, `z_u`, `bar_grounded`.

**Lags declarados dentro do substep (v2.2 — todos aceitáveis a dt ≈ 1/240 s; FAZEM PARTE do design: não reordenar os passos para "resolvê-los", isso reintroduz a circularidade que a §9 corrigiu):**

| Lag | O quê usa o quê | Por quê é seguro |
|---|---|---|
| Passo 4 | `T_load_eng` usa `F_x` e `T_brake_eff` do substep anterior | Embreagem ↔ pneu sem ciclo (auditoria 3.3) |
| Passo 6→7 | `mu_eff`/`F_x` usam `normal_load` derivado de `travel`/`theta` do substep anterior (`theta` só é integrado no passo 11) | Erro O(dt) na carga; 1/240 s é fino o bastante |
| Passo 7 | `F_x` e `T_demand_stick` (quebra/re-adesão) em stick usam `a_x` e `T_brake_eff` do substep anterior (§3.4 — v2.5) | Troca de lags com o passo 9, sem ciclo no MESMO substep |
| Passo 8 | `slip_power` usa o `F_x` DESTE substep | Sem circularidade (auditoria 3.1) |
| Passo 9 | `omega_w` do eixo driven em stick da embreagem NÃO integra — só projeção (§3.2) | Evita dupla integração/salto de energia (v2.3-2) |
| Passo 10 | `F_drag`/`F_aero_z` usam o `vx` do substep anterior (`vx` só é integrado no passo 11 — v2.5) | Forças suaves em `vx`; erro O(dt) a 1/240 s |
| Passo 6 | `normal_load` herda o aero via `travel`/`theta` do substep anterior (Opção A v2.6: sem termo aero direto na carga — §3.6/§3.8) | Defasagem física da suspensão (~0.1–0.2 s p/ assentar); em equilíbrio o split por eixo fecha por `x_cp` |
| Passos 6/12 | lift-off usa o `F_susp` da geometria do estado do substep anterior (`z_cg`/`theta` integrados no passo 11); no pouso, a `normal_load` volta 1 substep depois (v2.7) | Mesmo erro O(dt) dos demais lags — imperceptível a 1/240 s |

---

## 5. Interface com o mod existente

- **Entrada obrigatória**: `T_comb = global.motor_torque(rpm, throttle, afr, ...)`  
  (já calcula distribuidor, carburador, turbo, peças, desgaste).
- **Saída**: o novo sistema escreve `vx`, `rpm`, `omega_w`, `normal_load`, `slip`, `tire_temp` (por eixo — v2.4) em variáveis globais que a UI/dyno/AI já leem. **Novas saídas v2.1**: `engine_stalled`, `shift_phase`, `stick_state` (por eixo), `bar_grounded`, `z_u_front` (para a render do wheelie).
- **Ponto de injeção**: substituir o bloco que calcula `_base_acceleration` e aplica movimento. Manter os patches de torque.
- **Dyno**: deve chamar a mesma função de torque (estático) para consistência.
- **PSI**: ler a pressão que o jogo já expõe e alimentar `tire_front.psi` / `tire_rear.psi` (per-axle — v2.4).
- **Inputs novos a mapear**: line_lock, handbrake, ignition (ignição/starter) e brake por eixo — já existem como mecânicas de gameplay no jogo.

---

## 6. Notas de implementação GML

- Fixed timestep interno com acumulador. Clamp de substeps máximos (ex.: 4).
- Evitar `division by zero` (slip já regularizado; `F_z0_axle` é constante positiva por construção).
- **Dois "sticks" distintos**: embreagem (`clutch_mode`) e pneu (`stick_state`) — flags separadas, ciclos separados. Mesclar é o caminho mais curto para bug. **Porém acoplados** via `F_x`/`T_wheel`: no duplo stick (launch), seguir a cadeia declarada no §3.2 — não "resolver" a circularidade por conta própria. Em duplo stick, `omega_w = vx/radius` prevalece (constraint do solo); a projeção `omega_eng/g` só fecha a equação do motor (v2.4).
- A histerese do stick custa 2 comparações por eixo; em debug, logar transições/s (chatter aceitável < ~5/s).
- `omega_eng = max(0, omega_eng)` em todo substep, em qualquer regime.
- **Não reordenar o `phys_step`** sem revisar a dependência `slip_power → F_x` (auditoria 3.1) nem os lags da §4 (v2.2).
- Stick do pneu: para refinar o `a_x` do termo de inércia, 1 iteração dentro do substep basta (recalcular `F_x` com o `a_x` recém-integrado); a 240 Hz, mais iterações não mudam nada.
- Integração de `z_u`/`v_zu` semi-implícita, igual ao chassi (§4, passo 12).
- **Estabilidade dos contatos (v2.7; errata de MASSA v2.8):** Euler semi-implícito é estável se `ω·dt < 2` e `c·dt/m_eff < 2`, com `m_eff` = MASSA EFETIVA do ponto de contato (força vertical num corpo a distância r do CG acopla heave+pitch: `m_eff = 1/(1/mass_sprung + r²/inertia_pitch)`) — NÃO a massa total (foi o erro da v2.7). Piores casos: droop stop (`k_droop` ~100× `spring_k` numérico, Δ ~5 cm → rigidez tangente `2·k_droop·Δ` ≈ 6e5 N/m, `m_u` ~50 kg → ω ≈ 110 rad/s) → ω·dt ≈ 0.46 @ 1/240 s (~0.9 no lite 1/120 s) — folga 2–4×. **Wheelie bar (v2.8):** o contato age a `r_bar = b_cg + d_bar` do CG ⇒ **`m_eff_bar = 1/(1/mass_sprung + r_bar²/inertia_pitch)`** ≈ **314 kg** no carro-teste (m_s 1390 kg, I_pitch 1040 kg·m², r_bar 1,60 m) — ~4,4× menor que a massa usada na v2.7 (a "folga confortável" declarada não se sustentava). Critério ⇒ **`c_bar < 2·m_eff_bar/dt`**: < ~1.5e5 N·s/m @ 1/240 s e **< ~7.5e4 N·s/m @ 1/120 s (lite)** no carro-teste. Com `c_bar = 0.3·k_bar` (sugestão pré-v2.8; k_bar 3e5 → 9e4): `c·dt/m_eff` ≈ 1.19 @ 1/240 s (60% do limite — estável SEM folga: era o caso do S4 v2.7) e **2.39 @ 1/120 s — INSTÁVEL** (V2b: no lite a barra re-engaja 8× vs 2, θ pico 12,9° — a margem virou chatter; carros com `m_eff` menor ou `k_bar` no teto podem pogo de verdade: sedan genérico com k_bar 5e5 e 0.3·k_bar → 2.40 @240 Hz, instável até no full). **Correção v2.8: `c_bar ≤ 0.15·k_bar` (§2.5)** → 0.60 @ 1/240 s / 1.19 @ 1/120 s (estável nos DOIS modos, k_bar 3e5); o engajamento usa mais curso (V2b @240: θ pico 8,9°, penetração 0,66 cm vs 5,9°/0,01 cm com 0.3·k_bar) — dentro dos critérios da §7; quem quiser barra "mais dura" sobe `c_bar` até o limite `2·m_eff_bar/dt`, checando por carro E rate. No teto da faixa (`k_bar` 5e5) o 0.15·k_bar dá 1.99 @120 — na borda: **carro com barra rígida roda em modo full (240 Hz)** (linha abaixo). A rigidez nunca foi o problema: ω = sqrt(k_bar/m_eff) ≈ 31 rad/s → ω·dt ≈ 0.13 @ 1/240 s (0.26 no lite).
- Performance: modo lite (substeps = 2, sem relaxation length) por padrão — v2.8: carro com wheelie bar em lite exige `c_bar ≤ 0.15·k_bar` com `k_bar` ≤ ~3e5 (metade inferior da faixa §2.5); `k_bar` no teto (5e5) → rodar em modo full (240 Hz) — estabilidade acima.
- **Condições iniciais (frame 0 — auditoria 4):**
  ```
  vx = 0; x = 0; vz = 0; theta = 0; omega_pitch = 0
  mass_sprung = mass - Σ mass_unsprung      // v2.2: derivado no load do carro (§2.1)
  z_cg = z_cg_static        // resolver o equilíbrio estático (v2.2, corpo sprung):
                            //   F_susp_front = mass_sprung*g*b_cg/L  e
                            //   F_susp_rear  = mass_sprung*g*a_cg/L
                            //   (ou assentar 0.5–1.0 s de simulação antes da largada)
                            //   v2.7: escolher L_free (por eixo) de forma que o equilíbrio
                            //   sente o carro na ALTURA DE PROJETO (z_cg_static = h_cg) —
                            //   os braços VIVOS da §3.7 coincidem com os nominais em repouso
  omega_eng = idle (~800 rpm ≈ 84 rad/s);  omega_w = 0 (2 eixos)
  clutch_pedal = 1 (aberta) ou input do jogador;  gear = 1 (ou N)
  temp = T_amb (2 eixos);  psi = default POR EIXO do carro (street/drag — v2.4)
  stick_state = true (2 eixos — v2.5: com line_lock + throttle no staging do burnout o
                      traseiro quebra o stick no 1º substep, 4,2 ms @240 Hz: imperceptível;
                      opcional iniciar false no modo burnout);  engine_stalled = false
  shift_phase = "none";  bar_grounded = false
  z_u = radius (2 eixos);  v_zu = 0;  grounded = true (eixo dianteiro — estado persistente §3.6, v2.7)
  ```
- Reset de estado ao trocar de carro ou reiniciar corrida — incluir `stick_state`, `engine_stalled`, `shift_phase`, `temp` (2 eixos — v2.4) e `bar_grounded`.
- AI: mesma física + controlador simples de launch e shift (pode ser adicionado depois).
- **Stick da embreagem**: usar inércia efetiva (§3.2) em vez de atribuição direta de `omega_eng`.

---

## 7. Validação mínima

- Carro parado + throttle alto → wheelspin controlado (não explode).
- **Equilíbrio estático (v2.2)**: carro parado 2 s em pista plana → `z_cg` e `theta` NÃO derivam (`sum_F_z = 0`; valida o `mass_sprung` — na v2.1 o chassi afundava lentamente). Com `z_cg_static = h_cg` (init §6), os braços VIVOS da §3.7 (v2.7) coincidem com os nominais — as cargas estáticas não mudam.
- **Reflexão de inércia (v2.2)**: 1ª marcha sobe de giro com ET plausível; `I_eq ≈ inertia_eng + (I_w + I_d)/g²` (NÃO ~750×); marcha alta não fica "mais pesada" que a baixa entre trocas.
- **Ré (v2.2)**: em ré com throttle cortado, o carro desacelera — drag/rolamento nunca aceleram o carro.
- **Sinal aero (v2.3)**: downforce com spoiler atrás do CG LEVANTA o nariz em trap speed; downforce à frente mergulha; lift dianteiro levanta ainda mais (instabilidade real).
- **Duplo stick (v2.3)**: launch com embreagem travada + pneu aderente → `omega_w` atualizado UMA vez (projeção); logar `T_wheel − F_x·r − T_brake_eff − I_eq_wheel·domega_w/dt ≈ 0` (fechamento do §3.2).
- **`T_clutch` no stick (v2.3)**: telemetria mostra `T_clutch = T_eng − inertia_eng·domega_eng/dt`, coerente com `T_wheel` e ≤ capacity.
- **Reaction (v2.3)**: RWD longitudinal → `T_reaction_motor = 0` (wrap coberto pelo anti-squat); transversal → o nariz responde na direção declarada por `s_eng`.
- **PSI por tipo (v2.3)**: pneu drag (traseiro) em ~10 psi → `mu_psi ≈ 1.05`; em 35 psi, penalizado. Street: o contrário.
- **Burnout (auditoria 1.3; alvo temporal v2.6)**: parado + line lock + throttle cheio → roda traseira gira (`stick_state` NÃO mata o spin), a temperatura do pneu TRASEIRO sobe (a dianteira fica ~T_amb — estado per-axle, v2.4) e `temp_factor` melhora μ até o pico — **~90 °C (pico) em ~5 s de burnout**: é isto que calibra `k_heat` (§2.4; 10 °C em 5 s = pneu nunca chega ao pico, 100 °C+ = instantâneo); superaquecido (> 140 °C), μ cai de volta; `k_cool`: entre o burnout e a largada (~10–30 s) a temp CAI perceptivelmente (não zera).
- **Launch sem chatter**: `stick_state` não alterna mais que ~5×/s (log de debug na largada).
- Launch com μ alto → 60-foot dentro dos alvos da tabela v2.4 (1,9–2,1 s p/ a classe de referência); com o termo de inércia do stick (v2.2), o 60-foot melhora vs. `T_wheel/r` puro (sem viés de `a_x = 0`).
- **Wheelie real (auditorias 1.1/1.2; máquina de contato v2.7)**: com CG alto + torque alto: (i) o lift-off transiciona `grounded = false` com `F_susp_front + m_u·g < −eps_liftoff` (v2.8; telemetria); (ii) `z_u_front > radius` cresce até o pico; (iii) pouso com `v_zu ≤ 0` → `grounded = true`, `v_zu = 0`, sem explosão numérica; (iv) SEM chatter de contato (transições de `grounded` < ~10/s — a dinâmica do DOF é a histerese); (v) REGRESSÃO: sem a máquina v2.7 este teste FALHA com `z_u_front ≡ radius` (era o estado da v2.6 — manter este teste no gate é o que garante que o bug não volta); (vi) pouso amortecido SEM `k_tire` (pneu rígido — fase 1, §8): excursão de `z_cg` após o pouso < ~5 cm (a suspensão é a única absorvedora do impacto; medido no carro-teste: 1,2 cm — V3).
- **Wheelie bar (auditoria 2.1; damper v2.7)**: `theta` satura no contato da barra, sem capotar; `penetration` de regime < ~2 cm; `F_bar > 0` no contato e DECRESCENDO após o pico (viscoelástico `− c_bar·v_tip` — com o sinal antigo a barra INJETAVA energia: `bar_grounded` oscilando > ~10/s ou `vz` "pogo" na largada = bug de sinal do damper OU `c_bar` fora do limite de estabilidade do rate — §6 v2.8: o pogo também vem da INTEGRAÇÃO); sem barra, `theta > 60°` dispara "capotou".
- Anti-squat reduz o squat/wheelie de forma perceptível.
- **Stall (auditoria 2.2 + v2.2)**: soltar a embreagem em rpm < `omega_stall` → motor morre, embreagem abre AUTOMATICAMENTE e só religa com ignição + pedal pisado; `omega_eng` nunca fica negativa (assert em todo frame).
- **Troca de marcha (auditoria 2.3)**: ET com a sequência completa fica 0,2–0,4 s pior por troca que a troca instantânea (sanity do efeito no 1/4 mile).
- **Clamps (auditoria 2.4; limite relaxado v2.5)**: `assert mu_eff >= 0.03 * mu_base` (o 0.05 antigo falhava legitimamente: `mu_psi = 0.85` × `mu_load = 0.05` → `0.0425·mu_base < 0.05·mu_base`) e `assert omega_eng >= 0` em todo frame.
- **Sinal de `F_aero_z` (auditoria 3.2)**: carro com `ClA < 0` NÃO levanta em alta velocidade; com `x_cp` à frente do CG, downforce mergulha o nariz.
- **Aero sem dupla contagem (v2.6 — MAGNITUDE, não só comportamento)**: downforce `F_aero_z = −1000 N` em `vx` estável (~200 km/h, throttle parcial até o equilíbrio) → `N_front + N_rear ≈ mass·g + 1000` — da v2.1 à v2.5 fechava `mass·g + 2000` (aero na heave E na carga: grip ~2× em carro com aero) e os testes de COMPORTAMENTO acima passavam por erro disfarçado de realista. Conferir também o trap speed do carro com asa contra a tabela de alvos (a dupla contagem inflava ~3–5%).
- **Lift com sinal nas cargas (v2.5; caminho v2.6)**: carro com `ClA > 0` (lift local) → `normal_load_front` CAI com vx² — agora VIA SUSPENSÃO (Opção A: o corpo sobe, a mola dianteira estende, `F_susp_front` cai; na v2.4 o lift entrava no pitch mas NÃO na carga e o teste antigo passava por acidente). Lift extremo → o droop stop esgota e o pull-off do DOF `z_u` solta a roda (perda de contato por geometria); magnitude `N_front + N_rear = mass·g − F_aero_z` (1×). Se a resposta for LENTA DEMAIS p/ o gameplay (fase da suspensão ~0.15 s), ativar o FALLBACK B (§3.8) e re-rodar este teste.
- **Transição stick→slip no launch (v2.5; critério v2.8 — errata do "sem degrau")**: RWD com quebra de tração → **no substep da quebra `|F_x_rear| < 0.05·mu_static_eff·N`** (transiente de 1 substep: a quebra fura o `stick_state` com `omega_w = vx/radius` ainda ⇒ `kappa ≈ 0` ⇒ `mu_curve(0) = 0`; medido: 0,0% parado, ≤ 2,7% a 5 m/s); **recupera a ≥ 0.3·`mu_static_eff·N` em ≤ 2 substeps** (medido: 1 no clutch dump; quebras suaves: 2–3; tudo < 15 ms @240 Hz) — NÃO fica abaixo disso por mais de 2–3 substeps consecutivos (o "bog" da v2.4 — queda de força por ~0.05–0.15 s com teto em `mu_eff` — continua sendo o que este teste pega; o dip instantâneo é esperado, não é o bog); o PICO da curva está em `mu_peak·N = mu_static_eff·N` (`slip ≈ 0.2`) — MESMO valor do clamp do stick: patamar de regime sem perda PERMANENTE de tração.
- **Lockup na frenagem (v2.5)**: pedal cheio → o eixo dianteiro TRANSITA stick→slip quando `|T_demand_stick|/r > mu_static_eff·N` (a v2.4 via só `T_wheel`: a frenagem ficava eternamente em stick — sem lockup, sem ABS natural); em slip, `omega_w` NUNCA troca de sinal por ação do freio (complementaridade §3.5) e `F_x_front` satura no cinético (`−mu_slide·N`).
- **Foot-brake launch (v2.5/R5)**: parado com freio a fundo e torque de drive abaixo da capacidade do freio → o carro SEGURA (`T_brake_eff` estático reage o drive — §3.5) e o motor carrega contra o freio (risco real de stall); excedendo a capacidade, rasteja/quebra aderência como um carro real. Sem o freio estático, o carro atravessava o freio na largada.
- **Weight bias**: com `a_cg ≠ b_cg`, o split estático segue `§3.7` (checar `F_susp_front*a_cg == F_susp_rear*b_cg` no equilíbrio).
- Mudança de PSI (por eixo) altera μ e rolling resistance SÓ do eixo ajustado (v2.4).
- Soltar acelerador → engine braking reduz velocidade.
- Trap speed emerge do equilíbrio potência vs. arrasto (sem vmax artificial).
- Dyno mostra a mesma curva de torque que a pista usa.
- **Combined slip como interface (v2.4; regime v2.6)**: na Arrancada `F_y ≡ 0` — o teste antigo ("na Highway, combined slip limita a soma") passava trivialmente e não validava nada; validar a fórmula de escala INJETANDO `F_y` sintético (p.ex. `F_y = 0.5·mu_eff·N`) e conferindo o clip de `F_x` — em STICK o círculo satura em `mu_static_eff·N` (v2.6: `mu_circle` — com `mu_eff` a escala rebaixava `F_x` de `mu_static_eff·N` p/ `mu_eff·N` com a roda colada), em slip em `mu_eff·N`. A validação lateral real (`delta`, yaw) pertence ao modo Highway (§1.1).

**Alvos numéricos de validação (v2.4)** — "razoável" não é mensurável; ET simulado fora da faixa de forma sistemática (> ~5%) indica bug de física, não tuning:

| Classe de referência | 60-foot | 1/4 mile | Trap speed |
|---|---|---|---|
| Street ~500 HP (drag radial atrás, launch bem executado) | 1,9–2,1 s | 11,5–12,0 s | ~190 km/h |

- **A classe street ~500 HP é a REFERÊNCIA DE ACEITAÇÃO da fase 1 (v2.6 — gate de implantação: a física só entra no jogo com esta tabela batendo).** Demais classes: definir alvos calibrados contra tempos reais de referência ANTES de congelar a física.

---

## 8. Fora de escopo (fase 1)

- Soft-body completo (RoR).
- Física lateral da Highway (modelo bicicleta: `delta`, yaw rate, `F_y(α)`) — modo separado, decisão v2.4 (§1.1); o traction circle permanece como interface (§3.4).
- DOF dinâmico da massa não suspensa **TRASEIRA** (wheel hop traseiro) — o dianteiro entrou na fase 1 (decisão 1.2-b); o traseiro segue cinemático.
- Airborne total (2 eixos no ar / saltos) e capotamento completo — a fase 1 trata `theta > 60°` sem barra como game event (§3.6).
- Tire relaxation length (adicionar só se houver instabilidade).
- Braço exato da wheelie bar no pitch (`h_bar·sin(theta)` — §3.7/§3.11): anotado na v2.2; entra na fase 2 junto com wheelies extremos.
- Dinâmica térmica do PSI: `dP/dt = k_psi_heat*(temp - T_amb)` (~1–2 psi na Highway) — v2.4; na arrancada psi é estático.
- Slip por VELOCIDADE em baixa velocidade (`|vx| < eps_stick`): com o piso do ratio o carro ainda rasteja ~2–3 cm/s com o eixo travado no line lock durante o burnout; trocar a rampa por velocidade de slip se o gameplay exigir imobilidade total (v2.5 — §3.4, reflexão R3).
- Marcha à ré (`gear_ratio < 0`) — v2.4; a arrancada não precisa (posicionar no staging é gameplay).
- Line lock no eixo correto para FWD (traseiro) — v2.4; a fase 1 pressupõe RWD.
- Transmissão automática (conversor de torque: `K_factor`, `stall_ratio`, lock-up) — a fase 1 cobre SÓ CÂMBIO MANUAL com embreagem de pedal 0–1 (declaração v2.6, §2.3/§3.2); se o jogo expuser automáticos, spec própria na fase 2.
- Clutch bite point não-linear — `capacity = clutch_engagement·capacity_max` é LINEAR no pedal (§3.2); um disco real engata sobretudo nos ÚLTIMOS ~30% do curso. Mapa `pedal → engagement` (lookup) na fase 2; sem ele o feathering/launch control fica "digital" (soltar metade do pedal quase não muda o comportamento).
- `mu_psi` sem penalização de SOBRE-inflação (v2.7): acima de `psi_peak + 15` o mapa satura no máximo (`mu_psi = 1.05`) — pneu real perde aderência acima do pico também (patch de contato menor/carcaça rígida). Fase 2: rampa descendente simétrica pós-pico (o `psi_norm` passa a CAIR após `psi_peak + 15`).
- Decomposição do aero no frame do CORPO com pitch (`F_aero_z`/`F_drag` com cos/sin(theta)) — a fase 1 usa o frame do MUNDO; só difere em wheelie a alta velocidade (cenário raro com barra).
- DOF horizontal da roda no ar (§3.6: a roda acompanha o carro em x; erro ~2–3% em `a_x` durante o wheelie).
- Clutch térmico avançado e turbo lag dinâmico (já parcialmente no mod).
- Colisão carro-carro.

---

## 9. Histórico de revisão (auditoria → correção)

### v2.8 — 8ª rodada de auditoria (2026-11-10)

**Contexto:** 2 carryover (apontados na revisão do v2.7 e não incorporados) + 2 notas defensivas novas. Nenhum item muda fórmula de física: o item 1 é errata de texto + critério de teste; o item 2 é a MESMA análise com a massa correta; 3a/3b são defesa em profundidade.

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 **Claim "sem degrau" da stick→slip INEXATO (carryover):** no substep da quebra o `stick_state` fura ANTES do cálculo de `F_x` ⇒ cai no ramo slip com `omega_w = vx/radius` ainda (do substep anterior de stick) ⇒ `kappa ≈ 0` ⇒ `F_x ≈ 0` por 1 substep, retornando ao pico em 1–3 substeps. O teste da §7 como escrito ("no substep da quebra \|F_x\| = mu_static_eff·N") FALHA — o implementador leria a física como errada. O que a v2.5 consertou de verdade é o PATAMAR: pico = clamp = `mu_static_eff` (sem o bog permanente da v2.4) | Importante (texto + teste) | Nota da §3.4 reescrita com o transiente DECLARADO + números medidos (V1: `F_x` = 0,0% do pico na quebra parado; −2,7% a 5 m/s; −0,1% a 27 m/s; recuperação a ≥ 0.3·pico em 1 substep). Critério da §7 trocado: **"no substep da quebra `\|F_x_rear\| < 0.05·mu_static_eff·N`; recupera a ≥ 0.3·`mu_static_eff·N` em ≤ 2 substeps; pico da curva = clamp em `slip ≈ 0.2`"** — o dip de 1 substep é comportamento esperado, não bug | §3.4, §7 |
| 2 **Estabilidade da wheelie bar com a massa ERRADA (carryover):** a v2.7 ("c·dt/m ≈ 0.45 — folga confortável mesmo no lite") usava a massa total/sprung (~1500 kg); o contato age a `r_bar = b_cg + d_bar` do CG e a massa efetiva do DOF acoplado heave+pitch é **`m_eff_bar = 1/(1/mass_sprung + r_bar²/inertia_pitch)`** ≈ **314 kg** no carro-teste — e ≈ 261 kg no sedan genérico do revisor (a conta dele, com I_pitch ≈ 2400 kg·m², dá 430 kg; o correto pela fórmula da própria spec é ≈ 1260 kg·m² ⇒ o quadro real é MAIS grave). Com `c_bar = 0.3·k_bar`: `c·dt/m_eff` ≈ **1.19 @ 1/240 s** (60% do limite — estável sem folga: era o caso do S4 v2.7) e **≈ 2.39 @ 1/120 s lite — INSTÁVEL** (pogo por INTEGRAÇÃO, não por sinal); num sedan com `k_bar` 5e5: 2.40 @240 Hz — instável até no full | Importante (calibração) | §6 reescrita com `m_eff_bar` e o critério **`c_bar < 2·m_eff_bar/dt`** (≈ 1.5e5 @240 Hz / 7.5e4 @120 Hz no carro-teste); **`c_bar` sugerido reduzido para `≤ 0.15·k_bar`** (§2.5): 0.60 @240 / 1.19 @120 — estável nos DOIS modos (k_bar 3e5); no teto da faixa (k_bar 5e5) o lite fica na borda (1.99) ⇒ **carro com barra rígida roda em modo full** (§6). Reproduzido em V2b: no lite com 0.3·k_bar a barra re-engaja 8× (vs 2) | §2.5, §6, §3.11 |
| 3a Lift-off com `F_neta < 0` ESTRITO: blips de 1 substep em torno de 0 (ruído de integração na transferência de peso sem wheelie real, ex. `F_neta = −1 N`) descolam a roda por 2–3 substeps — "piscada" de 1–2/s no render | Menor (defensivo) | **`eps_liftoff ≈ 10 N`** (ou `0.01·m_u·g`) no threshold da §3.6 (constante §2.7, passo 6 da §4); wheelie real mergulha a `F_neta ≈ −m_u·g` inteiro — não é afetado. V3: flips/timing do S2 idênticos com/sem epsilon (liftoff 79,2 ms nos dois); micro-wheelies reais do sweep (F_neta ≤ −19,6 N) passam direto; 0 blips nos cenários de referência @240 Hz — seguro de custo zero | §2.7, §3.6, §4 |
| 3b Safety net cobria só `vx` (§4.13) — blowup de `omega_pitch` (100+ rad/s) NaN-eia o estado via `theta` → `z_cg` → `vz` | Menor (defensivo) | `vz = clamp(vz, −30, 30)` e `omega_pitch = clamp(omega_pitch, −20, 20)` no passo 13 — limites ≥ 10× acima da física real (pouso: \|vz\| ≤ ~3 m/s; wheelie: ω ≤ ~2 rad/s; 20 rad/s @240 Hz = 0.083 rad/substep, ainda integrável); engajou = bug, logar (V4: 0 engajamentos em todos os cenários, inclusive o lite marginal) | §4 |

**Notas registradas da rodada anterior (não bloqueavam; sem custo):** §7 passa a validar o pouso de wheelie SEM `k_tire` — excursão de `z_cg` após o pouso < ~5 cm (medido: 1,2 cm; a suspensão é a única absorvedora — pneu rígido da fase 1, §8); §3.11 declara o artefato do clamp da barra (separação rápida: `F_bar = 0` com `bar_grounded = true` por 1 substep — cosmético).

**Verificação numérica da v2.8** (`scripts/verify_v28.py`; gráficos `download/MCML_v2.8_verificacao_numerica.png` e `download/MCML_v2.8_regressao_v2.7.png`): **regressão S1–S4b intocada** (S2: lift-off 79,2 ms, 3 ciclos de contato; S3 bug v2.6 reproduzido — `z_u ≡ radius` por 6 s; S4: 5,9°, 0,01 cm, 2 flips; S4b pogo do damper antigo: 0,68 cm, 68 kN, 10,3°); **V1** transiente da quebra medido substep a substep (valores acima); **V2** `m_eff_bar` = 314 kg (m_s 1390 kg, I_pitch 1040 kg·m², r_bar 1,60 m); limites `c_bar < 1.51e5` @240 Hz / `7.55e4` @120 Hz; 0.3·k_bar → 1.19/2.39; 0.15·k_bar → 0.60/1.19; sedan corrigido: I 1261 kg·m², m_eff 261 kg (0.3·k_bar de 5e5 → 2.40 @240 Hz); **V2b** lite @120 Hz com 0.3·k_bar: 8 re-engajes da barra, θ pico 12,9°, pen 0,20 cm (degrada sem explodir no carro-teste — a margem analítica virou chatter); com 0.15·k_bar: 2 re-engajes, θ 13,2°, pen 0,36 cm; full @240 com 0.15·k_bar: θ 8,9°, pen 0,66 cm, 4 flips — dentro dos critérios da §7 (a barra usa mais curso em vez de travar seco); **V3** epsilon inerte na referência, não suprime lift-offs reais, e o caso-limiar (hover a +15 N no full) cruza genuinamente a −28,7 N no lite (0,1 mm, sub-pixel) — passa direto; **V4** 0 engajamentos de clamp em todos os cenários. O GATE de implantação continua sendo o teste no motor do jogo (§7).

**Decisões registradas nesta revisão (v2.8):** (33) o transiente de 1 substep com `F_x ≈ 0` na quebra do stick é COMPORTAMENTO DECLARADO — a garantia da Opção A é o patamar (pico = clamp); o teste da §7 valida transiente + recuperação, não a "ausência de degrau"; (34) o critério de estabilidade da wheelie bar usa a MASSA EFETIVA (`c_bar·dt/m_eff_bar < 2`, `m_eff_bar = 1/(1/mass_sprung + r_bar²/inertia_pitch)`); default `c_bar ≤ 0.15·k_bar`; `k_bar` no teto da faixa (5e5) → modo full 240 Hz; (35) `eps_liftoff ≈ 10 N` no lift-off por força; (36) safety net estendido a `vz`/`omega_pitch`.

**Registro (recomendação do revisor acatada):** os 4 itens são errata/defesa — nenhuma fórmula de física mudou desde a v2.7. A física está fechada; **a próxima ação é implementar o `phys_step` no `obj_player_Step_0` e rodar o §7 no motor**. Retorno de mais rodadas de auditoria de texto: decrescente (a lição da v2.7 se aplica agora ao próprio spec).

### v2.7 — 7ª rodada de auditoria (2026-11-09)

**Itens da auditoria (revisão de COMPORTAMENTO de contato — pegou o que 6 rodadas de auditoria de fórmula não pegaram):**

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 **Transição grounded→airborne INEXISTENTE (ramo airborne = CÓDIGO MORTO):** `grounded = (z_u ≤ radius)` recomputado por geometria a cada substep + ramo grounded clampa `z_u = radius` incondicionalmente ⇒ `z_u` NUNCA excede `radius` ⇒ o ramo airborne JAMAIS executa ⇒ **o wheelie é fisicamente impossível** — invalidava a decisão 1.2-b, o DOF dianteiro inteiro e o teste `z_u_front > radius` da §7. O `max(0,·)` da `normal_load` clampa a carga em 0 mas o estado NÃO transiciona (a roda fica "colada com carga zero") | **Crítico (bloqueante — feature central)** | **Máquina de contato unilateral completa:** `grounded` vira ESTADO PERSISTENTE (init true, §6 — nunca recomputado por geometria); **lift-off por FORÇA** no ramo grounded (`F_neta = F_susp + m_u·g < 0` → `grounded=false, stick_state=false, F_x=0, v_zu=0`; `z_u` fica `radius` nesse substep e o DOF integra no passo 12 do MESMO substep — sem epsilon); **pouso por GEOMETRIA + SENTIDO** (`z_u ≤ radius` E `v_zu ≤ 0` → inelástico `v_zu=0`, `grounded=true`, carga volta 1 substep depois). Histerese natural: lift-off estrito + pouso exige o DOF subir e voltar (~½ período da mola ≈ 100 ms) — chatter impossível por construção | §1, §2.2, §3.6, §4, §6, §7 |
| 2 Eixo traseiro nunca descola (0°–60° sem barra) mesmo quando `normal_load_rear → 0` | Menor (declarar — design aceito) | Nota explícita na §3.6: transitórios de nariz-para-baixo (pouso duro, frenagem) podem zerar a carga traseira com `z_u_rear = radius` (a traseira não descola visualmente); num wheelie não ocorre (a traseira é o pivô); sem barra o fallback é `theta > 60°` = "capotou"; DOF traseiro = fase 2 (§8) | §3.6, §8 |
| 3 `dynamic_mu` (§2.2) nunca usado nas fórmulas — flag morto | Cosmético | REMOVIDO do struct (o branching da §3.6/§3.7 é por eixo `front`/`rear`, não por flag) | §2.2 |
| 4 `mu_psi` não penaliza sobre-inflação (`psi > psi_peak + 15` segue no máximo 1.05) | Fase 2 | Anotado na §8 (rampa descendente pós-pico) | §3.4, §8 |

**Auditoria teórica independente (v2.7 — pedida pelo revisor: "a teoria tem que ficar 100% correta e conclusiva"):**

| # | Bug/deficiência encontrada | Correção | Onde |
|---|---|---|---|
| A1 | **Braços de momento do pitch inexatos:** suspensão sem `cos(theta)` (a 30° resistia ~13% demais ao wheelie, a 60° ~50% — e a wheelie bar JÁ tinha `cos` desde a v2.2: inconsistência interna); `F_x` e arrasto com alturas ESTÁTICAS (`h_cg`/`h_traction` fixos — durante o wheelie o CG sobe via `b_cg·sin(theta)` e o braço de tração deveria crescer: a tendência real de capotar além do equilíbrio não emergia) | Braços EXATOS: `±F_susp·a_cg/b_cg·cos(theta)`, `−F_aero_z·(x_cp−a_cg)·cos(theta)`; alturas VIVAS: `h_traction = z_cg − h_antisquat`, `F_x_front·z_cg`, `F_drag·(h_aero − z_cg)`. Em equilíbrio estático (`z_cg = h_cg` por init, `L_free` na altura de projeto) tudo coincide com os parâmetros nominais — nenhum cheque da §3.7/§7 muda | §3.7, §6 |
| A2 | **Sinal do damper da wheelie bar INVERTIDO:** `F_bar = max(0, k·p + c·v_tip)` com `v_tip` = velocidade real da ponta (↑+) ⇒ na aproximação (`v_tip < 0`) o damper REDUZIA a força (lutava contra a mola) e na separação (`v_tip > 0`) INJETAVA energia (força ↑ extra num corpo subindo = trabalho positivo — anti-amortecimento; com `c_bar ≈ 0.3·k_bar` o termo errado dominava a mola em ~3×: pogo na largada) | `F_bar = max(0, k_bar·penetration − c_bar·v_tip)` — contato viscoelástico padrão `F = k·p + c·ṗ`, `ṗ = −v_tip` (o mesmo papel de `travel_vel` na suspensão); teste da §7 com detector de chatter da barra | §3.11, §7 |
| A3 | ODE de temperatura só em prosa (forma, unidades e caminho de dependência não fixados — cada implementador inventaria uma) | ODE explícita por eixo: `dtemp/dt = k_heat·slip_power − k_cool·(temp − T_amb)`, `slip_power = |F_x·v_slip|` do substep corrente (unidades: k_heat [°C/J], k_cool [1/s] — Newton, decai exponencial sem zerar) | §3.4, §4 |
| A4 | Sem verificação de estabilidade numérica dos contatos rígidos (batentes, barra) | Nota na §6 com números dos piores casos (droop stop ω·dt ≈ 0.46; barra c·dt/m ≈ 0.45 @ 1/240 s) — 2–4× abaixo do limite do Euler semi-implícito | §6 |
| A5 | Roda no ar acompanhando o carro longitudinalmente sem declaração (erro ~2–3% em `a_x` no wheelie) | Declarado na §3.6 + anotado fase 2 na §8 | §3.6, §8 |

**Verificação numérica da v2.7 (primeiro "teste de integração" da teoria — loop núcleo §3.2–§3.11 implementado fielmente em Python, 240 Hz, carro-teste 61% atrás / h = 0.70 / slick quente):** **S1** equilíbrio fecha exato (`N_f + N_r = m·g` com erro 0,01%, split 5794/8923 N pela geometria, `theta` < 0,001°, `z_cg` volta a `h_cg`); **S2** launch sem barra → **lift-off REAL em 79 ms, roda sobe 25 cm, θ pico 7,5°, pouso inelástico em 0,78 s**, 3 ciclos de contato (~1/s — sem chatter), stick do pneu 0,8/s, sem NaN, 60-ft 1,78 s (informativo — carro-teste de alta aderência, não a classe de referência); **S3 REGRESSÃO (regra v2.6): `z_u_front ≡ radius` durante os 6 s inteiros — o ramo airborne NUNCA executa; 66 substeps com `N_f = 0` clamped e a roda "colada" ao solo (o bug crítico reproduzido numericamente)**; **S4** com wheelie bar → θ satura em 5,9°, penetração máx 0,1 mm, contato único, `F_bar` pico 21 kN; **S4b REGRESSÃO (sinal antigo `+c·v_tip`): penetração 68× maior (0,68 cm), `F_bar` pico 3,2× (68 kN), θ ULTRAPASSA a barra (10,3° vs 5,9°) e a barra re-contata — injeção de energia confirmada**. Gráfico: `download/MCML_v2.7_verificacao_numerica.png`; script: `scripts/verify_v27.py` (reprodutível). O GATE de implantação continua sendo o teste no motor do jogo (§7).

**Decisões registradas nesta revisão (v2.7):** (28) contato unilateral COMPLETO = pouso por geometria E lift-off por força, com `grounded` como estado persistente — as duas metades (a auditoria 1.1/v2.1 consertou o pouso, a v2.7 o lift-off; faltando qualquer uma, o wheelie não existe); (29) braços de momento do pitch usam geometria VIVA (`z_cg`, `cos(theta)`) — coincidem com os nominais em equilíbrio (`z_cg_static = h_cg`, init §6); (30) damper da wheelie bar viscoelástico correto (`−c_bar·v_tip`); (31) ODE de temperatura explícita por eixo; (32) `dynamic_mu` removido (flag morto).

**Lição registrada (pedido do revisor):** a revisão de COMPORTAMENTO de contato pegou um bug que 6 rodadas de auditoria de fórmula não pegaram — e o spec vinha se declarando "pronto para implementação" desde a v2.4. Consequência: o status "pronta" agora exige o TESTE DE INTEGRAÇÃO do §7 rodando (a verificação numérica acima é o primeiro passo; o gate final é no motor do jogo). Revisão de texto tem retorno decrescente — implementar e rodar o §7 é a próxima ação, não mais uma rodada de revisão.

### v2.6 — 6ª rodada de auditoria (2026-11-08)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 **Dupla contagem do aero**: `F_aero_z` na heave (§3.7) E `F_df_axle` na `normal_load` (§3.6) — em equilíbrio, downforce de 1000 N somava ~2000 N ao solo (grip ~2× em carro com aero: trap speed ~3–5% alto, Highway "cola" demais, lift dianteiro cortava a carga 2×). Bug presente da v2.1 à v2.5; a v2.5 (split com sinal) o AGRAVOU estendendo-o ao lift — correção pela metade. Testes §7 passavam porque validam COMPORTAMENTO, não magnitude | **Crítico (bloqueante antes de injetar)** | **Opção A** (fisicamente correta): aero age UMA vez, no CORPO sprung — `normal_load = max(0, F_susp + m_u·g)` SEM termo aero; a carga herda via mola (aero → corpo → `travel` → `F_susp` → `N`), com a defasagem física real de ~0.1–0.2 s (irrelevante a substeps de 1/240 s). `F_df_front/rear` re-rotulados: split de EQUILÍBRIO (telemetria/assert) + fórmula do FALLBACK B pré-aprovado (aero no eixo, resposta instantânea, caso o teste de lift reclame da fase). Cheque estático da §3.7 estendido ao aero + teste NOVO de magnitude na §7 (`N_f + N_r = mass·g − F_aero_z`) | §3.6, §3.7, §3.8, §4, §7 |
| 2 Combined slip usava `mu_eff` também em STICK: com `F_y ≠ 0` (Highway), a escala rebaixava `F_x` de `mu_static_eff·N` (clamp do stick) p/ `mu_eff·N` — perda de tração em curva com a roda AINDA colada | Importante (Highway/fase 2) | `mu_circle = stick_state ? mu_static_eff : mu_eff` na escala; na Arrancada `F_y ≡ 0` ⇒ `scale = 1` (zero efeito na fase 1); teste da §7 (injeção de `F_y` sintético) atualizado p/ conferir o μ do regime | §3.4, §7 |
| 3 Transmissão automática não coberta (máquina de embreagem = pedal manual 0–1) | Menor (escopo) | Declarado: **fase 1 = SÓ câmbio manual** (nota no §2.3); conversor (`K_factor`, `stall_ratio`) anotado na §8 p/ a fase 2 | §2.3, §8 |
| 4 Clutch bite point LINEAR no pedal (disco real engata nos últimos ~30%) | Menor (fase 2) | Anotado na §8: mapa `pedal → engagement` não-linear na fase 2 (sem ele o feathering fica "digital") | §3.2, §8 |
| 5 `rho` e `g` sem default (cada implementador escolheria) | Menor | Defaults na §2.7: `rho = 1.225 kg/m³` (ISA, nível do mar), `g = 9.81 m/s²` — pista e dyno obrigados à MESMA fonte | §2.7, §3.8 |
| 6 `k_heat`/`k_cool` sem valores de referência (burnout aquece 10 °C ou 100 °C?) | Menor (calibração) | Guia no §2.4 + ALVO TEMPORAL no teste de burnout (§7): pico de μ (~90 °C) em ~5 s; resfriamento perceptível entre burnout e largada | §2.4, §7 |
| 7 Sem clamp de segurança em `vx` (blowup contaminaria telemetria/UI) | Menor | Safety net no fim do `phys_step` (§4, passo 13): `vx = clamp(vx, −150, 150)` — 540 km/h fica ACIMA de qualquer classe legítima (Top Fuel ~147 m/s); engajou = bug, logar | §4 |
| 8 `F_rolling` descontínuo na re-adesão slip→stick (0 → `crr·N` num substep, ~1% de μN) | Cosmético | Nota na §3.8: imperceptível; se incomodar, interpolar ~3 substeps (fase 2) | §3.8 |

**Decisões registradas nesta revisão (v2.6):** (24) o aero atua UMA vez, no CORPO sprung (Opção A) — a `normal_load` não tem termo aero; o pneu sente o aero pela mola (~0.1–0.2 s de fase, física real) e o split por eixo emerge de `x_cp` via pitch em equilíbrio; (25) FALLBACK B pré-aprovado: se o teste de lift exigir resposta instantânea, mover o aero p/ o EIXO (`+F_df_axle` na carga, `F_aero_z` fora da heave) — equilíbrio idêntico; (26) o traction circle usa o μ do regime (`mu_static_eff` em stick / `mu_eff` em slip); (27) a classe street ~500 HP é a referência de ACEITAÇÃO da fase 1 (gate de implantação). **Nota de aplicação:** o clamp de `vx` foi fixado em ±150 m/s (a auditoria sugeria ±100): 100 m/s = 360 km/h ficaria ABAIXO do trap de Top Fuel (~147 m/s) e o safety net viraria vmax acidental das classes extremas.

### v2.5 — 5ª rodada de auditoria (2026-11-07)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 Descontinuidade stick→slip: pico da curva em `mu_eff·N` vs clamp em `mu_static·N` — na quebra F_x caía a ~0 por alguns substeps e voltava só até `mu_eff·N` (launch "boga") | **Bug físico (bloqueante p/ launch)** | Curva Opção A: `mu_peak = mu_static_eff` (nível ESTÁTICO com os fatores psi/carga/temp), `mu_slide = mu_eff` (cauda cinética — slip sustentado passa de 0.6·mu_eff p/ mu_eff, decisão da Opção A); clamp/quebra/pico coincidem POR CONSTRUÇÃO; nota de continuidade + teste novo na §7 (o "wheelspin controlado" antigo não capturava a queda) | §3.4, §7 |
| 2 Break condition via só `T_wheel/r`: ignorava `T_brake_eff` (frenagem nunca saía do stick — sem lockup/ABS natural) e `I_eq_wheel·a_x/r` (eixo dianteiro leve em wheelie) | **Bug (bloqueante p/ frenagem)** | `T_demand_stick = T_wheel − T_brake_eff − I_eq_wheel·a_x/r` (a MESMA grandeza do clamp; lags §4) testa a quebra | §3.4, §4, §7 |
| 3 Lift entrava no pitch (§3.7) mas NÃO na `normal_load` (`F_df = max(0, −F_aero_z)` zeronava o lift no split) | Inconsistência de design | Split aero COM SINAL por eixo: `F_df_front = −F_aero_z·(L−x_cp)/L`, `F_df_rear = −F_aero_z·x_cp/L`; `max(0, ·)` da carga clampa lift extremo (roda "flutua"); teste com `ClA > 0` adicionado — o antigo passava por acidente | §3.6, §3.8, §7 |
| 4a `F_rolling` com roda travada/patinando (dupla contagem do cinético, ~1–2%) | Menor | `F_rolling_axle = 0` fora do stick — o atrito em slip vem da curva `mu(kappa)`; `crr_psi_axle = crr_psi(tire_axle.psi, tire_axle.psi_peak)` declarado per-axle | §3.8 |
| 4b Lag de `vx` em `F_drag`/`F_aero_z` (passo 10 vs integração no 11) não declarado | Menor | Linha nova na tabela de lags da §4 | §4 |
| 4c `assert mu_eff ≥ 0.05·mu_base` falha com `mu_psi = 0.85` × `mu_load = 0.05` (= 0.0425) | Menor | Limite relaxado p/ `0.03·mu_base`, com a justificativa inline | §7 |
| 4d `stick_state` inicial `true` + line lock: 1 frame "tracionado" antes de patinar | Menor (cosmético) | Nota na §6: quebra no 1º substep (4,2 ms @240 Hz) é imperceptível; init opcional `false` no staging de burnout | §6 |
| 4e `omega_w` sem proteção contra inversão na frenagem | Menor na auditoria; **bug real MAIOR na re-derivação** | Freio como complementaridade PURA: trava sempre que `T_brake_demand ≥ \|T_stop\|` — o ramo antigo exigia `|omega_pred| < eps_lock` e, com I ≈ 2 kg·m², T_d ≈ 2400 N·m e dt = 1/240 s (freio cheio move ω ~5 rad/substep ≫ eps_lock), o ELSE aplicava torque CHEIO oposto ao previsto entre ~0,5 e ~5 rad/s: ω ≈ +1 era lançada a ≈ −4 — **o freio MOTORIZAVA a roda no lockup** (chatter); sem trava, `|ω|` cai sem trocar de sinal + snap `eps_lock` | §3.5, §7 |

**Reflexão proativa — bugs latentes fechados além da auditoria (pedido do revisor "reflita sobre possíveis bugs")**

| # | Bug latente | Correção | Onde |
|---|---|---|---|
| R1 | Fatores psi/carga/temp não degradavam a aderência ESTÁTICA (clamp/quebra em `mu_static·N` puro: pneu superaquecido mantinha o grip estático; pico factado < clamp → salto residual quando fatores < 1) | `mu_static_eff = mu_static·mu_psi·max(mu_load,0.05)·temp_factor` unifica clamp + threshold de quebra + pico da curva — os três degradam JUNTOS, sem salto (é o que torna a propriedade "pico = clamp" da Opção A literalmente verdadeira) | §3.4 |
| R2 | Re-adesão testava `\|T_wheel/r\| < 0.9·mu_static·N`: com freio aplicado (T_wheel = 0) o pneu re-colidia e quebrava no substep seguinte — chatter na frenagem (só apareceria DEPOIS do item 2) | Re-adesão compara `T_demand_stick` (a MESMA grandeza da quebra) contra `0.9·mu_static_eff·N` | §3.4 |
| R3 | Piso do `kappa` em 0.5 m/s: roda TRAVADA em creep ficava na RAMPA e o carro rastejava ≈6 cm/s através do line lock no burnout | Piso = `eps_stick` (0.2 m/s): creep residual ≈2–3 cm/s; slip por VELOCIDADE abaixo de `eps_stick` anotado na §8 | §3.4, §8 |
| R4 | O clamp literal `omega_w = max(0, omega_w)` (sugestão 4e) quebraria o rollback/ré em stick (`ω = vx/r < 0` legítimo — §3.7/§7 tratam ré via `sign(vx)`) | Não adotado; a garantia correta é o freio nunca inverter/motorizar ω (item 4e) — um clamp global de sinal proibiria marcha à ré e rollback | §3.5 |
| R5 | `T_brake_eff` indefinido p/ pneu em stick: no PARADO com freio, o torque de drive passava direto pela quebra (`T_demand = T_wheel`) e o carro ATRAVESSAVA o freio na largada — foot-brake launch impossível | Freio do eixo em stick: ROLANDO → drag `T_d·sign(ω)`; PARADO (`\|vx\| ≤ eps_stick`) → reação ESTÁTICA `clamp(T_wheel − I·a_x/r, ±T_d)` que segura o carro e carrega o motor via `T_load_eng` (risco real de stall — comportamento de foot-brake real) | §3.5, §7 |

**Decisões registradas nesta revisão (v2.5):** (18) pico da curva = aderência estática DISPONÍVEL (`mu_static_eff`), cauda = cinético (`mu_eff`) — clamp, quebra e pico coincidem por construção (sem salto na transição) **[errata v2.8: o "sem salto" é o PATAMAR de regime; há um transiente de 1 substep com `F_x ≈ 0` na quebra — §3.4/§7]**; (19) quebra E re-adesão do stick usam a demanda total `T_demand_stick` (drive + freio + inércia; histerese 0.9× sobre a mesma grandeza); (20) split aero por eixo é COM SINAL (lift reduz `normal_load`, consistente com o momento de pitch; `max(0,·)` clampa lift extremo); (21) freio = complementaridade pura (trava sempre que a demanda cobre `|T_stop|`; nunca inverte nem motoriza `omega_w` — rollback com `ω < 0` continua legítimo); (22) freio estático no eixo parado em stick sustenta o foot-brake launch (`clamp(T_wheel − I·a_x/r, ±T_d)`); (23) piso do `kappa` = `eps_stick` (0.2 m/s).

### v2.4 — 4ª rodada de auditoria (2026-11-07)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 `tire` única p/ os 4 pneus — PSI/temp/μ iguais nos 4 (PSI/burnout são o tuning central) | **Bloqueante** | `tire_front`/`tire_rear` separadas; cada `axle` referencia a sua (§2.2); §3.4 roda POR EIXO com a tire do eixo (`mu_base`, `mu_static`, `load_sensitivity`, `psi_peak`, `k_heat`/`k_cool`); `temp` é estado dinâmico POR EIXO (burnout aquece só a traseira); input `psi` → `psi_front`/`psi_rear`; `T_amb` sai do struct (fica na §2.7) | §1, §2.2, §2.4, §2.6, §3.4, §4, §5, §6, §7 |
| 2 Nota "needs confirmation" do `T_reaction_motor` aberta na §9 (v2.3) | **Bloqueante (formal)** | Nota FECHADA: derivação da mão direita confirmou — longitudinal → roll (`s_eng = 0` evita dupla contagem com o axle wrap via `h_traction`); transversal → pitch com `s_eng = ±1` pelo sentido do crank. Anotada como aproximação de 1ª ordem (reação dos MOUNTS; carcaça do diferencial/transaxle não modelada) | §3.7, §9 |
| 3 Precedência de `omega_w` em duplo stick não declarada | Importante | Declarado: em duplo stick, `omega_w = vx/radius` prevalece (constraint do solo); a projeção `omega_eng/g` serve SÓ para fechar a equação do motor — nunca para `slip`/`F_x` do passo 7 | §3.2, §3.4, §4, §6 |
| 4 Modelo lateral da Highway indefinido (`F_y`, `delta`, yaw) | **Bloqueante (se a Highway usasse este modelo)** | **Decisão de escopo:** Highway = modo físico SEPARADO (modelo bicicleta: `delta`, yaw rate, `F_y(α)` — spec próprio na fase 2, §1.1); este `phys_step` é estritamente longitudinal, `F_y ≡ 0` na Arrancada; traction circle permanece como interface; teste da §7 deixa de ser trivial (injeção de `F_y` sintético) | §1.1 (novo), §1, §3.4, §7, §8 |
| 5 `F_z0_axle` usava `mass` total (~2% no load sensitivity) | Menor | `F_z0_front = mass_sprung·g·b_cg/L + mass_unsprung_front·g` (traseiro análogo) — a carga estática correta não inclui o peso do outro eixo | §3.4 |
| 6 `k_idle = 0.002` baixo — idle podia cair (atrito ~29 N·m vs. ~2% de throttle no erro de 10 rad/s) | Menor | Default **0.015** (satura o cap 0.15 com erro ~10 rad/s); calibrar no checklist §7 | §2.7, §3.1 |
| 7 PSI sem dinâmica térmica | Menor | Anotado fase 2: `dP/dt = k_psi_heat·(temp − T_amb)` (~1–2 psi na Highway); na arrancada (~10 s) psi é estático | §3.4, §8 |
| 8 Alvos de validação não mensuráveis ("60-foot razoável") | Menor | Tabela de alvos: street ~500 HP → 60-ft 1,9–2,1 s; 1/4 mile 11,5–12,0 s; trap ~190 km/h; demais classes calibrar contra referências reais antes de congelar a física | §7 |
| 9 `inertia_pitch` usava `h_cg²` (altura do CG ≠ dimensão do corpo) | Menor | `≈ mass_sprung·(L² + H_body²)/12` com novo campo `H_body`; CAD/calibração substitui | §2.1 |
| 10 "Barra vencida", ré, line-lock FWD | Menor | "Capotou" só sem barra equipada (o modelo não tem stress/fadiga — barra nunca "vence"); ré fora do escopo (`gear_ratios` > 0); line lock = eixo NÃO motriz (em FWD seria traseiro, fase 2) | §3.6, §2.3, §2.6, §8 |

**Decisões registradas nesta revisão (v2.4):** (14) pneu é PER-AXLE (`tire_front`/`tire_rear`) com `psi`/`temp` separados por eixo — PSI/burnout exigem estado térmico independente; (15) Highway = modo físico separado — `F_y ≡ 0` na Arrancada e o traction circle permanece como interface (§1.1); (16) em duplo stick prevalece `vx/radius` (solo) sobre a projeção `omega_eng/g`, que existe só para fechar a equação do motor; (17) `T_reaction_motor = s_eng·T_clutch` CONFIRMADO (mão direita) e anotado como aproximação de 1ª ordem (mounts; transaxle fora).

### v2.3 — 3ª rodada de auditoria (2026-11-07)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 Momento de pitch da downforce com sinal invertido nos 4 casos | **Crítico** | `M = −F_aero_z·(x_cp − a_cg)` (era `+`): lift atrás → nariz↓, downforce atrás (spoiler) → nariz↑, downforce à frente → nariz↓ — coerente com a regra da §0 | §3.7, §7 |
| 2 `omega_w` integrado 2× no stick da embreagem (projeção §3.2 + passo 9) | **Crítico** | Passo 9 pula a integração do eixo driven quando `clutch_mode == "stick"` — `omega_w` só por projeção; regra anotada em §3.2/§3.5/§4 | §3.2, §3.5, §4 |
| 3 `T_clutch`/`T_wheel` indefinidos no stick da embreagem | Importante | `T_clutch = T_eng − inertia_eng·domega_eng/dt` definido no regime stick; alimenta §3.3 e §3.7; se `|T_clutch| > capacity` → slip no substep seguinte | §3.2, §3.3 |
| 4 `T_reaction_motor` sem fórmula | Importante | `T_reaction_motor = s_eng·T_clutch` com `s_eng` por layout: longitudinal → 0 (crank é roll; wrap já no anti-squat), transversal → ±1 conforme sentido do crank (derivação da mão direita em §3.7) | §2.1, §3.7 |
| 5 Lumped vs per-wheel não declarado | Importante | Convenção LUMPED no topo do struct do eixo: todos os campos = as 2 rodas somadas; `mass_sprung`, `T_load_eng`, `T_wheel`, `I_eq_wheel` anotados | §2.1, §2.2, §3.2, §3.3, §3.5 |
| 6 Cadeia do duplo stick não declarada | Importante | Sequência explícita de 4 passos (lag `a_x` → `F_x` → projeção → `T_load`) + fechamento `T_wheel − F_x·r − T_brake = I_eq_wheel·dω_w/dt` | §3.2, §4, §6 |
| 7a Idle controller sem fórmula | Menor | `throttle_idle = clamp(k_idle·(omega_idle − omega_eng), 0, 0.15)`; defaults `k_idle = 0.002`, `omega_idle ≈ 84` **(default corrigido p/ 0.015 na v2.4 — ganho insuficiente)** | §3.1, §2.7 |
| 7b `mu_psi` fixo em ~35 psi penaliza drag | Menor | Pico por tipo: `psi_peak` no struct (street ~32, drag ~10); rampa saturando no pico | §2.4, §3.4 |
| 7c `T_amb` sem default | Menor | Default 25 °C na §2.7 (ler da pista se exposto) | §2.7 |
| 7d `T_brake_max` subdimensionado (3000/2000 ≈ 1,0 g) | Menor | Defaults 3600/2400 N·m (~1,2 g p/ ~1500 kg), ajustável por carro | §2.2 |
| 7e `inertia_pitch` subestima massa traseira | Menor | Anotado: aproximação de caixa uniforme; CAD/calibração substitui **(fórmula corrigida na v2.4: `(L² + H_body²)/12` — o `h_cg²` era ad hoc)** | §2.1 |

**Decisões registradas nesta revisão (v2.3):** (9) sinal do momento aero = `−F_aero_z·(x_cp − a_cg)`, derivado da regra da §0; (10) no stick da embreagem `omega_w` é SEMPRE projeção (nunca integrado 2×); (11) `T_clutch` no stick = torque transmitido (`T_eng − I_e·dω/dt`); (12) `T_reaction_motor = s_eng·T_clutch` — RWD longitudinal fica 0 (dupla contagem com anti-squat) e transversal usa ±1 conforme o sentido do crank; (13) TODOS os campos do eixo são LUMPED (2 rodas somadas). **NOTA ao revisor:** no item 4, a tabela proposta sugeria RWD → +1 / FWD → −1; a derivação da mão direita indica que em RWD longitudinal a reação do crank é de ROLL (sem pitch, wrap já coberto por `h_traction`) e que em transversal o sinal depende do sentido do crank/final drive. **CONFIRMADO na v2.4:** a derivação da mão direita do revisor fecha a nota — longitudinal → roll (`s_eng = 0` evita dupla contagem com o axle wrap via `h_traction`) e transversal → pitch com `s_eng = ±1` conforme o sentido do crank. A fórmula fica anotada como aproximação de 1ª ordem (mounts; carcaça do diferencial/transaxle fora — §3.7).

### v2.2 — 2ª rodada de auditoria (2026-11-07)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 Reflexão de inércia e `T_load_eng` invertidas (`×g²` → `÷g²`; `×g` → `÷(g·eff)`) | **Crítico** | `I_eq = inertia_eng + Σ_driven(inertia_wheel + inertia_drivetrain)/gear_ratio²`; `T_load_eng = Σ_driven(F_x·r + T_brake_eff)/(gear_ratio·eff)`; `I_eq_wheel = inertia_wheel + inertia_drivetrain` (lado de saída, sem `g²`); `inertia_drivetrain` redefinida explicitamente; AWD soma `Σ_driven` em AMBOS | §3.2, §3.5, §2.2 |
| 2 Heave com `mass` em vez de `mass_sprung` (peso não suspenso contado 2×) | **Crítico** | `mass_sprung` no struct; `sum_F_z = F_susp_f + F_susp_r − mass_sprung·g + F_aero_z + F_bar`; `az = sum_F_z/mass_sprung`; `inertia_pitch` do corpo sprung; cheque estático reescrito | §2.1, §3.7 |
| 3 `F_drag`/`F_rolling` sem `sign(vx)` — aceleravam o carro em ré | Importante | `F_total_x = F_x_rear + F_x_front − sign(vx)·(F_drag + F_rolling)`; `sign(0)=0` → repouso neutro | §3.7, §3.8 |
| 4 `F_x` no stick ignorava `a_x` (subestimava o launch) | Importante | `F_x = (T_wheel − T_brake_eff − I_eq_wheel·a_x/r)/r`, com `a_x` do substep anterior | §3.4, §4 |
| 5 Stall não abria a embreagem — starter nunca religava | Importante | `engine_stalled ⇒ clutch_mode = "open"` forçado todo substep; restart exige `ignition` E `clutch_pedal > 0.5` | §3.2, §3.9, §7 |
| 6 Braço da wheelie bar sem `h_bar·sin(theta)` | Menor | Mantido simplificado na fase 1 (erro < 3% abaixo de 10°); braço exato anotado | §3.7, §3.11, §8 |
| 7 Parâmetros sem default (`k_fmep`, `k_pump`, `k_rev`, `eps_*`, `fade_termico`, `T_brake_max`) | Menor | Nova §2.7 centraliza as constantes; `T_brake_max` entra no struct do eixo; `fade_termico = 1.0` na fase 1 | §2.7 (novo), §2.2, §3.1–§3.5 |
| 8 Lag de `normal_load` no substep não declarado | Menor | Bloco "Lags declarados" na §4 (`normal_load`, `a_x`, `T_load` = 1 substep; `slip_power` = corrente) | §4 |
| 9 Unidade de `k_bump`/`k_droop` (`N/m` → `N/m²`) | Menor | Unidade corrigida (batente quadrático) + guia de magnitude (~100 × spring_k numérico) | §2.2, §3.6 |

**Decisões registradas nesta revisão (v2.2):** (5) a reflexão de inércia para o lado do MOTOR divide por `g²` (conservação de energia) e a carga refletida divide por `g·eff`; `inertia_drivetrain` é definida no lado de SAÍDA do câmbio; (6) heave/pitch são equações do corpo SPRUNG (`mass_sprung`); (7) stall força embreagem aberta e o restart exige pedal pisado; (8) lags de 1 substep são aceitáveis e fazem parte do design (§4).

### v2.1 — 1ª rodada de auditoria (2026-11-07)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1.1 Contato por força, não cinemática | Crítico | `grounded` por geometria (`z_u ≤ radius`); airborne zera `normal_load`, `F_x` e `stick_state` | §0, §3.6, §4.6 |
| 1.2 Roda no ar inconsistente (sem `m_u` DOF) | Crítico | **Decisão registrada: opção (b)** — `m_u` dinâmico no eixo dianteiro (`z_u`, `v_zu`); traseiro cinemático; pouso inelástico | §1, §2.2, §3.6, §4.12 |
| 1.3 Stiction sem histerese (mata burnout / chatter) | Crítico | `stick_state` por eixo: quebra por `\|T_wheel/r\| > mu_static*N`, recupera com histerese `0.9×` e condição por `v_slip` (nunca por `vx`) | §2.2, §3.4, §6 |
| 1.4 Bump/droop stops ausentes | Crítico | `k_bump`/`k_droop` (5–10 × spring_k) dentro da equação de `F_susp` | §2.2, §3.6 |
| 2.1 Wheelie bar sem fórmula | Importante | Contato geométrico da ponta (`z_tip`), `F_bar` unilateral (mola + damping), entra em heave e pitch | §2.5, §3.11, §3.7 |
| 2.2 `omega_eng` negativa (stall) | Importante | `omega_stall`, `engine_stalled` (combustão cortada), restart por ignição com starter; clamp `omega_eng ≥ 0` | §2.3, §2.6, §3.1, §3.9 |
| 2.3 Shift dynamics ausente | Importante | Máquina `opening → swapping → closing` com rampas em `shift_timer` e rev-match opcional | §2.3, §3.10, §4.2 |
| 2.4 `mu_eff` negativo em alta carga | Importante | `max(mu_load, 0.05)` + referência de carga por eixo (`F_z0_axle`, substitui `mass*g*0.5`) | §3.4 |
| 2.5 `temp_factor` sem fórmula | Importante | Âncoras piecewise linear: 20 °C → 0.70, 90 °C → 1.00, 140 °C → 0.70 (clamp fora) | §3.4 |
| 3.1 Temperatura usa `fx` do futuro | Bug | Nova ordem: `F_x` no passo 7, temperatura no passo 8 usando o `F_x` corrente | §4 |
| 3.2 Sinal de `F_down` ambíguo | Bug | Renomeado `F_aero_z` (lift-positivo, `ClA < 0` = downforce); heave/pitch reescritos; braço longitudinal `x_cp` para força vertical + `h_aero` para o arrasto **(sinal do braço corrigido na v2.3: `−F_aero_z·(x_cp−a_cg)`)** | §0, §2.1, §3.7, §3.8 |
| 3.3 `T_wheel_reaction` indefinido | Bug | `T_load_eng = (F_x*radius + T_brake_eff) * gear_ratio` **(fórmula corrigida na v2.2 — reflexão correta é `÷(g·eff)`)**; projeção `omega_w = omega_eng / gear_ratio` | §3.2 |
| 4 Line lock / handbrake | Menor | Entradas por eixo (line lock = dianteira, handbrake = traseira) integradas ao freio-constraint | §2.6, §3.5 |
| 4 Condições iniciais | Menor | Bloco completo de init do frame 0 + assentamento estático | §6 |
| 4 `inertia_drivetrain` sem default | Menor | Default 0.10 kg·m² (faixa 0.05–0.2), somente no eixo driven | §2.2 |
| 4 `T_brake` como força bruta | Menor | Freio como restrição (complementaridade: `T_stop` vs `T_brake_demand`) | §3.5 |

**Normalização adicional (além da auditoria), registrada para rastreabilidade:**

| Inconsistência da v2.0 | Correção v2.1 | Onde |
|---|---|---|
| `F_susp = -k*travel` incoerente com `travel` positivo = comprimido (comprimir reduzia a carga) | `F_susp = +k*travel + c*travel_vel` (força no chassi, ↑+); kinemática via `L_free`: `travel = L_free - (z_mount - z_u)` | §0, §3.6 |
| Momentos de suspensão no pitch trocados (+traseira / −dianteira) | `-F_susp_rear * b_cg` (↑ atrás → nariz↓) e `+F_susp_front * a_cg` (↑ à frente → nariz↑) | §3.7 |
| Termo dianteiro de tração/pitch invertido (`-F_x_front * h_cg`) | `+F_x_front * h_cg` (tração dianteira ergue; freio dianteiro mergulha) | §3.7 |
| Braços de momento `±L/2` (implicava CG centralizado, sem weight bias) | `a_cg` / `b_cg` reais → weight split emergente | §2.1, §3.6, §3.7 |
| `F_rolling` global com `mass*g + F_down` | Por eixo com `normal_load` real (roda no ar não rola com resistência) | §3.8 |
| `h_cp` usado para força vertical (altura não gera momento de força vertical) | `x_cp` longitudinal (split de downforce + braço de pitch) e `h_aero` só para o arrasto | §2.1, §3.7, §3.8 |

**Decisões registradas na v2.1:** (1) auditoria 1.2 = opção (b), DOF dianteiro apenas; (2) stall = morte real + restart manual por ignição (v2.2 adicionou: restart exige pedal pisado); (3) freio = restrição com complementaridade; (4) os 4 itens menores entram na fase 1.

---

**Próximo passo para o agente de código (atualizado v2.8):**  
Implementar as estruturas da §2 (incluindo `mass_sprung`, `tire_front`/`tire_rear` com `psi`/`temp` por eixo, `psi_peak`, `s_eng`, `stick_state`, `grounded` [estado persistente], `z_u`/`v_zu`, `shift_phase`, `engine_stalled`, `T_brake_max` e as constantes da §2.7) + o `global.phys_step` seguindo a ordem da §4 com os lags declarados, consumindo o `motor_torque` existente. **Bloqueantes (v2.2–v2.8):** reflexão de inércia `÷g²` e `T_load_eng ÷(g·eff)` (§3.2/§3.5); heave com `mass_sprung` (§3.7); sinal do momento aero `−F_aero_z·(x_cp−a_cg)·cos(theta)` (§3.7); proibição de re-integrar `omega_w` no stick da embreagem (§3.2/§3.5/§4); pneu PER-AXLE `tire_front`/`tire_rear` com a §3.4 inteira rodando por eixo (§2.4); em duplo stick `vx/radius` prevalece sobre a projeção `omega_eng/g` (§3.2/§3.4); curva do pneu com pico `mu_static_eff` + quebra/re-adesão por `T_demand_stick` (§3.4); freio complementar puro + freio estático no eixo em stick (§3.5); aero SEM termo direto na `normal_load` (Opção A v2.6 — age SÓ no corpo; §3.6/§3.7/§3.8); **máquina de contato v2.7: `grounded` estado persistente + lift-off por força `F_susp + m_u·g < −eps_liftoff` (v2.8) + pouso `z_u ≤ radius ∧ v_zu ≤ 0` (§3.6 — o ramo airborne era CÓDIGO MORTO até a v2.6)**; **braços de pitch exatos/vivos (`cos(theta)`, `z_cg` — §3.7)**; **damper da wheelie bar `− c_bar·v_tip` com `c_bar ≤ 0.15·k_bar` (§3.11/§2.5 — v2.8)**. Prioridade: (1) máquina de contato + DOF dianteiro + heave/pitch com braços vivos (§3.6/§3.7), (2) regimes da embreagem com reflexão correta, `T_clutch` de stick e cadeia do duplo stick (§3.2/§3.3), (3) stiction com histerese e pneus per-axle (§3.4), (4) bump/droop stops (§3.6), (5) wheelie bar com damper correto e `c_bar ≤ 0.15·k_bar` (§3.11/§2.5/§6 — v2.8), (6) shift (§3.10) e stall com embreagem aberta (§3.9). Respeitar as convenções de sinal da §0. Testar isoladamente (checklist §7, alvos numéricos da v2.4) antes de injetar no `obj_player_Step_0` — e NÃO marcar o spec "pronto" sem o §7 rodando no motor (lição v2.7).
