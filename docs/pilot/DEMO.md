# Fictional demo and media runbook

The committed product tour is a silent, captioned, 1280x720 H.264 MP4. Its measured duration is **57.12 seconds at 25 fps**. Chapters hold for roughly 5-9 seconds, navigation happens immediately, and there is no idle cursor time. This intentionally keeps the video faster than the live two-minute walkthrough. The committed file was converted and inspected with FFmpeg 8.1.2; FFmpeg is optional and used only to regenerate media.

## Live walkthrough (under two minutes)

| Time | Show | Proof point |
|---:|---|---|
| 0:00-0:12 | Sign in to fictional Alpha Plumbing | Tenant-owned staff session |
| 0:12-0:28 | Open urgent missed caller | Attention-first inbox and ownership |
| 0:28-0:48 | Read delivered recovery SMS and reply | One ordered timeline |
| 0:48-1:05 | Show low-confidence analysis | AI advises; staff decides |
| 1:05-1:22 | Show deterministic next actions | Booking/handoff remains policy-controlled |
| 1:22-1:42 | Open pilot report and CSV | Operational evidence, not revenue |
| 1:42-1:55 | Run or show demo proof result | Duplicate and STOP guarantees |
| 1:55-2:00 | State limitation | Fictional data and fake SMS provider |

## Reproduce the proof

Build the solution, make Docker available to Testcontainers, then run:

```powershell
dotnet build LeadRecovery.sln --configuration Release
./eng/Invoke-DemoProof.ps1 -Configuration Release
```

The script runs `DuplicateCallbackHasNoDuplicateEffect` and `SignedStopIsIdempotentCancelsPendingActionAndBlocksFutureSend`. A passing result is the duplicate/opt-out evidence; a caption is not a substitute for the test.

## Regenerate screenshots and video

Use a new disposable PostgreSQL database, apply migrations, build the web app, and set all `E2E_*` variables described by `tests/LeadRecovery.E2E/playwright.config.ts` to fictional values. Then:

```powershell
pnpm frontend:build
pnpm demo:capture
ffmpeg -i tests/LeadRecovery.E2E/demo-results/demo-video-capture-the-fictional-pilot-walkthrough-chromium/video.webm -c:v libx264 -crf 23 -pix_fmt yuv420p -movflags +faststart -an docs/pilot/assets/leadrecovery-demo.mp4
ffprobe -v error -show_entries format=duration,size -show_entries stream=codec_name,width,height,avg_frame_rate -of json docs/pilot/assets/leadrecovery-demo.mp4
```

The capture test is excluded from normal Playwright acceptance runs because deliberate chapter holds would slow CI. Review all four PNGs and the MP4 before publishing. Never run demo seeding or capture against a database containing real customer records.
