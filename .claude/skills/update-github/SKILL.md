---
name: update-github
description: Push the latest project changes to the GitHub backup https://github.com/Botsie696/boxie (branch main) - new/changed Assets, scripts, materials, scenes, Packages, ProjectSettings only. Use when the user says "update the github", "push to github", "back up the project", "sync boxie repo".
---

# Update the GitHub backup (Botsie696/boxie)

The project root (`C:\Users\Harshil\My project (1)`) is a git repo, remote `origin` = `https://github.com/Botsie696/boxie.git`, branch `main`. It is a **backup of source content only**. `.gitignore` already excludes Library, Temp, Logs, Builds, UserSettings, .vs, ios/, XcodeIOSBoxicleHB/, *.zip, *.csproj/*.sln, LeftRightDebugRuns, ProfilerCaptures. Files > 50 MB go through **Git LFS** (listed per-file in `.gitattributes`).

## Steps

1. `git status --short | head -50` and count - look at what changed. Only Assets/, Packages/, ProjectSettings/, CLAUDE.md, AGENTS.md, .claude/, notes .txt files should appear. If a generated/recreatable folder shows up, add it to `.gitignore` instead of committing it.
2. **New files > 50 MB** (GitHub rejects > 100 MB without LFS): find them with
   `Get-ChildItem Assets,Packages,ProjectSettings -Recurse -File | ? Length -gt 50MB`
   and for any not already in `.gitattributes`, run `git lfs track --filename "<relative/path/with/forward/slashes>"` BEFORE `git add`. (A file already committed as a normal blob needs `git lfs migrate import --include="<path>"` - ask the user first, it rewrites history.)
3. `git add -A`, then commit with a short message describing the changes (e.g. "Update checkpoint 5 look, new corner props"). End the message with the Co-Authored-By line from the session's attribution reminder.
   If nothing to commit, say so and stop.
4. `git push origin main` (run in background for big pushes; the first one is ~9 GB). Never force-push unless the user explicitly says so.
5. Report: commit hash, number of files changed, any LFS files uploaded, and push result. If the push fails with an LFS quota error (free tier = 10 GB storage), say so plainly.

## Notes
- Auth uses the user's saved git credentials (no `gh` CLI installed). If prompted/denied, tell the user rather than guessing.
- Don't commit Unity's `.csproj`/`.sln`; Unity regenerates them.
- Keep `.meta` files - they are required.
