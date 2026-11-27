//! One-time shader and pipeline preparation before Play. A small stamp file in the preset's
//! cache folder records what the cache was prepared for: the shader key (the engine's
//! translator fingerprint, `firstrun::shader_key`), the GPU driver and the preset's cache key.
//! Play prepares first only when the stamp does not match, so an update that keeps the
//! translator needs no preparation, and one that changes it only translates what is new.
use std::path::{Path, PathBuf};

pub const STAMP: &str = "prepared.txt";

/// What a prepared cache is valid for.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Stamp {
    /// `translator:<fingerprint>` (or `build:<exe hash>` for engines without one).
    pub shaders: String,
    pub driver: String,
    pub cache_key: String,
}

impl Stamp {
    pub fn to_text(&self) -> String {
        format!("shaders={}\ndriver={}\ncache={}\n", self.shaders.trim(), self.driver.trim(), self.cache_key.trim())
    }

    pub fn parse(text: &str) -> Option<Stamp> {
        let get = |k: &str| text.lines().find_map(|l| l.trim().strip_prefix(k).and_then(|r| r.strip_prefix('='))).map(|v| v.trim().to_string());
        Some(Stamp { shaders: get("shaders")?, driver: get("driver")?, cache_key: get("cache")? })
    }

    /// A stamp without a shader key never counts as prepared (the engine was not found yet).
    pub fn valid(&self) -> bool {
        !self.shaders.trim().is_empty()
    }
}

pub fn path(cache: &Path) -> PathBuf {
    cache.join(STAMP)
}

/// True when `cache` was prepared for exactly `want`.
pub fn matches(cache: &Path, want: &Stamp) -> bool {
    want.valid() && std::fs::read_to_string(path(cache)).ok().and_then(|t| Stamp::parse(&t)).is_some_and(|s| &s == want)
}

pub fn write(cache: &Path, s: &Stamp) -> std::io::Result<()> {
    std::fs::create_dir_all(cache)?;
    std::fs::write(path(cache), s.to_text())
}

/// The subcommands an engine's `--help` lists (the lines under `Commands:`).
pub fn commands(help: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut inside = false;
    for l in help.lines() {
        if l.trim_end() == "Commands:" {
            inside = true;
            continue;
        }
        if inside {
            if l.trim().is_empty() || !l.starts_with(' ') {
                if !out.is_empty() {
                    break;
                }
                continue;
            }
            if let Some(name) = l.split_whitespace().next() {
                out.push(name.to_string());
            }
        }
    }
    out
}

/// The most complete pipeline step the engine offers, best first: a command that builds every
/// pipeline ahead of time (`prepare`, `pipelines`, `precompile-pipelines`), else `warm`.
pub fn pipeline_command(cmds: &[String]) -> Option<&'static str> {
    ["prepare", "pipelines", "precompile-pipelines", "warm"].into_iter().find(|c| cmds.iter().any(|x| x == c))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn s(e: &str, d: &str, c: &str) -> Stamp {
        Stamp { shaders: e.into(), driver: d.into(), cache_key: c.into() }
    }

    #[test]
    fn stamp_round_trip_and_mismatch() {
        let d = std::env::temp_dir().join(format!("sp-prep-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        let a = s("abc", "32.0.15.6094", "stable|F:/c/scarlett");
        assert!(!matches(&d, &a), "no stamp yet");
        write(&d, &a).unwrap();
        assert!(matches(&d, &a));
        assert!(!matches(&d, &s("abd", "32.0.15.6094", "stable|F:/c/scarlett")), "translator changed");
        // a stamp from launchers before 0.5.1 (keyed on the engine build) is not reused
        std::fs::write(path(&d), "engine=abc\ndriver=32.0.15.6094\ncache=stable|F:/c/scarlett\n").unwrap();
        assert!(!matches(&d, &a));
        write(&d, &a).unwrap();
        assert!(!matches(&d, &s("abc", "32.0.15.7000", "stable|F:/c/scarlett")), "driver changed");
        assert!(!matches(&d, &s("abc", "32.0.15.6094", "look|F:/c/scarlett")), "preset changed");
        std::fs::write(path(&d), "garbage").unwrap();
        assert!(!matches(&d, &a), "a corrupt stamp means not prepared");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn empty_engine_never_matches() {
        let d = std::env::temp_dir().join(format!("sp-prep-e-{}", std::process::id()));
        let e = s("", "", "");
        write(&d, &e).unwrap();
        assert!(!matches(&d, &e));
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn help_commands_and_choice() {
        let help = "Run a title\n\nUsage: x <COMMAND>\n\nCommands:\n  inspect      Summarise\n  shaders      Translate\n  warm         Make the host pipelines\n  help         Print\n\nOptions:\n  -h, --help  Print help\n";
        let c = commands(help);
        assert_eq!(c, ["inspect", "shaders", "warm", "help"]);
        assert_eq!(pipeline_command(&c), Some("warm"));
        let mut c2 = c.clone();
        c2.push("prepare".into());
        assert_eq!(pipeline_command(&c2), Some("prepare"));
        assert_eq!(pipeline_command(&["run".to_string()]), None);
    }
}
