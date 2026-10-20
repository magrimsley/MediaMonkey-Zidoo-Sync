# Alpha60.18.9: splash timing and About artwork

Installed local UI correction, October 20, 2026.

- Replaced the native activation-based splash with a WPF image window using an arrow cursor.
- Starts a nonblocking two-second dispatcher timer when the main window first renders. Closes the splash automatically at expiry or when the main window closes; no activation event is required.
- Replaced About's standard message box with an About window showing the supplied second image, product/runtime information and an OK button. Help retains the same image.
- Retained optional-diagnostics removal, icons, shortcuts and the required four-step synchronization workflow.

Verification:33/33 focused UI/configuration checks passed against the exact final published DLL, including timing, dispatcher responsiveness, About artwork, tray lifecycle and existing Run History selection. A normal-startup observation of the same splash implementation measured 1.9689 seconds from observed main UI to splash disappearance (UI Automation polling includes latency). Final package file hashes verified; installed DLL matches package; desktop and Start shortcuts point to18.9; all existing configuration/archive hashes unchanged. Previous18.8 retained. No existing user application instance was deliberately closed.

Installed DLL SHA256:D6B629A2EB77B0FB191463AD90827DDD3B80FBAAFCD8BCE0CC1D1A7B7166865F.

The backend sharing issue remains open. This UI correction does not establish a green full regression or fix the backend. No live Zidoo operations, GitHub push, CI run or prerelease publication occurred for18.9.

The latest additional-dialog screenshot was unavailable. Tests briefly created fixture windows; the specific reported dialog could not be identified from available evidence.
