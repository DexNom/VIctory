//! InitHeaders' stores to title memory, recorded on the card and written
//! into title memory by the host from that record
//! (`victory_shader::emit::recorded_writes_enabled`).
//!
//! InitHeaders takes its destinations from a table in title memory, so
//! nothing bound to it says where it stores, and every visible copy was
//! declared written: in the opening with ray tracing (RX 7900 XTX, run
//! d5-trace) the only broad raw writer left, 6-19 GB of declarations per
//! frame-rate sample, each copied back and compared. Its stores, traced on
//! the card: at most 96 a dispatch, 1,408 bytes. Most land outside the
//! acceleration-structure headers the host knows (run d6-headers), so the
//! record is the only exact account of them.
//!
//! The translated shader appends each executed store to the record in the
//! fault buffer (address, width, copy slot, offset, the dwords stored). After
//! each dispatch the record is copied to a read-back buffer and its count
//! reset; once the list has run, the stored dwords are written into title
//! memory in record order, as the copy-back would have put them there.
//!
//! The record is used only if it holds everything the shader can change
//! there ([`translated`]: no raw-address atomics), and only until it
//! overflows once: a dispatch whose record overflowed lost stores, which is
//! said once, and from then on the shader's writes are declared broadly
//! again ([`usable`]).

use std::sync::OnceLock;
use std::sync::atomic::{AtomicBool, AtomicU8, Ordering};

use victory_shader::emit::{
    RAW_STORE_PROBE_BASE, RAW_STORE_PROBE_CAPACITY, RAW_STORE_PROBE_DWORDS,
    RAW_STORE_PROBE_EVENT_DWORDS, RAW_STORE_PROBE_HASH, SLOT_FAULTS, recorded_writes_enabled,
};
use windows::Win32::Graphics::Direct3D12::*;

use crate::host::{HostList, host, transition};
use crate::pipeline::host::HostPipeline;

const OFFSET: u64 = RAW_STORE_PROBE_BASE as u64 * 4;
const HEADER_BYTES: usize = 8;
const ADDRESS_MASK: u64 = (1 << 40) - 1;

/// 0: InitHeaders not translated yet; 1: translated with a complete record;
/// 2: translated, but it changes title memory some way the record does not
/// hold.
static TRANSLATED: AtomicU8 = AtomicU8::new(0);
/// A record overflowed: stores were lost, the record is not used again.
static BROKEN: AtomicBool = AtomicBool::new(false);

/// InitHeaders was translated with its stores recorded; `complete`: the
/// record holds every change it can make to title memory.
pub(crate) fn translated(complete: bool) {
    TRANSLATED.store(if complete { 1 } else { 2 }, Ordering::Relaxed);
    if !complete {
        static ONCE: std::sync::Once = std::sync::Once::new();
        ONCE.call_once(|| {
            tracing::warn!(
                shader = RAW_STORE_PROBE_HASH,
                "InitHeaders changes title memory through atomics too: its writes stay declared broadly"
            );
        });
    }
}

/// Whether the shader `hash`'s writes to title memory are taken from its
/// record instead of declared: InitHeaders, translated with a complete
/// record, which has not overflowed.
pub(crate) fn usable(hash: &str) -> bool {
    hash == RAW_STORE_PROBE_HASH
        && recorded_writes_enabled()
        && TRANSLATED.load(Ordering::Relaxed) == 1
        && !BROKEN.load(Ordering::Relaxed)
}

/// After a dispatch of InitHeaders: its record copied out and reset, and
/// once the list has run, its stores written into title memory. Also after
/// dispatches whose writes were declared broadly (indirect ones, or after
/// an overflow): the record must not carry their stores into the next.
pub(crate) fn after_dispatch(list: &HostList, pipeline: &HostPipeline) {
    if !recorded_writes_enabled() || TRANSLATED.load(Ordering::Relaxed) == 0 {
        return;
    }
    let Some(stage) = pipeline.stages.first().filter(|stage| stage.shader.hash == RAW_STORE_PROBE_HASH) else {
        return;
    };
    if !stage.translated().host.root_uavs.contains(&SLOT_FAULTS) {
        return;
    }
    let faults = {
        let guard = super::MEMORY.lock().unwrap_or_else(|e| e.into_inner());
        let Some(memory) = guard.as_ref() else { return };
        memory.faults.0.clone()
    };
    let Some(readback) = snapshot_and_reset(list, &faults) else {
        // Without the record the stores would be lost: from now on the
        // writes are declared again (this dispatch's were not).
        if !BROKEN.swap(true, Ordering::Relaxed) {
            tracing::warn!(shader = RAW_STORE_PROBE_HASH, "no read-back buffer for InitHeaders' store record: its writes are declared broadly from now on");
        }
        return;
    };
    list.defer_after(move || apply(&readback));
}

