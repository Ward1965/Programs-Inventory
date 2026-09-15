pub mod registry;
pub mod icons;
pub mod models;
pub mod shortcuts;
pub mod store;

use std::collections::HashSet;
use std::ffi::OsStr;
use std::path::Path;
use std::path::PathBuf;

use icons::expand_env;
use models::Program;

fn runnable_of(p: &Program) -> Option<String> {
    let icon = p.display_icon.as_deref()?;
    let base = clean_path(icon);
    let expanded = expand_env(&base);
    if expanded.is_empty() {
        None
    } else {
        Some(expanded)
    }
}

fn is_exe(path: &Path) -> bool {
    path.is_file() && path.extension().map(|e| e.eq_ignore_ascii_case("exe")).unwrap_or(false)
}

fn is_exe_name(path: &str) -> bool {
    Path::new(path).extension().map(|e| e.eq_ignore_ascii_case("exe")).unwrap_or(false)
}

/// Heuristic keeper: hide hardware drivers and Windows/Microsoft system
/// components that only clutter an "installed programs" inventory (real
/// user-facing applications must never be hidden).
pub(crate) fn is_system_noise(
    name: &str,
    publisher: &str,
    _install_location: &str,
    display_icon: &str,
) -> bool {
    let n = name.to_lowercase();
    let p = publisher.to_lowercase();
    let i = display_icon.to_lowercase();

    // ── Windows system host binaries (rundll32, script hosts, …) ───────────
    // Start-menu "configuration" shortcuts often point at rundll32.exe; their
    // real subject is the DLL argument, which we do not resolve, so the only
    // sensible thing is to drop the row entirely instead of listing a system
    // binary as a program.
    let bin = Path::new(&i)
        .file_name()
        .and_then(OsStr::to_str)
        .map(|f| f.to_lowercase())
        .unwrap_or_default();
    const HOST_BINS: [&str; 6] = [
        "rundll32.exe",
        "wscript.exe",
        "cscript.exe",
        "mshta.exe",
        "regsvr32.exe",
        "svchost.exe",
    ];
    if HOST_BINS.contains(&bin.as_str()) {
        return true;
    }

    // ── Hardware drivers & driver installers ────────────────────────────────
    if n.contains("driver")
        || n.contains("windows driver package")
        || i.contains("dpinst")
        || i.contains("\\difx\\")
        || n.contains("libwdi")
        || n.contains("libusb")
        || n.contains("npcap")
        || n.contains("winfsp")
        || n.contains("bluetooth")
        || n.contains("wireless lan")
        || n.contains("wlan")
        || n.contains("usb-cec adapter")
        || n.contains("universal dock")
        || n.contains("pen settings service")
        || (p.contains("samsung") && n.contains("series"))
        || (p.contains("intel") && n.contains("sensor"))
        || n.contains("amd system monitor")
        || (p.contains("samsung") && n.contains("printer live update"))
    {
        return true;
    }

    // ── Microsoft runtimes / SDKs / development infrastructure ─────────────
    if p.contains("microsoft") || p.contains("microsoft corporation") {
        if n.contains("redistributable")
            || n.contains("windows desktop runtime")
            || (n.contains(".net") && (n.contains("sdk") || n.contains("runtime") || n.contains("shared framework") || n.contains("asp.net")))
            || n.contains("software development kit")
            || n.contains("sdk addon")
            || n.contains("assessment and deployment kit")
            || n.contains("vs_coreeditorfonts")
            || n.contains("visual studio 2010 tools for office runtime")
            || (n.contains("visual studio") && (n.contains("installer") || n.contains("build tools")))
            || n.contains("update health")
            || n.contains("office file validation")
            || (n.contains("update for") && n.contains("windows") && n.contains("kb"))
            || n.contains("network monitor")
        {
            return true;
        }
    }

    // ── Windows-of-the-OS components regardless of publisher ───────────────
    if n.contains("intel(r) management engine") || n.contains("intel(r) chipset") {
        return true;
    }

    false
}

