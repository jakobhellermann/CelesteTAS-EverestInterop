use clap::{Parser, ValueEnum};
use serde_json::Value;
use std::collections::BTreeMap;
use std::io::IsTerminal;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

const TRACE_ROOT: &str = "/tmp/TAS-Traces";

static COLOR: AtomicBool = AtomicBool::new(false);

/// Diff two TAS trace JSON files: running frames aligned by frame number, stage/call trees aligned by name.
#[derive(Parser)]
struct Args {
    /// Traces to diff. none: newest dir's latest/{1,2}.json; one <dir>: its latest/{1,2}.json; two files: those.
    #[arg(value_name = "PATH")]
    paths: Vec<String>,

    /// Show full per-frame diffs (default: first divergence + summary).
    #[arg(long)]
    all: bool,

    /// Align running frames by position (0-based index) instead of by Frame number. Use to compare two
    /// independent segments (e.g. a continuous run vs a savestate resume) that sit at different TAS positions.
    #[arg(long)]
    relative: bool,

    /// Keys to skip when diffing (comma-separated).
    #[arg(long, value_delimiter = ',', default_values_t = [String::from("FrameCount"), String::from("State")])]
    ignore: Vec<String>,

    /// When to colorize output.
    #[arg(long, value_enum, default_value_t = ColorWhen::Auto)]
    color: ColorWhen,
}

#[derive(Clone, Copy, ValueEnum)]
enum ColorWhen {
    Auto,
    Always,
    Never,
}

fn paint(s: &str, code: &str) -> String {
    if COLOR.load(Ordering::Relaxed) {
        format!("\x1b[{code}m{s}\x1b[0m")
    } else {
        s.to_string()
    }
}
fn red(s: &str) -> String {
    paint(s, "31")
}
fn green(s: &str) -> String {
    paint(s, "32")
}
fn bold(s: &str) -> String {
    paint(s, "1")
}
fn dim(s: &str) -> String {
    paint(s, "2")
}

struct Diff {
    path: String,
    a: Option<String>,
    b: Option<String>,
}

fn main() {
    let args = Args::parse();

    COLOR.store(
        match args.color {
            ColorWhen::Always => true,
            ColorWhen::Never => false,
            ColorWhen::Auto => std::io::stdout().is_terminal(),
        },
        Ordering::Relaxed,
    );

    let mut ignore: Vec<String> = args.ignore.into_iter().filter(|s| !s.is_empty()).collect();
    // Under --relative the segments are aligned by position, so the absolute Frame number is expected to differ
    // (they sit at different points in their runs) and comparing it is meaningless — ignore it.
    if args.relative && !ignore.iter().any(|s| s == "Frame") {
        ignore.push("Frame".to_string());
    }

    let (pa, pb) = resolve_paths(&args.paths);
    let a = load(&pa);
    let b = load(&pb);

    let ta = a["Trace"].as_array().expect("A: no Trace array");
    let tb = b["Trace"].as_array().expect("B: no Trace array");

    let (run_a, paused_a) = partition(ta);
    let (run_b, paused_b) = partition(tb);

    println!("A: {}  (checksum {}, {} frames, {} paused)", pa.display(), a["Checksum"], run_a.len(), paused_a.len());
    println!("B: {}  (checksum {}, {} frames, {} paused)", pb.display(), b["Checksum"], run_b.len(), paused_b.len());
    if !ignore.is_empty() {
        println!("(ignoring keys: {})", ignore.join(", "));
    }
    println!();

    let diverged = diff_running(&run_a, &run_b, &ignore, args.all, args.relative);
    check_pause_freeze("A", &paused_a);
    check_pause_freeze("B", &paused_b);
    diff_paused(&paused_a, &paused_b);

    if diverged {
        std::process::exit(1);
    }
}

