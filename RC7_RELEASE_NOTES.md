# MM Zidoo Sync 1.1.0-rc.7

Application 1.1.0-rc.7; MSI revision 1.1.7. This candidate supersedes RC6.

The native MSI Icon control previously loaded the first 16x16 image in the application's multi-resolution ICO and enlarged it for the setup page. RC7 embeds the existing highest-resolution 256x256 frame as the setup-only icon. It copies the original image payload bytes without resizing, redrawing or generating artwork. Windows renders that higher-resolution frame into the existing setup image area.

The executable, shortcuts and Programs and Features retain the original multi-resolution icon. Native Windows Installer still controls its title-bar and Preparing to remove icons. No synchronization, configuration, file-selection, device or recovery logic changes in this revision. RC6's immutable PowerShell parity corrections remain intact.
