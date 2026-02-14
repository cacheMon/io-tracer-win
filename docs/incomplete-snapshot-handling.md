# Incomplete Snapshot Handling

## Problem

When a user stopped the tracer while a snapshot was in progress, the system marked the incomplete snapshot as "complete" and uploaded it. This caused:

- Incomplete/misleading data in analysis
- Confusion about which files/processes were actually captured
- Wasted storage space for unusable partial data

## Solution

Added completion tracking to prevent incomplete snapshots from being uploaded:

### Implementation

**FilesystemSnapper**

- Added `snapshotCompleted` flag (default: `false`)
- Only set to `true` when all drives finish scanning without interruption
- Exposed via `IsSnapshotComplete()` method

**ProcessSnapper**

- Added `snapshotCompleted` flag (default: `false`)
- Only set to `true` when the Run loop exits (after Stop is called)
- This ensures at least some process data was captured before marking as complete
- Exposed via `IsSnapshotComplete()` method

**WriterManager**

- Updated `FinalizeFilesystemSnapshot(bool isComplete)` to accept completion status
  - If incomplete: deletes all filesystem snapshot part files and skips upload
  - If complete: renames with `_complete_parts{N}` marker and queues for upload
- Added `FinalizeProcessSnapshot(bool isComplete)` with same behavior
  - If incomplete: deletes all process snapshot files and skips upload
  - If complete: logs success message

**Tracer**

- Updated cleanup to check both `fsSnapper.IsSnapshotComplete()` and `psHandler.IsSnapshotComplete()`
- Calls respective finalization methods with `false` for incomplete snapshots to trigger cleanup

### Behavior

**Complete Snapshot**: Snapper runs normally → stopped gracefully → marked complete → files finalized and uploaded

**Interrupted Snapshot**: User stops early → snapper interrupted → marked incomplete → all snapshot files deleted → nothing uploaded
