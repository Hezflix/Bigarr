# State machines

Every piece of Bigarr that moves through states: the explicit ones (an enum with a transition method) and the
implicit ones (a nullable timestamp, a counter, an in-memory set, a file on disk next to a database row). For each:
where the state lives, the transitions and who performs them, the invariants the rest of the code relies on, and the
ways the code can break them.

**Keep this file current.** A change that adds a state, a transition, a new writer of an existing state, or a
background worker belongs here in the same pull request. When a finding in [Findings](#findings) is fixed, move it to
"Fixed" with the test that guards it.

Line numbers are those of the commit that last touched the section; symbols are given wherever a file changes often.

- [Ground rules](#ground-rules)
- [Acquisition](#acquisition): grab, queue poll, import queue, version status
- [Events and notifications](#events-and-notifications): outbox, webhook delivery, media-server refresh, compat command
- [Background runners](#background-runners): scheduled job, health, scans and imports, search queues
- [Files on disk](#files-on-disk): file rows, transfer, series order, held releases, blocklist
- [Playback](#playback): session, play, listener, backstop
- [Shadow follow](#shadow-follow): followed grab, checkpoint, memory, pick view
- [Auth and UI](#auth-and-ui)
- [Findings](#findings): fixed, open, checked and sound

## Ground rules

Facts that hold across the app and decide which races are possible:

- **One process, SQLite, no concurrency tokens.** No entity has a row version. Two `DbContext`s that loaded the same
  row both save; the last write wins, column by column (EF only writes the columns it saw change).
- **Every background worker is a single hosted instance.** State holders are singletons (`Program.cs`). Races are
  therefore always worker against HTTP request, worker against another worker, or request against request, never two
  copies of the same worker.
- **Three kinds of state storage**, with different failure modes:
  - a database column: survives a restart, can be left mid-transition by a crash;
  - process memory: lost on restart, can never be stuck across one;
  - a file on disk paired with a row: the two are never written atomically.
- **State ownership** is the pattern that keeps the lock-free code correct: a state is written by exactly one
  component, or a transition out of it is a conditional update. Every finding below is a place where two components
  write the same state, or where disk and database are written in the wrong order.
- **Request cancellation is a transition trigger.** Endpoints pass `HttpContext.RequestAborted` down; a closed tab or
  a proxy timeout cancels whatever `await` comes next. Anything that changed the outside world (a file moved, a
  webhook accepted) before that `await` is not rolled back.

## Acquisition

### Grab

Explicit. `GrabState` on `Grab.State` (`src/Bigarr.Domain/Acquisition/GrabState.cs`), changed only through
`Grab.TransitionTo`, which refuses to leave a terminal state and mirrors `IsActive` onto every `GrabTarget.Active`.

| State | Meaning | Active | Terminal |
|---|---|---|---|
| `Grabbed` | sent to the download client | yes | |
| `Downloading` | seen in the client | yes | |
| `ImportPending` | complete in the client, waiting for the import worker | yes | |
| `Importing` | the import worker is working on it | yes | |
| `ImportFailed` | needs a human; retried or imported by hand | | |
| `Imported` | file in the library | | yes |
| `DownloadFailed` | client error or stall; blocklisted, searched again | | yes |
| `Removed` | vanished from the client | | yes |
| `Ignored` | dismissed by the user | | yes |

`ImportFailed` is neither active nor terminal: the target is free for another grab, and the grab can still move.

| From | To | Trigger | Guard | Performed by |
|---|---|---|---|---|
| none | `Grabbed` | grab (user, search, RSS) | no active grab on the target: check, then the partial unique index `IX_GrabTargets_TargetId_Active` | `GrabService.GrabAsync` / `GrabEpisodesAsync` |
| none | `ImportPending` | a complete torrent of our category with no grab row | download id unknown to every grab | `QueuePoller.AdoptCompletedAsync` |
| `Grabbed` | `Downloading` | client lists it, not complete, not failed | | `QueuePoller.Advance` |
| `Grabbed`, `Downloading` | `ImportPending` | client reports complete | | `QueuePoller.Advance` |
| `Grabbed`, `Downloading` | `DownloadFailed` | client reports failed, or stalled past `StalledTimeout` | | `QueuePoller.HandleFailureAsync` |
| `Grabbed`, `Downloading`, `ImportPending` | `Removed` | absent from an answering client for `RemovedAfterPolls` polls | a `Grabbed` grab younger than 45 s is not counted; never from `Importing` | `QueuePoller.PollCoreAsync` |
| `ImportPending` | `Importing` | import worker dequeues it | one conditional update, under the poll lock | `ImportService.ImportGrabAsync` |
| `Importing` | `Imported` | file transferred and recorded | same transaction as the file row | `ImportService.ExecuteAsync` (film), `ImportGrabAsync` (series) |
| `Importing` | `ImportFailed` | import refused or crashed | not terminal | `ImportService.FailAsync` / `FailSeriesAsync` |
| `Importing` | `ImportPending` | client unreachable or torrent not complete after all | still `Importing` | `ImportService.ImportGrabAsync` (deferred) |
| `Importing` | `ImportPending` | process start, or the worker crashed outside its failure handling | still `Importing` (conditional update) | `InterruptedImports` |
| `ImportFailed` | `ImportPending` | user retries | | `ImportService.RetryAsync` |
| `ImportFailed` | `Imported` | user imports by hand | | `ManualImportService`, `SeriesManualImportService.ImportAsync` |
| any non-terminal except `Importing` | `Ignored` | user removes the queue row | under the poll lock; 409 while `Importing` | `QueueService.RemoveGrabAsync` |

Ownership: `Grabbed` and `Downloading` belong to the poller; `Importing` belongs to the import worker, and neither the
poller nor the user writes a grab in that state. The three writers that can meet on a grab (the poll, the user's
removal, the worker's claim) all run under `QueueState.PollLock`, so none saves over a state another just wrote
([F7](#f7-a-terminal-grab-state-could-be-overwritten-by-a-stale-writer)). The retry and the manual import of an
`ImportFailed` grab are not under that lock: two of those at once on one grab is still a race.

Invariants:

1. A terminal state is never left. Enforced per object by `TransitionTo`, and across contexts by the ownership rules
   above (F7).
2. `GrabTarget.Active == Grab.State.IsActive()` for every target of the grab.
3. At most one active grab per target (unique index; `GrabService` removes the torrent again when the index refuses).
4. `Importing` is held only while the import worker is on that grab. Guarded since [F2](#f2-a-grab-interrupted-mid-import-stayed-importing-for-ever).
5. An `ImportPending` grab whose download is complete is queued at every poll until it leaves that state.
6. Failed handling commits state, blocklist, history and event together; the torrent leaves its client and the
   replacement search is queued only after that commit ([F21](#f21-failed-handling-removed-the-torrent-before-its-own-save)).

A failed grab whose torrent has left the client stays `ImportFailed` or `DownloadFailed` in the table and is simply no
longer shown (`QueuePoller.cs:173-177`). That is by design.

### Queue poll bookkeeping

Implicit, in memory (`QueueState`): per grab, a count of consecutive polls where the client did not list it, and the
time a stall began.

| From | To | Trigger |
|---|---|---|
| unseen | counted (n) | active grab absent from an answering client (`CountMiss`) |
| counted | unseen | client lists it again (`Seen`) |
| counted (n = `RemovedAfterPolls`) | forgotten | grab becomes `Removed` |
| not stalled | stalled since t | client reports `Stalled` |
| stalled | not stalled | any other client status (`NotStalled`) |

Invariants: one poll at a time (`QueueState.PollLock`); a client that did not answer counts neither as a miss nor as
a stall. Both counters restart from zero after a restart, so a stall or an absence is measured again from the first
poll: never a false failure, at worst a later one.

### Import queue

Implicit, in memory (`ImportQueue`): a set of pending grab ids and a single-reader channel.

| From | To | Trigger |
|---|---|---|
| absent | pending | `TryEnqueue` (poller, adoption, retry); false when already pending |
| pending | absent | worker finished, in `finally` (`ImportRunner`) |

Invariants: an id in the set is in the channel or being imported; one import at a time. The queue is lost on restart;
invariant 5 of the grab refills it.

### Version status

Derived, never stored (`Availability.StatusOf`): `Available` if the version has a file row, else `Downloading` if it
has an active grab, else `Missing` or `Pending` from the film's dates and the version's minimum availability. It
trusts the file row and never looks at the disk; see [Files on disk](#files-on-disk).

## Events and notifications

### Outbox event

Implicit: `OutboxEvent.DispatchedAt`.

| From | To | Trigger | Performed by |
|---|---|---|---|
| none | undispatched | a slice publishes, in its own unit of work | `OutboxWriter.Publish` |
| undispatched | dispatched | dispatcher creates the deliveries and stamps the row in one save, then publishes in memory | `OutboxDispatcher` |
| dispatched | purged | retention | `MediaMaintenance` |

Invariants: an event commits with the change it describes or not at all; deliveries are created exactly once; the
in-memory stream (UI, compat hub, media-server refresh) only ever sees committed events, at most once.

### Webhook delivery

Derived from three columns (`WebhookDelivery.State`): `Delivered` when `DeliveredAt` is set, `GivenUp` when it is not
and `NextAttemptAt` is null, `Pending` otherwise.

| From | To | Trigger | Performed by |
|---|---|---|---|
| none | `Pending` | dispatch | `OutboxDispatcher` |
| `Pending` | `Delivered` | 2xx | `WebhookSender` |
| `Pending` | `Pending`, attempts + 1, later | failure below `MaxAttempts` | `WebhookSender` |
| `Pending` | `Pending`, attempt given back | 429; the endpoint's other due deliveries wait too | `WebhookSender` |
| `Pending` | `GivenUp` | attempts reach `MaxAttempts`, endpoint disabled, or the endpoint's format has nothing for the event | `WebhookSender` |
| `Pending`, `GivenUp` | `Pending`, attempts 0, due now | user retries; a sender holding the delivery is told through `WebhookSignal` and applies the retry to its own copy | `NotificationService.RetryAsync` |
| `Pending` (waiting) | `Pending` (due now) | process start | `WebhookSender.ReplayPendingAsync` |

Invariants: `NextAttemptAt` is null once delivered or given up; a delivery the receiver accepted is never sent again
(guarded since [F4](#f4-an-interrupted-webhook-batch-re-sent-deliveries-already-accepted)); delivery is at least once.

### Media-server refresh

Implicit, in memory: a work item per (server, folder) with an attempt number 0 to 4, retried after 30 s, 2 min,
10 min, 30 min (`MediaServerRefresher`). The outcome lands on the server row (`LastRefreshAt`, `LastError`,
`LastErrorAt`), which is what `MediaServer.IsFailing` and the health check read.

Invariants: the single worker loop never ends (guarded since [F3](#f3-deleting-a-media-server-mid-refresh-stopped-every-later-refresh));
a deleted or disabled server drops its work. Pending retries are lost on restart, by design.

### Compat command

`CompatCommandLog`, in memory: a command is recorded `completed`, or `queued` with a probe that tells when its
background work is over (none of the versions it queued is still waiting or being searched; the RSS task returned).
Reading the command asks the probe, so `queued` becomes `completed` the first time it is read after that
([F19](#f19-compat-commands-stayed-queued-for-ever)).

## Background runners

### Scheduled job

Implicit: running is `SchedulerState.JobRuntime.Lock` being held; `NextRunAt` and the last outcome live in memory and
in the `ScheduledJobs` row (`TaskRunner`).

| From | To | Trigger | Guard |
|---|---|---|---|
| idle | running | due time, or manual run | try-lock; a running job answers 409 |
| running | idle, outcome recorded | job returned or threw | |

Invariants: one run per job at a time; a failure is an outcome, not an exception; a run started by hand belongs to
the app, not to its request, and only a shutdown cancels it ([F12](#f12-a-manual-task-run-was-cut-by-the-callers-disconnect)).

### Health

The issues standing after the last check are rows (`StandingHealthIssues`); `HealthState` keeps a copy in memory for
the pages. Each check diffs against the rows and publishes `HealthIssueRaised` / `HealthIssueResolved` for what
appeared and went away.

| From | To | Trigger |
|---|---|---|
| absent | standing, raised event | a check finds an issue (same source and wording) that has no row |
| standing | standing | a check finds it again: no event |
| standing | absent, resolved event | a check no longer finds it |

Invariants: the rows, and the events about their changes, commit in one save; the in-memory copy is replaced only
after it; a restart changes nothing ([F9](#f9-health-events-were-raised-again-at-every-restart)). Notices are stored
but raise no event.

### Library scan, Radarr / Sonarr / Tautulli import

Explicit, in memory: `ScanState` and `RadarrImportState`, both `Idle`, `Queued`, `Running`, `Completed`, `Failed`.

| From | To | Trigger | Guard |
|---|---|---|---|
| `Idle`, `Completed`, `Failed` | `Queued` | request | imports: check and set under one lock, 409 when busy. Scans: one progress object per root folder |
| `Queued` | `Running` | worker dequeues | scan: still `Queued` |
| `Running` | `Completed` | done | |
| `Running` | `Failed` | exception or cancellation | every exit path sets a terminal state |

The Tautulli runner has one more state, "uploading", taken under the same lock as the enqueue: no import starts while
a file is being uploaded and no upload while an import is queued or running ([F11](#f11-tautulli-upload-replaced-or-deleted-by-a-running-import)).

Invariants: one scan at a time; one import per kind; nothing is stuck across a restart because nothing is stored. A
scan key can sit in the channel more than once (every request while `Queued` writes it again); the worker drops the
extras through the state check, so the invariant is "one progress per root folder", not "one channel entry".

### Search queues and the RSS pass

In memory: `SearchQueue` and `SeriesSearchQueue` hold a pending set next to a channel; the RSS pass is a try-lock
(`RssFeedState`).

Invariants: an id is removed from the pending set on every exit path; a search is queued only after the change that
asks for it has committed (the search reads grab state on its own connection).

## Files on disk

### File row and file

Implicit pair: a `MediaFile` or `EpisodeFile` row and the file at the path it names. Every status the UI shows is
derived from the row.

| Operation | Order | Consequence of stopping in between |
|---|---|---|
| import | file transferred, then row (one transaction) | file in the library without a row; the retry finds "destination already exists" |
| rename, order switch, undo | files moved, then one save | guarded since [F5](#f5-a-rename-or-order-switch-whose-caller-went-away-left-disk-and-database-apart) |
| delete a version with its files | checks, then file, then row | guarded since [F1](#f1-deleting-the-default-version-with-its-files-destroyed-the-file-then-refused) |
| delete a series or a version with its files | folders, then rows, the rows whatever becomes of the request | guarded since [F15](#f15-a-series-delete-whose-caller-went-away-left-the-series-with-no-files) |

Invariants the code assumes: a file row means the file exists; one physical file has one row, also when two versions
of a show share a folder ([F8](#f8-two-versions-of-a-show-sharing-a-folder-claimed-each-others-files)); no scan ever
drops a row whose file is missing, so a broken pair does not heal on its own.

### File transfer

`FileTransfer`: source, then `<destination>.bigarr-partial`, then destination, for a copy and for a move alike
([F14](#f14-a-move-across-filesystems-bypassed-the-partial-name)). The final name only ever appears by a rename on its
own filesystem; a destination is never overwritten. A partial left by a killed transfer is overwritten by the retry to
the same destination; nothing else sweeps it.

### Series order and its journal

`Series.Ordering` with the episode-group fields and each episode's `Order*Number`; a `SeriesOrderChange` row per
switch, holding the previous numbers and every file move.

| From | To | Trigger | Guard |
|---|---|---|---|
| order A | order B, change journaled, files moved | apply | B differs from A; one writer per show (`SeriesFileGate`, shared with rename, refresh and "add version") |
| latest change applied | undone (`UndoneAt`), order A, files back | undo | latest change of the show, not undone; a file moved since is left alone |

Invariants: only the latest change can be undone, once; a journaled move is a move that happened; an apply and its
journal are saved together with the new paths. Apply and undo switch the order even when no file could be moved (the
mount is offline): by design.

The preview's `OrderFileStatus` (`Move`, `Unchanged`, `Unmatched`, `Conflict`) is computed, never stored.

### Held release, blocklist

A held release is held, dismissed (`DismissedAt`, an atomic conditional update) or grabbed (row deleted). A blocklist
entry exists or does not. Both insert after a check against a unique index; a concurrent duplicate throws inside the
caller's save.

## Playback

### Session

`PlaybackSession`: a row exists while the stream is open; closing deletes it. `State` is `Playing`, `Paused` or
`Buffering`, with `PausedSince`, `PausedSeconds`, `LastSeenAt` and a `MaxOffsetMs` that only grows.

| From | To | Trigger | Performed by |
|---|---|---|---|
| none | open | a stream in the server's list that matches no open session | `PlaybackTracker.ReconcileAsync` |
| open | open | list refresh or a `playing` event | `PlaybackTracker` |
| open | closed, play recorded | `stopped` event, or gone from the list (closed as of `LastSeenAt`) | `PlaybackTracker.CloseAsync` |
| open | closed, no play | same, when too short or history is off | `PlaybackTracker.CloseAsync` |
| open | closed | its server is no longer followed | `PlaybackMonitor.CloseUnfollowedSessionsAsync` |

Invariants: one writer per server (a gate per listener; there is no unique index on the session key); closing a
session and recording its play are one save; every open session belongs to a followed server (guarded since
[F6](#f6-disabling-a-media-server-left-its-streams-on-now-playing-for-ever)).

### Play, listener, backstop

- `Play`: unfinished, then watched. Only an unfinished play absorbs a later stream of the same item, so watched is
  terminal.
- Listener per server, in memory (`PlaybackMonitor`): not followed, reconciling, listening, with a 60 s reconcile
  while something plays; retries after 2 s (a working stream dropped) or 30 s.
- Backstop checkpoint `MediaServer.PlayHistoryCheckedUntil`: advances only after a full pass.

A failed tick of the 60 s reconcile is only that tick's loss, and the backstop never adds a play for a viewing whose
stream is still open ([F16](#f16-a-failed-refresh-tick-ended-the-playback-refresh), [F17](#f17-the-history-backstop-could-count-a-viewing-twice)).

## Shadow follow

### Followed grab

`RadarrGrab.State` / `SonarrGrab.State`: `Grabbed`, then `Imported`, `Failed` or `Ignored`.

| From | To | Trigger | Guard |
|---|---|---|---|
| none | `Grabbed` | a `grabbed` history line | history id unknown, unique on (follow, history id) |
| `Grabbed` | `Imported` / `Failed` / `Ignored` | a later line with the same download id | still `Grabbed`, and the grab is not newer than the line |

Invariants: the first resolution wins and is terminal; history is read oldest first; a replayed line is a no-op.

### Checkpoint, memory, pick view

- `HistoryCheckpoint`: set to now when the follow is created, moves to the newest line read at the end of a
  successful pass, never backwards; read again with a five-minute overlap.
- Follow memory (in memory): present after a whole-list pass, dropped by a failed pass or a restart, which forces the
  next pass to read the whole list.
- Follow memory keeps when the episodes of every series were last read (Sonarr, a 24-hour cadence) across a failed
  pass; only a restart starts that day over.
- `ShadowPick.RadarrViewAt` / `SeriesShadowPick.SonarrViewAt`: null, then set once, when Radarr or Sonarr answered. A
  pick they could not be asked about keeps its error view and is asked again at every pass for 24 hours.
- Search on add, per version: not searched, then searched. The marker is `SearchedOnAddAt` on the film or series
  version; eligible is "added to Radarr or Sonarr after the follow was created and
  not searched yet", so a failed pass or the 25-per-pass bound only postpones it.
- One pass at a time per follower (a static gate); wake-ups are debounced and coalesced.

See [F10](#f10-shadow-follow-searches-and-views-were-lost-for-good).

## Auth and UI

- Session cookie: stateless, 14 days sliding, carrying a fingerprint of the password it was opened with; valid, then
  refused once the password changes ([F20](#f20-a-password-change-did-not-end-other-sessions)). API key: active, then
  revoked, checked on every request. `UserRole` never changes.
- The web UI holds no state machine of its own: server state lives in the query cache, invalidated by the server's
  event stream (`web/src/hooks/useServerEvents.ts`). The stream is at most once and the cache is not invalidated on
  reconnect, so an event missed while disconnected leaves a page stale until its next refetch. Not audited further.

## Findings

Status of each finding:

- **Fixed**: reproduced by a test that fails without the fix and passes with it.
- **Open, confirmed**: the path was read end to end and no guard was found; not reproduced by a test.
- **Open, reported**: traced during the audit, not re-read independently.

### Fixed

F1 to F6 are ordered by impact; the others were fixed later, from the open list, and keep the order they were fixed
in.

#### F1. Deleting the default version with its files destroyed the file, then refused

`MovieService.DeleteVersionAsync` removed the file from disk first and only then checked that a default version has a
valid successor. `DELETE /movies/{id}/versions/{vid}?deleteFiles=true` on the default version without `promote` (or
with a one-shot successor) answered 409 or 400 with the file already gone, the row still claiming it, and the history
line and event dropped with the unsaved context.

Fix: every refusal is decided before the disk is touched.
Test: `MediaManagementTests.A_refused_version_delete_leaves_the_file_on_disk`.

#### F2. A grab interrupted mid-import stayed `Importing` for ever

Nothing left `Importing` except the worker that set it. A restart, kill or shutdown during an import (a long copy)
left the row there: the poll only queues `ImportPending`, the retry only takes `ImportFailed`, and the target kept
its active grab, so the version could never be grabbed again until someone removed the queue row. The same happened
when the save that records an import's outcome failed.

Fix: `InterruptedImports` puts every `Importing` grab back to `ImportPending` at startup, before any worker runs, and
`ImportRunner` does the same for the one grab whose import crashed. Both are conditional on the state still being
`Importing`.
Test: `ImportFlowTests.An_import_interrupted_by_a_shutdown_is_picked_up_again_at_startup`.

#### F3. Deleting a media server mid-refresh stopped every later refresh

`MediaServerRefresher.ProcessAsync` loaded the server tracked, awaited the HTTP call, then saved. A server deleted in
between made that save throw; the `catch` saved again and threw again; the worker loop had no handler, faulted, and
nothing was logged because the sibling loop kept running. No Plex or Jellyfin refresh happened until the next restart.

Fix: a vanished row means the work is done; the worker loop survives any exception and retries the item.
Test: `MediaServerTests.A_server_deleted_while_it_is_being_told_does_not_stop_later_refreshes`.

#### F4. An interrupted webhook batch re-sent deliveries already accepted

`WebhookSender` sent up to 50 deliveries and saved once at the end. A shutdown, or an endpoint deleted while one of
its deliveries was on the wire, lost the whole batch's outcomes: every delivery already accepted, including those of
unrelated endpoints, was sent again.

Fix: each outcome is saved as soon as it is known, independent of the shutdown token; a delivery whose row is gone is
dropped.
Test: `WebhookTests.An_accepted_delivery_is_not_sent_again_when_a_later_one_in_the_batch_loses_its_endpoint`.

#### F5. A rename or order switch whose caller went away left disk and database apart

Film rename, series rename, order apply and order undo moved files and then saved once with the request's token. A
closed tab or a proxy timeout after the moves cancelled the save: files on the new names, rows on the old ones, no
journal, and nothing that repairs it (a scan flags the moved files as duplicates of the stale rows; a second apply
sees every target taken and journals no move).

Separately, two order switches at once on one show both succeeded: the second planned from paths the first was moving
and became the latest change with no moves, so undo restored the numbers and left the files.

Fix: targets are computed before the first move, nothing waits on the request after it, and the save ignores the
request's token. `SeriesFileGate` lets one apply, undo or rename run per show.
Tests: `InterruptedRenameTests`, three cases (film rename, order switch, concurrent switches).

#### F6. Disabling a media server left its streams on Now playing for ever

A session only closes when its server's listener sees the stream gone. Disabling the server (the documented way to
stop following one, since deleting is refused once it has history) stopped the listener and left the rows; Now
playing reads every session with no server filter.

Fix: `PlaybackMonitor` closes the sessions of servers it no longer follows, as of the last time they were seen. That
also covers sessions left by a server disabled while the app was down.
Test: `PlaybackApiTests.Disabling_a_server_closes_its_open_streams_instead_of_leaving_them_on_now_playing`.

#### F7. A terminal grab state could be overwritten by a stale writer

The poller, the import worker and the queue actions each loaded a grab into their own context and saved later;
`TransitionTo` only checks the copy in hand. A row removed by the user during a poll could be failed (blocklist,
replacement search) or counted removed by that poll; a row removed during its import was imported anyway; a torrent
leaving the client during a long import could get `Removed` written over the worker's `Imported`.

Fix: the removal runs under the poll lock and refuses a grab that is `Importing`; the worker claims a pending grab
with one conditional update under the same lock; the poll no longer counts absences for a grab the worker holds.
Test: `ImportFlowTests.A_grab_being_imported_is_left_to_the_import_worker_and_a_removal_waits_for_the_poll`.

#### F8. Two versions of a show sharing a folder claimed each other's files

A version's scan knew only its own rows but walked the whole folder, so a sibling version's file for an episode it
lacked got a second row at the same path. The 1080p version then showed the 2160p file as its own and stopped
searching; deleting that "file" removed the sibling's. Sharing a folder is what a second version gets by default.

Fix: a scan skips the files that belong to a sibling version living in the same folder. A file nobody owns yet goes
to the first version whose scan can place it. A file two versions already claim keeps its older row (the version that
had it first); the scan drops the newer row and never touches the file.
Test: `SharedFolderScanTests.A_file_in_a_folder_two_versions_share_is_attached_to_one_version_only`.

#### F9. Health events were raised again at every restart

The baseline of the health diff lived in memory: the first check after a start raised every standing issue again
(duplicate webhook and Discord messages) and never resolved one fixed while the app was down. The baseline also moved
before the events were saved, so a failed save lost them.

Fix: the standing issues are rows, written in the same save as their events.
Test: `TasksAndHealthTests.A_restart_neither_raises_standing_issues_again_nor_forgets_to_resolve_one_fixed_meanwhile`.

#### F10. Shadow follow: searches and views were lost for good

- A film or show added by the follow was searched only in the pass that added it: a pass failing after the add, or the
  25-per-pass bound, left it never searched.
- "Added since the follow started" was tested against the moving history checkpoint, so a film added before newer
  history was read counted as back catalogue.
- A pick was stamped as shown even when Radarr or Sonarr could not be asked, making the error view permanent.
- Sonarr's 24-hour "every series" read restarted its clock on every failed pass.

Fix: eligibility is "added after the follow was created and not searched yet", read from the version; an unanswered
pick is asked again for 24 hours; a failed pass forgets the series list only. The migration marks every existing
version as searched, so an upgrade searches nothing again.
Tests: `RadarrFollowOnAddTests.A_film_older_than_the_history_already_read_is_still_searched_and_an_unanswered_pick_is_shown_again`,
`SonarrFollowTests.Search_on_add_is_capped_per_pass`,
`SonarrFollowTests.A_failed_pass_forgets_the_series_list_but_not_when_every_series_was_last_read`.

#### F11. Tautulli upload replaced or deleted by a running import

The upload checked "is an import running?" once, before streaming its body. An import started meanwhile ran on the
previous file; the upload then replaced it under the import, whose end deleted the new upload.

Fix: the runner has an "uploading" state under the lock of the enqueue.
Test: `TautulliImportTests.While_an_upload_is_streaming_no_import_starts_and_no_second_upload_replaces_it`.

#### F12. A manual task run was cut by the caller's disconnect

"Run now" and the health check passed the request's token into the job: a closed tab cut the job half-way and
recorded nothing.

Fix: those runs take the app's stopping token.
Test: `TaskRunDisconnectTests.A_task_run_by_hand_finishes_and_is_recorded_when_its_caller_goes_away`.

#### F13. Webhook retry lost in flight; disabled endpoints reported as failing

- A retry clicked while the delivery was on the wire was overwritten by that attempt's outcome, possibly "given up".
  Fix: the retry is left for the sender in `WebhookSignal`; a failure seen after it makes the delivery due again.
  Test: `WebhookTests.A_retry_asked_while_the_delivery_is_on_the_wire_is_not_lost_to_that_attempts_outcome`.
- A delivery given up because its endpoint was disabled kept a "webhook is failing" health warning.
  Fix: the health check only looks at enabled endpoints.
  Test: `WebhookTests.A_disabled_endpoint_is_not_reported_as_failing_for_what_it_left_undelivered`.

#### F14. A move across filesystems bypassed the partial name

In move mode the runtime copied straight to the final name when source and destination were on different
filesystems; a kill mid-copy left a truncated file there, refused by the retry and adopted by a scan.

Fix: a move goes through the partial name, then a rename. Not reproducible in a test without two filesystems; the
test covers the path and the overwrite of a stale partial.
Test: `FileTransferTests.A_move_goes_through_the_partial_name_and_replaces_what_a_killed_transfer_left_there`.

#### F15. A series delete whose caller went away left the series with no files

Folders went to the recycle bin, then the rows were deleted with the request's token.

Fix: everything is read before the first folder goes, and the rows follow whatever becomes of the request (series and
version deletes).
Test: `InterruptedSeriesDeleteTests`.

#### F16. A failed refresh tick ended the playback refresh

One failed tick of the 60 s reconcile ended it for the life of the event connection.

Fix: a failed tick is logged and the next one runs.
Test: `PlaybackApiTests.A_refresh_tick_that_fails_while_something_plays_does_not_end_the_refresh`.

#### F17. The history backstop could count a viewing twice

The backstop looked for an existing play, never an open session: a viewing past the watched mark and still open
after six hours got a play from the backstop and a second one when its session closed.

Fix: an open session of the same user and item covers the mark.
Test: `PlaybackStatsTests.Plex_history_leaves_a_viewing_whose_stream_is_still_open_to_the_live_tracking`.

#### F18. A series refresh raced the other writers of its show

Two refreshes at once, or a refresh overlapping "add version", failed with a 500 or left episodes without a target;
a refresh overlapping an order switch saved the order numbers it had read before it.

Fix: refresh and "add version" take the show's turn (`SeriesFileGate`) like the order switch and the rename.
Test: `ConcurrentSeriesWritersTests`.

#### F19. Compat commands stayed queued for ever

Fix: a queued command carries a probe and reads `completed` once its work is over; "refresh downloads" is complete
once asked.
Test: in `RadarrCompatTests`, the search command is polled to completion.

#### F20. A password change did not end other sessions

Fix: the session cookie carries a fingerprint of the password hash, checked on every request (kept in memory, no
query); the session that changed the password is re-issued. Sessions opened before this change carry no fingerprint
and have to log in once.
Test: `AuthEndpointsTests.Changing_the_password_ends_every_other_session_and_keeps_the_one_that_changed_it`.

#### F21. Failed handling removed the torrent before its own save

A save that failed after the removal left a downloading grab with no torrent, later counted "removed" without its
blocklist entry.

Fix: the torrent leaves the client after the commit; a client that refuses is noted on the grab and its journal line.
Test: `FailedHandlingTests.A_failed_torrent_only_leaves_its_client_once_the_failure_is_recorded`.

### Open

What is known and not fixed:

- A retry and a manual import of the same failed grab at the same moment are not serialised (see the grab's ownership
  rules).
- A `.bigarr-partial` left by a killed transfer is only removed by a retry to the same destination.
- The in-memory event stream is at most once: a subscriber that falls 256 events behind drops the oldest, and the UI
  does not refetch on reconnect. Suspected only; whether a bulk operation can outrun a subscriber was not measured.
- Two follows may share a version name; the pick view is selected by version name only. Reported, not re-read.

### Checked and sound

Paths that looked suspicious and are correctly guarded:

- Outbox dispatch is atomic: no duplicate deliveries on a crash or a retry; an endpoint deleted mid-dispatch fails the
  batch cleanly and the next sweep redoes it.
- A 429 never spends a webhook attempt; a delivered row cannot be retried.
- A second active grab on one target is refused by the unique index, and the torrent is taken back out of the client.
- A hand-added torrent is adopted once (download id known to every grab); a duplicate adoption cannot happen under
  the poll lock.
- Scheduled and manual runs of a job share one try-lock, released on every path.
- Import runners: check and set are atomic; every exit from `Running` is terminal.
- Search queues always remove their id, including on crash and shutdown.
- Shadow follow: one pass at a time; replayed history is a no-op; a late event cannot overwrite a resolved grab; the
  checkpoint never moves back; duplicate picks and sightings are caught by unique indexes.
- Playback: a restart with open sessions closes vanished streams as of `LastSeenAt`; a reused session key and a
  backwards seek are handled; kill-stream never writes the session.
- Series order: a second sequential undo is a 409; undo never overwrites and skips files moved since; the plan handles
  chains, cycles and duplicate targets.
- Episode refresh keeps every episode that has a file, a grab, a pick or history.
