mod engine;

use serde::Serialize;
use std::path::Path;
use tauri::Manager;

#[link(name = "shell32")]
extern "system" {
    fn ShellExecuteW(
        hwnd: *const core::ffi::c_void,
        lp_op: *const u16,
        lp_file: *const u16,
        lp_params: *const u16,
        lp_dir: *const u16,
        n_show: i32,
    ) -> isize;
}

fn shell_open(path: &str) -> Result<(), String> {
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    let wide: Vec<u16> = OsStr::new(path).encode_wide().chain(std::iter::once(0)).collect();
    let res = unsafe { ShellExecuteW(std::ptr::null(), std::ptr::null(), wide.as_ptr(), std::ptr::null(), std::ptr::null(), 1) };
    if res > 32 {
        Ok(())
    } else {
        Err(format!("ShellExecuteW returned {res}" ))
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct ProgramList {
    pub programs: Vec<engine::models::Program>,
    pub total: usize,
}

#[tauri::command]
async fn scan(app: tauri::AppHandle) -> Result<ProgramList, String> {
    let programs = tauri::async_runtime::spawn_blocking(move || {
        let mut list = engine::scan_all();
        let cache = app
            .path()
            .app_data_dir()
            .map_err(|e| e.to_string())?
            .join("icons-v3");
        let icons = engine::icons::attach_icons(&mut list, &cache);
        let with_icons = list.iter().filter(|p| p.has_icon).count();
        let c = |s: &str| list.iter().filter(|p| p.status == s).count();
        let line = format!(
            "wpi: scan: {} programs, {} with icons ({} new); installed={} shortcut={} broken={} store={}",
            list.len(),
            with_icons,
            icons,
            c("installed"),
            c("shortcut"),
            c("broken"),
            c("store")
        );
        println!("{line}");
        let _ = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(std::env::temp_dir().join("wpi-scan.log"))
            .map(|mut f| {
                use std::io::Write;
                let _ = f.write_all(line.as_bytes());
                let _ = f.write_all(b"\n");
            });
        Ok::<Vec<engine::models::Program>, String>(list)
    })
    .await
    .map_err(|e| e.to_string())?
    .map_err(|e| e.to_string())?;

    let total = programs.len();
    Ok(ProgramList { programs, total })
}

#[tauri::command]
fn launch_program(exe_path: String) -> Result<(), String> {
    let trimmed = exe_path.trim().trim_end_matches(|c| c == ',' || c == ' ');

    // Shell namespace paths (Store apps): open via ShellExecute on the URI.
    if trimmed.starts_with("shell:") {
        return shell_open(trimmed).map_err(|e| format!("Cannot launch {trimmed}: {e}"));
    }

    let mut candidates: Vec<String> = Vec::new();
    candidates.push(trimmed.to_string());

    // DisplayIcon values often end with ",0" (icon index). Strip it first.
    if let Some(stripped) = trimmed.split(',').next() {
        if stripped != trimmed {
            candidates.push(stripped.to_string());
        }
    }
    // Some records expose the folder but not the exe: probe the folder.
    if std::path::Path::new(trimmed).is_dir() {
        if let Ok(entries) = std::fs::read_dir(trimmed) {
            for e in entries.flatten() {
                if e.path().extension().map(|x| x == "exe").unwrap_or(false) {
                    candidates.push(e.path().to_string_lossy().into_owned());
                    break;
                }
            }
        }
    }

    for c in candidates {
        if std::path::Path::new(&c).exists() {
            let mut cmd = std::process::Command::new(&c);
            // CREATE_NO_WINDOW: launching a console program must not flash a
            // console window while the user is inside the GUI.
            use std::os::windows::process::CommandExt;
            cmd.creation_flags(0x0800_0000);
            cmd.spawn()
                .map(|_| ())
                .map_err(|e| format!("Cannot launch {c}: {e}"))?;
            return Ok(());
        }
    }

    Err(format!("No runnable file found for \"{exe_path}\""))
}

/// Reveal a program's file or folder in Windows Explorer, selecting the file
/// when it exists. Works with local paths (Open folder) and shell: namespaces
/// (cannot be revealed — reports a friendly error).
#[tauri::command]
fn open_file_location(path: String) -> Result<(), String> {
    // Registry DisplayIcon/InstallLocation values are often wrapped in quotes
    // ("C:\...\App.exe") or carry an icon index suffix (App.exe,0).
    let mut target = path.trim().trim_matches('"').trim().to_string();
    if let Some(stripped) = target.split(',').next() {
        let stripped = stripped.trim().trim_matches('"');
        if stripped != target {
            target = stripped.to_string();
        }
    }

    let p = std::path::Path::new(&target);
    if p.is_file() {
        // explorer /select,<file> opens the folder and highlights the file.
        std::process::Command::new("explorer.exe")
            .arg(format!("/select,{}", p.to_string_lossy()))
            .spawn()
            .map_err(|e| format!("Cannot open location for {target}: {e}"))?;
        return Ok(());
    }
    if p.is_dir() {
        // A trailing backslash in a quoted command line escapes the closing
        // quote, so explorer.exe would open the wrong folder (e.g. Documents).
        // Trim it, then open via ShellExecuteW which handles folders reliably.
        let dir = target.trim_end_matches(['/', '\\']);
        if dir.is_empty() {
            return Err(format!("Invalid folder path: \"{target}\""));
        }
        shell_open(dir).map_err(|e| format!("Cannot open location {dir}: {e}"))?;
        return Ok(());
    }

    // Maybe the location itself is a shell: URI (Store apps).
    if target.starts_with("shell:") {
        let mut cmd = std::process::Command::new("explorer.exe");
        cmd.arg(&target);
        cmd.spawn()
            .map_err(|e| format!("Cannot open location {target}: {e}"))?;
        return Ok(());
    }

    Err(format!("No file or folder found at \"{target}\""))
}

#[tauri::command]
fn get_icon(id: String, app: tauri::AppHandle) -> Result<Option<String>, String> {
    let dir = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("icons-v3");
    let bytes = std::fs::read(dir.join(format!("{id}.png"))).map_err(|e| e.to_string())?;
    use base64::Engine;
    Ok(Some(base64::engine::general_purpose::STANDARD.encode(&bytes)))
}

fn esc_html(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&#39;")
}

/// Escape Markdown values by wrapping them in backticks so Windows paths with
/// `_`, `*` and backslashes render as literal text.
fn esc_md(s: &str) -> String {
    format!("`{}`", s.replace('`', "'"))
}

fn report_path_of(p: &engine::models::Program) -> String {
    if let Some(icon) = &p.display_icon {
        let base = icon.split(',').next().unwrap_or(icon).trim();
        if !base.is_empty() {
            return base.to_string();
        }
    }
    p.install_location.clone().unwrap_or_default()
}

/// Collect the exact detail rows shown in the HTML report so the Markdown and
/// print/PDF variants render the same fields: Path, Publisher, Version,
/// Status, Source, Architecture, Location, Executable, Installed.
fn program_rows(p: &engine::models::Program) -> Vec<(&'static str, String)> {
    let mut rows: Vec<(&'static str, String)> = Vec::new();
    let path = report_path_of(p);
    rows.push(("Path", if path.is_empty() { "—".to_string() } else { path }));
    if let Some(v) = &p.publisher {
        if !v.is_empty() {
            rows.push(("Publisher", v.clone()));
        }
    }
    if let Some(v) = &p.version {
        if !v.is_empty() {
            rows.push(("Version", v.clone()));
        }
    }
    if !p.status.is_empty() {
        rows.push(("Status", p.status.clone()));
    }
    if !p.source.is_empty() {
        rows.push(("Source", p.source.clone()));
    }
    if let Some(v) = &p.architecture {
        if !v.is_empty() {
            rows.push(("Architecture", v.clone()));
        }
    }
    if let Some(v) = &p.install_location {
        if !v.is_empty() {
            rows.push(("Location", v.clone()));
        }
    }
    if let Some(v) = &p.display_icon {
        if !v.is_empty() && p.install_location.as_ref() != Some(v) {
            rows.push(("Executable", v.clone()));
        }
    }
    if let Some(v) = &p.install_date {
        if !v.is_empty() {
            rows.push(("Installed", v.clone()));
        }
    }
    rows
}

/// Build a colorful, self-contained HTML report. Returns (html, icons_used).
fn build_report_html(programs: &[engine::models::Program], cache: &Path, now: &str) -> (String, usize) {
    let mut rows = String::new();
    let mut with_icons = 0usize;
    let total = programs.len();
    for p in programs {
        let name = if p.name.trim().is_empty() {
            "Unknown".to_string()
        } else {
            p.name.clone()
        };
        let marker = if p.has_icon {
            let file = cache.join(format!("{}.png", p.id));
            if let Ok(bytes) = std::fs::read(file) {
                use base64::Engine;
                let b64 = base64::engine::general_purpose::STANDARD.encode(&bytes);
                with_icons += 1;
                format!(r#"<img class="app-icon" src="data:image/png;base64,{b64}" alt="">"#)
            } else {
                r#"<div class="chip">?</div>"#.to_string()
            }
        } else {
            r#"<div class="chip">?</div>"#.to_string()
        };

        let status = esc_html(&p.status);
        let status_badge = if status.is_empty() || status == "installed" {
            String::new()
        } else {
            format!(r#"<span class="st st-{status}">{status}</span>"#)
        };

        let mut cells = Vec::new();
        for (k, v) in program_rows(p) {
            let v_disp = if v.is_empty() { "—".to_string() } else { esc_html(&v) };
            cells.push(format!(
                r#"<div class="row"><span class="k">{k}</span><span class="v" dir="auto">{v_disp}</span></div>"#
            ));
        }

        let rows_html = cells.join("\n        ");

        rows.push_str(&format!(
            r#"      <div class="item">
        {marker}
        <div class="info">
          <div class="name" dir="auto">{name}{status_badge}</div>
          {rows_html}
        </div>
      </div>
"#,
            name = esc_html(&name),
            status_badge = status_badge,
            rows_html = rows_html,
        ));
    }

    let html = format!(
        r#"<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Windows Program Inventory — Report</title>
<style>
  :root {{
    --bg: #f3f4fb; --card: #ffffff; --ink: #1e2433; --muted: #7a8194;
    --accent: linear-gradient(135deg, #6366f1, #8b5cf6); --border: #e5e8f2;
    --corn: 16px;
  }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; background: var(--bg); color: var(--ink);
    font-family: "Segoe UI", system-ui, sans-serif; }}
  .head {{
    background: var(--accent); color: #fff; padding: 26px 34px;
    display: flex; align-items: baseline; gap: 14px; flex-wrap: wrap;
  }}
  .head h1 {{ margin: 0; font-size: 22px; letter-spacing: .3px; }}
  .head .meta {{ opacity: .85; font-size: 13px; margin-left: auto; }}
  .head .count {{ font-size: 13px; padding: 3px 12px; border-radius: 999px;
    background: rgba(255,255,255,.18); }}
  .wrap {{ max-width: 920px; margin: 22px auto; padding: 0 18px; }}
  .item {{
    display: flex; align-items: flex-start; gap: 16px; padding: 14px 16px;
    background: var(--card); border: 1px solid var(--border);
    border-radius: var(--corn); margin-bottom: 10px;
    box-shadow: 0 2px 10px rgba(30,36,51,.05);
    break-inside: avoid;
  }}
  .app-icon {{
    width: 44px; height: 44px; object-fit: contain; border-radius: 10px;
    background: #f0f2fa; border: 1px solid var(--border); padding: 6px; flex: none;
  }}
  .chip {{
    width: 44px; height: 44px; border-radius: 10px; flex: none;
    display: grid; place-items: center; color: #fff; font-weight: 700; font-size: 20px;
    background: linear-gradient(135deg, #94a3b8, #cbd5e1);
  }}
  .info {{ min-width: 0; flex: 1; }}
  .name {{ font-size: 14.5px; font-weight: 600; }}
  .st {{
    font-size: 10.5px; text-transform: uppercase; letter-spacing: .4px;
    padding: 2px 8px; border-radius: 999px; margin-left: 8px; font-weight: 700;
    background: #4f46e5; color: #fff; vertical-align: 2px;
  }}
  .st-store {{ background: #0ea5e9; }}
  .st-shortcut {{ background: #10b981; }}
  .st-broken {{ background: #ef4444; }}
  .row {{ display: grid; grid-template-columns: 96px 1fr; gap: 10px;
    margin-top: 5px; font-size: 12px; }}
  .row .k {{ color: var(--muted); font-weight: 600; }}
  .row .v {{ color: var(--ink); word-break: break-all; overflow-wrap: anywhere; }}
  .foot {{ text-align: center; color: var(--muted); font-size: 12px;
    padding: 14px 0 30px; }}
  @media print {{
    .head {{ -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
    body {{ background: #fff; }}
    .item {{ box-shadow: none; }}
  }}
</style>
</head>
<body>
  <header class="head">
    <h1>Windows Program Inventory</h1>
    <span class="count">{total} programs · {with_icons} icons</span>
    <span class="meta">{now}</span>
  </header>
  <main class="wrap">
      {rows}
  </main>
  <footer class="foot">Generated by Windows Program Inventory</footer>
</body>
</html>"#,
        total = total,
        with_icons = with_icons,
        now = now,
        rows = rows,
    );
    (html, with_icons)
}

/// Generate a colorful, self-contained HTML report (inline icons + name +
/// path per program) and open it in the default browser.
#[tauri::command]
async fn export_report(app: tauri::AppHandle) -> Result<String, String> {
    let cache = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("icons-v3");

    let cache_for_block = cache.clone();
    let programs = tauri::async_runtime::spawn_blocking(move || {
        let mut list = engine::scan_all();
        engine::icons::attach_icons(&mut list, &cache_for_block);
        list
    })
    .await
    .map_err(|e| e.to_string())?;

    let now = chrono::Local::now().format("%Y-%m-%d %H:%M").to_string();
    let (html, _icons) = build_report_html(&programs, &cache, &now);

    // Prefer Documents; fall back to home, then the guaranteed app-data dir.
    let dir = app
        .path()
        .document_dir()
        .or_else(|_| app.path().home_dir())
        .or_else(|_| app.path().app_data_dir())
        .map_err(|e| e.to_string())?;
    let file = dir.join("Windows Programs Report.html");
    if let Err(e) = std::fs::write(&file, &html) {
        // Last-resort location that always exists on Windows.
        let fallback = std::env::temp_dir().join("Windows Programs Report.html");
        std::fs::write(&fallback, &html).map_err(|f| format!("report write error: {e} / {f}"))?;
        let path_str = fallback.to_string_lossy().into_owned();
        shell_open(&path_str)?;
        return Ok(path_str);
    }
    let path_str = file.to_string_lossy().into_owned();
    shell_open(&path_str)?;
    Ok(path_str)
}

/// Generate Markdown report text from a program list. Each program is a flat
/// detail block with exactly the same fields as the HTML report.
fn build_report_md(programs: &[engine::models::Program]) -> String {
    let mut md = String::with_capacity(programs.len() * 320);
    md.push_str("# Windows Program Inventory\n\n");
    let now = chrono::Local::now().format("%Y-%m-%d %H:%M");
    md.push_str(&format!("> Generated: {now} · {total} programs\n\n", total = programs.len()));

    for (i, p) in programs.iter().enumerate() {
        let name = if p.name.trim().is_empty() {
            "Unknown".to_string()
        } else {
            p.name.clone()
        };
        let badge = if p.status.is_empty() || p.status == "installed" {
            String::new()
        } else {
            format!(" `{}`", p.status)
        };
        md.push_str(&format!("## {}. {}{}\n\n", i + 1, name, badge));

        for (k, v) in program_rows(p) {
            if v.is_empty() {
                continue;
            }
            md.push_str(&format!("- **{k}:** {}\n", esc_md(&v)));
        }
        md.push('\n');
    }

    md.push_str("---\n\n*Generated by Windows Program Inventory*\n");
    md
}

/// Build a print-optimized HTML document that auto-triggers the print dialog
/// (which includes "Save as PDF" on Windows 10+). Each program uses the same
/// detail rows as the HTML report — Path, Publisher, Version, Status, Source,
/// Architecture, Location, Executable, Installed.
fn build_print_html(programs: &[engine::models::Program], cache: &Path, now: &str) -> String {
    let mut rows = String::new();
    let mut _with_icons = 0usize;
    for p in programs {
        let letter = p.name.chars().next().map(|c| c.to_uppercase().to_string()).unwrap_or_default();
        let icon_html = if p.has_icon {
            let file = cache.join(format!("{}.png", p.id));
            if let Ok(bytes) = std::fs::read(file) {
                use base64::Engine;
                let b64 = base64::engine::general_purpose::STANDARD.encode(&bytes);
                _with_icons += 1;
                format!(r#"<div class="icon"><img src="data:image/png;base64,{b64}" alt="" /></div>"#)
            } else {
                format!(r#"<div class="icon fallback">{letter}</div>"#)
            }
        } else {
            format!(r#"<div class="icon fallback">{letter}</div>"#)
        };
        let name = if p.name.trim().is_empty() {
            "Unknown".to_string()
        } else {
            p.name.clone()
        };
        let badge = if p.status.is_empty() || p.status == "installed" {
            String::new()
        } else {
            format!(r#"<span class="st st-{s}">{s}</span>"#, s = esc_html(&p.status))
        };
        let mut detail = String::new();
        for (k, v) in program_rows(p) {
            if v.is_empty() {
                continue;
            }
            detail.push_str(&format!(
                r#"<div class="row"><span class="k">{k}</span><span class="v" dir="auto">{v}</span></div>"#,
                k = k,
                v = esc_html(&v)
            ));
        }
        rows.push_str(&format!(
            r#"<div class="item">
  {icon_html}
  <div class="info">
    <div class="name" dir="auto">{name}{badge}</div>
    {detail}
  </div>
</div>
"#,
            icon_html = icon_html,
            name = esc_html(&name),
            badge = badge,
            detail = detail,
        ));
    }

    let total = programs.len();
    format!(
        r#"<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Windows Program Inventory — Report</title>
<style>
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: "Segoe UI", system-ui, sans-serif; color: #1a1a2e; background: #fff; }}
.hdr {{ background: linear-gradient(135deg, #6366f1, #8b5cf6); color: #fff;
  padding: 24px 32px; display: flex; align-items: baseline; gap: 16px; flex-wrap: wrap; }}
.hdr h1 {{ font-size: 20px; }}
.hdr .meta {{ margin-left: auto; font-size: 12px; opacity: .85; }}
.hdr .count {{ font-size: 12px; background: rgba(255,255,255,.18);
  padding: 3px 12px; border-radius: 99px; }}
.wrap {{ max-width: 980px; margin: 18px auto; padding: 0 16px; }}
.item {{
  display: flex; align-items: flex-start; gap: 14px; break-inside: avoid;
  border: 1px solid #e5e7eb; border-radius: 12px; padding: 12px 14px; margin-bottom: 10px;
  background: #fff;
}}
.icon {{ width: 40px; height: 40px; flex: none; }}
.icon img {{ width: 40px; height: 40px; object-fit: contain; border-radius: 8px;
  background: #f0f2fa; border: 1px solid #e5e7eb; padding: 4px; }}
.fallback {{ display: grid; place-items: center; color: #fff; font-weight: 700; font-size: 15px;
  background: linear-gradient(135deg, #94a3b8, #cbd5e1); border-radius: 8px; }}
.info {{ min-width: 0; flex: 1; }}
.name {{ font-weight: 700; font-size: 14px; }}
.st {{ font-size: 10px; text-transform: uppercase; letter-spacing: .4px;
  padding: 2px 8px; border-radius: 999px; margin-left: 6px; font-weight: 700;
  background: #4f46e5; color: #fff; vertical-align: 2px; }}
.st-store {{ background: #0ea5e9; }}
.st-shortcut {{ background: #10b981; }}
.st-broken {{ background: #ef4444; }}
.row {{ display: grid; grid-template-columns: 92px 1fr; gap: 8px; margin-top: 4px; font-size: 11.5px; }}
.row .k {{ color: #6b7280; font-weight: 600; }}
.row .v {{ word-break: break-all; overflow-wrap: anywhere; }}
.foot {{ text-align: center; color: #9ca3af; font-size: 11px; padding: 20px 0 40px; }}
@media print {{
  body {{ background: #fff; }}
  .hdr {{ -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
  .item {{ box-shadow: none; border-color: #cbd5e1; }}
  .row {{ font-size: 10px; }}
}}
</style>
</head>
<body>
<header class="hdr">
  <h1>Windows Program Inventory</h1>
  <span class="count">{total} programs</span>
  <span class="meta">{now}</span>
</header>
<main class="wrap">
{rows}
</main>
<div class="foot">Generated by Windows Program Inventory</div>
</body>
</html>"#,
        total = total,
        now = now,
        rows = rows,
    )
}

/// Export the program inventory as a Markdown file and open it.
#[tauri::command]
async fn export_report_md(app: tauri::AppHandle) -> Result<String, String> {
    let cache = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("icons-v3");

    let cache_for_block = cache.clone();
    let programs = tauri::async_runtime::spawn_blocking(move || {
        let mut list = engine::scan_all();
        engine::icons::attach_icons(&mut list, &cache_for_block);
        list
    })
    .await
    .map_err(|e| e.to_string())?;

    let md = build_report_md(&programs);

    let dir = app
        .path()
        .document_dir()
        .or_else(|_| app.path().home_dir())
        .or_else(|_| app.path().app_data_dir())
        .map_err(|e| e.to_string())?;
    let file = dir.join("Windows Programs Report.md");
    if let Err(e) = std::fs::write(&file, &md) {
        let fallback = std::env::temp_dir().join("Windows Programs Report.md");
        std::fs::write(&fallback, &md).map_err(|f| format!("MD write error: {e} / {f}"))?;
        let path_str = fallback.to_string_lossy().into_owned();
        shell_open(&path_str)?;
        return Ok(path_str);
    }
    let path_str = file.to_string_lossy().into_owned();
    shell_open(&path_str)?;
    Ok(path_str)
}

/// Export the report as a print-optimized HTML that auto-triggers the print
/// dialog (the user can choose "Save as PDF" from there).
#[tauri::command]
async fn export_report_pdf(app: tauri::AppHandle) -> Result<String, String> {
    let cache = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("icons-v3");

    let cache_for_block = cache.clone();
    let programs = tauri::async_runtime::spawn_blocking(move || {
        let mut list = engine::scan_all();
        engine::icons::attach_icons(&mut list, &cache_for_block);
        list
    })
    .await
    .map_err(|e| e.to_string())?;

    let now = chrono::Local::now().format("%Y-%m-%d %H:%M").to_string();
    let mut html = build_print_html(&programs, &cache, &now);
    html.push_str("<script>window.onload=function(){setTimeout(function(){window.print()},600)}</sc");
    html.push_str("ript>\n");

    let dir = app
        .path()
        .document_dir()
        .or_else(|_| app.path().home_dir())
        .or_else(|_| app.path().app_data_dir())
        .map_err(|e| e.to_string())?;
    let file = dir.join("WPI Report Print.html");
    if let Err(e) = std::fs::write(&file, &html) {
        let fallback = std::env::temp_dir().join("WPI Report Print.html");
        std::fs::write(&fallback, &html).map_err(|f| format!("PDF write error: {e} / {f}"))?;
        let path_str = fallback.to_string_lossy().into_owned();
        shell_open(&path_str)?;
        return Ok(path_str);
    }
    let path_str = file.to_string_lossy().into_owned();
    shell_open(&path_str)?;
    Ok(path_str)
}

/// Generate an HTML report for selected programs only (sent from the frontend).
#[tauri::command]
async fn export_custom_report(
    app: tauri::AppHandle,
    programs: Vec<engine::models::Program>,
) -> Result<String, String> {
    let cache = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("icons-v3");

    let now = chrono::Local::now().format("%Y-%m-%d %H:%M").to_string();
    let (html, _icons) = build_report_html(&programs, &cache, &now);

    write_and_open(&app, "WPI Custom Report.html", &html)
}

/// Generate a Markdown report for the selected programs only.
#[tauri::command]
async fn export_custom_report_md(
    app: tauri::AppHandle,
    programs: Vec<engine::models::Program>,
) -> Result<String, String> {
    let md = build_report_md(&programs);
    write_and_open(&app, "WPI Custom Report.md", &md)
}

/// Generate a print/PDF report for the selected programs only — opens the print
/// dialog (with "Save as PDF") in the default browser.
#[tauri::command]
async fn export_custom_report_pdf(
    app: tauri::AppHandle,
    programs: Vec<engine::models::Program>,
) -> Result<String, String> {
    let cache = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("icons-v3");

    let now = chrono::Local::now().format("%Y-%m-%d %H:%M").to_string();
    let mut html = build_print_html(&programs, &cache, &now);
    html.push_str("<script>window.onload=function(){setTimeout(function(){window.print()},600)}</sc");
    html.push_str("ript>\n");
    write_and_open(&app, "WPI Custom Report Print.html", &html)
}

/// Write `content` next to Documents (falling back to home / app-data / temp)
/// and open it in the default viewer. Returns the final path.
fn write_and_open(app: &tauri::AppHandle, name: &str, content: &str) -> Result<String, String> {
    let dir = app
        .path()
        .document_dir()
        .or_else(|_| app.path().home_dir())
        .or_else(|_| app.path().app_data_dir())
        .map_err(|e| e.to_string())?;
    let file = dir.join(name);
    if let Err(e) = std::fs::write(&file, content) {
        let fallback = std::env::temp_dir().join(name);
        std::fs::write(&fallback, content).map_err(|f| format!("{name}: {e} / {f}"))?;
        let path_str = fallback.to_string_lossy().into_owned();
        shell_open(&path_str)?;
        return Ok(path_str);
    }
    let path_str = file.to_string_lossy().into_owned();
    shell_open(&path_str)?;
    Ok(path_str)
}

pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            scan,
            launch_program,
            open_file_location,
            get_icon,
            export_report,
            export_report_md,
            export_report_pdf,
            export_custom_report,
            export_custom_report_md,
            export_custom_report_pdf
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shell_open_opens_file() {
        let f = std::env::temp_dir().join("wpi-shell-open-test.html");
        std::fs::write(&f, "<h1>test</h1>").expect("write");
        let r = shell_open(&f.to_string_lossy());
        let _ = std::fs::remove_file(&f);
        assert_eq!(r, Ok(()), "ShellExecuteW should open the file in default browser");
    }

    #[test]
    fn build_report_md_writes_markdown() {
        let list = engine::scan_all();
        let md = build_report_md(&list);

        assert!(md.starts_with("# Windows Program Inventory"));
        assert!(md.contains("**Path:**"), "must have Path row");
        assert!(md.contains("**Version:**"), "must have Version row");
        assert!(md.contains("**Source:**"), "must have Source row");
        assert!(md.contains("**Status:**"), "must have Status row");
        assert!(md.contains("**Location:**"), "must have Location row");
    }

    #[test]
    fn build_print_html_writes_print_doc() {
        let cache = std::env::temp_dir().join("wpi-print-test-icons");
        let mut list = engine::scan_all();
        engine::icons::attach_icons(&mut list, &cache);
        let html = build_print_html(&list, &cache, "2099-01-01 00:00");

        assert!(html.starts_with("<!DOCTYPE html>"));
        assert_eq!(html.matches("<div class=\"item\">").count(), list.len());
        assert!(html.contains("Windows Program Inventory"));
        assert!(html.contains("**Path:**") || html.contains("Path"), "must show program detail");
        let file = std::env::temp_dir().join("wpi-print-test.html");
        std::fs::write(&file, &html).expect("write");
        assert!(file.metadata().unwrap().len() > 5_000);
        let _ = std::fs::remove_file(&file);
    }
        #[test]
    fn build_report_html_writes_real_report() {
        let cache = std::env::temp_dir().join("wpi-report-test-icons");
        let mut list = engine::scan_all();
        engine::icons::attach_icons(&mut list, &cache);
        let (html, icons) = build_report_html(&list, &cache, "2099-01-01 00:00");

        assert!(html.starts_with("<!DOCTYPE html>"));
        assert_eq!(html.matches("<div class=\"item\">").count(), list.len());
        assert_eq!(html.matches("<img class=\"app-icon\"").count(), icons);
        assert!(list.iter().any(|p| p.has_icon), "expected real icons on this machine");

        let file = std::env::temp_dir().join("wpi-report-test.html");
        std::fs::write(&file, html).expect("write");
        assert!(file.exists(), "report file written");
        assert!(file.metadata().unwrap().len() > 10_000, "report should be non-trivial size");
        let _ = std::fs::remove_file(&file);
    }
}