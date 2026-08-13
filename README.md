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
- Quarantines attachments as inert metadata only, with no open, preview, save, or execute action.
- Provides a persistent global emergency stop that disables all outbound sending.
- Exports a lightweight redacted text summary separately from the full evidence ZIP.

## Requirements

- Windows 10 1809 (build 17763) or newer; Windows 11 recommended.
- Visual Studio 2026 with **WinUI application development** workload, or .NET 10 SDK plus the WinUI templates.

## Run

1. Open `ScamBaitDesk.sln` in Visual Studio.
2. Select `x64` and run the `ScamBaitDesk` project.
3. Open **Inbox settings** and enter the IMAP and SMTP hosts, ports, username, and app password for a dedicated test account.
4. Select **Sync inbox**. Messages are fetched with `ReadOnly` folder access.

Common values: Gmail IMAP `imap.gmail.com:993` and SMTP `smtp.gmail.com:587`; Outlook IMAP `outlook.office365.com:993` and SMTP `smtp.office365.com:587`. Use an app-specific password where supported; do not reuse a personal account password.

## Safety boundary

Use only a dedicated bait account and messages you own or are authorized to handle. Use fictional persona details that do not resemble a real person. Do not send malware or tracking content, collect credentials, impersonate real people or authorities, threaten anyone, or attempt access to another system. Treat links and attachments as hostile. Outbound sending is deliberately manual, plain-text only, rate-limited, privacy-checked, confirmed twice, and logged.

## Build from the command line

```powershell
dotnet restore ScamBaitDesk.sln
dotnet build ScamBaitDesk.sln -c Release -p:Platform=x64
```

The project uses Windows App SDK 2.3.1 and MailKit 4.17.0.
