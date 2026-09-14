use std::collections::HashMap;
use std::ffi::OsStr;
use std::os::raw::c_void;
use std::os::windows::ffi::OsStrExt;
use std::path::Path;

use super::models::Program;

/* ── Win32 FFI (version-proof, no binding-crate dependency) ── */

#[repr(C)]
struct SHFILEINFOW {
    h_icon: *mut c_void,
    i_icon: i32,
    dw_attributes: u32,
    sz_display_name: [u16; 260],
    sz_type_name: [u16; 80],
}

#[repr(C)]
struct ICONINFO {
    f_icon: i32,
    x_hotspot: u32,
    y_hotspot: u32,
    hbm_mask: *mut c_void,
    hbm_color: *mut c_void,
}

#[repr(C)]
struct BITMAP {
    bm_type: i32,
    bm_width: i32,
    bm_height: i32,
    bm_width_bytes: i32,
    bm_planes: u16,
    bm_bits_pixel: u16,
    bm_bits: *mut c_void,
}

#[repr(C)]
struct BITMAPINFOHEADER {
    bi_size: u32,
    bi_width: i32,
    bi_height: i32,
    bi_planes: u16,
    bi_bit_count: u16,
    bi_compression: u32,
    bi_size_image: u32,
    bi_x_pels_per_meter: i32,
    bi_y_pels_per_meter: i32,
    bi_clr_used: u32,
    bi_clr_important: u32,
}

#[repr(C)]
struct BITMAPINFO {
    bmi_header: BITMAPINFOHEADER,
    bmi_colors: [u32; 1],
}

const SHGFI_ICON: u32 = 0x0000_0100;
const SHGFI_EXTRALARGEICON: u32 = 0x0000_0020;
const SHGFI_USEFILEATTRIBUTES: u32 = 0x0000_0010;
const BI_RGB: u32 = 0;
const DIB_RGB_COLORS: u32 = 0;
const SRCCOPY: u32 = 0x00CC_0020;
const COINIT_APARTMENTTHREADED: u32 = 0x2;

#[link(name = "shell32")]
extern "system" {
    fn SHGetFileInfoW(
        psz_path: *const u16,
        dw_file_attributes: u32,
        psfi: *mut SHFILEINFOW,
        cb_file_info: u32,
        u_flags: u32,
    ) -> usize;
}

#[link(name = "user32")]
extern "system" {
    fn GetIconInfo(h_icon: *mut c_void, piconinfo: *mut ICONINFO) -> i32;
    fn DestroyIcon(h_icon: *mut c_void) -> i32;
}

#[link(name = "gdi32")]
extern "system" {
    fn CreateCompatibleDC(hdc: *mut c_void) -> *mut c_void;
    fn DeleteDC(hdc: *mut c_void) -> i32;
    fn CreateDIBSection(
        hdc: *mut c_void,
        pbmi: *const BITMAPINFO,
        usage: u32,
        ppv_bits: *mut *mut c_void,
        h_section: *mut c_void,
        offset: u32,
    ) -> *mut c_void;
    fn SelectObject(hdc: *mut c_void, h: *mut c_void) -> *mut c_void;
    fn BitBlt(
        hdc_dest: *mut c_void,
        x: i32,
        y: i32,
        w: i32,
        h: i32,
        hdc_src: *mut c_void,
        x1: i32,
        y1: i32,
        rop: u32,
    ) -> i32;
    fn DeleteObject(h: *mut c_void) -> i32;
    fn GetObjectW(h: *mut c_void, c: i32, pv: *mut c_void) -> i32;
}

#[link(name = "user32")]
extern "system" {
    fn ExtractIconExW(
        lpsz_file: *const u16,
        n_icon_index: i32,
        phicon_large: *mut *mut c_void,
        phicon_small: *mut *mut c_void,
        n_icons: u32,
    ) -> u32;
}

#[link(name = "ole32")]
extern "system" {
    fn CoInitializeEx(pv_reserved: *const c_void, dw_coinit: u32) -> i32;
    fn CoUninitialize();
}

#[link(name = "shell32")]
extern "system" {
    fn SHCreateItemFromParsingName(
        psz_path: *const u16,
        pbc: *const c_void,
        riid: *const Guid,
        ppv: *mut *mut c_void,
    ) -> i32;
}

#[rustfmt::skip]
const CLSID_IID_ISHELL_ITEM: Guid = Guid {
    data1: 0x4382_6D1E,
    data2: 0xE718,
    data3: 0x42EE,
    data4: [0xBC, 0x55, 0xA1, 0xE2, 0x61, 0xC3, 0x7B, 0xFE],
};

