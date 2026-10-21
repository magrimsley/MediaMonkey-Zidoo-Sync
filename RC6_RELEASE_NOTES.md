# MM Zidoo Sync 1.1.0-rc.6

Application 1.1.0-rc.6; MSI revision 1.1.6. This candidate supersedes RC5.

RC5 introduced two folder-layout restrictions that the immutable PowerShell exporter does not impose: it rejected identical Music Root/Playlist Folder paths and restricted browsing to direct parent/child folder layouts. RC6 removes those restrictions. The application passes the configured paths to the existing native implementation without substituting a folder, recursively searching for another mirror, or changing which playlists qualify.

Browsing suggests names only for unambiguous layouts. It preserves the selected path and existing manual device-navigation names for other layouts. Users remain responsible for reviewing configuration. A file comparison with no M3U files in the selected folder may legitimately propose all managed playlists as Create; the application must not change process rules to conceal that outcome.

Audit anchors: the frozen exporter sets OutputFolder from NAS.PlaylistFolderUNC, selects exactly one case-sensitive MediaMonkey parent, recursively includes its descendants while excluding the container itself, creates each target under OutputFolder, and enumerates stale M3U files in that output folder. SourceReference and all 39 reference hashes remain unchanged. The audit also found that native file enumeration included hidden/system M3Us, whereas the frozen scripts enumerate without -Force. RC6 shares that exact nonrecursive visible-file scope across native stale-file handling, equivalence comparison, transaction planning and execution baseline enumeration. It does not change device navigation, transactional recovery, playlist descendant rules, or configured paths. Hidden files and their attributes stay untouched during this audit. This targeted RC5 regression audit does not replace full end-to-end owner acceptance.

Retain the remaining RC5 setup, Help, JSON import, log management, layout, and scrollbar improvements. The native Windows Installer icon limitation remains documented. No live device commands form part of this correction.

The frozen orchestrator selects UPDATE by exact baseline/stage SHA-256 hashes. RC6 removes native logical-line normalization from change planning and execution decisions, and builds candidate bytes using the frozen Windows UTF-8 BOM/CRLF format. The diagnostic comparison still explains line differences and also reports encoding/line-ending-only byte changes.

Review and execution retain the frozen CREATE classification when an expected playlist is absent from the visible baseline, including hidden targets. Physical hidden target bytes still receive their own reviewed hash and backup for safe file replacement; classification does not discard recovery protections. This audit never modifies user file attributes.
