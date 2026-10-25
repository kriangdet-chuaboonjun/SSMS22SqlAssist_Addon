# Go to Definition, Quick Info, TVF columns, USE, Search types — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** เพิ่มฟีเจอร์ 5 ข้อที่ผู้ใช้เลือกไว้: (3) F12 Go to Definition, (4) Quick Info บนคอลัมน์และ procedure/function,
(6) คอลัมน์ของ table-valued function, (7) completion ตาม `USE db` ในสคริปต์ และ (8) Search หา index / constraint / user-defined type

**Architecture:** ใส่ logic ทั้งหมดใน `SqlAssist.Core` (มี test) ส่วน `SqlAssist.Vsix` มีแค่ส่วนที่ต่อกับ SSMS โดยใช้ของที่มีอยู่แล้ว
คือ `SqlContextAnalyzer.FindHoverTarget` (ใช้ทั้ง hover และ F12), `SchemaCache`, `DefinitionLoader`, `SsmsNavigator.OpenScript/LocateAsync`,
`TableDetailsView`, `ColumnInfo` และ `SearchIndexLoader`

**Tech Stack:** C# (netstandard2.0 Core / net48 Vsix), xUnit, VS editor MEF (`IAsyncQuickInfoSource`, `IOleCommandTarget`), `System.Data.SqlClient`

**Spec:** ไม่มีไฟล์ spec แยก ขอบเขตมาจากข้อเสนอข้อ 3, 4, 6, 7, 8 ในบทสนทนาเมื่อ 2026-10-25 และส่วน "Known v1 gaps" ใน `CLAUDE.md`

## สิ่งที่มีอยู่แล้ว (อย่าเขียนซ้ำ)

- Hover บน **ชื่อตาราง / alias** ทำงานอยู่แล้ว: `src/SqlAssist.Vsix/Editor/TableQuickInfo.cs` + `SqlContextAnalyzer.FindHoverTarget`
  (มี test อยู่ใน `tests/SqlAssist.Core.Tests/HoverTargetTests.cs`) ข้อ 4 จึงเป็นการ **ขยาย** ไปที่คอลัมน์และ routine
- `TableRef.IsFunction` ถูกตั้งค่าแล้วตอน parse `FROM dbo.fn(...) x` แต่ `HasColumns` คืน false สำหรับ function
- `SearchIndexLoader.ForDatabase(connStr, db)` ใช้สลับ database ใน connection string ได้
- `ScriptDelivery.Status(string)` ใช้เขียนข้อความลง status bar ได้

## Global Constraints

- Exception ต้องไม่หลุดเข้า editor: ครอบด้วย `catch` แล้วเขียน `Log.Error(...)` ตามแบบของไฟล์รอบ ๆ
- โค้ดที่อ่าน connection หรือเรียก reflection ต้องรันบน UI thread (`ThreadHelper.ThrowIfNotOnUIThread`)
- Tooltip ใช้เฉพาะ schema ที่โหลดไว้แล้ว (`SchemaCache.TryGetLoaded`) ห้ามรอหรือยิง query เอง
- Comment ในโค้ดเป็นภาษาอังกฤษ ส่วน README / CHANGELOG เป็นภาษาไทย
- ทุก task เพิ่มรายการใน `CHANGELOG.md` ใต้ `## [Unreleased]` (เพิ่ม / เปลี่ยน / แก้ไข)
- ไม่ bump version และไม่ publish GitHub Release ในแต่ละ task ทำครั้งเดียวที่ Task 6 หลังผู้ใช้ทดสอบใน SSMS แล้วเท่านั้น
- ห้ามใส่ปุ่มลัดใหม่: F12 ใช้ command `Edit.GoToDefinition` (`VSStd97CmdID.GotoDefn`) ที่ผูกกับ F12 อยู่แล้ว
  (ตรวจแล้วว่า `SSMS.vssettings` และ `CurrentSettings.vssettings` ทั้ง 3 ชุดไม่มีการผูก F12 ใหม่)

## Review Focus

