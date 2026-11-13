---
lang: en-GB
---

# KeePass Custom Icon Dashboarder

## Maintenance fork

This maintenance fork adds a readable, resizable dashboard with measured columns, theme-aware headers and compact previews. It also fixes multi-entry icon assignment, download cancellation, resource ownership and unsafe or excessive favicon responses. See [REVIEW.md](./REVIEW.md) for verification and limitations, and [HOWTO_COMPILE.md](./HOWTO_COMPILE.md) for the local build procedure.

Version **1.4.1** is distributed as `CustomIconDashboarderPlugin.dll` from this fork's [releases](https://github.com/cantwalkwontrun/KeePass-Custom-Icon-Dashboarder/releases). KeePass update notifications remain intentionally disabled; check the fork releases manually. No PLGX is supplied.

## Original project

This plugin provides some management features and statistics for custom icons in KeePass.

High definition of custom icon can be uploaded. URLs of associated entries are used to 
retrieve the highest definition of icons.

For each custom icon, the following information is available:
- Number and list of entries that use it
- Number and list of groups that use it


Features are:
- Download highest definition of custom icons from favicon URL
- Remove custom icons
- Modify custom icons of groups and entries
- Get list of custom icons in KeePass
- Get number and list of entries used by each custom icon
- Get number and list of groups used by each custom icon


## Installation

### Pre-requisites

- Windows with KeePass Password Safe 2; verification used KeePass 2.62
- .NET Framework 4.8 or later


### Installation steps
1. Download `CustomIconDashboarderPlugin.dll` and `SHA256SUMS.txt` from the [fork release](https://github.com/cantwalkwontrun/KeePass-Custom-Icon-Dashboarder/releases/latest). Verify the DLL with `Get-FileHash -Algorithm SHA256` against the checksum file.
2. Close KeePass and keep a local copy of the currently working plugin for rollback.
3. Replace the DLL in KeePass's `Plugins` directory. Remove an older `CustomIconDashboarder.plgx` from that directory so only one Dashboarder version loads.
4. Start KeePass and test with a disposable database first. See [REVIEW.md](./REVIEW.md) for the tested scope.


## Screenshots
A menu item is added to **Tool** menu

![screenshot-menuitem](docs/images/screenshot-menuitem.png)

Custom icon Dashboard (downloading in progress)

![screenshot-dashboard](docs/images/screenshot-dashboard-1.png)


## Changelog

### Version 1.4.1 (maintenance fork)
- Readable columns, monitor-bounded window sizing, theme-aware headers and responsive action controls.
- Correct multi-entry assignment and image ownership; clear stale previews and release owned resources.
- UI-thread database changes, bounded workers/downloads/decoding, reliable Stop/close and generation-bound result acceptance.
- Prevent overlapping batches when actions or checked rows change during a download.

### Version 1.3.0
- Manage malformed URL
- Detect and fix corrupt icons

### Version 1.2.0
- Simultaneous icon downloads by introducing multithreading
- Remember size and position of dashboard when closing windows
- Plugin is now hosted by github
- Improvement: Update HtmlAgilityPack with version v1.11.12

### Version 1.1.0
- All icons from URL are downloaded. The best-sized icon is chosen by default.
  Then it is possible to change manually the chosen icon.
- Add support of downloading icons of entries with standard icons.
- Display index of selected icon in the upper right corner of dashboard.

### Version 1.0.0
- Add support for downloading icons from url of entries

### Version 0.3.0
- Display size of icons in the list icon view
- Display selected icon with different sizes 

### Version 0.2.1
- Ensure that icon list is correctly rebuilt after a removal (Ticket #2)

### Version 0.2
- Add support for modifying custom icons
- Add support for removing custom icons


### Version 0.1
- Enhance Dashboard Form: resizable and sortable lists
- Add the column "total" in the list icon view
- Disable menu item if no database is open
- Change version numbering: x.y.z -> x.y


### Version 0.0.1
- Initial version
