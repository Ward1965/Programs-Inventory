use std::collections::hash_map::DefaultHasher;
use std::ffi::OsStr;
use std::hash::{Hash, Hasher};
use std::os::raw::c_void;
use std::os::windows::ffi::OsStrExt;
use std::path::{Path, PathBuf};

use super::icons::expand_env;
use super::models::Program;

/* ── COM: IShellLink — resolve a .lnk's target via the shell ── */

#[repr(C)]
#[derive(Clone, Copy)]
struct Guid {
    data1: u32,
    data2: u16,
    data3: u16,
    data4: [u8; 8],
}

#[rustfmt::skip]
const CLSID_SHELL_LINK: Guid = Guid {
    data1: 0x0002_1401,
    data2: 0x0000,
    data3: 0x0000,
    data4: [0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46],
};

#[rustfmt::skip]
const IID_ISHELL_LINK_W: Guid = Guid {
    data1: 0x0002_14F9,
    data2: 0x0000,
    data3: 0x0000,
    data4: [0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46],
};

#[rustfmt::skip]
const IID_IPERSIST_FILE: Guid = Guid {
    data1: 0x0000_010B,
    data2: 0x0000,
    data3: 0x0000,
    data4: [0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46],
};

const CLSCTX_INPROC_SERVER: u32 = 0x1;
const SLGP_RAWPATH: u32 = 0x4;
const STGM_READ: u32 = 0x0;
const COINIT_APARTMENTTHREADED: u32 = 0x2;

type ReleaseFn = unsafe extern "system" fn(*mut c_void) -> u32;
type QueryInterfaceFn = unsafe extern "system" fn(*mut c_void, *const Guid, *mut *mut c_void) -> i32;
type LoadFn = unsafe extern "system" fn(*mut c_void, *const u16, u32) -> i32;
type GetPathFn = unsafe extern "system" fn(*mut c_void, *mut u16, i32, *mut c_void, u32) -> i32;

#[link(name = "ole32")]
extern "system" {
    fn CoInitializeEx(pv_reserved: *const c_void, dw_coinit: u32) -> i32;
    fn CoCreateInstance(
        rclsid: *const Guid,
        p_unk_outter: *const c_void,
        dw_cls_context: u32,
        riid: *const Guid,
        ppv: *mut *mut c_void,
    ) -> i32;
}

/// Resolve the absolute target path stored inside a `.lnk` file.
fn resolve_link(path: &Path) -> Option<String> {
    unsafe {
        CoInitializeEx(std::ptr::null(), COINIT_APARTMENTTHREADED);

        let wide: Vec<u16> = OsStr::new(path).encode_wide().chain(std::iter::once(0)).collect();
        let mut shell: *mut c_void = std::ptr::null_mut();
        let hr = CoCreateInstance(
            &CLSID_SHELL_LINK,
            std::ptr::null(),
            CLSCTX_INPROC_SERVER,
            &IID_ISHELL_LINK_W,
            &mut shell,
        );
        if hr != 0 || shell.is_null() {
            return None;
        }
        let vtbl = *(shell as *const *const usize);

        // Load the .lnk file contents into the ShellLink object (IPersistFile).
        let query_interface: QueryInterfaceFn = std::mem::transmute(std::ptr::read(vtbl.add(0)));
        let release: ReleaseFn = std::mem::transmute(std::ptr::read(vtbl.add(2)));
        let mut persist: *mut c_void = std::ptr::null_mut();
        if query_interface(shell, &IID_IPERSIST_FILE, &mut persist) != 0 || persist.is_null() {
            release(shell);
            return None;
        }
        let pvt = *(persist as *const *const usize);
        let load: LoadFn = std::mem::transmute(std::ptr::read(pvt.add(5)));
        let prelease: ReleaseFn = std::mem::transmute(std::ptr::read(pvt.add(2)));
        let hr_load = load(persist, wide.as_ptr(), STGM_READ);
        prelease(persist);
        if hr_load != 0 {
            release(shell);
            return None;
        }

        let get_path: GetPathFn = std::mem::transmute(std::ptr::read(vtbl.add(3)));
        let mut buf = vec![0u16; 1024];
        let hr_get = get_path(shell, buf.as_mut_ptr(), 1024, std::ptr::null_mut(), SLGP_RAWPATH);
        release(shell);

        if hr_get != 0 {
            return None;
        }
        let end = buf.iter().position(|&u| u == 0).unwrap_or(buf.len());
        Some(String::from_utf16_lossy(&buf[..end]))
    }
}

fn normalize(p: &str) -> String {
    p.trim().to_lowercase()
}

fn walk(dir: &Path, out: &mut Vec<PathBuf>, depth: usize) {
    if depth > 12 {
        return;
    }
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for e in entries.flatten() {
        let pt = e.path();
        if pt.is_dir() {
            walk(&pt, out, depth + 1);
        } else if pt.extension().map(|x| x.eq_ignore_ascii_case("lnk")).unwrap_or(false) {
            out.push(pt);
        }
    }
}

/// Scan the Start Menu (user + machine) and return `.lnk` shortcuts that point
/// at programs which are NOT in the installed registry set.
pub fn scan_all(installed: &std::collections::HashSet<String>) -> Vec<Program> {
    let mut links: Vec<PathBuf> = Vec::new();
    for base in [
        std::env::var("APPDATA")
            .ok()
            .map(|d| PathBuf::from(d).join(r"Microsoft\Windows\Start Menu\Programs")),
        std::env::var("ProgramData")
            .ok()
            .map(|d| PathBuf::from(d).join(r"Microsoft\Windows\Start Menu\Programs")),
    ]
    .into_iter()
    .flatten()
    {
        walk(&base, &mut links, 0);
    }
    links.sort();

    let mut out = Vec::new();
    for link in links {
        let Some(target) = resolve_link(&link) else {
            continue;
        };
        if installed.contains(&normalize(&target)) {
            continue; // already installed and registered
        }
        if super::store::is_windows_apps_target(&target) {
            continue; // Store apps are listed under their own category
        }
        let display_target = expand_env(&target);
        let key = link.file_stem().map(|s| s.to_string_lossy().into_owned())
            .unwrap_or_else(|| link.to_string_lossy().into_owned());
        let target_key = format!("{}|{}", key, normalize(&target));

        let mut h = DefaultHasher::new();
        target_key.hash(&mut h);
        let id = format!("sm-{:016x}", h.finish());

        let exists = Path::new(&display_target).exists();
        let status = if exists { "shortcut" } else { "broken" };

        out.push(Program {
            id,
            name: key,
            publisher: None,
            version: None,
            install_location: None,
            display_icon: Some(display_target),
            install_date: None,
            architecture: None,
            source: "start-menu".into(),
            status: status.into(),
            has_icon: false,
        });
    }
    out
}