1. **TVF ในตัว (`STRING_SPLIT`, `OPENJSON`, `sys.dm_*`)**: ไม่มีใน schema ห้ามทำให้ `RefreshIfOlderThanThrottle` reload schema ทุก 30 วินาทีระหว่างพิมพ์ → test อยู่ที่ Task 1 Step 6
2. **`USE` ที่พิมพ์ผิดหรือไม่มีสิทธิ์**: schema โหลดไม่ขึ้น completion จะเหลือแค่ keyword/snippet ต้องไม่ค้างและไม่ throw (SchemaCache จำว่าโหลดล้มเหลวอยู่แล้ว) ต้องตรวจเองใน SSMS ที่ Task 6
3. **`USE` ที่อยู่ไกลเกิน 20,000 ตัวอักษรเหนือ caret**: มองไม่เห็นเพราะอยู่นอก `TextWindow` จะกลับไปใช้ database ของหน้าต่าง (เหมือนพฤติกรรมเดิม) ต้องเขียน comment `shortcut:` และบอกใน README
4. **F12 บนชื่อที่ไม่ใช่ object** (keyword, ตัวแปร, คอลัมน์, CTE): ต้องส่งต่อให้ command chain เดิมโดยไม่มี popup → test ฝั่ง Core อยู่ที่ Task 3 (`FindHoverTarget` คืน null/Column)
5. **คอลัมน์ที่ไม่ระบุตารางและซ้ำกันหลายตาราง** (`SELECT CustomerID FROM Orders o JOIN Customers c ...`): ห้ามแสดงคอลัมน์ของตารางที่เดาเอา → แสดงเฉพาะเมื่อมีตารางเดียวที่มีคอลัมน์นั้น ตรวจใน SSMS ที่ Task 6 (ฝั่ง Core คืน `Scope` ให้ test ได้ที่ Task 3)

---

### Task 1: คอลัมน์ของ table-valued function (ข้อ 6)

**Files:**
- Modify: `src/SqlAssist.Core/Schema/SchemaModel.cs` (`DbRoutine`, `SchemaModel`)
- Modify: `src/SqlAssist.Core/Sql/SqlContextAnalyzer.cs:30` (`TableRef.HasColumns`)
- Modify: `src/SqlAssist.Core/Schema/TableDetails.cs` (`Build(SchemaModel, DbObject, int)`)
- Modify: `src/SqlAssist.Vsix/Schema/SchemaLoader.cs`
- Modify: `src/SqlAssist.Vsix/Editor/SqlCompletionSource.cs:121`
- Test: `tests/SqlAssist.Core.Tests/TvfColumnTests.cs` (สร้างใหม่)

**Interfaces:**
- Produces:
  - `DbRoutine(string schema, string name, RoutineKind kind, IReadOnlyList<DbParameter> parameters, string returnType = null, string description = null, IReadOnlyList<DbColumn> columns = null)`
    พร้อม property `IReadOnlyList<DbColumn> Columns` (ไม่เคยเป็น null ถ้าไม่มีให้เป็น array ว่าง)
  - `DbObject SchemaModel.FindFunctionTable(string schema, string name)`: หา routine ด้วยกฎเดียวกับ `FindRoutine`
    แล้วคืน `DbObject` ของ TVF ที่มี `Kind = "Function"` และ `IsView = false` ถ้าไม่ใช่ TVF หรือไม่มีคอลัมน์ให้คืน null
    สร้าง `DbObject` ครั้งเดียวต่อ routine ใน constructor (เก็บใน `Dictionary<DbRoutine, DbObject>`) เพื่อให้ได้ instance เดิมทุกครั้ง
  - `SchemaModel.Find(TableRef r)`: ถ้า `r.IsFunction && r.Derived == null` ให้คืน `FindFunctionTable(r.Schema, r.Name)`
  - `TableRef.HasColumns => Derived != null || !IsDerived` (function นับว่ามีคอลัมน์ด้วย)
  - `TableDetails.Build(schema, o)` ตั้ง `Returns = "TABLE"` เมื่อ `o.Kind == "Function"` (`TableDetailsView` จะเลือก icon TableFunction ให้เอง)

- [ ] **Step 1: เขียน test ที่ยัง fail** ใน `TvfColumnTests.cs` โดยใช้ schema ที่มี `TestSchema` + TVF `dbo.fnOrders(@cust int)` ซึ่งมีคอลัมน์ `OrderID`, `Total`

```csharp
[Fact] public void FunctionRefHasColumns() {
    var ctx = SqlContextAnalyzer.Analyze("SELECT f. FROM dbo.fnOrders(1) f", 9);
    var r = ctx.TablesInScope.Single(t => t.Alias == "f");
    Assert.True(r.IsFunction); Assert.True(r.HasColumns);
    Assert.Equal(new[] { "OrderID", "Total" }, Schema.Find(r).Columns.Select(c => c.Name));
}
[Fact] public void StarExpandsFunctionColumns() {
    const string sql = "SELECT * FROM dbo.fnOrders(1) f";
    var star = SqlContextAnalyzer.FindStar(sql, 8);
    Assert.Contains("f.Total", StarExpander.Expand(star, Schema));
}
[Fact] public void CrossApplyFunctionHasColumns()   // "FROM Orders o CROSS APPLY dbo.fnOrders(o.CustomerID) f": Schema.Find(f) != null
[Fact] public void ScalarFunctionHasNoTable() =>     // FindFunctionTable("dbo", "fnScalar") == null
[Fact] public void FunctionDetailsSayReturnsTable()  // TableDetails.Build(Schema, Schema.FindFunctionTable("dbo","fnOrders")).Returns == "TABLE", Kind == "Function"
[Fact] public void HoverOnFunctionName()             // SqlContextAnalyzer.FindHoverTarget(sql, pos in "fnOrders").Table.IsFunction == true
```

