---
lang: en-GB
---

# Three-party maintenance review

The upstream baseline is `6bd1c2bd7d296a244b907364043719df8f24d08e` (v1.3.0). Three separate reviewers examined both this plugin and the companion HIBP maintenance fork: security/privacy, robustness/lifecycle, and usability/theme compatibility. The implementing developer then addressed their findings and ran the checks below. The reviewers performed source reviews; this is not independent certification or a comprehensive audit of KeePass, the theme or the bundled parser/decoder.

## Required fixes

| Area | Finding and resulting behaviour |
| --- | --- |
| Readability | Proportional shrinking compressed headers and values into unreadable columns. Columns now use measured header/content widths with readable minima, full-row tooltips and horizontal scrolling when necessary. The explicit **Fit columns** button recalculates widths; ordinary selection preserves manually widened main columns. |
| Window layout | Old saved dimensions could restore a cramped or off-screen window. The dashboard now requests a larger window, clamps its bounds to the current monitor working area, increases the list pane and uses compact previews/actions. Preview boxes adapt to available cells. Very narrow screens still require scrolling; arbitrarily long values cannot all fit simultaneously. |
| Theme contrast | Native header colours could produce black text on a dark background. Headers now draw using the list's foreground/background, with a system-colour high-contrast fallback and sort indicators. |
| Multi-selection | The entry icon picker repeatedly changed only the first checked entry. Every chosen live entry now receives the selected standard/custom icon. |
| Resource ownership | Replaced previews and download image lists leaked resources, while same-size resizing could return a borrowed source image. Previews are now owned copies, replaced images are disposed, and borrowed KeePass image lists are detached rather than disposed. Finder results and owned icon copies are released when the dashboard closes. |
| UI and database lifetime | Background icon application changed live entries and raised entry events on worker threads. Mutations now use the captured UI context and validate the original open database/root and live entry. Locked, switched, removed and cloned entries are rejected. Password fields and breach metadata are preserved. |
| Workers and Stop | Unbounded jobs and worker exceptions threatened responsiveness/process stability. At most four jobs are scheduled concurrently, failures release counters, and older/cancelled batches cannot start work. Stop/close cancel active clients; UI application consumes cached results only and cannot restart a download. Rejected stale-URL results release pending reservations. |
| Unsafe URLs | Favicon and refresh links could invoke local file/UNC or other unsupported handlers. All initial, linked and redirected URLs now require absolute HTTP/HTTPS without embedded credentials. Backslash paths and HTTPS-to-HTTP downgrades are rejected; relative paths, case, IPv6 and explicit ports are preserved. |
| Excessive responses | Network reads and decoding lacked useful resource bounds. HTML is capped at 512 KiB, images at 2 MiB, requests at 10 seconds and each finder at 30 seconds of elapsed network budget. Deadlines cover headers, stream acquisition and body reads, including handlers ignoring cancellation. Late disposable results are released. |
| Parsing and decoding | HTML nesting, redirect chains, candidate lists and decoded dimensions could amplify work. Limits now cover nesting (128), HTTP redirects (5 per request), meta refreshes (3), page links (12), tested image URLs (24), retained gallery results (8), dimensions (2048 per axis) and aggregate ICO pixels (4 Mi). ICO payloads require recognised headers and disjoint ranges before decoding. Retained images are reduced to at most 256 pixels per axis. |
| Shared policy | The original finder changed process-wide TLS settings. TLS 1.2 is now configured only on its HTTP handler. Legacy static network/image caches were removed; failed construction no longer risks a throwing finaliser. |

The companion HIBP review additionally fixed entry events lost during bulk scans, unnecessary dirty-state changes after saving, inconsistent menu checkmarks and silent repeated manual failures. Its response buffer is bounded and failures preserve existing breach information. See its own review for the complete findings.

## Further release reviews

Two further rounds used the same three independent source-review roles. The first found that action/checkbox events could re-enable Start during an active batch, and that narrow panes could clip Stop. It also found worker-side cache/image reads after UI publication. The fixes centralise all start guards, require the original generation for registration/publication/application, run complete row updates on the UI context, and adapt the action controls to the pane width. The second round rechecked those fixes. The implementing developer ran the tests; source reviewers did not independently execute them.

## Verification

- Production DLL: AnyCPU, .NET Framework 4.8, assembly version **1.4.1.0**, built against the official signed KeePass 2.62 assembly. Both project templates include the new helpers and HTTP reference.
- **54 Dashboarder regression checks passed**, using synthetic in-memory databases and fake HTTP handlers. They cover layout/header minima, manual column widths, geometry calculations and action-control bounds at scale factors 1/1.5/2, multi-entry assignment, repeated preview replacement, stale selections, worker limits, database lifetime, URL validation, byte/decode bounds, deadlines, busy UI changes and Stop/close races.
- The production build retains three existing warnings in bundled HtmlAgilityPack (two obsolete Crc32 uses and one unused exception variable). The test project builds without warnings.
- The companion HIBP suite passed **69 offline checks plus one live documented synthetic HIBP query (70 total)**, including calls into the actual rebuilt Dashboarder entry/shared-icon mutation methods and the exact released Fluent Theme v1.5.2026.1111 DLL.
- Synthetic light/dark tag repaint checks exercise the theme's actual details panel. A separate off-screen dashboard preview uses its released palette/dialog styler. These checks support those paths; native title bars, plugin load order and every interactive theme behaviour are not covered.
- `git diff --check` and a manual review of the exact source/test/documentation changes form part of publication checks. Local previews, dependencies, binaries, logs, database material and installation files are excluded.

## Limits and recommendations

The harness does not open or save a real password database. Real KeePass menu/save/lock/shutdown timing, large real download batches, multi-monitor DPI transitions, Linux/Mono and arbitrary websites remain unverified. Scale-factor geometry checks are not a substitute for testing on physical high-DPI monitors. Test the candidate with a disposable database before important use. Do not load the upstream PLGX and this DLL together.

Downloading favicons necessarily contacts the websites derived from entry URLs and may follow permitted HTTP/HTTPS links to other hosts, including network-local HTTP services. Those servers see ordinary connection/request information. No default Windows credentials are supplied. This feature does not need the password field. Disabling downloads avoids that traffic. This review does not provide a network-isolation boundary or guarantee safety against every decoder/parser vulnerability.

The safer format subset accepts PNG, GIF, BMP, JPEG and recognised PNG/DIB ICO payloads. SVG, WebP, unusual ICO payloads and HTTPS sites redirecting to HTTP may now return no result. Only TLS 1.2 is enabled on the downloader; this is deliberately more restrictive than arbitrary platform negotiation. Per-item bounds and close-time cleanup reduce memory growth, but retained choices still scale with the number of rows searched during the session; there is no fixed whole-session RAM cap.

The v1.4.1 fork release supplies the DLL and its SHA-256 checksum. Update notifications remain intentionally disabled; updates are manual through this fork's release page. No rebuilt PLGX is supplied and the inherited creation script remains unvalidated. Optional future work includes a bounded gallery eviction policy and more cross-platform/real-application visual testing; these are recommendations, not claims of completed work.