/// Split a Trace into advancing frames (keyed by Frame number) and paused/slow entries (held frames, which
/// can repeat the same Frame number). Distinguished by the `State` tag. Event-only entries are dropped.
fn partition(trace: &[Value]) -> (BTreeMap<i64, &Value>, Vec<(Option<i64>, &Value)>) {
    let mut run = BTreeMap::new();
    let mut paused = vec![];
    for e in trace {
        let held = matches!(
            e.get("State").and_then(Value::as_str),
            Some("Paused") | Some("SlowForward")
        );
        if held {
            paused.push((e.get("Frame").and_then(Value::as_i64), e));
        } else if let Some(f) = e.get("Frame").and_then(Value::as_i64) {
            run.insert(f, e);
        }
    }
    (run, paused)
}

/// Running frames aligned by Frame number — immune to the extra paused entries a stepped run inserts.
fn diff_running(a: &BTreeMap<i64, &Value>, b: &BTreeMap<i64, &Value>, ignore: &[String], all: bool, relative: bool) -> bool {
    // Aligned (key, A entry, B entry) pairs: by Frame number, or — for two independent segments sitting at
    // different TAS positions (e.g. a continuous run vs a savestate resume) — by 0-based position, so they
    // compare frame-for-frame. A swallowed frame then shows as a length mismatch / shifted per-index diff.
    let pairs: Vec<(i64, Option<&Value>, Option<&Value>)> = if relative {
        let av: Vec<&Value> = a.values().copied().collect();
        let bv: Vec<&Value> = b.values().copied().collect();
        (0..av.len().max(bv.len()))
            .map(|i| (i as i64, av.get(i).copied(), bv.get(i).copied()))
            .collect()
    } else {
        let mut frames: Vec<i64> = a.keys().chain(b.keys()).copied().collect();
        frames.sort_unstable();
        frames.dedup();
        frames.into_iter().map(|f| (f, a.get(&f).copied(), b.get(&f).copied())).collect()
    };

    let mut diverging: Vec<(i64, String, Vec<Diff>)> = vec![];
    for (key, ea, eb) in pairs {
        match (ea, eb) {
            (Some(ea), Some(eb)) => {
                let mut diffs = vec![];
                diff_value("", ea, eb, ignore, &mut diffs);
                if !diffs.is_empty() {
                    diverging.push((key, label_entry(ea), diffs));
                }
            }
            (Some(ea), None) => diverging.push((key, label_entry(ea), vec![only("A")])),
            (None, Some(eb)) => diverging.push((key, label_entry(eb), vec![only("B")])),
            (None, None) => unreachable!(),
        }
    }

    if diverging.is_empty() {
        println!("{}", green(&format!("\u{2713} running frames identical ({} frames)", a.len())));
        return false;
    }

    let to_show = if all { diverging.len() } else { 1 };
    for (n, (frame, lbl, diffs)) in diverging.iter().enumerate().take(to_show) {
        let header = if n == 0 { "first divergence" } else { "divergence" };
        println!("{}", bold(&format!("=== {header} @ {lbl} ===")));
        let _ = frame;
        for d in diffs {
            print_diff(d);
        }
        println!();
    }

    if !all && diverging.len() > 1 {
        let rest: Vec<String> = diverging[1..]
            .iter()
            .map(|(f, _, diffs)| format!("{f}({})", diffs.len()))
            .collect();
        println!("{}", dim(&format!("{} more diverging frames: {}", diverging.len() - 1, rest.join(", "))));
        println!("{}", dim("(run with --all for full per-frame diffs)"));
    }

    true
}

/// Paused entries (stepped/slow-forward frames). Usually only one run has them; list their contents
/// (HeroController calls, SetState + backtrace) grouped by the TAS frame they sit on.
fn diff_paused(a: &[(Option<i64>, &Value)], b: &[(Option<i64>, &Value)]) {
    if a.is_empty() && b.is_empty() {
        return;
    }
    println!("{}", bold(&format!("=== paused entries  (A: {}, B: {}) ===", a.len(), b.len())));
    print_paused_side("A", a);
    print_paused_side("B", b);
}

