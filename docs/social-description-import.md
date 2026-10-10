# Description-only social and text import

Backend branch: `feature/social-description-import`, based on `WebsiteScraping`.
Use the matching frontend branch, based on the existing `WebsiteReipeEctraction`
branch. `WebsiteRecipeExtraction` was not present on the remote. Neither base
branch is modified by this feature. No new database migration is needed.

The existing import endpoint accepts recipe website links, public YouTube videos
and Shorts, TikTok videos, Instagram Reels, or pasted text. Social import reads
only yt-dlp's full `description` field. It never substitutes a title, downloads
media, reads subtitles/comments, or follows caption/bio links. Photo import stays
unimplemented; existing website pictures and manual photo uploads are unchanged.

## Dependencies and configuration

Tested locally: .NET SDK **10.0.401**, YoutubeDLSharp **1.2.0**, Python **3.12.14**,
yt-dlp **2026.08.19**, bundled yt-dlp-ejs **0.8.0**, Node **24.19.0**.
YoutubeDLSharp supplies the actual `OptionSet` and `VideoData` APIs. Its unbounded
process reader is replaced with a bounded, cancellable process runner.

Install .NET 10 SDK, Python 3.10+ and Node 24 (also used by Angular). The default
Python yt-dlp distribution and its bundled EJS scripts use Node for YouTube's JS
challenges. **FFmpeg is not required**. Executables/dependencies are installed at
setup/image-build time, never downloaded or updated by an import request.

From the backend checkout, Bash:

```bash
git fetch origin WebsiteScraping feature/social-description-import
git switch feature/social-description-import
dotnet restore
dotnet tool restore
python3 -m venv .tools/yt-dlp
.tools/yt-dlp/bin/python -m pip install -r tools/requirements-yt-dlp.txt
.tools/yt-dlp/bin/yt-dlp --version
export SocialImport__YtDlpPath="$PWD/.tools/yt-dlp/bin/yt-dlp"
export SocialImport__NodePath="$(command -v node)"
export RecipeText__Model=gemini-flash-latest
read -r -s -p 'Gemini API key: ' RecipeText__ApiKey; printf '\n'
export RecipeText__ApiKey
dotnet build
dotnet run --project backend/RecipeAtlas.Api --launch-profile http
```

On PowerShell 7, replace the Python/configuration lines with:

```powershell
py -3 -m venv .tools/yt-dlp
& .tools/yt-dlp/Scripts/python.exe -m pip install -r tools/requirements-yt-dlp.txt
& .tools/yt-dlp/Scripts/yt-dlp.exe --version
$env:SocialImport__YtDlpPath = Join-Path $PWD '.tools/yt-dlp/Scripts/yt-dlp.exe'
$env:SocialImport__NodePath = (Get-Command node).Source
$env:RecipeText__Model = 'gemini-flash-latest'
$env:RecipeText__ApiKey = Read-Host 'Gemini API key' -MaskInput
```

Keep your existing owner-password configuration and data directory. For a fresh
checkout, follow the owner setup in [README](../README.md). API health is
`http://localhost:5080/health`; startup applies existing migrations. Do not delete
the database. Neither key nor owner credentials belong in Git, Angular, request
logs, or example files. Environment variables above last only for that shell;
production should supply the key with its secret-management mechanism.

`RecipeText:ApiKey` and `RecipeText:Model` can alternatively use backend .NET
development user secrets. `SocialImport:YtDlpPath` / `SocialImport:NodePath` can
point to existing installations. The default executable names are `yt-dlp` and
`node`, found through PATH. Missing AI configuration produces an actionable error
only for social/text extraction; recipe website imports keep working.

Gemini uses the officially documented REST endpoint
`POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`,
the `x-goog-api-key` header, `responseMimeType: application/json` and
`responseJsonSchema`. The default **gemini-flash-latest** is a currently documented
structured-output alias, not a pinned model revision. Choose/pin another available
structured-output model with `RecipeText:Model` if needed; verify availability
with Google's models list/get API in your project. No live model request was run
in this environment because no task credentials were configured.