- [ ] **Step 2: รัน test ให้เห็นว่า fail**
  Run: `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~TvfColumnTests"`
  Expected: FAIL (`DbRoutine` ยังไม่มี parameter `columns`)
- [ ] **Step 3: Implement ส่วน Core** ตาม Interfaces ด้านบน แล้วรัน **test ทั้งชุด** (การเปลี่ยน `HasColumns` มีผลกับ star, join, hover และ completion)
  Run: `dotnet test tests\SqlAssist.Core.Tests` → Expected: PASS ทั้งหมด
  ถ้ามี test เดิมที่ assert ว่า function ไม่มีคอลัมน์ ให้แก้ test นั้นและเขียนเหตุผลใน commit message
- [ ] **Step 4: `SchemaLoader`**: ในชุดผลลัพธ์แรก เปลี่ยนเป็น `WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT')` และใน `FlushObject` ถ้า type เป็น function
  ให้เก็บคอลัมน์ไว้ใน `Dictionary<string, DbColumn[]> functionColumns` (key `schema + "." + name`, OrdinalIgnoreCase) แทนการเพิ่มลง `objects`/`byKey`
  จากนั้นใน `FlushRoutine` ให้ส่ง `functionColumns.TryGetValue(...)` เป็น `columns:` (ยังคงยิง query รอบเดียว)
- [ ] **Step 5: `SqlCompletionSource.cs:121`**: เปลี่ยนเงื่อนไข refresh เป็น `t.HasColumns && !t.IsFunction && schema.Find(t) == null`
  เพื่อไม่ให้ TVF ในตัวของ SQL Server ทำให้ reload ซ้ำ ถ้าเพิ่งสร้าง TVF ใหม่ ผู้ใช้กด Ctrl+Shift+R เอง
- [ ] **Step 6: Test ของ Review Focus 1**: เพิ่ม `BuiltInFunctionResolvesToNull()` โดยใช้ `FROM STRING_SPLIT(@s, ',') s` แล้ว assert ว่า
  `HasColumns == true` และ `Schema.Find(r) == null` (ตัวป้องกันจริงอยู่ที่เงื่อนไขใน Step 5 ซึ่งเป็นฝั่ง Vsix)
- [ ] **Step 7: Build Vsix**: Run `dotnet build src\SqlAssist.Vsix -c Release` → Expected: Build succeeded
- [ ] **Step 8: Docs + commit**: ลบ "TVF columns" ออกจาก Known v1 gaps ใน `CLAUDE.md` และหัวข้อข้อจำกัดใน `README.md` แล้วเพิ่ม CHANGELOG
  "เพิ่ม: คอลัมน์ของ table-valued function (`FROM dbo.fn(...) x` แล้วพิมพ์ `x.`, `SELECT *` + Tab, hover)"
  `git commit -m "TVF columns in completion, * expansion and hover"`

---

### Task 2: Completion ตาม `USE db` ในสคริปต์ (ข้อ 7)

**Files:**
- Create: `src/SqlAssist.Core/Sql/SqlUseStatement.cs`
- Create: `src/SqlAssist.Vsix/Editor/EditorConnection.cs`
- Modify: `src/SqlAssist.Vsix/Connection/ActiveConnection.cs` (`ConnectionTarget`)
- Modify (เปลี่ยน `ActiveConnection.TryGet()` เป็น `EditorConnection.Get(window)`): `Editor/ExpansionTip.cs:93`, `Editor/SqlCompletionSource.cs:90`,
  `Editor/TabCommandFilter.cs:231` และ `:311`, `Editor/TableQuickInfo.cs:86`
- Test: `tests/SqlAssist.Core.Tests/UseStatementTests.cs`

