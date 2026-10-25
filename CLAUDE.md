# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## ภาษา / Language

- ตอบกลับผู้ใช้เป็น **ภาษาไทย** เสมอ (Always respond in Thai).
- คำศัพท์ทางเทคนิค (เช่น completion source, commit manager, MEF, VSIX, reflection, schema cache, tool window)
  ให้คงคำภาษาอังกฤษไว้ได้ ไม่ต้องแปล
- ใน repo นี้คอมเมนต์ในโค้ดเป็น **ภาษาอังกฤษ** — เขียนเพิ่มเป็นภาษาอังกฤษให้กลมกลืนกับของเดิม
  ส่วนข้อความที่ผู้ใช้เห็น (README, ข้อความในตัวติดตั้ง `SqlAssist.Setup`) เป็นภาษาไทย

## What this is

SqlAssist: an SSMS 22 extension (VSIX) that replaces the parts of dbForge SQL Complete / dbForge Search the user
actually uses — `*` expansion on Tab, auto table aliases, FK-based JOIN suggestions, fuzzy completion, snippets, and
a dockable object-search tool window. `README.md` (Thai) is the user manual; `CONTEXT.md` is the domain glossary
(Active Connection, Schema, Search Index, Search Item, Definition, Table Script, Match…) — use those terms.

## Commands

```powershell
dotnet test tests\SqlAssist.Core.Tests                                   # all tests (xUnit, Core only)
dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~SearchMatcherTests"   # one class
dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~CompletionTests.<Method>"  # one test
dotnet build src\SqlAssist.Vsix -c Release      # -> src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix
.\build-setup.ps1                               # icons -> tests -> Setup exe -> dist\SqlAssistSetup-<version>.exe
.\install.ps1 / .\uninstall.ps1                 # install the built .vsix into SSMS 22 via SSMS's VSIXInstaller
.\clear-ssms-cache.ps1                          # when SqlAssist menus don't appear after install
```

- **Version lives only in `Directory.Build.props`.** It flows to assemblies, the .vsix manifest (the
  `StampVsixVersion` target in `SqlAssist.Vsix.csproj` overwrites the placeholder in the merged manifest and fails
  the build on mismatch) and the setup exe name. Bump it for every release.
- **Every change gets a `CHANGELOG.md` entry** (Thai, user-facing: เพิ่ม / เปลี่ยน / แก้ไข) under `## [Unreleased]`;
  when bumping the version, rename that heading to `## [x.y.z] - YYYY-MM-DD` and open a new `[Unreleased]`.
  `publish-release.ps1` refuses to publish without the version's section and uses it (plus any unreleased versions
  since the previous release) as the text on the GitHub Releases page; `-UpdateNotes [-ForVersion x.y.z]` rewrites
  an existing release's text.
- Building needs no SSMS installed. Debugging: SSMS 22 has no experimental instance — install the extension first,
  then F5 (start program is `Ssms.exe /log`) or attach.
- Runtime log: `%LOCALAPPDATA%\SqlAssist\SqlAssist.log` (Tools › SqlAssist › Options… › the Open Log button, which opens it in the default `.log` editor). User files:
  `%APPDATA%\SqlAssist\snippets.xml`.

## Architecture

Three projects (`SqlAssist.sln`) plus tests:

