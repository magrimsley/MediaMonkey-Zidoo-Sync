# Privacy — draft for publication review

Deployment settings and configuration archives are stored locally. Existing per-user settings may take precedence over packaged defaults, and valid legacy settings may be copied into per-user storage without changing the original.

The application contains user-invoked device discovery and assessment features. When invoked with configured values, those features may access the selected local database, network storage and device. Production synchronization is disabled in this alpha. Offline regression fixtures do not authorize access to real devices or playlists.

Logs and exported diagnostics can contain device identifiers, filesystem paths and playlist names. Review and redact them before sharing. Do not upload databases, configuration files, secrets or private library information to public issues. The project does not intentionally implement an analytics upload service; this draft is not a guarantee about Windows, .NET, GitHub, package feeds or other third-party services.

Uninstallation retains configuration, archives, unrelated files and recovery evidence. Users manage retained local data separately. A maintainer contact channel must be selected before publication.
