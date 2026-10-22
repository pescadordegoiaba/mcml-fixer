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
try { s3 = GetDecompiledText(Data.Code.ByName("gml_Object_obj_crt_Draw_77"), ctx, st); } catch { s3 = "ERR"; }
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
            println!("  sem argumentos             abre a GUI");
        }
        other => println!("acao desconhecida: {} (use --help)", other),
    }
}
