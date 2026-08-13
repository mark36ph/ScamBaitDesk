# ScamBait Desk

ScamBait Desk is a defensive WinUI 3 workspace for reviewing a **dedicated test inbox**, assessing suspicious messages, conducting controlled manual engagements, and preserving case evidence. It never automates replies, downloads attachments, embeds remote content, or sends attachments.

## What the first version does

- Connects to an IMAP inbox over TLS and fetches message text read-only.
- Stores the inbox password in Windows Credential Locker.
- Scores common scam signals locally and explains every flag.
- Redacts likely email addresses, phone numbers, URLs, and long account-like numbers.
- Generates a neutral draft that asks for verifiable details without disclosing personal data.
- Saves cases and evidence notes as local JSON under `%LOCALAPPDATA%\ScamBaitDesk`.
- Groups related inbox messages into chronological case conversations.
- Tracks New, Investigating, Awaiting verification, Reported, and Closed states.
- Maintains an audit timeline for case creation, saves, status changes, and approved lookups.
- Searches inbox messages and cases from one field.
- Opens a sender-domain reputation lookup only after showing exactly what will be disclosed.
- Preserves relevant message headers and explains SPF, DKIM, and DMARC results locally.
- Compares From, Reply-To, and Return-Path domains and highlights identity mismatches.
- Shows the probable originating IP, Message-ID, warnings, and expandable raw headers.
- Extracts URLs, domains, email addresses, IPs, phone numbers, crypto wallets, payment handles, and account-like numbers locally.
- Deduplicates indicators across a conversation while retaining occurrence counts and source messages.
- Keeps URLs inert and requires explicit approval before opening a domain, IP, or URL reputation lookup.
- Exports a case as a portable ZIP containing a redacted HTML summary, transcripts, headers, notes, timeline, indicators, and case metadata.
- Generates a SHA-256 manifest for every evidence file and records the manifest hash in the case timeline.
- Sends a single plain-text SMTP reply only after a privacy review and two explicit safety confirmations.
- Locks the recipient to the selected message sender; there is no free-form recipient or bulk-send path.
- Blocks likely passwords, authentication codes, payment-card numbers, bank details, and wallet recovery material.
- Warns about locations, phone numbers, links, threats, and authority claims before sending.
- Limits each case to one outbound message every two minutes and five per hour.
- Stores a redacted outbound audit log and Message-ID in the case and evidence export.
- Manages reusable fictional personas locally and assigns one persona to each case.
- Provides safe reply templates for common scam patterns; applying a template never sends it.
- Permanently stops outbound engagement per case and records the reason in the audit timeline.
- Generates editable, redacted report drafts for fraud services, providers, banks, registrars, and law enforcement without submitting data automatically.
- Shows a case dashboard with active, high-risk, stopped, replied, and reminder-due totals.
- Detects possible duplicate cases locally using sender and subject-term similarity.
- Schedules local manual follow-up reminders; reminders never send messages.
- Adds one-click 24-hour follow-up reminders for active conversations.
- Organises cases with Low, Normal, High, or Urgent priority and up to ten editable tags.
- Quarantines attachments as inert metadata only, with no open, preview, save, or execute action.
- Provides a persistent global emergency stop that disables all outbound sending.
- Exports a lightweight redacted text summary separately from the full evidence ZIP.
- Tracks conversation stage, a current engagement objective, a total reply budget, and an optional deadline per case.
- Provides controlled playbooks for identity verification, payment evidence, delaying safely, and preparing a report.
- Generates a concise local case briefing with risk, stage, message counts, contradictions, overdue reminders, and the next suggested action.
- Provides a provider-neutral call workspace that uses only phone numbers extracted from the active case and opens one number in the configured Windows VoIP handler.
- Stores redacted call outcomes, notes, recording-consent confirmation, and timeline entries in the case and evidence export.
- Does not provide caller-ID spoofing, autodialling, bulk calling, covert recording, or automatic calls.
- Opens the official FastVPN Windows client and reports whether Windows exposes an active VPN/tunnel adapter; it does not automate or bypass FastVPN's own Connect control.
- Saves reusable reply templates locally after applying the outbound privacy guard.
- Tracks a per-case investigation checklist and manual work-session duration.
- Defangs extracted URLs, domains, and IP addresses locally for safer copying and reporting.
- Orders the main navigation as a numbered case workflow and keeps Settings separate at the bottom.
- Associates the process with the installed package identity before creating the window so Windows can resolve the taskbar icon reliably.
- Organises Settings into compact account, monitoring, integration, maintenance, and collapsible diagnostic sections.
- Edits and deletes user-created reply templates while keeping built-in safety templates read-only.
- Exports and merge-imports the complete local case library as a manual JSON backup.
- Verifies exported evidence ZIP contents against their SHA-256 manifest entirely offline.
- Shows a recent case-activity feed on the dashboard.
- Provides a prominent standalone Website check from the sidebar, Start Here page, and command bar; it checks address structure locally without contacting the target and opens an existing VirusTotal report search only after explicit approval.
- Performs an optional, explicitly approved live page-content scan without running JavaScript or submitting forms; it blocks private-network destinations, restricts ports and redirects, caps downloads at 1 MB, and flags credential forms, external submissions, authentication codes, advance fees, crypto, gift cards, remote-access tools, urgency, hidden frames, and script-obfuscation indicators.
- Explains every website warning with a severity, category, score contribution, safe evidence description, reason for concern, and recommended next action, plus a high/medium/low scan summary.
- Replaces the empty dashboard workspace with a state-aware Start Here guide, plain-language tabs, guarded workflow shortcuts, and a recommended next step for the active message or case.
- Maintains a redacted ledger of sender claims and their verification status.
- Offers a safe verification-question bank that only inserts text into the manual draft.
- Warns when a reply may be inconsistent with the assigned fictional persona.
- Blocks sending when the case budget is exhausted, its deadline has passed, or its engagement stage is Ended.
- Supports Gmail OAuth 2.0 desktop authorization with PKCE and stores refresh tokens in Windows Credential Locker.
- Guides Gmail setup inside Settings with official Google links, live connection status, and safe `credentials.json` import that retains only the Desktop Client ID.
- Synchronises recent Inbox and Sent messages read-only so cases can show both sides of a conversation.
- Generates deterministic, fully local reply suggestions from case history and recorded claim contradictions.
- Shows a unified Next Actions queue across active cases.
- Accepts manually entered email/domain indicators only with provenance, an authorization note, and a no-first-contact confirmation.
- Does not crawl the internet, harvest addresses, or initiate contact from imported indicators.
- Adds standards-based `In-Reply-To` and `References` headers so outbound replies stay in the original email thread.
- Autosaves recoverable drafts locally after edits and clears recovery data after a successful send.
- Monitors the dedicated inbox every 60 seconds while the app is open and alerts when new received messages arrive.
- Builds deterministic local conversation summaries of money requests, deadlines, organisation claims, contradictions, and unanswered verification questions.
- Tests IMAP TLS/authentication and SMTP TLS/authentication without sending a message.
- Uses a single persistent sidebar for Home, Inbox, Case, Engage, Investigate, Report, and Settings.
- Shows only task-relevant tabs within each destination and keeps the active case visible in the sidebar.
- Applies card surfaces, clearer spacing, a simplified primary command bar, and secondary overflow actions for a calmer visual hierarchy.

