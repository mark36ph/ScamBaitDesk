# ScamBait Desk

ScamBait Desk is a defensive WinUI 3 workspace for reviewing a **dedicated test inbox**, assessing suspicious messages, drafting safe replies, and preserving case notes. It never sends mail, opens links, downloads attachments, or embeds remote content.

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

## Requirements

- Windows 10 1809 (build 17763) or newer; Windows 11 recommended.
- Visual Studio 2026 with **WinUI application development** workload, or .NET 10 SDK plus the WinUI templates.

## Run

1. Open `ScamBaitDesk.sln` in Visual Studio.
2. Select `x64` and run the `ScamBaitDesk` project.
3. Open **Inbox settings** and enter the IMAP host, port, username, and app password for a dedicated test account.
4. Select **Sync inbox**. Messages are fetched with `ReadOnly` folder access.

Common IMAP values: Gmail `imap.gmail.com:993`, Outlook `outlook.office365.com:993`. Use an app-specific password where supported; do not reuse a personal account password.

## Safety boundary

Use only accounts and messages you own or are authorized to handle. Do not send malware, collect credentials, impersonate real people, threaten anyone, or attempt access to another system. Treat links and attachments as hostile. The app deliberately has no SMTP/send path.

## Build from the command line

```powershell
dotnet restore ScamBaitDesk.sln
dotnet build ScamBaitDesk.sln -c Release -p:Platform=x64
```

The project uses Windows App SDK 2.3.1 and MailKit 4.17.0.
