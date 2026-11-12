---
lang: en-GB
---

# Threading and lifecycle maintenance review

The baseline is upstream commit `6bd1c2bd7d296a244b907364043719df8f24d08e`, corresponding to release v1.3.0. This is a targeted maintenance review, not a complete security audit of the downloader or its bundled libraries.

## Required fixes

- Background icon application changed live KeePass entries and raised `EntryTouched` on worker threads. Database changes, icon flags and entry events now run on the captured UI context; downloads remain on workers. This prevents dependent plugins, including breach checkers and themes, from receiving these changes on an inappropriate thread.
- Each dashboard is tied to its original database/root. Closed, locked or switched databases, removed entries and editor/history clones are rejected before applying results. Database changes are marked before entry callbacks, which may open modal dialogs and allow a lock/close.
- Worker exceptions could escape ThreadPool callbacks and terminate KeePass, or leave counters active forever. A common worker boundary records a failure count and releases active counters in `finally`. Diagnostics contain the exception type rather than entry data or URLs.
- Queued work could briefly appear complete before workers started, and jobs queued for an older batch could run after a restart. Pending jobs now keep the batch active; generation checks reject superseded work.
- Closing previously waited and pumped nested UI messages while holding a lock. The dashboard now closes immediately and rejects subsequent results. Already running network work can finish in the background, subject to the original downloader timeouts; closing does not abort every underlying request.
- Shared state flags are scoped to a dashboard instance, and failed plugin initialisation/termination is handled safely.

## Build and compatibility

The normal DLL project now references a supplied KeePass assembly instead of requiring an external KeePass source checkout. It targets AnyCPU/.NET Framework 4.8 and compiles the existing LomsonLib sources into the plugin, consistent with the single-assembly PLGX approach. Both project templates include the worker helper; inherited developer-specific source-analysis paths were removed from these templates. The development assembly is 1.3.1.0 and update checks are disabled until fork releases exist.

Verification uses KeePass 2.62. Fifteen targeted regression cases cover UI-thread mutation/events, preserved password/breach metadata, shared icons, stale entries/databases, queued work, failure counters and non-blocking close. The forms remain unshown, and all entries are synthetic. The production DLL builds successfully; four existing compiler warnings remain in legacy sources (two obsolete Crc32 uses and two unused exception variables).

The companion HIBP regression suite exercises the actual entry/shared-icon mutation methods from both the original release source and the rebuilt development DLL. Its additional Fluent Theme checks use the exact released `FluentTheme.dll` v1.5.2026.1111 and render synthetic tag changes off-screen in light and dark modes. These checks support the tested code paths, not a blanket guarantee of complete visual compatibility.

## Limitations and publication

Interactive menus, real favicon downloads, Stop/restart under a large real batch, plugin load order, real KeePass save/lock/shutdown timing and Linux/Mono have not been validated. Test the local DLL with a disposable database before using it with important data. Do not load both the upstream PLGX and this DLL into one KeePass instance.

The downloader's existing networking/TLS policy and bundled HtmlAgilityPack were not comprehensively audited or modernised in this change. No guarantee that all vulnerabilities have been found is made.

Only source, synthetic tests and documentation are included in the new commit. Downloaded dependencies, compiled DLLs, local paths, logs and local test artefacts are excluded. The original upstream history and images are inherited; no fork binary release is published.
