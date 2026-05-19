# Privacy Policy — MeetingNotes

**Effective date:** May 18, 2026
**Last updated:** May 18, 2026

## Overview

MeetingNotes is a privacy-first desktop application. All recording, transcription, and AI summarization happen entirely on your device. Your meeting data never leaves your computer unless you explicitly choose to share it.

## Data collected and stored

MeetingNotes stores the following data **locally on your device only**, in `%LOCALAPPDATA%\MeetingNotes\` by default:

| Data | Where | Purpose |
|------|-------|---------|
| Audio recordings (MP3 or WAV) | Configurable folder (default: `Data\Audio`) | Preserves the original meeting audio |
| Transcripts and AI summaries | Local SQLite database | Lets you search and review past meetings |
| Meeting titles, notes, and folder names | Local database | Organizes your meetings |
| App settings | `settings.json` in `%LOCALAPPDATA%\MeetingNotes\` | Remembers your preferences |
| Optional diagnostic logs | Configurable folder (default: `Data\Logs`) | Troubleshooting; disabled by default |

## Data NOT collected

MeetingNotes does **not**:

- Send audio, transcripts, summaries, or any meeting content to any server operated by the developer.
- Collect analytics, telemetry, crash reports, or usage statistics.
- Require an account, login, or email address.
- Store any data in the cloud.
- Share any data with third parties.

## Network access

MeetingNotes makes outbound network connections for the following limited purposes only:

1. **Whisper speech-to-text model download** — On first run (or when the model file is missing), the app downloads a Whisper GGML model file from [huggingface.co](https://huggingface.co) to your local cache (`~/.cache/whisper.net/`). After the initial download, transcription runs fully offline.

2. **Local AI inference** — If you use the LLM summarization feature, the app sends transcript text to a locally running AI server on your own machine (Ollama at `localhost:11434` or LM Studio at `localhost:1234`). This traffic never leaves your device.

No other network communication occurs.

## Microphone and audio capture

MeetingNotes captures audio from your microphone and system audio output (loopback) while a recording is in progress. Audio capture begins only when you explicitly start a recording and stops when you stop it. Captured audio is saved to the folder you configure (default: `%LOCALAPPDATA%\MeetingNotes\Data\Audio`). You can delete recordings at any time from within the app or directly from disk.

## Windows registry

MeetingNotes may write a single registry entry to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` if you enable the "Launch at startup" option. This entry is removed when you disable the option. No other registry keys are written.

## Children's privacy

MeetingNotes is not directed at children under 13 and does not knowingly collect personal information from children.

## Changes to this policy

If this policy changes materially, the "Last updated" date at the top will be updated. Continued use of the app after a policy update constitutes acceptance of the revised policy.

## Contact

If you have questions about this privacy policy, please open an issue at:
**https://github.com/dfwdino/MeetingNotes/issues**