**Interfaces:**
- Produces:
  - `public static string SqlUseStatement.LastDatabaseBefore(string text, int caret)`: ชื่อ database (ถอด `[]`/`""` ออกแล้ว) ของ `USE x` ตัวสุดท้าย
    ที่ token ชื่อจบก่อน caret (`name.End < caret`) ถ้าไม่เจอคืน null ใช้ `SqlTokenizer` จึงข้าม string/comment ได้เอง
    ไม่นับ `USE` ที่ตามด้วย `PLAN`/`HINT` หรือที่อยู่หลัง `(` หรือ `,` (เป็น query hint ใน `OPTION (...)`) และไม่สนใจ `GO` เพราะ database ที่ `USE` ไว้ยังมีผลข้าม batch
  - `ConnectionTarget ConnectionTarget.ForDatabase(string database)`: ถ้าชื่อเดียวกับ `Database` (OrdinalIgnoreCase) คืน `this`
    ถ้าไม่ใช่ คืน copy ใหม่ที่เปลี่ยน `Database`, `ConnectionString` (ผ่าน `SqlConnectionStringBuilder.InitialCatalog`) และ
    `Key = (Server + "|" + database + "|" + (IntegratedSecurity ? "windows" : UserName)).ToLowerInvariant()` โดยเก็บ field อื่นไว้เหมือนเดิม
  - `internal static ConnectionTarget EditorConnection.Get(TextWindow window)`: เรียก `ActiveConnection.TryGet()` (บน UI thread)
    แล้วถ้า `LastDatabaseBefore(window.Text, window.Caret)` ไม่เป็น null ให้คืน `.ForDatabase(db)` ใส่ comment
    `// shortcut: a USE more than TextWindow.Radius characters above the caret is not seen, falls back to the window's database; scan the whole buffer if users hit it.`

- [ ] **Step 1: เขียน test ที่ยัง fail** (`|` = caret, helper เหมือน `HoverTargetTests.Hover`)

```csharp
[Theory]
[InlineData("USE Sales;\nSELECT * FROM |", "Sales")]
[InlineData("USE [My Db]\nGO\nSELECT |", "My Db")]
[InlineData("USE A\nUSE B\nSELECT |", "B")]
[InlineData("SELECT |\nUSE Later", null)]
[InlineData("-- USE Sales\nSELECT |", null)]
[InlineData("SELECT 'USE Sales' |", null)]
[InlineData("SELECT 1 OPTION (USE HINT('X'))\nSELECT |", null)]
[InlineData("USE Sal|", null)]          // the name still being typed
[InlineData("SELECT |", null)]
public void LastDatabaseBefore(string sql, string expected)
```

- [ ] **Step 2: รันให้ fail**: `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~UseStatementTests"` → FAIL (ยังไม่มี type)
- [ ] **Step 3: Implement `SqlUseStatement`** → รัน test ชุดเดิม → PASS
- [ ] **Step 4: Implement `ForDatabase` + `EditorConnection` แล้วแก้ call site ทั้ง 5 จุด** โดย `SnippetInserter` (`$DB$`) **ไม่เปลี่ยน**
  เพราะ `$DB$` หมายถึง database ของหน้าต่าง ไม่ใช่ของสคริปต์ (บันทึกเหตุผลนี้ใน commit message)
- [ ] **Step 5: Build**: `dotnet build src\SqlAssist.Vsix -c Release` → Build succeeded
- [ ] **Step 6: Docs + commit**: ลบ "`USE` switching the database" ออกจาก Known v1 gaps (`CLAUDE.md`) และ README ส่วน README ให้เขียนข้อจำกัด 20,000 ตัวอักษรเพิ่ม
  CHANGELOG: "เพิ่ม: completion, `*` + Tab และ hover ใช้ตารางของ database ที่ `USE` ไว้ในสคริปต์เหนือ caret"
  `git commit -m "Completion follows USE <database> in the script"`

---

### Task 3: Hover บนคอลัมน์และ procedure / function (ข้อ 4)

**Files:**
- Modify: `src/SqlAssist.Core/Sql/SqlContextAnalyzer.cs` (`HoverTarget`, `FindHoverTarget`)
- Modify: `src/SqlAssist.Vsix/Editor/TableQuickInfo.cs` (`GetQuickInfoItemAsync`, doc comment)
- Test: `tests/SqlAssist.Core.Tests/HoverTargetTests.cs`

**Interfaces:**
- Consumes: `SchemaModel.Find(TableRef)` (Task 1), `EditorConnection.Get` (Task 2)
- Produces (ใช้ต่อใน Task 4):
  - `public enum HoverKind { Table, Column, Routine }`
  - `HoverTarget` มี property เพิ่ม: `HoverKind Kind`, `string Column`, `IReadOnlyList<TableRef> Scope`, `string Schema`, `string Name` โดย `Table`, `Start` และ `End` คงไว้เหมือนเดิม
  - กฎของ `FindHoverTarget` เรียงตามลำดับ (กฎแรกที่เข้าเงื่อนไขชนะ):
    1. ชื่อหรือ alias ของตาราง (กฎเดิม) → `Kind = Table`
    2. ชื่อที่มี `(` ตามหลังติดกัน หรืออยู่หลัง `EXEC` (`IsExecAt`) → `Kind = Routine` โดย `Schema`/`Name` มาจาก chain `a.b` และ `Start`/`End` ครอบทั้ง chain
    3. ชื่อที่อยู่หลัง `q.` และ `q` ตอบเป็น table ref ที่ `HasColumns` → `Kind = Column`, `Table = ref นั้น`, `Column = token.Value`
    4. ชื่อเดี่ยว (ไม่ใช่ reserved word และไม่ได้ตามด้วย `.`) ใน statement ที่มีตาราง → `Kind = Column`, `Table = null`,
       `Scope` = ตารางที่มองเห็นได้จาก group/segment ของ token นั้นและมี `HasColumns`
    5. นอกนั้นคืน null