## Frontend and trying an import

From the matching frontend checkout:

```bash
git fetch origin WebsiteReipeEctraction feature/social-description-import
git switch feature/social-description-import
npm ci
npm test
npm run build
npm start
```

Open `http://localhost:4200`, sign in, choose **Import recipe → Link**, and paste
a public video/Short/Reel URL. On caption retrieval failure, choose **Paste
description instead**: the original social URL remains the draft's source.
**Import recipe → Text** accepts standalone recipe text without a URL. Paste full
ingredients/instructions in the source language. Missing servings/instructions
remain empty and block saving until corrected. All AI ingredient rows require
review confirmation. Source text is displayed as text, never injected HTML.

Authenticated requests use the existing cookie and `X-RecipeAtlas: 1` protection:

```bash
curl -b cookies.txt -H 'X-RecipeAtlas: 1' -H 'Content-Type: application/json' \
  --data '{"url":"https://youtu.be/BaW_jenozKc"}' \
  http://localhost:5080/api/recipes/import

curl -b cookies.txt -H 'X-RecipeAtlas: 1' -H 'Content-Type: application/json' \
  --data '{"text":"Eierpfanne\n2 Portionen\n3-4 Eier\n1/2 cup Milch\nSalz nach Geschmack\nAlles mischen.\nIn der Pfanne garen.","url":"https://youtu.be/BaW_jenozKc"}' \
  http://localhost:5080/api/recipes/import
```

Omit `url` in the second request for standalone text. In PowerShell, use
`Invoke-RestMethod` with an existing authenticated `WebRequestSession` and
`ConvertTo-Json`, or `curl.exe --data-binary @request.json` with UTF-8 JSON.
Do not commit local request files containing private input.

Only explicit source facts are requested. Output fields must quote the supplied
text (whitespace normalized for checking); explicit servings must match the
original yield. Lengths, types, collection sizes and unknown properties are
validated. This catches invented wording/numbers but cannot establish semantic
correctness or guarantee that the model detects all distinct recipes. Review
ingredients and instructions against the source. Several recipes produce an
actionable error asking for just one; no usable recipe yields `no_recipe`, and
comments/bio references explain the description-only limit. Ingredients without
steps return a partial editable draft. Nothing is automatically saved.

Quantity normalization remains in the existing ingredient parser, after AI
extraction. Fractions/string ranges, notes, piece defaults, review flags and
metric conversions follow [the existing quantity policy](quantity-strings.md):
lb/weight oz → g; cups/fluid oz → ml, with unspecified volumes assuming US
customary and requiring review. No density-free cup-to-gram conversion.

## Limits, cancellation and platform restrictions

- HTTPS/443 only, no embedded credentials. Accepted social hosts/paths are
  allowlisted; YouTube Shorts/youtu.be normalize to the video provider. TikTok
  vm/vt/t share links and Instagram `/share/reel/` resolve with at most four
  redirects to the same supported platform. No profiles or playlists.
- Public-host DNS checks precede retrieval. Short-link HTTP connections use the
  existing handler pinned to checked public IPv4 addresses, with automatic
  redirects/cookies/proxies disabled. Each redirect is validated before use.
- yt-dlp is a separate process: **HttpClient's socket protections do not cover
  its subsequent extractor requests, redirects, DNS lookups or Node subprocess**.
  Inputs and extractors are restricted to these platforms, but this is not an
  OS-level networking sandbox. Deploy with outbound restrictions that deny
  private/link-local destinations (IPv4 and IPv6); DNS rebinding or future
  extractor changes remain a limitation without that network isolation. The
  process inherits the deployment's normal networking/proxy configuration; this
  feature adds no proxy rotation or access bypass.
- No shell execution, browser login/cookie collection, remote JS components,
  plugins, update checks, or comments/subtitle/media writes. Owner/model credential
  environment variables are removed from the child environment. Raw captions,
  model responses and yt-dlp stderr are not logged or returned in error details.
