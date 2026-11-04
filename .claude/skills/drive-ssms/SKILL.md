---
name: drive-ssms
description: Install the built VSIX into SSMS 22, start SSMS connected to the Docker test instance and drive its query editor (paste text, type, press keys, read the completion list and the editor text) to verify completion behavior yourself. Use when a change to the SSMS layer or to completion must be verified in the real SSMS.
---

# Driving SSMS 22 to verify the extension

Everything runs on the user's Windows desktop, in the interactive session. Do not touch the user's own servers: connect only to the Docker test instance (`test-database` skill, config in `%LOCALAPPDATA%\SqlPrompter\testdb.json`; the password is never printed or stored in the repository).

## Steps

1. Build, with the version in `source.extension.vsixmanifest` bumped:
   `dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release -warnaserror`.
2. Close SSMS if it runs: `Alt+F4` with `drive.ps1 -Keys "%{F4}"`, then invoke the "Don't Save" button through UI Automation. Killing the process shows a recovery dialog on the next start ("Do Not Recover").
3. Install: `VSIXInstaller.exe /quiet /shutdownprocesses <path to .vsix>` from `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\`. Check the version in the installed `extension.vsixmanifest` under `%LOCALAPPDATA%\Microsoft\SSMS\22.0_*\Extensions`.
4. Create the test databases (`verify-schema.sql`) with the `test-database` skill.
5. `start-ssms.ps1` starts SSMS with `-S -U -d master -C -N Optional`, dismisses the confirmation dialog, sets the password through `ValuePattern` and connects. If it does not report `connecting` (the confirmation dialog sometimes is a Pane without an Invoke pattern), click "Yes" with the mouse in physical pixels (SSMS must be topmost, `drive.ps1` makes it so).
6. `drive.ps1 -ClearAll -Paste "<sql>" -Keys "<keys>"` pastes text through the clipboard (no completion is triggered), then sends `SendKeys` text; parts separated by `@@` are sent 2.5 s apart. `-NoFocus` keeps the open completion list alive (focusing the editor closes it). It prints the editor text and the completion list (`name | suffix`). Typing a letter or `.` opens the list; Tab commits.
7. Completion for a database used for the first time is empty on the first try (metadata loads in the background); repeat after a few seconds.
8. When done, close SSMS as in step 2 and drop the `SqlPrompterTest_*` databases.

The scripts need PowerShell 7 and the user's desktop in front; VS Code may take the foreground, so SSMS is made topmost. Take a screenshot (`-Shot file.png`, then Read it) when UI Automation does not show what you expect.
