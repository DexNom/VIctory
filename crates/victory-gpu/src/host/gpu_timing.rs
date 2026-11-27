//! Completed intervals on the main direct queue, not CPU fence waits or
//! whole-device utilization. No extra signal or CPU wait is introduced.

use std::collections::VecDeque;
use std::time::Duration;

use windows::Win32::Graphics::Direct3D12::*;
use windows::Win32::Graphics::Dxgi::Common::DXGI_SAMPLE_DESC;
use windows::core::Interface;

fn elapsed(first: u64, last: u64, frequency: u64) -> Option<Duration> {
    let ticks = last.checked_sub(first)?;
    if frequency == 0 {
        return None;
    }
    let nanos = u128::from(ticks % frequency) * 1_000_000_000 / u128::from(frequency);
    Some(Duration::new(ticks / frequency, nanos as u32))
}

struct Slot {
    begin: ID3D12CommandList,
    end: ID3D12CommandList,
    readback: ID3D12Resource,
    // Command lists do not retain all the objects their commands name.
    _queries: ID3D12QueryHeap,
    _allocators: [ID3D12CommandAllocator; 2],
}

impl Slot {
    fn new(device: &ID3D12Device) -> Option<Self> {
        let mut queries = None;
        let mut readback = None;
        // SAFETY: local descriptions and typed output pointers. Both query
        // slots and their 16-byte destination remain alive with this slot.
        unsafe {
            device
                .CreateQueryHeap(
                    &D3D12_QUERY_HEAP_DESC {
                        Type: D3D12_QUERY_HEAP_TYPE_TIMESTAMP,
                        Count: 2,
                        NodeMask: 0,
                    },
                    &mut queries,
                )
                .ok()?;
            device
                .CreateCommittedResource(
                    &D3D12_HEAP_PROPERTIES {
                        Type: D3D12_HEAP_TYPE_READBACK,
                        ..Default::default()
                    },
                    D3D12_HEAP_FLAG_NONE,
                    &D3D12_RESOURCE_DESC {
                        Dimension: D3D12_RESOURCE_DIMENSION_BUFFER,
                        Width: 16,
                        Height: 1,
                        DepthOrArraySize: 1,
                        MipLevels: 1,
                        SampleDesc: DXGI_SAMPLE_DESC {
                            Count: 1,
                            Quality: 0,
                        },
                        Layout: D3D12_TEXTURE_LAYOUT_ROW_MAJOR,
                        ..Default::default()
                    },
                    D3D12_RESOURCE_STATE_COPY_DEST,
                    None,
                    &mut readback,
                )
                .ok()?;
            let queries = queries?;
            let readback = readback?;
            let first: ID3D12CommandAllocator = device
                .CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT)
                .ok()?;
            let last: ID3D12CommandAllocator = device
                .CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT)
                .ok()?;
            let begin: ID3D12GraphicsCommandList = device
                .CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, &first, None)
                .ok()?;
            let end: ID3D12GraphicsCommandList = device
                .CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, &last, None)
                .ok()?;
            begin.EndQuery(&queries, D3D12_QUERY_TYPE_TIMESTAMP, 0);
            begin.Close().ok()?;
            end.EndQuery(&queries, D3D12_QUERY_TYPE_TIMESTAMP, 1);
            end.ResolveQueryData(&queries, D3D12_QUERY_TYPE_TIMESTAMP, 0, 2, &readback, 0);
            end.Close().ok()?;
            Some(Self {
                begin: begin.cast().ok()?,
                end: end.cast().ok()?,
                readback,
                _queries: queries,
                _allocators: [first, last],
            })
        }
    }

    /// Only called after the owning submission's existing fence completed.
    fn read(&self, frequency: u64) -> Option<Duration> {
        let mut data = std::ptr::null_mut();
        // SAFETY: a completed ResolveQueryData wrote two aligned u64s. The
        // slot cannot be reissued while the CPU reads its result.
        unsafe {
            self.readback
                .Map(0, Some(&D3D12_RANGE { Begin: 0, End: 16 }), Some(&mut data))
                .ok()?;
            let values = std::ptr::NonNull::new(data.cast::<u64>())
                .map(|p| (p.as_ptr().read(), p.as_ptr().add(1).read()));
            self.readback
                .Unmap(0, Some(&D3D12_RANGE { Begin: 0, End: 0 }));
            let (first, last) = values?;
            elapsed(first, last, frequency)
        }
    }
}

#[derive(Default)]
pub(super) struct Window {
    pub samples: u64,
    pub elapsed: Duration,
    pub pending: usize,
    pub skipped: u64,
    pub invalid: u64,
}

