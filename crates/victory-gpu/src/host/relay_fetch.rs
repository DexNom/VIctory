use windows::Win32::Foundation::{E_FAIL, WAIT_EVENT, WAIT_FAILED, WAIT_OBJECT_0};
use windows::Win32::Graphics::Dxgi::DXGI_ERROR_DEVICE_REMOVED;
use windows::core::{Error, Result};

/// Call immediately after the native wait so a failure retains its Win32 error.
pub(super) fn confirm_completion(
    wait: WAIT_EVENT,
    submitted: u64,
    completed_value: impl FnOnce() -> u64,
) -> Result<()> {
    if wait == WAIT_FAILED {
        return Err(Error::from_thread());
    }
    if wait != WAIT_OBJECT_0 {
        return Err(Error::new(
            E_FAIL,
            format!("relay readback event wait returned {:#x}", wait.0),
        ));
    }
    let completed = completed_value();
    // Device removal also wakes pending fence events.
    if completed == u64::MAX {
        return Err(Error::from_hresult(DXGI_ERROR_DEVICE_REMOVED));
    }
    if completed < submitted {
        return Err(Error::new(
            E_FAIL,
            format!("relay readback fence completed {completed}, expected at least {submitted}"),
        ));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use std::os::windows::io::{FromRawHandle, OwnedHandle};

    use windows::Win32::Foundation::{
        ERROR_INVALID_HANDLE, HANDLE, WAIT_EVENT, WAIT_OBJECT_0, WAIT_TIMEOUT,
    };
    use windows::Win32::Graphics::Dxgi::DXGI_ERROR_DEVICE_REMOVED;
    use windows::Win32::System::Threading::{CreateEventW, WaitForSingleObject};
    use windows::core::{HRESULT, Result};

    use super::confirm_completion;

    const SUBMITTED: u64 = 7;
    const QUEUE_ORDER: u64 = 3;

    #[derive(Debug, Default, PartialEq, Eq)]
    struct Readback {
        value: Option<u32>,
        retired_through: Option<u64>,
    }

    impl Readback {
        fn finish(&mut self, wait: WAIT_EVENT, completed: impl FnOnce() -> u64) -> Result<()> {
            confirm_completion(wait, SUBMITTED, completed)?;
            self.value = Some(42);
            self.retired_through = Some(QUEUE_ORDER);
            Ok(())
        }
    }

    #[test]
    fn signaled_wait_at_or_past_the_target_allows_readback_and_retirement() {
        for completed in [SUBMITTED, SUBMITTED + 1] {
            // SAFETY: create a private, initially signaled auto-reset event.
            let event = unsafe { CreateEventW(None, false, true, None) }.unwrap();
            // SAFETY: this test owns the newly created handle.
            let _owned = unsafe { OwnedHandle::from_raw_handle(event.0) };
            let mut readback = Readback::default();
            // SAFETY: the owned event is live throughout the wait.
            let wait = unsafe { WaitForSingleObject(event, 0) };
            readback.finish(wait, || completed).unwrap();
            assert_eq!(
                readback,
                Readback {
                    value: Some(42),
                    retired_through: Some(QUEUE_ORDER),
                }
            );
        }
    }

    #[test]
    fn failed_wait_preserves_the_win32_error_and_blocks_readback() {
        let mut readback = Readback::default();
        // SAFETY: a null handle is rejected by Win32 without dereferencing it.
        let wait = unsafe { WaitForSingleObject(HANDLE::default(), 0) };
        let result = readback.finish(wait, || panic!("failed wait queried the fence"));
        assert_eq!(readback, Readback::default());
        assert_eq!(
            result.unwrap_err().code(),
            HRESULT::from_win32(ERROR_INVALID_HANDLE.0)
        );
    }

    #[test]
    fn nonsignaled_wait_blocks_readback() {
        let mut readback = Readback::default();
        let result = readback.finish(WAIT_TIMEOUT, || panic!("timeout queried the fence"));
        assert_eq!(readback, Readback::default());
        assert!(result.is_err());
    }

    #[test]
    fn signaled_wait_with_incomplete_fence_blocks_readback() {
        let mut readback = Readback::default();
        let result = readback.finish(WAIT_OBJECT_0, || SUBMITTED - 1);
        assert_eq!(readback, Readback::default());
        assert!(result.is_err());
    }

    #[test]
    fn device_removal_is_not_completion() {
        let mut readback = Readback::default();
        let result = readback.finish(WAIT_OBJECT_0, || u64::MAX);
        assert_eq!(readback, Readback::default());
        assert_eq!(result.unwrap_err().code(), DXGI_ERROR_DEVICE_REMOVED);
    }
}
