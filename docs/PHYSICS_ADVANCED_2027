# MCML Realistic Physics Spec (2D Drag Racing)

**Versão:** v2.3 — 3ª rodada de auditoria (2026-11-07). Corrige o sinal do momento aero (invertido nos 4 casos) e a dupla integração de `omega_w` no stick da embreagem. Histórico completo de correções na §9.  
**Status:** Especificação pronta para implementação.  
**Alvo:** My Car My Life (GameMaker 1.4) via injeção GML/CSX do `mcml-fixer`.  
**Objetivo:** Substituir a física artificial de tração/velocidade/RPM por um sistema emergente (torque → roda → pneu → chassi), sem clamps de velocidade ou redline duro.  
**Escopo principal:** Longitudinal + pitch (side-view de arrancada) + DOF vertical da massa não suspensa **dianteira** (wheelie com perda de contato real). Lateral mínimo (traction circle) para a Ignorantes Highway.

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
| Contato com o solo | **Por geometria** (`z_u ≤ radius`), nunca por força | Auditoria 1.1: checar "no ar" por `normal_load == 0` eliminava o wheelie real. |
| Suspensão | Half-car cinemático no eixo traseiro; dianteiro com `m_u` dinâmico. **Bump/droop stops na equação de força** | Auditoria 1.4: sem stops, o launch comprime além do curso, `F_susp` explode e o chassi dá "snap". |
| Rodas | 2 eixos. Cada eixo tem `omega_w`, suspensão relativa, contato unilateral e **`stick_state` com histerese** | Auditoria 1.3: sem máquina de estados, o burnout morre no 1º frame e o launch treme (chatter). |
| Modelo de pneu | Curva de aderência simplificada + load sensitivity (com clamp) + **`temp_factor` explícito** + efeito de PSI | Auditorias 2.4 e 2.5. Pacejka completo é pesado; temperatura e PSI são mecânicas existentes no jogo. |
| Embreagem | Stick/slip com regimes explícitos, inércia efetiva e **`T_load_eng` definido** | Auditoria 3.3. |
| Câmbio | **Sequência de troca com `shift_timer`**: abrir → trocar → fechar → rev-match (opcional) | Auditoria 2.3: troca instantânea distorce o ET em 0,2–0,5 s por troca. |
| Motor | **Stall + restart por ignição** (starter); `omega_eng ≥ 0` garantido em todo substep | Auditoria 2.2: sem stall, `omega_eng` fica negativa e inverte todos os sinais acoplados. |
| Wheelie bar | **Fórmula explícita de contato** (ponta da barra vs. solo, mola + damping unilaterais) | Auditoria 2.1: dispositivo de segurança central; sem fórmula o carro capota ou a barra atravessa o chão. |
| Freio | **Restrição (complementaridade)**, por eixo: pedal + **line lock** + handbrake | Auditoria 4: freio força-bruta oscila entre travar/destravar. Line lock/handbrake = mecânica de burnout. |
| Anti-squat | Parâmetro `h_antisquat` (ou %) por carro/suspensão traseira. | Tuning central de drag racing. |
| Lateral | Traction circle explícito (escala `F_x` e `F_y`) quando ativo. | Necessário na Highway. Desligado na arrancada reta. |
| Integração | Euler semi-implícito + substeps limitados (2–4). | Estabilidade com molas, embreagem e wheelie bar. |
| Soft-body RoR | **Fora de escopo** | Desnecessário em 2D de arrancada. |
| Unidades | Física em SI (m, kg, N, rad/s). Conversão para pixels só na renderização. | Evita bugs de sinal e escala. |

---

## 2. Estruturas de Estado

### 2.1 Chassis (corpo rígido 2-DOF)
```
chassis:
  mass                // kg (de global.weight) — massa TOTAL (sprung + unsprung)
  mass_sprung         // kg = mass - axle_front.mass_unsprung - axle_rear.mass_unsprung
                      //     (derivado no load do carro; heave/pitch são do corpo SPRUNG — §3.7;
                      //      mass_unsprung é LUMPED por eixo — §2.2)
  inertia_pitch       // kg·m²  ≈ mass_sprung * (wheelbase²/12 + h_cg²)
                      //     (aproximação de caixa uniforme; carro com massa traseira
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
  fy                  // N (força lateral, só quando lateral ativo)
  grounded            // bool (definido POR GEOMETRIA — §3.6)
  stick_state         // bool (máquina de estados do pneu, §3.4; init = true)
  z_u                 // m (altura do centro da roda; estado dinâmico no eixo dianteiro,
                      //     fixo em `radius` no traseiro durante a fase 1)
  v_zu                // m/s (velocidade vertical da massa não suspensa)
  dynamic_mu          // bool (true só no eixo dianteiro na fase 1 — decisão 1.2-b)
```

### 2.3 Powertrain
```
powertrain:
  omega_eng           // rad/s (nunca < 0 — clamp §3.1)
  inertia_eng         // kg·m²
  clutch_engagement   // 0..1 (pressão)
  clutch_capacity_max // N·m
  gear                // int
  gear_ratios[]       // final já incluída
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

### 2.4 Parâmetros de pneu / pista
```
tire:
  mu_base             // pico (street ~0.9, slick ~1.3, drag ~1.6–2.0)
  mu_static           // ≥ mu_base
  load_sensitivity    // 0.1–0.2 (μ cai com F_z)
  temp                // °C (estado dinâmico)
  T_amb               // °C (ambiente da pista)
  psi                 // pressão (estado; afeta μ e Crr)
  psi_peak            // pressão de pico de mu_psi POR TIPO de pneu (v2.3):
                      //     street ~32 | drag ~10 — pneu drag PENALIZA pressão alta
  k_heat              // coeficiente de aquecimento por potência de slip
  k_cool              // coeficiente de resfriamento
