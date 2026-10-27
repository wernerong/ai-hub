# Releasing AI Hub

The public update channel is the latest **published, non-prerelease** GitHub release in `wernerong/ai-hub`. A source push alone does not update installed applications. Publish a release to deliver an update.

## Local release without Actions minutes

1. Update `<Version>` in `AIHub.csproj`, `README.txt`, the changelog and a matching `releases/vX.Y.Z.md` file. Use three-part versions and `vX.Y.Z` tags.
2. Commit the source and documentation on `main`. Keep the tree clean.
3. On Windows with the .NET 10 SDK and Git Credential Manager signed in, run:

   ```powershell
   .\Publish-Release.ps1 -OutputDirectory .\work\release-X.Y.Z -Publish
   ```

The script builds and runs all self-tests, creates an annotated tag at the exact commit, pushes `main` and the tag atomically, uploads a draft ZIP and checksum, and compares all uploaded sizes and GitHub SHA-256 digests. It publishes only after these checks pass. Omit `-Publish` to leave a verified draft. `-MakePublic` is available for the initial visibility change after draft verification.

No Actions workflow is required or triggered. Release assets go directly to GitHub Releases, so no Actions artifact or cache storage is used. Use a new output directory for each attempt; never reuse an old package staging folder.

After publication, download the ZIP anonymously, verify its digest, and confirm `/repos/wernerong/ai-hub/releases/latest` returns the intended version. Keep `release-record.json` from the output folder as provenance; it contains the source commit, tag, release URL and uploaded digests, without credentials.

## Package contract

The ZIP is flat and contains exactly:

- `AIHub.exe`
- `README.txt`
- `validation.txt`
- `Install-AIHub.ps1`
- `Uninstall-AIHub.ps1`

Name it `AIHub-vX.Y.Z-windows-x64.zip`. Upload `SHA256SUMS.txt` beside it. The updater also requires GitHub's `digest` field to be a valid SHA-256 and the executable's embedded version to match the release tag. Drafts, prereleases, version downgrades, unexpected paths and corrupt packages are rejected.

Updates are staged in `%LOCALAPPDATA%\AIHub\updates`. The detached helper waits for the original process identity to exit, checks the package again, backs up the files, and replaces the executable last. An IO failure restores replaced files. Completed staging is cleaned on the next application start; failure details remain in that attempt's `update-result.txt`. The application never terminates a provider app or elevates for updates. Use a writable per-user installation.

The first public release must be installed manually over older builds because they do not contain an updater. Subsequent stable releases use the update channel automatically unless disabled in Settings.