impl Window {
    pub fn summary(&self) -> String {
        let coverage = format!(
            "{} completed, {} pending, {} skipped, {} invalid",
            self.samples, self.pending, self.skipped, self.invalid
        );
        if self.samples == 0 {
            format!("GPU queue n/a ({coverage})")
        } else {
            format!(
                "GPU queue {:.3} ms/batch ({coverage}; {:.3} ms total)",
                self.elapsed.as_secs_f64() * 1e3 / self.samples as f64,
                self.elapsed.as_secs_f64() * 1e3
            )
        }
    }
}

pub(super) struct QueueTimer {
    device: ID3D12Device,
    fence: ID3D12Fence,
    frequency: u64,
    capacity: usize,
    spare: Vec<Slot>,
    pending: VecDeque<(u64, Slot)>,
    window: Window,
}

impl QueueTimer {
    /// The caller serializes submissions to this direct queue and keeps
    /// using the same monotonically increasing completion fence.
    pub fn new(
        device: &ID3D12Device,
        queue: &ID3D12CommandQueue,
        fence: &ID3D12Fence,
        capacity: usize,
    ) -> Option<Self> {
        // SAFETY: a plain query on the selected queue, not a CPU clock.
        let frequency = unsafe { queue.GetTimestampFrequency().ok()? };
        if frequency == 0 || capacity == 0 {
            return None;
        }
        Some(Self {
            device: device.clone(),
            fence: fence.clone(),
            frequency,
            capacity,
            spare: Vec::new(),
            pending: VecDeque::new(),
            window: Window::default(),
        })
    }

    fn poll(&mut self) {
        // SAFETY: polling does not wait. Device removal is not completion.
        let completed = unsafe { self.fence.GetCompletedValue() };
        if completed == u64::MAX {
            return;
        }
        while self
            .pending
            .front()
            .is_some_and(|(fence, _)| *fence <= completed)
        {
            let (_, slot) = self.pending.pop_front().expect("checked above");
            if let Some(elapsed) = slot.read(self.frequency) {
                self.window.samples += 1;
                self.window.elapsed = self.window.elapsed.saturating_add(elapsed);
            } else {
                self.window.invalid += 1;
            }
            self.spare.push(slot);
        }
    }

    /// Bracket an unchanged ordered list array. `completion` must be
    /// signalled by the caller after this array, as for an untimed submit.
    /// At capacity, fall back to the original array without waiting.
    pub fn bracket(
        &mut self,
        lists: &[Option<ID3D12CommandList>],
        completion: u64,
    ) -> Option<Vec<Option<ID3D12CommandList>>> {
        self.poll();
        let slot = self.spare.pop().or_else(|| {
            (self.pending.len() < self.capacity)
                .then(|| Slot::new(&self.device))
                .flatten()
        });
        let Some(slot) = slot else {
            self.window.skipped += 1;
            return None;
        };
        let mut bracketed = Vec::with_capacity(lists.len() + 2);
        bracketed.push(Some(slot.begin.clone()));
        bracketed.extend_from_slice(lists);
        bracketed.push(Some(slot.end.clone()));
        self.pending.push_back((completion, slot));
        Some(bracketed)
    }