```

### 2.5 Wheelie bar
```
wheelie_bar:
  d_bar               // m (distância horizontal da ponta da barra ALÉM do eixo traseiro)
  h_bar               // m (altura da ponta da barra abaixo do CG, carro nivelado;
                      //     ponta fica alguns cm acima do solo em repouso)
  k_bar               // N/m (rigidez do contato; 2e5–5e5)
  c_bar               // N·s/m (damping do contato; ~0.3 × k_bar)
  bar_grounded        // bool (estado)
```

### 2.6 Entradas (novas / alteradas)
```
inputs:
  throttle            // 0..1
  clutch_pedal        // 0..1 (1 = totalmente aberta)
  brake_pedal         // 0..1 (age nos 2 eixos)
  line_lock           // bool — trava SÓ o eixo dianteiro (burnout, auditoria 4)
  handbrake           // bool — trava SÓ o eixo traseiro
  gear_target         // int
  shift_request       // bool (borda de subida dispara a sequência §3.10)
  ignition            // bool (aciona starter quando engine_stalled — §3.9)
  psi                 // pressão ajustada pelo jogador
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
  k_idle              // 1/(rad/s) — ganho P do idle (default 0.002)
  omega_idle          // rad/s — referência do idle (default ~84 ≈ 800 rpm)
  T_amb               // °C — ambiente da pista (default 25; ler da pista se o jogo expuser)
  // Tolerâncias numéricas
  eps_clutch          // rad/s — stick da embreagem (1.0, lado motor)
  eps_stick           // m/s — stick do pneu (0.2)
  eps_lock            // rad/s — freio considerado travado (0.5)
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
- Idle controller (v2.3, P simples): `throttle_idle = clamp(k_idle*(omega_idle - omega_eng), 0, 0.15)`; usar `throttle_eff = max(throttle_eff, throttle_idle)` com embreagem aberta ou ponto morto. Defaults na §2.7.
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
                                            //   no passo 9 (v2.3 — §3.5/§4)
   ```
   - `F_x` e `T_brake_eff` de `T_load_eng` são os do **substep anterior** (lag de 1 substep — declarado e aceitável).
   - Não atribuir `omega_eng` diretamente sem ajustar o torque líquido (evita salto de energia).
   - AWD: o `Σ_driven` aparece DUAS vezes — na inércia refletida de `I_eq` (2 eixos) e na carga `T_load_eng` (soma de `F_x_axle·radius + T_brake_eff_axle` dos 2 eixos driven); `T_wheel` continua dividido por `n_driven_axles` (§3.3).
   - `T_load_eng` soma sobre EIXOS driven com valores LUMPED (§2.2) — nunca por roda.

**Cadeia do DUPLO STICK (v2.3 — embreagem travada + pneu travado: o regime que define o 60-foot. Os dois sticks são acoplados via `F_x`/`T_wheel`; resolver NESTA ordem, sem tentar fechar a circularidade dentro do mesmo substep):**
1. `a_x` do substep anterior (lag declarado, §4).
2. Pneu em stick: `F_x = (T_wheel - T_brake_eff - I_eq_wheel*a_x/radius)/radius` (§3.4).
3. Embreagem em stick: `omega_eng` integra com `I_eq`; `omega_w = omega_eng/gear_ratio` é PROJEÇÃO — o passo 9 NÃO re-integra (§3.5).
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
```
v_wheel = omega_w * radius
kappa = (v_wheel - vx) / max(abs(vx), 0.5)

// Efeito de PSI (mapa simples, POR TIPO de pneu — v2.3: o pico fixo em ~35 psi
// penalizava pneu drag esvaziado, o oposto do tuning que o jogo explora)
psi_norm = clamp((psi - psi_peak + 15) / 15, 0, 1)   // rampa 0→1 saturando no pico
mu_psi  = lerp(0.85, 1.05, psi_norm)                 // street: pico ~32 | drag: pico ~10
crr_psi = lerp(0.015, 0.008, psi_norm)

// mu_eff com referência de carga DO PRÓPRIO EIXO (auditoria 2.4)
F_z0_front = mass * g * b_cg / L          // carga estática dianteira
F_z0_rear  = mass * g * a_cg / L          // carga estática traseira
mu_load = 1 - load_sensitivity * (normal_load / F_z0_axle - 1)
mu_eff = mu_base * mu_psi * max(mu_load, 0.05)      // clamp: mu_eff NUNCA ≤ 0
mu_eff *= temp_factor(temp)

// temp_factor (auditoria 2.5) — piecewise linear entre âncoras:
//   20 °C → 0.70  |  90 °C → 1.00  |  140 °C → 0.70
//   fora da faixa: clamp em 0.70
//   (fase 2 opcional: acima de 140 °C decai até 0.50 — pneu "gorduroso")