/// Registry paths are wrapped in quotes and may carry an icon index suffix
/// (`"C:\...\App.exe"` or `App.exe,0`). Collapse those to a clean filesystem
/// path while keeping `shell:` URIs intact.
fn clean_path(raw: &str) -> String {
    let raw = raw.trim();
    if raw.starts_with("shell:") {
        return raw.to_string();
    }
    raw.split(',')
        .next()
        .unwrap_or(raw)
        .trim()
        .trim_matches('"')
        .trim()
        .to_string()
}

/* ───────────────────────── Smart main-exe detection ─────────────────────────
 * Many Uninstall keys leave DisplayIcon empty or pointing at a helper
 * (uninstaller, updater, crash-sender, 7z …). Instead of trusting that blindly
 * or picking the first .exe in the folder, we score every candidate executable
 * by matching the program's name against the exe's own Version Information
 * (ProductName / FileDescription / OriginalFilename) and its file name, while
 * heavily penalizing known non-application binaries.
 * ────────────────────────────────────────────────────────────────────────── */

#[cfg(windows)]
mod pe_version {
    use std::os::windows::ffi::OsStrExt;
    use std::path::Path;

    #[link(name = "version")]
    extern "system" {
        fn GetFileVersionInfoSizeW(lptstr_filename: *const u16, dw_handle: *mut u32) -> u32;
        fn GetFileVersionInfoW(
            lptstr_filename: *const u16,
            dw_handle: u32,
            dw_len: u32,
            lp_data: *mut u8,
        ) -> i32;
        fn VerQueryValueW(
            p_block: *const u8,
            lp_sub_block: *const u16,
            lplp_buffer: *mut *mut u8,
            pu_len: *mut u32,
        ) -> i32;
    }

    /// (ProductName, FileDescription, OriginalFilename, CompanyName) from the
    /// exe's Version resource. None when the file carries no version info.
    pub fn identity(
        path: &Path,
    ) -> Option<(Option<String>, Option<String>, Option<String>, Option<String>)> {
        let wide: Vec<u16> = path
            .as_os_str()
            .encode_wide()
            .chain(std::iter::once(0))
            .collect();
        unsafe {
            let mut handle: u32 = 0;
            let size = GetFileVersionInfoSizeW(wide.as_ptr(), &mut handle);
            if size == 0 {
                return None;
            }
            let mut buf: Vec<u8> = vec![0u8; size as usize];
            if GetFileVersionInfoW(wide.as_ptr(), handle, size, buf.as_mut_ptr()) == 0 {
                return None;
            }

            // Locate the (language, codepage) block so we can address StringFileInfo.
            let mut p_trans: *mut u8 = std::ptr::null_mut();
            let mut len: u32 = 0;
            let trans_key: Vec<u16> = "\\VarFileInfo\\Translation"
                .encode_utf16()
                .chain(std::iter::once(0))
                .collect();
            if VerQueryValueW(buf.as_ptr(), trans_key.as_ptr(), &mut p_trans, &mut len) == 0
                || len < 4
                || p_trans.is_null()
            {
                return None;
            }
            let lang = u16::from_le_bytes([*p_trans, *p_trans.add(1)]);
            let cp = u16::from_le_bytes([*p_trans.add(2), *p_trans.add(3)]);
            let sub = format!("\\StringFileInfo\\{:04X}{:04X}\\", lang, cp);

            let keys = ["ProductName", "FileDescription", "OriginalFilename", "CompanyName"];
            let mut out = Vec::with_capacity(keys.len());
            for key in keys {
                let key_path = format!("{sub}{key}");
                let wide_key: Vec<u16> = key_path.encode_utf16().chain(std::iter::once(0)).collect();
                let mut p: *mut u8 = std::ptr::null_mut();
                let mut l: u32 = 0;
                if VerQueryValueW(buf.as_ptr(), wide_key.as_ptr(), &mut p, &mut l) != 0
                    && l > 0
                    && !p.is_null()
                {
                    let words = std::slice::from_raw_parts(p as *const u16, (l as usize) / 2);
                    let text = words
                        .split(|&c| c == 0)
                        .next()
                        .and_then(|u| String::from_utf16(u).ok())
                        .filter(|s| !s.trim().is_empty());
                    out.push(text);
                } else {
                    out.push(None);
                }
            }
            Some((out[0].clone(), out[1].clone(), out[2].clone(), out[3].clone()))
        }
    }
}

