# Code signing policy — draft

Current state: unsigned. No SignPath application has been submitted or approved, and no certificate or signing sponsor is available to this project. Do not infer approval from the presence of this document.

The intended future policy is to sign only project-owned artifacts built from reviewed repository source. Third-party binaries retain their upstream signatures and attribution. Future releases require an authorized maintainer's explicit signing approval. SignPath credentials and signing approvals must not be exposed to pull-request builds.

GPLv3 has been selected. Before applying, establish the public source/download pages and an actual release, assign named author/reviewer/approver roles, enable MFA for repository and signing access, complete dependency/privacy review, and connect verifiable source builds. SignPath may require verifiable project reputation and may decline an application.

If approved, update this page and release pages with the exact sponsorship attribution required by SignPath, real role assignments, privacy policy and artifact metadata restrictions. Do not publish sponsorship language as an existing fact before approval.

Requirements reference: https://signpath.org/terms.html

## Confirmed roles and remaining setup — 2026-10-20

Michael Grimsley (GitHub: magrimsley) confirmed that he serves as maintainer/author, reviewer and final signing approver. Every production signing request requires his approval. The repository and Beta download are now public by his explicit authorization. He reported that GitHub two-factor authentication is not yet enabled; enable it before applying and use MFA for SignPath access. No signing application has been submitted, no provider has approved the project, and the Beta remains unsigned.