#[rustfmt::skip]
const CLSID_IID_ISHELL_FOLDER: Guid = Guid {
    data1: 0x0002_14E6,
    data2: 0x0000,
    data3: 0x0000,
    data4: [0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46],
};

#[rustfmt::skip]
const CLSID_IID_ISHELL_ITEM_IMAGE_FACTORY: Guid = Guid {
    data1: 0xBCC1_8B79,
    data2: 0xBA16,
    data3: 0x442F,
    data4: [0x80, 0xC4, 0x8A, 0x59, 0xC3, 0x0C, 0x46, 0x3B],
};

#[repr(C)]
struct Guid {
    data1: u32,
    data2: u16,
    data3: u16,
    data4: [u8; 8],
}

#[repr(C)]
struct Size {
    cx: i32,
    cy: i32,
}

const SIIGBF_ICONONLY: u32 = 0x20;
const SIIGBF_BIGGERSIZEOK: u32 = 0x01;

const SHGDN_FORPARSING: u32 = 0x8000;
const SHCONTF_FOLDERS: u32 = 0x20;
const SHCONTF_NONFOLDERS: u32 = 0x2;
const SHCONTF_INCLUDEHIDDEN: u32 = 0x80;
const STRRET_WSTR: u32 = 0;

#[link(name = "shell32")]
extern "system" {
    fn SHParseDisplayName(
        psz_path: *const u16,
        pbc: *const c_void,
        ppidl: *mut *mut c_void,
        sfgao_in: u32,
        psfgao_out: *mut u32,
    ) -> i32;
    fn SHBindToObject(
        psf: *const c_void,
        pidl: *const c_void,
        pbc: *const c_void,
        riid: *const Guid,
        ppv: *mut *mut c_void,
    ) -> i32;
    fn SHCreateItemWithParent(
        pidl_parent: *const c_void,
        psf_parent: *const c_void,
        pidl: *const c_void,
        riid: *const Guid,
        ppv: *mut *mut c_void,
    ) -> i32;
}

#[link(name = "ole32")]
extern "system" {
    fn CoTaskMemFree(pv: *mut c_void);
}

/// Invoke an IShellFolder or IEnumIDList vtable method: `(Release, EnumObjects)`
/// or `(Next, Skip..)`. Offset 2 is Release on every IUnknown vtable.
unsafe fn shell_vtable(base: *mut c_void) -> *const usize {
    *(base as *const *const usize)
}

type ShellReleaseFn = unsafe extern "system" fn(*mut c_void) -> u32;
type EnumObjectsFn = unsafe extern "system" fn(*mut c_void, *mut c_void, u32, *mut *mut c_void) -> i32;
type GetDisplayNameFn =
    unsafe extern "system" fn(*mut c_void, *const c_void, u32, *mut StrRet) -> i32;
type EnumNextFn =
    unsafe extern "system" fn(*mut c_void, u32, *mut *mut c_void, *mut u32) -> i32;
type QueryInterfaceFn = unsafe extern "system" fn(*mut c_void, *const Guid, *mut *mut c_void) -> i32;

#[repr(C)]
struct StrRet {
    u_type: u32,
    // The union (with LPWSTR pOleStr) is 8-byte aligned on x64 → starts at
    // offset 8. Reproduced exactly so shell32 writes where we read.
    _pad: u32,
    union_bytes: [u8; 260],
}

/* ── Icon resolution ───────────────────────────────────── */

/// Expand `%VAR%` tokens found in registry paths to their real values.
pub(crate) fn expand_env(path: &str) -> String {
    let mut out = String::with_capacity(path.len() + 16);
    let mut rest = path;
    while let Some(start) = rest.find('%') {
        out.push_str(&rest[..start]);
        let after = &rest[start + 1..];
        if let Some(end) = after.find('%') {
            let name = &after[..end];
            if let Ok(v) = std::env::var(name) {
                out.push_str(&v);
                rest = &after[end + 1..];
                continue;
            }
        }
        out.push('%');
        rest = after;
    }
    out.push_str(rest);
    out
}