// Curva simples (pico em ~0.2, depois queda)
slip_abs = abs(kappa)
if slip_abs < 0.2:
    force_factor = slip_abs / 0.2
else:
    force_factor = 1.0 - 0.4 * clamp((slip_abs - 0.2) / 0.5, 0, 1)

F_x_ss = mu_eff * normal_load * force_factor * sign(kappa)
```

**Stiction — máquina de estados POR EIXO com histerese (auditoria 1.3):**
```
v_slip = v_wheel - vx

if stick_state:
    if (not grounded) or abs(T_wheel / radius) > mu_static * normal_load:
        stick_state = false                        // quebrou aderência
else:
    if grounded
       and abs(v_slip) < eps_stick                 // roda e solo sincronizados (0.2 m/s — §2.7)
       and abs(T_wheel / radius) < 0.9 * mu_static * normal_load:
        stick_state = true                         // recuperou (histerese 0.9×)

if stick_state and grounded:
    omega_w = vx / radius                          // constraint cinemática
    // v2.2: a constraint completa é domega_w/dt = a_x/r. Isolando F_x na equação
    // da roda (§3.5), esta é a força que mantém a roda sincronizada com o solo
    // ENQUANTO o carro acelera (T_brake_eff e a_x: do substep anterior — lags §4).
    F_x = clamp((T_wheel - T_brake_eff - I_eq_wheel * a_x / radius) / radius,
                -mu_static*normal_load, +mu_static*normal_load)
else:
    F_x = F_x_ss
```
- **Não** usar `abs(vx) < 0.3` como condição de stick. Burnout (`vx = 0`, `omega_w = 100`): `v_slip = 100*R` → grande → slip → `F_x = F_x_ss` → burnout vive.
- Sem estado + histerese, o launch oscila stick/slip a cada substep e o carro "tremedeia" na largada (chatter).
- **Termo de inércia no stick (v2.2):** sem `- I_eq_wheel·a_x/r`, `F_x = T_wheel/r` assume `a_x = 0` e subestima a força exatamente no launch (60-foot mais lento — viés sistemático na fase crítica).
- `slip_power = abs(F_x * v_slip)` usa o `F_x` **deste substep** — a nova ordem do `phys_step` (§4) calcula `F_x` ANTES da temperatura (resolve a circularidade da auditoria 3.1).

**Combined slip (quando lateral ativo):**
```
scale = min(1.0, mu_eff * normal_load / sqrt(F_x² + F_y² + 1e-6))
F_x *= scale
F_y *= scale
```

### 3.5 Dinâmica da roda + freio como restrição
```
// v2.2: esta inércia vive NO LADO DE SAÍDA do câmbio — NÃO refletir gear_ratio² aqui
// (v2.1 multiplicava por g² e inflava a roda). Reflexão ÷g² só acontece em I_eq (§3.2).
I_eq_wheel = inertia_wheel + inertia_drivetrain     // eixo NÃO driven: só inertia_wheel
I_eq_wheel * domega_w/dt = T_wheel - F_x * radius - T_brake_eff
```
- Eixo em `stick_state` (pneu): pular a integração — `omega_w = vx / radius` (constraint, §3.4).
- **Eixo driven com embreagem em `stick` (`clutch_mode == "stick"`, v2.3): pular a integração TAMBÉM — `omega_w` já foi PROJETADO de `omega_eng` no §3.2.** Integrar 2× no mesmo substep (projeção + passo 9) acumulava erro e podia gerar salto de energia na largada (bug v2.3-2). Eixo NÃO driven e pneu em slip: integra normal com `I_eq_wheel`.
- Eixo no ar: `F_x = 0`; integra só com `T_wheel` e `T_brake_eff`.
- `F_x` e `T_brake_eff` são do EIXO (lumped, §2.2).

**Freio por eixo (auditoria 4):** a demanda usa pedal + line lock + handbrake:
```
T_brake_demand = max(brake_pedal, line_lock (só front), handbrake (só rear)) * T_brake_max   // default por eixo: §2.2
```
Aplicar como **restrição (complementaridade)** — mesma lógica da embreagem; força bruta oscila entre travar e destravar:
```
omega_pred = omega_w + (T_wheel - F_x*radius) / I_eq_wheel * dt    // passo SEM freio
T_stop = I_eq_wheel * omega_pred / dt        // torque necessário p/ zerar neste substep
if abs(omega_pred) < eps_lock (0.5 rad/s — §2.7) and T_brake_demand >= abs(T_stop):
    omega_w = 0
    T_brake_eff = clamp(T_stop, -T_brake_demand, +T_brake_demand)
else:
    T_brake_eff = T_brake_demand * sign(omega_pred)
    integrar normal