fn snapshot_and_reset(list: &HostList, faults: &ID3D12Resource) -> Option<ID3D12Resource> {
    let host = host()?;
    static ZERO_HEADER: OnceLock<Option<ID3D12Resource>> = OnceLock::new();
    let zero = ZERO_HEADER.get_or_init(|| {
        let (buffer, mapped) = host.upload_buffer(HEADER_BYTES as u64)?;
        // SAFETY: a private, fresh allocation that is read-only after initialization.
        unsafe { mapped.write_bytes(0, HEADER_BYTES) };
        Some(buffer)
    }).as_ref()?;
    let bytes = u64::from(RAW_STORE_PROBE_DWORDS) * 4;
    let readback = host.read_back_buffer(bytes)?;
    let uav = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    let source = D3D12_RESOURCE_STATE_COPY_SOURCE;
    let dest = D3D12_RESOURCE_STATE_COPY_DEST;
    // SAFETY: the dispatch just recorded used this buffer as a UAV; the copy
    // and the reset follow it in the same list. Only the record's count and
    // overflow flag are reset, leaving the fault pages untouched.
    unsafe {
        list.list.ResourceBarrier(&[transition(faults, uav, source)]);
        list.list.CopyBufferRegion(&readback, 0, faults, OFFSET, bytes);
        list.list.ResourceBarrier(&[transition(faults, source, dest)]);
        list.list.CopyBufferRegion(faults, OFFSET, zero, 0, HEADER_BYTES as u64);
        list.list.ResourceBarrier(&[transition(faults, dest, D3D12_RESOURCE_STATE_COMMON)]);
    }
    list.keep(&readback);
    list.keep(faults);
    list.keep(zero);
    Some(readback)
}

/// One recorded store: where, how many bytes, the dwords.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct Store {
    address: u64,
    width: u32,
    slot: u32,
    values: [u32; 4],
}

/// The record's stores, in the order they were recorded, and whether it
/// overflowed (stores lost). Stores that cannot be what a store wrote
/// (unaligned, an odd width, no copy) are left out.
fn decode(words: &[u32]) -> (Vec<Store>, bool) {
    let attempted = words[0];
    let stored = attempted.min(RAW_STORE_PROBE_CAPACITY) as usize;
    let stride = RAW_STORE_PROBE_EVENT_DWORDS as usize;
    let stores = words[2..2 + stored * stride]
        .chunks_exact(stride)
        .map(|e| Store {
            address: (u64::from(e[0]) | (u64::from(e[1]) << 32)) & ADDRESS_MASK,
            width: e[2],
            slot: e[3],
            values: [e[5], e[6], e[7], e[8]],
        })
        .filter(|s| s.address % 4 == 0 && matches!(s.width, 4 | 8 | 12 | 16) && s.slot != 0)
        .collect();
    (stores, words[1] != 0 || attempted > RAW_STORE_PROBE_CAPACITY)
}

/// Once the list has run: the record's stores into title memory.
fn apply(readback: &ID3D12Resource) {
    let mut data = std::ptr::null_mut();
    let bytes = RAW_STORE_PROBE_DWORDS as usize * 4;
    let range = D3D12_RANGE { Begin: 0, End: bytes };
    // SAFETY: the list holding the copy into this buffer has run.
    let words: Vec<u32> = unsafe {
        if readback.Map(0, Some(&range), Some(&mut data)).is_err() || data.is_null() {
            return;
        }
        let words = std::slice::from_raw_parts(data.cast::<u32>(), RAW_STORE_PROBE_DWORDS as usize).to_vec();
        readback.Unmap(0, Some(&D3D12_RANGE::default()));
        words
    };
    let (stores, overflow) = decode(&words);
    if overflow && !BROKEN.swap(true, Ordering::Relaxed) {
        tracing::warn!(
            shader = RAW_STORE_PROBE_HASH, capacity = RAW_STORE_PROBE_CAPACITY,
            "InitHeaders' store record overflowed: stores were lost; its writes are declared broadly from now on"
        );
    }
    for store in stores {
        let at = store.address as usize;
        let n = store.width as usize;
        let mut bytes = [0u8; 16];
        for (i, value) in store.values.iter().enumerate() {
            bytes[4 * i..4 * i + 4].copy_from_slice(&value.to_le_bytes());
        }
        // No mapping changes while the bytes go in; the guards over them are
        // opened, as for the copy-back's writes.
        let _mapped = crate::watch::title_memory_reading_range(at..at + n);
        crate::watch::writing(at, n);
        crate::host::write_mapped(at as *mut u8, bytes.as_ptr(), n);
    }
    #[cfg(feature = "diagnostics")]
    {
        use std::sync::atomic::AtomicU64;
        static DISPATCHES: AtomicU64 = AtomicU64::new(0);
        let count = DISPATCHES.fetch_add(1, Ordering::Relaxed) + 1;
        if count.is_power_of_two() {
            tracing::info!(shader = RAW_STORE_PROBE_HASH, dispatches = count, stores = words[0], overflow,
                "InitHeaders' stores written into title memory from its record");
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decode_keeps_valid_stores_in_order_and_reports_overflow() {
        let stride = RAW_STORE_PROBE_EVENT_DWORDS as usize;
        let mut words = vec![0u32; RAW_STORE_PROBE_DWORDS as usize];
        words[0] = 3;
        let entries = [
            [0x2345_fff0, 0x101, 16, 5, 0x20010, 1, 2, 3, 4],
            [0x1002, 0, 4, 5, 0, 9, 0, 0, 0], // Unaligned: left out.
            [0x1000, 0, 8, 2, 0, 7, 8, 0xdead, 0xbeef],
        ];
        for (i, e) in entries.iter().enumerate() {
            words[2 + i * stride..2 + (i + 1) * stride].copy_from_slice(e);
        }
        let (stores, overflow) = decode(&words);
        assert!(!overflow);
        assert_eq!(stores, vec![
            Store { address: 0x01_2345_fff0, width: 16, slot: 5, values: [1, 2, 3, 4] },
            Store { address: 0x1000, width: 8, slot: 2, values: [7, 8, 0xdead, 0xbeef] },
        ]);
        words[0] = RAW_STORE_PROBE_CAPACITY + 1;
        assert!(decode(&words).1);
        words[0] = 1;
        words[1] = 1;
        assert!(decode(&words).1);
    }
}