/// Resolve the file (exe/dll/ico) whose icon should represent this program.
///
/// The returned string may keep the `,<index>` suffix so the shell picks the
/// correct icon inside shared resources like `imageres.dll`.
pub fn icon_source(p: &Program) -> Option<String> {
    let candidate = p.display_icon.as_deref().unwrap_or("").trim();
    if !candidate.is_empty() {
        let expanded = expand_env(candidate);
        let base = expanded
            .rsplit_once(',')
            .map(|(b, _)| b.trim().trim_matches('"').trim())
            .unwrap_or(expanded.trim());
        if base.starts_with("shell:") {
            return Some(expanded);
        }
        let lower = base.to_lowercase();
        let ok_ext = lower.ends_with(".exe") || lower.ends_with(".dll") || lower.ends_with(".ico");
        if ok_ext && Path::new(base).exists() {
            return Some(expanded);
        }
    }
    // Fall back to probing the install folder for the first executable.
    if let Some(loc) = &p.install_location {
        let loc_clean = loc.trim().trim_matches('"').trim();
        if let Ok(entries) = std::fs::read_dir(loc_clean) {
            for e in entries.flatten() {
                let pt = e.path();
                let name = pt.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
                let is_installer = name.starts_with("unins") || name.starts_with("uninstall");
                if !is_installer
                    && pt.extension().map(|x| x.eq_ignore_ascii_case("exe")).unwrap_or(false) {
                    return Some(pt.to_string_lossy().into_owned());
                }
            }
        }
    }
    None
}

/// Extract the shell icon of a display string (optionally `path,index`) as
/// (width, height, RGBA bytes). Serialized with a global lock: the shell/GDI
/// icon cache is not safe to hit from many threads at once.
fn extract_rgba(display: &str) -> Option<(u32, u32, Vec<u8>)> {
    shell_extract(display, false)
}

/// File-type icon via SHGFI_USEFILEATTRIBUTES — works even when the file is
/// missing on disk, giving every entry a usable icon.
fn filetype_rgba(display: &str) -> Option<(u32, u32, Vec<u8>)> {
    shell_extract(display, true)
}

/// The shared "generic application" icon, decoded once and reused for every
/// entry that cannot provide a real one.
fn generic_png_bytes() -> Option<Vec<u8>> {
    static GEN: std::sync::OnceLock<Option<Vec<u8>>> = std::sync::OnceLock::new();
    GEN.get_or_init(|| {
        filetype_rgba("app.exe")
            .map(|(w, h, rgba)| {
                image::RgbaImage::from_raw(w, h, rgba)
                    .and_then(|img| {
                        let mut buf = std::io::Cursor::new(Vec::new());
                        img.write_to(&mut buf, image::ImageFormat::Png).ok()?;
                        Some(buf.into_inner())
                    })
            })
            .flatten()
    })
    .clone()
}

/// Extract the shell icon of a display string (optionally `path,index`) as
/// (width, height, RGBA bytes). The shell icon cache is not safe to hit from
/// many threads at once, so the Win32 calls are guarded by a global mutex;
/// callers may still parallelize the PNG encoding that happens afterward.
fn shell_extract(display: &str, use_file_attributes: bool) -> Option<(u32, u32, Vec<u8>)> {
    static LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());
    let _guard = match LOCK.lock() {
        Ok(g) => g,
        Err(p) => p.into_inner(),
    };

    extract_unsafe(display, use_file_attributes)
}

fn extract_unsafe(display: &str, use_file_attributes: bool) -> Option<(u32, u32, Vec<u8>)> {
    #[allow(clippy::redundant_closure)]
    let (path, index, cannot_parse_index) = if use_file_attributes {
        (display.split(',').next().unwrap_or(display), None, true)
    } else {
        match display.rsplit_once(',') {
            Some((base, idx)) if !base.is_empty() && idx.trim().chars().all(|c| c.is_ascii_digit() || c == '-') => {
                (base.trim(), idx.trim().parse::<i32>().ok(), false)
            }
            _ => (display, None, false),
        }
    };
    let _ = cannot_parse_index;

    unsafe {
        // Shell icon loading needs COM initialized on the calling thread.
        let _ = CoInitializeEx(std::ptr::null(), COINIT_APARTMENTTHREADED);

        let wide: Vec<u16> = OsStr::new(path).encode_wide().chain(std::iter::once(0)).collect();
        let h_icon: *mut c_void;

        if let Some(idx) = index {
            // Shared resources (e.g. `imageres.dll,-200`) need ExtractIconExW,
            // which honors a specific resource index/identifier.
            let mut hicon_large: *mut c_void = std::ptr::null_mut();
            let mut got = ExtractIconExW(wide.as_ptr(), idx, &mut hicon_large, std::ptr::null_mut(), 1);
            if got == 0 || hicon_large.is_null() {
                // The shell icon cache occasionally still warms up; retry once.
                std::thread::sleep(std::time::Duration::from_millis(120));
                got = ExtractIconExW(wide.as_ptr(), idx, &mut hicon_large, std::ptr::null_mut(), 1);
            }
            if got == 0 || hicon_large.is_null() {
                CoUninitialize();
                return None;
            }
            h_icon = hicon_large;
        } else {
            let flags = SHGFI_ICON
                | SHGFI_EXTRALARGEICON
                | if use_file_attributes { SHGFI_USEFILEATTRIBUTES } else { 0 };
            let mut info: SHFILEINFOW = std::mem::zeroed();
            let size = std::mem::size_of::<SHFILEINFOW>() as u32;
            let mut ret = SHGetFileInfoW(wide.as_ptr(), 0, &mut info, size, flags);
            if ret == 0 {
                // First lookups can miss while the shell populates its cache.
                std::thread::sleep(std::time::Duration::from_millis(150));
                ret = SHGetFileInfoW(wide.as_ptr(), 0, &mut info, size, flags);
            }
            if ret == 0 {
                CoUninitialize();
                return None;
            }
            h_icon = info.h_icon;
        }
        if h_icon.is_null() {
            CoUninitialize();
            return None;
        }

        let mut ii: ICONINFO = std::mem::zeroed();
        if GetIconInfo(h_icon, &mut ii) == 0 {
            DestroyIcon(h_icon);
            CoUninitialize();
            return None;
        }
        if ii.hbm_color.is_null() {
            DeleteObject(ii.hbm_mask);
            DeleteObject(ii.hbm_color);
            DestroyIcon(h_icon);
            CoUninitialize();
            return None;
        }
        let out = bitmap_to_rgba(ii.hbm_color);
        DeleteObject(ii.hbm_mask);
        DestroyIcon(h_icon);
        CoUninitialize();
        out
    }
}

