//! Correcoes de compatibilidade para **My Car My Life** (GameMaker Studio 1.4,
//! YoYo Linux Runner V1.3) em Linux moderno.
//!
//! O jogo foi lancado em 2016 e nao funciona num sistema atual sem quatro
//! intervencoes independentes, cada uma documentada em `docs/BUGS.md`:
//!
//! 1. **Launch** - o binario pede OpenSSL 1.0 e `libcurl-gnutls` da epoca.
//!    `fix_libs()` copia as bibliotecas do Steam Runtime para a pasta do jogo;
//!    como o binario tem `RPATH=$ORIGIN`, nada no sistema e' alterado.
//! 2. **Resolucao** - a janela vem do chunk `GEN8` do `game.unx` e o device do
//!    runner de um par de inteiros em `.data`. `patch_native()` grava os dois.
//! 3. **Cursor** - hotspot do sprite e objeto de cursor que nunca era
//!    instanciado. Corrigidos via UTMT em `PATCH_CSX`.
//! 4. **UI do PC** - a GUI herdava o tamanho da janela e o passe CRT recortava
//!    a `application_surface` com coordenadas fixas de 1280x720. Ambos
//!    corrigidos em `PATCH_CSX`.
//!
//! # Interface
//!
//! Sem argumentos abre a GUI (egui/eframe). Com argumentos roda em modo CLI -
//! as duas interfaces chamam exatamente as mesmas funcoes de acao, entao nada
//! existe so na GUI ou so na CLI. Ver `run_cli()` para a lista de comandos.
//!
//! # Regra de ouro
//!
//! Patch nao validado nao e' entregue: `data_check()` confirma o texto dentro do
//! arquivo, mas o jogo precisa ser executado depois, porque um erro de GML so
//! aparece em runtime (e as vezes so numa room especifica).

use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

const BIN_NAME: &str = "My_Car_My_Life";
const CANVAS_W: u32 = 1024;
const CANVAS_H: u32 = 768;

/// Offset no binario onde fica o par (device_w, device_h) do runner.
/// Descoberto por RE: `watch *(int*)0xf422f8` mostrou "Old value = 1024",
/// ou seja o default do device e inicializador estatico em .data.
/// vaddr 0xf422f8 -> file offset 0x9422f8 (.data em 0xedb990 / file 0x8db990).
const NATIVE_OFF: u64 = 0x9422f8;
/// Valores aceitos como estado "conhecido" antes de patchar (seguranca).
const NATIVE_KNOWN: &[(u32, u32)] = &[
    (1024, 768),
    (1280, 1024),
    (1280, 720),
    (1920, 1080),
    (2560, 1440),
];

/// (caminho relativo dentro do Steam Runtime, nome do arquivo no jogo)
const LIBS: &[(&str, &str)] = &[
    ("lib/x86_64-linux-gnu/libcrypto.so.1.0.0", "libcrypto.so.1.0.0"),
    ("lib/x86_64-linux-gnu/libssl.so.1.0.0", "libssl.so.1.0.0"),
    ("usr/lib/x86_64-linux-gnu/libcurl-gnutls.so.4", "libcurl-gnutls.so.4"),
    ("usr/lib/x86_64-linux-gnu/libidn.so.11", "libidn.so.11"),
    ("lib/x86_64-linux-gnu/libgcrypt.so.11", "libgcrypt.so.11"),
    ("usr/lib/x86_64-linux-gnu/librtmp.so.0", "librtmp.so.0"),
    ("usr/lib/x86_64-linux-gnu/libnettle.so.6", "libnettle.so.6"),
    ("usr/lib/x86_64-linux-gnu/libhogweed.so.4", "libhogweed.so.4"),
];

/// Diretorio do usuario (`$HOME`), com fallback para `.`.
fn home() -> PathBuf {
    PathBuf::from(std::env::var("HOME").unwrap_or_else(|_| ".".into()))
}

/// Caminhos conhecidos do Steam Runtime scout (`ubuntu12_32/steam-runtime`).
fn steam_runtimes() -> Vec<PathBuf> {
    let h = home();
    vec![
        h.join(".local/share/Steam/ubuntu12_32/steam-runtime"),
        h.join(".steam/root/ubuntu12_32/steam-runtime"),
        h.join(".steam/steam/ubuntu12_32/steam-runtime"),
    ]
}

/// Primeiro Steam Runtime que existir no disco.
fn first_steam_runtime() -> Option<PathBuf> {
    steam_runtimes().into_iter().find(|p| p.is_dir())
}

/// Arquivo onde a resolucao de saida escolhida na GUI e' persistida.
fn cfg_path() -> PathBuf {
    home().join(".config/mcml-res.json")
}

/// stdout/stderr do jogo quando iniciado por `launch()`.
fn log_path() -> PathBuf {
    home().join(".config/mcml-fixer-launch.log")
}

// ---------------------------------------------------------------- deteccao
/// Procura a instalacao do jogo em `steamapps/common` (dois roots) e, se
/// falhar, no diretorio atual. Criterio: a pasta contem `BIN_NAME`.
fn detect_game_dir() -> Option<PathBuf> {
    let mut roots = vec![home().join(".local/share/Steam/steamapps/common")];
    roots.push(PathBuf::from("/usr/local/share/Steam/steamapps/common"));
    for root in roots {
        if let Ok(rd) = fs::read_dir(&root) {
            for e in rd.flatten() {
                let p = e.path();
                if p.join(BIN_NAME).is_file() {
                    return Some(p);
                }
            }
        }
    }
    let cwd = std::env::current_dir().ok()?;
    if cwd.join(BIN_NAME).is_file() {
        return Some(cwd);
    }
    None
}

/// Libs que o loader nao consegue resolver (ldd | "not found").
fn missing_libs(bin: &Path) -> Vec<String> {
    let out = match Command::new("ldd").arg(bin).output() {
        Ok(o) => o,
        Err(_) => return vec!["ldd indisponivel".into()],
    };
    let txt = String::from_utf8_lossy(&out.stdout).to_string();
    txt.lines()
        .filter(|l| l.contains("not found"))
        .map(|l| l.trim().to_string())
        .collect()
}

/// Le `DefaultWindowWidth/Height` do chunk `GEN8`.
///
/// Valida a assinatura (`FORM` em 0x00, `GEN8` em 0x08) antes de confiar nos
/// offsets; devolve `None` em vez de lixo se o layout nao for o esperado.
fn read_gen8(data: &Path) -> Option<(u32, u32)> {
    let b = fs::read(data).ok()?;
    if b.len() < 0x60 || &b[0..4] != b"FORM" || &b[8..12] != b"GEN8" {
        return None;
    }
    let w = u32::from_le_bytes(b[0x4c..0x50].try_into().ok()?);
    let h = u32::from_le_bytes(b[0x50..0x54].try_into().ok()?);
    Some((w, h))
}

/// Modos reais do EDID (nao do xrandr, que no XWayland mostra so o virtual).
/// Modos reais do monitor, lidos de `/sys/class/drm/*/modes`.
///
/// Nao usa `xrandr`: sob XWayland ele reporta apenas o modo virtual, nao os
/// modos que o EDID do monitor realmente oferece.
fn edid_modes() -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    if let Ok(rd) = fs::read_dir("/sys/class/drm") {
        for e in rd.flatten() {
            let name = e.file_name().to_string_lossy().to_string();
            if !name.contains('-') {
                continue;
            }
            let base = e.path();
            let status = fs::read_to_string(base.join("status")).unwrap_or_default();
            if status.trim() != "connected" {
                continue;
            }
            if let Ok(m) = fs::read_to_string(base.join("modes")) {
                for line in m.lines() {
                    let l = line.trim().to_string();
                    if !l.is_empty() && !out.contains(&l) {
                        out.push(l);
                    }
                }
            }
        }
    }
    out.sort_by_key(|m| {
        let p: Vec<u32> = m.split('x').filter_map(|v| v.parse().ok()).collect();
        p.first().copied().unwrap_or(0) * p.get(1).copied().unwrap_or(0)
    });
    out
}

/// `"1280x1024"` -> `(1280, 1024)`.
fn parse_wh(s: &str) -> Option<(u32, u32)> {
    let (a, b) = s.split_once('x')?;
    Some((a.trim().parse().ok()?, b.trim().parse().ok()?))
}

/// Le a resolucao salva em `cfg_path()`.
///
/// Parser manual de dois inteiros - nao vale puxar uma dependencia de JSON
/// por um arquivo de 4 linhas.
fn load_cfg() -> Option<(u32, u32)> {
    let t = fs::read_to_string(cfg_path()).ok()?;
    let grab = |key: &str| -> Option<u32> {
        let i = t.find(key)?;
        let rest = &t[i + key.len()..];
        let start = rest.find(|c: char| c.is_ascii_digit())?;
        let digits: String = rest[start..]
            .chars()
            .take_while(|c| c.is_ascii_digit())
            .collect();
        digits.parse().ok()
    };
    Some((grab("\"width\"")?, grab("\"height\"")?))
}

/// Grava a resolucao escolhida, criando `~/.config` se necessario.
fn save_cfg(w: u32, h: u32) -> std::io::Result<()> {
    let p = cfg_path();
    if let Some(dir) = p.parent() {
        fs::create_dir_all(dir)?;
    }
    let mut f = fs::File::create(p)?;
    write!(f, "{{\n  \"width\": {},\n  \"height\": {}\n}}\n", w, h)
}

// ---------------------------------------------------------------- acoes
/// BUG 1 - copia as 8 bibliotecas legadas do Steam Runtime para a pasta do
/// jogo.
///
/// Nao sobrescreve arquivos existentes e nao escreve fora de `game_dir`; o
/// binario resolve tudo por `RPATH=$ORIGIN`, entao nenhum ajuste no sistema e'
/// necessario. No fim confere com `ldd` e informa o que ainda falta.
fn fix_libs(game_dir: &Path, log: &mut Vec<String>) {
    let srt = match first_steam_runtime() {
        Some(p) => p,
        None => {
            log.push("ERRO: Steam Runtime nao encontrado. Rode o Steam uma vez.".into());
            return;
        }
    };
    log.push(format!("Steam Runtime: {}", srt.display()));
    let mut copied = 0;
    for (rel, name) in LIBS {
        let src = srt.join(rel);
        let dst = game_dir.join(name);
        if dst.exists() {
            log.push(format!("  ja existe: {}", name));
            continue;
        }
        if !src.exists() {
            log.push(format!("  AVISO: ausente na runtime: {}", rel));
            continue;
        }
        match fs::copy(&src, &dst) {
            Ok(_) => {
                log.push(format!("  copiado: {}", name));
                copied += 1;
            }
            Err(e) => log.push(format!("  ERRO copiando {}: {}", name, e)),
        }
    }
    log.push(format!("{} arquivo(s) copiado(s).", copied));
    let miss = missing_libs(&game_dir.join(BIN_NAME));
    if miss.is_empty() {
        log.push("OK: todas as dependencias resolvidas.".into());
    } else {
        log.push(format!("AINDA FALTANDO: {:?}", miss));
    }
}

/// Remove as bibliotecas copiadas por `fix_libs()`.
fn revert_libs(game_dir: &Path, log: &mut Vec<String>) {
    for (_, name) in LIBS {
        let p = game_dir.join(name);
        if p.exists() {
            match fs::remove_file(&p) {
                Ok(_) => log.push(format!("  removido: {}", name)),
                Err(e) => log.push(format!("  ERRO removendo {}: {}", name, e)),
            }
        }
    }
}

/// Grava o tamanho de janela no chunk `GEN8` do `game.unx`.
///
/// Cria `game.unx.orig` antes de qualquer escrita e aborta sem tocar no
/// arquivo se a assinatura `FORM`/`GEN8` nao bater.
fn patch_gen8(data: &Path, w: u32, h: u32, log: &mut Vec<String>) {
    let backup = data.with_extension("unx.orig");
    if !backup.exists() {
        if let Err(e) = fs::copy(data, &backup) {
            log.push(format!("ERRO criando backup: {}", e));
            return;
        }
        log.push(format!("backup: {}", backup.display()));
    }
    let mut buf = match fs::read(data) {
        Ok(b) => b,
        Err(e) => {
            log.push(format!("ERRO lendo game.unx: {}", e));
            return;
        }
    };
    if &buf[0..4] != b"FORM" || &buf[8..12] != b"GEN8" {
        log.push("ERRO: layout inesperado, abortando para nao corromper.".into());
        return;
    }
    buf[0x4c..0x50].copy_from_slice(&w.to_le_bytes());
    buf[0x50..0x54].copy_from_slice(&h.to_le_bytes());
    match fs::write(data, &buf) {
        Ok(_) => log.push(format!("GEN8 defaultWindow -> {}x{}", w, h)),
        Err(e) => log.push(format!("ERRO gravando: {}", e)),
    }
}

/// Restaura o `game.unx` a partir do backup `.orig`.
fn unpatch_gen8(data: &Path, log: &mut Vec<String>) {
    let backup = data.with_extension("unx.orig");
    if !backup.exists() {
        log.push("nao ha backup para restaurar.".into());
        return;
    }
    match fs::copy(&backup, data) {
        Ok(_) => log.push("game.unx restaurado do backup.".into()),
        Err(e) => log.push(format!("ERRO restaurando: {}", e)),
    }
}

// ------------------------------------------------- resolucao nativa (binario)
/// Le o par `(device_w, device_h)` do runner em `NATIVE_OFF`.
///
/// Usa `seek` + 8 bytes: nao carrega o ELF inteiro em memoria.
fn read_native(bin: &Path) -> Option<(u32, u32)> {
    use std::io::{Read, Seek, SeekFrom};
    let mut f = fs::File::open(bin).ok()?;
    f.seek(SeekFrom::Start(NATIVE_OFF)).ok()?;
    let mut b = [0u8; 8];
    f.read_exact(&mut b).ok()?;
    let w = u32::from_le_bytes(b[0..4].try_into().ok()?);
    let h = u32::from_le_bytes(b[4..8].try_into().ok()?);
    Some((w, h))
}

/// Define a resolucao NATIVA fixa: patch no device do runner (.data) + GEN8.
fn patch_native(game_dir: &Path, w: u32, h: u32, log: &mut Vec<String>) {
    use std::io::{Seek, SeekFrom, Write};
    let bin = game_dir.join(BIN_NAME);
    let data = game_dir.join("assets/game.unx");

    let cur = match read_native(&bin) {
        Some(v) => v,
        None => {
            log.push("ERRO: nao consegui ler o device do binario.".into());
            return;
        }
    };
    if !NATIVE_KNOWN.contains(&cur) {
        log.push(format!(
            "AVISO: valor atual {:?} nao esta na lista conhecida; abortando por seguranca.",
            cur
        ));
        return;
    }

    // backup do binario
    let bkp = bin.with_extension("orig");
    if !bkp.exists() {
        match fs::copy(&bin, &bkp) {
            Ok(_) => log.push(format!("backup: {}", bkp.display())),
            Err(e) => {
                log.push(format!("ERRO criando backup do binario: {}", e));
                return;
            }
        }
    }

    match fs::OpenOptions::new().write(true).open(&bin) {
        Ok(mut f) => {
            let mut buf = [0u8; 8];
            buf[0..4].copy_from_slice(&w.to_le_bytes());
            buf[4..8].copy_from_slice(&h.to_le_bytes());
            if f.seek(SeekFrom::Start(NATIVE_OFF)).is_err() || f.write_all(&buf).is_err() {
                log.push("ERRO gravando device no binario.".into());
                return;
            }
            log.push(format!("device do runner -> {}x{}", w, h));
        }
        Err(e) => {
            log.push(format!("ERRO abrindo binario: {}", e));
            return;
        }
    }

    // GEN8 tem de casar, senao a janela nasce menor que o device
    patch_gen8(&data, w, h, log);
    log.push(format!(
        "resolucao nativa fixa: {}x{} (device + GEN8). Reinicie o jogo.",
        w, h
    ));
}

/// Restaura o binario do `.orig` e desfaz o patch do GEN8.
fn revert_native(game_dir: &Path, log: &mut Vec<String>) {
    let bin = game_dir.join(BIN_NAME);
    let bkp = bin.with_extension("orig");
    if bkp.exists() {
        match fs::copy(&bkp, &bin) {
            Ok(_) => log.push("binario restaurado do backup.".into()),
            Err(e) => log.push(format!("ERRO restaurando binario: {}", e)),
        }
    } else {
        log.push("sem backup do binario.".into());
    }
    unpatch_gen8(&game_dir.join("assets/game.unx"), log);
}

// ---------------- patches no game.unx (via UTMT) ----------------
//
// Estes tres patches nao sao byte-patch: mexem no sprite do cursor, na
// persistencia de um objeto e no codigo GML da room inicial. Por isso passam
// pelo UndertaleModCli (que recompila as entradas de codigo com seguranca).

const CURSOR_TIP: (i32, i32) = (332, 1);
const GUI_W: u32 = 1280;
const GUI_H: u32 = 720;
const MARKER_FILE: &str = "assets/.mcml-data-patches";

/// Caminho do marcador de estado dos patches de dados.
///
/// E' um arquivo de texto pequeno para a GUI saber o que esta aplicado sem
/// abrir o `game.unx` de 135 MB a cada frame.
fn marker_path(game_dir: &Path) -> PathBuf {
    game_dir.join(MARKER_FILE)
}

/// Onde esta o UndertaleModCli: $MCML_UTMT ou ~/.local/share/mcml-fixer/utmt.
fn utmt_dir() -> Option<PathBuf> {
    if let Ok(p) = std::env::var("MCML_UTMT") {
        let pb = PathBuf::from(p);
        if pb.join("UndertaleModCli").is_file() {
            return Some(pb);
        }
    }
    let pb = home().join(".local/share/mcml-fixer/utmt");
    if pb.join("UndertaleModCli").is_file() {
        return Some(pb);
    }
    None
}

const PATCH_CSX: &str = r#"
using System;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;

EnsureDataLoaded();

var spr = Data.Sprites.ByName("spr_mouse");
if (spr == null) { ScriptError("spr_mouse nao existe"); return; }
spr.OriginX = 332;
spr.OriginY = 1;
ScriptMessage($"MARK spr_mouse_origin={spr.OriginX},{spr.OriginY}");

var obj = Data.GameObjects.ByName("obj_mouse");
if (obj == null) { ScriptError("obj_mouse nao existe"); return; }
obj.Persistent = true;
ScriptMessage($"MARK obj_mouse_persistent={obj.Persistent}");

var ctx = new GlobalDecompileContext(Data);
var settings = new Underanalyzer.Decompiler.DecompileSettings();
var group = new CodeImportGroup(Data, ctx, settings) { MainThreadAction = MainThreadAction };
group.QueueAppend("gml_RoomCC_Languages_0_Create", "instance_create_depth(0, 0, -9999, obj_mouse);");
group.QueueAppend("gml_RoomCC_Start_0_Create", "display_set_gui_size(1280, 720);");

