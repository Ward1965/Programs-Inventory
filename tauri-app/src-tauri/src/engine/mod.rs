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

/// Collect every installed program from all scan sources and attach icons.
pub fn scan_all() -> Vec<Program> {
    let mut list = registry::scan_registry();

    let installed: HashSet<String> = list
        .iter()
        .filter_map(runnable_of)
        .filter(|p| Path::new(p).exists())
        .map(|p| p.trim().to_lowercase())
        .collect();

    list.extend(shortcuts::scan_all(&installed));
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
}