/// Blit an HBITMAP (BGRA, bottom-up aware) into RGBA bytes.
fn bitmap_to_rgba(hbm: *mut c_void) -> Option<(u32, u32, Vec<u8>)> {
    unsafe {
        let mut bmp: BITMAP = std::mem::zeroed();
        if GetObjectW(hbm, std::mem::size_of::<BITMAP>() as i32, &mut bmp as *mut _ as *mut c_void) == 0 {
            DeleteObject(hbm);
            return None;
        }

        let w = bmp.bm_width.max(0) as u32;
        let h = bmp.bm_height.max(0) as u32;
        if w == 0 || h == 0 || w * h > 4096 * 4096 {
            DeleteObject(hbm);
            return None;
        }

        // DIB section (32bpp, top-down) as the blit target.
        let mut bmi: BITMAPINFO = std::mem::zeroed();
        bmi.bmi_header.bi_size = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmi_header.bi_width = w as i32;
        bmi.bmi_header.bi_height = -(h as i32);
        bmi.bmi_header.bi_planes = 1;
        bmi.bmi_header.bi_bit_count = 32;
        bmi.bmi_header.bi_compression = BI_RGB;

        let mut bits: *mut c_void = std::ptr::null_mut();
        let hdc_screen = CreateCompatibleDC(std::ptr::null_mut());
        let hdc_icon = CreateCompatibleDC(std::ptr::null_mut());
        let hbmp_dib = CreateDIBSection(hdc_screen, &bmi, DIB_RGB_COLORS, &mut bits, std::ptr::null_mut(), 0);
        if hbmp_dib.is_null() || bits.is_null() {
            DeleteObject(hbm);
            DeleteDC(hdc_screen);
            DeleteDC(hdc_icon);
            return None;
        }

        SelectObject(hdc_icon, hbm);
        SelectObject(hdc_screen, hbmp_dib);
        BitBlt(hdc_screen, 0, 0, w as i32, h as i32, hdc_icon, 0, 0, SRCCOPY);

        let mut rgba = vec![0u8; (w * h * 4) as usize];
        std::ptr::copy_nonoverlapping(bits as *const u8, rgba.as_mut_ptr(), rgba.len());
        // DIB rows are BGRA (top-down): swap to RGBA for PNG.
        for px in rgba.chunks_exact_mut(4) {
            px.swap(0, 2);
        }

        DeleteObject(hbmp_dib);
        DeleteObject(hbm);
        DeleteDC(hdc_icon);
        DeleteDC(hdc_screen);

        Some((w, h, rgba))
    }
}