- [ ] **Step 1: แก้และเพิ่ม test ที่ยัง fail**: ย้าย `"SELECT wr.Req|Id FROM Work_Request wr"` ออกจาก `NothingToShow` แล้วเพิ่ม test ต่อไปนี้

```csharp
[Fact] public void QualifiedColumn()   // Kind == Column, Table.Name == "Work_Request", Column == "ReqId", span "ReqId"
[Fact] public void UnqualifiedColumn_ScopeHasBothTables() // "SELECT Req|Id FROM Work_Request wr JOIN Work_InitialApproval wia ON ..." → Table == null, Scope.Count == 2
[Fact] public void ExecProcedure()     // "EXEC dbo.usp_Ge|tOrders 1" → Kind == Routine, Schema == "dbo", Name == "usp_GetOrders", span "dbo.usp_GetOrders"
[Fact] public void ScalarFunctionCall()// "SELECT dbo.fnTa|x(1)" → Kind == Routine, Name == "fnTax"
[Fact] public void ExistingTableCasesKeepKindTable() // every case of the current Theory/Facts returns Kind == Table
[Theory]
[InlineData("SEL|ECT 1 FROM Work_Request wr")]
[InlineData("SELECT 'Work_Req|uest' FROM Work_Request wr")]
[InlineData("SELECT @v|ar FROM Work_Request wr")]
[InlineData("SELECT GETD|ATE()")]   // routine rule matches: Kind == Routine, Name == "GETDATE"; the Vsix side finds no routine and shows nothing
public void NothingOrUnresolved(string sql)
```

  (กรณี `GETDATE()` ให้ assert `Kind == Routine` ไม่ใช่ null ใส่ไว้เพื่อเป็นเอกสารว่า Core ไม่ได้รู้จักชื่อ function ในตัว)
- [ ] **Step 2: รันให้ fail**: `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~HoverTargetTests"` → FAIL
- [ ] **Step 3: Implement ใน Core** → รัน `dotnet test tests\SqlAssist.Core.Tests` → PASS ทั้งชุด
- [ ] **Step 4: `TableQuickInfo`**: แยกตาม `hover.Kind`
  - `Table` ใช้โค้ดเดิม
  - `Routine` → `schema.FindRoutine(hover.Schema, hover.Name)` → `TableDetailsView.Create(TableDetails.Build(routine), ...)` ถ้าหาไม่เจอคืน null
  - `Column` → ถ้ามี `hover.Table` ให้ใช้ `schema.Find(hover.Table)` ถ้าไม่มีให้ใช้ทุกตารางใน `Scope` ที่ `Find` แล้วมีคอลัมน์ชื่อนี้ (OrdinalIgnoreCase)
    ต้องเจอ **ตารางเดียวพอดี** จึงแสดง (Review Focus 5) แล้วส่ง `ColumnInfo.Describe(schema, owner, column)` เป็น content ของ `QuickInfoItem` ในรูป string
- [ ] **Step 5: Build** → Build succeeded
- [ ] **Step 6: Docs + commit**: README หัวข้อปุ่มลัด/การใช้งาน: "ชี้เมาส์ที่คอลัมน์ หรือชื่อ procedure / function เพื่อดูชนิดข้อมูล key และคำอธิบาย"
  CHANGELOG "เพิ่ม: ..." แล้ว `git commit -m "Quick info for columns and procedures/functions"`

---

### Task 4: F12 Go to Definition (ข้อ 3)

**Files:**
- Create: `src/SqlAssist.Vsix/Editor/GoToDefinition.cs`
- Modify: `src/SqlAssist.Vsix/Editor/TabCommandFilter.cs` (`QueryStatus`, `Exec`)

**Interfaces:**
- Consumes: `HoverTarget`/`HoverKind` (Task 3), `EditorConnection.Get` (Task 2), `SchemaModel.FindFunctionTable` (Task 1),
  `DefinitionLoader.LoadAsync`, `SsmsNavigator.OpenScript(ConnectionTarget, SearchItem, -1, 0)`, `SsmsNavigator.LocateAsync`,
  `SearchIndexLoader.CurrentDatabaseAsync`, `ScriptDelivery.Status`
- Produces: `internal static bool GoToDefinition.TryRun(IWpfTextView view)` คืน true เมื่อจัดการแล้ว และคืน false เพื่อส่งต่อ command ให้ SSMS

