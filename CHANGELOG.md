# Changelog

All notable changes to **WinyWash** are documented here.

## [1.2.0] - 2026-11-07

### Added
- **International News Feed**: Curated top daily stories for English and global languages (The Verge, TechCrunch AI, Wired, Ars Technica, Yahoo Sports).
- **Full UI Multi-Language Localization**: Complete Persian, English, Arabic, German, Spanish, French, Japanese, Korean, Russian, and Simplified Chinese localization across all cards, modals, and tooltips.
- **Dynamic Character Hero Localization**: Intelligent live metric translations (junk files, RAM, registry entries, and custom dialogue).
- **Streamlined Distribution Bundle**: Optimized binary bundle reducing release footprint from 446 MB to ~66 MB.

### Changed
- Converted all Launcher diagnostics and error dialogs to standard English.
- Updated project branding and repository references to **WinyWash**.
- Enhanced reader mode styles for international publications and isolated CSS sandboxing.
- Reclassified VPNs and utility extensions (Hoxx VPN, ZenMate, Similarweb, SetupVPN, Astar) into `ReviewWarning` to prevent accidental removal without explicit user review.

### Fixed
- **Browser Extension Subfolder Backup**: Preserved complete relative folder structure (`relPath`) for extension subpaths (`Local Extension Settings`, `IndexedDB`, `Extension State`) into discrete backup subfolders.
- **Accurate Freed Storage Reporting**: Computed liberated space and count only after confirmed filesystem deletion.
- **Fail-Safe Recycle Bin Handling**: Implemented Win32 `SHFileOperation` with `FOF_WANTNUKEWARNING` to detect drives without Recycle Bin support or oversized files, automatically falling back to secure Quarantine instead of permanent deletion.
- **In-Process Direct Registry Backup**: Eliminated reliance on external `reg.exe` by exporting full Unicode `.reg` backups directly via native .NET Registry APIs, strictly aborting key deletion if backup fails.
- **Hierarchical Protected System Guard**: Enhanced `IsProtectedSystemPath` to reject not only exact system directories but also all parent/ancestor paths (such as `C:\Users`, `C:\Windows`, and root drives) using normalized path separators.
- **Safe Startup Item Collision Resolution**: Preserved existing shortcut files during disable/restore operations by renaming to `.conflict` timestamps instead of silent deletion.
- **Defensive Telemetry Service Restoration**: Restored genuine Windows default start types (`DiagTrack` = 2 Automatic, `dmwappushservice` = 3 Manual) with defensive fallbacks and null-safety.
