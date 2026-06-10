#!/usr/bin/env bash
set -euo pipefail

repo="${GITHUB_REPOSITORY:-FinnGrndl/novr}"
server="${GITHUB_SERVER_URL:-https://github.com}"
repo_url="${server}/${repo}"
sha="${GITHUB_SHA:-$(git rev-parse HEAD 2>/dev/null || echo local)}"
short_sha="${sha:0:7}"
version="$(perl -0ne 'print "$1\n" and exit if /\[BepInPlugin\(\s*"[^"]+",\s*"NOVR",\s*"([0-9]+\.[0-9]+\.[0-9]+)"/s' NOVR/NOVRPlugin.cs || true)"
version="${version:-unknown}"

mkdir -p site

cat > site/.nojekyll <<'EOF'
EOF

cat > site/styles.css <<'EOF'
:root {
  color-scheme: dark;
  --bg: #101417;
  --panel: #171d21;
  --text: #eef4f2;
  --muted: #adbbb6;
  --line: #30413b;
  --accent: #6fd0a5;
  --accent-2: #f0c66e;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  line-height: 1.55;
}

main {
  width: min(1080px, calc(100% - 32px));
  margin: 0 auto;
  padding: 48px 0 72px;
}

header {
  border-bottom: 1px solid var(--line);
  margin-bottom: 32px;
  padding-bottom: 24px;
}

h1, h2, h3 {
  line-height: 1.1;
}

h1 {
  margin: 0 0 12px;
  font-size: clamp(2.2rem, 5vw, 4.6rem);
}

h2 {
  margin-top: 40px;
  font-size: 1.55rem;
}

a {
  color: var(--accent);
}

.lede {
  color: var(--muted);
  max-width: 780px;
  font-size: 1.1rem;
}

.grid {
  display: grid;
  gap: 16px;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
}

.card {
  background: var(--panel);
  border: 1px solid var(--line);
  border-radius: 8px;
  padding: 18px;
}

.card h3 {
  margin-top: 0;
}

.flow {
  display: grid;
  gap: 10px;
}

.step {
  border-left: 3px solid var(--accent);
  padding: 10px 0 10px 14px;
}

code, pre {
  font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
}

pre {
  overflow-x: auto;
  background: #0b0f11;
  border: 1px solid var(--line);
  border-radius: 8px;
  padding: 16px;
}

.meta {
  color: var(--muted);
  font-size: 0.95rem;
}
EOF

cat > site/index.html <<EOF
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>NOVR Architecture</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <main>
    <header>
      <p class="meta">Generated from <a href="${repo_url}/tree/${sha}">${repo}@${short_sha}</a></p>
      <h1>NOVR</h1>
      <p class="lede">A generated map of how the Nuclear Option VR mod is packaged, installed, and loaded at runtime. Current source version: <strong>${version}</strong>.</p>
    </header>

    <section class="grid">
      <article class="card">
        <h3>Release Package</h3>
        <p><code>NOVR.Build</code> stages a BepInEx layout and zips it as <code>dist/NOVR.zip</code>.</p>
        <p><a href="${repo_url}/blob/${sha}/NOVR.Build/NOVR.Build.csproj">NOVR.Build.csproj</a></p>
      </article>
      <article class="card">
        <h3>Installer</h3>
        <p>The Avalonia installer lists GitHub releases from <code>FinnGrndl/novr</code> and downloads the selected <code>NOVR.zip</code>.</p>
        <p><a href="${repo_url}/blob/${sha}/NOVR.Installer/Services/GitHubReleaseClient.cs">GitHubReleaseClient.cs</a></p>
      </article>
      <article class="card">
        <h3>Runtime</h3>
        <p>BepInEx loads <code>plugins/NOVR/NOVR.dll</code> during the game and <code>patchers/NOVR/NOVR.Patcher.dll</code> before it.</p>
        <p><a href="${repo_url}/blob/${sha}/NOVR/NOVRPlugin.cs">NOVRPlugin.cs</a></p>
      </article>
    </section>

    <h2>Install Flow</h2>
    <div class="flow">
      <div class="step">The user runs <code>NOVR.Installer-Win.exe</code> or <code>NOVR.Installer-Linux</code>.</div>
      <div class="step">The installer loads release metadata from the GitHub Releases API.</div>
      <div class="step">The selected release's exact <code>NOVR.zip</code> asset is downloaded.</div>
      <div class="step">BepInEx 5 is downloaded and installed only when missing.</div>
      <div class="step"><code>plugins/NOVR</code> and <code>patchers/NOVR</code> are copied into the game's <code>BepInEx</code> folder.</div>
    </div>

    <h2>Release Automation</h2>
    <pre><code>git push origin release
dotnet build NOVR.Build/NOVR.Build.csproj -c Release
dotnet build NOVR.Installer/NOVR.Installer.csproj -c Release
gh release create/update v${version}</code></pre>

    <p class="meta">See <a href="release.html">release automation</a> and <a href="runtime.html">runtime layout</a> for the expanded generated notes.</p>
  </main>
</body>
</html>
EOF

cat > site/release.html <<EOF
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>NOVR Release Automation</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <main>
    <header>
      <p class="meta"><a href="index.html">NOVR architecture</a></p>
      <h1>Release Automation</h1>
      <p class="lede">The <code>release</code> branch is the publishing lane. Every push builds the mod zip, installers, and checksums, then creates or updates the matching GitHub release.</p>
    </header>

    <h2>Artifacts</h2>
    <div class="grid">
      <article class="card"><h3>NOVR.zip</h3><p>Contains the BepInEx <code>plugins</code> and <code>patchers</code> folders.</p></article>
      <article class="card"><h3>NOVR.Installer-Win.exe</h3><p>Windows self-extracting wrapper around the Avalonia installer.</p></article>
      <article class="card"><h3>NOVR.Installer-Linux</h3><p>Linux shell self-extractor around the Avalonia installer.</p></article>
      <article class="card"><h3>checksums.sha256</h3><p>SHA-256 hashes for release verification.</p></article>
    </div>

    <h2>Source Links</h2>
    <ul>
      <li><a href="${repo_url}/blob/${sha}/.github/workflows/release.yml">Release workflow</a></li>
      <li><a href="${repo_url}/blob/${sha}/NOVR.Installer/NOVR.Installer.csproj">Installer packaging target</a></li>
      <li><a href="${repo_url}/blob/${sha}/NOVR.Installer/linux-sfx-stub.sh">Linux SFX stub</a></li>
      <li><a href="${repo_url}/blob/${sha}/NOVR.Installer.Sfx/Program.cs">Windows SFX stub</a></li>
    </ul>
  </main>
</body>
</html>
EOF

cat > site/runtime.html <<EOF
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>NOVR Runtime Layout</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <main>
    <header>
      <p class="meta"><a href="index.html">NOVR architecture</a></p>
      <h1>Runtime Layout</h1>
      <p class="lede">The release zip mirrors the BepInEx folders that are copied into Nuclear Option.</p>
    </header>

    <pre><code>BepInEx/
  plugins/
    NOVR/
      NOVR.dll
      Assets/
  patchers/
    NOVR/
      NOVR.Patcher.dll
      classdata.tpk
      CopyToGame/
        Data/
        Plugins/</code></pre>

    <h2>Responsibilities</h2>
    <div class="grid">
      <article class="card">
        <h3>Plugin</h3>
        <p><code>NOVR.dll</code> runs while the game is active, applies Harmony patches, and creates the VR camera/UI systems.</p>
      </article>
      <article class="card">
        <h3>Patcher</h3>
        <p><code>NOVR.Patcher.dll</code> runs before normal plugins, adjusts XR assemblies, and copies Unity XR support files into the game data folder.</p>
      </article>
    </div>

    <h2>Source Links</h2>
    <ul>
      <li><a href="${repo_url}/blob/${sha}/NOVR/NOVR.csproj">Plugin project</a></li>
      <li><a href="${repo_url}/blob/${sha}/NOVR.Patcher/NOVR.Patcher.csproj">Patcher project</a></li>
      <li><a href="${repo_url}/blob/${sha}/NOVR.Patcher/UuvrPatcher.cs">Patcher entry point</a></li>
    </ul>
  </main>
</body>
</html>
EOF