**พฤติกรรมของ `TryRun`** (ทุกอย่างรันบน UI thread ใช้ timeout แบบเดียวกับ `TabCommandFilter.DefinitionWait` = 10 วินาที และ `StarSchemaWait` = 5 วินาที):
1. ใช้ `FindHoverTarget` ที่ตำแหน่ง caret ถ้าไม่เจอให้ลอง `caret - 1` (กรณี caret อยู่ท้ายคำ)
2. ถ้าไม่เจอ, ได้ `Kind == Column` หรือได้ `Table.Derived != null` (CTE / derived table) → คืน false
3. `target = EditorConnection.Get(window)` ถ้าเป็น null → `Status("SqlAssist: no connected query window")` แล้วคืน false
4. หา schema ผ่าน `SchemaCache.GetAsync(target, 5s)` แล้วใช้ `obj = Find(schema, name)`
   - ถ้าเป็น **ตาราง** (`obj != null && !obj.IsView`): `LocateAsync(target, new SearchItem(db, obj.Schema, obj.Name, SearchObjectType.Table))`
     ผ่าน `JoinableTaskFactory.RunAsync(...).FileAndForget("SqlAssist/GoToDefinition")` แล้วส่งข้อความผิดพลาด (ถ้ามี) ไปที่ `Status`
   - ถ้าเป็นแบบอื่น (view, routine, TVF หรือชื่อที่ schema ไม่รู้จัก เช่น trigger/synonym): `DefinitionLoader.LoadAsync`
     ถ้าได้ null → `Status("SqlAssist: no definition found for <name>")` แล้วคืน false
     ถ้าได้ข้อความ → `OpenScript(target, new SearchItem(db, schema, name, type, definition: text), -1, 0)` แล้วคืน true
     (`type` = View / Procedure / Function ตามที่หาเจอ ถ้าไม่รู้ให้ใช้ Procedure เพราะ `OpenScript` ใช้แค่ `Database` กับ `PreviewText`)
5. `db = target.Database ?? CurrentDatabaseAsync(...)` เพราะ `OpenScript` เขียน `USE [db]` นำหน้า ห้ามปล่อยให้เป็น `USE []`
6. ครอบทั้งหมดด้วย `catch (Exception ex) { Log.Error("Go to definition", ex); return false; }`

- [ ] **Step 1: Implement `GoToDefinition.TryRun`** ตามพฤติกรรมด้านบน
- [ ] **Step 2: ผูกกับ `TabCommandFilter`**: ใน `Exec` ถ้า `pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn && GoToDefinition.TryRun(_view)`
  ให้ return S_OK ใน `QueryStatus` ให้ GotoDefn เป็น SUPPORTED|ENABLED (แบบเดียวกับ `RefreshCommand`) เพื่อให้ F12 มาถึง filter แม้ SSMS จะปิด command นี้ไว้
- [ ] **Step 3: Build** → Build succeeded
- [ ] **Step 4: Review**: ส่งให้ agent `ssms-safety-reviewer` ตรวจไฟล์ที่แก้ใน Task 2–4 แล้วแก้ทุก finding ที่ยืนยันได้
- [ ] **Step 5: Docs + commit**: README หัวข้อใหม่ "F12 ไปที่ definition": ตารางจะเปิดใน Object Explorer ส่วน view/procedure/function/trigger
  จะเปิด script ในหน้าต่างใหม่ (ไม่รันให้) แล้วเพิ่ม CHANGELOG และ `git commit -m "F12 Go to Definition"`

---

### Task 5: Search หา index / constraint / user-defined type (ข้อ 8)

**Files:**
- Modify: `src/SqlAssist.Core/Search/SearchModel.cs` (`SearchObjectType`, `SearchItem`)
- Modify: `src/SqlAssist.Core/Search/ObjectUrn.cs`
- Modify: `src/SqlAssist.Core/Search/TableScriptBuilder.cs`
- Create: `src/SqlAssist.Core/Search/ObjectScriptBuilder.cs`
- Modify: `src/SqlAssist.Vsix/Search/SearchIndexLoader.cs`, `SearchControl.cs` (`TypeChoices`, `ResultRow.Type`, `CopyName`), `SsmsNavigator.cs:44` (fallback ไป owner)
- Modify: `CONTEXT.md` (Search Item / Object Type)
- Test: `tests/SqlAssist.Core.Tests/Search/ObjectUrnTests.cs`, `tests/SqlAssist.Core.Tests/Search/ObjectScriptBuilderTests.cs` (ใหม่), `SearchMatcherTests.cs`