/// Fetch the icon of a shell namespace item (Store apps, etc.) via
/// `IShellItemImageFactory`. Accepts an AppsFolder parsing name such as
/// `shell:AppsFolder\{AUMID}`, which is exactly what store entries carry.
fn shell_item_icon(name: &str) -> Option<(u32, u32, Vec<u8>)> {
    unsafe {
        let _ = CoInitializeEx(std::ptr::null(), COINIT_APARTMENTTHREADED);
        let wide: Vec<u16> = OsStr::new(name).encode_wide().chain(std::iter::once(0)).collect();

        let mut item: *mut c_void = std::ptr::null_mut();
        let hr = SHCreateItemFromParsingName(
            wide.as_ptr(),
            std::ptr::null(),
            &CLSID_IID_ISHELL_ITEM,
            &mut item,
        );
        if hr != 0 || item.is_null() {
            return None;
        }
        let vtbl = *(item as *const *const usize);
        let query_interface: unsafe extern "system" fn(*mut c_void, *const Guid, *mut *mut c_void) -> i32 =
            std::mem::transmute(std::ptr::read(vtbl.add(0)));
        let release: unsafe extern "system" fn(*mut c_void) -> u32 =
            std::mem::transmute(std::ptr::read(vtbl.add(2)));

        let mut img_factory: *mut c_void = std::ptr::null_mut();
        let hr = query_interface(item, &CLSID_IID_ISHELL_ITEM_IMAGE_FACTORY, &mut img_factory);
        release(item);
        if hr != 0 || img_factory.is_null() {
            return None;
        }
        let f_vtbl = *(img_factory as *const *const usize);
        let factory_release: unsafe extern "system" fn(*mut c_void) -> u32 =
            std::mem::transmute(std::ptr::read(f_vtbl.add(2)));
        let get_image: unsafe extern "system" fn(
            *mut c_void,
            Size,
            u32,
            *mut *mut c_void,
        ) -> i32 = std::mem::transmute(std::ptr::read(f_vtbl.add(3)));

let mut hbm: *mut c_void = std::ptr::null_mut();
        let hr = get_image(
            img_factory,
            Size { cx: 48, cy: 48 },
            SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK,
            &mut hbm,
        );
        factory_release(img_factory);
        if hr != 0 || hbm.is_null() {
            return None;
        }
        bitmap_to_rgba(hbm)
    }
}

/// Enumerate `shell:AppsFolder` through IShellFolder and extract the real icon
/// of every child via IShellItemImageFactory in a single pass. Returns a map of
/// AUMID → (w, h, RGBA) so callers can match store entries without parsing
/// the AppsFolder namespace repeatedly.
///
/// This is the path Explorer itself uses: binding the AppsFolder *contains*
/// the child items, and each child yields a distinct per-app icon.
pub fn apps_folder_icons() -> HashMap<String, (u32, u32, Vec<u8>)> {
    let mut out = HashMap::new();
    unsafe {
        let _ = CoInitializeEx(std::ptr::null(), COINIT_APARTMENTTHREADED);
        let wide: Vec<u16> = OsStr::new("shell:AppsFolder")
            .encode_wide()
            .chain(std::iter::once(0))
            .collect();

        let mut pidl_root: *mut c_void = std::ptr::null_mut();
        let mut sfgao: u32 = 0;
        if SHParseDisplayName(wide.as_ptr(), std::ptr::null(), &mut pidl_root, 0, &mut sfgao) != 0
            || pidl_root.is_null()
        {
            return out;
        }

        let mut folder: *mut c_void = std::ptr::null_mut();
        if SHBindToObject(
            std::ptr::null(),
            pidl_root,
            std::ptr::null(),
            &CLSID_IID_ISHELL_FOLDER,
            &mut folder,
        ) != 0
            || folder.is_null()
        {
            return out;
        }
        let f_vtbl = shell_vtable(folder);
        let f_release: ShellReleaseFn = std::mem::transmute(std::ptr::read(f_vtbl.add(2)));
        let f_enum: EnumObjectsFn = std::mem::transmute(std::ptr::read(f_vtbl.add(4)));
        let f_dispname: GetDisplayNameFn = std::mem::transmute(std::ptr::read(f_vtbl.add(11)));

        let mut enum_list: *mut c_void = std::ptr::null_mut();
        if f_enum(folder, std::ptr::null_mut(), SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN, &mut enum_list)
            != 0
            || enum_list.is_null()
        {
            f_release(folder);
            return out;
        }
        let e_vtbl = shell_vtable(enum_list);
        let e_release: ShellReleaseFn = std::mem::transmute(std::ptr::read(e_vtbl.add(2)));
        let e_next: EnumNextFn = std::mem::transmute(std::ptr::read(e_vtbl.add(3)));

        loop {
            let mut pidl_child: *mut c_void = std::ptr::null_mut();
            let mut fetched: u32 = 0;
            if e_next(enum_list, 1, &mut pidl_child, &mut fetched) != 0 || fetched == 0 {
                break;
            }
            if pidl_child.is_null() {
                continue;
            }

            // Parsing name → `shell:AppsFolder\{AUMID}` (or a bare AUMID).
            let mut strret: StrRet = std::mem::zeroed();
            if f_dispname(folder, pidl_child, SHGDN_FORPARSING, &mut strret) == 0
                && strret.u_type == STRRET_WSTR
            {
                let p_ole_str: *mut u16 =
                    std::ptr::read_unaligned(strret.union_bytes.as_ptr() as *const *mut u16);
                if !p_ole_str.is_null() {
                    let name = wide_string_from_ptr(p_ole_str);
                    if let Some(aumid) = extract_aumid(&name) {
                        if let Some(rgba) = folders_child_icon(folder, pidl_child, pidl_root) {
                            out.insert(aumid, rgba);
                        }
                    }
                    CoTaskMemFree(p_ole_str.cast());
                }
            }

            // Free the child PIDL (ILFree == CoTaskMemFree).
            CoTaskMemFree(pidl_child);
        }

        e_release(enum_list);
        f_release(folder);
        CoTaskMemFree(pidl_root);
    }
    out
}