```
Line lock + handbrake viabilizam o burnout de gameplay: dianteira travada, traseira girando.

### 3.6 Suspensão, contato e massa não suspensa (reescrito)

**Geometria (pontos de montagem no chassi):**
```
z_mount_front = z_cg + a_cg * sin(theta)
z_mount_rear  = z_cg - b_cg * sin(theta)
travel        = L_free - (z_mount - z_u)      // > 0 = compressão (convenção §0)
```
- Chassi sobe → `(z_mount - z_u)` cresce → `travel` cai (droop). Squat traseiro → montagem desce → `travel` cresce (compressão). Sinais consistentes por construção.

**Contato é por GEOMETRIA (auditoria 1.1):**
```
grounded = (z_u <= radius)                    // solo em z = 0
```
- `grounded == false` → `normal_load = 0`, `F_x = 0`, `stick_state = false` (roda no ar de verdade — wheelie real).
- A checagem por força da v2.0 (`normal_load == 0`) mantinha a roda presa à cinemática do chassi e eliminava o wheelie.

**Força da suspensão no chassi (↑+; batentes — auditoria 1.4):**
```
F_spring = spring_k * travel
if travel > travel_max:  F_spring += k_bump  * (travel - travel_max)²    // bump stop
if travel < travel_min:  F_spring -= k_droop * (travel_min - travel)²    // droop stop
F_susp = F_spring + damp_c * travel_vel
```
- Sem os stops, o launch comprime além do curso, `travel` vai a valores absurdos, `F_susp` explode e o chassi dá "snap"; no wheelie, a dianteira estende além do droop e a mola puxa de forma irreal. Com `k_bump`/`k_droop` progressivos, a restauração é realista nos dois lados.

**Eixo dianteiro — `m_u` dinâmico (decisão 1.2-b):**
```
if grounded:
    z_u = radius
    v_zu = max(0, v_zu)                          // pouso INELÁSTICO
    travel = L_free - (z_mount_front - radius)
    travel_vel = -(vz + a_cg * cos(theta) * omega_pitch)   // v_zu = 0 em contato
    normal_load = max(0, F_susp + mass_unsprung * g + F_df_front)
else:                                            // NO AR
    travel = L_free - (z_mount_front - z_u)
    travel_vel = -(vz + a_cg * cos(theta) * omega_pitch - v_zu)
    m_u * d(v_zu)/dt = -F_susp - m_u * g         // reação: mola comprimida empurra a roda
                                                 // para baixo; gravidade age na roda
    v_zu += d(v_zu)/dt * dt                      // semi-implícito
    z_u  += v_zu * dt
    if z_u <= radius: → pouso (z_u = radius; v_zu = max(0, v_zu); grounded = true)
    normal_load = 0
```
- No ar, a roda cai por gravidade + extensão da mola, quica no damper e pousa — comportamento definido, sem zona indefinida.

**Eixo traseiro — cinemático (fase 1):**
```
z_u_rear = radius                                // restrição sempre ativa
travel_rear = L_free - (z_mount_rear - radius)
travel_vel_rear = -(vz - b_cg * cos(theta) * omega_pitch)
normal_load = max(0, F_susp + mass_unsprung * g + F_df_rear)
```
- Com wheelie bar (§3.11), a traseira não levanta — a restrição cinemática é honesta.
- Sem barra (ou barra vencida): se `theta > 60°` → **game event "capotou"**; congela o pitch e encerra a passagem (a fase 1 não simula capotamento completo — ver §8).

### 3.7 Equações do chassi (heave + pitch + longitudinal — sinais normalizados)
```
// v2.2: sign(vx) — em RÉ, arrasto e rolamento empurram PARA FRENTE (opõem o
// movimento). Na v2.1 eram sempre subtraídos e ACELERAVAM o carro em ré (Highway).
// sign(0) = 0 em GML: parado, as resistências não empurram (correto — a largada
// depende só de F_x).
F_total_x = F_x_rear + F_x_front - sign(vx) * (F_drag + F_rolling)
a_x = F_total_x / mass                          // massa TOTAL: sprung+unsprung transladam juntos

// Heave (v2.2 — BUG CRÍTICO corrigido): o chassi é o corpo SPRUNG. `F_susp` NÃO
// inclui o peso não suspenso (ele entra na FBD da roda: normal_load = F_susp +
// m_u·g + F_df). Com `- mass*g` o peso m_u·g era contado 2× e, em repouso,
// sum_F_z = -m_u·g ≠ 0 → o chassi afundava lentamente, sem achar equilíbrio.
mass_sprung = mass - mass_unsprung_front - mass_unsprung_rear    // derivado no load (§2.1)
sum_F_z = F_susp_front + F_susp_rear - mass_sprung * g + F_aero_z + F_bar
az = sum_F_z / mass_sprung

// Pitch (nariz↑ = +; braços reais a_cg/b_cg)
h_traction = h_cg - h_antisquat          // altura efetiva da linha de tração
M_pitch = + F_x_rear * h_traction        // tração traseira abaixo do CG → nariz↑ (squat/wheelie)
          + F_x_front * h_cg             // tração dianteira → nariz↑; freio (F_x_front < 0) → mergulho
          + F_susp_front * a_cg          // ↑ à frente do CG → nariz↑ (restaura dive)
          - F_susp_rear  * b_cg          // ↑ atrás do CG → nariz↓ (restaura squat)
          + T_reaction_motor             // reação do trem de força — DEFINIÇÃO v2.3 abaixo
          + F_drag * (h_aero - h_cg)     // arrasto acima do CG → nariz↑
          - F_aero_z * (x_cp - a_cg)     // v2.3: sinal CORRIGIDO (era +, invertido nos 4
                                         //   casos). Regra §0: força ↑ à frente do CG →
                                         //   nariz↑. Testes: lift atrás (F>0, braço>0) → −
                                         //   nariz↓ ✓ | downforce atrás (spoiler) → +
                                         //   nariz↑ ✓ | downforce à frente → − nariz↓ ✓
          - F_bar * (b_cg + d_bar) * cos(theta)   // wheelie bar: ↑ atrás do CG → nariz↓
                                                  //   (braço EXATO: (b_cg+d_bar)*cos(theta) −
                                                  //    h_bar*sin(theta); o termo omitido é < 3%
                                                  //    p/ theta < 10° — fase 2, v2.2)

