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
- [Requests](#requests): request lifecycle, derived states, forward mode
- [Issues](#issues): issue lifecycle, thread, actions from the issue
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
| `DownloadFailed` | client failure, or a stall or client error that lasted; blocklisted, searched again | | yes |
| `Removed` | vanished from the client | | yes |
| `Ignored` | dismissed by the user | | yes |

`ImportFailed` is neither active nor terminal: the target is free for another grab, and the grab can still move.

| From | To | Trigger | Guard | Performed by |
|---|---|---|---|---|
| none | `Grabbed` | grab (user, search, RSS) | no active grab on the target: check, then the partial unique index `IX_GrabTargets_TargetId_Active` | `GrabService.GrabAsync` / `GrabEpisodesAsync` |
| none | `ImportPending` | a complete torrent of our category with no grab row | download id unknown to every grab | `QueuePoller.AdoptCompletedAsync` |
| `Grabbed` | `Downloading` | client lists it, not complete, not failed, not in error | | `QueuePoller.Advance` |
| `Grabbed`, `Downloading` | `ImportPending` | client reports complete | qBittorrent's `moving` (shown as downloading) and `checkingUP` (queued) are not complete: the files may be half in place ([F39](#f39-a-torrent-qbittorrent-was-still-moving-or-rechecking-was-imported)) | `QueuePoller.Advance` |
| `Grabbed`, `Downloading` | `DownloadFailed` | client reports failed, stalled past `StalledTimeout`, or in error (`Errored`) past `ErroredTimeout` | | `QueuePoller.HandleFailureAsync` |
| `Grabbed`, `Downloading`, `ImportPending` | `Removed` | absent from an answering client for `RemovedAfterPolls` polls | a `Grabbed` grab younger than 45 s is not counted; never from `Importing` | `QueuePoller.PollCoreAsync` |
| `ImportPending` | `Importing` | import worker dequeues it | one conditional update, under the poll lock | `ImportService.ImportGrabAsync` |
| `Importing` | `Imported` | file transferred and recorded | same transaction as the file row | `ImportService.ExecuteAsync` (film), `ImportGrabAsync` (series) |
| `Importing` | `ImportFailed` | import refused or crashed | not terminal | `ImportService.FailAsync` / `FailSeriesAsync` |
| `Importing` | `ImportPending` | client unreachable or torrent not complete after all | still `Importing` | `ImportService.ImportGrabAsync` (deferred) |
| `Importing` | `ImportPending` | process start, or the worker crashed outside its failure handling | still `Importing` (conditional update) | `InterruptedImports` |
| `ImportFailed` | `ImportPending` | user retries | | `ImportService.RetryAsync` |
| `ImportFailed` | `Imported` | user imports by hand | 409 while the grab is active (`Grabbed`, `Downloading`, `ImportPending`, `Importing`); a terminal grab only lends its release title ([F42](#f42-a-manual-import-accepted-a-download-that-was-still-active)) | `ManualImportService`, `SeriesManualImportService.ImportAsync` |
| any non-terminal except `Importing` | `Ignored` | user removes the queue row | under the poll lock; 409 while `Importing` | `QueueService.RemoveGrabAsync` |

Ownership: `Grabbed` and `Downloading` belong to the poller; `Importing` belongs to the import worker, and neither the
poller nor the user writes a grab in that state, and a manual import refuses an active grab. The three writers that can meet on a grab (the poll, the user's
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
6. Failed handling commits state, blocklist, history and event together; the replacement search is queued only after
   that commit ([F21](#f21-failed-handling-removed-the-torrent-before-its-own-save)), and the torrent leaves its client
   after the search is queued, so a removal that goes wrong cannot stop it
   ([F32](#f32-a-failed-download-whose-removal-threw-lost-its-replacement-search)).
7. Once a download client has accepted the torrent, the grab row is written whatever the caller does: the grab runs on
   no cancellation token after the send, and the qBittorrent check that follows the add does not stop either
   ([F31](#f31-a-grab-whose-caller-went-away-after-the-send-left-an-untracked-download)).

A failed grab whose torrent has left the client stays `ImportFailed` or `DownloadFailed` in the table and is simply no
longer shown (`QueuePoller.cs:173-177`). That is by design.

### Queue poll bookkeeping

Implicit, in memory (`QueueState`): per grab, a count of consecutive polls where the client did not list it, the
time a stall began, and the time the client began reporting an error.

| From | To | Trigger |
|---|---|---|
| unseen | counted (n) | active grab absent from an answering client (`CountMiss`) |
| counted | unseen | client lists it again (`Seen`) |
| counted (n = `RemovedAfterPolls`) | forgotten | grab becomes `Removed` |
| not stalled | stalled since t | client reports `Stalled` |
| stalled | not stalled | any other client status (`NotStalled`) |
| not errored | errored since t | client reports `Errored` (qBittorrent `error`, `missingFiles`) (`ErroredSince`) |
| errored | not errored | any other client status (`NotErrored`) |

Order at the end of a poll: commit the transitions, queue the imports and the replacement searches, then remove the
failed torrents from their client. The removal catches anything and runs to its end whatever the caller does (it takes
no cancellation token); a torrent it could not remove is noted on the grab and its journal line.

Invariants: one poll at a time (`QueueState.PollLock`); a client that did not answer counts neither as a miss nor as
a stall. The counters and both timers restart from zero after a restart, so a stall, an error or an absence is measured
again from the first poll: never a false failure, at worst a later one. An `Errored` download is a warning in the queue
("Download client error", with the minutes left) until `ErroredTimeout` has passed; a grab already `ImportPending` whose
torrent turns `Errored` gets the warning and no transition.

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
| absent | standing, raised event | a check finds an issue (same source and key, or wording when it has no key) that has no row |
| standing | standing | a check finds it again: no event; if its wording changed, the stored text follows silently ([F28](#f28-a-health-issue-whose-wording-changed-was-resolved-and-raised-again-at-every-check)) |
| standing | absent, resolved event | a check no longer finds it |

Invariants: the rows, and the events about their changes, commit in one save; the in-memory copy is replaced only
after it; a restart changes nothing ([F9](#f9-health-events-were-raised-again-at-every-restart)). Notices are stored
but raise no event. An issue whose text carries a count, a duration or a time gives the check a key (`Key`, never
sent to clients) naming its subject; the row compares by it, so a changing number is not a new issue.

### Library scan, Radarr / Sonarr / Tautulli / Seerr import

Explicit, in memory: `ScanState` and `RadarrImportState`, both `Idle`, `Queued`, `Running`, `Completed`, `Failed`.

| From | To | Trigger | Guard |
|---|---|---|---|
| `Idle`, `Completed`, `Failed` | `Queued` | request | imports: check and set under one lock, 409 when busy. Scans: one progress object per root folder |
| `Queued` | `Running` | worker dequeues | scan: still `Queued` |
| `Running` | `Completed` | done | |
| `Running` | `Failed` | exception or cancellation | every exit path sets a terminal state |

A timeout of the metadata provider on one folder or one title is that folder's or that title's failure, not the run's
([F43](#f43-one-metadata-timeout-failed-a-whole-scan-or-a-whole-import)).

The Tautulli and Seerr runners have one more state, "uploading", taken under the same lock as the enqueue: no import
starts while a file is being uploaded and no upload while an import is queued or running ([F11](#f11-tautulli-upload-replaced-or-deleted-by-a-running-import)).

Invariants: one scan at a time; one import per kind; nothing is stuck across a restart because nothing is stored. A
scan key can sit in the channel more than once (every request while `Queued` writes it again); the worker drops the
extras through the state check, so the invariant is "one progress per root folder", not "one channel entry".

### Search queues and the RSS pass

In memory: `SearchQueue` and `SeriesSearchQueue` hold a pending set next to a channel; the RSS pass is a try-lock
(`RssFeedState`). The RSS quick recheck is a plan in `RssFeedState` plus the `rssRecheck` task's next run: a whole
pass clears both when it starts and may set them when it ends; the recheck takes the plan under the same try-lock and
clears the next run; `TaskRunner` drops a one-off task's next run once it has run, unless a newer plan moved it later.

Invariants: an id is removed from the pending set on every exit path; a search is queued only after the change that
asks for it has committed (the search reads grab state on its own connection); a whole pass and a recheck never run at
once, and a recheck never plans another.

An indexer is paused by a failed request (`IndexerGateway`, in memory, lost on restart), and a request that gets no
answer within `TorznabClient.RequestTimeout` (60 s) is such a failure
([F46](#f46-an-indexer-that-never-answered-was-never-paused)). The search's deadline for one indexer
(`IndexerQueries.PerIndexerTimeout`, 90 s) is longer and pauses nothing by itself: a cancellation from outside is not
counted, since it cannot be told from the user leaving the page.

## Files on disk

### File row and file

Implicit pair: a `MediaFile` or `EpisodeFile` row and the file at the path it names. Every status the UI shows is
derived from the row.

| Operation | Order | Consequence of stopping in between |
|---|---|---|
| import, assign an unmatched episode file, delete an episode file | file transferred or recycled, then row, the rows whatever becomes of the request | guarded since [F34](#f34-an-import-an-assign-or-an-episode-file-delete-whose-caller-went-away-left-disk-and-database-apart); a process killed in between still leaves a file without a row, and the retry finds "destination already exists"; a series one waits for the show's turn since [F36](#f36-series-imports-did-not-wait-for-the-shows-turn) |
| rename, order switch, undo | files moved, then one save | guarded since [F5](#f5-a-rename-or-order-switch-whose-caller-went-away-left-disk-and-database-apart) |
| delete a version with its files | checks, then file, then row | guarded since [F1](#f1-deleting-the-default-version-with-its-files-destroyed-the-file-then-refused) |
| delete a series or a version with its files | folders, then rows, the rows whatever becomes of the request | guarded since [F15](#f15-a-series-delete-whose-caller-went-away-left-the-series-with-no-files) |
| delete a film, a film version or a film's file | file or folders, then rows, the rows whatever becomes of the request | guarded since [F27](#f27-a-film-delete-whose-caller-went-away-left-a-version-with-a-file-that-was-gone) |
| delete a film or a series over several folders, a series version over a shared folder | every folder (or file) is tried; the rows of those that went are saved with their lines, the film or series stays with what is left, and the answer is the error | guarded since [F45](#f45-a-delete-that-failed-on-one-folder-left-the-others-deleted-with-their-files-still-listed) |

Invariants the code assumes: a file row means the file exists; one physical file has one row, also when two versions
of a show share a folder ([F8](#f8-two-versions-of-a-show-sharing-a-folder-claimed-each-others-files)); no scan ever
drops a row whose file is missing, so a broken pair does not heal on its own.

### File transfer

`FileTransfer`: source, then `<destination>.bigarr-partial`, then destination, for a copy and for a move alike
([F14](#f14-a-move-across-filesystems-bypassed-the-partial-name)). The final name only ever appears by a rename on its
own filesystem; a destination is never overwritten. A partial left by a killed transfer is overwritten by the retry to
the same destination; nothing else sweeps it. A replacement in place (an upgrade that renders the same name) stages the
new file under the partial name beside the old one, retires the old one, then renames
([F26](#f26-an-upgrade-keeping-its-file-name-removed-the-old-file-before-the-new-one-arrived)).

### Series order and its journal

`Series.Ordering` with the episode-group fields and each episode's `Order*Number`; a `SeriesOrderChange` row per
switch, holding the previous numbers and every file move.

| From | To | Trigger | Guard |
|---|---|---|---|
| order A | order B, change journaled, files moved | apply | B differs from A; one writer per show (`SeriesFileGate`, shared with rename, reassign, refresh, "add version", the series import, the manual assign and the episode-file delete) |
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
`Buffering`, with `PausedSince`, `PausedSeconds`, `LastSeenAt` and a `MaxOffsetMs` that only grows once the opening
offset is confirmed: until playback has gone 60 s past `StartOffsetMs`, an offset before it means Plex opened the
stream on a stale position, and `StartOffsetMs` and `MaxOffsetMs` both move back to it
([F23](#f23-a-stale-opening-offset-counted-as-progress)).

| From | To | Trigger | Performed by |
|---|---|---|---|
| none | open | a stream in the server's list that matches no open session (same session key, rating key and user; Plex's session id only after a 2 min gap) | `PlaybackTracker.ReconcileAsync` |
| open | open | list refresh or a `playing` event | `PlaybackTracker` |
| open | closed, play recorded | `stopped` event, or gone from the list (closed as of `LastSeenAt`) | `PlaybackTracker.CloseAsync` |
| open | closed, no play | same, when too short or history is off | `PlaybackTracker.CloseAsync` |
| open | closed | its server is no longer followed | `PlaybackMonitor.CloseUnfollowedSessionsAsync` |

Invariants: one writer per server (a gate per listener; there is no unique index on the session key); closing a
session and recording its play are one save; every open session belongs to a followed server (guarded since
[F6](#f6-disabling-a-media-server-left-its-streams-on-now-playing-for-ever)); a stream that goes on keeps its session
when Plex changes its own session id ([F22](#f22-a-new-plex-session-id-chopped-a-viewing-into-pieces)).

### Play, listener, backstop

- `Play`: unfinished, then watched. Only an unfinished play absorbs a later stream of the same item, so watched is
  terminal. Watched and "continues" read `MaxOffsetMs`, which counts only positions reached by playing.
- Listener per server, in memory (`PlaybackMonitor`): not followed, reconciling, listening, with a 60 s reconcile
  whether or not something plays ([F22](#f22-a-new-plex-session-id-chopped-a-viewing-into-pieces)); retries after 2 s
  (a working stream dropped) or 30 s.
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
  refused once the password changes ([F20](#f20-a-password-change-did-not-end-other-sessions)) or the user is disabled.
  It also carries the user's role and permissions: when the admin changes them, the next request reissues the cookie
  with the current ones (`SessionStamps` keeps each user's state in memory, refreshed by `Changed` on every edit). API
  key: active, then revoked, checked on every request; a disabled user's keys are refused too. A key follows its
  owner's role at each request: demoted from admin, it is refused on the Radarr and Sonarr surfaces; promoted again,
  accepted ([F24](#f24-a-demoted-admins-api-key-kept-the-compat-surfaces)). A key also has a scope (`compat` by default,
  `full`, `calendar`) that says which policies it passes, checked on top of the role and, on the compatible surfaces,
  of its view ([F41](#f41-an-api-key-opened-the-whole-admin-api)); the admin changes it from the cookie session only.
  A request that changes something (any method but GET, HEAD, OPTIONS, TRACE) and carries the session cookie is refused
  with 403 unless the browser says it comes from Bigarr's own origin: `Sec-Fetch-Site` is `same-origin` or `none`, or,
  without it, `Origin` has the request's host and port; a request with neither header passes
  ([F48](#f48-another-site-on-the-same-host-or-domain-could-act-in-the-admins-session)).
- User: active ⇄ disabled (`DisabledAt`), by the admin only, never the admin's own account; no delete. Role (admin,
  request manager, user) and permissions change freely, except the admin's own role. A Plex account linked to no one
  creates its user (role user, default permissions) on its first sign-in, only when new Plex accounts are let in and
  plex.tv lists the configured Plex server among the account's resources; a non-admin needs that access at every
  sign-in, the admin never.
- Discover availability: derived on every request from the library, never stored, no scan job
  (`Features/Discover/Availability.cs`, `LibraryAvailability.cs`). Per version slot ("standard" = the default version, or the
  first other version when the default is the 4K one; "4K" = the version named in Discover settings): a film version is available with a file, processing while monitored without
  one, not in the library otherwise. A season or a whole series (specials left out) is available when every aired
  episode has a file, partially available when only some do, processing when none has one yet but episodes are
  monitored (or the monitored version has no episode listed yet), not in the library otherwise. An episode that has not
  aired never makes it partial. TMDB answers are cached for a few minutes; availability is not.
- The web UI holds no state machine of its own: server state lives in the query cache, invalidated by the server's
  event stream (`web/src/hooks/useServerEvents.ts`). The stream is at most once; it is reopened after a hard failure
  (`web/src/hooks/eventStream.ts`: after 5 s, then 10 s, then every 30 s) and every reopening after an interruption
  invalidates the whole cache, so what was missed while disconnected is refetched
  ([F47](#f47-the-event-stream-stayed-closed-after-a-restart-and-missed-events-were-never-refetched)). Not audited further.

## Requests

`MediaRequest.Status` (`Features/Requests/RequestService.cs`): `Pending`, `Approved`, `Declined`, `Failed`. One row per
title, version slot (standard / 4K) and, for a series, set of seasons.

| From | To | Trigger | Guard |
|---|---|---|---|
| none | `Pending` | a signed-in user asks (`POST /requests`) | permission for the type (and "request 4K"); slot not wanted or available in the library; no active request for the same title, slot (and season); quota |
| none → `Pending` | `Approved` | at once, same request | the user's auto-approve flag for the type, or an admin |
| none → `Pending` | `Approved` | the admin adds a title from Discover (the only way to add one from the UI) | admin only; the library add's own options are kept on the request (`AddOptions`); a version the admin names is its own slot, refused only when the library already has a version of that name; a series TMDB lacks comes by its TVDB id |
| `Pending` | `Approved` | an admin or a request manager approves (with edits: profile, root folder, slot, seasons) | still `Pending` |
| `Pending` | `Declined` | an admin or a request manager declines (optional reason) | still `Pending`; terminal |
| `Pending` | (deleted) | its author cancels | still `Pending`, author only |
| `Approved` | `Failed` | the library add failed or threw (no root folder, metadata provider down or timing out, title unknown, the caller gone) | in the same approval |
| `Approved` | `Failed` | startup repair (`StrandedApprovals`): the process stopped between the approval and the add | approved, no library id (`MovieId` and `SeriesId` both null), not imported from Seerr (`SourceRef`); a `requestFailed` event goes out |
| `Failed` | `Approved` | an approver retries | the add goes through this time |
| none | `Pending`, `Approved`, `Declined` or `Failed` | the Seerr import (`SeerrImportService`) writes the row as Seerr had it (Seerr's "completed" is `Approved`) | not imported before (`SourceRef`); its title and requester exist in Seerr. No outbox event, no library add, no forward, no search |

Derived, never stored (`RequestStates`): an `Approved` request shows `Processing`, `PartiallyAvailable` or
`Available` from the library with Discover's availability rules, on the version slot it asked for and, for a series,
on its seasons only (all available: available; some episodes in: partial; wanted: processing). The library losing the
title shows plain `Approved` again.

Forward state (`ForwardStatus`, written only by `RequestLibrary.ForwardAsync`): `None` (no enabled followed
Radarr/Sonarr carries the version's name), `Forwarded`, `AlreadyThere`, `Failed` → `Forwarded` / `AlreadyThere` on a
retry. Independent of the lifecycle: a failed forward never fails the request.

Invariants:

- Every add from the UI is a request: the admin's goes through the same approval and forward as anyone's, attributed
  to them, so it shows in Requests and in its events. A retry adds the title the same way (`AddOptions`).
- Approval saves `Approved` (and its outbox event) before touching the library, so a crash never loses a request. Whatever
  the add does or throws, the request ends `Approved` with its library id or `Failed` with the reason (the outcome is
  written on no token: the caller leaving changes nothing). Only a process stop between the two saves leaves an
  `Approved` request with nothing in the library, and the next startup turns it into `Failed` where Retry works; the add
  then writes the library's rows through `MovieService` / `SeriesService`, and the
  request's library id and version name.
- "Available" goes to the outbox once per request: `AvailableAt` is set by `RequestAvailabilityJob` when the derived
  state first reads `Available`, and never cleared.
- An imported request is history: written straight to the table, never through `RequestService`, with Seerr's dates,
  requester and approver. One Seerr already called available (completed, or its title available in that slot) or one
  the library already holds in full gets `AvailableAt` at the import, so the job never announces it; any other
  approved one is announced when the library first has it, as a request made in Bigarr would be.
- Active (pending, approved, failed) requests hold their slot: the duplicate check reads them; a declined one does not,
  and neither counts a deleted (cancelled) one. Quotas count every request but declined ones over each window.
- Shadow mode needs nothing of its own here: the search the approval enqueues goes through the grab choke point.
- Notifications add no state: each event above is one outbox row. A Discord delivery reads who to mention (`Mentions`)
  when it is sent, not when the event happened, so a Discord id or opt-in set in between counts and a disabled user is
  never mentioned; a request approved at once posts one message, from its approval.

Known gap: two approvers acting on the same pending request at once both pass the "still pending" check (no
conditional update); the second add finds the title in the library and only monitors it again.

## Issues

`Issue.Status` (`Features/Issues/IssueService.cs`): `Open`, `Resolved`. One row per report: a title of the library (film,
or series / season / episode), one version, and the file of a film or an episode at report time (`Issue.File`, a JSON
snapshot: path, size, quality, release, indexer, grab id).

| From | To | Trigger | Guard |
|---|---|---|---|
| none | `Open` | a user with "report issues" reports (`POST /issues`) | the title is in the library (TMDB id, or TVDB id for a series); the scope fits the title; the version, season or episode exists; a message |
| `Open` | `Resolved` | the reporter or an approver resolves | still `Open` |
| `Resolved` | `Open` | the reporter or an approver reopens | still `Resolved` |
| none | `Open` or `Resolved` | the Seerr import writes the issue as Seerr had it, its first comment as its message, the others as its thread (a `Resolved` line for a resolved one) | not imported before (`SourceRef`); its title and reporter exist in Seerr. The title need not be in the library; no file snapshot (Seerr keeps none); no outbox event |

Each transition adds a line to the thread (`IssueComment.Kind` `Resolved` / `Reopened`) and an outbox event
(`issueResolved` / `issueReopened`); a report adds `issueCreated`, a comment `issueCommented`. Comments are written by
whoever sees the issue (reporter, approvers), in either state.

Thread (`IssueComment`): `Comment` rows are edited or deleted by their author for `IssueService.CommentEditWindow`
(15 minutes), by the admin at any time. `Action`, `Resolved` and `Reopened` rows are the app's and never change.

Actions (`IssueActionService`, approvers only), each logged as an `Action` row: an `Outcome` every reader sees and a
`Detail` (release names, paths) only the admin reads.

| Action | Through | Guard |
|---|---|---|
| Search again | `AutomaticSearchService` (film version) / `SeriesAutomaticSearchService.SearchEpisodesAsync` (the episode, the season's or the series' episodes), trigger `User`; then the followed app's search command, when one carries the version (below) | the title and version are still in the library |
| Blocklist the release (and optionally delete the file), then search | `BlocklistService.BlockAsync` with the grab that produced the file (import line → download id → grab), or a stand-in grab carrying the file's release title | Bigarr owns the file (below); film or episode scope; a release is known |
| Delete the file (then optionally search) | `MovieService.DeleteFileAsync` / `SeriesManualImportService.DeleteEpisodeFileAsync` (recycle bin, history, `fileDeleted` events) | Bigarr owns the file; film or episode scope; a file is there now |

Ownership: while shadow mode is on, or while an enabled followed Radarr/Sonarr carries the issue's version name, the
files are that app's. Blocklist and delete are refused (409) and not offered; the search stays and, in shadow mode,
records a pick through the grab choke point like any other search. When a followed app carries the version, the search
is also sent there (`ArrForwarder.SearchMovieAsync` / `SearchSeriesAsync`, the same client as forward mode): Radarr
`MoviesSearch` for the movie it has by TMDB id; Sonarr `SeriesSearch`, `SeasonSearch` or `EpisodeSearch` (its episode
of the same numbers) for the series it has by TVDB id. A refusal or an unreachable app is written in the action's
outcome and changes nothing else; nothing is retried. This search command is, with forward mode's adds, the only write
Bigarr makes to a followed app.

Invariants:

- The issue keeps its own copy of the title, version name, episode label and file: the library ids
  (`MovieId`, `SeriesId`, `EpisodeId`, `MovieVersionId`, `SeriesVersionId`) are references without foreign keys, so the
  issue outlives a deleted title, version or file. An action on an issue whose title or version is gone is refused.
- An action works on the file there now (`IssueFiles.CurrentAsync`), not on the snapshot: the admin's view shows both
  when they differ (`FileChanged`).
- What reaches a user carries no path, release, indexer, library id nor action detail; another user's issue answers 404.
  The Discord message of an issue event follows the same rule (title, type, status, message, season or episode only),
  since the channel may be read by reporters; mentions are resolved at send time, as for requests.

Known gap: two approvers resolving the same issue at once both pass the "still open" check (no conditional update);
the thread then shows two "resolved" lines and two `issueResolved` events go out.

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

#### F22. A new Plex session id chopped a viewing into pieces

A stream was the same only while Plex's own session id stayed the same, and Plex changes it under a stream that goes
on (a transcode restarted by a seek or a quality change, a player reconnecting). Each change closed the session as of
its last sighting and opened a new one, and pieces under the minimum were dropped: a viewing lost time, or all of it.
The 60 s list read also ran only while a session was open, so a stream whose events came before Plex listed it (or
were lost) stayed unseen until another event.

Fix: a stream is its session key, rating key and user, like Tautulli; Plex's session id only tells a new stream after
2 minutes unseen (a Plex restart reusing its keys). The list is read every 60 s in any case.
Test: `PlaybackTrackerTests.A_plex_session_id_changing_under_a_stream_that_goes_on_keeps_one_session_and_the_whole_time`,
`PlaybackTrackerTests.An_idle_paused_stream_resumed_hours_later_under_a_new_plex_session_id_is_a_play_of_the_time_played`,
`PlaybackApiTests.The_periodic_tick_finds_a_stream_no_event_announced_while_nothing_else_plays`.

#### F23. A stale opening offset counted as progress

Plex's first offset for a stream is often stale: an earlier view's resume point, or the position of the previous
item on the same player, sometimes past the item's end. The tracker took it as the start and the furthest point, so a
stream that jumped back and stopped at 28 % was watched, and the next day's resume became a second play because a
watched play absorbs nothing.

Fix: the opening offset counts only once playback goes 60 s past it; a jump back before that moves the start and the
furthest point to the real position. A one-time repair (queued by its migration in `PendingRepairs`, run at startup,
live plays only) gives older streams that start more than 60 s past their end the start "end minus played time",
works out the play's furthest point and watched flag again, and joins a play back into the previous one of the same
user and item when it continues it.
Tests: `PlaybackTrackerTests.A_stale_opening_offset_is_not_progress_and_the_next_day_resume_continues_the_same_play`,
`StaleStartRepairTests`.

#### F24. A demoted admin's API key kept the compat surfaces

The policy on the `/radarr` and `/sonarr` surfaces (and their hubs and cover routes) checked that the key was valid
and its owner not disabled, but not the owner's role. A user made an admin who created a key and was then moved back
to an ordinary role kept full access to those surfaces, which can delete films with their files and rewrite indexers.

Fix: the `ApiKey` policy requires the admin role, read from the owner at each request. A key is not revoked by a
demotion: it works again if its owner is made admin again, like a disabled user's.
Test: `UsersAndRolesTests.A_demoted_admin_s_key_no_longer_opens_the_compat_surfaces`.

#### F25. A Tautulli re-import counted a viewing resumed live twice

A Tautulli row was left out when it overlapped a live play of the same user and item. A viewing that resumes an
unfinished imported play goes on that play, so its live stream sits in a play whose source is Tautulli: a re-run did
not see it and added Tautulli's row for the same viewing, on the imported play or as a play of its own.

Fix: the streams Bigarr followed live (no Tautulli key) count wherever they are. A new play overlapping one is left out
whole, as before; a play imported before only gains the rows Bigarr did not follow live. The preview counts the same.
Test: `TautulliImportTests.A_tautulli_history_becomes_plays_once_and_a_rerun_adds_only_what_is_new`.

#### F26. An upgrade keeping its file name removed the old file before the new one arrived

When an imported file rendered the same name as the file it replaced, the import recycled the old file first and
transferred the new one afterwards. A transfer that failed (disk full, a copy cut short, cancellation) left the old
file gone for good without a recycle bin, and the row pointing at an empty path.

Fix: `FileTransfer.ReplaceAsync` brings the new file in full under the partial name, then retires the old one, then
renames; a failure before the rename puts everything back (a move returns the source). Film and series imports use it.
Tests: `FileTransferTests.Replacing_brings_the_new_file_in_before_the_old_one_leaves`,
`FileTransferTests.A_replacement_whose_transfer_fails_leaves_the_old_file_alone`,
`FileTransferTests.A_replacement_whose_old_file_cannot_be_retired_puts_everything_back`,
`FileTransferTests.A_partial_file_left_by_a_killed_replacement_is_overwritten`.

#### F27. A film delete whose caller went away left a version with a file that was gone

The film side of [F15](#f15-a-series-delete-whose-caller-went-away-left-the-series-with-no-files): the file left the
disk, then the rows were saved with the request's token. A caller that went away in between left a version that
still named a file that no longer existed, with no history line and no event; the next scan does not drop such a row.

Fix: once the file (or the folders) are gone, the rows follow whatever becomes of the request (version delete, film
delete, file-only delete). Everything that can refuse the delete still runs before the first disk operation, with
the live token.
Tests: `InterruptedMovieDeleteTests` (file delete, version delete, last version delete). The folder delete of
`DeleteMovieAsync` shares `RemoveMovieAsync` with the last-version case and is not tested on its own.

#### F28. A health issue whose wording changed was resolved and raised again at every check

An issue was the same from one check to the next only by its source and its exact text. Texts that carry a count or a
time ("last ran 47 minutes ago", "3 deliveries pending") changed at every check, so each check sent a "restored" and a
"raised" pair. The failing-webhook issue fed itself: the health event made another delivery to the failing endpoint,
the count grew, and the next check sent another pair.

Fix: an issue may carry a key naming its subject and nothing that changes (stored in `StandingHealthIssues.Key`, not
in the API response). The baseline compares source and key, or the wording when there is no key; an issue still
standing under new wording updates the stored text with no event. Rows from before the upgrade have no key and compare
by their text, so a varying one is resolved and raised one last time, then is stable.
Test: `TasksAndHealthTests.An_issue_whose_wording_changes_between_checks_is_raised_once`.

#### F29. Linking a special to a film recycled the special's file before the hardlink was made

When the film and the special both have a file, the film's wins and the special's own file goes to the recycle bin.
The pass retired the special's file and its row first and only then tried the hardlink, which is never replaced by a
copy. Where the film and the series live on different filesystems (or the destination refuses the new name) the
hardlink failed after the special's file was gone, for good without a recycle bin, and every later pass failed the same
way.

Fix: the hardlink is made first, under a hidden `.bigarr-adopt-` name in the destination's own folder. Only when it
exists is the special's file recycled, its row removed and saved, and anything untracked at the destination recycled;
then the hidden name is renamed into place. A refused hardlink, or a bin that cannot take the file, leaves the special
as it was and shows in the link error. A previous film link (only another name for the film's bytes) is still removed
first. A process killed between the hardlink and the rename leaves a hidden name, one more name for the film's bytes,
which scans skip (hidden file) and nothing cleans.
Test: `SeriesFilmRefusedHardlinkTests.When_both_have_a_file_and_the_hardlink_is_refused_the_special_keeps_its_file`.

#### F30. Reassigning a file skipped the show's gate and could leave its rename unsaved

"Reassign file" was the one writer of a show's files that did not take the show's turn (`SeriesFileGate`), so it could
interleave with an order switch, its undo or a rename. It also saved the row after renaming the file on disk with the
request's token: a caller that went away in between left the file under its new name and the row under the old one.

Fix: reassign takes the show's turn before it loads the show, and the save that follows the move ignores the
request's token. The first save (the episode links) still honours it: nothing on disk has changed by then.
Test: `ConcurrentSeriesWritersTests.A_reassign_waits_for_the_writer_that_holds_the_show`.

#### F31. A grab whose caller went away after the send left an untracked download

The torrent was sent to the client, then the grab row was written (two saves and a commit) on the caller's cancellation
token, and the qBittorrent client's check that the torrent is listed (up to about 4.5 s) used it too. A caller that
went away in that window, such as an aborted `POST /api/v1/grab`, RSS sync or search request, left a download with no
`Grab` row: the version looked idle, the next RSS pass or search grabbed again, and the orphan was later adopted by
title, possibly for another version than the one chosen.

Fix: once a client accepted the torrent, `GrabService` (films and series) goes on with no cancellation token, and the
qBittorrent add check runs to its end. Everything before the send stays cancellable.
Test: `InterruptedGrabTests.A_grab_whose_caller_goes_away_once_the_client_has_the_torrent_is_still_recorded` (film
path; the series path has the same two lines and is covered by the existing series grab tests).

#### F32. A failed download whose removal threw lost its replacement search

The removal from the download client ran before the replacement search was queued and only caught
`DownloadClientException`. Any other exception (a network error while reading the answer) or a cancelled token (the
refresh endpoint polls with the request's token) ended the poll there: no replacement search, no import queued for the
other finished downloads of that tick, the torrent still in the client, and a journal that said it was removed and a
search queued. Nothing retried, since the handling only runs on the transition into failed.

Fix: the imports and searches are queued right after the commit, then the removal runs with no cancellation token and
catches everything: the grab's note and its journal line say the torrent could not be removed and the poll goes on.
Test: `FailedHandlingTests.A_failed_download_whose_removal_blows_up_is_still_searched_again_and_says_it_was_not_removed`.

#### F33. The download client password, indexer API key and media server token came back in clear

The settings API returned each stored credential on every read, to anything that can read it (an API key pasted into
another application, a browser extension, a shared screen with the network tab open).

Fix: on the Bigarr API (`/api/v1`) a response carries a fixed mask (`SecretMask`) where a secret is stored, and a request that sends the mask back
means "unchanged"; an empty value clears the secret and anything else replaces it. A new resource cannot be created with
the mask. The `/test` routes take the id of the saved resource being edited: a masked secret is then replaced by the
stored one for the test, and refused without an id. The `/radarr` and `/sonarr` compat surfaces (admin API keys only)
keep returning the indexer key, as before, because Prowlarr compares it: `IndexerService` reads and writes take
`revealSecrets`, which only those routes pass. The compat surfaces expose no download client password or media server
token.
Tests: `DownloadClientsEndpointsTests.The_password_is_never_sent_back_and_the_mask_keeps_it`,
`ProwlarrPushTests.Prowlarr_can_push_update_and_remove_torznab_indexers` (the compat surface still returns the real key),
`IndexersEndpointsTests.The_api_key_is_never_sent_back_and_the_mask_keeps_it`,
`MediaServerTests.The_token_is_never_sent_back_and_the_mask_keeps_it`.

#### F34. An import, an assign or an episode-file delete whose caller went away left disk and database apart

The endpoints pass the request's cancellation token down. Four paths changed the disk and then went on with that token:
the film import (`ImportService.ExecuteAsync`), the series import (`SeriesImportService.ExecuteAsync`), assigning an
unmatched episode file (`AssignUnmatchedAsync`) and deleting an episode file (`DeleteEpisodeFileAsync`). A caller that
went away after the transfer or the recycling left a file in the library without a row, or a row for a file that was
gone, and nothing repairs that afterwards.

Fix: both imports switch to `CancellationToken.None` just before the first disk change, so the transfer (a hardlink is
instant) and every save after it run to their end; what comes before stays cancellable. `AssignUnmatchedAsync` loads the
rows it detaches in a first loop (no disk), then recycles the files and removes their rows on the neutral token.
`DeleteEpisodeFileAsync` saves with no token once the file is gone. A background import therefore also finishes its
saves during a shutdown instead of being cut after the transfer.
Test: `InterruptedEpisodeFileDeleteTests.An_episode_file_delete_whose_caller_goes_away_after_the_file_is_gone_still_removes_the_row`.
The two imports and the assign have no clock read at a useful moment for that trick; they are guarded by inspection
(no `await` after the first disk change takes the request's token) and covered by the existing import suites.

#### F35. A client error such as a full disk failed and blocklisted the download at once

qBittorrent's `error` and `missingFiles` states mapped to `Failed`, so the first poll that saw one blocklisted the
release, removed the torrent with its data and searched again. A full disk or an unmounted drive clears on its own; the
replacement hit the same error and healthy releases were blocklisted one after another, their partial data deleted.

Fix: those states map to a new status, `Errored` (a warning in the queue with the time left). Only an error that lasts
past `Bigarr__Downloads__ErroredTimeoutMinutes` (60 by default, 0 keeps it a warning) goes through the failed handling;
one that clears before is a download like any other. `Failed` stays for a definitive failure.
Tests: `FailedHandlingTests.A_download_the_client_reports_in_error_is_a_warning_until_the_delay_then_fails`,
`FailedHandlingTests.A_download_that_recovers_from_an_error_carries_on`.

#### F36. Series imports did not wait for the show's turn

One writer at a time may move a show's files: an order switch, its undo and a rename each read every path, move files
and save once, and `SeriesFileGate` keeps them from overlapping. The series import (automatic and manual), assigning
an unmatched episode file and deleting an episode file did not take that gate. An import landing while an order switch
or a rename ran read the existing paths before the other writer moved them: the file it meant to replace was no longer
where it looked, so the moved file stayed on disk without a row and the order journal described a move whose row was
gone. The race was found by reading the code; it was not reproduced.

Fix: `SeriesImportService.ExecuteGuardedAsync` (the one entry of both imports), `AssignUnmatchedAsync` and
`DeleteEpisodeFileAsync` take the show's turn before they read anything, on the request's token (nothing has changed
yet, so a caller that gives up while waiting is harmless). `SeriesImportPlan` now carries the series id for that. The
gate is not re-entrant: none of the three is called from a method that holds it, and none of them calls one.
An import of another show is not delayed.
Test: `ConcurrentSeriesWritersTests.An_episode_file_delete_waits_for_the_writer_that_holds_the_show` (one of the three
entries; the import and the assign share the same one-line change and are covered by the existing import suites).

#### F37. A film import judged the title's quality, not the one chosen for it

The check that refuses an import that is not an upgrade read the quality parsed from the release title whenever the
title parsed at all, so the quality the user picked in the interactive search, or the file name's when the title says
nothing, was ignored there while the file was named, stored and journaled with it. A title saying 1080p grabbed as 720p
replaced a better file; a title saying 720p grabbed as 1080p was refused as not an upgrade.

Fix: the film import judges the quality the import carries (`plan.Quality`), as the series import already did, and a
hand-added torrent is attributed to a version with that same quality. The plan's parsed title is no longer written to
by the check. A release grabbed from the interactive search no longer meets this check at all: the import takes it as
the user chose (`Grab.Interactive`); the check guards automatic grabs and hand-added torrents.
Test: `ImportFlowTests.An_overridden_quality_is_the_one_judged_against_the_file_in_place`.

#### F38. An approved request whose add threw stayed approved for good

`ApproveCoreAsync` saved `Approved`, then added the title inside a `catch` that knew three exception types. Anything else
escaped: a metadata provider timeout (a `TaskCanceledException`, which the film add does not catch), or the approver
closing the tab during the add. The approval answered 500 and the request stayed `Approved` with nothing in the library;
"approve" refused (not pending) and "retry" refused (not failed), so it was a dead end. A process stop between the two
saves left the same state.

Fix: the add (and the target lookup before it) is wrapped in a catch for every exception but out-of-memory, including the
caller's own cancellation: the request becomes `Failed` with the reason, `requestFailed` is published and Retry works.
What follows the add (the request's save and the forward) runs on `CancellationToken.None`. A startup task,
`StrandedApprovals`, turns an `Approved` request with no library id and no Seerr source into `Failed`.
Tests: `RequestLifecycleTests.An_add_that_times_out_fails_the_request_and_a_retry_takes_it`,
`RequestLifecycleTests.An_approval_whose_caller_goes_away_still_ends_failed_or_approved`,
`RequestLifecycleTests.A_stop_between_the_approval_and_the_add_is_repaired_at_startup`.

#### F39. A torrent qBittorrent was still moving or rechecking was imported

qBittorrent's `moving` and `checkingUP` states mapped to `Completed`. While qBittorrent moves a finished torrent out of
the incomplete folder the files are split between the two places, so the import found no video or copied one still
being written, and the grab ended `ImportFailed` with nothing to retry it.

Fix: `moving` maps to `Downloading` and `checkingUP` to `Queued`, with qBittorrent's own message in the queue. The grab
goes to `ImportPending` at the first poll that reports a seeding state, and an import already queued defers while the
torrent is not complete. A move that never ends is not timed out: it stays in the queue with its message until
qBittorrent changes its state (usually to `error`, handled by F35).
Tests: `QBittorrentClientTests.Every_qbittorrent_state_has_a_status` (`moving`, `checkingUP`, `stalledUP`),
`QBittorrentClientTests.Torrents_of_the_category_are_mapped_to_queue_items`.

#### F40. A stored secret could be sent to another address

[F33](#f33-the-download-client-password-indexer-api-key-and-media-server-token-came-back-in-clear) made the mask mean
"keep the stored secret" whatever the host or URL of the same request: an update, or a test with `?id=`, that kept the
mask and pointed the host at another machine made Bigarr sign in there with the stored password, API key or token. The
followed Radarr and Sonarr had the same hole with their own mask.

Fix: the mask, or for a followed app a blank or masked key, is only honoured while the destination stays the same
(`SecretMask.SameDestination`: same scheme, host and port for an indexer, a media server and a followed app; host, port
and `UseSsl` for a download client; a path change alone keeps the secret, so does a username change). Otherwise the
request is refused with "Type the … again", before the stored secret is read into the object used for the connection.
The `/radarr` and `/sonarr` compat surfaces are unchanged: they carry the real key, never the mask. The settings forms
empty the secret field when the address moves.
Tests: `DownloadClientsEndpointsTests.Changing_the_address_with_the_mask_is_refused_and_the_stored_password_stays`,
`IndexersEndpointsTests.Changing_the_address_with_the_mask_is_refused_and_the_stored_api_key_stays`,
`MediaServerTests.Changing_the_address_with_the_mask_is_refused_and_the_stored_token_stays`, a
`Testing_another_address_with_the_mask_is_refused_and_nothing_is_contacted` in each of the three,
`RadarrFollowTests.The_stored_key_is_not_sent_to_another_address`, `SonarrFollowTests.The_stored_key_is_not_sent_to_another_address`,
`ProwlarrPushTests.Prowlarr_moving_an_indexer_to_another_address_with_the_real_key_still_updates_it` (the compat side).

#### F41. An API key opened the whole admin API

Every API key was full admin access. Keys are made for connected apps (Seerr, Bazarr, Prowlarr) but also opened
everything under `/api/v1`: downloading and restoring backups, listing the filesystem, changing download clients. They
end up in other apps' settings, in webhook URLs and in a calendar subscription URL that a third-party service fetches, so
a key leaked from any of those was the admin's account.

Fix: a key has a scope, stored on the key and carried in its principal (`bigarr:scope`). `compat` (the default) opens the
`/radarr` and `/sonarr` surfaces (`ApiKey` policy) and what a connected app calls beside them: the cover images under the
URL base and the followed Radarr and Sonarr webhooks (`ConnectedApp`). `full` opens all of that and the rest of
`/api/v1` (`Api`, `SignedIn`, `ManageRequests`, the permission policies and the fallback). `calendar` opens the iCal feed
only (`CalendarFeed`) and has no view. One rule: a request made with a key passes a policy only if the key's scope is one
the policy names (`KeyScopeIn`); a request made with a cookie is not concerned. Three independent checks apply: the
owner's role ([F24](#f24-a-demoted-admins-api-key-kept-the-compat-surfaces)), the scope, and the view. Every key that
existed before is migrated to `compat`. A refusal on scope answers 403 and names the key's scope (never the key). The scope
is set when the key is created and changed by `PUT /api/v1/apikeys/{id}`, from the cookie session only, so a key can never
widen itself.
Tests: `ApiKeyScopeTests` (default and `compat`, `full`, `calendar`, the refusals, changing the scope, the webhooks and
covers), `UsersAndRolesTests.A_demoted_owner_s_full_key_opens_nothing_admin_until_promoted_again`.

#### F42. A manual import accepted a download that was still active

`POST /api/v1/manualimport` and `/manualimport/series` took a `grabId` in any state. The web dialog only offers the button
on an `ImportFailed` grab, but a stale dialog, a script or a second tab could import a file for a grab the worker was
importing at that moment, then write `Imported` over the worker's state: two writers on one grab, two copies of the
transfer, and the last save won.

Fix: both routes answer 409 before reading anything from disk when a `grabId` names an active grab (`Grabbed`,
`Downloading`, `ImportPending`, `Importing`), through one shared check (`ManualImportService.RefuseActiveGrabAsync`).
`ImportFailed` and the terminal states stay accepted: one film request can carry several files of one grab, and the
first moves it to `Imported`. An id that matches no grab keeps its per-file answer ("Queue item not found."). The
retry-versus-manual-import race on one `ImportFailed` grab stays open.
Tests: `GrabOwnershipTests.A_manual_import_is_refused_while_the_download_is_active_and_leaves_the_grab_alone`.

#### F43. One metadata timeout failed a whole scan or a whole import

A provider call that gets no answer in time throws a `TaskCanceledException`, which derives from
`OperationCanceledException`, the type a real cancellation throws. The film scan and the Radarr and Sonarr imports
protect each folder or title with a `try`, but their first `catch` was a bare `catch (OperationCanceledException)
{ throw; }`, so one slow answer ended the scan of the whole root folder or the whole import as if the app had stopped.
The series scan already had the filter.

Fix: those catches carry `when (ct.IsCancellationRequested)`: only a real cancellation stops the run. In the film scan
`ResolveAsync` also treats a timeout like a provider error (`HttpRequestException`), so the folder is listed as unmapped
with "metadata error" and the scan goes on; the same for the Radarr import's `FetchAsync`. A rerun picks the failed folder
up. The Radarr and Sonarr clients still turn their own timeouts into `RadarrUnavailableException` and
`SonarrUnavailableException`, which still fail the run.
Test: `LibraryScanTimeoutTests.A_metadata_timeout_on_one_folder_leaves_the_rest_of_the_scan_running`. The import side has
no test: the Radarr import only asks the provider for a film whose Radarr payload has no title, and both of its loaders
drop those films first; the Sonarr change is the same one-line filter.

#### F44. One show whose refresh threw ended every series refresh

The "Refresh series" task asks the providers again about the shows that are due, in batches, the one that waited longest
first. A provider failure comes back as a result and is counted; anything else that went wrong inside one show's refresh
(a database constraint, an unexpected shape in the provider's data) was an exception that nothing caught, so the task
failed there and the shows after it were not refreshed. A show that throws never gets its "last refreshed" date, so it
stayed the one that waited longest and came first in every later batch: each run stopped on it and no other show was
refreshed any more.

Fix: the refresh of one show is wrapped. A show that throws (a cancellation excepted) is logged as an error, the tracker
is cleared, it is counted as "stopped by an error" in the task's summary and the run goes on with the next show. It does
not count towards the streak of provider failures that stops a run, which is about the provider being down. The show
keeps its place at the head of the next batches, so it costs one slot per pass until its cause is fixed.
Tests: `SeriesRefreshTests.A_show_whose_refresh_throws_is_counted_and_the_others_are_still_refreshed`,
`SeriesRefreshTests.A_cancelled_refresh_still_stops`.

#### F45. A delete that failed on one folder left the others deleted with their files still listed

A film or a series with versions in several folders loses them one folder after the other. When a later folder could not
be removed (a read-only mount, a permission, an I/O error) the method returned the error at once. The folders before it
were already gone, but nothing was saved: their versions still listed their files, and the "file deleted" lines prepared
before the loop were thrown away with the request. The library showed files that did not exist, with no trace of what
happened to them. [F15](#f15-a-series-delete-whose-caller-went-away-left-the-series-with-no-files) and
[F27](#f27-a-film-delete-whose-caller-went-away-left-a-version-with-a-file-that-was-gone) covered the caller going away;
this is the same gap for a disk error.

Fix: a film delete and a series delete try every folder, then decide. With no failure nothing changes. With a failure,
the files of the folders that went are removed from the database with their history line (a film also publishes
`FileDeleted` for each, since the film is still there), on `CancellationToken.None`, the film or series stays with the
versions whose folder went showing no file, and the answer is still the first error, so the front shows what stayed. A
failure with nothing gone writes nothing. A series version deleted over a folder it shares with another version removes
its files one by one and does the same: the files that went leave the database, the one that failed stays listed.
A folder emptied halfway by a failing recursive delete counts as not gone: its rows stay until the next scan.
Tests: `PartialMovieDeleteTests.A_film_delete_that_fails_on_one_folder_clears_the_files_that_did_go_and_keeps_the_film`,
`PartialSeriesDeleteTests.A_series_delete_that_fails_on_one_folder_clears_the_files_that_did_go_and_keeps_the_series`.

#### F46. An indexer that never answered was never paused

Every indexer goes through `IndexerGateway`, which pauses it after a failed request (5 minutes, then 15, 30, up to a
day) and marks it unhealthy. An indexer that accepts the connection and never answers escaped this. The HTTP client's
timeout and the search's deadline for one indexer were both 60 seconds, and the deadline starts first (before the wait
for the indexer's rate limit), so it always fired first. The request was then cancelled from outside: the Torznab
client does not turn a cancellation it did not cause into an indexer failure, and the gateway never heard of it. The
search reported "no answer within 60 seconds" for that indexer, the next search did the same, and the indexer cost 60
seconds on every search and every RSS pass while staying "healthy". The code that would pause it (the client's own
timeout becoming an `IndexerException`) could never run.

Fix: the two timeouts are named and ordered. `TorznabClient.RequestTimeout` stays 60 s, so no indexer that answers today
starts failing; `IndexerQueries.PerIndexerTimeout` is now 90 s. A request that gets no answer ends as the client's
failure ("did not answer"), the gateway counts it and pauses the indexer. The deadline itself still counts nothing: it
also covers the waits on the rate limit and cannot be told from the user leaving. A search can therefore wait up to
30 s longer for an indexer that hangs, once, until it is paused.
Not covered: a series search of several requests where a late request hangs; the deadline (90 s from the start) can
fire before that request's own 60 s, and nothing is paused, as before. The RSS pass and the film searches send one
request and do pause the indexer.
Tests: `IndexerTimeoutTests.An_indexer_that_never_answers_is_paused_after_its_first_timeout`,
`IndexerTimeoutTests.A_search_the_user_leaves_does_not_pause_the_indexer` (pins why cancellations are not counted),
`IndexerTimeoutTests.The_request_timeout_is_shorter_than_the_search_deadline`.

#### F47. The event stream stayed closed after a restart and missed events were never refetched

The web app keeps one `EventSource` on `/api/v1/events`; each event invalidates the queries of the pages that show it.
A browser reconnects an `EventSource` by itself only after a network drop. When the server answers the stream request
with anything but a 200 (a 502 from the reverse proxy while the container restarts, a 401), the browser closes the
stream for good: no page refreshed any more until the tab was reloaded, and nothing showed it. Events published while
the stream was down were lost as well, and nothing refetched when it came back.

Fix: the stream is opened by `openEventStream` (`web/src/hooks/eventStream.ts`). When the browser has given up
(`readyState` closed) it closes the source and opens a new one after 5 s, then 10 s, then every 30 s; a successful open
resets the wait. Every open that follows an interruption, whether the browser retried or the stream was reopened,
invalidates every query, which refetches the ones on screen. The first open does not. The queue page keeps its own copy
of the retry.
Tests: `web/test/eventStream.test.ts`.

#### F48. Another site on the same host or domain could act in the admin's session

The web UI signs in with a cookie, and a browser attaches it to any request sent to Bigarr's address, whatever page sent
it. `SameSite=Lax` stops other *sites*, not another application on the same host and another port (cookies ignore ports)
or on a sibling subdomain. Several routes that change a lot take no body (restore a backup, run a task), so a hidden form
or a `fetch` from such a page was enough to trigger them in the admin's session.

Fix: `SameOriginGuard`, a middleware before authentication. A request is refused with 403 when its method is not safe, it
carries the `bigarr_session` cookie, and either `Sec-Fetch-Site` is present and neither `same-origin` nor `none`, or it is
absent and `Origin` is `null`, not an address, or has a host and port other than the request's `Host` (compared without
case, the scheme is not compared so that a proxy ending TLS without forwarding it does not make everything look foreign).
Safe methods, requests with no session cookie (API keys, sign-in) and requests with neither header (not a browser) pass.
The host is the one after the forwarded headers, so a proxy must pass `Host` or `X-Forwarded-Host`; the 403's text and
the log line say so. `Bigarr__Auth__RequireSameOrigin=false` turns the check off.
Tests: `SameOriginGuardTests` (the rule case by case, then through the real application: each `Sec-Fetch-Site` value,
`Origin` against `Host` and `X-Forwarded-Host`, a safe method, an API key with no cookie on `/api/v1` and `/radarr`,
sign-in from the same origin, the switch).

#### F49. A full sign-in throttle stopped counting new usernames and addresses

`SignInThrottle` counts failed sign-ins per username and per client address and keeps at most 10,000 keys. When the
table was full and no key had expired, a failure on a new key was not recorded. The address a failure is counted under
can be forged by a client that reaches Bigarr directly, so such a client could fill the table with made-up usernames and
addresses and then guess every username not already in it without any limit until the 15-minute window emptied the
table: the protection turned itself off exactly under attack.

Fix: a full table (after sweeping the expired keys) forgets the 100 keys that have gone longest without a failure and
always records the new one. "Longest without a failure" is decided by a counter bumped at each recorded failure, not by
time, since several failures can share an instant; looking a key up never bumps it. The counters are still in memory:
a restart forgets everything. Not covered: a client able to forge its address can still make a blocked username
forgotten by causing about 5,000 failures on other keys, which buys it a few more guesses on that username.
Tests: `SignInThrottleTests.A_full_table_still_records_a_new_key_and_forgets_the_one_that_failed_longest_ago`,
`SignInThrottleTests.A_key_that_keeps_failing_is_not_the_one_forgotten`.

### Open

What is known and not fixed:

- A retry and a manual import of the same failed grab at the same moment are not serialised (see the grab's ownership
  rules).
- A `.bigarr-partial` left by a killed transfer is only removed by a retry to the same destination.
- The in-memory event stream is at most once: a subscriber that falls 256 events behind drops the oldest. Suspected only; whether a bulk operation can outrun a subscriber was not measured.
- Two follows may share a version name; the pick view is selected by version name only. Reported, not re-read.
- Playback: some viewings recorded by Tautulli are missed, often the first of an evening, with the next item on the
  same player recorded from where the missed one ended. One case had its session opened, idle for hours, then closed
  without a play. Not reproduced; the Information lines on new streams and closes without a play should explain the
  next one. Many apparent misses were viewings resumed live into an imported play
  ([F25](#f25-a-tautulli-re-import-counted-a-viewing-resumed-live-twice)); none was seen in the first day after
  [F22](#f22-a-new-plex-session-id-chopped-a-viewing-into-pieces).

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