/// Read a UTF-16 NUL-terminated string from a raw LPWSTR.
unsafe fn wide_string_from_ptr(ptr: *mut u16) -> String {
    if ptr.is_null() {
        return String::new();
    }
    let mut len = 0usize;
    while *ptr.add(len) != 0 {
        len += 1;
    }
    String::from_utf16_lossy(std::slice::from_raw_parts(ptr, len))
}

/// Pull `{AUMID}` out of a parsing name like `shell:AppsFolder\{X}` or `X`.
fn extract_aumid(name: &str) -> Option<String> {
    let last = name.rsplit('\\').next().unwrap_or(name);
    let trimmed = last.trim_start_matches('{').trim_end_matches('}');
    if trimmed.is_empty() || trimmed.contains('\0') {
        return None;
    }
    Some(trimmed.to_string())
}

/// Extract the real per-item icon of one AppsFolder child via
/// `IShellFolder::GetUIObjectOf(IID_IExtractIconW)` then `Extract(nIconSize=32)`
/// — the mechanism Explorer uses for Store app icons.
unsafe fn folders_child_icon(folder: *mut c_void, pidl_child: *mut c_void, pidl_root: *mut c_void) -> Option<(u32, u32, Vec<u8>)> {
    // Bind the child PIDL into a real IShellItem, then QI IShellItemImageFactory.
    // Critical: use SIIGBF_ICONONLY|SIIGBF_BIGGERSIZEOK — adding RESIZETOFIT
    // makes GetImage fail with E_FAIL for AppsFolder children.
    let mut item: *mut c_void = std::ptr::null_mut();
    if SHCreateItemWithParent(
        pidl_root,
        folder,
        pidl_child,
        &CLSID_IID_ISHELL_ITEM,
        &mut item,
    ) != 0
        || item.is_null()
    {
        return None;
    }
    let vtbl = shell_vtable(item);
    let qi: QueryInterfaceFn = std::mem::transmute(std::ptr::read(vtbl.add(0)));
    let rel: ShellReleaseFn = std::mem::transmute(std::ptr::read(vtbl.add(2)));

    let mut fac: *mut c_void = std::ptr::null_mut();
    if qi(item, &CLSID_IID_ISHELL_ITEM_IMAGE_FACTORY, &mut fac) != 0 || fac.is_null() {
        rel(item);
        return None;
    }
    let f_vtbl = shell_vtable(fac);
    let fac_rel: ShellReleaseFn = std::mem::transmute(std::ptr::read(f_vtbl.add(2)));
    type GetImgFn = unsafe extern "system" fn(*mut c_void, Size, u32, *mut *mut c_void) -> i32;
    let get_img: GetImgFn = std::mem::transmute(std::ptr::read(f_vtbl.add(3)));

    let mut hbm: *mut c_void = std::ptr::null_mut();
    let hr = get_img(
        fac,
        Size { cx: 48, cy: 48 },
        SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK,
        &mut hbm,
    );
    fac_rel(fac);
    rel(item);
    if hr != 0 || hbm.is_null() {
        return None;
    }
    bitmap_to_rgba(hbm)
}