/// Lowercased alphanumeric form used for fuzzy matching ("Visual C++ x64" → "visualcx64").
fn norm_name(s: &str) -> String {
    s.to_lowercase().chars().filter(|c| c.is_alphanumeric()).collect()
}

fn norm_tokens(s: &str) -> Vec<String> {
    let mut v = s
        .to_lowercase()
        .split(|c: char| !c.is_alphanumeric())
        .filter(|t| t.len() >= 2)
        .map(String::from)
        .collect::<Vec<_>>();
    v.sort();
    v.dedup();
    v
}

/// Binaries that are almost never the program the user wants to launch.
fn is_support_binary(path: &Path) -> bool {
    let stem = path
        .file_stem()
        .map(|s| s.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default();
    let p = path.to_string_lossy().to_ascii_lowercase();
    let in_bad_dir = p.contains("package cache") || p.contains("windows\\installer");
    if in_bad_dir {
        return true;
    }
    let exact_bad = [
        "uninstall", "uninstaller", "uninst", "update", "updater", "setup", "setupx",
        "installer", "install", "helper", "dpinst", "aria2c", "ffmpeg", "ncat", "adb",
        "7z", "debugfs", "maintenancetool", "activator", "keyhh", "crashsender",
        "crashreport", "service", "daemon",
    ];
    let prefix_bad = [
        "unins", "uninstall", "update", "updater", "setup", "install", "helper", "crash",
        "service", "daemon",
    ];
    if exact_bad.contains(&stem.as_str()) || prefix_bad.iter().any(|w| stem.starts_with(w)) {
        return true;
    }
    // "*svc*", "*-svc.exe" style service processes.
    stem.contains("svc")
}

/// Score one candidate executable against the installed program's name.
fn score_exe(path: &Path, name_norm: &str, name_tokens: &[String], folder_stem: &str) -> i32 {
    let mut score = 0i32;

    if is_support_binary(path) {
        score -= 600;
    }

    let stem = path
        .file_stem()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default();
    let stem_norm = norm_name(&stem);
    let folder_norm = norm_name(folder_stem);

    // File name that mirrors the install folder name (App.exe inside App\) is
    // a strong signal the binary is the main application.
    if !folder_norm.is_empty() && stem_norm.contains(&folder_norm) {
        score += 30;
    }

    // Match the program's own name against the exe's embedded version info,
    // the most reliable "this is that program" signal. When several binaries
    // of a suite share the product name (Hex Workshop ships BConv64/Calc64/
    // HWorks64 …), the one whose description matches the *shortest* is the main
    // application, so reward closeness to the exact display name.
    if let Some((prod, desc, orig, company)) = pe_version::identity(path) {
        let combos: [(Option<String>, i32, i32); 4] = [
            (prod, 120, 50),
            (desc, 80, 30),
            (orig, 40, 15),
            (company, 25, 10),
        ];
        for (text, strong, weak) in combos {
            let Some(t) = text else { continue };
            let m = norm_name(&t);
            if m.is_empty() {
                continue;
            }
            if m == name_norm {
                score += strong + 40;
            } else if name_norm.contains(&m) && !m.is_empty() {
                // Shortest exact substring of the program name = the main exe.
                score += strong;
                score += (40 - (m.len() as i32).min(40)) * 1; // shorter wins
            } else if m.contains(name_norm) {
                score += strong + 20;
                // Penalize obvious sub-tools that merely mention the suite name.
                let coarse = ["calculator", "converter", "unlocker", "disk access", "helper", "console", "daemon", "service", "agent", "crash"];
                if coarse.iter().any(|w| m.contains(w)) {
                    score -= 90;
                }
            } else if name_tokens.iter().any(|tok| m.contains(tok)) {
                score += weak;
            }
        }
    }

    // Last signal: the file stem itself resembles the program name. Among
    // equally-named files the shortest / exact sigma wins again.
    if stem_norm == name_norm || name_norm.contains(&stem_norm) {
        score += 70;
        if !stem_norm.is_empty() {
            score += (40 - (stem_norm.len() as i32).min(40)) * 1;
        }
    } else if name_tokens.iter().any(|tok| stem_norm.contains(tok)) && name_norm.len() >= 3 {
        score += 30;
    }

    score
}

/// The main executable may live next to the DisplayIcon even when no install
/// folder is recorded (e.g. AIMP points DisplayIcon at Uninstall.exe). Collect
/// every direct candidate around the icon and inside the install folder.
/// System-wide folders (C:\Windows, Common Files, …) are never enumerated:
/// probing them for a single app would read version info of thousands of exes.
fn candidates_for(p: &Program) -> Vec<String> {
    let icon_exe_support = p.display_icon.as_deref().map(clean_path).filter(|c| !c.starts_with("shell:")).map(|c| expand_env(&c)).filter(|e| is_support_binary(Path::new(e))).is_some();

    let mut dirs = Vec::new();

    // The install folder is the natural home of the real binary.
    if let Some(loc) = p.install_location.as_deref() {
        let expanded = expand_env(&clean_path(loc));
        let dir = PathBuf::from(&expanded);
        if dir.is_dir() && dir_is_probeable(&dir) {
            dirs.push(dir);
        }
    }

    // The DisplayIcon's own folder can also hold the application, but only
    // bother when the recorded icon is a helper (uninstaller/setup/…) or there
    // is no install folder at all.
    if p.install_location.is_none() || icon_exe_support {
        if let Some(icon) = p.display_icon.as_deref() {
            let cleaned = clean_path(icon);
            if !cleaned.starts_with("shell:") {
                let expanded = expand_env(&cleaned);
                if let Some(parent) = Path::new(&expanded).parent() {
                    if parent.is_dir() && dir_is_probeable(parent) {
                        dirs.push(parent.to_path_buf());
                    }
                }
            }
        }
    }

    let mut out: Vec<String> = Vec::new();
    let mut names: HashSet<String> = HashSet::new();
    for dir in dirs {
        let Ok(entries) = std::fs::read_dir(&dir) else { continue };
        for entry in entries.flatten() {
            let path = entry.path();
            if is_exe(&path) {
                if names.insert(path.to_string_lossy().to_lowercase()) {
                    out.push(path.to_string_lossy().into_owned());
                }
            }
        }
    }
    out
}

/// Folders that contain system-wide binaries must not be scanned candidate by
/// candidate; also skip any directory too large to probe cheaply.
fn dir_is_probeable(dir: &Path) -> bool {
    let s = dir.to_string_lossy().to_ascii_lowercase();
    let system_dirs = [
        r"c:\windows",
        r"c:\program files\common files",
        r"c:\program files (x86)\common files",
        r"c:\program files\windowsapps",
        r"c:\programdata",
        r"c:\program files\difx",
    ];
    if system_dirs.iter().any(|d| s.starts_with(d)) {
        return false;
    }
    // Some entries point their icon into a globally-shared folder (e.g. Adobe
    // natively installs into "C:\Program Files\Adobe"): cap the probe cost.
    let exe_count = std::fs::read_dir(dir)
        .map(|it| {
            it.flatten()
                .filter(|e| e.path().extension().map(|x| x.eq_ignore_ascii_case("exe")).unwrap_or(false))
                .take(60)
                .count()
        })
        .unwrap_or(0);
    exe_count < 60 && exe_count > 0
}

/// Smart resolution of the main executable for a registry/shortcut program.
/// Candidates are the recorded DisplayIcon plus every exe in the icon folder
/// and the install folder. Each is scored against the program's name (via the
/// exe's embedded version info); the registry-provided exe is kept unless a
/// better candidate beats it by a clear margin.
fn smart_main_exe(p: &Program) -> Option<String> {
    let name_norm = norm_name(&p.name);
    let name_tokens = norm_tokens(&p.name);

    let icon_exe: Option<String> = p.display_icon.as_deref()
        .map(clean_path)
        .filter(|c| !c.starts_with("shell:"))
        .map(|c| expand_env(&c))
        .filter(|e| is_exe(Path::new(e)));

    let candidates = candidates_for(p);

    if candidates.is_empty() {
        return icon_exe;
    }

    let mut scored: Vec<(i32, String)> = Vec::new();
    for c in &candidates {
        let path = Path::new(c);
        let folder_stem = path.parent().and_then(|d| d.file_name()).map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
        let s = score_exe(path, &name_norm, &name_tokens, &folder_stem);
        scored.push((s, c.clone()));
    }
    scored.sort_by(|a, b| b.0.cmp(&a.0).then_with(|| a.1.to_lowercase().cmp(&b.1.to_lowercase())));
    let (best_score, best) = scored[0].clone();

    // Prefer the registry-provided exe when it is a reasonable candidate; only
    // switch when the folder clearly holds the real application.
    if let Some(icon) = &icon_exe {
        let icon_score = score_exe(Path::new(icon), &name_norm, &name_tokens, "");
        if best == *icon || best_score - icon_score < 40 {
            return Some(icon.clone());
        }
        // A file-name-only win is not trustworthy (e.g. ImDisk-Dlg.exe vs the
        // registry's config.exe when no binary carries version info). Only
        // replace the registry exe when the winner embeds version info that
        // matches the program name.
        if !is_support_binary(Path::new(icon)) && !has_name_identity(&best, &name_norm, &name_tokens) {
            return Some(icon.clone());
        }
    }
    Some(best)
}

/// True when the executable embeds a product/description naming the program.
fn has_name_identity(path: &str, name_norm: &str, name_tokens: &[String]) -> bool {
    let Some((prod, desc, orig, _)) = pe_version::identity(Path::new(path)) else {
        return false;
    };
    [prod, desc, orig].into_iter().flatten().any(|t| {
        let m = norm_name(&t);
        !m.is_empty()
            && (m == name_norm
                || name_norm.contains(&m)
                || m.contains(name_norm)
                || name_tokens.iter().any(|tok| m.contains(tok)))
    })
}

/// Keep only entries backed by a runnable executable. Help files, icon/decor
/// files and folder-only records are noise, so they are dropped. When an entry
/// only exposes its install folder, the best-matching `.exe` inside becomes its
/// target (grand uninstaller/updater files are never chosen).
/// Keep only entries backed by a runnable executable. Help files, icon/decor
/// files and folder-only records are noise, so they are dropped. Registry
/// entries get smart main-exe resolution (the recorded icon may be a helper);
/// start-menu shortcuts are authoritative and keep their resolved target.
fn solidify(p: &mut Program) -> bool {
    if p.source == "store" {
        return true; // Store apps are launched through the shell, not an exe.
    }

    if let Some(loc) = p.install_location.as_deref() {
        let cleaned = expand_env(&clean_path(loc));
        if !cleaned.is_empty() {
            p.install_location = Some(cleaned);
        }
    }

    let icon_target: Option<String> = p.display_icon.as_deref().map(clean_path).and_then(|c| {
        if c.starts_with("shell:") {
            Some(c)
        } else {
            let expanded = expand_env(&c);
            Some(expanded).filter(|e| is_exe_name(e))
        }
    });

    match icon_target {
        // A resolved shell namespace link stays as-is.
        Some(ref t) if t.starts_with("shell:") => {
            p.display_icon = Some(t.clone());
            return true;
        }
        Some(ref t) => {
            if p.source == "start-menu" {
                // The shortcut is authoritative: an existing exe target stays
                // exactly as resolved; a missing one is kept as "broken".
                if is_exe(Path::new(t)) {
                    p.display_icon = Some(t.clone());
                    return true;
                }
                return true;
            }
            // Registry: run the smart scorer even when the recorded exe exists,
            // so an icon that points at Uninstall.exe/7z.exe gets corrected.
            let target = smart_main_exe(p).or_else(|| Some(t.clone()));
            if let Some(t) = target {
                p.display_icon = Some(t);
                return true;
            }
        }
        None => {
            if p.source == "start-menu" {
                return false; // nothing executable carried by the shortcut.
            }
            if let Some(t) = smart_main_exe(p) {
                p.display_icon = Some(t);
                return true;
            }
        }
    }
    false
}

/// Progress callback: `(percent, phase label)`. Must be safe to call from any
/// worker thread (icon extraction runs in a thread pool).
pub type Progress = dyn Fn(u8, &str) + Send + Sync;

/// Collect every installed program from all scan sources and attach icons.
pub fn scan_all() -> Vec<Program> {
    scan_all_with_progress(&|_, _| {})
}

/// `scan_all` with a progress callback that reports each real phase so the
/// frontend can mirror actual work instead of a cosmetic timer.
pub fn scan_all_with_progress(progress: &Progress) -> Vec<Program> {
    progress(5, "Warming up");
    let mut list: Vec<Program> = registry::scan_registry()
        .into_iter()
        .filter_map(|mut p| solidify(&mut p).then_some(p))
        .collect();

    progress(25, "Reading the installed registry…");
    let installed: HashSet<String> = list
        .iter()
        .filter_map(runnable_of)
        .filter(|p| Path::new(p).exists())
        .map(|p| p.trim().to_lowercase())
        .collect();

    list.extend(shortcuts::scan_all(&installed));
    progress(50, "Scanning your Start Menu…");

    // Shortcuts landed above with their resolved target in display_icon. Only
    // keep those that point at an executable (or a broken .exe hook); drop
    // nothing-but-links like .chm/.ico/.url/.lnk-only or plain folders.
    list = list
        .into_iter()
        .filter_map(|mut p| solidify(&mut p).then_some(p))
        .collect();

    list.extend(store::scan_registry());
    progress(70, "Enumerating Store apps…");

    // Drop any driver/Windows-system rows that slipped in through the other
    // sources (e.g. a copy sitting in the start menu or Store).
    list.retain(|p| {
        !is_system_noise(
            &p.name,
            p.publisher.as_deref().unwrap_or(""),
            p.install_location.as_deref().unwrap_or(""),
            p.display_icon.as_deref().unwrap_or(""),
        )
    });
    progress(80, "Preparing the inventory…");

    // Deterministic order, matching the frontend's name expectations.
    list.sort_by(|a, b| {
        let an = a.name.to_lowercase();
        let bn = b.name.to_lowercase();
        an.cmp(&bn).then_with(|| a.source.cmp(&b.source))
    });
    list
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn drivers_and_windows_system_are_hidden() {
        let list = scan_all();
        let names: Vec<String> = list.iter().map(|p| p.name.to_lowercase()).collect();
        let bad: Vec<&str> = [
            "windows driver package",
            "media tek sp driver",
            "realtek wireless lan driver",
            "intel(r) management engine",
            "intel(r) chipset",
            "microsoft visual c++ 2010",
            "microsoft .net sdk",
            "windows desktop runtime",
            "windows software development kit",
            "assessment and deployment kit",
            "microsoft update health tools",
            "update for x64-based windows",
            "npcap",
            "winfsp",
            "universal adb driver",
            "samsung m332x",
        ]
        .into_iter()
        .filter(|b| names.iter().any(|n| n.contains(b)))
        .collect();
        assert!(
            bad.is_empty(),
            "system/driver entries leaked into scan: {:?}",
            bad
        );
        // Real applications must survive the filter.
        for keep in ["7-zip", "firefox", "vlc", "notepad++", "telegram desktop", "imdisk toolkit"] {
            assert!(
                names.iter().any(|n| n.contains(keep)),
                "expected real app {keep} to stay in the inventory"
            );
        }
        eprintln!("scan_mix: total={}", list.len());
    }

    #[test]
    fn system_host_binaries_are_hidden() {
        assert!(is_system_noise(
            "LAV Audio Configuration",
            "",
            "",
            r"C:\Windows\System32\rundll32.exe",
        ));
        assert!(is_system_noise(
            "Properties (ExplorerPatcher)",
            "",
            "",
            r"C:\WINDOWS\system32\RUNDLL32.EXE",
        ));
        assert!(!is_system_noise(
            "LAV Filters",
            "",
            "",
            r"C:\Program Files (x86)\LAV Filters\LAVAudio.ax",
        ));
        // A real app must never be blocked by the host-binary rule.
        assert!(!is_system_noise(
            "ShareX",
            "",
            "",
            r"C:\Program Files\ShareX\ShareX.exe",
        ));
    }

    #[test]
    fn start_menu_scan_mixes_sources() {
        let list = scan_all();
        let installed = list.iter().filter(|p| p.status == "installed").count();
        let shortcuts = list.iter().filter(|p| p.status == "shortcut").count();
        let broken = list.iter().filter(|p| p.status == "broken").count();
        assert!(installed > 0, "registry installed programs required");
        // Consistency: a start-menu item is "shortcut" when its target exists,
        // "broken" otherwise, and always carries a display_icon.
        for p in list.iter().filter(|p| p.source == "start-menu") {
            let t = p.display_icon.as_deref().unwrap_or("");
            assert!(!t.is_empty(), "start-menu item lacks a display_icon: {}", p.name);
            let exists = Path::new(t).exists();
            assert_eq!(
                p.status == "shortcut",
                exists,
                "status mismatch for {} (target: {t})",
                p.name
            );
        }
        eprintln!("scan_mix: installed={installed} shortcut={shortcuts} broken={broken}");
    }

    #[test]
    fn shortcut_icons_extract() {
        let list = scan_all();
        let targets: Vec<String> = list
            .iter()
            .filter(|p| p.source == "start-menu")
            .filter_map(|p| p.display_icon.clone())
            .collect();
        assert!(!targets.is_empty(), "start menu must yield shortcut targets");
        assert!(
            targets.iter().filter(|t| Path::new(t).exists()).count() > 0,
            "some shortcuts must resolve to real files"
        );
        let ok = targets
            .iter()
            .filter(|t| Path::new(t).exists())
            .take(8)
            .filter(|t| super::icons::debug_extract(t).is_some())
            .count();
        assert!(ok > 0, "expected at least one shortcut target icon to extract");
    }

    #[test]
    fn every_listed_program_is_an_executable() {
        let list = scan_all();
        for p in list.iter() {
            let icon = p.display_icon.as_deref().unwrap_or("");
            if p.source == "store" {
                assert!(
                    icon.is_empty() || icon.starts_with("shell:"),
                    "store entries must launch through the shell: {icon}"
                );
                continue;
            }
            let base = icon.split(',').next().unwrap_or(icon).trim();
            let expanded = expand_env(base);
            assert!(
                expanded.to_lowercase().ends_with(".exe"),
                "{} lists a non-executable target: {icon}",
                p.name
            );
        }
    }
}