alpha_pitch = M_pitch / inertia_pitch
```

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
```
- Cheque de sanidade estático (reescrito v2.2): em repouso, `sum_F_z = 0` exige `F_susp_front + F_susp_rear = mass_sprung * g` e o pitch exige `F_susp_front * a_cg = F_susp_rear * b_cg`. Somando as FBDs das rodas (`normal_load = F_susp + m_u·g + F_df`), as cargas no solo fecham `N_front + N_rear = mass * g` e o split segue a geometria (`N_front ≈ m*g*b_cg/L` — exato se a massa não suspensa respeita o mesmo weight bias).
- A convenção de `F_aero_z` (lift-positivo, `ClA < 0` = downforce) resolve a ambiguidade da v2.0 — sem ela o carro "levitava" em alta velocidade (auditoria 3.2). O momento aerodinâmico vertical usa o braço LONGITUDINAL `x_cp` (força vertical aplicada a uma altura não gera pitch); o arrasto (horizontal) usa a altura `h_aero`.

### 3.8 Resistências e aerodinâmica
```
F_drag   = 0.5 * rho * CdA * vx²                 // MAGNITUDE (≥ 0); o SINAL entra via
                                                 // sign(vx) na §3.7 (v2.2 — vale também em ré)
F_aero_z = 0.5 * rho * ClA * vx²                 // lift-positivo; ClA < 0 = downforce
F_df     = max(0, -F_aero_z)                     // magnitude do downforce
F_df_front = F_df * (L - x_cp) / L               // split pelo CP
F_df_rear  = F_df * x_cp / L
F_rolling  = crr_psi_front * normal_load_front + crr_psi_rear * normal_load_rear   // MAGNITUDE; sinal via sign(vx) na §3.7 (v2.2)
```
- `F_rolling` agora é POR EIXO com a carga real: roda no ar não gera resistência ao rolamento (consequência direta do wheelie).
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
    F_bar = max(0, k_bar * penetration + c_bar * v_tip)   // unilateral: só empurra ↑
else:
    bar_grounded = false
    F_bar = 0
