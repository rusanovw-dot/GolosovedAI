# 🎙️ Golosoved AI

**Local voice recorder with speech recognition and AI summarization**  
Fully offline, confidential, no data sent to the cloud.

## 📥 Download

[![Download](https://img.shields.io/badge/Download-GolosovedAI_Setup_v1.0.1.exe-brightgreen)](https://github.com/rusanovw-dot/GolosovedAI/releases/download/v1.0.1/GolosovedAI_Setup_v1.0.1.exe)

> **Version 1.0.1** – full localization (Russian/English), bug fixes, single-instance protection.

## Screenshot

<img width="1083" height="823" alt="Screenshot" src="https://github.com/user-attachments/assets/40cd1c7d-96e7-4a5e-9e7c-9affd64dc4a3" />

---

## ✨ Features

- 📂 **File support:** Audio, Video, PDF, TXT (drag‑and‑drop)
- 🎙️ **Recording:** Microphone and system sound (Zoom, Teams, lectures)
- 🗣️ **Speech recognition:** Whisper (local, Base–Large models)
- 🧠 **AI summarization:** Qwen (local, 4 templates)
- 💾 **Export:** Full text or AI Summary to TXT, MD, PDF, SRT, JSON, VTT
- 🌙 **Dark / Light theme** (toggle, saved between sessions)
- 🔒 **Fully offline** – your data never leaves your computer
- 🚫 **Single instance** – prevents multiple windows

## 👥 Who can benefit

- **Students** – record lectures, get transcripts and concise summaries.
- **Journalists & bloggers** – transcribe interviews without manual typing.
- **Managers** – capture meetings with key conclusions.
- **Researchers** – turn field recordings into structured text.
- **People with hearing impairments** – get subtitles for conversations.
- **Privacy‑conscious users** – no cloud upload, full control.

## 📖 Getting started

1. Install **.NET 8 Desktop Runtime** (if the program doesn't start, download from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.0-windows-x64-installer)).
2. Download the recognition model (Whisper) in the **Recognition Model Settings** section.
3. Download the AI model (Qwen) in the **AI Summarization** section.
4. Select a file or click **Record**.
5. Click **Generate AI Summary** to get the final report.

## 🎥 Video overview

[![Video overview](https://img.youtube.com/vi/d2DKVnj7P40/maxresdefault.jpg)](https://youtu.be/d2DKVnj7P40)

## 🖥️ Interface overview

**Left panel:**
- "Select and Recognize" – open a file.
- "Save original audio" – save original audio during recognition.
- "System sound / Microphone" – select recording source.
- "Record" / "Stop" – control recording.
- "Save audio on record" – save recorded audio as a separate file.
- Whisper model and language selection.
- "Download" button for models.
- Folder field and "Browse" – specify audio save folder.

**Right panel (tabs):**
- "📄 Full Text" – recognized text.
- "🧠 AI Summary" – generated summary.
- "❓ Help" – instructions.
- "☀️/🌙" button – toggle theme (saved between sessions).

**Bottom panel:**
- "🧠 Generate AI Summary" – generate summary.
- Template selection (Meeting, Interview, Lecture, General).
- Format selection (TXT, MD, SRT, JSON, VTT, PDF).
- "💾 Save as..." – save current result.

**Status bar:** progress bar, Cancel button, status text.

---

## 💾 Where models and settings are stored

**Models and settings**  
Downloaded models (Whisper and Qwen) and `settings.json` are saved in the user folder:
`C:\Users\YourUsername\AppData\Roaming\GolosovedAI`
When uninstalling via the installer, this folder is automatically removed (for the current user). If you want to keep models or settings after uninstallation, copy them elsewhere beforehand.

**Audio saving**  
- For files (if "Save original audio" is enabled) – audio is saved next to the original file.
- For recording (if "Save audio on record" is enabled) – saved to the `Records` folder inside the program folder (unless a custom folder is specified).

You can specify your own folder in the "Save audio" field at the bottom of the left panel.

## 💻 System requirements

- Windows 10/11
- .NET 8 Desktop Runtime
- 4 GB RAM (8 GB recommended)
- ~4 GB free disk space for models

## 📄 License

MIT. See [LICENSE](LICENSE.txt) for details.

## 💝 Support the project

[pay.cloudtips.ru/p/31f0f59a](https://pay.cloudtips.ru/p/31f0f59a)