/// Detect a pause that isn't freezing game state. When the TAS pauses (breakpoint / savestate resume / step), it
/// should hold every probed Var constant for as long as the frame is held — `EnablePause` removes the
/// MonoBehaviour update systems and zeroes timeScale. If a probe (vel, pos, a timer, …) *changes* across the
/// consecutive paused entries sitting on one TAS frame, something is still ticking — the classic step-vs-run
/// desync source (e.g. a force-pause that bypassed `EnablePause`/`FreezeScriptUpdates`). Surfaces it directly
/// instead of making you eyeball per-phase dumps.
fn check_pause_freeze(tag: &str, entries: &[(Option<i64>, &Value)]) {
    // Group consecutive paused entries by the TAS frame they're held on.
    let mut groups: Vec<(Option<i64>, Vec<&Value>)> = vec![];
    for (frame, entry) in entries {
        match groups.last_mut() {
            Some((gf, v)) if gf == frame => v.push(entry),
            _ => groups.push((*frame, vec![*entry])),
        }
    }

    let mut header = false;
    for (frame, group) in &groups {
        if group.len() < 2 {
            continue;
        }
        let varsets: Vec<BTreeMap<String, String>> = group.iter().map(|e| final_vars(e)).collect();
        let mut keys: Vec<String> = varsets.iter().flat_map(|m| m.keys().cloned()).collect();
        keys.sort();
        keys.dedup();
        for k in keys {
            let seq: Vec<String> = varsets.iter().filter_map(|m| m.get(&k).cloned()).collect();
            if seq.len() < 2 || seq.iter().all(|v| v == &seq[0]) {
                continue;
            }
            if !header {
                println!(
                    "{}",
                    bold(&red(&format!("=== ⚠ {tag}: state advanced DURING pause (freeze leak — pause not freezing MonoBehaviours) ===")))
                );
                header = true;
            }
            let fno = frame.map(|f| f.to_string()).unwrap_or_else(|| "?".into());
            println!(
                "  frame {fno}: {} drifts over {} held frames: {} {} {}",
                bold(&k),
                seq.len(),
                red(seq.first().unwrap()),
                dim("→"),
                red(seq.last().unwrap())
            );
        }
    }
}

/// End-of-frame value of each probed Var for a paused entry: later stages overwrite earlier ones, so the final
/// insert wins — i.e. the state the held frame settled to.
fn final_vars(entry: &Value) -> BTreeMap<String, String> {
    let mut out = BTreeMap::new();
    if let Some(stages) = entry.get("FrameHistory").and_then(Value::as_array) {
        for st in stages {
            if let Some(vars) = st.get("Vars").and_then(Value::as_object) {
                for (k, v) in vars {
                    out.insert(k.clone(), fmt_val(v));
                }
            }
        }
    }
    out
}

fn print_paused_side(tag: &str, entries: &[(Option<i64>, &Value)]) {
    if entries.is_empty() {
        return;
    }
    for (frame, entry) in entries {
        let state = entry.get("State").and_then(Value::as_str).unwrap_or("?");
        let fno = frame.map(|f| f.to_string()).unwrap_or_else(|| "?".into());
        println!("  {tag} frame {fno} [{state}]");
        for item in render_paused_items(entry) {
            println!("      {item}");
        }
    }
}

fn render_paused_items(entry: &Value) -> Vec<String> {
    entry
        .get("FrameHistory")
        .and_then(Value::as_array)
        .map(|stages| {
            stages
                .iter()
                .filter_map(|st| {
                    let name = st.get("Stage").and_then(Value::as_str)?;
                    let events: Vec<String> = st
                        .get("Events")
                        .and_then(Value::as_array)
                        .map(|es| es.iter().map(render_item).collect())
                        .unwrap_or_default();
                    if events.is_empty() {
                        None
                    } else {
                        Some(format!("{name}: {}", events.join(", ")))
                    }
                })
                .collect()
        })
        .unwrap_or_default()
}