- Two metadata processes and two model calls maximum, no job queue. Process
  deadline 30s; Gemini 45s; total social 80s/text 50s; browser 90s. Closing the
  dialog cancels its request; cancellation kills the process tree. Metadata output
  is capped at 2 MiB, stderr 64 KiB, input text 20,000 characters and structured
  recipe JSON 64 KiB. Existing authenticated import rate limit is unchanged.
- Private/deleted/age-restricted posts, bot checks, rate limits, sign-in gates,
  unavailable descriptions and platform API changes can prevent retrieval.
  Failures stay in the dialog with an explanation/paste fallback. No promise of
  TikTok/Instagram end-to-end support is based on mocked tests.

## Tests and live verification

```bash
# backend, no network or paid model calls in the normal suite
dotnet build
dotnet run --no-build --project tests/RecipeAtlas.Tests
python3 tests/integration.py
python3 tests/check_shared_contracts.py ../RecipeAtlasFrontend
# frontend
npm test
npm run build
# separate metadata-only live smoke; use any public test links
dotnet run --no-build --project tests/RecipeAtlas.Tests -- --live-social \
  'https://www.youtube.com/watch?v=BaW_jenozKc' \
  'https://www.tiktok.com/@leenabhushan/video/6748451240264420610' \
  'https://www.instagram.com/reel/Chunk8-jurw/'
```

The live command prints outcome/description length, never caption contents, and
makes no AI call. A fixture URL may disappear; substitute a current public post.
Verification in this environment: backend build with zero warnings/errors,
**567** deterministic regression checks, **62** isolated HTTP checks, matching
shared quantity/unit fixtures, frontend production build and **134** tests.
Additional Chromium runs passed **35** social/text handoff checks at mobile and
desktop sizes and **51** existing UI checks at 320/390/600/1280px with mocked APIs.
The existing migration SQL exported successfully; SQLite does not support EF's
optional `--idempotent` script mode. Migration/data-preservation tests passed.
The backend smoke attempts were blocked by DNS/network restrictions. Separate
real yt-dlp metadata-only invocations and proxy checks returned CONNECT HTTP 403
for all three platform domains. TikTok also reported no optional impersonation
target; this setup does not add impersonation dependencies or access workarounds.
No social platform or
Gemini was verified end to end live. Test a real caption and the pasted-text path
locally after configuring your key. Standard tests mock retrieval/model responses
and cover English/German, partial drafts, invalid/invented values, redirects,
timeouts, cancellation, process limits and existing website imports.

## Container deployment

The Dockerfile installs Python, pinned yt-dlp/EJS and Node at build time, keeps
the existing non-root API and persistent data volume, and adds no service or
FFmpeg. Compose passes `RecipeText__ApiKey` / `RecipeText__Model` from the host
environment (empty key keeps website imports available):

```bash
# export the model/key securely as above; retain existing owner/domain settings
docker compose up -d --build
```

The image build was attempted here but the registry's Microsoft runtime blob
download returned HTTP 403. Container execution remains unverified; build/test
the image locally. Deploy both feature branches together to enable the text UI;
the existing URL contract stays compatible. No additional schema migration.

## API references used

- [YoutubeDLSharp 1.2.0 package](https://www.nuget.org/packages/YoutubeDLSharp/1.2.0)
  and [official source/API](https://github.com/Bluegrams/YoutubeDLSharp).
- [yt-dlp 2026.08.19 release](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19)
  and [dependencies/EJS](https://github.com/yt-dlp/yt-dlp#dependencies).
- [Official Google Gen AI SDK structured-output examples](https://github.com/googleapis/python-genai#json-response-schema),
  [official REST JSON cookbook](https://github.com/google-gemini/cookbook/blob/main/quickstarts/rest/JSON_mode_REST.ipynb),
  [REST service definitions](https://github.com/googleapis/googleapis/blob/master/google/ai/generativelanguage/v1beta/generative_service.proto).
  The Google documentation site was blocked here; its official SDK mappings and
  REST definitions were checked directly instead of guessing an SDK method.
