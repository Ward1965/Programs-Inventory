pub mod registry;
pub mod icons;
pub mod models;
pub mod shortcuts;
pub mod store;

use std::collections::HashSet;
use std::path::Path;

use icons::expand_env;
use models::Program;

fn runnable_of(p: &Program) -> Option<String> {
    let icon = p.display_icon.as_deref()?;
    let base = icon.split(',').next().unwrap_or(icon).trim();
    let expanded = expand_env(base);
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

/// First `.exe` found directly inside a folder (mirrors launch_program).
fn exe_in_dir(dir: &Path) -> Option<String> {
    std::fs::read_dir(dir)
        .ok()?
        .flatten()
        .find_map(|e| is_exe(&e.path()).then(|| e.path().to_string_lossy().into_owned()))
}

/// Keep only entries backed by a runnable executable. Help files, icon/decor
/// files and folder-only records are noise, so they are dropped. When an entry
/// only exposes its install folder, the first `.exe` inside becomes its target.
fn solidify(p: &mut Program) -> bool {
    if p.source == "store" {
        return true; // Store apps are launched through the shell, not an exe.
    }

    if let Some(icon) = p.display_icon.as_deref() {
        let raw = icon.split(',').next().unwrap_or(icon).trim();
        if raw.starts_with("shell:") {
            return true; // shell namespace (Store-style) app link
        }
        let expanded = expand_env(raw);
        if is_exe(Path::new(&expanded)) {
            return true;
        }
        if is_exe_name(&expanded) {
            // Broken shortcut that still points at a .exe must be kept so the
            // status "broken" stays visible; anything else (chm/ico/dll/…) dies.
            return p.source == "start-menu";
        }
        // Icon points at a non-executable or is missing; probe the folder below.
    }

    if let Some(loc) = p.install_location.as_deref() {
        let expanded = expand_env(loc.trim());
        let lp = Path::new(&expanded);
        if is_exe(lp) {
            return true;
        }
        if lp.is_dir() {
            if let Some(exe) = exe_in_dir(lp) {
                p.display_icon = Some(exe);
                return true;
            }
        }
    }
    false
}

/// Collect every installed program from all scan sources and attach icons.
pub fn scan_all() -> Vec<Program> {
    let mut list: Vec<Program> = registry::scan_registry()
        .into_iter()
        .filter_map(|mut p| solidify(&mut p).then_some(p))
        .collect();

    let installed: HashSet<String> = list
        .iter()
        .filter_map(runnable_of)
        .filter(|p| Path::new(p).exists())
        .map(|p| p.trim().to_lowercase())
        .collect();

    list.extend(shortcuts::scan_all(&installed));

    // Shortcuts landed above with their resolved target in display_icon. Only
    // keep those that point at an executable (or a broken .exe hook); drop
    // nothing-but-links like .chm/.ico/.url/.lnk-only or plain folders.
    list = list
        .into_iter()
        .filter_map(|mut p| solidify(&mut p).then_some(p))
        .collect();

    list.extend(store::scan_registry());

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