/// Render a frame-history entry compactly: a call node as `name` (or `name(…)` if it has children),
/// a `["label", [stack]]` as `label <- frame <- …`, otherwise the raw value.
fn render_item(it: &Value) -> String {
    match it {
        Value::Object(o) if o.contains_key("Call") => {
            let name = o.get("Call").and_then(Value::as_str).unwrap_or("?");
            let has_children = o.get("Children").and_then(Value::as_array).is_some_and(|c| !c.is_empty());
            if has_children {
                format!("{name}(…)")
            } else {
                name.to_string()
            }
        }
        Value::Array(arr) => {
            let head = arr.first().map(fmt_val).unwrap_or_default();
            // A trailing string-array is a serialized stacktrace.
            if let Some(stack) = arr.iter().skip(1).find_map(Value::as_array) {
                let frames: Vec<String> = stack.iter().take(8).map(fmt_val).collect();
                if frames.is_empty() {
                    head
                } else {
                    format!("{head}  <- {}", frames.join(" <- "))
                }
            } else {
                let rest: Vec<String> = arr.iter().skip(1).map(fmt_val).collect();
                if rest.is_empty() {
                    head
                } else {
                    format!("{head} {}", rest.join(" "))
                }
            }
        }
        _ => fmt_val(it),
    }
}

fn only(side: &str) -> Diff {
    Diff {
        path: "<frame present>".into(),
        a: if side == "A" { Some("yes".into()) } else { None },
        b: if side == "B" { Some("yes".into()) } else { None },
    }
}

/// Recursively collect leaf differences. Multi-line strings (the `Info` blob) are split and compared
/// line-by-line, labelled by the `Key:` prefix where present.
fn diff_value(path: &str, a: &Value, b: &Value, ignore: &[String], out: &mut Vec<Diff>) {
    if a == b {
        return;
    }
    match (a, b) {
        (Value::Object(oa), Value::Object(ob)) => {
            let mut seen = std::collections::HashSet::new();
            for k in oa.keys().chain(ob.keys()) {
                if !seen.insert(k) || ignore.iter().any(|ig| ig == k) {
                    continue;
                }
                let child = if path.is_empty() { k.clone() } else { format!("{path}.{k}") };
                match (oa.get(k), ob.get(k)) {
                    (Some(va), Some(vb)) => diff_value(&child, va, vb, ignore, out),
                    (va, vb) => out.push(Diff {
                        path: child,
                        a: va.map(fmt_val),
                        b: vb.map(fmt_val),
                    }),
                }
            }
        }
        // Arrays are sequences (FrameHistory stages, stage Events, stacktraces) where insertions happen, so
        // always LCS-align: a single inserted entry shows as one +/- instead of cascading every following
        // element as "changed". Matched entries with the same key recurse, so nested object/var changes still
        // surface field-wise.
        (Value::Array(aa), Value::Array(ab)) => diff_array_lcs(path, aa, ab, ignore, out),
        (Value::String(sa), Value::String(sb)) if sa.contains('\n') || sb.contains('\n') => {
            diff_multiline(path, sa, sb, out);
        }
        _ => out.push(Diff {
            path: path.to_string(),
            a: Some(fmt_val(a)),
            b: Some(fmt_val(b)),
        }),
    }
}