```
- `F_bar` entra no **heave** (`+F_bar`) e no **pitch** (`-F_bar * (b_cg + d_bar) * cos(theta)` — força ↑ atrás do CG → nariz↓), conforme §3.7. **Braço exato (anotado v2.2, fase 2):** com theta > 0 o tip desloca-se horizontalmente (`h_bar·sin(theta)`), e o braço real é `(b_cg + d_bar)·cos(theta) − h_bar·sin(theta)` — erro < 3% abaixo de ~10°; só importa em wheelies extremos.
- `k_bar` alto + substeps evitam a barra "atravessar" o chão em launches brutais.
- Sem wheelie bar equipada, `F_bar = 0` sempre → launches com torque demais levam ao game event "capotou" (§3.6).

---

## 4. Ordem do `phys_step(dt)` (reordenada)

1. **Entradas**: throttle, clutch_pedal, brake_pedal, line_lock, handbrake, gear_target, shift_request, ignition, psi.
2. **Máquina de troca de marcha** (§3.10) → `clutch_engagement` alvo, `throttle_eff`, `gear`.
3. **Torque do motor**: `motor_torque` existente + friction; checa stall (§3.9); obtém `T_eng`.
4. **Determina regime da embreagem** e calcula `T_clutch` (§3.2) — usa `F_x` e `T_brake_eff` do substep anterior (lag declarado).
5. **Atualiza `omega_eng`** conforme o regime; clamp `omega_eng ≥ 0` (§3.1).
6. **Geometria e contato por eixo** (§3.6): `z_mount`, `grounded` (por geometria), `travel`, `travel_vel`, `F_susp` (com bump/droop stops), `normal_load` (com `F_df_axle`).
7. **Para cada eixo grounded**: slip ratio → `mu_eff` (PSI, `temp_factor`, load sensitivity com clamp) → `F_x` via máquina de stiction (§3.4, com termo `- I_eq_wheel·a_x/r` usando o `a_x` do substep anterior); combined slip se lateral ativo.
8. **Atualiza temperatura do pneu** (`dtemp/dt` via `slip_power`) usando o `F_x` **do passo 7** — sem circularidade (auditoria 3.1).
9. **Integra `omega_w`** (§3.5): freio como restrição; eixo em stick do pneu segue constraint; **eixo driven com embreagem em `stick` NÃO integra (projeção do §3.2 — v2.3)**; eixo no ar gira livre com `T_wheel`.
10. **Calcula forças e momentos no chassi** (§3.7): anti-squat, aerodinâmica (`F_aero_z`, `F_drag`), `F_rolling` por eixo e wheelie bar (§3.11 → `F_bar`).
11. **Integra o chassi** (semi-implícito): `vx, vz, omega_pitch, x, z_cg, theta`.
12. **Integra o DOF dianteiro** se no ar: `v_zu, z_u` (§3.6); checa pouso inelástico.
13. **Substeps**: repete 2–4 vezes com `dt/substeps` se necessário.
14. **Expõe telemetria**: slip, normal_load, T_wheel, rpm, temp, psi, `stick_state`, `clutch_mode`, `shift_phase`, `engine_stalled`, `z_u`, `bar_grounded`.

**Lags declarados dentro do substep (v2.2 — todos aceitáveis a dt ≈ 1/240 s; FAZEM PARTE do design: não reordenar os passos para "resolvê-los", isso reintroduz a circularidade que a §9 corrigiu):**

| Lag | O quê usa o quê | Por quê é seguro |
|---|---|---|
| Passo 4 | `T_load_eng` usa `F_x` e `T_brake_eff` do substep anterior | Embreagem ↔ pneu sem ciclo (auditoria 3.3) |
| Passo 6→7 | `mu_eff`/`F_x` usam `normal_load` derivado de `travel`/`theta` do substep anterior (`theta` só é integrado no passo 11) | Erro O(dt) na carga; 1/240 s é fino o bastante |
| Passo 7 | `F_x` em stick usa `a_x` e `T_brake_eff` do substep anterior (§3.4) | Troca de lags com o passo 9, sem ciclo no MESMO substep |
| Passo 8 | `slip_power` usa o `F_x` DESTE substep | Sem circularidade (auditoria 3.1) |
| Passo 9 | `omega_w` do eixo driven em stick da embreagem NÃO integra — só projeção (§3.2) | Evita dupla integração/salto de energia (v2.3-2) |

---

## 5. Interface com o mod existente

- **Entrada obrigatória**: `T_comb = global.motor_torque(rpm, throttle, afr, ...)`  
  (já calcula distribuidor, carburador, turbo, peças, desgaste).
- **Saída**: o novo sistema escreve `vx`, `rpm`, `omega_w`, `normal_load`, `slip`, `tire_temp` em variáveis globais que a UI/dyno/AI já leem. **Novas saídas v2.1**: `engine_stalled`, `shift_phase`, `stick_state` (por eixo), `bar_grounded`, `z_u_front` (para a render do wheelie).
- **Ponto de injeção**: substituir o bloco que calcula `_base_acceleration` e aplica movimento. Manter os patches de torque.
- **Dyno**: deve chamar a mesma função de torque (estático) para consistência.
- **PSI**: ler o valor de pressão que o jogo já expõe e alimentar `tire.psi`.
- **Inputs novos a mapear**: line_lock, handbrake, ignition (ignição/starter) e brake por eixo — já existem como mecânicas de gameplay no jogo.

---

## 6. Notas de implementação GML

- Fixed timestep interno com acumulador. Clamp de substeps máximos (ex.: 4).
- Evitar `division by zero` (slip já regularizado; `F_z0_axle` é constante positiva por construção).
- **Dois "sticks" distintos**: embreagem (`clutch_mode`) e pneu (`stick_state`) — flags separadas, ciclos separados. Mesclar é o caminho mais curto para bug. **Porém acoplados** via `F_x`/`T_wheel`: no duplo stick (launch), seguir a cadeia declarada no §3.2 — não "resolver" a circularidade por conta própria.
- A histerese do stick custa 2 comparações por eixo; em debug, logar transições/s (chatter aceitável < ~5/s).
- `omega_eng = max(0, omega_eng)` em todo substep, em qualquer regime.
- **Não reordenar o `phys_step`** sem revisar a dependência `slip_power → F_x` (auditoria 3.1) nem os lags da §4 (v2.2).
- Stick do pneu: para refinar o `a_x` do termo de inércia, 1 iteração dentro do substep basta (recalcular `F_x` com o `a_x` recém-integrado); a 240 Hz, mais iterações não mudam nada.
- Integração de `z_u`/`v_zu` semi-implícita, igual ao chassi (§4, passo 12).
- Performance: modo lite (substeps = 2, sem relaxation length) por padrão.
- **Condições iniciais (frame 0 — auditoria 4):**
  ```
  vx = 0; x = 0; vz = 0; theta = 0; omega_pitch = 0
  mass_sprung = mass - Σ mass_unsprung      // v2.2: derivado no load do carro (§2.1)
  z_cg = z_cg_static        // resolver o equilíbrio estático (v2.2, corpo sprung):
                            //   F_susp_front = mass_sprung*g*b_cg/L  e
                            //   F_susp_rear  = mass_sprung*g*a_cg/L
                            //   (ou assentar 0.5–1.0 s de simulação antes da largada)
  omega_eng = idle (~800 rpm ≈ 84 rad/s);  omega_w = 0 (2 eixos)
  clutch_pedal = 1 (aberta) ou input do jogador;  gear = 1 (ou N)
  temp = T_amb;  psi = default do carro
  stick_state = true (2 eixos);  engine_stalled = false
  shift_phase = "none";  bar_grounded = false
  z_u = radius (2 eixos);  v_zu = 0
  ```
- Reset de estado ao trocar de carro ou reiniciar corrida — incluir `stick_state`, `engine_stalled`, `shift_phase`, `temp` e `bar_grounded`.
- AI: mesma física + controlador simples de launch e shift (pode ser adicionado depois).
- **Stick da embreagem**: usar inércia efetiva (§3.2) em vez de atribuição direta de `omega_eng`.

---

## 7. Validação mínima

- Carro parado + throttle alto → wheelspin controlado (não explode).
- **Equilíbrio estático (v2.2)**: carro parado 2 s em pista plana → `z_cg` e `theta` NÃO derivam (`sum_F_z = 0`; valida o `mass_sprung` — na v2.1 o chassi afundava lentamente).
- **Reflexão de inércia (v2.2)**: 1ª marcha sobe de giro com ET plausível; `I_eq ≈ inertia_eng + (I_w + I_d)/g²` (NÃO ~750×); marcha alta não fica "mais pesada" que a baixa entre trocas.
- **Ré (v2.2)**: em ré com throttle cortado, o carro desacelera — drag/rolamento nunca aceleram o carro.
- **Sinal aero (v2.3)**: downforce com spoiler atrás do CG LEVANTA o nariz em trap speed; downforce à frente mergulha; lift dianteiro levanta ainda mais (instabilidade real).
- **Duplo stick (v2.3)**: launch com embreagem travada + pneu aderente → `omega_w` atualizado UMA vez (projeção); logar `T_wheel − F_x·r − T_brake_eff − I_eq_wheel·domega_w/dt ≈ 0` (fechamento do §3.2).
- **`T_clutch` no stick (v2.3)**: telemetria mostra `T_clutch = T_eng − inertia_eng·domega_eng/dt`, coerente com `T_wheel` e ≤ capacity.
- **Reaction (v2.3)**: RWD longitudinal → `T_reaction_motor = 0` (wrap coberto pelo anti-squat); transversal → o nariz responde na direção declarada por `s_eng`.
- **PSI por tipo (v2.3)**: pneu drag em ~10 psi → `mu_psi ≈ 1.05`; em 35 psi, penalizado. Street: o contrário.
- **Burnout (auditoria 1.3)**: parado + line lock + throttle cheio → roda traseira gira (`stick_state` NÃO mata o spin), temperatura sobe e `temp_factor` melhora μ até o pico; superaquecido (> 140 °C), μ cai de volta.
- **Launch sem chatter**: `stick_state` não alterna mais que ~5×/s (log de debug na largada).
- Launch com μ alto → 60-foot razoável; com o termo de inércia do stick (v2.2), o 60-foot melhora vs. `T_wheel/r` puro (sem viés de `a_x = 0`).
- **Wheelie real (auditorias 1.1/1.2)**: com CG alto + torque alto, `z_u_front > radius` (perda de contato GEOMÉTRICA), a roda cai, quica no damper e pousa inelasticamente sem explosão numérica.
- **Wheelie bar (auditoria 2.1)**: `theta` satura no contato da barra, sem capotar; `penetration` de regime < ~2 cm; sem barra, `theta > 60°` dispara "capotou".
- Anti-squat reduz o squat/wheelie de forma perceptível.
- **Stall (auditoria 2.2 + v2.2)**: soltar a embreagem em rpm < `omega_stall` → motor morre, embreagem abre AUTOMATICAMENTE e só religa com ignição + pedal pisado; `omega_eng` nunca fica negativa (assert em todo frame).
- **Troca de marcha (auditoria 2.3)**: ET com a sequência completa fica 0,2–0,4 s pior por troca que a troca instantânea (sanity do efeito no 1/4 mile).
- **Clamps (auditoria 2.4)**: `assert mu_eff >= 0.05 * mu_base` e `assert omega_eng >= 0` em todo frame.
- **Sinal de `F_aero_z` (auditoria 3.2)**: carro com `ClA < 0` NÃO levanta em alta velocidade; com `x_cp` à frente do CG, downforce mergulha o nariz.
- **Weight bias**: com `a_cg ≠ b_cg`, o split estático segue `§3.7` (checar `F_susp_front*a_cg == F_susp_rear*b_cg` no equilíbrio).
- Mudança de PSI altera μ e rolling resistance.
- Soltar acelerador → engine braking reduz velocidade.
- Trap speed emerge do equilíbrio potência vs. arrasto (sem vmax artificial).
- Dyno mostra a mesma curva de torque que a pista usa.
- Na Highway, combined slip limita a soma de forças longitudinais e laterais.

---

## 8. Fora de escopo (fase 1)

- Soft-body completo (RoR).
- Modelo de bicicleta lateral completo (só traction circle).
- DOF dinâmico da massa não suspensa **TRASEIRA** (wheel hop traseiro) — o dianteiro entrou na fase 1 (decisão 1.2-b); o traseiro segue cinemático.
- Airborne total (2 eixos no ar / saltos) e capotamento completo — a fase 1 trata `theta > 60°` sem barra como game event (§3.6).
- Tire relaxation length (adicionar só se houver instabilidade).
- Braço exato da wheelie bar no pitch (`h_bar·sin(theta)` — §3.7/§3.11): anotado na v2.2; entra na fase 2 junto com wheelies extremos.
- Clutch térmico avançado e turbo lag dinâmico (já parcialmente no mod).
- Colisão carro-carro.

---

## 9. Histórico de revisão (auditoria → correção)

### v2.3 — 3ª rodada de auditoria (2026-11-07)

| Item da auditoria | Severidade | Correção aplicada | Onde |
|---|---|---|---|
| 1 Momento de pitch da downforce com sinal invertido nos 4 casos | **Crítico** | `M = −F_aero_z·(x_cp − a_cg)` (era `+`): lift atrás → nariz↓, downforce atrás (spoiler) → nariz↑, downforce à frente → nariz↓ — coerente com a regra da §0 | §3.7, §7 |
| 2 `omega_w` integrado 2× no stick da embreagem (projeção §3.2 + passo 9) | **Crítico** | Passo 9 pula a integração do eixo driven quando `clutch_mode == "stick"` — `omega_w` só por projeção; regra anotada em §3.2/§3.5/§4 | §3.2, §3.5, §4 |
| 3 `T_clutch`/`T_wheel` indefinidos no stick da embreagem | Importante | `T_clutch = T_eng − inertia_eng·domega_eng/dt` definido no regime stick; alimenta §3.3 e §3.7; se `|T_clutch| > capacity` → slip no substep seguinte | §3.2, §3.3 |
| 4 `T_reaction_motor` sem fórmula | Importante | `T_reaction_motor = s_eng·T_clutch` com `s_eng` por layout: longitudinal → 0 (crank é roll; wrap já no anti-squat), transversal → ±1 conforme sentido do crank (derivação da mão direita em §3.7) | §2.1, §3.7 |
| 5 Lumped vs per-wheel não declarado | Importante | Convenção LUMPED no topo do struct do eixo: todos os campos = as 2 rodas somadas; `mass_sprung`, `T_load_eng`, `T_wheel`, `I_eq_wheel` anotados | §2.1, §2.2, §3.2, §3.3, §3.5 |
| 6 Cadeia do duplo stick não declarada | Importante | Sequência explícita de 4 passos (lag `a_x` → `F_x` → projeção → `T_load`) + fechamento `T_wheel − F_x·r − T_brake = I_eq_wheel·dω_w/dt` | §3.2, §4, §6 |
| 7a Idle controller sem fórmula | Menor | `throttle_idle = clamp(k_idle·(omega_idle − omega_eng), 0, 0.15)`; defaults `k_idle = 0.002`, `omega_idle ≈ 84` | §3.1, §2.7 |
| 7b `mu_psi` fixo em ~35 psi penaliza drag | Menor | Pico por tipo: `psi_peak` no struct (street ~32, drag ~10); rampa saturando no pico | §2.4, §3.4 |
| 7c `T_amb` sem default | Menor | Default 25 °C na §2.7 (ler da pista se exposto) | §2.7 |
| 7d `T_brake_max` subdimensionado (3000/2000 ≈ 1,0 g) | Menor | Defaults 3600/2400 N·m (~1,2 g p/ ~1500 kg), ajustável por carro | §2.2 |
| 7e `inertia_pitch` subestima massa traseira | Menor | Anotado: aproximação de caixa uniforme; CAD/calibração substitui | §2.1 |

**Decisões registradas nesta revisão (v2.3):** (9) sinal do momento aero = `−F_aero_z·(x_cp − a_cg)`, derivado da regra da §0; (10) no stick da embreagem `omega_w` é SEMPRE projeção (nunca integrado 2×); (11) `T_clutch` no stick = torque transmitido (`T_eng − I_e·dω/dt`); (12) `T_reaction_motor = s_eng·T_clutch` — RWD longitudinal fica 0 (dupla contagem com anti-squat) e transversal usa ±1 conforme o sentido do crank; (13) TODOS os campos do eixo são LUMPED (2 rodas somadas). **NOTA ao revisor:** no item 4, a tabela proposta sugeria RWD → +1 / FWD → −1; a derivação da mão direita indica que em RWD longitudinal a reação do crank é de ROLL (sem pitch, wrap já coberto por `h_traction`) e que em transversal o sinal depende do sentido do crank/final drive. Solicitada confirmação na próxima revisão.

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
| 1.3 Stiction sem histerese (mata burnout / chatter) | Crítico | `stick_state` por eixo: quebra por `|T_wheel/r| > mu_static*N`, recupera com histerese `0.9×` e condição por `v_slip` (nunca por `vx`) | §2.2, §3.4, §6 |
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

**Próximo passo para o agente de código (atualizado v2.3):**  
Implementar as estruturas da §2 (incluindo `mass_sprung`, `psi_peak`, `s_eng`, `stick_state`, `z_u`/`v_zu`, `shift_phase`, `engine_stalled`, `T_brake_max` e as constantes da §2.7) + o `global.phys_step` seguindo a ordem da §4 com os lags declarados, consumindo o `motor_torque` existente. **Bloqueantes (v2.2–v2.3):** reflexão de inércia `÷g²` e `T_load_eng ÷(g·eff)` (§3.2/§3.5); heave com `mass_sprung` (§3.7); sinal do momento aero `−F_aero_z·(x_cp−a_cg)` (§3.7); proibição de re-integrar `omega_w` no stick da embreagem (§3.2/§3.5/§4). Prioridade: (1) contato geométrico + DOF dianteiro + heave corrigido (§3.6/§3.7), (2) regimes da embreagem com reflexão correta, `T_clutch` de stick e cadeia do duplo stick (§3.2/§3.3), (3) stiction com histerese (§3.4), (4) bump/droop stops (§3.6), (5) wheelie bar (§3.11), (6) shift (§3.10) e stall com embreagem aberta (§3.9). Respeitar as convenções de sinal da §0. Testar isoladamente (checklist §7) antes de injetar no `obj_player_Step_0`.