/// Decode an existing image file (png/jpg/bmp/ico) into a downscaled 48x48
/// PNG. Used for Store tile logos and any direct-image `display_icon`s.
fn png_bytes_from_file(path: &str) -> Option<Vec<u8>> {
    let ext = Path::new(path)
        .extension()
        .map(|e| e.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default();
    match ext.as_str() {
        "png" | "jpg" | "jpeg" | "bmp" | "ico" | "gif" => {}
        _ => return None,
    }
    let meta = std::fs::metadata(path).ok()?;
    if meta.len() > 5_000_000 {
        return None;
    }
    let img = image::open(path).ok()?;
    let rgba = img
        .resize_exact(48, 48, image::imageops::FilterType::Lanczos3)
        .into_rgba8();
    let (w, h) = rgba.dimensions();
    let mut buf = std::io::Cursor::new(Vec::new());
    image::write_buffer_with_format(
        &mut buf,
        rgba.as_raw(),
        w,
        h,
        image::ExtendedColorType::Rgba8,
        image::ImageFormat::Png,
    )
    .ok()?;
    Some(buf.into_inner())
}

/// Fingerprint the icon source (resolved display string + file mtime) so a
/// cached PNG is reused only while it still matches. Programs that get upgraded
/// with a new exe therefore re-fetch a fresh icon automatically.
fn icon_stamp(p: &Program) -> String {
    let Some(src) = icon_source(p) else {
        return String::new();
    };
    let base = src.split(',').next().unwrap_or(&src).trim();
    let mtime = std::fs::metadata(base)
        .ok()
        .and_then(|m| m.modified().ok())
        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d| d.as_secs())
        .unwrap_or(0);
    format!("{src}|{mtime}")
}

/// Produce the PNG bytes for one program (icon asset, shell icon, file-type
/// icon, or generic fallback). Pure function: safe to run on any thread.
fn png_for(p: &Program, appsfolder: &HashMap<String, (u32, u32, Vec<u8>)>) -> Option<Vec<u8>> {
    icon_source(p)
        .and_then(|disp| {
            let base = disp.split(',').next().unwrap_or(&disp).trim();
            if base.starts_with("shell:") {
                // Prefer the per-app image from our AppsFolder pass.
                if let Some(aumid) = extract_aumid(base) {
                    if let Some((w, h, rgba)) = appsfolder.get(&aumid) {
                        return rgba_to_png(*w, *h, rgba);
                    }
                }
                // SHGetFileInfoW resolves the AppUserModelID too.
                return extract_rgba(&disp)
                    .or_else(|| shell_item_icon(base))
                    .or_else(|| filetype_rgba(&disp))
                    .map(|(w, h, rgba)| rgba_to_png(w, h, &rgba))
                    .flatten();
            }
            png_bytes_from_file(base)
                .map(|b| (48u32, 48u32, b))
                .or_else(|| extract_rgba(&disp))
                .or_else(|| filetype_rgba(&disp))
                .map(|(w, h, rgba)| rgba_to_png(w, h, &rgba))
                .flatten()
        })
        .or_else(generic_png_bytes)
}

/// Extract + cache a PNG per program; sets `has_icon` when available.
///
/// Every entry ends up with an icon: its own image asset, the real shell icon
/// when its file exists, the file-type icon otherwise, and a generic
/// application icon as a last resort. Extraction runs on a thread pool so cold
/// scans finish quickly; a per-icon stamp makes upgrades re-fetch automatically.
pub fn attach_icons(list: &mut [Program], cache: &Path) -> usize {
    if list.is_empty() {
        return 0;
    }
    let _ = std::fs::create_dir_all(cache);

    // One enumeration of AppsFolder per scan yields real per-app icons for
    // every store entry; lookup afterward is pure hashmap. Must happen before
    // the worker threads borrow it.
    let want_shell = list
        .iter()
        .any(|p| p.display_icon.as_deref().unwrap_or("").starts_with("shell:"));
    let appsfolder = if want_shell { apps_folder_icons() } else { HashMap::new() };

    // Decide which entries need fresh icons (missing or stale stamp).
    let mut todo: Vec<usize> = Vec::new();
    for (i, p) in list.iter_mut().enumerate() {
        let target = cache.join(format!("{}.png", p.id));
        let stamp = icon_stamp(p);
        let stamp_path = cache.join(format!("{}.stamp", p.id));
        let up_to_date = target.exists()
            && std::fs::read_to_string(&stamp_path).map(|s| s == stamp).unwrap_or(false);
        if up_to_date {
            p.has_icon = true;
            continue;
        }
        todo.push(i);
    }
    if todo.is_empty() {
        return 0;
    }

    // Clone only the subset that needs work: workers need owned data so they
    // can run concurrently without touching the mutable caller list.
    let jobs: Vec<Program> = todo.iter().map(|&i| list[i].clone()).collect();

    let n_cpus = std::thread::available_parallelism().map(|n| n.get()).unwrap_or(4).min(8);
    let chunk = jobs.len().div_ceil(n_cpus);

    std::thread::scope(|s| {
        for w in 0..n_cpus {
            let start = w * chunk;
            let end = (start + chunk).min(jobs.len());
            if start >= end {
                break;
            }
            let slice = &jobs[start..end];
            let worker_dir = cache;
            let af = &appsfolder;
            s.spawn(move || {
                for p in slice {
                    let Some(bytes) = png_for(p, af) else {
                        continue;
                    };
                    let target = worker_dir.join(format!("{}.png", p.id));
                    if std::fs::write(&target, bytes).is_ok() {
                        let _ = std::fs::write(
                            worker_dir.join(format!("{}.stamp", p.id)),
                            icon_stamp(p),
                        );
                    }
                }
            });
        }
    });

    // Report which programs actually have an icon on disk now.
    let mut count = 0usize;
    for p in list.iter_mut() {
        let target = cache.join(format!("{}.png", p.id));
        p.has_icon = target.exists();
        if p.has_icon {
            count += 1;
        }
    }
    count
}