/// LCS-based sequence diff for long arrays (FrameHistory). Elements are aligned by `match_key` so a single
/// insertion/deletion shows as one +/- entry instead of shifting every following element. Aligned pairs with
/// the same key but differing content recurse, so an inner change (e.g. a ThroughFrame dict's vel) still
/// surfaces field-wise rather than as a whole-element replacement.
fn diff_array_lcs(path: &str, a: &[Value], b: &[Value], ignore: &[String], out: &mut Vec<Diff>) {
    let ka: Vec<String> = a.iter().map(match_key).collect();
    let kb: Vec<String> = b.iter().map(match_key).collect();
    let (n, m) = (a.len(), b.len());

    // dp[i*(m+1)+j] = LCS length of ka[i..] and kb[j..]
    let mut dp = vec![0u32; (n + 1) * (m + 1)];
    let at = |i: usize, j: usize| i * (m + 1) + j;
    for i in (0..n).rev() {
        for j in (0..m).rev() {
            dp[at(i, j)] = if ka[i] == kb[j] {
                dp[at(i + 1, j + 1)] + 1
            } else {
                dp[at(i + 1, j)].max(dp[at(i, j + 1)])
            };
        }
    }

    let (mut i, mut j) = (0, 0);
    while i < n && j < m {
        if ka[i] == kb[j] {
            if a[i] != b[j] {
                diff_value(&format!("{path}[{}]", ka[i]), &a[i], &b[j], ignore, out);
            }
            i += 1;
            j += 1;
        } else if dp[at(i + 1, j)] >= dp[at(i, j + 1)] {
            out.push(Diff { path: format!("{path}[-]"), a: Some(render_item(&a[i])), b: None });
            i += 1;
        } else {
            out.push(Diff { path: format!("{path}[+]"), a: None, b: Some(render_item(&b[j])) });
            j += 1;
        }
    }
    while i < n {
        out.push(Diff { path: format!("{path}[-]"), a: Some(render_item(&a[i])), b: None });
        i += 1;
    }
    while j < m {
        out.push(Diff { path: format!("{path}[+]"), a: None, b: Some(render_item(&b[j])) });
        j += 1;
    }
}

/// Alignment key for LCS: a stage object aligns by its `Stage` name, an event array by its leading label
/// string, a plain string by itself; anything else by full serialization.
fn match_key(v: &Value) -> String {
    match v {
        Value::String(s) => s.clone(),
        Value::Object(o) => o
            .get("Stage")
            .or_else(|| o.get("Call"))
            .and_then(Value::as_str)
            .map(str::to_string)
            .unwrap_or_else(|| serde_json::to_string(v).unwrap_or_default()),
        Value::Array(a) => match a.first() {
            Some(Value::String(s)) => s.clone(),
            _ => serde_json::to_string(v).unwrap_or_default(),
        },
        _ => serde_json::to_string(v).unwrap_or_default(),
    }
}

/// Stable per-line identity for LCS alignment of the Info blob. Enemy lines (`<name> hp=… (pos) …`) key by the
/// name — hp/position vary frame to frame and must not shift the alignment; `Key: value` lines key by the key;
/// everything else by the whole trimmed line.
fn line_key(line: &str) -> String {
    let t = line.trim();
    if let Some(idx) = t.find(" hp=") {
        return t[..idx].to_string();
    }
    if let Some((k, _)) = t.split_once(':') {
        return k.trim().to_string();
    }
    t.to_string()
}

/// Diff the multi-line `Info` blob by LCS-aligning its lines on `line_key`, mirroring `diff_array_lcs`. A changed,
/// inserted or removed line then shows as a single entry instead of cascading every following line as a spurious
/// positional diff (the previous naive line-by-index comparison did the latter — one enemy drifting a few units
/// misaligned the whole block). Matched lines whose content differs surface as one labelled A/B change.
fn diff_multiline(path: &str, sa: &str, sb: &str, out: &mut Vec<Diff>) {
    let la: Vec<&str> = sa.split('\n').collect();
    let lb: Vec<&str> = sb.split('\n').collect();
    let ka: Vec<String> = la.iter().map(|l| line_key(l)).collect();
    let kb: Vec<String> = lb.iter().map(|l| line_key(l)).collect();
    let (n, m) = (la.len(), lb.len());

    // dp[i*(m+1)+j] = LCS length of ka[i..] and kb[j..]
    let mut dp = vec![0u32; (n + 1) * (m + 1)];
    let at = |i: usize, j: usize| i * (m + 1) + j;
    for i in (0..n).rev() {
        for j in (0..m).rev() {
            dp[at(i, j)] = if ka[i] == kb[j] {
                dp[at(i + 1, j + 1)] + 1
            } else {
                dp[at(i + 1, j)].max(dp[at(i, j + 1)])
            };
        }
    }

    let mut push = |lbl: &str, a: Option<&str>, b: Option<&str>| {
        out.push(Diff {
            path: format!("{path}/{lbl}"),
            a: a.map(|s| s.trim().to_string()),
            b: b.map(|s| s.trim().to_string()),
        });
    };
    let (mut i, mut j) = (0, 0);
    while i < n && j < m {
        if ka[i] == kb[j] {
            if la[i] != lb[j] {
                push(&ka[i], Some(la[i]), Some(lb[j]));
            }
            i += 1;
            j += 1;
        } else if dp[at(i + 1, j)] >= dp[at(i, j + 1)] {
            push(&ka[i], Some(la[i]), None);
            i += 1;
        } else {
            push(&kb[j], None, Some(lb[j]));
            j += 1;
        }
    }
    while i < n {
        push(&ka[i], Some(la[i]), None);
        i += 1;
    }
    while j < m {
        push(&kb[j], None, Some(lb[j]));
        j += 1;
    }
}