**Interfaces:**
- Produces:
  - `SearchObjectType`: เพิ่ม `Index = 128, Constraint = 256, UserDefinedType = 512` และให้ `All` รวมสามค่านี้ด้วย
  - `SearchItem`: เพิ่มพารามิเตอร์ท้าย constructor `string subKind = null` และ property `SubKind`
    ใช้ค่า `"PrimaryKey" | "Unique" | "ForeignKey" | "Check" | "Default"` สำหรับ Constraint และ `"Alias" | "Table"` สำหรับ UDT ถ้าเป็น type อื่นให้เป็น null
    โดย `Parent`/`ParentType` ของ Index และ Constraint คือตารางหรือ view ที่เป็นเจ้าของ
  - `ObjectUrn.Candidates`:
    - Index, PrimaryKey และ Unique → `<owner>/Index[@Name='x']`
    - ForeignKey → `Table.../ForeignKey[@Name]`
    - Check → `Table.../Check[@Name]`
    - Default → URN ของตารางเจ้าของ (ใน SMO default อยู่ใต้ column ซึ่ง item ไม่ได้เก็บชื่อไว้)
    - UDT Alias → `Database/UserDefinedDataType[@Name and @Schema]` และ UDT Table → `UserDefinedTableType[@Name and @Schema]`
  - `ObjectScriptBuilder` (Core, คืนข้อความ CRLF และใช้ `TableScriptBuilder.Bracket` ทุกชื่อ):
    - `string Index(string schema, string table, string name, bool unique, bool clustered, IReadOnlyList<(string column, bool descending)> keys, IReadOnlyList<string> included, string filter)`
    - `string Constraint(string schema, string table, string name, string subKind, string body)`: `body` คือส่วนหลัง `ADD CONSTRAINT [n] `
      ที่ loader ประกอบไว้แล้ว เช่น `CHECK ([Qty]>(0))`, `DEFAULT (getdate()) FOR [Created]`, `FOREIGN KEY ([a]) REFERENCES [s].[t] ([b])` หรือ `PRIMARY KEY CLUSTERED ([a])`
    - `string AliasType(string schema, string name, string baseType, bool nullable)`
  - `TableScriptBuilder.BuildTableType(string schema, string name, IReadOnlyList<TableColumn> columns, PrimaryKey primaryKey)`:
    ใช้ส่วน body เดียวกับ `Build` แต่ header เป็น `CREATE TYPE [s].[n] AS TABLE (` โดยแยก body ออกเป็น private method เพื่อไม่ให้มีโค้ดซ้ำ

- [ ] **Step 1: เขียน test ที่ยัง fail**

```csharp
// ObjectUrnTests
[Fact] public void IndexOfView()        // ParentType View → ".../View[@Name='vOrders' and @Schema='dbo']/Index[@Name='IX_v']"
[Fact] public void ForeignKey()         // subKind "ForeignKey" → ".../Table[...]/ForeignKey[@Name='FK_x']"
[Fact] public void DefaultGoesToTable() // subKind "Default" → ends with "/Table[@Name='Orders' and @Schema='dbo']"
[Fact] public void TableType()          // UserDefinedType, subKind "Table" → ".../UserDefinedTableType[@Name='OrderList' and @Schema='dbo']"
// ObjectScriptBuilderTests
[Fact] public void FilteredIndexWithInclude() => Assert.Equal(
    "CREATE UNIQUE NONCLUSTERED INDEX [IX_a] ON [dbo].[T] ([a], [b] DESC)\r\nINCLUDE ([c])\r\nWHERE ([a] IS NOT NULL);",
    ObjectScriptBuilder.Index("dbo", "T", "IX_a", true, false, new[] { ("a", false), ("b", true) }, new[] { "c" }, "([a] IS NOT NULL)"));
[Fact] public void CheckConstraint() => Assert.Equal(
    "ALTER TABLE [dbo].[T] ADD CONSTRAINT [CK_q] CHECK ([Qty]>(0));",
    ObjectScriptBuilder.Constraint("dbo", "T", "CK_q", "Check", "CHECK ([Qty]>(0))"));
[Fact] public void AliasType() => Assert.Equal("CREATE TYPE [dbo].[Phone] FROM varchar(20) NOT NULL;", ObjectScriptBuilder.AliasType("dbo", "Phone", "varchar(20)", false));
// TableScriptBuilderTests
[Fact] public void TableTypeHeader()    // BuildTableType(...) starts with "CREATE TYPE [dbo].[OrderList] AS TABLE ("
// SearchMatcherTests
[Fact] public void TypeFilterExcludesIndexes() // Types = Table → an Index item named like the query does not match
```

