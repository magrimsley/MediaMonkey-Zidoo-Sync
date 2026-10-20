# Privacy — MM Zidoo Sync Beta

The application stores configuration, configuration archives, UI state and diagnostics locally under the user's LocalAppData folders. Synchronization reports, transaction evidence, exported playlists and backups use the configured report/NAS locations. Existing per-user settings may take precedence over packaged defaults; valid legacy settings may migrate without changing the original.

When the user invokes validation, review, discovery or synchronization, the application reads the selected MediaMonkey database and playlist files and accesses the configured network storage and Zidoo. Production synchronization is enabled behind the validation, exporter verification and review gates. MediaMonkey is the source of truth. Synchronization can update NAS files and Zidoo playlists and manage the device's power state according to the reviewed workflow.

User-invoked discovery checks active directly connected IPv4 subnets using Zidoo's device-information HTTP endpoint on port 9529, with bounded concurrency and a subnet-size limit. Wake-on-LAN can send a local broadcast. Device commands use local-network HTTP rather than TLS; use a trusted network. Discovery does not search arbitrary ports for security vulnerabilities.

The reviewed application code contains no automatic analytics or diagnostic-upload service. Network operations serve the user-invoked local device/storage workflow; this statement does not cover independent Windows, .NET, GitHub, bank or package-feed services. Opening local logs uses the user's associated viewer. Development builds obtain dependencies from configured package feeds.

Logs and transaction evidence may contain IP/MAC addresses, model names, filesystem paths, track/playlist information and exception details. Review and redact them before sending them to anyone. Do not upload databases, settings, credentials or private library information to public issues. Sending an email report or a Zelle donation is a separate user action through the user's email/bank service.

Uninstallation preserves configuration, archives, unrelated files and recovery evidence. Users manage retained data separately; preserve evidence while recovering an interrupted transaction.

Security/privacy contact: magrimsley@hotmail.com. See SECURITY.md. This describes the reviewed Beta source; review again if network, logging or storage behavior changes.