## Requirements

- Windows 10 1809 (build 17763) or newer; Windows 11 recommended.
- Visual Studio 2026 with **WinUI application development** workload, or .NET 10 SDK plus the WinUI templates.

## Run

1. Open `ScamBaitDesk.sln` in Visual Studio.
2. Select `x64` and run the `ScamBaitDesk` project.
3. Open **Inbox settings** and enter the IMAP and SMTP hosts, ports, username, and app password for a dedicated test account.
4. Select **Sync inbox**. Messages are fetched with `ReadOnly` folder access.

Common values: Gmail IMAP `imap.gmail.com:993` and SMTP `smtp.gmail.com:587`; Outlook IMAP `outlook.office365.com:993` and SMTP `smtp.office365.com:587`. Use an app-specific password where supported; do not reuse a personal account password.

### Gmail OAuth setup

Open **Settings → Dedicated mail account → Set up Gmail OAuth step by step**. The in-app guide links to the official pages for creating a Google Cloud project, enabling the Gmail API, configuring Google Auth Platform, adding the dedicated bait account as a test user, and creating a **Desktop app** OAuth client. Download Google's `credentials.json`, choose **Configure inbox**, select **Gmail OAuth**, and import that file; the app copies only its Client ID. Save, choose **Connect Google**, approve the dedicated account in the system browser, then choose **Test**. The app uses a loopback redirect, PKCE, and the `https://mail.google.com/` scope. It neither needs nor retains the client secret, API keys, or the downloaded JSON file.

## Safety boundary

Use only a dedicated bait account and messages you own or are authorized to handle. Use fictional persona details that do not resemble a real person. Do not send malware or tracking content, collect credentials, impersonate real people or authorities, threaten anyone, or attempt access to another system. Treat links and attachments as hostile. Outbound sending is deliberately manual, plain-text only, rate-limited, privacy-checked, confirmed twice, and logged.

## Build from the command line

```powershell
dotnet restore ScamBaitDesk.sln
dotnet build ScamBaitDesk.sln -c Release -p:Platform=x64
```

The project uses Windows App SDK 2.3.1 and MailKit 4.17.0.

## Update an existing development installation

From the repository root, run `PowerShell -ExecutionPolicy Bypass -File .\scripts\Update-ScamBaitDesk.ps1`. The script pulls `main`, builds the app, assigns a monotonically increasing loose-package version, and registers the update in place. This avoids uninstalling the app or disturbing identity-bound credentials.

The same updater can be launched from **Settings → Application updates**. **Check for updates** retrieves one published numeric build value rather than fetching or comparing repository files. **Update app now** performs that same preflight check and closes the app only when a newer build exists. A compact progress window then reports each update stage, and ScamBait Desk reopens automatically. Check and update failures are shown without silently closing the app.

The update check reads the build number from GitHub's repository API to avoid stale raw-content CDN responses, with the raw file retained as a fallback.

Updater diagnostics are written to `%LOCALAPPDATA%\ScamBaitDesk\update.log`; a failure dialog includes both the underlying command error and the log location.