fn print_diff(d: &Diff) {
    println!("  {}", bold(&d.path));
    println!("    {}", red(&format!("A: {}", d.a.as_deref().unwrap_or("<missing>"))));
    println!("    {}", green(&format!("B: {}", d.b.as_deref().unwrap_or("<missing>"))));
}

fn fmt_val(v: &Value) -> String {
    match v {
        Value::String(s) => s.clone(),
        _ => serde_json::to_string(v).unwrap_or_default(),
    }
}

/// Short human label for a trace entry: `Frame 18 [10,R,J]`.
fn label_entry(e: &Value) -> String {
    let f = e.get("Frame").map(ToString::to_string).unwrap_or_else(|| "?".into());
    let input = e.get("InputLine").and_then(Value::as_str).unwrap_or("").trim();
    if input.is_empty() {
        format!("Frame {f}")
    } else {
        format!("Frame {f} [{input}]")
    }
}

fn load(p: &Path) -> Value {
    let content = std::fs::read_to_string(p).unwrap_or_else(|e| panic!("read {}: {e}", p.display()));
    serde_json::from_str(&content).unwrap_or_else(|e| panic!("parse {}: {e}", p.display()))
}

/// 0 args -> newest trace dir's latest/{1,2}.json. 1 dir arg -> that dir's latest/{1,2}.json.
/// 2 args -> the two files verbatim.
fn resolve_paths(paths: &[String]) -> (PathBuf, PathBuf) {
    match paths.len() {
        2 => (PathBuf::from(&paths[0]), PathBuf::from(&paths[1])),
        1 => {
            let p = PathBuf::from(&paths[0]);
            if p.is_dir() {
                latest_pair(&p)
            } else {
                panic!("single arg must be a trace dir; got file {}", p.display());
            }
        }
        0 => {
            let dir = newest_trace_dir().unwrap_or_else(|| {
                panic!("no trace dirs under {TRACE_ROOT}; pass two files explicitly")
            });
            latest_pair(&dir)
        }
        _ => {
            eprintln!("error: expected at most 2 paths (two files, or one trace dir); got {}", paths.len());
            std::process::exit(2);
        }
    }
}

fn latest_pair(dir: &Path) -> (PathBuf, PathBuf) {
    let latest = dir.join("latest");
    let base = if latest.is_dir() { latest } else { dir.to_path_buf() };
    (base.join("1.json"), base.join("2.json"))
}

