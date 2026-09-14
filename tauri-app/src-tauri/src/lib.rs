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
            std::process::Command::new(&c)
                .spawn()
                .map(|_| ())
                .map_err(|e| format!("Cannot launch {c}: {e}"))?;
            return Ok(());
        }
    }

    Err(format!("No runnable file found for \"{exe_path}\""))
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

fn report_path_of(p: &engine::models::Program) -> String {
    if let Some(icon) = &p.display_icon {
        let base = icon.split(',').next().unwrap_or(icon).trim();
        if !base.is_empty() {
            return base.to_string();
        }
    }
    p.install_location.clone().unwrap_or_default()
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
        let path = report_path_of(p);
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
        rows.push_str(&format!(
            r#"      <div class="item">
        {marker}
        <div class="info">
          <div class="name" dir="auto">{}</div>
          <div class="path" dir="auto">{}</div>
        </div>
      </div>
"#,
            esc_html(&name),
            if path.is_empty() {
                "â€”".to_string()
            } else {
                esc_html(&path)
            },
        ));
    }

    let html = format!(
        r#"<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Windows Program Inventory â€” Report</title>
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
  .wrap {{ max-width: 880px; margin: 22px auto; padding: 0 18px; }}
  .group {{ margin: 16px 0 8px; font-size: 12px; color: var(--muted);
    text-transform: uppercase; letter-spacing: .6px; }}
  .item {{
    display: flex; align-items: center; gap: 16px; padding: 12px 16px;
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
  .info {{ min-width: 0; }}
  .name {{ font-size: 14.5px; font-weight: 600; }}
  .path {{ font-size: 12px; color: var(--muted); margin-top: 2px;
    word-break: break-all; }}
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
    <span class="count">{total} programs آ· {with_icons} icons</span>
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

/// Generate Markdown report text from a program list.
fn build_report_md(programs: &[engine::models::Program]) -> String {
    let mut md = String::with_capacity(programs.len() * 160);
    md.push_str("# Windows Program Inventory\n\n");
    let now = chrono::Local::now().format("%Y-%m-%d %H:%M");
    md.push_str(&format!("> Generated: {now} آ· {total} programs\n\n", total = programs.len()));

    let groups: [(&str, &str); 4] = [
        ("installed", "Installed"),
        ("shortcut", "Shortcuts"),
        ("store", "Store Apps"),
        ("broken", "Broken"),
    ];

    for (status, title) in groups {
        let items: Vec<_> = programs.iter().filter(|p| p.status == status).collect();
        if items.is_empty() {
            continue;
        }
        md.push_str(&format!("## {title} ({})\n\n", items.len()));
        md.push_str("| # | Name | Version | Source | Location |\n");
        md.push_str("|---|------|---------|--------|----------|\n");
        for (i, p) in items.iter().enumerate() {
            let ver = p.version.as_deref().unwrap_or("â€”");
            let src = &p.source;
            let loc = p.install_location.as_deref().unwrap_or("â€”");
            md.push_str(&format!(
                "| {} | {} | {} | {} | {} |\n",
                i + 1,
                p.name,
                ver,
                src,
                loc
            ));
        }
        md.push('\n');
    }

    md.push_str("---\n\n*Generated by Windows Program Inventory*\n");
    md
}

/// Build a print-optimized HTML document that auto-triggers the print dialog
/// (which includes "Save as PDF" on Windows 10+).
fn build_print_html(programs: &[engine::models::Program], cache: &Path, now: &str) -> String {
    let (rows, _with_icons) = {
        let mut rows = String::new();
        let mut with_icons = 0usize;
        for p in programs {
            let letter = p.name.chars().next().map(|c| c.to_uppercase().to_string()).unwrap_or_default();
            let icon_html = if p.has_icon {
                let file = cache.join(format!("{}.png", p.id));
                if let Ok(bytes) = std::fs::read(file) {
                    use base64::Engine;
                    let b64 = base64::engine::general_purpose::STANDARD.encode(&bytes);
                    with_icons += 1;
                    format!(r#"<img class="icon" src="data:image/png;base64,{b64}" alt="" />"#)
                } else {
                    format!(r#"<div class="icon fallback">{letter}</div>"#)
                }
            } else {
                format!(r#"<div class="icon fallback">{letter}</div>"#)
            };
            rows.push_str(&format!(
                r#"<tr>
  <td class="tc">{icon_html}</td>
  <td class="name">{name}</td>
  <td>{ver}</td>
  <td>{src}</td>
  <td>{status}</td>
  <td class="loc">{loc}</td>
</tr>"#,
                icon_html = icon_html,
                name = esc_html(&p.name),
                ver = esc_html(p.version.as_deref().unwrap_or("â€”")),
                src = esc_html(&p.source),
                status = esc_html(&p.status),
                loc = esc_html(p.install_location.as_deref().unwrap_or("â€”")),
            ));
        }
        (rows, with_icons)
    };

    let total = programs.len();
    format!(
        r#"<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Windows Program Inventory â€” Report</title>
<style>
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: "Segoe UI", system-ui, sans-serif; color: #1a1a2e; background: #fff; }}
.hdr {{ background: linear-gradient(135deg, #6366f1, #8b5cf6); color: #fff;
  padding: 24px 32px; display: flex; align-items: baseline; gap: 16px; flex-wrap: wrap; }}
.hdr h1 {{ font-size: 20px; }}
.hdr .meta {{ margin-left: auto; font-size: 12px; opacity: .85; }}
.hdr .count {{ font-size: 12px; background: rgba(255,255,255,.18);
  padding: 3px 12px; border-radius: 99px; }}
table {{ width: 100%; border-collapse: collapse; margin: 16px 0; font-size: 11.5px; }}
th {{ text-align: left; padding: 8px 10px; border-bottom: 2px solid #e5e7eb;
  font-size: 10.5px; text-transform: uppercase; letter-spacing: .5px; color: #6b7280; }}
td {{ padding: 7px 10px; border-bottom: 1px solid #f0f0f5; vertical-align: middle; }}
tr:nth-child(even) {{ background: #fafbff; }}
.icon {{ width: 32px; height: 32px; border-radius: 7px; object-fit: contain;
  background: #f0f2fa; border: 1px solid #e5e7eb; padding: 4px; }}
.fallback {{ display: grid; place-items: center; color: #fff; font-weight: 700; font-size: 14px;
  background: linear-gradient(135deg, #94a3b8, #cbd5e1); }}
.name {{ font-weight: 600; }}
.loc {{ font-size: 10px; color: #9ca3af; word-break: break-all; max-width: 240px; }}
.tc {{ width: 40px; }}
.foot {{ text-align: center; color: #9ca3af; font-size: 11px; padding: 20px 0 40px; }}
@media print {{
  body {{ background: #fff; }}
  .hdr {{ -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
  table {{ font-size: 9px; }}
}}
</style>
</head>
<body>
<header class="hdr">
  <h1>Windows Program Inventory</h1>
  <span class="count">{total} programs</span>
  <span class="meta">{now}</span>
</header>
<table>
<thead><tr><th></th><th>Name</th><th>Version</th><th>Source</th><th>Status</th><th>Location</th></tr></thead>
<tbody>
{rows}
</tbody>
</table>
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

    let dir = app
        .path()
        .document_dir()
        .or_else(|_| app.path().home_dir())
        .or_else(|_| app.path().app_data_dir())
        .map_err(|e| e.to_string())?;
    let file = dir.join("WPI Custom Report.html");
    if let Err(e) = std::fs::write(&file, &html) {
        let fallback = std::env::temp_dir().join("WPI Custom Report.html");
        std::fs::write(&fallback, &html).map_err(|f| format!("custom report error: {e} / {f}"))?;
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
            get_icon,
            export_report,
            export_report_md,
            export_report_pdf,
            export_custom_report
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
        assert!(md.contains("| # | Name |"), "must have table header");
        assert!(md.contains("## Installed"), "must have Installed section");
        assert!(md.contains("## Shortcuts"), "must have Shortcuts section");
        assert!(md.contains("## Store Apps"), "must have Store Apps section");
        assert!(md.contains("## Broken"), "must have Broken section");
    }

    #[test]
    fn build_print_html_writes_print_doc() {
        let cache = std::env::temp_dir().join("wpi-print-test-icons");
        let mut list = engine::scan_all();
        engine::icons::attach_icons(&mut list, &cache);
        let html = build_print_html(&list, &cache, "2099-01-01 00:00");

        assert!(html.starts_with("<!DOCTYPE html>"));
        assert!(html.matches("</tr>").count() >= list.len(), "must have a row per program");
        assert!(html.contains("</table>"));
        assert!(html.contains("Windows Program Inventory"));
        let file = std::env::temp_dir().join("wpi-print-test.html");
        std::fs::write(&file, html).expect("write");
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