fn rgba_to_png(w: u32, h: u32, rgba: &[u8]) -> Option<Vec<u8>> {
    image::RgbaImage::from_raw(w, h, rgba.to_vec())
        .and_then(|img| {
            let mut buf = std::io::Cursor::new(Vec::new());
            img.write_to(&mut buf, image::ImageFormat::Png).ok()?;
            Some(buf.into_inner())
        })
}

#[cfg(test)]
pub fn debug_extract(display: &str) -> Option<(u32, u32, usize)> {
    extract_rgba(display).map(|(w, h, rgba)| (w, h, rgba.len()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn extracts_real_icon_from_exe() {
        let candidates = [
            r"C:\Windows\notepad.exe",
            r"C:\Windows\System32\notepad.exe",
        ];
        let Some(path) = candidates.iter().find(|p| Path::new(p).exists()) else {
            eprintln!("notepad not found, skipping");
            return;
        };
        let (w, h, rgba) = extract_rgba(path).expect("shell icon must extract");
        assert!(w >= 16 && h >= 16, "expected a real icon size, got {w}x{h}");
        assert_eq!(rgba.len(), (w * h * 4) as usize);

        let dir = std::env::temp_dir().join("wpi-test-icon.png");
        let img = image::RgbaImage::from_raw(w, h, rgba).expect("rgba buffer");
        img.save_with_format(&dir, image::ImageFormat::Png).expect("png encode");
        assert!(dir.exists());
        let _ = std::fs::remove_file(dir);
    }

    #[test]
    fn expands_env_vars_in_paths() {
        let root = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into());
        let expanded = expand_env(r"%SystemRoot%\System32\notepad.exe");
        assert_eq!(expanded, format!(r"{root}\System32\notepad.exe"));
        let untouched = expand_env(r"C:\Program Files\app\a.exe");
        assert_eq!(untouched, r"C:\Program Files\app\a.exe");
    }

    #[test]
    fn appsfolder_icons_enumerate_real() {
        let map = apps_folder_icons();
        assert!(
            map.len() >= 5,
            "expected AppsFolder enumeration to yield icons, got {}",
            map.len()
        );
        let mut distinct = std::collections::HashSet::new();
        for (aumid, (w, h, rgba)) in &map {
            eprintln!("app={aumid} {w}x{h}");
            distinct.insert(rgba.clone());
        }
        eprintln!("appsfolder entries={} distinct={}", map.len(), distinct.len());
        assert!(
            distinct.len() >= 5,
            "AppsFolder icons should be distinct per app, got {}",
            distinct.len()
        );
    }

    #[test]
    fn store_attest_real_icons() {
        let mut list = super::super::store::scan_registry();
        assert!(list.len() >= 10, "expected store entries, got {}", list.len());
        let n_shell = list.iter().filter(|p| p.display_icon.as_deref().unwrap_or("").starts_with("shell:")).count();
        eprintln!("store entries={} with shell icon={}", list.len(), n_shell);
        let dir = std::env::temp_dir().join("wpi-store-test-icons");
        let _ = std::fs::remove_dir_all(&dir);
        attach_icons(&mut list, &dir);
        let mut distinct = std::collections::HashSet::new();
        for p in &list {
            let f = dir.join(format!("{}.png", p.id));
            if let Ok(b) = std::fs::read(&f) {
                distinct.insert(b);
            }
        }
        eprintln!("store distinct icons = {}", distinct.len());
        assert!(distinct.len() >= 8, "expected real distinct store icons, got {}", distinct.len());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn indexed_icon_differs_from_default() {
        let base = r"C:\Windows\System32\imageres.dll";
        if !Path::new(base).exists() {
            eprintln!("imageres.dll not found, skipping");
            return;
        }
        let a = extract_rgba(base).expect("default imageres icon");
        let mut differing = false;
        for idx in -80..=80 {
            if let Some((_, _, bytes)) = extract_rgba(&format!("{base},{idx}")) {
                if bytes != a.2 {
                    differing = true;
                    break;
                }
            }
        }
        assert!(differing, "no indexed icon differs from the default");
    }
}