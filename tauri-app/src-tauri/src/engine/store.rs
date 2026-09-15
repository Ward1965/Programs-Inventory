use std::collections::hash_map::DefaultHasher;
use std::collections::{HashMap, HashSet};
use std::hash::{Hash, Hasher};
use std::io::Read;
use std::path::PathBuf;

use winreg::enums::*;
use winreg::RegKey;

use super::models::Program;

const APPX_BASE: &str = r"Software\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore";
const WINDOWS_APPS: &str = "C:\\Program Files\\WindowsApps";

const DENYLIST: [&str; 11] = [
    "microsoft.vclibs",
    "microsoft.winappruntime",
    "microsoft.windowsappruntime",
    "microsoft.ui.xaml",
    "microsoft.net.native",
    "microsoft.services.store",
    "microsoft.storepurchaseapp",
    "microsoft.desktopappinstaller",
    "microsoft.549981c3f5f10",
    "microsoftwindows.client.cbs",
    "windows.cbspreview",
];

fn parse_full_name(dir: &str) -> Option<(String, &str, &str, &str)> {
    let parts: Vec<&str> = dir.split('_').collect();
    if parts.len() < 5 {
        return None;
    }
    let version = parts[parts.len() - 4];
    let arch = parts[parts.len() - 3];
    let hash = parts[parts.len() - 1];
    let family = parts[..parts.len() - 4].join("_");
    if family.is_empty() {
        return None;
    }
    Some((family, version, arch, hash))
}

fn is_guid(family: &str) -> bool {
    let compact: String = family.chars().filter(|c| c.is_ascii_alphanumeric() || *c == '-').collect();
    compact.trim_start_matches('-').len() >= 32
        && compact.chars().all(|c| c == '-' || c.is_ascii_hexdigit())
}

fn split_camel(word: &str) -> String {
    let chars: Vec<char> = word.chars().collect();
    if chars.len() <= 2 {
        return word.to_string();
    }
    let mut out = String::with_capacity(word.len() + 4);
    for (i, &c) in chars.iter().enumerate() {
        if i > 0
            && c.is_ascii_uppercase()
            && (chars[i - 1].is_ascii_lowercase() || chars[i - 1].is_ascii_digit())
        {
            out.push(' ');
        }
        out.push(c);
    }
    out
}

fn family_to_name(family: &str) -> String {
    let rest = ["Microsoft.", "MicrosoftWindows.", "MicrosoftCorporationII."]
        .iter()
        .find_map(|p| family.strip_prefix(p))
        .unwrap_or(family);
    let display = if let Some((pubp, tail)) = rest.split_once('.') {
        if pubp.len() <= 12
            && pubp.chars().all(|c| c.is_ascii_alphanumeric())
            && pubp.contains(|c: char| c.is_ascii_digit())
        {
            tail
        } else {
            rest
        }
    } else {
        rest
    };
    let cleaned = display.replace(['.', '-', '_'], " ");
    let words: Vec<String> = cleaned
        .split_whitespace()
        .map(split_camel)
        .collect();
    let mut compact: Vec<&str> = words.iter().map(|w| w.as_str()).collect();
    if compact.len() > 1 && compact[0] == compact[compact.len() - 1] && !compact[0].is_empty() {
        compact.truncate(compact.len() - 1);
    }
    if compact.iter().all(|w| w.is_empty()) {
        family.to_string()
    } else {
        compact.join(" ")
    }
}