- [ ] **Step 2: รันให้ fail**: `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~SqlAssist.Core.Tests.Search"` → FAIL
- [ ] **Step 3: Implement ใน Core** → `dotnet test tests\SqlAssist.Core.Tests` → PASS
- [ ] **Step 4: `SearchIndexLoader`**: เปลี่ยนชุดผลลัพธ์ที่ 4 (PK) เป็น index ทุกตัว
  (`i.type IN (1, 2) AND i.is_hypothetical = 0` คืน object_id, index name, clustered, is_unique, is_primary_key, is_unique_constraint, filter_definition,
  column name, is_descending_key, is_included_column เรียงตาม object_id, index_id, is_included_column, key_ordinal, index_column_id)
  แถว PK ยังต้องป้อน `ObjectRow.Key` เหมือนเดิม (เพื่อให้ preview CREATE TABLE ไม่เปลี่ยน) จากนั้นเพิ่มชุดผลลัพธ์สำหรับ
  `sys.foreign_keys` (+ columns), `sys.check_constraints` (definition), `sys.default_constraints` (definition + ชื่อคอลัมน์) และ
  `sys.types WHERE is_user_defined = 1` (table type ใช้ `sys.table_types.type_table_object_id` → `sys.columns`)
  ทุกชุดกรอง `is_ms_shipped = 0` ของตารางเจ้าของ ส่วน columnstore index (type 5/6) ยังไม่รวม ให้ใส่ comment `shortcut:` ไว้
  ถ้าจะค้นหา constraint ตาม definition ด้วยก็ทำได้ แต่รอบนี้ค้นแค่ตามชื่อ (`Definition = null`, ใช้ `Script` เป็น preview)
- [ ] **Step 5: `SearchControl`**: เพิ่ม `(Index, "Index"), (Constraint, "Constraint"), (UserDefinedType, "User-defined type")` ใน `TypeChoices`
  ให้ `ResultRow.Type` = label จาก `TypeChoices` + `" (SubKind)"` ถ้ามี SubKind และให้ `CopyName` ของ Index/Constraint คัดลอกแค่ `[name]`
  ส่วน `SsmsNavigator.cs:44` ให้ขยายเงื่อนไข fallback ไป owner เป็น `item.Parent != null`
- [ ] **Step 6: Build** → Build succeeded
- [ ] **Step 7: Docs + commit**: แก้ `CONTEXT.md` (รายการ type ของ Search Item / Object Type), ลบ "index/constraint/UDT search" ออกจาก Known v1 gaps,
  เพิ่มหัวข้อค้นหา object ใน README และ CHANGELOG แล้ว `git commit -m "Search indexes, constraints and user-defined types"`

---

### Task 6: ตรวจทั้งหมดก่อนปล่อย

- [ ] **Step 1**: `dotnet test tests\SqlAssist.Core.Tests` → PASS ทั้งหมด และ `.\build-setup.ps1` → ได้ `dist\SqlAssistSetup-<version>.exe`
- [ ] **Step 2**: ส่ง branch ทั้งหมดให้ `ssms-safety-reviewer` ตรวจ แล้วแก้ finding ที่ยืนยันได้
- [ ] **Step 3: ผู้ใช้ทดสอบใน SSMS 22** (`.\install.ps1`) ตาม checklist นี้ แล้วอ่าน `%LOCALAPPDATA%\SqlAssist\SqlAssist.log`
  - `FROM dbo.<TVF>(...) x` → `x.` ขึ้นคอลัมน์, `SELECT *` + Tab ขยายได้, ชี้เมาส์แล้วเห็นแผงที่เขียนว่า "returns TABLE"
  - `FROM STRING_SPLIT(@s, ',') s` → log ไม่มี "Loaded schema" ซ้ำทุก 30 วินาทีระหว่างพิมพ์
  - `USE <db อื่น>` บนสุด → completion แสดงตารางของ db นั้น, `USE` db ที่ไม่มีอยู่จริง → SSMS ไม่ค้าง และ log มี error ครั้งเดียว
  - Hover คอลัมน์ `x.Col`, คอลัมน์เดี่ยวที่มีตารางเดียว, คอลัมน์ซ้ำสองตาราง (ต้องไม่ขึ้น) และ `EXEC proc`
  - F12 บนตาราง (เปิด Object Explorer), view/proc (เปิดหน้าต่างใหม่ที่มี `USE`), keyword/ตัวแปร (ไม่มี popup) และเมนูคลิกขวา Go To Definition
  - Search: ติ๊ก Index / Constraint / User-defined type แล้วค้นหา, ดู preview และ Locate
- [ ] **Step 4**: เมื่อผู้ใช้บอกว่าผ่าน ให้ bump `Directory.Build.props` เป็น `1.5.0`, เปลี่ยนหัว `[Unreleased]` ใน CHANGELOG เป็น `## [1.5.0] - <วันที่>`
  และเปิด `[Unreleased]` ใหม่ แล้ว commit + push **ห้าม publish GitHub Release จนกว่าผู้ใช้จะสั่ง**