fn newest_trace_dir() -> Option<PathBuf> {
    let mut best: Option<(std::time::SystemTime, PathBuf)> = None;
    for entry in std::fs::read_dir(TRACE_ROOT).ok()?.flatten() {
        let path = entry.path();
        if !path.is_dir() {
            continue;
        }
        // Rank by mtime of latest/1.json (the freshest written trace), falling back to the dir.
        let stamp_target = path.join("latest/1.json");
        let Ok(meta) = std::fs::metadata(&stamp_target).or_else(|_| std::fs::metadata(&path)) else {
            continue;
        };
        let Ok(mtime) = meta.modified() else { continue };
        if best.as_ref().map_or(true, |(t, _)| mtime > *t) {
            best = Some((mtime, path));
        }
    }
    best.map(|(_, p)| p)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    /// Collect leaf diffs as (path, a, b) tuples for easy assertions.
    fn diffs(a: Value, b: Value) -> Vec<(String, Option<String>, Option<String>)> {
        diffs_ignoring(&[], a, b)
    }

    fn diffs_ignoring(ignore: &[String], a: Value, b: Value) -> Vec<(String, Option<String>, Option<String>)> {
        let mut out = vec![];
        diff_value("", &a, &b, ignore, &mut out);
        out.into_iter().map(|d| (d.path, d.a, d.b)).collect()
    }

    #[test]
    fn identical_has_no_diffs() {
        let v = json!({"Stage": "Early", "Events": ["A", "B"]});
        assert!(diffs(v.clone(), v).is_empty());
    }

    #[test]
    fn ignored_keys_are_skipped() {
        let a = json!({"FrameCount": 1, "x": 1});
        let b = json!({"FrameCount": 2, "x": 1});
        assert!(diffs_ignoring(&["FrameCount".into()], a, b).is_empty());
    }

    // Regression: a single inserted event must show as ONE `[+]`, not cascade every following element.
    #[test]
    fn event_insertion_is_a_single_plus() {
        let a = json!({"Events": ["Move", "GetRunSpeed"]});
        let b = json!({"Events": ["Move", "DoMovement", "GetRunSpeed"]});
        assert_eq!(
            diffs(a, b),
            vec![("Events[+]".to_string(), None, Some("DoMovement".to_string()))]
        );
    }

    #[test]
    fn event_deletion_is_a_single_minus() {
        let a = json!({"Events": ["Move", "DoMovement", "GetRunSpeed"]});
        let b = json!({"Events": ["Move", "GetRunSpeed"]});
        assert_eq!(
            diffs(a, b),
            vec![("Events[-]".to_string(), Some("DoMovement".to_string()), None)]
        );
    }

    // Stages align by their `Stage` name (LCS), and a changed var surfaces field-wise under the stage name.
    #[test]
    fn stages_align_by_name_and_var_change_surfaces() {
        let a = json!([{"Stage": "Early", "Vars": {"onGround": true}}]);
        let b = json!([{"Stage": "Early", "Vars": {"onGround": false}}]);
        assert_eq!(
            diffs(a, b),
            vec![("[Early].Vars.onGround".to_string(), Some("true".to_string()), Some("false".to_string()))]
        );
    }

    // An inserted stage shows as one `[+]`; the shared stage still diffs normally (no cascade).
    #[test]
    fn inserted_stage_does_not_cascade() {
        let a = json!([{"Stage": "Early", "Events": ["A"]}]);
        let b = json!([{"Stage": "Pre", "Events": ["Z"]}, {"Stage": "Early", "Events": ["A"]}]);
        let d = diffs(a, b);
        assert_eq!(d.len(), 1, "expected one insertion, got {d:?}");
        assert_eq!(d[0].1, None);
        assert!(d[0].0.ends_with("[+]"));
    }

    // Multiline strings (the Info blob) diff line-by-line, labelled by the `Key:` prefix.
    #[test]
    fn multiline_string_diffs_by_line_label() {
        let a = json!("Pos: (1, 2)\nVel: (3, 4)");
        let b = json!("Pos: (1, 2)\nVel: (9, 9)");
        assert_eq!(
            diffs(a, b),
            vec![("/Vel".to_string(), Some("Vel: (3, 4)".to_string()), Some("Vel: (9, 9)".to_string()))]
        );
    }

    // Call nodes align by name (LCS); a call inserted into the children of a shared call shows as one `[+]`.
    #[test]
    fn call_tree_nested_insertion_is_a_single_plus() {
        let a = json!({"Events": [{"Call": "Move", "Children": ["GetRunSpeed"]}]});
        let b = json!({"Events": [{"Call": "Move", "Children": ["DoMovement", "GetRunSpeed"]}]});
        let d = diffs(a, b);
        assert_eq!(d.len(), 1, "expected one insertion, got {d:?}");
        assert_eq!(d[0].1, None);
        assert_eq!(d[0].2.as_deref(), Some("DoMovement"));
    }
}
