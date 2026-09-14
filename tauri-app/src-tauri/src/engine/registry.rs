use std::collections::hash_map::DefaultHasher;
use std::hash::{Hash, Hasher};

use winreg::enums::*;
use winreg::RegKey;

use super::models::Program;

const UNINSTALL: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
const WOW64_UNINSTALL: &str = r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

fn read_value(reg: &RegKey, name: &str) -> Option<String> {
    match reg.get_value::<String, _>(name) {
        Ok(v) => Some(v),
        Err(_) => reg
            .get_raw_value(name)
            .ok()
            .map(|rv| String::from_utf8_lossy(rv.bytes.as_slice()).into_owned())
            .map(|s| s.trim_end_matches('\0').to_string()),
    }
    .filter(|s| !s.trim().is_empty())
}

fn is_system_component(reg: &RegKey) -> bool {
    reg.get_value::<u32, _>("SystemComponent").unwrap_or(0) == 1
}

// Windows Update / runtime entries pollute the inventory; skip them.
fn is_update_entry(reg: &RegKey) -> bool {
    if reg.get_raw_value("ParentKeyName").is_ok() {
        return true;
    }
    match reg.get_value::<String, _>("ReleaseType") {
        Ok(rt) => {
            let rt = rt.to_ascii_lowercase();
            rt.contains("security update")
                || rt.contains("update")
                || rt.contains("hotfix")
                || rt.contains("language pack")
        }
        Err(_) => false,
    }
}

fn read_program(reg: &RegKey, name: &str, arch: Option<&str>) -> Option<Program> {
    let display_name = read_value(reg, "DisplayName")?;
    if is_system_component(reg) || is_update_entry(reg) {
        return None;
    }

    let publisher = read_value(reg, "Publisher");
    let install_location = read_value(reg, "InstallLocation");
    let display_icon = read_value(reg, "DisplayIcon");
    if super::is_system_noise(
        &display_name,
        publisher.as_deref().unwrap_or(""),
        install_location.as_deref().unwrap_or(""),
        display_icon.as_deref().unwrap_or(""),
    ) {
        return None;
    }

    let mut h = DefaultHasher::new();
    display_name.hash(&mut h);
    name.hash(&mut h);
    let id = format!("reg-{:016x}", h.finish());

    Some(Program {
        id,
        name: display_name,
        publisher,
        version: read_value(reg, "DisplayVersion"),
        install_location,
        display_icon,
        install_date: read_value(reg, "InstallDate"),
        architecture: arch.map(String::from),
        source: "registry".into(),
        status: "installed".into(),
        has_icon: false,
    })
}

fn scan_hive(root: &RegKey, path: &str, arch: Option<&str>, out: &mut Vec<Program>) {
    let Ok(base) = root.open_subkey(path) else {
        return;
    };
    for sub in base.enum_keys().flatten() {
        let Ok(entry) = base.open_subkey(&sub) else {
            continue;
        };
        if let Some(p) = read_program(&entry, &sub, arch) {
            out.push(p);
        }
    }
}

pub fn scan_registry() -> Vec<Program> {
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let mut all = Vec::with_capacity(2048);

    scan_hive(&hklm, UNINSTALL, None, &mut all);
    scan_hive(&hklm, WOW64_UNINSTALL, Some("x86"), &mut all);
    scan_hive(&hkcu, UNINSTALL, None, &mut all);

    all.sort_by(|a, b| a.name.to_lowercase().cmp(&b.name.to_lowercase()));
    all
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn registry_scan_finds_real_programs() {
        let list = scan_registry();
        assert!(!list.is_empty(), "expected at least one installed program");
        assert!(
            list.iter().all(|p| !p.name.trim().is_empty()),
            "every entry must have a display name"
        );
        eprintln!("Registry scan found {} programs", list.len());
    }
}