/// Call `Get-StartApps` via PowerShell and return `(display_name, aumid)` pairs.
/// The AUMID has the form `PackageFamilyName_Hash!AppId` for Store apps.
///
/// Uses `-EncodedCommand` (base64 UTF-16LE) so PowerShell never re-parses the
/// script through the interactive command line, plus a hard timeout so a stuck
/// PowerShell can never block a scan.
fn get_start_apps() -> Vec<(String, String)> {
    const TIMEOUT: std::time::Duration = std::time::Duration::from_secs(8);
    const SCRIPT: &str = r#"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + "`t" + $_.AppID }"#;

    let mut args = std::process::Command::new("powershell.exe");
    args.args([
        "-NoProfile",
        "-NonInteractive",
        "-WindowStyle",
        "Hidden",
        "-EncodedCommand",
        &powershell_encoded(SCRIPT),
    ]);
    args.stdin(std::process::Stdio::null());
    args.stdout(std::process::Stdio::piped());
    args.stderr(std::process::Stdio::null());
    // CREATE_NO_WINDOW + WindowStyle Hidden: this app is GUI (no console), so
    // the PowerShell child must never flash a console window.
    use std::os::windows::process::CommandExt;
    args.creation_flags(0x0800_0000);

    let mut child = match args.spawn() {
        Ok(c) => c,
        Err(_) => return Vec::new(),
    };

    let mut bytes: Vec<u8> = Vec::new();
    if let Some(mut out) = child.stdout.take() {
        // Read the pipe on a background thread fed through a channel. If
        // PowerShell writes partial output then stalls, the blocking read
        // would otherwise hold the scan forever — the timeout below is only
        // reachable while waiting on the channel, never inside a stuck read.
        let (tx, rx) = std::sync::mpsc::channel::<Vec<u8>>();
        let reader = std::thread::spawn(move || {
            let mut buf = [0u8; 8192];
            loop {
                match out.read(&mut buf) {
                    Ok(0) | Err(_) => break,
                    Ok(n) => {
                        if tx.send(buf[..n].to_vec()).is_err() {
                            break;
                        }
                    }
                }
            }
        });

        let deadline = std::time::Instant::now() + TIMEOUT;
        loop {
            let remaining = deadline.saturating_duration_since(std::time::Instant::now());
            match rx.recv_timeout(remaining) {
                Ok(chunk) => bytes.extend_from_slice(&chunk),
                Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {
                    let _ = child.kill();
                    break;
                }
                Err(_) => break, // pipe closed: the reader thread finished
            }
        }
        // Killing the child closes its pipe, so a reader stuck in read()
        // returns promptly and this join never hangs.
        reader.join().ok();
    }
    let _ = child.wait();

    String::from_utf8_lossy(&bytes)
        .lines()
        .filter_map(|line| {
            let (name, aumid) = line.split_once('\t')?;
            let name = name.trim().to_string();
            let aumid = aumid.trim().to_string();
            if name.is_empty() || aumid.is_empty() {
                None
            } else {
                Some((name, aumid))
            }
        })
        .collect()
}

/// Base64-encode a PowerShell script as UTF-16LE for `-EncodedCommand`.
fn powershell_encoded(script: &str) -> String {
    use base64::Engine;
    let u16: Vec<u16> = script.encode_utf16().collect();
    let bytes: Vec<u8> = u16
        .iter()
        .flat_map(|u| u.to_le_bytes())
        .collect();
    base64::engine::general_purpose::STANDARD.encode(bytes)
}

/// From a `Get-StartApps` AUMID, extract the family+hash key used to match
/// HKLM registry entries. Store AUMIDs look like `Family_PublisherHash!AppId`.
fn aumid_folder_key(aumid: &str) -> Option<String> {
    let family_part = aumid.split('!').next()?;
    if !family_part.contains('_') {
        return None;
    }
    Some(family_part.to_lowercase())
}

