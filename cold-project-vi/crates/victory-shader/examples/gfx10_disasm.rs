//! Disassembles a raw GFX10 code blob: `cargo run -p victory-shader --example gfx10_disasm -- <file> [64]`.
fn main() {
    let path = std::env::args().nth(1).expect("path");
    let wave64 = std::env::args().nth(2).is_some_and(|a| a == "64");
    let code = std::fs::read(path).unwrap();
    match victory_shader::gfx10::decode(&code) {
        Ok(ins) => for i in ins { println!("{:04x}: {}", i.offset, i.text(wave64)); },
        Err(e) => {
            println!("decode error: {e:?}");
        }
    }
}