    /// Completed batches in this observation window may have been sent
    /// in an earlier frame. Never divide this sum by current-frame counts.
    pub fn take_window(&mut self) -> Window {
        self.poll();
        let mut window = std::mem::take(&mut self.window);
        window.pending = self.pending.len();
        window
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows::Win32::Foundation::CloseHandle;
    use windows::Win32::System::Threading::{CreateEventW, WaitForSingleObject};

    #[test]
    fn timestamp_conversion_preserves_fractions_and_rejects_invalid_queries() {
        assert_eq!(elapsed(10, 11, 3), Some(Duration::new(0, 333_333_333)));
        assert_eq!(elapsed(0, u64::MAX, 1), Some(Duration::from_secs(u64::MAX)));
        assert_eq!(elapsed(20, 19, 1000), None);
        assert_eq!(elapsed(0, 1, 0), None);
        assert_eq!(elapsed(9, 9, 1000), Some(Duration::ZERO));
        assert!(Window::default().summary().contains("n/a"));
    }

    /// A GPU gate prevents completion while the CPU continues. The timer
    /// must neither read unresolved queries nor wait when its pool is full.
    /// Delaying CPU observation after completion must not extend GPU time.
    #[test]
    fn cpu_delay_and_queue_backpressure_do_not_become_gpu_execution_time() {
        let host = crate::host::host().expect("D3D12 device");
        // SAFETY: private queue, resources, fence, and event; no game data.
        unsafe {
            let queue: ID3D12CommandQueue = host
                .device
                .CreateCommandQueue(&D3D12_COMMAND_QUEUE_DESC {
                    Type: D3D12_COMMAND_LIST_TYPE_DIRECT,
                    ..Default::default()
                })
                .unwrap();
            let done: ID3D12Fence = host.device.CreateFence(0, D3D12_FENCE_FLAG_NONE).unwrap();
            let gate: ID3D12Fence = host.device.CreateFence(0, D3D12_FENCE_FLAG_NONE).unwrap();
            struct ReleaseGate(ID3D12Fence);
            impl Drop for ReleaseGate {
                fn drop(&mut self) {
                    // SAFETY: always unblock the private queue, even on panic.
                    let _ = unsafe { self.0.Signal(1) };
                }
            }
            let release = ReleaseGate(gate.clone());
            let (release_tx, release_rx) = std::sync::mpsc::channel();
            let signal = std::thread::spawn(move || {
                // A regression that waits inside bracket() must fail rather
                // than leaving the test queue blocked forever.
                if release_rx.recv_timeout(Duration::from_secs(5)).is_ok() {
                    std::thread::sleep(Duration::from_millis(40));
                }
                drop(release);
            });
            let mut timer = QueueTimer::new(&host.device, &queue, &done, 2).unwrap();
            let bytes = 1 << 20;
            let (input, mapped) = host.upload_buffer(bytes).unwrap();
            let output = host.read_back_buffer(bytes).unwrap();
            std::ptr::write_bytes(mapped, 0x5a, bytes as usize);
            let mut allocators = Vec::new();
            let mut bodies = Vec::new();
            for _ in 0..2 {
                let allocator: ID3D12CommandAllocator = host
                    .device
                    .CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT)
                    .unwrap();
                let body: ID3D12GraphicsCommandList = host
                    .device
                    .CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, &allocator, None)
                    .unwrap();
                body.CopyBufferRegion(&output, 0, &input, 0, bytes);
                body.Close().unwrap();
                bodies.push(body.cast::<ID3D12CommandList>().unwrap());
                allocators.push(allocator);
            }
            queue.Wait(&gate, 1).unwrap();
            for value in 1..=2 {
                let work = [Some(bodies[value as usize - 1].clone())];
                let bracketed = timer.bracket(&work, value).unwrap();
                queue.ExecuteCommandLists(&bracketed);
                queue.Signal(&done, value).unwrap();
            }
            let work = [Some(bodies[0].clone())];
            assert!(timer.bracket(&work, 3).is_none());
            let pending = timer.take_window();
            assert_eq!(
                (pending.samples, pending.pending, pending.skipped),
                (0, 2, 1)
            );
            assert!(pending.summary().contains("n/a"));
            let event = CreateEventW(None, false, false, None).unwrap();
            done.SetEventOnCompletion(2, event).unwrap();
            release_tx.send(()).unwrap();
            let (waited, cpu_wait) =
                super::super::measure_cpu_wait(|| WaitForSingleObject(event, 10_000));
            signal.join().unwrap();
            CloseHandle(event).unwrap();
            assert_eq!(waited, windows::Win32::Foundation::WAIT_OBJECT_0);
            let completed = timer.take_window();
            assert_eq!(
                (completed.samples, completed.pending, completed.invalid),
                (2, 0, 0)
            );
            assert_eq!(timer.take_window().samples, 0, "count each completion once");
            let before = timer.spare[0].read(timer.frequency).unwrap();
            std::thread::sleep(Duration::from_millis(40));
            assert_eq!(timer.spare[0].read(timer.frequency), Some(before));
            let mut data = std::ptr::null_mut();
            output
                .Map(
                    0,
                    Some(&D3D12_RANGE {
                        Begin: 0,
                        End: bytes as usize,
                    }),
                    Some(&mut data),
                )
                .unwrap();
            assert!(
                std::slice::from_raw_parts(data.cast::<u8>(), bytes as usize)
                    .iter()
                    .all(|&byte| byte == 0x5a)
            );
            output.Unmap(0, Some(&D3D12_RANGE { Begin: 0, End: 0 }));
            let again = timer
                .bracket(&work, 3)
                .expect("completed slots can be reused");
            queue.ExecuteCommandLists(&again);
            queue.Signal(&done, 3).unwrap();
            let event = CreateEventW(None, false, false, None).unwrap();
            done.SetEventOnCompletion(3, event).unwrap();
            let waited = WaitForSingleObject(event, 10_000);
            CloseHandle(event).unwrap();
            assert_eq!(waited, windows::Win32::Foundation::WAIT_OBJECT_0);
            assert_eq!(timer.take_window().samples, 1);
            let adapter = host
                .adapter
                .as_ref()
                .and_then(|adapter| adapter.GetDesc1().ok())
                .map(|desc| {
                    let end = desc
                        .Description
                        .iter()
                        .position(|&c| c == 0)
                        .unwrap_or(desc.Description.len());
                    String::from_utf16_lossy(&desc.Description[..end])
                })
                .unwrap_or_else(|| "unknown adapter".to_owned());
            println!(
                "{adapter}: synthetic CPU fence wait {:.3} ms; GPU timestamps {:.3} ms across 2 batches; delayed CPU read preserved the GPU result",
                cpu_wait.as_secs_f64() * 1e3,
                completed.elapsed.as_secs_f64() * 1e3,
            );
        }
    }
}