- **`src/SqlAssist.Core`** (netstandard2.0) — pure logic, no VS/SSMS references: `Sql/` (tokenizer, context
  analyzer that figures out which tables/aliases are in scope and what kind of completion the caret wants),
  `Completion/` (fuzzy matcher, alias generator, JOIN suggester, star expander), `Snippets/`, `Results/` (scripts from result-grid cells,
  the UPDATE script builder, a cell cache), `Schema/` (schema model,
  SQL type formatting), `Search/` (search matcher with match-case/whole-word/wildcards, `CREATE TABLE` preview
  builder), `Clipboard/` (Excel-style paste parser, strict typed cell parsing, IN / VALUES / #Temp builder),
  `Formatting/` (the SQL formatter), `Risk/` (UPDATE/DELETE without WHERE, TRUNCATE, DROP TABLE analyzer) and `Settings/`
  (`settings.xml`). `Sql/SqlStatementScanner` splits text into GO batches and statements (no grammar: a statement starts at a
  statement keyword unless the word before it continues the current one); the formatter and the risk check both build on it.
  All unit tests target this layer; put testable logic here, not in the Vsix project.
- **`src/SqlAssist.Vsix`** (net48, WPF) — SSMS glue. It does **not** reference the Core assembly: Core's `.cs`
  files are compiled in via a linked `<Compile Include="..\SqlAssist.Core\**\*.cs">`, so the extension ships as one
  DLL. Pieces:
  - `Editor/` — MEF exports for the async-completion API (`SqlCompletionSource`, `SqlItemManager`,
    `SqlCommitManager`, `SqlCompletionPresenter`) bound to the SQL content type, plus `TabCommandFilter`, which owns
    the Tab key order: snippet → commit open completion → expand `*` → plain Tab.
  - `Connection/ActiveConnection.cs` — reads the active query window's server/database/login from SSMS's
    `SQLEditors` assembly **by reflection** (no public API; no SSMS DLL referenced at build). Every step must
    tolerate members disappearing in later SSMS builds. Falls back to the last known connection. Windows/SQL auth
    only (no Entra ID). DB access uses the in-box `System.Data.SqlClient`.
  - `Schema/` — `SchemaLoader` queries catalog views; `SchemaCache` keys by server+login+database, 10-minute TTL,
    and reloads (≥30s apart) when a query names a table the cache doesn't know.
  - `Search/` — the Search tool window (`SearchToolWindow`/`SearchControl`), `SearchIndexLoader`/`SearchIndexCache`
    (per-database in-memory index, 10-minute stale-while-revalidate, loads databases one by one and streams results),
    `SearchSettings` (object-type/match options, in memory only: every SSMS session starts from the defaults).
  - `Results/` — results grid › Generate Script As (INSERT / INSERT #tmpTable / WHERE IN() / WHERE AND OR / UPDATE
    to new window, clipboard or file). `ResultsGridReader` reads the focused grid by reflection
    (`Microsoft.SqlServer.GridControl`'s GridControl: `SelectedCells`, `GridStorage` = SQLEditors' `QEResultSet`, whose
    storage column 0 is the row-number column). The submenu hangs off SSMS's grid context menu
    (`{33F13AC3-80BB-4ECB-85BC-225435603A5E}:0x70`). Ctrl+J,<key> is caught in `ComponentDispatcher.ThreadPreprocessMessage`
    only while a grid has focus. Script text comes from Core `Results/ResultScriptBuilder`; UPDATE is the exception: it opens
    `UpdateScriptWindow` (target table and key columns chosen by the user, never guessed) and uses `UpdateScriptBuilder`.
    `ScriptDelivery` sends any generated text to a new query window, the clipboard or a file (never executes it).
  - `ClipboardSql/` — Tools › SqlAssist › CSV to SQL (`ClipboardSqlWindow`, Core `Clipboard/`; the code kept its
    original "Clipboard" names, only the user-facing name is CSV to SQL). Cell delimiter: Tab by default, comma / semicolon /
    pipe by choice, never guessed from the text. The clipboard's content is never written to the log.
  - `Formatting/` — `FormatSqlCommand` (Ctrl+K, Ctrl+Shift+F, the Tools menu, and a second command `cmdFormatSqlContext` on the
    query editor's right-click menu: SSMS's `IDM_SQLWB_SQLSCRIPT_CONTEXT` = 0x50 in the same `{33F13AC3-…}` group as the grid's
    0x70): formats the selection or the document in one edit (one Undo),
    replacing only the differing stretch and mapping the caret with `SqlFormatter.MapOffset`. The formatter only changes
    whitespace, keyword case and the end of a line a list comma sits at, and re-checks that on its own output
    (`SqlFormatter.Compare`: same tokens, comments and GO structure) or leaves the text alone. Only reserved words and
    positionally certain function/type words are re-cased (`KeywordCaser`), because a database may be case-sensitive.
  - `Risk/` — Check SQL Risks (`RiskCheckCommand`, `RiskWindow`: click a row to jump) and the experimental
    `ExecuteGuard`, a priority command target (off unless Options says so; nothing is registered while off) that asks before
    `Query.Execute` runs a script with findings. Which commands count as Execute is looked up by name through DTE and logged;
    it has not been confirmed against every way of executing, so never describe it as a guarantee.
  - `Settings/` — `SettingsProvider` (`%APPDATA%\SqlAssist\settings.xml`, re-read when the file changes) and `OptionsWindow`.
    `DialogHost` shows WPF windows owned by SSMS's main window; the windows are plain `Window`s so they can be rendered
    outside SSMS to check their layout (keep `ThreadHelper.ThrowIfNotOnUIThread` out of their constructors).
  - `Update/` — online update: `UpdateService` reads GitHub's `releases/latest` for `kriangdet/SSMS22SqlAssist_Addon`
    (daily on startup via background `ProvideAutoLoad`, or Tools › SqlAssist › Check for Updates), shows
    `UpdateInfoBar`, and on request downloads the release's `SqlAssistSetup-*.exe` (SHA-256 checked against the
    asset's GitHub digest) and starts it — the setup window then waits for SSMS to close. Version/asset parsing and
    the check/skip policy live in Core `Update/`. State: `%LOCALAPPDATA%\SqlAssist\update.txt`.
    Releases are published with `.\publish-release.ps1` (needs `$env:GITHUB_TOKEN`; text from `CHANGELOG.md`).
    "Install when SSMS closes" starts the downloaded setup with `/installonexit`, which waits (windowless) for every SSMS
    to exit, then installs behind a small `InstallOnExitForm` (progress, done / failed) and leaves `update-result.txt` for
    the next start to report. The waiter logs its own steps to `SqlAssist.log` through `SetupLog`.
  - `SqlAssistPackage.cs` + `SqlAssistPackage.vsct` — the AsyncPackage, Tools-menu commands, Ctrl+Shift+R refresh, the
    Ctrl+K, Ctrl+Shift+F key binding (checked against VS's defaults, SSMS 22's own profile — its built-in Format SQL is
    Ctrl+K, Ctrl+Q — and SqlAssist's keys). Bump `ProvideMenuResource` whenever the .vsct changes.
- **`src/SqlAssist.Setup`** (net48 WinForms) — `SqlAssistSetup.exe`. Its build depends on the Vsix project and
  embeds `SqlAssist.vsix` as a resource (`EmbedVsix` target); `SetupEngine` detects SSMS 22, the installed version
  (per-user under `%LOCALAPPDATA%\Microsoft\SSMS\22.0_*\Extensions`), whether SSMS is running, and drives SSMS's own
  `VSIXInstaller.exe` (uninstalls first so same-version reinstall works). CLI: `/install`, `/uninstall`
  (exit codes 0 ok, 1 fail, 2 SSMS running, 3 no SSMS 22). After installing it copies itself to
  `%LOCALAPPDATA%\SqlAssist\SqlAssistSetup.exe` and writes an HKCU `...\Uninstall\SqlAssist` key so it shows in
  Programs and Features; that entry runs the copy with `/remove` (the window, "uninstall" preselected).

Conventions that matter inside SSMS: exceptions must never escape into the editor — catch and write to `Log`
instead. Connection/reflection code runs on the UI thread (`ThreadHelper.ThrowIfNotOnUIThread`).

Statement boundaries for completion are `;`, `GO` or a blank line — except a blank line inside parentheses or right
after a CTE (`WITH x AS (...)` ⏎⏎ `SELECT`). CTE and derived-table columns come from the query text
(`DerivedTable`, linked to `TableRef.Derived`; `SchemaModel.Find(TableRef)` resolves them, borrowing types and
MS_Description from the columns they pass through). TVF columns are loaded with the schema and hang off `DbRoutine.Columns`
(`SchemaModel.FindFunctionTable`); built-in TVFs (STRING_SPLIT, OPENJSON) are unknown and skipped. Known v1 gaps (don't
assume they work): data search, columnstore/XML/spatial indexes in Search, searching inside constraint definitions. Editor features get their connection from
`EditorConnection.Get(window)`: the query window's, switched by the last `USE` inside the `TextWindow` above the caret
(`SqlUseStatement`); `$DB$` in snippets stays the window's database.
