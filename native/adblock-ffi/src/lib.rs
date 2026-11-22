//! A tiny C ABI over Brave's adblock-rust engine, consumed by the C# app via P/Invoke.
//! All strings crossing the boundary are NUL-terminated UTF-8.

use std::collections::HashSet;
use std::ffi::{c_char, CStr, CString};
use std::ptr;
use std::slice;

use adblock::lists::{FilterSet, ParseOptions};
use adblock::request::Request;
use adblock::resources::Resource;
use adblock::Engine;

unsafe fn to_str<'a>(p: *const c_char) -> &'a str {
    if p.is_null() {
        return "";
    }
    CStr::from_ptr(p).to_str().unwrap_or("")
}

fn to_c_string(s: String) -> *mut c_char {
    CString::new(s).map(CString::into_raw).unwrap_or(ptr::null_mut())
}

/// Builds an engine from filter list text (lists may be concatenated with newlines).
#[no_mangle]
pub unsafe extern "C" fn brisk_engine_from_rules(rules: *const u8, len: usize) -> *mut Engine {
    let bytes = slice::from_raw_parts(rules, len);
    let text = String::from_utf8_lossy(bytes);
    let mut set = FilterSet::new(false);
    set.add_filter_list(text.into_owned(), ParseOptions::default());
    Box::into_raw(Box::new(Engine::new_with_filter_set(set)))
}

/// Loads an engine previously produced by `brisk_engine_serialize`. Returns null if the data is
/// corrupt or from an incompatible engine version.
#[no_mangle]
pub unsafe extern "C" fn brisk_engine_deserialize(data: *const u8, len: usize) -> *mut Engine {
    let bytes = slice::from_raw_parts(data, len);
    let mut engine = Engine::default();
    match engine.deserialize(bytes) {
        Ok(()) => Box::into_raw(Box::new(engine)),
        Err(_) => ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn brisk_engine_serialize(engine: *const Engine, out_len: *mut usize) -> *mut u8 {
    let data = (*engine).serialize().into_boxed_slice();
    *out_len = data.len();
    Box::into_raw(data) as *mut u8
}

#[no_mangle]
pub unsafe extern "C" fn brisk_free_bytes(data: *mut u8, len: usize) {
    if !data.is_null() {
        drop(Box::from_raw(ptr::slice_from_raw_parts_mut(data, len)));
    }
}

#[no_mangle]
pub unsafe extern "C" fn brisk_engine_free(engine: *mut Engine) {
    if !engine.is_null() {
        drop(Box::from_raw(engine));
    }
}

/// Returns 1 if the request should be blocked, 0 otherwise.
#[no_mangle]
pub unsafe extern "C" fn brisk_engine_check(
    engine: *const Engine,
    url: *const c_char,
    source_url: *const c_char,
    request_type: *const c_char,
) -> i32 {
    let request = match Request::new(to_str(url), to_str(source_url), to_str(request_type), "get") {
        Ok(r) => r,
        Err(_) => return 0,
    };
    (*engine).check_network_request(&request).should_block() as i32
}

/// Loads scriptlet resources (a JSON array of adblock-rust `Resource`s), replacing any loaded
/// before. Returns how many were loaded, or -1 if the JSON is invalid.
#[no_mangle]
pub unsafe extern "C" fn brisk_engine_use_resources(engine: *mut Engine, json: *const c_char) -> i32 {
    match serde_json::from_str::<Vec<Resource>>(to_str(json)) {
        Ok(resources) => {
            let n = resources.len() as i32;
            (*engine).use_resources(resources);
            n
        }
        Err(_) => -1,
    }
}

/// Returns JSON `{"hide_selectors":[...],"exceptions":[...],"generichide":bool,"injected_script":"..."}`
/// for a page URL.
/// Free with `brisk_free_string`.
#[no_mangle]
pub unsafe extern "C" fn brisk_engine_url_cosmetics(engine: *const Engine, url: *const c_char) -> *mut c_char {
    let res = (*engine).url_cosmetic_resources(to_str(url));
    let json = serde_json::json!({
        "hide_selectors": res.hide_selectors,
        "exceptions": res.exceptions,
        "generichide": res.generichide,
        "injected_script": res.injected_script,
    });
    to_c_string(json.to_string())
}

/// Takes JSON arrays of class names, ids and exceptions; returns a JSON array of selectors to hide.
/// Free with `brisk_free_string`.
#[no_mangle]
pub unsafe extern "C" fn brisk_engine_hidden_selectors(
    engine: *const Engine,
    classes_json: *const c_char,
    ids_json: *const c_char,
    exceptions_json: *const c_char,
) -> *mut c_char {
    let classes: Vec<String> = serde_json::from_str(to_str(classes_json)).unwrap_or_default();
    let ids: Vec<String> = serde_json::from_str(to_str(ids_json)).unwrap_or_default();
    let exceptions: HashSet<String> = serde_json::from_str(to_str(exceptions_json)).unwrap_or_default();
    let selectors = (*engine).hidden_class_id_selectors(&classes, &ids, &exceptions);
    to_c_string(serde_json::to_string(&selectors).unwrap_or_else(|_| "[]".into()))
}

#[no_mangle]
pub unsafe extern "C" fn brisk_free_string(s: *mut c_char) {
    if !s.is_null() {
        drop(CString::from_raw(s));
    }
}