// O passe CRT recortava a application_surface com coordenadas fixas de 1280x720,
// mas a surface real tem o tamanho da janela. Com isso ele amostrava so a metade
// de cima e redesenhava por cima, apagando a barra de tarefas e o rodape das
// janelas do PC (botoes do XLO inclusive). Passa a amostrar a origem com a mesma
// escala do destino. O bloco abaixo e' unico da ramificacao "room == Computer".
string crt = "gml_Object_obj_crt_Draw_77";
group.QueueFindReplace(crt, @"    var x1 = 333;
    var y1 = 109;
    var x2 = 940;
    var y2 = 581;
    var surf_w = x2 - x1;
    var surf_h = y2 - y1;
    if (!surface_exists(surf_crt) || surface_get_width(surf_crt) != surf_w || surface_get_height(surf_crt) != surf_h)
    {
        if (surface_exists(surf_crt))
        {
            surface_free(surf_crt);
        }
        surf_crt = surface_create(surf_w, surf_h);
    }
    surface_set_target(surf_crt);
    draw_clear_alpha(c_black, 0);
    draw_surface_part(application_surface, x1, y1, surf_w, surf_h, 0, 0);", @"    var x1 = 333;
    var y1 = 109;
    var x2 = 940;
    var y2 = 581;
    var surf_w = x2 - x1;
    var surf_h = y2 - y1;
    var _kx = surface_get_width(application_surface) / base_w;
    var _ky = surface_get_height(application_surface) / base_h;
    var _sw = surf_w * _kx;
    var _sh = surf_h * _ky;
    if (!surface_exists(surf_crt) || surface_get_width(surf_crt) != _sw || surface_get_height(surf_crt) != _sh)
    {
        if (surface_exists(surf_crt))
        {
            surface_free(surf_crt);
        }
        surf_crt = surface_create(_sw, _sh);
    }
    surface_set_target(surf_crt);
    draw_clear_alpha(c_black, 0);
    draw_surface_part(application_surface, x1 * _kx, y1 * _ky, _sw, _sh, 0, 0);", false);

group.Import();
ScriptMessage("MARK gui=1280,720");
ScriptMessage("MARK crt_scale=fixed");
"#;

const CHECK_CSX: &str = r#"
using System;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Decompiler;

EnsureDataLoaded();

var spr = Data.Sprites.ByName("spr_mouse");
ScriptMessage($"MARK spr_mouse_origin={(spr != null ? spr.OriginX + "," + spr.OriginY : "none")}");
var obj = Data.GameObjects.ByName("obj_mouse");
ScriptMessage($"MARK obj_mouse_persistent={(obj != null ? obj.Persistent.ToString() : "none")}");

var ctx = new GlobalDecompileContext(Data);
var st = Data.ToolInfo.DecompilerSettings;
string s1 = "";
string s2 = "";
try { s1 = GetDecompiledText(Data.Code.ByName("gml_RoomCC_Start_0_Create"), ctx, st); } catch { s1 = "ERR"; }
try { s2 = GetDecompiledText(Data.Code.ByName("gml_RoomCC_Languages_0_Create"), ctx, st); } catch { s2 = "ERR"; }
string s3 = "";
try { s3 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_crt_Draw_77"), ctx, st); } catch { s3 = "ERR"; }try { s3 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_crt_Draw_77"), ctx, st); } catch { s3 = "ERR"; }
string s4 = "";
try { s4 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_player_Create_0"), ctx, st); } catch { s4 = "ERR"; }
string s5 = "";
try { s5 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_noguiin_Draw_0"), ctx, st); } catch { s5 = "ERR"; }
ScriptMessage($"MARK gui_fix={(s1.Contains("display_set_gui_size") ? "set" : "none")}");
ScriptMessage($"MARK cursor_instance={(s2.Contains("obj_mouse") ? "set" : "none")}");
ScriptMessage($"MARK crt_scale={(s3.Contains("_kx") ? "fixed" : "original")}");
"#;

/// Executa um script CSX dentro do UndertaleModCli.
///
/// O script e' gravado num temporario com o PID no nome (permite execucoes
/// concorrentes) e o processo roda com `current_dir` no diretorio do UTMT,
/// que resolve o runtime .NET por caminho relativo. Devolve stdout+stderr;
/// em caso de falha, a mensagem carrega as tres ultimas linhas da saida.
/// CSX do mod Distribuidor + Carburador (aplicado pelo UTMT).
const MOD_PATCH_CSX: &str = r#"using System;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;

EnsureDataLoaded();

var ctx = new GlobalDecompileContext(Data);
var st = new Underanalyzer.Decompiler.DecompileSettings();
var g = new CodeImportGroup(Data, ctx, st) { MainThreadAction = MainThreadAction };

g.QueueAppend("gml_RoomCC_Start_0_Create", @"
// =====================================================================
//  MOD DISTRIBUIDOR v2  -  item de carro (0 nenhum, 1 original, 2 performance)
//  instalado por mcml-fixer --mod-install
// =====================================================================
global.mod_dist_versao = ""2.0"";
global.mod_dist_preco = [0, 0, 12000];
global.mod_dist_nome = [""Nenhum"", ""Original"", ""Competicao""];
global.mod_dist_def = {
    avanco_inicial: 10, cent_total: 22, rpm_inicio: 1000, rpm_fim: 3200,
    curva: 1, vac_total: 12, vac_limiar: 0, dwell: 45, desgaste: 0, ganho: 100
};
// padroes de fisica: o ini [fisica] sobrescreve; cada carro copia ao criar
// globais do jogo que o mod le: garante que existem em qualquer sala
if (!variable_global_exists(""turbo_pressure"")) global.turbo_pressure = 0;
if (!variable_global_exists(""turbo"")) global.turbo = 0;
if (!variable_global_exists(""iat"")) global.iat = 25;
if (!variable_global_exists(""temp"")) global.temp = 25;
if (!variable_global_exists(""engine"")) global.engine = 0;
if (!variable_global_exists(""rpm"")) global.rpm = 900;
if (!variable_global_exists(""kmh"")) global.kmh = 0;
if (!variable_global_exists(""gear"")) global.gear = 0;
if (!variable_global_exists(""ignicao"")) global.ignicao = 0;
if (!variable_global_exists(""afr"")) global.afr = 14.7;
if (!variable_global_exists(""cash"")) global.cash = 0;
if (!variable_global_exists(""dt"")) global.dt = 1;

global.mod_rev = 0;
// ---- dinheiro (oculto): [dev] cash=N ----
global.dev_cash = 0;
global.dev_cash_modo = 0;   // 0 = piso (nunca abaixo de N), 1 = valor exato
global.dev_cash_rev = -1;

global.dev_cash_aplicar = function() {
    if (global.dev_cash <= 0) return 0;
    if (global.dev_cash_modo == 1) {
        if (global.dev_cash_rev == global.mod_rev) return 0;
        global.cash = global.dev_cash;
        global.dev_cash_rev = global.mod_rev;
        return 1;
    }
    if (global.cash < global.dev_cash) {
        global.cash = global.dev_cash;
        return 1;
    }
    return 0;
};

// dinheiro nunca fica negativo
global.cash_guard = function() {
    if (!variable_global_exists(""cash"")) return 0;
    if (global.cash < 0) {
        global.cash = 0;
        return 1;
    }
    return 0;
};
global.phys_def = { raio: 0.31, eficiencia: 0.90, mu: 1.00, share: 1.00, embreagem: 0, inercia: 0.15 };
global.mod_dist_estado = { menu: 0, sel: 0, aviso: """", spr: -1, spr_try: 0, rpm: 800, map: 0, cent: 0, vac: 0, avanco: 10, ideal: 0, bonus: 0, q: 1, knock: 0, dwell_ef: 45, dwell_ms: 5, tok: -1, icon: -1, icon_try: 0 };
global.mod_dist_ativo = 0;

global.mod_dist_car = function() {
    if (!is_array(global.car_data)) return undefined;
    if (global.vehicles_array < 0 || global.vehicles_array >= array_length(global.car_data)) return undefined;
    return global.car_data[global.vehicles_array];
};

// tipo do motor: -1 = indefinido, 0 = injetado, 1 = carburado.
// carburador = -1 e o valor de fabrica (EstruturaSalvamento:129) e o jogo so
// resolve para 1/3 dentro da Garage (obj_player Step:78-93). Ler -1 como
// injetado era o BUG 14: gravava distribuidor=0 no carro salvo e matava o F6.
global.mod_dist_tipo = function(car) {
    if (is_undefined(car)) return -1;
    if (!variable_struct_exists(car, ""carburador"")) return -1;
    var _c = car.carburador;
    if (is_undefined(_c)) return -1;
    // bicos instalados = injetado, nao importa o que diga o carburador
    if (variable_struct_exists(car, ""bicos"") && car.bicos > 0) return 0;
    if (_c > 0) return 1;
    if (_c == 0) return 0;
    // indeterminado: fail-open, o unico efeito do mod e instalar distribuidor
    return 1;
};

global.mod_dist_modelo = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    var _t = global.mod_dist_tipo(car);
    if (_t < 0) return 0;
    // todo motor carburado sai de fabrica com o distribuidor original
    if (!variable_struct_exists(car, ""distribuidor"") || is_undefined(car.distribuidor)) {
        car.distribuidor = (_t == 1) ? 1 : 0;
    }
    // cura carro estragado pela versao antiga (gravou 0 com carburador -1)
    else if (_t == 1 && car.distribuidor == 0) {
        car.distribuidor = 1;
    }
    return car.distribuidor;
};

// limites fisicos por modelo: [ini_min, ini_max, cent_max, rpm_fim_max, vac_max, dwell_min, dwell_max, ganho_max]
// [ini_min, ini_max, cent_max, rpm_fim_max, vac_max, dwell_min, dwell_max, ganho_max, corte_rpm]
global.mod_dist_limites = function(modelo) {
    if (modelo == 1) return [0, 12, 20, 5500, 14, 38, 52, 45, 5500];
    return [0, 20, 30, 7500, 20, 30, 60, 100, 7500];
};

// configuracao salva junto com o carro (persiste no save do jogo)
global.mod_dist_cfg = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return global.mod_dist_def;
    if (!variable_struct_exists(car, ""dist_cfg"")) car.dist_cfg = undefined;
    if (is_undefined(car.dist_cfg)) {
        car.dist_cfg = {
            avanco_inicial: global.mod_dist_def.avanco_inicial,
            cent_total: global.mod_dist_def.cent_total,
            rpm_inicio: global.mod_dist_def.rpm_inicio,
            rpm_fim: global.mod_dist_def.rpm_fim,
            curva: global.mod_dist_def.curva,
            vac_total: global.mod_dist_def.vac_total,
            vac_limiar: global.mod_dist_def.vac_limiar,
            dwell: global.mod_dist_def.dwell,
            desgaste: 0,
            ganho: global.mod_dist_def.ganho,
            ver: 2
        };
    }
    // config antiga (ganho vinha 50 e diluia o bonus): sobe uma vez
    if (!variable_struct_exists(car.dist_cfg, ""ver"")) {
        car.dist_cfg.ver = 2;
        car.dist_cfg.ganho = 100;
    }
    return car.dist_cfg;
};

// Aplica o ini ao carro SALVO quando o arquivo muda (compara data de modificacao).
// Assim o que voce grava com --phys/--mod-set vale no proximo boot, sem comprar
// carro novo e sem apagar o que foi ajustado no F6 (o F6 nao toca no ini).
global.mod_cfg_tick = 0;
global.mod_cfg_aplicar = function() {
    if (current_time < global.mod_cfg_tick) return 0;
    global.mod_cfg_tick = current_time + 1000;
    var _st = variable_global_exists(""mod_rev"") ? global.mod_rev : 0;
    if (_st <= 0) return 0;
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 0;
    if (variable_struct_exists(car, ""cfg_stamp"") && car.cfg_stamp == _st) return 0;

    var _tinha = (variable_struct_exists(car, ""phys_cfg"") && !is_undefined(car.phys_cfg)) ? 1 : 0;
    car.phys_cfg = { raio: global.phys_def.raio, eficiencia: global.phys_def.eficiencia,
                     mu: global.phys_def.mu, share: global.phys_def.share,
                     embreagem: global.phys_def.embreagem, inercia: global.phys_def.inercia };

    var _dist = 0;
    if (global.mod_dist_modelo() > 0) {
        var _c = global.mod_dist_cfg();
        _c.avanco_inicial = global.mod_dist_def.avanco_inicial;
        _c.cent_total = global.mod_dist_def.cent_total;
        _c.rpm_inicio = global.mod_dist_def.rpm_inicio;
        _c.rpm_fim = global.mod_dist_def.rpm_fim;
        _c.curva = global.mod_dist_def.curva;
        _c.vac_total = global.mod_dist_def.vac_total;
        _c.vac_limiar = global.mod_dist_def.vac_limiar;
        _c.dwell = global.mod_dist_def.dwell;
        _c.ganho = global.mod_dist_def.ganho;
        _c.ver = 2;
        _dist = 1;
    }
    car.cfg_stamp = _st;

    var _f = file_text_open_write(""mod_cfg_diag.txt"");
    file_text_write_string(_f, ""aplicado=1 phys_antes="" + string(_tinha) + "" dist="" + string(_dist));
    file_text_write_string(_f, "" raio="" + string(car.phys_cfg.raio) + "" mu="" + string(car.phys_cfg.mu));
    file_text_write_string(_f, "" inercia="" + string(car.phys_cfg.inercia) + "" ganho="" + string(global.mod_dist_def.ganho));
    file_text_close(_f);
    return 1;
};

global.mod_dist_comprar = function(modelo) {
    var car = global.mod_dist_car();
    var e = global.mod_dist_estado;
    if (is_undefined(car)) { e.aviso = ""sem carro selecionado""; return; }
    if (global.mod_dist_tipo(car) != 1) { e.aviso = ""so instala em motor carburado""; return; }
    if (global.mod_dist_modelo() == modelo) { e.aviso = ""este modelo ja esta instalado""; return; }
    if (modelo == 1) {
        // voltar ao original de fabrica: nao custa nada, mas nao devolve o dinheiro
        car.distribuidor = 1;
        car.dist_cfg = undefined;
        global.mod_dist_cfg();
        e.aviso = ""distribuidor original reinstalado"";
        global.mod_dist_ativo = 1;
        return;
    }
    var preco = global.mod_dist_preco[modelo];
    if (global.cash < preco) { e.aviso = ""faltam $"" + string(preco - global.cash); return; }
    global.cash -= preco;
    car.distribuidor = modelo;
    car.dist_cfg = undefined;
    global.mod_dist_cfg();
    e.aviso = ""comprado: "" + global.mod_dist_nome[modelo];
    global.mod_dist_ativo = 1;
};

global.mod_dist_remover = function() {
    var car = global.mod_dist_car();
    var e = global.mod_dist_estado;
    if (is_undefined(car)) return;
    var m = global.mod_dist_modelo();
    if (m <= 1) { e.aviso = ""ja esta com o original de fabrica""; return; }
    global.cash += floor(global.mod_dist_preco[m] * 0.5);
    car.distribuidor = 1;
    car.dist_cfg = undefined;
    e.aviso = ""voltou ao original (+$"" + string(floor(global.mod_dist_preco[m] * 0.5)) + "")"";
    global.mod_dist_ativo = 1;
};

global.mod_dist_padrao_modelo = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return;
    car.dist_cfg = undefined;
    global.mod_dist_cfg();
};

// limite de detonacao depende do carro: compressao (pistao), cabecote e combustivel
global.mod_dist_knock_limite = function(car) {
    var lim = 34;
    if (car.pistao >= 3) lim -= 3;
    if (car.pistao >= 4) lim -= 1;
    if (car.cabecote >= 3) lim += 1;
    if (global.fuel == 2) lim += 3;
    if (global.fuel == 3) lim += 5;
    if (global.fuel == 4) lim += 7;
    return lim;
};

global.mod_dist_calcular = function() {
    var e = global.mod_dist_estado;
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return 0;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _rpm = clamp(global.rpm, 800, 8000);
    var _map = clamp(global.turbo_pressure, -0.8, 4);
    var _ini = d.rpm_inicio;
    var _fim = max(_ini + 100, min(d.rpm_fim, lim[3]));
    // curva de dois estagios: duas molas - a primaria entra cedo, a secundaria depois
    var _faixa = max(100, _fim - _ini);
    var _div = 0.62;
    if (d.curva == 0) _div = 0.5;
    if (d.curva == 2) _div = 0.75;
    var t1 = clamp((_rpm - _ini) / (_faixa * _div), 0, 1);
    var t2 = clamp((_rpm - _ini - (_faixa * _div)) / (_faixa * (1 - _div)), 0, 1);
    var area = min(d.cent_total, lim[2]);
    var cent = area * ((_div * t1) + ((1 - _div) * t2));
    // vacuo medido em inHg, como nas curvas de distribuidor: comeca em ~5 inHg e
    // satura em ~15 inHg (1 inHg = 0.0339 bar). Ported vacuum: nada acima de 0 bar.
    var _vac_inhg = clamp((0 - _map) / 0.0339, 0, 30);
    var vac = min(d.vac_total, lim[4]) * clamp((_vac_inhg - 5) / 10, 0, 1);
    e.rpm = _rpm;
    e.map = _map;
    e.cent = cent;
    e.vac = vac;
    e.dwell_ef = clamp(d.dwell - (d.desgaste * 0.1), lim[5], lim[6]);
    e.avanco = clamp(min(d.avanco_inicial, lim[1]) + cent + vac, lim[0], 45);
    return e.avanco;
};

global.mod_dist_fator = function() {
    var e = global.mod_dist_estado;
    var car = global.mod_dist_car();
    if (is_undefined(car)) return 1;
    if (global.mod_dist_tipo(car) != 1) return 1;
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return 1;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    // corte de giro do modelo: acima disso o distribuidor corta a faisca
    var _corte = lim[8];
    var _rpm_max_carro = 99999;
    if (variable_struct_exists(car, ""rpm"")) _rpm_max_carro = car.rpm;
    if (global.rpm > min(_corte, _rpm_max_carro)) return 0;
    var adv = global.mod_dist_calcular();
    var tn = clamp((e.rpm - 800) / 7200, 0, 1);
    var ideal = lerp(14, 38, tn) - (e.map * 3.5);
    var pos = (clamp((adv - ideal) / 8, -2, 2) + 1) / 2;
    var bonus = 0;
    if (pos >= 0.9) bonus = lerp(0.55, 0.6, (pos - 0.9) / 0.1);
    else if (pos >= 0.7) bonus = lerp(0.4, 0.55, (pos - 0.7) / 0.2);
    else if (pos >= 0.4) bonus = lerp(0.2, 0.4, (pos - 0.4) / 0.3);
    else bonus = -power((0.4 - pos) / 0.4, 1.3) * 0.25;
    // o modelo define o teto de ganho (Original 45%, Competicao 100%) e o
    // parametro ganho regula quanto desse teto o carro usa. Antes isso era
    // multiplicado E limitado, o que derrubava o bonus maximo para ~25%.
    var _cap = lim[7] / 100;
    if (bonus > _cap) bonus = _cap;
    bonus *= clamp(d.ganho, 0, 100) / 100;
    // o angulo de dwell vira tempo de saturacao da bobina: o distribuidor gira a
    // metade do motor, entao uma volta leva 120000/rpm ms
    var _dwell_ms = (e.dwell_ef / 360) * (120000 / max(800, e.rpm));
    // abaixo de ~1.2 ms a faisca enfraquece; acima de ~3.2 ms a bobina satura
    var q = clamp((_dwell_ms - 1.2) / 2.0, 0, 1);
    e.dwell_ms = _dwell_ms;
    e.ideal = ideal;
    e.bonus = bonus;
    e.q = q;
    // igual ao nativo: bonus cheio, sem diluicao. A faisca fraca (q<1) so piora.
    var f = (1 + bonus);
    if (q < 1) f *= (0.90 + 0.10 * q);
    var falha = max(0, (1 - q) * 0.6 + (d.desgaste / 100) * 0.4);
    if (random(1) < falha * clamp(e.rpm / 6000, 0, 1) * 0.05) f *= 0.55;
    return f;
};

// dano de detonacao: mesma curva do ECU do jogo, mas calculada aqui dentro
// (o mod roda fora do evento do player e nao enxerga funcoes locais dele)
global.mod_dist_dano_mult = function(peca, turbo) {
    var mult = 0;
    switch (peca) {
        case 0: mult = 0.75; break;
        case 1: mult = 0.5; break;
        case 2: mult = 0.25; break;
        case 3: mult = turbo ? 2 : 0.5; break;
        case 4: mult = turbo ? 3.5 : 0.25; break;
    }
    return mult * 0.35;
};

global.mod_dist_dano = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return;
    if (global.engine != 1) return;
    if (global.mod_dist_tipo(car) != 1) return;
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return;
    var d = global.mod_dist_cfg();
    var e = global.mod_dist_estado;
    var dt = global.dt;
    d.desgaste = min(100, d.desgaste + (0.0015 + abs(e.avanco) * 0.00002) * dt);
    var _iat = global.iat ?? 25;
    var _ect = global.temp ?? 90;
    var limite = global.mod_dist_knock_limite(car) - (e.map * 3.5);
    if (e.map > 0) limite -= (e.map * 2);
    limite -= max(0, _iat - 25) * 0.15;          // ar quente = menos avanco
    limite -= max(0, _ect - 95) * 0.20;          // motor quente = menos avanco
    e.knock = max(0, e.avanco - limite);
    // detonacao castiga cabecote e pistao (entra em motor_desgaste)
    if (e.knock > 0.6) {
        if (variable_struct_exists(car, ""vcabecote"")) {
            car.vcabecote = max(0, car.vcabecote - (e.knock * dt * 0.5));
        }
        if (variable_struct_exists(car, ""vpistao"")) {
            car.vpistao = max(0, car.vpistao - (e.knock * dt * 0.25));
        }
    }
    // dwell grande em baixa rotacao satura a bobina: esquenta e falha
    if (e.dwell_ms > 8 && e.rpm < 2500) {
        global.temp += (e.dwell_ms - 8) * 0.01 * dt;
        if (random(1) < 0.002 * (e.dwell_ms - 8)) d.desgaste = min(100, d.desgaste + 0.2);
    }
    if (e.knock > 0 && global.kmh >= 20) {
        car.vpistao -= e.knock * global.mod_dist_dano_mult(car.pistao, car.turbo) * 0.35 * dt;
        car.vbielas -= e.knock * global.mod_dist_dano_mult(car.bielas, car.turbo) * 0.30 * dt;
        global.temp += e.knock * 0.25 * dt;
    }
};

global.mod_dist_input = function() {
    var e = global.mod_dist_estado;
    var car = global.mod_dist_car();
    if (keyboard_check_pressed(vk_f6)) { e.menu = (e.menu == 0) ? 1 : 0; e.sel = 0; e.aviso = """"; }
    if (e.menu == 0) return;
    if (is_undefined(car)) return;
    if (keyboard_check_pressed(vk_tab)) {
        if (e.menu == 1) e.menu = 2;
        else if (e.menu == 2) e.menu = 3;
        else e.menu = 1;
        e.sel = 0;
        e.aviso = """";
    }
    if (e.menu == 2) {
        if (keyboard_check_pressed(ord(""1""))) global.mod_dist_comprar(1);
        if (keyboard_check_pressed(ord(""2""))) global.mod_dist_comprar(2);
        return;
    }
    if (e.menu == 3) {
        var _cc = global.mod_carb_cfg();
        if (!is_undefined(_cc) && global.mod_dist_tipo(car) == 1) {
            if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod 3;
            if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + 2) mod 3;
            var _d3 = 0;
            if (keyboard_check_pressed(vk_right)) _d3 = 1;
            if (keyboard_check_pressed(vk_left)) _d3 = -1;
            if (_d3 != 0) {
                switch (e.sel) {
                    case 0: _cc.lenta = clamp(_cc.lenta + _d3, 20, 60); break;
                    case 1: _cc.agulha = clamp(_cc.agulha + _d3, 20, 80); break;
                    case 2: _cc.borboleta = clamp(_cc.borboleta + (_d3 * 5), 0, 100); break;
                }
                e.aviso = """";
            }
            if (keyboard_check_pressed(vk_f7)) {
                var _gi7 = global.mod_carb_gicle_ideal(max(1, car.carburador));
                _cc.lenta = round(_gi7 * 0.35);
                _cc.agulha = round(_gi7 * 0.90);
                _cc.borboleta = 50;
                e.aviso = ""carburador no padrao"";
            }
        }
        return;
    }
    if (keyboard_check_pressed(ord(""3""))) global.mod_dist_remover();
    if (keyboard_check_pressed(vk_f7)) { global.mod_dist_padrao_modelo(); e.aviso = ""padrao do modelo""; }
    var modelo = global.mod_dist_modelo();
    if (modelo <= 0) return;
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _n = 10;
    if (keyboard_check_pressed(vk_down)) e.sel = (e.sel + 1) mod _n;
    if (keyboard_check_pressed(vk_up)) e.sel = (e.sel + _n - 1) mod _n;
    var _dir = 0;
    if (keyboard_check_pressed(vk_right)) _dir = 1;
    if (keyboard_check_pressed(vk_left)) _dir = -1;
    if (_dir != 0) {
        switch (e.sel) {
            case 0: d.avanco_inicial = clamp(d.avanco_inicial + _dir, lim[0], lim[1]); break;
            case 1: d.cent_total = clamp(d.cent_total + (_dir * 2), 0, lim[2]); break;
            case 2: d.rpm_inicio = clamp(d.rpm_inicio + (_dir * 100), 600, 4000); break;
            case 3: d.rpm_fim = clamp(d.rpm_fim + (_dir * 100), 1200, lim[3]); break;
            case 4: d.curva = clamp(d.curva + _dir, 0, 2); break;
            case 5: d.vac_total = clamp(d.vac_total + _dir, 0, lim[4]); break;
            case 6: d.vac_limiar = clamp(d.vac_limiar + (_dir * 0.1), -0.8, 1); break;
            case 7: d.dwell = clamp(d.dwell + _dir, lim[5], lim[6]); break;
            case 8: d.desgaste = clamp(d.desgaste + (_dir * 5), 0, 100); break;
            case 9: d.ganho = clamp(d.ganho + (_dir * 5), 0, lim[7]); break;
        }
        e.aviso = """";
    }
};

// roda o modelo com um parametro no valor dado e devolve o bonus resultante
global.mod_dist_sim = function(_sel, _val) {
    var d = global.mod_dist_cfg();
    var _k = [""avanco_inicial"", ""cent_total"", ""rpm_inicio"", ""rpm_fim"", ""curva"", ""vac_total"", ""vac_limiar"", ""dwell"", ""desgaste"", ""ganho""];
    var _old = variable_struct_get(d, _k[_sel]);
    variable_struct_set(d, _k[_sel], _val);
    global.mod_dist_calcular();
    global.mod_dist_fator();
    var _b = global.mod_dist_estado.bonus;
    variable_struct_set(d, _k[_sel], _old);
    global.mod_dist_calcular();
    global.mod_dist_fator();
    return _b;
};

// tela do carburador: circuitos de lenta, agulha e borboleta
global.mod_carb_ui = function() {
    var e = global.mod_dist_estado;
    var car = global.mod_dist_car();
    draw_set_font(-1);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    var _x = 250;
    var _y = 40;
    var _w = 780;
    var _h = 420;
    draw_set_alpha(0.93);
    draw_set_color(c_black);
    draw_rectangle(_x, _y, _x + _w, _y + _h, false);
    draw_set_alpha(1);
    draw_set_color(make_color_rgb(60, 130, 90));
    draw_rectangle(_x, _y, _x + _w, _y + 34, false);
    draw_set_color(c_white);
    if (is_undefined(car)) {
        draw_text(_x + 12, _y + 8, ""CARBURADOR"");
        draw_text(_x + 20, _y + 60, ""sem carro selecionado"");
        return;
    }
    draw_text(_x + 12, _y + 8, ""CARBURADOR - tipo "" + string(max(1, car.carburador)));
    var cfg = global.mod_carb_cfg();
    if (is_undefined(cfg) || global.mod_dist_tipo(car) != 1) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 20, _y + 60, ""Este motor e injetado: nao tem carburador para ajustar."");
        draw_set_color(c_white);
        return;
    }
    var _labels = [""Gicle de lenta"", ""Agulha (media)"", ""Borboleta""];
    var _vals = [cfg.lenta, cfg.agulha, cfg.borboleta];
    for (var i = 0; i < 3; i++) {
        var _ry = _y + 54 + (i * 30);
        if (i == e.sel) {
            draw_set_color(make_color_rgb(50, 70, 60));
            draw_rectangle(_x + 20, _ry - 2, _x + _w - 20, _ry + 24, false);
        }
        draw_set_color(c_white);
        draw_text(_x + 30, _ry + 4, _labels[i]);
        draw_set_color(make_color_rgb(255, 210, 120));
        draw_text(_x + _w - 120, _ry + 4, string(_vals[i]));
    }
    var _d1 = [""Mistura em marcha lenta e na saida."",
               ""Mistura na faixa media (cruzeiro)."",
               ""Abertura inicial da borboleta.""];
    var _d2 = [""Baixo = pobre, falha frio. Alto = rico, afoga e suja."",
               ""Baixo = pobre em media. Alto = rico, perde potencia e gasta."",
               ""Baixo = lenta baixa. Alto = lenta alta e resposta brusca.""];
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 30, _y + 158, _d1[e.sel]);
    draw_text(_x + 30, _y + 176, _d2[e.sel]);
    var _gi = global.mod_carb_gicle_ideal(max(1, car.carburador));
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 30, _y + 214, ""gicle principal ideal deste modelo: "" + string(_gi));
    draw_set_color(make_color_rgb(170, 200, 255));
    if (variable_global_exists(""afr"")) {
        draw_text(_x + 30, _y + 240, ""AFR atual: "" + string(round(global.afr * 10) / 10) + ""   (14.7 = estequeometrico)"");
    }
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 30, _y + _h - 60, ""setas ajustam   F7 padrao   TAB proxima aba   F6 fecha"");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 30, _y + _h - 36, e.aviso);
    draw_set_color(c_white);
};

// categoria de compra exclusiva de distribuidores
global.mod_dist_loja = function() {
    var e = global.mod_dist_estado;
    var modelo = global.mod_dist_modelo();
    draw_set_font(-1);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    var _x = 250;
    var _y = 40;
    var _w = 780;
    var _h = 400;
    draw_set_alpha(0.93);
    draw_set_color(c_black);
    draw_rectangle(_x, _y, _x + _w, _y + _h, false);
    draw_set_alpha(1);
    draw_set_color(make_color_rgb(200, 60, 60));
    draw_rectangle(_x, _y, _x + _w, _y + 34, false);
    draw_set_color(c_white);
    draw_text(_x + 12, _y + 8, ""LOJA - DISTRIBUIDORES"");
    draw_set_halign(fa_right);
    draw_text(_x + _w - 12, _y + 8, ""dinheiro: $"" + string(global.cash));
    draw_set_halign(fa_left);
    global.mod_dist_draw_icon(_x + 150, _y + 210, 300);
    for (var i = 0; i < 2; i++) {
        var _m = i + 1;
        var _ry = _y + 56 + (i * 112);
        var _inst = (modelo == _m);
        draw_set_color(_inst ? make_color_rgb(45, 80, 55) : make_color_rgb(32, 32, 40));
        draw_rectangle(_x + 300, _ry, _x + _w - 20, _ry + 98, false);
        draw_set_color(c_white);
        draw_text(_x + 312, _ry + 8, ""["" + string(_m) + ""]  "" + global.mod_dist_nome[_m]);
        var _p = global.mod_dist_preco[_m];
        draw_text(_x + 312, _ry + 28, (_p <= 0) ? ""incluso de fabrica"" : ""$"" + string(_p));
        var _lim = global.mod_dist_limites(_m);
        draw_set_color(make_color_rgb(180, 190, 210));
        draw_text(_x + 312, _ry + 50, ""avanco ate "" + string(_lim[1]) + ""   centrifugo "" + string(_lim[2]) + ""   ate "" + string(_lim[3]) + "" rpm"");
        draw_text(_x + 312, _ry + 68, ""vacuo ate "" + string(_lim[4]) + ""   dwell "" + string(_lim[5]) + ""-"" + string(_lim[6]) + ""   ganho ate "" + string(_lim[7]) + ""%"");
        if (_inst) {
            draw_set_color(make_color_rgb(120, 255, 140));
            draw_text(_x + _w - 140, _ry + 8, ""INSTALADO"");
        }
    }
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 300, _y + _h - 62, ""[1] original   [2] competicao   TAB ajustar   F6 fechar"");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 300, _y + _h - 40, e.aviso);
    draw_set_color(c_white);
};

// motor carburado = unico que aceita distribuidor
global.mod_dist_pode = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return false;
    return (global.mod_dist_tipo(car) == 1);
};

// ---- potencia real: hp = torque(Nm) * rpm / K ----
// A potencia deixa de ser uma curva propria e passa a ser derivada do torque
// instantaneo e do giro, como num motor de verdade (P = T * w).
// Em unidades imperiais K = 5252 (lb-ft). Aqui o torque esta em Nm e o jogo
// mede hp, entao K = 7121 em unidades puras; 0.626 e a calibracao contra a
// propria curva de torque do jogo, para o pico de torque*rpm cair exatamente
// no maxhp declarado do motor (medido nas tres faixas: 45, 99 e 171 hp).
global.pot_hp = function(_torque, _rpm, _maxtorque, _maxrpm, _maxhp) {
    if (_maxhp <= 0 || _maxtorque <= 0 || _maxrpm <= 0) return 0;
    var _k = 0.626 * _maxtorque * _maxrpm / _maxhp;
    return (_torque * _rpm) / _k;
};

// =====================================================================
//  MOTOR: um lugar so para todo efeito de peca/ajuste
// =====================================================================
// leitura segura de campo (save antigo pode nao ter a chave)
global.motor_campo = function(_car, _nome) {
    if (is_undefined(_car)) return 0;
    if (!variable_struct_exists(_car, _nome)) return 0;
    var _v = variable_struct_get(_car, _nome);
    if (is_undefined(_v)) return 0;
    return _v;
};

// efeito conjunto das pecas no torque (ganho pequeno e cumulativo)
global.motor_pecas = function(_car) {
    var _m = 1;
    _m *= 1 + (0.010 * min(global.motor_campo(_car, ""pistao""), 3));
    _m *= 1 + (0.005 * min(global.motor_campo(_car, ""bielas""), 3));
    _m *= 1 + (0.008 * min(global.motor_campo(_car, ""bloco""), 3));
    _m *= 1 + (0.006 * min(global.motor_campo(_car, ""vira""), 3));
    _m *= 1 + (0.015 * min(global.motor_campo(_car, ""filtro""), 2));
    _m *= 1 + (0.020 * min(global.motor_campo(_car, ""admissao""), 2));
    _m *= 1 + (0.018 * min(global.motor_campo(_car, ""escape""), 2));
    _m *= 1 + (0.018 * min(global.motor_campo(_car, ""pipe""), 2));
    return _m;
};

// cabecote do carro do jogador (evita aspas dentro do CSX)
global.motor_cabecote_player = function() {
    return global.motor_campo(global.mod_dist_car(), ""cabecote"");
};

// cabecote troca o conjunto de valvulas (+4 por nivel, teto 24)
global.motor_valvulas = function(_base, _cab) {
    if (is_undefined(_base)) _base = 8;
    return clamp(_base + (4 * min(_cab, 3)), 8, 24);
};

// boost / deslocamento: o que hoje vive em _engine_multiplier
global.phys_engine_fator = function(_turbo, _piston, _litros) {
    if (_turbo > 0) {
        return clamp(1 + (power(_turbo, 1.3) * 0.35), 1, 2);
    }
    var _cil = _piston / max(1, _piston);
    return clamp(0.6 + (_cil * 0.35) + (_litros * 0.05), 0.9, 1.35);
};

// faixa ideal de AFR por combustivel e giro (mesma tabela do jogo)
global.motor_afr_ideal = function(_rpm, _rpm_max, _turbo, _fuel) {
    var _ratio = clamp(_rpm / max(1, _rpm_max), 0, 1);
    var _bmin = 12.35, _amin = (_turbo > 0) ? 11.76 : 12.35;
    if (_fuel == 2) { _bmin = 8.82; _amin = (_turbo > 0) ? 7.02 : 7.47; }
    else if (_fuel == 3) { _bmin = 6.08; _amin = (_turbo > 0) ? 4.74 : 4.99; }
    else if (_fuel == 4) { _bmin = 1.53; _amin = (_turbo > 0) ? 1.12 : 1.19; }
    return lerp(_bmin, _amin, power(_ratio, 1.8));
};

// AFR do carburador: gicle ideal por tipo + blend do 2o corpo + circuitos do mod
global.motor_afr = function(_car, _rpm, _rpm_max, _turbo, _fuel) {
    if (is_undefined(_car)) return 14.7;
    var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, ""carburador"")) : 0;
    if (_carb <= 0) return 14.7;
    var _tabela_ideal = [38, 42, 46, 48, 50, 53, 56, 60];
    var _tabela_sens = [0.018, 0.022, 0.026, 0.030, 0.034, 0.038, 0.042, 0.048];
    var _i = clamp(_carb, 1, 8) - 1;
    var _ratio = clamp(_rpm / max(1, _rpm_max), 0, 1);
    var _min = global.motor_afr_ideal(_rpm, _rpm_max, _turbo, _fuel);
    var _bmax = 14.99;
    var _amax = (_turbo > 0) ? 12.35 : 12.94;
    if (_fuel == 2) { _bmax = 9.18; _amax = (_turbo > 0) ? 7.38 : 7.74; }
    else if (_fuel == 3) { _bmax = 6.4; _amax = (_turbo > 0) ? 4.99 : 5.25; }
    else if (_fuel == 4) { _bmax = 1.61; _amax = (_turbo > 0) ? 1.19 : 1.26; }
    var _bmin = 12.35;
    if (_fuel == 2) { _bmin = 8.82; } else if (_fuel == 3) { _bmin = 6.08; } else if (_fuel == 4) { _bmin = 1.53; }
    var _max = lerp(_bmax, _amax, power(_ratio, 1.8));
    var _mid = (_min + _max) / 2;
    var _gicle = global.motor_campo(_car, ""gicle"");
    var _gicle2 = global.motor_campo(_car, ""gicle_2nd"");
    var _ativo = _gicle;
    if (_carb >= 3 && _rpm >= (_rpm_max / 2)) {
        var _blend = power(clamp((_rpm - (_rpm_max / 2)) / (_rpm_max / 2), 0, 1), 0.7);
        _ativo = lerp(_gicle, _gicle2, _blend);
    }
    var _desvio = (_ativo - _tabela_ideal[_i]) + global.mod_carb_desvio(_ratio, global.mod_carb_cfg(), _tabela_ideal[_i]);
    return max(_mid - (_desvio * _tabela_sens[_i] * _mid), 0.5);
};

// AFR dos injetados: duracao de injecao define o desvio (70 = referencia)
global.motor_afr_inj = function(_rpm, _rpm_max, _turbo, _fuel, _injecao) {
    var _ideal = global.motor_afr_ideal(_rpm, _rpm_max, _turbo, _fuel);
    var _desvio = clamp((_injecao - 70) / 30, -1, 1);
    return max(_ideal * (1 - (_desvio * 0.18)), 0.5);
};

// mistura -> multiplicador (mesma curva do jogo: 1.45 ideal, 0.70 pessimo)
global.motor_mistura_mult = function(_afr, _rpm, _rpm_max, _turbo, _fuel) {
    var _ideal_min = global.motor_afr_ideal(_rpm, _rpm_max, _turbo, _fuel);
    var _dist = abs(_afr - _ideal_min) / max(0.001, _ideal_min * 0.5);
    return lerp(1.45, 0.70, clamp(_dist, 0, 1));
};

// ignicao: ~28 graus e o ponto bom; longe disso perde torque
global.motor_ignicao_mult = function(_ignicao) {
    if (is_undefined(_ignicao)) return 1;
    if (_ignicao <= 0) return 1;   // sem leitura de ignicao: neutro, nao penaliza
    var _desvio = (_ignicao - 28) / 10;
    return clamp(1 - (abs(_desvio) * 0.045), 0.85, 1.04);
};

// desgaste: 70 = peca boa; abaixo perde torque ate -40%
global.motor_desgaste = function(_car) {
    if (is_undefined(_car)) return 1;
    var _campos = [""vpistao"", ""vbielas"", ""vcabecote"", ""vcomando"", ""vvira"", ""vbloco""];
    var _m = 1;
    for (var _i = 0; _i < array_length(_campos); _i++) {
        // 0 e desgaste total, nao ""campo ausente"": checar existencia antes
        if (variable_struct_exists(_car, _campos[_i])) {
            var _v = variable_struct_get(_car, _campos[_i]);
            if (!is_undefined(_v) && _v < 70) {
                _m *= clamp(1 - ((1 - (_v / 70)) * 0.06), 0.60, 1);
            }
        }
    }
    return _m;
};

// compositor: TODO efeito de peca/ajuste entra aqui, uma vez
global.motor_torque = function(_car, _rpm, _rpm_max, _afr) {
    var _m = global.motor_pecas(_car);
    _m *= global.phys_engine_fator(global.turbo, global.piston, global.litros);
    var _carb = (global.mod_dist_tipo(_car) == 1) ? max(1, global.motor_campo(_car, ""carburador"")) : 0;
    var _afr_eff = _afr;
    if (_carb <= 0) _afr_eff = global.motor_afr_inj(_rpm, _rpm_max, global.turbo, global.fuel, global.injecao);
    _m *= global.motor_mistura_mult(_afr_eff, _rpm, _rpm_max, global.turbo, global.fuel);
    _m *= global.motor_ignicao_mult(global.ignicao);
    _m *= global.motor_desgaste(_car);
    return _m;
};

// =====================================================================
//  FISICA DE TRACAO (modelo derivado do Rigs of Rods)
//  F = T * relacao * efic / raio ; limite = mu * N ; excedente = patinacao
// =====================================================================
global.phys_slip = 0;

global.phys_cfg = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return undefined;
    if (!variable_struct_exists(car, ""phys_cfg"") || is_undefined(car.phys_cfg)) {
        var _d = global.phys_def;
        car.phys_cfg = { raio: _d.raio, eficiencia: _d.eficiencia, mu: _d.mu, share: _d.share, embreagem: _d.embreagem, inercia: _d.inercia };
    }
    return car.phys_cfg;
};

// calibracao: reproduz o termo antigo (torque*hp)/(weight*750) em 1a marcha.
// weight esta em TONELADAS, entao a massa em kg entra x1000. Usa o hp CORRENTE
// (nao o maxhp) para casar exatamente com a formula antiga; o 0.90 e a
// eficiencia de referencia, entao abaixar a eficiencia realmente tira forca.
global.phys_k = function(_hp, _raio, _g1) {
    if (_g1 <= 0 || _raio <= 0 || _hp <= 0) return 0;
    return (_hp * _raio * 1000) / (750 * _g1 * 0.90);
};

global.phys_forca = function(_torque, _relacao, _raio, _efic) {
    if (_raio <= 0 || _relacao <= 0) return 0;
    return (_torque * _relacao * _efic) / _raio;
};

global.phys_limite = function(_massa_kg, _mu, _share) {
    return _massa_kg * 9.81 * _mu * _share;
};

// aderencia por composto e temperatura
global.phys_mu = function(_pneu, _temp) {
    var _base = 0.92;
    switch (_pneu) {
        case 1: _base = 1.05; break;
        case 2: _base = 1.25; break;
        case 3: _base = 0.78; break;
    }
    var _ideal = (_pneu == 3) ? 55 : 80;
    if (is_undefined(_temp) || _temp <= 0) return _base;
    var _desvio = abs(_temp - _ideal) / 60;
    return _base * clamp(1 - (_desvio * 0.35), 0.6, 1);
};

// termo base de aceleracao, na mesma escala do jogo; atualiza global.phys_slip
global.phys_base = function(_torque, _relacao, _raio, _massa_kg, _k, _efic, _mu, _share) {
    global.phys_slip = 0;
    if (_massa_kg <= 0 || _raio <= 0 || _relacao <= 0 || _k <= 0) return 0;
    var _f = global.phys_forca(_torque, _relacao, _raio, _efic);
    var _lim = global.phys_limite(_massa_kg, _mu, _share);
    if (_lim > 0 && _f > _lim) {
        global.phys_slip = clamp((_f - _lim) / _f, 0, 1);
        _f = _lim;
    }
    return (_f / _massa_kg) * _k;
};

// inercia: gira livre com a embreagem aberta (rpm += dt * T / I)
global.phys_rev = function(_torque, _response, _dt) {
    var _i = max(0.02, global.phys_cfg().inercia) / clamp(_response, 0.5, 2);
    return ((_torque / _i) * 9.549) * (_dt / 60);
};

// embreagem: capacidade e torque transmitido (opt-in via phys_cfg.embreagem)
global.phys_clutch_cap = function(_maxtorque, _g1) {
    return 1.5 * _maxtorque * abs(_g1);
};

global.phys_clutch = function(_torque_motor, _slip, _abertura, _cap) {
    if (_cap <= 0) return _torque_motor;
    var _t = clamp(_slip * _abertura * _cap, -_cap, _cap);
    _t *= 1 - exp(-abs(_slip));
    if (abs(_t) > abs(_torque_motor)) _t = _torque_motor;
    return _t;
};

// ---- carburador: circuitos de lenta, agulha e borboleta ----
global.mod_carb_gicle_ideal = function(_carb) {
    switch (_carb) {
        case 1: return 38;
        case 2: return 42;
        case 3: return 46;
        case 4: return 48;
        case 5: return 50;
        case 6: return 53;
        case 7: return 56;
        case 8: return 60;
    }
    return 50;
};

// configuracao propria do carro; padrao = gicle ideal do modelo (neutro)
global.mod_carb_cfg = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return undefined;
    if (!variable_struct_exists(car, ""carb_cfg"") || is_undefined(car.carb_cfg)) {
        var _gi = global.mod_carb_gicle_ideal(max(1, car.carburador));
        car.carb_cfg = {
            lenta: round(_gi * 0.35),
            agulha: round(_gi * 0.90),
            borboleta: 50
        };
    }
    return car.carb_cfg;
};

// desvio equivalente em pontos de gicle, somado ao desvio do gicle principal
global.mod_carb_desvio = function(_rpm_ratio, _cfg, _gicle_ideal) {
    if (is_undefined(_cfg)) return 0;
    var _il = round(_gicle_ideal * 0.35);
    var _ia = round(_gicle_ideal * 0.90);
    // janelas separadas: lenta domina abaixo de 0.08, agulha no meio, borboleta na saida
    var _w_l = 1 - clamp((_rpm_ratio - 0.08) / 0.12, 0, 1);
    var _w_a = clamp(1 - abs(_rpm_ratio - 0.45) / 0.30, 0, 1);
    var _w_b = 1 - clamp(_rpm_ratio / 0.12, 0, 1);
    var _d = 0;
    _d += (_cfg.lenta - _il) * _w_l * 0.80;
    _d += (_cfg.agulha - _ia) * _w_a * 0.60;
    _d += (_cfg.borboleta - 50) * _w_b * 0.40;
    return _d;
};

// sprite do distribuidor: caminho relativo no GMS resolve para a pasta de SAVE,
// por isso tentamos working_directory (pasta do jogo) antes do relativo.
global.mod_dist_spr = function() {
    var e = global.mod_dist_estado;
    if (e.spr >= 0) return e.spr;
    if (e.spr_try == 0) {
        e.spr_try = 1;
        var _p = working_directory + ""assets/Mod_assets/distribuidor.png"";
        if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        if (e.spr < 0) {
            _p = ""assets/Mod_assets/distribuidor.png"";
            if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        }
        if (e.spr < 0) {
            _p = ""Mod_assets/distribuidor.png"";
            if (file_exists(_p)) e.spr = sprite_add(_p, 1, false, false, 0, 0);
        }
    }
    return e.spr;
};

// icone quadrado (256x256) recortado da ilustracao
global.mod_dist_icon = function() {
    var e = global.mod_dist_estado;
    if (e.icon >= 0) return e.icon;
    if (e.icon_try == 0) {
        e.icon_try = 1;
        // este runner so resolve arquivos na raiz da pasta de save
        // (subpasta e caminho absoluto retornam -1)
        if (file_exists(""distribuidor_icon.png"")) e.icon = sprite_add(""distribuidor_icon.png"", 1, false, false, 0, 0);
        if (e.icon < 0 && file_exists(""distribuidor.png"")) e.icon = sprite_add(""distribuidor.png"", 1, false, false, 0, 0);
    }
    return e.icon;
};

// desenha centralizado em (_cx,_cy) com altura _h em pixels. Nunca passa sprite -1:
// se nenhuma arte carregar, cai no icone do carburador.
global.mod_dist_draw_icon = function(_cx, _cy, _h) {
    var _sp = global.mod_dist_icon();
    if (_sp < 0) _sp = global.mod_dist_spr();
    var _sw = 0;
    var _sh = 0;
    if (_sp >= 0) {
        _sw = sprite_get_width(_sp);
        _sh = sprite_get_height(_sp);
    }
    if (_sw > 0 && _sh > 0) {
        var _sc = _h / _sh;
        draw_sprite_ext(_sp, 0, _cx - (_sw * _sc) / 2, _cy - _h / 2, _sc, _sc, 0, c_white, 1);
    } else {
        // sem arte: marcador alinhado, sem sprite deslocado
        draw_set_color(make_color_rgb(70, 70, 90));
        draw_roundrect(_cx - (_h * 0.42), _cy - (_h * 0.42), _cx + (_h * 0.42), _cy + (_h * 0.42), false);
        draw_set_color(c_white);
        draw_set_halign(fa_center);
        draw_set_valign(fa_middle);
        draw_text(_cx, _cy, ""DIST"");
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
    }
};

// registra os dois itens na base do inventario (idempotente)
global.mod_dist_db = function() {
    if (!variable_global_exists(""item_db"")) return;
    if (!is_struct(global.item_db)) return;
    if (variable_struct_exists(global.item_db, ""distribuidor_original"")) return;
    variable_struct_set(global.item_db, ""distribuidor_original"", {
        id: ""distribuidor_original"", name: ""Distribuidor Original"", category: ""peca_externa"",
        value_label: ""Original"", value_default: 1, sprite_name: ""spr_venture_externa"", image_index: 17,
        campo: ""distribuidor"", vcampo: """", campo_val: 1
    });
    variable_struct_set(global.item_db, ""distribuidor_competicao"", {
        id: ""distribuidor_competicao"", name: ""Distribuidor Competicao"", category: ""peca_externa"",
        value_label: ""Competicao"", value_default: 2, sprite_name: ""spr_venture_externa"", image_index: 17,
        campo: ""distribuidor"", vcampo: """", campo_val: 2
    });
};

global.mod_dist_ui = function() {
    var e = global.mod_dist_estado;
    global.mod_dist_db();
    global.mod_dist_ativo = (global.mod_dist_modelo() > 0) ? 1 : 0;
    if (!global.mod_dist_pode()) { e.menu = 0; return; }
    global.mod_dist_input();
    if (e.menu == 0 && !global.mod_dist_ativo) return;
    if (variable_global_exists(""mod_dist_debug"") && global.mod_dist_debug == 1) {
        var _f = file_text_open_write(""mod_diag.txt"");
        file_text_write_string(_f, ""room="" + string(room) + "" player="" + string(instance_exists(obj_player)));
        file_text_write_string(_f, "" carros="" + string(array_length(global.car_data)) + "" cash="" + string(global.cash));
        file_text_write_string(_f, "" modelo="" + string(global.mod_dist_modelo()) + "" menu="" + string(e.menu) + "" ativo="" + string(global.mod_dist_ativo));
        file_text_write_string(_f, "" avanco="" + string(global.mod_dist_calcular()) + "" fator="" + string(global.mod_dist_fator()));
        file_text_close(_f);
    }
    var modelo = global.mod_dist_modelo();
    var car = global.mod_dist_car();
    draw_set_font(-1);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    if (e.menu == 0) {
        if (modelo <= 0) return;
        draw_set_alpha(0.55);
        draw_set_color(c_black);
        draw_rectangle(8, 8, 360, 46, false);
        draw_set_alpha(1);
        draw_set_color(c_white);
        draw_text(14, 12, global.mod_dist_nome[modelo] + ""  "" + string(round(e.avanco)) + "" graus"");
        draw_set_color(make_color_rgb(170, 200, 255));
        draw_text(14, 27, ""cent "" + string(round(e.cent)) + ""  vac "" + string(round(e.vac)) + ""  dwell "" + string(round(e.dwell_ef)) + ""  F6 menu"");
        draw_set_color(c_white);
        return;
    }
    if (e.menu == 2) { global.mod_dist_loja(); return; }
    if (e.menu == 3) { global.mod_carb_ui(); return; }
    var _x = 250;
    var _y = 40;
    var _w = 780;
    var _h = 620;
    draw_set_alpha(0.9);
    draw_set_color(c_black);
    draw_rectangle(_x, _y, _x + _w, _y + _h, false);
    draw_set_alpha(1);
    draw_set_color(make_color_rgb(200, 60, 60));
    draw_rectangle(_x, _y, _x + _w, _y + 34, false);
    draw_set_color(c_white);
    draw_text(_x + 12, _y + 8, ""DISTRIBUIDOR - "" + global.mod_dist_nome[modelo]);
    global.mod_dist_draw_icon(_x + 150, _y + 200, 300);
    if (modelo <= 0) {
        draw_set_color(make_color_rgb(255, 170, 120));
        draw_text(_x + 310, _y + 60, ""Este motor e injetado: distribuidor so serve em carburador."");
        draw_set_color(c_white);
        return;
    }
    var d = global.mod_dist_cfg();
    var lim = global.mod_dist_limites(modelo);
    var _labels = [""Avanco inicial (graus)"", ""Centrifugo total (graus)"", ""RPM de inicio"", ""RPM de fim"", ""Curva (0 linear, 1 progressiva, 2 rapida)"", ""Vacuo total (graus)"", ""Limiar de vacuo (bar)"", ""Dwell (graus)"", ""Desgaste (%)"", ""Ganho (%)""];
    var _vals = [d.avanco_inicial, d.cent_total, d.rpm_inicio, d.rpm_fim, d.curva, d.vac_total, d.vac_limiar, d.dwell, d.desgaste, d.ganho];
    var _i;
    for (_i = 0; _i < 10; _i++) {
        var _ry = _y + 50 + (_i * 30);
        if (_i == e.sel) {
            draw_set_color(make_color_rgb(60, 60, 90));
            draw_rectangle(_x + 300, _ry - 2, _x + _w - 14, _ry + 24, false);
        }
        draw_set_color(c_white);
        draw_text(_x + 310, _ry + 4, _labels[_i]);
        draw_set_color(make_color_rgb(255, 210, 120));
        draw_text(_x + _w - 120, _ry + 4, string(_vals[_i]));
    }
    // explicacao do parametro selecionado + efeito real de min/atual/max
    var _d1 = [""Avanco inicial: ponto base da ignicao."",
               ""Centrifugo total: avanco extra que sobe com o giro."",
               ""RPM de inicio: giro onde a curva comeca a entrar."",
               ""RPM de fim: giro onde o avanco satura."",
               ""Curva: formato da entrada das duas molas."",
               ""Vacuo total: avanco extra em carga leve."",
               ""Limiar de vacuo: MAP onde o vacuo comeca."",
               ""Dwell: tempo de carga da bobina."",
               ""Desgaste do platinado e do rotor."",
               ""Ganho: quanto do teto do modelo o carro usa.""];
    var _d2 = [""Baixo = fraco e lento. Alto = torque, mas detona."",
               ""Baixo = morre em alta. Alto = potencia, risco de knock."",
               ""Baixo = antecipa cedo. Alto = amarrado ate subir."",
               ""Baixo = trava cedo e perde alta. Alto = estica a curva."",
               ""0 linear, 1 progressiva, 2 rapida (mola leve)."",
               ""Baixo = consumo pior. Alto = economia, mas detona."",
               ""Baixo = entra cedo. Alto = so em vacuo forte."",
               ""Baixo = faisca fraca. Alto = forte, bobina esquenta."",
               ""Alto = faisca irregular e perda de potencia."",
               ""Baixo = pouco proveito. Alto = 100% do potencial.""];
    var _pmin = [lim[0], 0, 600, 1200, 0, 0, -0.8, lim[5], 0, 0];
    var _pmax = [lim[1], lim[2], 4000, lim[3], 2, lim[4], 1, lim[6], 100, lim[7]];
    var _bmin = global.mod_dist_sim(e.sel, _pmin[e.sel]);
    var _bmax = global.mod_dist_sim(e.sel, _pmax[e.sel]);
    draw_set_color(make_color_rgb(255, 235, 160));
    draw_text(_x + 310, _y + 352, _d1[e.sel]);
    draw_text(_x + 310, _y + 368, _d2[e.sel]);
    draw_set_color(make_color_rgb(255, 150, 150));
    draw_text(_x + 310, _y + 394, ""min "" + string(round(_bmin * 100)) + ""%"");
    draw_set_color(c_white);
    draw_text(_x + 380, _y + 394, ""atual "" + string(round(e.bonus * 100)) + ""%"");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 480, _y + 394, ""max "" + string(round(_bmax * 100)) + ""%"");
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 310, _y + 412, ""(bonus de potencia do motor neste giro/carga)"");
    draw_set_color(make_color_rgb(170, 200, 255));
    draw_text(_x + 310, _y + _h - 110, ""avanco "" + string(round(e.avanco)) + "" (ideal "" + string(round(e.ideal)) + "")  limite knock "" + string(round(global.mod_dist_knock_limite(car))) + ""  knock "" + string(round(e.knock)));
    draw_text(_x + 310, _y + _h - 92, ""bonus "" + string(round(e.bonus * 100)) + ""%   faisca "" + string(round(e.q * 100)) + ""% ("" + string(round(e.dwell_ms * 10) / 10) + ""ms)   dinheiro $"" + string(global.cash));
    draw_set_color(make_color_rgb(200, 200, 200));
    draw_text(_x + 310, _y + _h - 70, ""setas ajustam (limite do modelo)   TAB loja   F7 padrao   [3] voltar ao original"");
    draw_set_color(make_color_rgb(120, 255, 140));
    draw_text(_x + 310, _y + _h - 50, e.aviso);
    draw_set_color(c_white);
};

// ---- cambio: relacao final, limites de ajuste e teto de velocidade ----
global.cambio_rel_min = 2.60;
global.cambio_rel_max = 5.10;
global.cambio_rel_ref = 3.73;
global.cambio_passo = 0.01;
global.cambio_limite_pct = 0.70;
global.cambio_vmax = 500;
global.cambio_custo = 775;

global.cambio_cfg = function() {
    return { rel_min: global.cambio_rel_min, rel_max: global.cambio_rel_max,
             rel_ref: global.cambio_rel_ref, passo: global.cambio_passo,
             custo: global.cambio_custo };
};

global.cambio_rel_valor = function() {
    var car = global.mod_dist_car();
    if (is_undefined(car)) return global.cambio_rel_ref;
    if (!variable_struct_exists(car, ""relacao_final"")) return global.cambio_rel_ref;
    var _r = car.relacao_final;
    if (!is_real(_r) || _r <= 0) return global.cambio_rel_ref;
    return clamp(_r, global.cambio_rel_min, global.cambio_rel_max);
};

global.cambio_rel_mult = function() {
    if (global.cambio_rel_ref <= 0) return 1;
    return global.cambio_rel_valor() / global.cambio_rel_ref;
};

global.cambio_txt_rel = function() {
    switch (global.idioma) {
        case 1: return ""FINAL DRIVE"";
        case 2: return ""RELACION FINAL"";
        case 3: return ""RAPPORTO FINALE"";
        case 4: return ""RAPPORT FINAL"";
        case 5: return ""主减速比"";
        case 6: return ""ГЛАВНАЯ ПАРА"";
        case 7: return ""ファイナル"";
        default: return ""RELAÇÃO FINAL"";
    }
};

// ================= POWERTRAIN (base: Rigs of Rods Engine.cpp) =================
// RoR: torque = max_torque * curva(rpm) * throttle ; clutch = clamp(slip * forca, +/-limite) ;
// forca na roda = T * relacao / raio ; o corte de giro corta torque, nao a velocidade.
// O rpm continua vindo da roda (o jogo nao separa eixo de motor), mas a curva de torque
// passa a ser a do RoR - cheia embaixo, o que faz relacao longa ainda puxar.
global.pt_shift_t = 0;
global.pt_shift_time = 0.22;

global.pt_pico = function(_cmd) {
    switch (_cmd) {
        case 1: return 0.35;
        case 2: return 0.50;
        case 3: return 0.65;
        case 4: return 0.55;
        default: return 0.45;
    }
};

global.pt_curva = function(_x, _pico) {
    _x = clamp(_x, 0, 1);
    var _d;
    if (_x < _pico) _d = (_pico - _x) / max(0.05, _pico);
    else _d = (_x - _pico) / max(0.05, 1 - _pico);
    return max(0.45, 1 - power(clamp(_d, 0, 1), 1.6));
};

global.pt_torque = function(_base, _rpm, _redline, _cmd) {
    var _x = _rpm / max(1, _redline);
    var _alvo = global.maxtorque * (0.5 + 0.125 * global.litros) * global.pt_curva(_x, global.pt_pico(_cmd));
    return max(_base, _alvo);
};

global.pt_ratio = function(_gear) {
    if (_gear <= 0) return 0;
    if (!instance_exists(obj_player)) return 0;
    var _i = _gear - 1;
    if (_i >= array_length(obj_player.gear_ratios)) return 0;
    return obj_player.gear_ratios[_i];
};

global.pt_rpm_roda = function() {
    var _r = global.pt_ratio(global.gear);
    if (_r <= 0) return 0;
    return global.kmh * _r * 16.667;
};

global.pt_gear_limit = function() {
    var _r = global.pt_ratio(global.gear);
    if (_r <= 0) return 0;
    return ((global.car_data[global.vehicles_array].rpm / _r) * 60) / 1000;
};

global.pt_em_corte = function() {
    // usa o rpm implicado pela velocidade na marcha ATUAL (nao o global.rpm,
    // que pode estar velho depois de uma troca)
    var _red = global.car_data[global.vehicles_array].rpm;
    return (global.pt_rpm_roda() >= _red - 50) ? 1 : 0;
};

global.pt_step = function() {
    if (global.pt_shift_t > 0) {
        global.pt_shift_t = max(0, global.pt_shift_t - global.dt);
    }
};
// ==============================================================================

global.mod_dist_carregar = function() {
    // o ini define os PADROES de uma compra nova; cada carro guarda a sua config
    if (file_exists(""mod_distribuidor.ini"")) {
        ini_open(""mod_distribuidor.ini"");
        global.mod_dist_def.avanco_inicial = ini_read_real(""distribuidor"", ""avanco_inicial"", global.mod_dist_def.avanco_inicial);
        global.mod_dist_def.cent_total = ini_read_real(""distribuidor"", ""cent_total"", global.mod_dist_def.cent_total);
        global.mod_dist_def.rpm_inicio = ini_read_real(""distribuidor"", ""rpm_inicio"", global.mod_dist_def.rpm_inicio);
        global.mod_dist_def.rpm_fim = ini_read_real(""distribuidor"", ""rpm_fim"", global.mod_dist_def.rpm_fim);
        global.mod_dist_def.curva = ini_read_real(""distribuidor"", ""curva"", global.mod_dist_def.curva);
        global.mod_dist_def.vac_total = ini_read_real(""distribuidor"", ""vac_total"", global.mod_dist_def.vac_total);
        global.mod_dist_def.vac_limiar = ini_read_real(""distribuidor"", ""vac_limiar"", global.mod_dist_def.vac_limiar);
        global.mod_dist_def.dwell = ini_read_real(""distribuidor"", ""dwell"", global.mod_dist_def.dwell);
        global.mod_dist_def.ganho = ini_read_real(""distribuidor"", ""ganho"", global.mod_dist_def.ganho);
        global.phys_def.raio = ini_read_real(""fisica"", ""raio"", global.phys_def.raio);
        global.phys_def.eficiencia = ini_read_real(""fisica"", ""eficiencia"", global.phys_def.eficiencia);
        global.phys_def.mu = ini_read_real(""fisica"", ""mu"", global.phys_def.mu);
        global.phys_def.share = ini_read_real(""fisica"", ""share"", global.phys_def.share);
        global.phys_def.embreagem = ini_read_real(""fisica"", ""embreagem"", global.phys_def.embreagem);
        global.phys_def.inercia = ini_read_real(""fisica"", ""inercia"", global.phys_def.inercia);
        global.cambio_rel_min = ini_read_real(""cambio"", ""rel_min"", global.cambio_rel_min);
        global.cambio_rel_max = ini_read_real(""cambio"", ""rel_max"", global.cambio_rel_max);
        global.cambio_rel_ref = ini_read_real(""cambio"", ""rel_ref"", global.cambio_rel_ref);
        global.cambio_passo = ini_read_real(""cambio"", ""passo"", global.cambio_passo);
        global.cambio_limite_pct = ini_read_real(""cambio"", ""limite_pct"", global.cambio_limite_pct);
        global.cambio_vmax = ini_read_real(""cambio"", ""vmax"", global.cambio_vmax);
        global.cambio_custo = ini_read_real(""cambio"", ""custo"", global.cambio_custo);
        global.mod_rev = ini_read_real(""mod"", ""rev"", 0);
        global.dev_cash = ini_read_real(""dev"", ""cash"", 0);
        global.dev_cash_modo = ini_read_real(""dev"", ""cash_modo"", 0);
        ini_close();
    }
};

// le o ini uma vez no boot (antes so era definida, nunca chamada)
global.mod_dist_carregar();
");

g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    "if (global.mod_dist_ativo) { global.torque *= global.mod_dist_fator(); global.ignicao = global.mod_dist_estado.avanco; } var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    false);

// --- FASE 1: motor com consequencia ---
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "    var _camshaft_low = 1;",
    "    global.afr = global.motor_afr(global.car_data[global.vehicles_array], global.rpm, global.car_data[global.vehicles_array].rpm, global.turbo, global.fuel); var _camshaft_low = 1;",
    false);

// todo efeito de peca/ajuste entra no torque aqui, antes do hp
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.hp = clamp(global.hp, 0, global.maxhp * 1.2);",
    "global.torque *= global.motor_torque(global.car_data[global.vehicles_array], global.rpm, global.car_data[global.vehicles_array].rpm, global.afr);\nglobal.torque = global.pt_torque(global.torque, global.rpm, global.car_data[global.vehicles_array].rpm, global.comando); global.hp = clamp(global.pot_hp(global.torque, global.rpm, global.maxtorque, global.car_data[global.vehicles_array].rpm, global.maxhp), 0, global.maxhp * 1.2);",
    false);

// cabecote muda o conjunto de valvulas
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "switch (global.valvulas)",
    "switch (global.motor_valvulas(global.valvulas, global.motor_cabecote_player()))",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "switch (valvulas)", "switch (global.motor_valvulas(valvulas, cabecote))", false);

// multiplicadores tardios: agora tudo vem de motor_torque
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= mult_pistao;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= mult_pistao;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= mult_cmd;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= mult_cmd;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= mult_vira;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= mult_vira;", "// desgaste: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.hp *= (_carb_mult / 1.25);", "// mistura: motor_torque", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.torque *= (_carb_mult / 1.8);", "// mistura: motor_torque", false);

// teto de aceleracao deixa de punir quem instala peca
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_max_acc = 12;", "_max_acc = 21;", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_max_acc = 16;", "_max_acc = 25;", false);

g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.2);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.2);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.2);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.2);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.1);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.1);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "hp = clamp(hp, 0, maxhp * 1.1);",
    "hp = clamp(global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp), 0, maxhp * 1.1);",
    false);

// --- FASE 2: trem de forca ---
// 1) base do jogador: forca na roda com relacao real e limite de aderencia
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 750);",
    "var _pc = global.phys_cfg(); var _g1 = gear_ratios[0]; var _gr = gear_ratios[max(0, global.gear - 1)]; var _base_acceleration = global.phys_base(global.torque, _gr, _pc.raio, global.weight * 1000, global.phys_k(global.hp, _pc.raio, _g1), _pc.eficiencia, global.phys_mu(global.car_data[global.vehicles_array].pneu, global.heat_tire), _pc.share);",
    false);

// 2) re: mesma fisica, relacao da re
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _base_acceleration = (global.torque * global.hp) / (global.weight * 375);",
    "var _base_acceleration = global.phys_base(global.torque, gear_ratios[2], _pc.raio, global.weight * 1000, global.phys_k(global.hp, _pc.raio, gear_ratios[0]), _pc.eficiencia, global.phys_mu(global.car_data[global.vehicles_array].pneu, global.heat_tire), _pc.share);",
    false);

// 3) _gear_resistance sai da cadeia (relacao real ja entrou na base)
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "_acceleration = _base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    "_acceleration = _base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "_acceleration = (_base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty) / _vezes;",
    "_acceleration = (_base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty) / _vezes;",
    false);

// 4) inercia: gira livre em neutro ou com a embreagem pisada
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "global.rpm = clamp(global.rpm, global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm);",
    "global.rpm = clamp(global.rpm, global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm); if (global.engine == 1 && key_up && (global.gear == 0 || key_clutch)) { global.rpm = clamp(global.rpm + global.phys_rev(global.torque, _camshaft_response, global.dt), global.car_data[global.vehicles_array].minrpm, global.car_data[global.vehicles_array].rpm); }",
    false);

// 5) IAs: mesma base fisica
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "var _base_acceleration = (torque * hp) / (weight * 750);",
    "var _base_acceleration = global.phys_base(torque, gear_ratios[max(0, gear - 1)], 0.31, weight * 1000, global.phys_k(hp, 0.31, gear_ratios[0]), 0.90, global.phys_mu(pneu, temp), 1.0);",
    false);

// 6) IAs: fora o proxy de relacao
g.QueueFindReplace("gml_Object_obj_ia_car_Step_0",
    "_acceleration = _base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    "_acceleration = _base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_TOUGE_Step_0",
    "_acceleration = _base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    "_acceleration = _base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty;",
    false);
g.QueueFindReplace("gml_Object_obj_ia_opponent_Step_0",
    "_acceleration = (_base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    "_acceleration = (_base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    false);
g.QueueFindReplace("gml_Object_obj_ia_car_NPC_Step_0",
    "_acceleration = (_base_acceleration * _engine_multiplier * _gear_resistance * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    "_acceleration = (_base_acceleration * _engine_multiplier * _speed_resistance * _weight_penalty * _low_speed_penalty) + _others_aderencia;",
    false);

// --- FASE 3: fim das punicoes por tunar ---
// a fisica (mu * N + phys_slip) ja limita a tracao; esses descontos so sabotavam
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "        if (_mod_points > 5)", "        if (false)", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "        if (global.kmh < 80 && global.gear <= 3 && _mod_points > 5)",
    "        if (false)", false);

// compra falhava quando o dinheiro era exatamente o preco
g.QueueFindReplace("gml_Object_obj_ntc_dinamometro_confirm_Step_0",
    "if (global.cash > global.dinamometro_custo)",
    "if (global.cash >= global.dinamometro_custo)", false);

g.QueueAppend("gml_Object_obj_player_Step_0", @"
global.pt_step();
if (global.mod_dist_ativo) {
    global.mod_dist_dano();
}
");
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd;",
    "var _gicle2 = global.car_data[global.vehicles_array].gicle_2nd; var _mcarb = global.mod_carb_cfg();",
    false);
g.QueueFindReplace("gml_Object_obj_player_Step_0",
    "var _gicle_desvio = _gicle_ativo - _gicle_ideal;",
    "var _gicle_desvio = _gicle_ativo - _gicle_ideal + global.mod_carb_desvio(_rpm_ratio, _mcarb, _gicle_ideal);",
    false);

g.QueueAppend("gml_Object_obj_noguiin_Draw_64", @"
// aplica o ini ao carro salvo quando o arquivo muda (1x por segundo, no maximo)
global.mod_cfg_aplicar();
global.dev_cash_aplicar();
global.cash_guard();
// desenha so no Draw GUI: fica acima do passe CRT, sem piscar
global.mod_dist_ui();

// telemetria do cambio: marcha, giro, relacao total e topo teorico
if (instance_exists(obj_player) && global.kmh > 5) {
    var _g = global.gear;
    var _rr = global.pt_ratio(_g);
    var _topo = 0;
    if (_rr > 0) _topo = ((global.car_data[global.vehicles_array].rpm / _rr) * 60) / 1000;
    var _txt = ""G"" + string(_g) + ""/"" + string(obj_player.max_gears)
        + ""   "" + string(round(global.rpm)) + "" rpm""
        + ""   rel "" + string(round(_rr * 100) / 100)
        + ""   topo "" + string(round(_topo))
        + ""   "" + string(round(global.kmh)) + "" km/h"";
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    draw_set_alpha(0.8);
    draw_set_color(c_white);
    draw_text_transformed(12, 12, _txt, 0.6, 0.6, 0);
    draw_set_alpha(1);
}

// telemetria do noguintech (item comprado) + placa + FIPE
if (variable_global_exists(""car_data"") && is_array(global.car_data)
    && global.vehicles_array >= 0 && global.vehicles_array < array_length(global.car_data)) {
    var _ntc = global.car_data[global.vehicles_array];
    if (variable_struct_exists(_ntc, ""noguiintech"") && _ntc.noguiintech != 0) {
        var _tx = display_get_gui_width() - 250;
        var _ty = 90;
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        draw_set_alpha(0.85);
        draw_text_transformed(_tx, _ty, ""NOGUITECH v"" + string(_ntc.noguiintech), 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 16, ""torque "" + string(round(global.torque)) + "" Nm"", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 30, ""potencia "" + string(round(global.hp)) + "" hp"", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 44, ""AFR "" + string(round(global.afr * 10) / 10), 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 58, ""avanco "" + string(round(global.ignicao)) + "" graus"", 0.5, 0.5, 0);
        draw_text_transformed(_tx, _ty + 72, ""patinacao "" + string(round(global.phys_slip * 100)) + ""%"", 0.5, 0.5, 0);
        var _pl = ""SEM PLACA"";
        if (variable_struct_exists(_ntc, ""plate"")) _pl = string(_ntc.plate);
        draw_text_transformed(_tx, _ty + 86, ""placa "" + _pl, 0.5, 0.5, 0);
        if (variable_global_exists(""carro"")) {
            draw_text_transformed(_tx, _ty + 100, ""FIPE $"" + string(ValueCars()), 0.5, 0.5, 0);
        }
        if (global.mod_dist_estado.knock > 0) {
            draw_set_color(make_color_rgb(255, 90, 60));
            draw_text_transformed(_tx, _ty + 118, ""KNOCK"", 0.6, 0.6, 0);
            draw_set_color(c_white);
        }
        draw_set_alpha(1);
    }
}
");


// ---- CAMBIOS 7 E 8 MARCHAS ----
// razoes: cambio 8 = 7 marchas, cambio 9 = 8 marchas. max_gears sai do array_length,
// entao troca, automatico, HUD e IA (que copia as razoes do jogador) ja funcionam.
g.QueueFindReplace("gml_Object_obj_player_Create_0", "max_gears = array_length(gear_ratios);", "if (global.car_data[global.vehicles_array].cambio == 8)\n{\n    gear_ratios = [9.35, 6.2, 4.5, 3.5, 2.75, 2.15, 1.7];\n    original_ratios = [9.35, 6.2, 4.5, 3.5, 2.75, 2.15, 1.7];\n}\nelse if (global.car_data[global.vehicles_array].cambio == 9)\n{\n    gear_ratios = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];\n    original_ratios = [9.35, 6.4, 4.9, 3.8, 3.0, 2.4, 1.9, 1.55];\n}\nmax_gears = array_length(gear_ratios);", false);

// nomes: 7 terminadores cobrem as 5 tabelas (case "cambio" indexado por cambio-1,
// GetPartLevelsArray/2 e GetNamePartsVenture/2 indexados por nivel)
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6 marchas\"]", "\"GSD 6 marchas\", \"Manual 7 marchas\", \"Manual 8 marchas\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"6-speed GSD\"]", "\"6-speed GSD\", \"7-speed Manual\", \"8-speed Manual\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6 marce\"]", "\"GSD 6 marce\", \"Manuale 7 marce\", \"Manuale 8 marce\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6 rapports\"]", "\"GSD 6 rapports\", \"Manuelle 7 rapports\", \"Manuelle 8 rapports\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"6速自动\", \"GSD 6速\"]", "\"6速自动\", \"GSD 6速\", \"7速手动\", \"8速手动\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"6速オートマ\", \"GSD 6速\"]", "\"6速オートマ\", \"GSD 6速\", \"7速マニュアル\", \"8速マニュアル\"]", true);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "\"GSD 6-ст.\"]", "\"GSD 6-ст.\", \"7-ст. механика\", \"8-ст. механика\"]", true);

// precos: nivel 10 = 45.000, nivel 11 = 85.000 (as duas tabelas paralelas)
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "[250, 750, 1500, 6500, 10500, 17500, 16750, 23450, 32500, 65000]", "[250, 750, 1500, 6500, 10500, 17500, 16750, 23450, 32500, 65000, 45000, 85000]", false);
g.QueueFindReplace("gml_GlobalScript_GetNameParts", "[250, 750, 6500, 6500, 10500, 17500, 16750, 23450, 32500, 65000]", "[250, 750, 6500, 6500, 10500, 17500, 16750, 23450, 32500, 65000, 45000, 85000]", false);

// item no banco de inventario
g.QueueFindReplace("gml_GlobalScript_Inventory", "_r(\"gsd_6m\", \"GSD 6 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");", "_r(\"gsd_6m\", \"GSD 6 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");\n_r(\"manual_7m\", \"Manual 7 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");\n_r(\"manual_8m\", \"Manual 8 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");", false);

// instalacao pelo minigame (manual: at = 0)
g.QueueFindReplace("gml_GlobalScript_minigames_noguiintech", "case \"gsd_6m\":\n                    _cambio_val = 7;\n                    _car.at = 1;\n                    break;", "case \"gsd_6m\":\n                    _cambio_val = 7;\n                    _car.at = 1;\n                    break;\n                case \"manual_7m\":\n                    _cambio_val = 8;\n                    _car.at = 0;\n                    break;\n                case \"manual_8m\":\n                    _cambio_val = 9;\n                    _car.at = 0;\n                    break;", false);

// entrega: (categoria 6, nivel 10/11) -> id do item
g.QueueFindReplace("gml_GlobalScript_Delivery", "case 9:\n                    return \"gsd_6m\";", "case 9:\n                    return \"gsd_6m\";\n                case 10:\n                    return \"manual_7m\";\n                case 11:\n                    return \"manual_8m\";", false);

// caderno de marchas: 8 linhas nao podem invadir o texto de custo
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0",
    "var _start_y = _notebook_y - 80;",
    "var _start_y = _notebook_y - 80 - ((obj_player.max_gears > 6) ? 18 : 0);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0",
    "var _spacing = 35;",
    "var _spacing = (obj_player.max_gears > 6) ? 26 : 35;", false);

// ---- BUG 17: itens novos nao herdam use_func (_r nao define) ----
// o laco _cambio_keys e o unico lugar que atribui item_use_dinamometro aos cambios
g.QueueFindReplace("gml_GlobalScript_Inventory", "var _cambio_keys = [\"transmissao_original\", \"transmissao_forjada\", \"transmissao_desempenho\", \"manual_4m\", \"manual_5m\", \"manual_6m\", \"auto_4m\", \"auto_5m\", \"auto_6m\", \"gsd_6m\"];", "var _cambio_keys = [\"transmissao_original\", \"transmissao_forjada\", \"transmissao_desempenho\", \"manual_4m\", \"manual_5m\", \"manual_6m\", \"auto_4m\", \"auto_5m\", \"auto_6m\", \"gsd_6m\", \"manual_7m\", \"manual_8m\"];", false);

// ---- BUG 18: campo_val real (1..9) para _inventory_get_item_by_campo("cambio") ----
// sem campo_val ele compara posicao 0-based com cambio 1-based e nunca acha
g.QueueFindReplace("gml_GlobalScript_Inventory", "_r(\"manual_8m\", \"Manual 8 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");", "_r(\"manual_8m\", \"Manual 8 Marchas\", \"cambio\", \"un\", 1, \"spr_venture_externa\", 6, \"cambio\", \"\");\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_4m\")).campo_val = 1;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_5m\")).campo_val = 2;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_6m\")).campo_val = 3;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"auto_4m\")).campo_val = 4;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"auto_5m\")).campo_val = 5;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"auto_6m\")).campo_val = 6;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"gsd_6m\")).campo_val = 7;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_7m\")).campo_val = 8;\nstruct_get_from_hash(global.item_db, variable_get_hash(\"manual_8m\")).campo_val = 9;", false);

// ================= CAMBIO: relacao final + limites =================
g.QueueFindReplace("gml_GlobalScript_EstruturaSalvamento", "data.gears_mod = [];", "data.gears_mod = [];\ndata.relacao_final = 3.73;", false);
g.QueueFindReplace("gml_Object_obj_player_Create_0", "max_gears = array_length(gear_ratios);", "max_gears = array_length(gear_ratios);\nvar _rel_final = 1;\nif (variable_struct_exists(global.car_data[global.vehicles_array], \"relacao_final\")) {\n    var _rf_tmp = global.car_data[global.vehicles_array].relacao_final;\n    if (is_real(_rf_tmp) && _rf_tmp > 0) {\n        _rel_final = clamp(_rf_tmp, global.cambio_rel_min, global.cambio_rel_max) / global.cambio_rel_ref;\n    }\n}\nif (_rel_final != 1) {\n    for (var _ri = 0; _ri < max_gears; _ri++) {\n        gear_ratios[_ri] = gear_ratios[_ri] * _rel_final;\n        if (_ri < array_length(original_ratios)) {\n            original_ratios[_ri] = original_ratios[_ri] * _rel_final;\n        }\n    }\n}", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                if (arg0 == 0)\n                {\n                    return 1;\n                }\n                else if (arg0 >= 4)\n                {\n                    return 0.2;\n                }\n                else\n                {\n                    return 0.5;\n                }", "                return global.cambio_limite_pct;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                var _limite = get_limite_marcha(i, obj_player.max_gears);\n                var _valor_min = _gear_original[i] - _limite;\n                var _valor_max = _gear_original[i] + _limite;", "                var _limite = max(0.3, _gear_original[i] * get_limite_marcha(i, obj_player.max_gears));\n                var _valor_min = max(0.3, _gear_original[i] - _limite);\n                var _valor_max = _gear_original[i] + _limite;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                global.gear_selecionada = 0;\n                global.gear_editando = false;\n                global.gear_area_botoes = false;\n                global.gear_botao_selecionado = 0;\n            }", "                global.gear_selecionada = 0;\n                global.gear_editando = false;\n                global.gear_area_botoes = false;\n                global.gear_botao_selecionado = 0;\n                global.gear_temp_rel = global.cambio_rel_valor();\n                global.gear_rel_original = global.gear_temp_rel;\n            }", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "            var _custo_cambio = 775 * _marchas_modificadas;", "            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)\n            {\n                _marchas_modificadas++;\n            }\n            var _custo_cambio = global.cambio_custo * _marchas_modificadas;", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears - 1);", "global.gear_selecionada = min(global.gear_selecionada + 1, obj_player.max_gears);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "                    draw_set_halign(fa_center);\n                    draw_set_color(c_yellow);\n                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n                }\n            }\n            draw_set_halign(fa_left);\n            draw_set_color(c_white);\n            draw_text(_notebook_x - 290, _notebook_y + 150, txt_custo);", "                    draw_set_halign(fa_center);\n                    draw_set_color(c_yellow);\n                    draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n                }\n            }\n            var _rel_idx = obj_player.max_gears;\n            var _y_rel = _start_y + (obj_player.max_gears * _spacing) + 8;\n            var _rel_min = global.cambio_cfg().rel_min;\n            var _rel_max = global.cambio_cfg().rel_max;\n            var _rel_cor = (!global.gear_area_botoes && global.gear_selecionada == _rel_idx && !global.gear_editando) ? global.red : 16777215;\n            if (global.gear_selecionada == _rel_idx && global.gear_editando) _rel_cor = global.gold;\n            draw_set_halign(fa_left);\n            draw_set_color(_rel_cor);\n            draw_set_font(NotoSans);\n            draw_text(_marcha_x, _y_rel, global.cambio_txt_rel() + \":\");\n            draw_set_color(c_gray);\n            draw_text(_notebook_x - 180, _y_rel, string_format(global.gear_rel_original, 2, 2));\n            if (abs(global.gear_temp_rel - global.gear_rel_original) > 0.005)\n            {\n                draw_set_color(c_yellow);\n                draw_text(_notebook_x - 130, _y_rel, \">\");\n            }\n            draw_set_color((abs(global.gear_temp_rel - global.gear_rel_original) > 0.005) ? 65280 : 16777215);\n            draw_text(_notebook_x - 100, _y_rel, string_format(global.gear_temp_rel, 2, 2));\n            var _rbar_x = _notebook_x + 50;\n            draw_set_color(c_dkgray);\n            draw_roundrect(_rbar_x, _y_rel - 6, _rbar_x + 200, _y_rel + 6, false);\n            var _rpct = (global.gear_temp_rel - _rel_min) / (_rel_max - _rel_min);\n            draw_set_color(_rel_cor);\n            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, false);\n            draw_set_color(c_black);\n            draw_circle(_rbar_x + (_rpct * 200), _y_rel, 8, true);\n            var _rhover = point_in_rectangle(mouse_x, mouse_y, _rbar_x, _y_rel - 15, _rbar_x + 200, _y_rel + 15);\n            if (mouse_check_button_pressed(mb_left) && _rhover)\n            {\n                global.gear_selecionada = _rel_idx;\n                global.gear_editando = true;\n                global.gear_area_botoes = false;\n            }\n            if (global.gear_editando && global.gear_selecionada == _rel_idx && mouse_check_button(mb_left) && _rhover)\n            {\n                var _rn = _rel_min + (clamp((mouse_x - _rbar_x) / 200, 0, 1) * (_rel_max - _rel_min));\n                global.gear_temp_rel = round(_rn * 100) / 100;\n            }\n            if (global.gear_editando && global.gear_selecionada == _rel_idx)\n            {\n                if (keyboard_check_pressed(vk_left)) global.gear_temp_rel = max(global.gear_temp_rel - global.cambio_passo, _rel_min);\n                if (keyboard_check_pressed(vk_right)) global.gear_temp_rel = min(global.gear_temp_rel + global.cambio_passo, _rel_max);\n                draw_set_halign(fa_center);\n                draw_set_color(c_yellow);\n                draw_text_transformed(_notebook_x, _notebook_y + 180, txt_ajuste_info, 0.7, 0.7, 0);\n            }\n            draw_set_halign(fa_left);\n            draw_set_color(c_white);\n            draw_text(_notebook_x - 290, _notebook_y + 176, txt_custo);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "draw_text(_notebook_x + 80, _notebook_y + 150, \"$\" + string(floor(_custo_cambio)));", "draw_text(_notebook_x + 80, _notebook_y + 176, \"$\" + string(floor(_custo_cambio)));", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "draw_text_transformed(_notebook_x, _notebook_y + 110, t + string(global.car_data[global.vehicles_array].gears_mod), 1, 1, 0);", "draw_text_transformed(_notebook_x, _notebook_y + 200, t + string(global.car_data[global.vehicles_array].gears_mod) + \"  |  \" + global.cambio_txt_rel() + \": \" + string(global.car_data[global.vehicles_array].relacao_final), 1, 1, 0);", false);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i];", "global.car_data[global.vehicles_array].gears_mod[i] = global.gear_temp_array[i] * global.cambio_rel_ref / global.gear_rel_original;", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "obj_player.gear_ratios[i] = global.gear_temp_array[i];", "obj_player.gear_ratios[i] = (global.gear_temp_array[i] * global.cambio_rel_ref / global.gear_rel_original) * (global.gear_temp_rel / global.cambio_rel_ref);", true);
g.QueueFindReplace("gml_Object_obj_noguiin_Draw_0", "global.car_data[global.vehicles_array].cambio_mod = 1;", "global.car_data[global.vehicles_array].cambio_mod = 1;\nglobal.car_data[global.vehicles_array].relacao_final = global.gear_temp_rel;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (_gearlimit > 400)\n{\n    _gearlimit = 400;\n}\nif (global.kmh > 400)\n{\n    global.kmh = 400;\n}", "if (_gearlimit > global.cambio_vmax)\n{\n    _gearlimit = global.cambio_vmax;\n}\nif (global.kmh > global.cambio_vmax)\n{\n    global.kmh = global.cambio_vmax;\n}", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "global.kmh = clamp(global.kmh, 0, 400);", "global.kmh = clamp(global.kmh, 0, global.cambio_vmax);", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "else if (global.kmh >= 300)\n    {\n        _fator_velocidade = 3.2;\n    }", "else if (global.kmh >= 400)\n    {\n        _fator_velocidade = 3.2;\n    }", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "else if (global.kmh >= 250 && global.kmh < 300)", "else if (global.kmh >= 250 && global.kmh < 400)", false);
// ===================================================================

// ---- powertrain: corte de giro corta torque, corte de troca corta torque, sem parede ----
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_target_speed = min(global.kmh + _acceleration, _gearlimit);\n        if (global.kmh < _gearlimit)", "_acceleration *= (1 - global.pt_em_corte());\n        if (global.pt_shift_t > 0) _acceleration *= 0.15;\n        _target_speed = global.kmh + _acceleration;\n        if (global.kmh < global.cambio_vmax)", false);
// upshift nao recalculava _gearlimit: o rpm nao caia na troca (bug do jogo)
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (key_next && global.gear < max_gears)\n        {\n            global.gear += 1;", "if (key_next && global.gear < max_gears)\n        {\n            global.gear += 1;\n            _gearlimit = global.pt_gear_limit();", false);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "if (global.gear < max_gears)\n                {\n                    global.gear += 1;\n                    if (_gsd)", "if (global.gear < max_gears)\n                {\n                    global.gear += 1;\n                    _gearlimit = global.pt_gear_limit();\n                    if (_gsd)", false);
// limitadores artificiais que brigavam com a curva de torque
g.QueueFindReplace("gml_Object_obj_player_Step_0", "_base_acceleration *= _power_band_mult;", "_power_band_mult = 1;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "var _speed_resistance = lerp(1, 0.5, _smooth_ratio);", "var _speed_resistance = 1;", true);
g.QueueFindReplace("gml_Object_obj_player_Step_0", "play_audio(snd_gearchange);", "global.pt_shift_t = global.pt_shift_time; play_audio(snd_gearchange);", true);

g.QueueFindReplace("gml_GlobalScript_GetNameParts",
    @"        default:
            return [];
    }
}

function GetPressureTurbo(arg0)",
    @"        case 18:
            return (global.idioma == 0) ? [""Original"", ""Competicao""] : [""Original"", ""Competition""];
        default:
            return [];
    }
}

function GetPressureTurbo(arg0)",
    false);
g.QueueFindReplace("gml_GlobalScript_GetNameParts",
    @"                return @@array_get@@([""シングルボディ 2E"", ""シングルボディ 3E"", ""デュアルボディ VFD 446"", ""デュアルボディ Vever h34"", ""デュアルボディ Vever 40"", ""デュアルボディ Vever 44"", ""デュアルボディ Vever 48"", ""デュアルボディ Vever 55""], arg1);
            }
            break;
        default:",
    @"                return @@array_get@@([""シングルボディ 2E"", ""シングルボディ 3E"", ""デュアルボディ VFD 446"", ""デュアルボディ Vever h34"", ""デュアルボディ Vever 40"", ""デュアルボディ Vever 44"", ""デュアルボディ Vever 48"", ""デュアルボディ Vever 55""], arg1);
            }
            break;
        case 18:
            if (idioma == 0)
            {
                return @@array_get@@([""Distribuidor Original"", ""Distribuidor Competicao""], arg1);
            }
            return @@array_get@@([""Original Distributor"", ""Competition Distributor""], arg1);
            break;
        default:",
    false);
g.QueueFindReplace("gml_GlobalScript_GetNameParts",
    @"            return @@array_get@@([600, 1500, 950, 1300, 2600, 3200, 6600, 9600, 9600], arg1);",
    @"            return @@array_get@@([600, 1500, 950, 1300, 2600, 3200, 6600, 9600, 9600], arg1);
        case 18:
            return @@array_get@@([800, 12000], arg1);",
    false);
g.QueueFindReplace("gml_GlobalScript_GetNameParts",
    @"        case 17:
            return ""carburador"";
        default:
            return ""parte_invalida"";",
    @"        case 17:
            return ""carburador"";
        case 18:
            return ""distribuidor"";
        default:
            return ""parte_invalida"";",
    false);
g.QueueFindReplace("gml_GlobalScript_Delivery",
    @"                case 7:
                    return ""carburador_8"";
            }
    }
    return """";",
    @"                case 7:
                    return ""carburador_8"";
            }
        case 18:
            switch (arg1)
            {
                case 0:
                    return ""distribuidor_original"";
                case 1:
                    return ""distribuidor_competicao"";
            }
    }
    return """";",
    false);
g.QueueFindReplace("gml_Object_obj_pc_window_Draw_0",
    @"        var content_height_ext2 = 1120;",
    @"        var content_height_ext2 = 1260;",
    false);
g.QueueFindReplace("gml_Object_obj_pc_window_Draw_0",
    @"[win_x + 400, win_y + 820 + _venture_y]];",
    @"[win_x + 400, win_y + 820 + _venture_y], [win_x + 40, win_y + 950 + _venture_y]];",
    false);
g.QueueFindReplace("gml_Object_obj_pc_window_Draw_0",
    @"[win_x + 480, win_y + 978 + _venture_y]];",
    @"[win_x + 480, win_y + 978 + _venture_y], [win_x + 120, win_y + 1133 + _venture_y]];",
    false);
g.QueueFindReplace("gml_Object_obj_pc_window_Draw_0",
    @"                if (i == 15)
                {
                    draw_sprite_ext(spr_venture_wb, i, sx, sy, scale, scale, 0, -1, 1);
                }",
    @"                if (i == 15)
                {
                    draw_sprite_ext(spr_venture_wb, i, sx, sy, scale, scale, 0, -1, 1);
                }
                else if (i == 18)
                {
                    global.mod_dist_draw_icon(sx, sy + 10, 96);
                }",
    false);
g.QueueFindReplace("gml_Object_obj_pc_window_Draw_0",
    @"                            else
                            {
                                label_ext = ""キャブレター"";
                            }
                            break;
                    }",
    @"                            else
                            {
                                label_ext = ""キャブレター"";
                            }
                            break;
                        case 18:
                            label_ext = ""DISTRIBUIDOR"";
                            break;
                    }",
    false);
g.QueueFindReplace("gml_Object_obj_pc_window_Draw_0",
    @"                if (global.venture_pecas == 15)
                {
                    draw_sprite_ext(spr_venture_wb, 0, sx, sy, scale, scale, 0, -1, 1);
                }",
    @"                if (global.venture_pecas == 18)
                {
                    global.mod_dist_draw_icon(sx, sy + 10, 96);
                }
                else if (global.venture_pecas == 15)
                {
                    draw_sprite_ext(spr_venture_wb, 0, sx, sy, scale, scale, 0, -1, 1);
                }",
    false);
g.QueueFindReplace("gml_GlobalScript_Inventory",
    @"    variable_struct_set(@@array_get@@(global.car_data, idx), campo, _inventory_campo_value(arg1.item_id, campo));",
    @"    var _pv_inst = variable_struct_exists(data, ""campo_val"") ? data.campo_val : _inventory_campo_value(arg1.item_id, campo);
    variable_struct_set(@@array_get@@(global.car_data, idx), campo, _pv_inst);",
    false);

g.Import();
ScriptMessage("MARK distribuidor=v2 instalado");
"#;

/// CSX somente-leitura que confere o mod.
const MOD_CHECK_CSX: &str = r#"using System;
using UndertaleModLib;
using UndertaleModLib.Decompiler;

EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var st = Data.ToolInfo.DecompilerSettings;
string s1 = "", s2 = "", s3 = "";
try { s1 = GetDecompiledText(Data.Code.ByName("gml_RoomCC_Start_0_Create"), ctx, st); } catch { s1 = "ERR"; }
try { s2 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_player_Step_0"), ctx, st); } catch { s2 = "ERR"; }
try { s3 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_noguiin_Draw_64"), ctx, st); } catch { s3 = "ERR"; }
string s4 = "", s5 = "", s6 = "", s7 = "", s8 = "", s9 = "", s10 = "", s11 = "", s12 = "", s13 = "";
try { s4 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_pc_window_Draw_0"), ctx, st); } catch { s4 = "ERR"; }
try { s5 = GetDecompiledText(Data.Code.ByName("gml_GlobalScript_GetNameParts"), ctx, st); } catch { s5 = "ERR"; }
try { s6 = GetDecompiledText(Data.Code.ByName("gml_GlobalScript_Delivery"), ctx, st); } catch { s6 = "ERR"; }
try { s7 = GetDecompiledText(Data.Code.ByName("gml_GlobalScript_Inventory"), ctx, st); } catch { s7 = "ERR"; }
try { s8 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_player_Create_0"), ctx, st); } catch { s8 = "ERR"; }
try { s9 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_noguiin_Draw_0"), ctx, st); } catch { s9 = "ERR"; }
try { s10 = GetDecompiledText(Data.Code.ByName("gml_GlobalScript_minigames_noguiintech"), ctx, st); } catch { s10 = "ERR"; }
try { s11 = GetDecompiledText(Data.Code.ByName("gml_GlobalScript_EstruturaSalvamento"), ctx, st); } catch { s11 = "ERR"; }
try { s12 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_player_Step_0"), ctx, st); } catch { s12 = "ERR"; }
try { s13 = GetDecompiledText(Data.Code.ByName("gml_RoomCC_Start_0_Create"), ctx, st); } catch { s13 = "ERR"; }
string n1 = "", n2 = "", n3 = "", n4 = "";
try { n1 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_ia_car_Step_0"), ctx, st); } catch { n1 = "ERR"; }
try { n2 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_ia_car_TOUGE_Step_0"), ctx, st); } catch { n2 = "ERR"; }
try { n3 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_ia_opponent_Step_0"), ctx, st); } catch { n3 = "ERR"; }
try { n4 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_ia_car_NPC_Step_0"), ctx, st); } catch { n4 = "ERR"; }
ScriptMessage($"MARK versao={(s1.Contains("_vac_inhg") ? "v2.1-fisica" : "antiga")}");
ScriptMessage($"MARK item={(s1.Contains("mod_dist_comprar") ? "ok" : "ausente")}");
ScriptMessage($"MARK limites={(s1.Contains("mod_dist_limites") ? "ok" : "ausente")}");
ScriptMessage($"MARK dano={(s1.Contains("mod_dist_dano_mult") && s2.Contains("mod_dist_dano") ? "ok" : "ausente")}");
ScriptMessage($"MARK potencia={(s2.Contains("mod_dist_fator") ? "ok" : "ausente")}");
ScriptMessage($"MARK ui={(s3.Contains("mod_dist_ui") ? "ok" : "ausente")}");
ScriptMessage($"MARK loja={(s1.Contains("mod_dist_loja") && s1.Contains("mod_dist_db") ? "ok" : "ausente")}");
ScriptMessage($"MARK shop_tile={(s4.Contains("win_y + 950 + _venture_y") && s4.Contains("DISTRIBUIDOR") ? "ok" : "ausente")}");
ScriptMessage($"MARK shop_preco={(s5.Contains("[800, 12000]") ? "ok" : "ausente")}");
ScriptMessage($"MARK shop_entrega={(s6.Contains("distribuidor_competicao") ? "ok" : "ausente")}");
ScriptMessage($"MARK shop_instala={(s7.Contains("_pv_inst") ? "ok" : "ausente")}");
ScriptMessage($"MARK carb={(s1.Contains("mod_carb_desvio") && s1.Contains("mod_carb_cfg") ? "ok" : "ausente")}");
ScriptMessage($"MARK carb_hook={(s2.Contains("mod_carb_desvio") && s2.Contains("_mcarb") ? "ok" : "ausente")}");
ScriptMessage($"MARK torque={(s2.Contains("global.torque *= global.mod_dist_fator()") && s2.Contains("global.ignicao = global.mod_dist_estado.avanco") ? "ok" : "ausente")}");
ScriptMessage($"MARK potencia_real={(s1.Contains("global.pot_hp = function") && s2.Contains("global.pot_hp(global.torque") ? "ok" : "ausente")}");
ScriptMessage($"MARK pot_npc={(n1.Contains("global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp)") && n2.Contains("global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp)") && n3.Contains("global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp)") && n4.Contains("global.pot_hp(torque, rpm, maxtorque, max_rpm, maxhp)") ? "ok" : "ausente")}");
ScriptMessage($"MARK motor_upg={(s1.Contains("global.motor_pecas") && s1.Contains("global.motor_afr") && s1.Contains("global.motor_ignicao_mult") && s1.Contains("global.motor_torque") ? "ok" : "ausente")}");
ScriptMessage($"MARK motor_hook={(s2.Contains("global.torque *= global.motor_torque(") ? "ok" : "ausente")}");
ScriptMessage($"MARK cabecote={(s2.Contains("global.motor_valvulas(global.valvulas") && n1.Contains("global.motor_valvulas(valvulas") ? "ok" : "ausente")}");
ScriptMessage($"MARK maxacc={(s2.Contains("_max_acc = 12;") ? "ausente" : "ok")}");
ScriptMessage($"MARK phys_base={(s1.Contains("global.phys_base = function") && s2.Contains("global.phys_base(global.torque") ? "ok" : "ausente")}");
ScriptMessage($"MARK phys_gear={(s2.Contains("_base_acceleration * _engine_multiplier * _gear_resistance") ? "ausente" : "ok")}");
ScriptMessage($"MARK phys_npc={(n1.Contains("global.phys_base(torque") && n2.Contains("global.phys_base(torque") && n3.Contains("global.phys_base(torque") && n4.Contains("global.phys_base(torque") ? "ok" : "ausente")}");
ScriptMessage($"MARK phys_inercia={(s2.Contains("global.phys_rev(") ? "ok" : "ausente")}");
ScriptMessage($"MARK phys_cfg={(s1.Contains("global.phys_def") && s1.Contains("ini_read_real") && s1.Contains("car.phys_cfg = ") ? "ok" : "ausente")}");
ScriptMessage($"MARK cfg_auto={(s1.Contains("global.mod_cfg_aplicar = function") && s1.Contains("cfg_stamp") && s1.Contains("global.mod_rev") ? "ok" : "ausente")}");
ScriptMessage($"MARK carb_tipo={(s1.Contains("mod_dist_tipo") ? "ok" : "ausente")}");
ScriptMessage($"MARK carb_nocache={(s1.Contains("_carb > 0") ? "ausente" : "ok")}");
ScriptMessage($"MARK carb_cura={(s1.Contains("distribuidor == 0") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio7={(s8.Contains("cambio == 8") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio8={(s8.Contains("cambio == 9") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_ui={(s9.Contains("max_gears > 6") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_nomes={(s5.Contains("Manual 7 marchas") && s5.Contains("Manuale 8 marce") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_preco={(s5.Contains("45000") && s5.Contains("85000") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_item={(s7.Contains("manual_8m") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_dlv={(s6.Contains("manual_8m") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_mini={(s10.Contains("manual_8m") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_usefunc={(s7.Contains("\"manual_8m\"]") && s7.Contains("_cambio_keys") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_campoval={(s7.Contains("campo_val = 9") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_rel_schema={(s11.Contains("relacao_final = 3.73") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_rel_create={(s8.Contains("_rel_final") && s8.Contains("global.cambio_rel_min") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_rel_ui={(s9.Contains("gear_temp_rel") && s9.Contains("cambio_txt_rel") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_rel_save={(s9.Contains("relacao_final = global.gear_temp_rel") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_limite={(s9.Contains("global.cambio_limite_pct") ? "ok" : "ausente")}");
ScriptMessage($"MARK cambio_vmax={(s12.Contains("global.cambio_vmax") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_curva={(s13.Contains("global.pt_curva") && s13.Contains("global.pt_torque") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_torque={(s12.Contains("global.pt_torque") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_corte={(s12.Contains("global.pt_em_corte") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_shift={(s12.Contains("global.pt_shift_t = global.pt_shift_time") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_upshift={(s12.Contains("global.pt_gear_limit") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_band={(s12.Contains("_power_band_mult = 1") ? "ok" : "ausente")}");
ScriptMessage($"MARK pt_speedres={(s12.Contains("_speed_resistance = 1") ? "ok" : "ausente")}");
ScriptMessage($"MARK dev_cash={(s1.Contains("global.dev_cash_aplicar") && s1.Contains("global.cash_guard") ? "ok" : "ausente")}");
ScriptMessage($"MARK no_tracao_pen={(s2.Contains("if (_mod_points > 5)") ? "ausente" : "ok")}");
ScriptMessage($"MARK no_launch_pen={(s2.Contains("_launch_grip") ? "ausente" : "ok")}");
ScriptMessage($"MARK corte_giro={(s1.Contains("corte") && s1.Contains("rpm_max_carro") ? "ok" : "ausente")}");
ScriptMessage($"MARK knock_dano={(s1.Contains("e.knock > 0.6") ? "ok" : "ausente")}");
ScriptMessage($"MARK hud_ntc={(s3.Contains("NOGUITECH v") && s3.Contains("FIPE") ? "ok" : "ausente")}");
"#;

fn run_utmt(script: &str, input: &Path, output: Option<&Path>) -> Result<String, String> {
    let dir = utmt_dir().ok_or_else(|| {
        "UndertaleModCli nao encontrado (instale em ~/.local/share/mcml-fixer/utmt ou defina MCML_UTMT)".to_string()
    })?;
    let sp = std::env::temp_dir().join(format!("mcml_script_{}.csx", std::process::id()));
    fs::write(&sp, script).map_err(|e| format!("gravando script: {}", e))?;
    let mut c = Command::new(dir.join("UndertaleModCli"));
    c.current_dir(&dir).arg("load").arg(input).arg("-s").arg(&sp);
    if let Some(o) = output {
        c.arg("-o").arg(o).arg("-f");
    }
    let out = c.output().map_err(|e| format!("executando UTMT: {}", e))?;
    let _ = fs::remove_file(&sp);
    let mut txt = String::from_utf8_lossy(&out.stdout).to_string();
    txt.push_str(&String::from_utf8_lossy(&out.stderr));
    if !out.status.success() {
        let tail: Vec<&str> = txt.lines().rev().take(3).collect();
        return Err(format!("UTMT falhou: {}", tail.join(" | ")));
    }
    Ok(txt)
}

/// Aplica cursor (hotspot na ponta) + cursor proprio do jogo + GUI 1280x720.
/// Sempre parte do game.unx.orig, porque o UTMT recusa arquivo com GEN8 alterado.
fn data_patch(game_dir: &Path, log: &mut Vec<String>) {
    let assets = game_dir.join("assets");
    let cur = assets.join("game.unx");
    let pristine = assets.join("game.unx.orig");
    if utmt_dir().is_none() {
        log.push("ERRO: UndertaleModCli ausente (veja ~/.local/share/mcml-fixer/utmt).".into());
        return;
    }
    if !pristine.is_file() {
        log.push("ERRO: assets/game.unx.orig nao existe (necessario para o patch via UTMT).".into());
        return;
    }
    let tmp = assets.join("game.unx.new");
    log.push("aplicando patches no game.unx (UTMT, ~1 min)...".into());
    match run_utmt(PATCH_CSX, &pristine, Some(&tmp)) {
        Ok(out) => {
            for l in out.lines().filter(|l| l.trim_start().starts_with("MARK")) {
                log.push(format!("  {}", l.trim()));
            }
            let bkp = assets.join("game.unx.before-datafix.bak");
            if cur.is_file() && !bkp.exists() {
                let _ = fs::copy(&cur, &bkp);
            }
            match fs::copy(&tmp, &cur) {
                Ok(_) => {
                    let _ = fs::remove_file(&tmp);
                    log.push("game.unx atualizado.".into());
                    let bin = game_dir.join(BIN_NAME);
                    let (w, h) = read_native(&bin).unwrap_or((1280, 1024));
                    patch_native(game_dir, w, h, log);
                    let _ = fs::write(
                        marker_path(game_dir),
                        format!(
                            "cursor={},{}\ngui={}x{}\n",
                            CURSOR_TIP.0, CURSOR_TIP.1, GUI_W, GUI_H
                        ),
                    );
                }
                Err(e) => log.push(format!("ERRO instalando game.unx: {}", e)),
            }
        }
        Err(e) => log.push(format!("ERRO: {}", e)),
    }
}

/// Desfaz os patches de dados restaurando o `game.unx` anterior.
///
/// Prefere `.before-datafix.bak` (estado do usuario) e cai no `.orig`.
fn data_revert(game_dir: &Path, log: &mut Vec<String>) {
    let assets = game_dir.join("assets");
    let cur = assets.join("game.unx");
    let bkp = assets.join("game.unx.before-datafix.bak");
    let pristine = assets.join("game.unx.orig");
    let src = if bkp.is_file() {
        bkp
    } else if pristine.is_file() {
        pristine
    } else {
        log.push("ERRO: nenhum backup do game.unx encontrado.".into());
        return;
    };
    match fs::copy(&src, &cur) {
        Ok(_) => log.push(format!("game.unx restaurado de {}", src.display())),
        Err(e) => log.push(format!("ERRO restaurando: {}", e)),
    }
    let _ = fs::remove_file(marker_path(game_dir));
}

/// Marcador rapido (sem abrir o arquivo de 135 MB).
fn data_state(game_dir: &Path) -> Option<String> {
    let s = fs::read_to_string(marker_path(game_dir)).ok()?;
    Some(s.trim().replace('\n', " "))
}

/// Consulta de verdade dentro do game.unx (lento: carrega o arquivo inteiro).
/// Copia para um temporario com o GEN8 original, senao o UTMT recusa.
fn data_check(game_dir: &Path, log: &mut Vec<String>) {
    let cur = game_dir.join("assets/game.unx");
    if utmt_dir().is_none() {
        log.push("ERRO: UndertaleModCli ausente.".into());
        return;
    }
    let tmp = std::env::temp_dir().join("mcml_check.unx");
    if fs::copy(&cur, &tmp).is_err() {
        log.push("ERRO: nao consegui copiar o game.unx.".into());
        return;
    }
    if let Ok(mut b) = fs::read(&tmp) {
        if b.len() > 0x54 && &b[0..4] == b"FORM" && &b[8..12] == b"GEN8" {
            b[0x4c..0x50].copy_from_slice(&1280u32.to_le_bytes());
            b[0x50..0x54].copy_from_slice(&720u32.to_le_bytes());
            let _ = fs::write(&tmp, &b);
        }
    }
    log.push("verificando game.unx (UTMT, ~1 min)...".into());
    match run_utmt(CHECK_CSX, &tmp, None) {
        Ok(out) => {
            for l in out.lines().filter(|l| l.trim_start().starts_with("MARK")) {
                log.push(format!("  {}", l.trim()));
            }
        }
        Err(e) => log.push(format!("ERRO: {}", e)),
    }
    let _ = fs::remove_file(&tmp);
}

/// Inicia o jogo, opcionalmente sob gamescope, e devolve o PID.
///
/// `setsid` desacopla o jogo do terminal para que fechar o fixer nao derrube a
/// partida. `-w/-h` e' o canvas interno e `-W/-H` a saida no monitor.
fn launch(game_dir: &Path, out_w: u32, out_h: u32, use_gamescope: bool) -> Result<u32, String> {
    let bin = game_dir.join(BIN_NAME);
    if !bin.is_file() {
        return Err(format!("binario nao encontrado: {}", bin.display()));
    }
    let lp = log_path();
    if let Some(d) = lp.parent() {
        let _ = fs::create_dir_all(d);
    }
    let logf = fs::File::create(&lp).map_err(|e| e.to_string())?;
    let errf = logf.try_clone().map_err(|e| e.to_string())?;

    let mut cmd = Command::new("setsid");
    if use_gamescope {
        cmd.arg("gamescope")
            .arg("-w")
            .arg(CANVAS_W.to_string())
            .arg("-h")
            .arg(CANVAS_H.to_string())
            .arg("-W")
            .arg(out_w.to_string())
            .arg("-H")
            .arg(out_h.to_string())
            .arg("-f")
            .arg("--")
            .arg(&bin);
    } else {
        cmd.arg(&bin);
    }
    let child = cmd
        .current_dir(game_dir)
        .stdout(Stdio::from(logf))
        .stderr(Stdio::from(errf))
        .stdin(Stdio::null())
        .spawn()
        .map_err(|e| format!("falha ao iniciar: {}", e))?;
    Ok(child.id())
}


// ============================ MOD DISTRIBUIDOR ============================
// Instalador do mod de distribuidor, integrado como aba. Aplica um CSX que
// adiciona ignicao mecanica aos motores carburados (o jogo so tem modelo de
// ignicao para carros com ECU). O backup e' separado do backup do --data-fix.

const MOD_BACKUP: &str = "assets/game.unx.bak-mod-distribuidor";
const MOD_MARKER: &str = "assets/.mcml-mod-distribuidor";

/// Configuracao do distribuidor (chaves da secao [distribuidor] do ini).
#[derive(Clone)]
struct PhysOpts {
    raio: f32,
    eficiencia: f32,
    mu: f32,
    share: f32,
    embreagem: f32,
    inercia: f32,
}

impl Default for PhysOpts {
    fn default() -> Self {
        PhysOpts { raio: 0.31, eficiencia: 0.90, mu: 1.00, share: 1.00, embreagem: 0.0, inercia: 0.15 }
    }
}

impl PhysOpts {
    fn to_ini(&self) -> String {
        format!(
            "[fisica]\nraio={}\neficiencia={}\nmu={}\nshare={}\nembreagem={}\ninercia={}\n",
            self.raio, self.eficiencia, self.mu, self.share, self.embreagem, self.inercia
        )
    }

    fn set(&mut self, k: &str, v: &str) -> bool {
        let f = v.parse::<f32>().ok();
        match k {
            "raio" => self.raio = f.unwrap_or(self.raio),
            "eficiencia" => self.eficiencia = f.unwrap_or(self.eficiencia),
            "mu" => self.mu = f.unwrap_or(self.mu),
            "share" => self.share = f.unwrap_or(self.share),
            "embreagem" => self.embreagem = f.unwrap_or(self.embreagem),
            "inercia" => self.inercia = f.unwrap_or(self.inercia),
            _ => return false,
        }
        true
    }
}

#[derive(Clone)]
struct ModOpts {
    ativo: i32,
    avanco_inicial: i32,
    cent_total: i32,
    rpm_inicio: i32,
    rpm_fim: i32,
    curva: i32,
    vac_total: i32,
    vac_limiar: f32,
    dwell: i32,
    desgaste: i32,
    ganho: i32,
    phys: PhysOpts,
    dev_cash: i64,
    dev_cash_modo: i32,
}

impl Default for ModOpts {
    fn default() -> Self {
        ModOpts {
            ativo: 1, avanco_inicial: 10, cent_total: 22, rpm_inicio: 1000, rpm_fim: 3200,
            curva: 1, vac_total: 12, vac_limiar: 0.0, dwell: 45, desgaste: 0, ganho: 100,
            phys: PhysOpts::default(),
            dev_cash: 0,
            dev_cash_modo: 0,
        }
    }
}

impl ModOpts {
    fn to_ini(&self) -> String {
        format!(
            "[distribuidor]\nativo={}\navanco_inicial={}\ncent_total={}\nrpm_inicio={}\nrpm_fim={}\ncurva={}\nvac_total={}\nvac_limiar={}\ndwell={}\ndesgaste={}\nganho={}\n",
            self.ativo, self.avanco_inicial, self.cent_total, self.rpm_inicio, self.rpm_fim,
            self.curva, self.vac_total, self.vac_limiar, self.dwell, self.desgaste, self.ganho
        ) + &self.phys.to_ini()
          + &format!("[dev]\ncash={}\ncash_modo={}\n", self.dev_cash, self.dev_cash_modo)
    }

    fn set(&mut self, k: &str, v: &str) -> bool {
        let f = v.parse::<f32>().ok();
        match k {
            "ativo" => self.ativo = f.map(|x| x as i32).unwrap_or(self.ativo),
            "avanco_inicial" => self.avanco_inicial = f.map(|x| x as i32).unwrap_or(self.avanco_inicial),
            "cent_total" => self.cent_total = f.map(|x| x as i32).unwrap_or(self.cent_total),
            "rpm_inicio" => self.rpm_inicio = f.map(|x| x as i32).unwrap_or(self.rpm_inicio),
            "rpm_fim" => self.rpm_fim = f.map(|x| x as i32).unwrap_or(self.rpm_fim),
            "curva" => self.curva = f.map(|x| x as i32).unwrap_or(self.curva),
            "vac_total" => self.vac_total = f.map(|x| x as i32).unwrap_or(self.vac_total),
            "vac_limiar" => self.vac_limiar = f.unwrap_or(self.vac_limiar),
            "dwell" => self.dwell = f.map(|x| x as i32).unwrap_or(self.dwell),
            "desgaste" => self.desgaste = f.map(|x| x as i32).unwrap_or(self.desgaste),
            "ganho" => self.ganho = f.map(|x| x as i32).unwrap_or(self.ganho),
            _ => {
                if let Some(k2) = k.strip_prefix("fisica.") {
                    return self.phys.set(k2, v);
                }
                match k {
                    "cash" | "dev.cash" => {
                        self.dev_cash = v.parse::<i64>().unwrap_or(self.dev_cash);
                        return true;
                    }
                    "cash_modo" | "dev.cash_modo" => {
                        self.dev_cash_modo = f.map(|x| x as i32).unwrap_or(self.dev_cash_modo);
                        return true;
                    }
                    _ => {}
                }
                return false;
            }
        }
        true
    }
}

fn mod_ini_path() -> PathBuf {
    home().join(".config/My_Car_My_Life/mod_distribuidor.ini")
}

/// Le o ini existente (mantem o que o usuario ja configurou).
fn mod_read_ini() -> ModOpts {
    let mut o = ModOpts::default();
    let Ok(txt) = fs::read_to_string(mod_ini_path()) else { return o };
    let mut sec = String::new();
    for l in txt.lines() {
        let l = l.trim();
        if l.is_empty() || l.starts_with('#') {
            continue;
        }
        if l.starts_with('[') && l.ends_with(']') {
            sec = l[1..l.len() - 1].to_lowercase();
            continue;
        }
        if let Some((k, v)) = l.split_once('=') {
            let (k, v) = (k.trim(), v.trim());
            if sec == "fisica" {
                o.phys.set(k, v);
            } else {
                o.set(k, v);
            }
        }
    }
    o
}

fn mod_write_ini(o: &ModOpts) -> Result<PathBuf, String> {
    let p = mod_ini_path();
    if let Some(d) = p.parent() {
        fs::create_dir_all(d).map_err(|e| e.to_string())?;
    }
    let rev = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let txt = format!("{}[mod]\nrev={}\n", o.to_ini(), rev);
    let mut f = fs::File::create(&p).map_err(|e| e.to_string())?;
    f.write_all(txt.as_bytes()).map_err(|e| e.to_string())?;
    Ok(p)
}

/// Grava o GEN8 sem criar backup (usado no arquivo temporario do UTMT).
fn gen8_set_raw(data: &Path, w: u32, h: u32) -> Result<(), String> {
    let mut b = fs::read(data).map_err(|e| e.to_string())?;
    if b.len() < 0x54 || &b[0..4] != b"FORM" || &b[8..12] != b"GEN8" {
        return Err("layout inesperado".into());
    }
    b[0x4c..0x50].copy_from_slice(&w.to_le_bytes());
    b[0x50..0x54].copy_from_slice(&h.to_le_bytes());
    fs::write(data, &b).map_err(|e| e.to_string())
}

/// Estado do mod pelo marcador (rapido).
fn mod_state(game_dir: &Path) -> Option<String> {
    fs::read_to_string(game_dir.join(MOD_MARKER)).ok().map(|s| s.trim().replace('\n', " "))
}

/// Instala o mod: backup -> UTMT (GEN8 temporario) -> GEN8 de volta -> ini.
fn mod_install(game_dir: &Path, o: &ModOpts, log: &mut Vec<String>) {
    let assets = game_dir.join("assets");
    let cur = assets.join("game.unx");
    if !cur.is_file() {
        log.push("MOD: assets/game.unx nao existe.".into());
        return;
    }
    if utmt_dir().is_none() {
        log.push("MOD: UndertaleModCli ausente.".into());
        return;
    }
    if mod_state(game_dir).is_some() {
        log.push("MOD: ja instalado. Use --mod-uninstall antes de reinstalar.".into());
        return;
    }
    let bkp = game_dir.join(MOD_BACKUP);
    if !bkp.is_file() {
        match fs::copy(&cur, &bkp) {
            Ok(_) => log.push(format!("MOD backup: {}", bkp.display())),
            Err(e) => { log.push(format!("MOD ERRO backup: {}", e)); return; }
        }
    } else {
        log.push("MOD: backup ja existe (mantido)".into());
    }
    let gen8 = read_gen8(&cur).unwrap_or((1280, 1024));
    let tin = assets.join("game.unx.modtmp");
    let tout = assets.join("game.unx.modout");
    if fs::copy(&cur, &tin).is_err() {
        log.push("MOD ERRO: nao consegui copiar o game.unx.".into());
        return;
    }
    if let Err(e) = gen8_set_raw(&tin, 1280, 720) {
        log.push(format!("MOD ERRO preparando temporario: {}", e));
        return;
    }
    log.push("MOD: aplicando no game.unx (UTMT, ~1 min)...".into());
    match run_utmt(MOD_PATCH_CSX, &tin, Some(&tout)) {
        Ok(out) => {
            for l in out.lines().filter(|l| l.trim_start().starts_with("MARK")) {
                log.push(format!("  {}", l.trim()));
            }
            match fs::copy(&tout, &cur) {
                Ok(_) => {
                    let _ = fs::remove_file(&tin);
                    let _ = fs::remove_file(&tout);
                    match gen8_set_raw(&cur, gen8.0, gen8.1) {
                        Ok(_) => log.push(format!("MOD: GEN8 restaurado {}x{}", gen8.0, gen8.1)),
                        Err(e) => log.push(format!("MOD AVISO GEN8: {}", e)),
                    }
                    match mod_write_ini(o) {
                        Ok(p) => log.push(format!("MOD config: {}", p.display())),
                        Err(e) => log.push(format!("MOD AVISO config: {}", e)),
                    }
                    // copia a arte para a pasta de save: no GMS o caminho relativo
                    // de sprite_add resolve ali, nao na pasta do jogo
                    // a raiz do save: o runner nao enxerga subpastas nem caminhos absolutos
                    let dst_dir = home().join(".config/My_Car_My_Life");
                    for nome in ["distribuidor.png", "distribuidor_icon.png"] {
                        let src_png = game_dir.join("assets/Mod_assets").join(nome);
                        if src_png.is_file() {
                            match fs::copy(&src_png, dst_dir.join(nome)) {
                                Ok(_) => log.push(format!("MOD arte: {}", dst_dir.join(nome).display())),
                                Err(e) => log.push(format!("MOD AVISO arte {}: {}", nome, e)),
                            }
                        } else {
                            log.push(format!("MOD AVISO: assets/Mod_assets/{} nao encontrado", nome));
                        }
                    }
                    let _ = fs::write(game_dir.join(MOD_MARKER), o.to_ini());
                    log.push("MOD instalado. No carro carburado, F6 abre o menu.".into());
                }
                Err(e) => log.push(format!("MOD ERRO instalando: {}", e)),
            }
        }
        Err(e) => {
            let _ = fs::remove_file(&tin);
            log.push(format!("MOD ERRO: {}", e));
        }
    }
}

fn mod_uninstall(game_dir: &Path, log: &mut Vec<String>) {
    let cur = game_dir.join("assets/game.unx");
    let bkp = game_dir.join(MOD_BACKUP);
    if !bkp.is_file() {
        log.push("MOD: nenhum backup do mod encontrado.".into());
        return;
    }
    match fs::copy(&bkp, &cur) {
        Ok(_) => {
            let _ = fs::remove_file(game_dir.join(MOD_MARKER));
            log.push("MOD removido (game.unx restaurado do backup).".into());
        }
        Err(e) => log.push(format!("MOD ERRO restaurando: {}", e)),
    }
}

fn mod_check(game_dir: &Path, log: &mut Vec<String>) {
    if utmt_dir().is_none() {
        log.push("MOD: UndertaleModCli ausente.".into());
        return;
    }
    let tmp = std::env::temp_dir().join("mcml_mod_check.unx");
    if fs::copy(game_dir.join("assets/game.unx"), &tmp).is_err() {
        log.push("MOD ERRO: nao consegui copiar o game.unx.".into());
        return;
    }
    let _ = gen8_set_raw(&tmp, 1280, 720);
    log.push("MOD: verificando game.unx (UTMT, ~1 min)...".into());
    match run_utmt(MOD_CHECK_CSX, &tmp, None) {
        Ok(out) => {
            for l in out.lines().filter(|l| l.trim_start().starts_with("MARK")) {
                log.push(format!("  {}", l.trim()));
            }
        }
        Err(e) => log.push(format!("MOD ERRO: {}", e)),
    }
    let _ = fs::remove_file(&tmp);
}

// ---------------------------------------------------------------- GUI
/// Estado da GUI.
///
/// Tudo que a tela mostra fica em cache aqui e e' repopulado por
/// [`App::refresh`]: nenhum widget le disco no meio do frame.
struct App {
    game_dir: String,
    modes: Vec<String>,
    selected: Option<usize>,
    gen8: Option<(u32, u32)>,
    native: Option<(u32, u32)>,
    missing: Vec<String>,
    has_gamescope: bool,
    has_steamrt: bool,
    log: Vec<String>,
    out_w: u32,
    out_h: u32,
    nat_w: u32,
    nat_h: u32,
    use_gamescope: bool,
    tab: u8,
    mod_opts: ModOpts,
}

/// Construcao e atualizacao do estado da GUI.
impl App {
    fn new() -> Self {
        let game_dir = detect_game_dir()
            .map(|p| p.display().to_string())
            .unwrap_or_else(|| ".".into());
        let mut app = App {
            game_dir,
            modes: edid_modes(),
            selected: None,
            gen8: None,
            native: None,
            missing: Vec::new(),
            has_gamescope: which("gamescope"),
            has_steamrt: first_steam_runtime().is_some(),
            log: Vec::new(),
            out_w: CANVAS_W,
            out_h: CANVAS_H,
            nat_w: 1280,
            nat_h: 1024,
            use_gamescope: true,
            tab: 0,
            mod_opts: ModOpts::default(),
        };
        if let Some((w, h)) = load_cfg() {
            app.out_w = w;
            app.out_h = h;
        }
        if let Some(i) = app
            .modes
            .iter()
            .position(|m| m == &format!("{}x{}", app.out_w, app.out_h))
        {
            app.selected = Some(i);
        }
        app.refresh();
        app.log.push(format!(
            "Detectado: {} modos EDID, canvas nativo {}x{}.",
            app.modes.len(),
            CANVAS_W,
            CANVAS_H
        ));
        app
    }

    fn dir(&self) -> PathBuf {
        PathBuf::from(&self.game_dir)
    }

    fn refresh(&mut self) {
        let d = self.dir();
        let bin = d.join(BIN_NAME);
        self.gen8 = read_gen8(&d.join("assets/game.unx"));
        self.native = read_native(&bin);
        self.missing = if bin.is_file() {
            missing_libs(&bin)
        } else {
            vec!["binario ausente".into()]
        };
        self.has_gamescope = which("gamescope");
        self.has_steamrt = first_steam_runtime().is_some();
        if self.modes.is_empty() {
            self.modes = edid_modes();
        }
    }

    fn say(&mut self, s: impl Into<String>) {
        self.log.push(s.into());
    }

    /// Aba do mod: configuracao fisica do distribuidor + instalar/desinstalar.
    fn ui_mod(&mut self, ui: &mut egui::Ui) {
        ui.label(egui::RichText::new("Mod: Motor + Distribuidor + Carburador + Física").strong());
        ui.label(
            egui::RichText::new(
                "Ignicao mecanica para motores carburados: avanco inicial, curva centrifuga \
                 por rpm, avanco a vacuo, dwell do platinado, desgaste e detonacao.",
            )
            .small(),
        );
        ui.add_space(4.0);
        ui.horizontal(|ui| {
            ui.label("estado:");
            match mod_state(&self.dir()) {
                Some(s) => ui.colored_label(
                    egui::Color32::from_rgb(80, 200, 120),
                    format!("instalado ({})", s),
                ),
                None => ui.colored_label(egui::Color32::from_rgb(230, 160, 60), "nao instalado"),
            };
            ui.separator();
            ui.label("UTMT:");
            if utmt_dir().is_some() {
                ui.colored_label(egui::Color32::from_rgb(80, 200, 120), "ok");
            } else {
                ui.colored_label(egui::Color32::RED, "ausente");
            };
        });
        ui.label(
            egui::RichText::new(format!("ini: {}", mod_ini_path().display()))
                .monospace()
                .small(),
        );
        ui.separator();
        ui.label(egui::RichText::new("Configuracao do distribuidor").strong());
        ui.add(egui::Slider::new(&mut self.mod_opts.avanco_inicial, 0..=20).text("avanco inicial (graus)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.cent_total, 0..=30).text("centrifugo total (graus)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.rpm_inicio, 600..=4000).text("rpm de inicio do centrifugo"));
        ui.add(egui::Slider::new(&mut self.mod_opts.rpm_fim, 1200..=7000).text("rpm de fim (curva travada)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.curva, 0..=2).text("curva: 0 linear, 1 progressiva, 2 rapida"));
        ui.add(egui::Slider::new(&mut self.mod_opts.vac_total, 0..=20).text("vacuo total (graus)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.vac_limiar, -0.8..=1.0).text("limiar de vacuo (bar)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.dwell, 20..=70).text("dwell do platinado (graus)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.desgaste, 0..=100).text("desgaste do platinado (%)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.ganho, 0..=100).text("ganho de potencia (%)"));
        ui.separator();
        ui.label(egui::RichText::new("Física do trem de força").strong());
        ui.add(egui::Slider::new(&mut self.mod_opts.phys.raio, 0.20..=0.45).text("raio do pneu (m)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.phys.eficiencia, 0.60..=1.0).text("eficiência da transmissão"));
        ui.add(egui::Slider::new(&mut self.mod_opts.phys.mu, 0.5..=1.5).text("μ global (aderência)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.phys.share, 0.3..=1.0).text("peso no eixo motriz"));
        ui.add(egui::Slider::new(&mut self.mod_opts.phys.embreagem, 0.0..=1.0).text("embreagem (0 = desligada)"));
        ui.add(egui::Slider::new(&mut self.mod_opts.phys.inercia, 0.02..=0.60).text("inércia do motor (giro livre)"));
        ui.label(
            egui::RichText::new(
                "Motor: peças, cabeçote (válvulas), mistura, ignição e desgaste entram no torque. \
                 Força na roda = T·relação·eficiência/raio, limitada por μ·m·g; o excesso patina.",
            )
            .small(),
        );
        ui.separator();
        ui.horizontal(|ui| {
            if ui.button("Instalar mod").clicked() {
                let d = self.dir();
                self.say("MOD: aplicando (pode levar ~1 min)...");
                mod_install(&d, &self.mod_opts.clone(), &mut self.log);
                self.refresh();
            }
            if ui.button("Desinstalar").clicked() {
                let d = self.dir();
                mod_uninstall(&d, &mut self.log);
                self.refresh();
            }
            if ui.button("Verificar no arquivo").clicked() {
                let d = self.dir();
                mod_check(&d, &mut self.log);
            }
            if ui.button("Salvar config").clicked() {
                match mod_write_ini(&self.mod_opts) {
                    Ok(p) => self.log.push(format!("MOD config: {}", p.display())),
                    Err(e) => self.log.push(format!("MOD ERRO: {}", e)),
                }
            }
        });
        ui.separator();
        ui.label(egui::RichText::new("Log").strong());
        egui::ScrollArea::vertical()
            .max_height(150.0)
            .stick_to_bottom(true)
            .show(ui, |ui| {
                for l in &self.log {
                    ui.label(egui::RichText::new(l).monospace().small());
                }
            });
    }
}

/// `true` se o executavel existe em algum diretorio do `PATH`.
fn which(bin: &str) -> bool {
    std::env::var("PATH")
        .unwrap_or_default()
        .split(':')
        .any(|p| Path::new(p).join(bin).is_file())
}

/// Desenho da janela: uma secao por etapa de correcao, mais o log.
impl eframe::App for App {
    fn update(&mut self, ctx: &egui::Context, _f: &mut eframe::Frame) {
        egui::TopBottomPanel::top("top").show(ctx, |ui| {
            ui.add_space(4.0);
            ui.heading("My Car My Life - Fixer");
            ui.label("fix de launch (libs legadas) + fix de resolucao/input");
            ui.add_space(4.0);
        });

        egui::CentralPanel::default().show(ctx, |ui| {
            ui.horizontal(|ui| {
                if ui.selectable_label(self.tab == 0, "Fixes do jogo").clicked() {
                    self.tab = 0;
                }
                if ui.selectable_label(self.tab == 1, "Mod (motor/física)").clicked() {
                    self.tab = 1;
                }
            });
            ui.separator();
            if self.tab == 0 {
            // ---- pasta do jogo
            ui.group(|ui| {
                ui.horizontal(|ui| {
                    ui.label("Pasta do jogo:");
                    ui.add(
                        egui::TextEdit::singleline(&mut self.game_dir)
                            .desired_width(420.0),
                    );
                    if ui.button("Detectar").clicked() {
                        if let Some(p) = detect_game_dir() {
                            self.game_dir = p.display().to_string();
                            self.refresh();
                            self.say(format!("pasta: {}", self.game_dir));
                        } else {
                            self.say("nao encontrei a instalacao automaticamente.");
                        }
                    }
                    if ui.button("Atualizar status").clicked() {
                        self.refresh();
                        self.say("status atualizado.");
                    }
                });
            });

            ui.add_space(6.0);

            // ---- status
            ui.group(|ui| {
                ui.label(egui::RichText::new("Status").strong());
                let bin_ok = self.dir().join(BIN_NAME).is_file();
                ui.horizontal(|ui| {
                    ui.label("binario:");
                    if bin_ok {
                        ui.colored_label(egui::Color32::from_rgb(80, 200, 120), "encontrado");
                    } else {
                        ui.colored_label(egui::Color32::RED, "AUSENTE");
                    }
                });
                ui.horizontal(|ui| {
                    ui.label("libs de launch:");
                    if self.missing.is_empty() {
                        ui.colored_label(egui::Color32::from_rgb(80, 200, 120), "OK (ldd sem 'not found')");
                    } else {
                        ui.colored_label(
                            egui::Color32::from_rgb(230, 160, 60),
                            format!("{} pendencia(s)", self.missing.len()),
                        );
                    }
                });
                for m in &self.missing {
                    ui.label(egui::RichText::new(format!("   {}", m)).monospace().small());
                }
                ui.horizontal(|ui| {
                    ui.label("GEN8 default window:");
                    match self.gen8 {
                        Some((w, h)) => ui.label(format!("{}x{}", w, h)),
                        None => ui.colored_label(egui::Color32::RED, "game.unx nao reconhecido"),
                    };
                    ui.separator();
                    ui.label(format!("canvas nativo: {}x{}", CANVAS_W, CANVAS_H));
                });
                ui.horizontal(|ui| {
                    ui.label("device do runner:");
                    match self.native {
                        Some((w, h)) if (w, h) == (1280, 1024) => ui.colored_label(
                            egui::Color32::from_rgb(80, 200, 120),
                            format!("{}x{} (nativo fixo)", w, h),
                        ),
                        Some((w, h)) => ui.label(format!("{}x{}", w, h)),
                        None => ui.colored_label(egui::Color32::RED, "nao lido"),
                    };
                });
                ui.horizontal(|ui| {
                    ui.label("Steam Runtime:");
                    if self.has_steamrt {
                        ui.colored_label(egui::Color32::from_rgb(80, 200, 120), "disponivel");
                    } else {
                        ui.colored_label(egui::Color32::RED, "ausente (rode o Steam uma vez)");
                    }
                    ui.separator();
                    ui.label("gamescope:");
                    if self.has_gamescope {
                        ui.colored_label(egui::Color32::from_rgb(80, 200, 120), "disponivel");
                    } else {
                        ui.colored_label(egui::Color32::from_rgb(230, 160, 60), "ausente");
                    }
                });
            });

            ui.add_space(6.0);

            // ---- fix 1: libs
            ui.group(|ui| {
                ui.label(egui::RichText::new("1. Fix de launch (libs legadas)").strong());
                ui.label(
                    egui::RichText::new(
                        "Copia OpenSSL 1.0 / libcurl-gnutls do Steam Runtime para a pasta do jogo \
                         (o binario usa RPATH=$ORIGIN).",
                    )
                    .small(),
                );
                ui.horizontal(|ui| {
                    if ui.button("Corrigir libs").clicked() {
                        let d = self.dir();
                        fix_libs(&d, &mut self.log);
                        self.refresh();
                    }
                    if ui.button("Reverter libs").clicked() {
                        let d = self.dir();
                        revert_libs(&d, &mut self.log);
                        self.refresh();
                    }
                });
            });

            ui.add_space(6.0);

            // ---- fix 2: resolucao
            ui.group(|ui| {
                ui.label(egui::RichText::new("2. Fix de resolucao e input").strong());
                ui.label(
                    egui::RichText::new(
                        "Canvas fica em 1024x768 (janela == display == device, mouse 1:1) e o \
                         gamescope escala a saida para o modo EDID escolhido.",
                    )
                    .small(),
                );
                ui.horizontal(|ui| {
                    ui.label("Resolucao de saida (EDID):");
                    let cur = self
                        .selected
                        .and_then(|i| self.modes.get(i))
                        .cloned()
                        .unwrap_or_else(|| format!("{}x{}", self.out_w, self.out_h));
                    egui::ComboBox::from_id_source("modes")
                        .selected_text(cur)
                        .width(160.0)
                        .show_ui(ui, |ui| {
                            for (i, m) in self.modes.clone().iter().enumerate() {
                                let sel = self.selected == Some(i);
                                if ui.selectable_label(sel, m).clicked() {
                                    self.selected = Some(i);
                                    if let Some((w, h)) = parse_wh(m) {
                                        self.out_w = w;
                                        self.out_h = h;
                                    }
                                }
                            }
                        });
                    if ui.button("Salvar escolha").clicked() {
                        match save_cfg(self.out_w, self.out_h) {
                            Ok(_) => self.say(format!(
                                "salvo: {}x{} em {}",
                                self.out_w,
                                self.out_h,
                                cfg_path().display()
                            )),
                            Err(e) => self.say(format!("ERRO salvando: {}", e)),
                        }
                    }
                });
                ui.horizontal(|ui| {
                    ui.checkbox(&mut self.use_gamescope, "usar gamescope (recomendado)");
                    if ui.button("Jogar").clicked() {
                        match launch(&self.dir(), self.out_w, self.out_h, self.use_gamescope) {
                            Ok(pid) => self.say(format!(
                                "jogo iniciado (pid {}), saida {}x{}, log: {}",
                                pid,
                                self.out_w,
                                self.out_h,
                                log_path().display()
                            )),
                            Err(e) => self.say(format!("ERRO: {}", e)),
                        }
                    }
                });
            });

            ui.add_space(6.0);

            // ---- resolucao NATIVA fixa (patch no binario)
            ui.group(|ui| {
                ui.label(egui::RichText::new("3. Resolucao NATIVA fixa (patch no binario)").strong());
                ui.label(
                    egui::RichText::new(
                        "Grava o device do runner (.data, offset 0x9422f8) e o GEN8 do game.unx. \
                         Depois disso janela == device == display: sem escala e sem clique deslocado.",
                    )
                    .small(),
                );
                ui.horizontal(|ui| {
                    ui.label("largura");
                    ui.add(egui::DragValue::new(&mut self.nat_w).range(320..=7680));
                    ui.label("altura");
                    ui.add(egui::DragValue::new(&mut self.nat_h).range(200..=4320));
                    if ui.button("Aplicar nativo").clicked() {
                        let d = self.dir();
                        let (w, h) = (self.nat_w, self.nat_h);
                        patch_native(&d, w, h, &mut self.log);
                        self.refresh();
                    }
                    if ui.button("Reverter nativo").clicked() {
                        let d = self.dir();
                        revert_native(&d, &mut self.log);
                        self.refresh();
                    }
                });
            });

            ui.add_space(6.0);

            // ---- patches no game.unx (cursor + GUI do PC)
            ui.group(|ui| {
                ui.label(egui::RichText::new("4. Cursor e GUI do PC (patch no game.unx)").strong());
                ui.label(
                    egui::RichText::new(
                        "Hotspot do cursor na ponta da chave, cursor proprio do jogo ativado e GUI \
                         fixada em 1280x720. A barra de tarefas do PC testa clique em coordenadas de \
                         GUI mas desenha em coordenadas de room: sem isso os icones so respondem \
                         ~230 px acima do que aparece.",
                    )
                    .small(),
                );
                ui.horizontal(|ui| {
                    ui.label("estado:");
                    match data_state(&self.dir()) {
                        Some(s) => ui.colored_label(
                            egui::Color32::from_rgb(80, 200, 120),
                            format!("aplicado ({})", s),
                        ),
                        None => ui.colored_label(egui::Color32::from_rgb(230, 160, 60), "nao aplicado"),
                    };
                    ui.separator();
                    ui.label("UTMT:");
                    if utmt_dir().is_some() {
                        ui.colored_label(egui::Color32::from_rgb(80, 200, 120), "ok");
                    } else {
                        ui.colored_label(egui::Color32::RED, "ausente");
                    };
                });
                ui.horizontal(|ui| {
                    if ui.button("Aplicar cursor + GUI").clicked() {
                        let d = self.dir();
                        self.say("aplicando patches (pode levar ~1 min)...");
                        data_patch(&d, &mut self.log);
                        self.refresh();
                    }
                    if ui.button("Reverter").clicked() {
                        let d = self.dir();
                        data_revert(&d, &mut self.log);
                        self.refresh();
                    }
                    if ui.button("Verificar no arquivo").clicked() {
                        let d = self.dir();
                        data_check(&d, &mut self.log);
                    }
                });
            });

            ui.add_space(6.0);

            // ---- patch estatico
            ui.group(|ui| {
                ui.collapsing("Patch estatico do GEN8 (opcional)", |ui| {
                    ui.label(
                        egui::RichText::new(
                            "Muda o tamanho da janela que o runner cria no boot. \
                             Nao muda o canvas; nao conserta input sozinho.",
                        )
                        .small(),
                    );
                    ui.horizontal(|ui| {
                        ui.label("largura");
                        ui.add(egui::DragValue::new(&mut self.out_w).range(320..=7680));
                        ui.label("altura");
                        ui.add(egui::DragValue::new(&mut self.out_h).range(200..=4320));
                        if ui.button("Aplicar no game.unx").clicked() {
                            let data = self.dir().join("assets/game.unx");
                            patch_gen8(&data, self.out_w, self.out_h, &mut self.log);
                            self.refresh();
                        }
                        if ui.button("Restaurar original").clicked() {
                            let data = self.dir().join("assets/game.unx");
                            unpatch_gen8(&data, &mut self.log);
                            self.refresh();
                        }
                    });
                });
            });

            ui.add_space(6.0);
            ui.separator();
            ui.horizontal(|ui| {
                ui.label(egui::RichText::new("Log").strong());
                if ui.small_button("limpar").clicked() {
                    self.log.clear();
                }
            });
            egui::ScrollArea::vertical()
                .max_height(180.0)
                .stick_to_bottom(true)
                .show(ui, |ui| {
                    for l in &self.log {
                        ui.label(egui::RichText::new(l).monospace().small());
                    }
                });
            } else {
                self.ui_mod(ui);
            }
        });
    }
}

/// Sem argumentos abre a GUI; com argumentos delega para [`run_cli`].
fn main() -> eframe::Result<()> {
    // Modo CLI (headless): permite testar/automatizar sem GUI.
    let args: Vec<String> = std::env::args().skip(1).collect();
    if !args.is_empty() {
        run_cli(&args);
        return Ok(());
    }
    let opts = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([860.0, 780.0])
            .with_min_inner_size([680.0, 600.0])
            .with_title("My Car My Life - Fixer"),
        ..Default::default()
    };
    eframe::run_native(
        "mcml-fixer",
        opts,
        Box::new(|_cc| Ok(Box::new(App::new()) as Box<dyn eframe::App>)),
    )
}

/// Modo CLI (headless) - mesmas acoes da GUI.
///
/// Aceita `--dir PASTA` em qualquer posicao. Restaura `SIGPIPE` para que
/// `mcml-fixer --check | head` nao entre em panic (o Rust ignora SIGPIPE por
/// padrao e o `println!` seguinte falharia).
fn run_cli(args: &[String]) {
    // Sem isso o Rust entra em panic quando a saida e fechada cedo (ex: | head).
    #[cfg(unix)]
    unsafe {
        libc::signal(libc::SIGPIPE, libc::SIG_DFL);
    }
    let mut game_dir = detect_game_dir().unwrap_or_else(|| PathBuf::from("."));
    let mut rest: Vec<String> = Vec::new();
    let mut i = 0;
    while i < args.len() {
        if args[i] == "--dir" && i + 1 < args.len() {
            game_dir = PathBuf::from(&args[i + 1]);
            i += 2;
        } else {
            rest.push(args[i].clone());
            i += 1;
        }
    }
    let mut log: Vec<String> = Vec::new();
    let cmd = rest.first().map(|s| s.as_str()).unwrap_or("--check");
    let data = game_dir.join("assets/game.unx");
    let bin = game_dir.join(BIN_NAME);

    match cmd {
        "--check" => {
            println!("pasta     : {}", game_dir.display());
            println!("binario   : {}", if bin.is_file() { "ok" } else { "AUSENTE" });
            let miss = missing_libs(&bin);
            if miss.is_empty() {
                println!("libs      : OK");
            } else {
                println!("libs      : {} pendencia(s)", miss.len());
                for m in miss {
                    println!("   {}", m);
                }
            }
            match read_gen8(&data) {
                Some((w, h)) => println!("GEN8      : {}x{}", w, h),
                None => println!("GEN8      : nao reconhecido"),
            }
            match read_native(&bin) {
                Some((w, h)) => println!("device    : {}x{}", w, h),
                None => println!("device    : nao lido"),
            }
            println!("canvas    : {}x{}", CANVAS_W, CANVAS_H);
            match data_state(&game_dir) {
                Some(s) => println!("data      : patches aplicados ({})", s),
                None => println!("data      : nao aplicado (use --data-fix)"),
            }
            match mod_state(&game_dir) {
                Some(s) => println!("mod dist  : instalado ({})", s),
                None => println!("mod dist  : nao instalado (use --mod-install)"),
            }
            println!(
                "utmt      : {}",
                utmt_dir()
                    .map(|p| p.display().to_string())
                    .unwrap_or_else(|| "ausente (opcional, so para --data-fix)".into())
            );
            println!(
                "steamrt   : {}",
                first_steam_runtime()
                    .map(|p| p.display().to_string())
                    .unwrap_or_else(|| "ausente".into())
            );
            println!("gamescope : {}", if which("gamescope") { "ok" } else { "ausente" });
            let modes = edid_modes();
            println!("EDID      : {} modos -> {}", modes.len(), modes.join(", "));
        }
        "--fix-libs" => {
            fix_libs(&game_dir, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--revert-libs" => {
            revert_libs(&game_dir, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--patch-gen8" => {
            let wh = rest.get(1).and_then(|s| parse_wh(s));
            match wh {
                Some((w, h)) => {
                    patch_gen8(&data, w, h, &mut log);
                    for l in &log {
                        println!("{}", l);
                    }
                }
                None => println!("uso: --patch-gen8 LARGURAxALTURA"),
            }
        }
        "--revert-gen8" => {
            unpatch_gen8(&data, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--play" => {
            let (w, h) = rest
                .get(1)
                .and_then(|s| parse_wh(s))
                .or_else(load_cfg)
                .unwrap_or((CANVAS_W, CANVAS_H));
            let gs = !rest.iter().any(|a| a == "--no-gamescope");
            match launch(&game_dir, w, h, gs) {
                Ok(pid) => println!(
                    "iniciado pid {} saida {}x{} canvas {}x{} log {}",
                    pid,
                    w,
                    h,
                    CANVAS_W,
                    CANVAS_H,
                    log_path().display()
                ),
                Err(e) => println!("ERRO: {}", e),
            }
        }
        "--native-res" => {
            let wh = rest.get(1).and_then(|s| parse_wh(s));
            match wh {
                Some((w, h)) => {
                    patch_native(&game_dir, w, h, &mut log);
                    for l in &log {
                        println!("{}", l);
                    }
                }
                None => println!("uso: --native-res LARGURAxALTURA"),
            }
        }
        "--revert-native" => {
            revert_native(&game_dir, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--data-fix" => {
            data_patch(&game_dir, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--data-revert" => {
            data_revert(&game_dir, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--data-check" => {
            data_check(&game_dir, &mut log);
            for l in &log {
                println!("{}", l);
            }
        }
        "--mod-install" => {
            let mut o = mod_read_ini();
            for a in rest.iter().skip(1) {
                if let Some((k, v)) = a.split_once('=') {
                    if !o.set(k, v) {
                        log.push(format!("chave desconhecida: {}", k));
                    }
                }
            }
            mod_install(&game_dir, &o, &mut log);
        }
        "--mod-uninstall" => mod_uninstall(&game_dir, &mut log),
        "--mod-check" => mod_check(&game_dir, &mut log),
        "--mod-set" => {
            let mut o = mod_read_ini();
            for a in rest.iter().skip(1) {
                if let Some((k, v)) = a.split_once('=') {
                    if !o.set(k, v) {
                        log.push(format!("chave desconhecida: {}", k));
                    }
                }
            }
            match mod_write_ini(&o) {
                Ok(p) => log.push(format!("MOD config: {}", p.display())),
                Err(e) => log.push(format!("MOD ERRO: {}", e)),
            }
        }
        "--phys" => {
            let mut o = mod_read_ini();
            let mut n = 0;
            for a in rest.iter().skip(1) {
                if let Some((k, v)) = a.split_once('=') {
                    let k2 = k.strip_prefix("fisica.").unwrap_or(k);
                    if o.phys.set(k2, v) {
                        n += 1;
                    } else {
                        log.push(format!("chave de fisica desconhecida: {}", k));
                    }
                }
            }
            match mod_write_ini(&o) {
                Ok(p) => log.push(format!("FISICA: {} chave(s) -> {}", n, p.display())),
                Err(e) => log.push(format!("MOD ERRO: {}", e)),
            }
        }
        "--dev-cash" => {
            let mut o = mod_read_ini();
            let arg = rest.get(1).cloned().unwrap_or_default();
            if arg == "off" || arg == "0" || arg.is_empty() {
                o.dev_cash = 0;
            } else if let Ok(n) = arg.parse::<i64>() {
                o.dev_cash = n.max(0);
            } else {
                log.push("uso: --dev-cash N | --dev-cash off".into());
            }
            o.dev_cash_modo = if rest.iter().any(|a| a == "--dev-cash-exato") { 1 } else { 0 };
            match mod_write_ini(&o) {
                Ok(_) => log.push(format!(
                    "cash: {} (modo {})",
                    o.dev_cash,
                    if o.dev_cash_modo == 1 { "exato" } else { "piso" }
                )),
                Err(e) => log.push(format!("MOD ERRO: {}", e)),
            }
        }
        "--mod-status" => {
            log.push(format!(
                "MOD   : {}",
                mod_state(&game_dir).unwrap_or_else(|| "nao instalado".into())
            ));
            log.push(format!("MOD ini: {}", mod_ini_path().display()));
            let o = mod_read_ini();
            log.push(format!(
                "MOD dist: ativo={} avanco={} cent={} rpm={}..{} curva={} vac={}/{} dwell={} ganho={}",
                o.ativo, o.avanco_inicial, o.cent_total, o.rpm_inicio, o.rpm_fim,
                o.curva, o.vac_total, o.vac_limiar, o.dwell, o.ganho
            ));
            log.push(format!(
                "MOD fis : raio={} eficiencia={} mu={} share={} embreagem={} inercia={}",
                o.phys.raio, o.phys.eficiencia, o.phys.mu, o.phys.share,
                o.phys.embreagem, o.phys.inercia
            ));
        }
        "--help" | "-h" => {
            println!("mcml-fixer [--dir PASTA] <acao>");
            println!("  --check                    status (libs, GEN8, device, EDID)");
            println!("  --fix-libs                 copia libs do Steam Runtime");
            println!("  --revert-libs              remove as libs copiadas");
            println!("  --native-res LARGURAxALT   resolucao NATIVA fixa (binario + GEN8)");
            println!("  --revert-native            restaura binario e game.unx");
            println!("  --data-fix                 cursor (hotspot) + cursor do jogo + GUI 1280x720");
            println!("  --data-revert              restaura o game.unx de antes do --data-fix");
            println!("  --data-check               confere os patches dentro do game.unx (lento)");
            println!("  --patch-gen8 LARGURAxALT   patch so no GEN8 (com backup)");
            println!("  --revert-gen8              restaura game.unx");
            println!("  --play [LARGURAxALT]       inicia o jogo (gamescope)");
            println!("  --mod-install [k=v ...]    instala o mod (motor, distribuidor, carburador, fisica)");
            println!("  --mod-uninstall            remove o mod");
            println!("  --phys k=v ...             fisica: raio, eficiencia, mu, share, embreagem, inercia");
            println!("  --mod-check                confere o mod dentro do game.unx");
            println!("  --mod-set k=v ...          grava a configuracao do mod");
            println!("  --mod-status               estado do mod");
            println!("  sem argumentos             abre a GUI");
        }
        other => println!("acao desconhecida: {} (use --help)", other),
    }
    for l in &log {
        println!("{}", l);
    }
}