pub fn scan_registry() -> Vec<Program> {
    let mut seen: HashSet<String> = HashSet::new();
    let mut out = Vec::new();

    let start_apps = get_start_apps();
    let mut name_by_key: HashMap<String, String> = HashMap::new();
    let mut aumid_by_key: HashMap<String, String> = HashMap::new();
    for (name, aumid) in &start_apps {
        if let Some(key) = aumid_folder_key(aumid) {
            name_by_key.entry(key.clone()).or_insert_with(|| name.clone());
            aumid_by_key.entry(key).or_insert_with(|| aumid.clone());
        }
    }

    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    let Ok(base) = hklm.open_subkey(APPX_BASE) else {
        return out;
    };

    for scope in base.enum_keys().flatten() {
        let is_app_scope = scope == "Applications" || scope.starts_with("S-1-");
        if !is_app_scope {
            continue;
        }
        let Ok(scope_key) = base.open_subkey(&scope) else {
            continue;
        };
        for entry in scope_key.enum_keys().flatten() {
            if !seen.insert(entry.clone()) {
                continue;
            }
            let Some((family, version, arch, hash)) = parse_full_name(&entry) else {
                continue;
            };
            if is_guid(&family)
                || DENYLIST.iter().any(|d| family.to_ascii_lowercase().starts_with(d))
            {
                continue;
            }
            let pkg_dir = PathBuf::from(WINDOWS_APPS).join(&entry);
            if !pkg_dir.is_dir() {
                continue;
            }

            let mut h = DefaultHasher::new();
            family.hash(&mut h);
            entry.hash(&mut h);
            let id = format!("store-{:016x}", h.finish());

            let folder_key = format!("{family}_{hash}").to_lowercase();
            let name = name_by_key
                .get(&folder_key)
                .cloned()
                .unwrap_or_else(|| family_to_name(&family));
            let display_icon = aumid_by_key
                .get(&folder_key)
                .map(|aumid| format!("shell:AppsFolder\\{{{aumid}}}"));

            out.push(Program {
                id,
                name,
                publisher: None,
                version: Some(version.to_string()),
                install_location: Some(pkg_dir.to_string_lossy().into_owned()),
                display_icon,
                install_date: None,
                architecture: if arch == "neutral" { None } else { Some(arch.to_string()) },
                source: "store".into(),
                status: "store".into(),
                has_icon: false,
            });
        }
    }

    out.sort_by(|a, b| a.name.to_lowercase().cmp(&b.name.to_lowercase()));
    out
}

pub fn is_windows_apps_target(path: &str) -> bool {
    std::path::Path::new(path)
        .to_string_lossy()
        .to_ascii_lowercase()
        .starts_with(&WINDOWS_APPS.to_ascii_lowercase())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn store_scan_finds_registered_apps() {
        let list = scan_registry();
        eprintln!("Store scan found {} apps", list.len());
        assert!(
            list.iter().all(|p| p.source == "store" && p.status == "store"),
            "every store entry must be tagged as store"
        );
        assert!(
            list.iter().all(|p| !p.name.trim().is_empty()),
            "every store entry needs a display name"
        );
    }

    #[test]
    fn parses_package_full_names() {
        assert_eq!(
            parse_full_name("Microsoft.WindowsCalculator_2021.2607.0.0_neutral_~_8wekyb3d8bbwe")
                .map(|(f, v, a, h)| (f, v.to_string(), a.to_string(), h.to_string())),
            Some((
                "Microsoft.WindowsCalculator".to_string(),
                "2021.2607.0.0".to_string(),
                "neutral".to_string(),
                "8wekyb3d8bbwe".to_string()
            ))
        );
        let (f, _, _, hash) = parse_full_name("Microsoft.WinAppRuntime.DDLM.2000.609.1413.0-x6-p1_2000.609.1413.0_x64__8wekyb3d8bbwe")
            .expect("runtime name parses");
        assert_eq!(f, "Microsoft.WinAppRuntime.DDLM.2000.609.1413.0-x6-p1");
        assert_eq!(hash, "8wekyb3d8bbwe");
    }

    #[test]
    fn cleans_family_names() {
        assert_eq!(family_to_name("Microsoft.WindowsCalculator"), "Windows Calculator");
        assert_eq!(family_to_name("5319275A.WhatsAppDesktop"), "Whats App Desktop");
        assert_eq!(family_to_name("Clipchamp.Clipchamp"), "Clipchamp");
        assert_eq!(family_to_name("MicrosoftWindows.Client.CBS"), "Client CBS");
    }

    #[test]
    fn aumid_key_extraction() {
        assert_eq!(
            aumid_folder_key("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            Some("microsoft.windowscalculator_8wekyb3d8bbwe".into())
        );
        assert_eq!(aumid_folder_key("Firefox"), None);
    }
}
