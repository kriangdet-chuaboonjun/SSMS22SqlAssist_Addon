# Changelog

บันทึกว่าแต่ละเวอร์ชันเพิ่มหรือแก้อะไร ส่วนของเวอร์ชันไหน `publish-release.ps1` จะเอาไปแสดงเป็นรายละเอียดในหน้า
[GitHub Releases](https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases) ของเวอร์ชันนั้น

ทุกครั้งที่แก้โค้ด ให้เขียนเพิ่มไว้ใต้ `## [Unreleased]` ก่อน พอปรับเวอร์ชันใน `Directory.Build.props` แล้วก็เปลี่ยนหัวข้อเป็น
`## [x.y.z] - YYYY-MM-DD` แล้วเปิด `## [Unreleased]` อันใหม่ไว้ด้านบน

## [Unreleased]

## [1.6.1] - 2026-11-27

### เพิ่ม

- ไฟล์ [REVISION_HISTORY.md](REVISION_HISTORY.md): สรุปสั้น ๆ ว่าแต่ละเวอร์ชันมีอะไร เริ่มตั้งแต่ 1.5.0
- **หน้าต่าง Search:** ปุ่มไอคอนข้างช่องกรองในเมนูเลือก Database สำหรับล้าง Database ที่เลือกทั้งหมด (รวม All databases) ในคลิกเดียว

### เปลี่ยน

- **ตัวติดตั้ง:** ก่อนอัปเดตจะเตรียมไฟล์กู้คืนของเวอร์ชันที่ติดตั้งอยู่ไว้ก่อน ถ้าติดตั้งเวอร์ชันใหม่ไม่สำเร็จจะลงเวอร์ชันเดิมกลับให้ ถ้าเตรียมไฟล์กู้คืนไม่ได้จะหยุดโดยไม่ถอนของเดิม และจะไม่ทำงานถ้ายังเปิด SSMS อยู่หรือมี Setup อีกหน้าต่างกำลังทำงาน
- **หน้าต่าง Search:** จำกัดหน่วยความจำของ Search Index database ที่ใหญ่เกินจะค้นทีละส่วนจากฐานข้อมูลแทนการเก็บทั้งหมดไว้ในหน่วยความจำ และ index ที่ไม่ได้ใช้นานจะถูกล้างออก
- **IntelliSense ของ `#temp` / `@table` และปุ่ม Tab:** วิเคราะห์ข้อความในเบื้องหลัง editor ไม่ต้องรอระหว่างโหลด

### แก้ไข

- **หน้าต่าง Search:** เปิดเมนูเลือก Database ครั้งแรกแล้วเหมือนค้าง ตอนนี้ดึงรายชื่อ Database ไว้ล่วงหน้าตั้งแต่เปิดหน้าต่าง ถ้ายังโหลดไม่เสร็จปุ่มจะขึ้น "Loading…" และแถบสถานะบอกว่ากำลังโหลด รอนานสุด 30 วินาที ถ้าไม่สำเร็จจะแจ้งสาเหตุ และ log บันทึกเวลาที่ใช้
- **Format SQL:** ถ้าวงเล็บหรือ `CASE` ซ้อนกันเกิน 128 ชั้น จะแจ้งเหตุผลและไม่แก้ข้อความ แทนที่จะเสี่ยงทำให้ SSMS หยุดทำงาน

## [1.6.0] - 2026-11-19

### เพิ่ม

- **AI Assistant (แชตด้านข้าง):** เปิดจาก **Tools › SqlAssist › AI Assistant…** เป็นหน้าต่าง dock ด้านขวาของ editor ถามเรื่อง SQL กับ **OpenRouter** หรือ **Ollama** (เครื่องตัวเองหรือ server ในองค์กร) วิธีตั้งค่าอยู่ใน [docs/ssms-ai-assistant.md](docs/ssms-ai-assistant.md)
  - ส่งเฉพาะข้อความที่พิมพ์และ SQL ที่กด **แนบ SQL ที่เลือก** เอง (ดูและลบได้ก่อนส่ง) ไม่อ่านทั้งไฟล์ ไม่ส่ง schema ผลลัพธ์ query หรือข้อมูล connection
  - เมนู **…** มี Explain SQL / Review SQL / Suggest Optimization ซึ่งเตรียมข้อความให้ตรวจก่อนกดส่งเอง
  - code block ในคำตอบมีปุ่ม Copy และ **Insert into Editor** (แทรกที่ caret, Undo ได้ในครั้งเดียว) SqlAssist ไม่รัน SQL ให้
  - ตั้งค่า provider, model และ key ที่ **Settings / API & Keys** ในแชต หรือ **Options › AI Assistant** key ของ OpenRouter เก็บใน Windows Credential Manager (หรือใช้ `OPENROUTER_API_KEY`) ไม่อยู่ใน `settings.xml` และไม่เขียนลง log บทสนทนาไม่ถูกบันทึกลงดิสก์
  - ยังไม่ได้ลองส่งคำถามจริงกับ OpenRouter / Ollama และการแทรก SQL ใน SSMS จริง
- **IntelliSense สำหรับ `#temp` และ `@table`** ที่ประกาศใน Query Editor (`CREATE TABLE #x (...)`, `DECLARE @x TABLE (...)`) เติมชื่อคอลัมน์และขยาย `*` ด้วย Tab ได้ ยังไม่รวม `SELECT INTO`, dynamic SQL และ `##temp`

### เปลี่ยน

- **หน้าต่าง Options ใหม่:** ตั้งค่าอยู่ซ้าย ตัวอย่าง SQL อยู่ขวา ปุ่มบันทึก/ยกเลิกอยู่ล่างเสมอ สีตามธีม Light/Dark ของ SSMS ตัวเลือกและวิธีบันทึกเหมือนเดิม
- **หน้าต่าง Search:** dropdown และปุ่มใช้หน้าตาเดียวกับ Options ตำแหน่งและการทำงานเหมือนเดิม
- ภายใน: ทุกการเชื่อมต่อฐานข้อมูลของ SqlAssist (schema, Search, Find Usages, F12, ALTER + Tab) เปิดผ่านจุดเดียวกัน เพื่อเตรียมรองรับ Entra ID การทำงานกับ Windows auth และ SQL auth เหมือนเดิม

## [1.5.7] - 2026-11-13

### เพิ่ม

- **Generate Script As › INSERT รู้จักคอลัมน์ IDENTITY:** ถ้าคอลัมน์ที่เลือกใน results grid มีคอลัมน์ IDENTITY script จะครอบ INSERT ด้วย `SET IDENTITY_INSERT ตาราง ON` … `OFF` ให้ (เดิมรันแล้ว error `Cannot insert explicit value for identity column`) เลือกได้ที่ **Tools › SqlAssist › Options… › Results** ว่าจะ
  - **เก็บค่าเดิมไว้** (ค่าเริ่มต้น): ใช้ `SET IDENTITY_INSERT` เหมาะกับการย้ายข้อมูลให้เลข ID ตรงกัน (ต้องมีสิทธิ์ ALTER บนตาราง) ชื่อตารางใช้ชื่อเดียวกับใน INSERT ถ้าเป็น `[Table]` ให้แก้ทั้งสามที่
  - **ตัดคอลัมน์ IDENTITY ออก**: ให้ตารางปลายทางออกเลขใหม่เอง script มี comment บอกว่าตัดคอลัมน์ไหนออก
  - ค่าถูกจำใน `settings.xml` (`<Results InsertIdentity="KeepValues|Skip"/>`) · INSERT #tmpTable, WHERE IN() และ WHERE AND OR ทำงานเหมือนเดิม
  - รู้ว่าคอลัมน์ไหนเป็น IDENTITY จากข้อมูลของผลลัพธ์ใน SSMS (ไม่ query ฐานข้อมูลเพิ่ม) คอลัมน์ที่เป็นผลจากการคำนวณจะไม่ถูกมองเป็น IDENTITY

- **รายการแนะนำเรียนรู้จากที่ใช้บ่อย:** รายการที่เลือกบ่อยและเพิ่งใช้ (keyword, snippet, ตาราง, คอลัมน์, function) จะขึ้นมาก่อนรายการอื่นที่ตรงกับคำที่พิมพ์พอ ๆ กัน
  - มีผลเมื่อพิมพ์แล้วอย่างน้อย 1 ตัวอักษร และต้องเลือกรายการนั้นอย่างน้อย 3 ครั้ง คำที่พิมพ์ครบยังขึ้นก่อนเสมอ และรายการที่ขึ้นต้นตรงยังอยู่เหนือรายการที่แค่มีตัวอักษรตรง ที่เลิกใช้แล้วจะค่อย ๆ ลดลง (ครึ่งหนึ่งทุก 14 วัน)
  - keyword และ snippet นับรวมทุกฐานข้อมูล ส่วนตาราง คอลัมน์ และ function นับแยกตาม server + database
  - เก็บในเครื่องที่ `%LOCALAPPDATA%\SqlAssist\usage.txt` (มีชื่อตารางและคอลัมน์ ไม่ส่งไปที่ไหนและไม่เขียนลง log) ปิดได้และล้างประวัติได้ที่ **Tools › SqlAssist › Options… › Editor**
  - คอลัมน์ที่ติ๊กในตัวเลือกคอลัมน์ของ `SELECT |` ยังไม่ถูกนับ

### เปลี่ยน

- **Generate Script As › UPDATE:** คอลัมน์ IDENTITY ที่เลือกมาจะเริ่มเป็น **Key** แทน SET (SQL Server ไม่ให้ UPDATE คอลัมน์ IDENTITY) และแสดงคำว่า `IDENTITY` ต่อท้ายชนิดข้อมูล ยังเปลี่ยนเองได้ ถ้าติ๊ก SET ที่คอลัมน์ IDENTITY จะมีคำเตือน
- **รายการแนะนำหลังชื่อตาราง + alias:** พิมพ์ต่อจาก `FROM ตาราง alias` (เช่น `FROM JOB_General jg w`) `WHERE` จะขึ้นก่อน `WHEN` / `WITH` / `WHILE` (อยู่ถัดจาก snippet ที่ชื่อสั้นกว่า เช่น `wh`) ตำแหน่งอื่นเรียงเหมือนเดิม

## [1.5.6] - 2026-11-10

### เพิ่ม

- **Find Usages (หน้าต่าง Search):** คลิกขวาที่ตาราง / view / procedure / function / คอลัมน์ / user-defined type ในผลลัพธ์ › **Find Usages** (หรือ **Shift+F12** ขณะโฟกัสอยู่ที่ตารางผลลัพธ์ ถ้า SSMS ส่งปุ่มนี้มาให้หน้าต่าง Search) จะแสดงว่า object ไหนอ้างถึงมัน ตารางผลลัพธ์เปลี่ยนเป็น "Usages of [dbo].[X] (n)" คอลัมน์ "Matched in" เป็น "How" preview ไฮไลต์ชื่อที่เจอใน definition ปุ่ม **← Back to results** หรือ **Esc** กลับไปผลค้นหาเดิม (Enter / Ctrl+Enter / คลิกขวาใช้กับแถวใน Usages ได้เหมือนเดิม)
  - ดูจาก database ของ object นั้นเท่านั้น อ่านอย่างเดียวจาก catalog view: dependency ของ SQL Server (รวมการอ้างที่ยังไม่ resolve ซึ่งจับจากชื่ออย่างเดียวและติดป้าย "Name only"), foreign key ที่ชี้มาที่ตาราง, trigger ของตาราง, synonym และ default / check constraint (แสดงเป็นตารางเจ้าของ) · user-defined type ดูจากคอลัมน์ / parameter ที่ใช้ type นั้น และ module ที่ประกาศตัวแปรชนิดนั้น
  - **คอลัมน์** SQL Server เก็บ dependency ระดับคอลัมน์เฉพาะ object แบบ schema-bound จึงใช้วิธีหา object ที่อ้างตารางเจ้าของ แล้วค้นชื่อคอลัมน์ใน definition (แสดงเป็น "Text match" คอลัมน์ชื่อเดียวกันของตารางอื่นอาจติดมาด้วย)
  - ไม่พบ: dynamic SQL, การอ้างจาก database / server อื่น และจากโปรแกรมที่ต่อเข้ามา
- **Generate CRUD procedures (หน้าต่าง Search):** คลิกขวาที่ตารางในผลลัพธ์ › **Generate CRUD procedures…** เลือกว่าจะสร้าง `Select` / `Insert` / `Update` / `Delete` ตั้งรูปแบบชื่อได้ (ค่าเริ่มต้น `usp_{Table}_{Action}`, ใช้ `{Schema}` `{Table}` `{Action}`) และเลือก `CREATE OR ALTER` หรือ `CREATE` ดู script ตัวอย่างก่อนส่งไปที่หน้าต่าง query ใหม่ / clipboard / ไฟล์ (มี `USE [database]` นำหน้า) SqlAssist ไม่รัน script ให้
  - Insert ข้ามคอลัมน์ identity / computed / rowversion และคืนค่า identity ผ่าน `OUTPUT` parameter (`SCOPE_IDENTITY()`) · Select รับ primary key เป็น parameter แบบ `= NULL` (ส่ง NULL = ได้ทุกแถว) · Update / Delete ใช้ primary key (รองรับ Composite Key) และ Update ไม่มีค่าเริ่มต้นของ parameter เพื่อไม่ให้ลืมส่งแล้วกลายเป็น NULL
  - ตารางที่ไม่มี primary key (หรือมีแต่ key ไม่มีคอลัมน์ให้ SET) จะไม่ได้ Update / Delete และมี comment `-- Skipped ...` บอกเหตุผลบนสุดของ script ไม่เดา key ให้
  - รูปแบบชื่อและตัวเลือก `CREATE OR ALTER` ถูกจำใน `settings.xml` (`<Crud NamePattern="..." CreateOrAlter="true|false"/>`) · `CREATE OR ALTER` ต้องใช้ SQL Server 2016 SP1 ขึ้นไป
- **Function Parameter Hints:** พิมพ์ `(` หลังชื่อฟังก์ชันที่รู้จัก หรือคลิก/เลื่อน caret เข้าไปในวงเล็บของฟังก์ชันที่เขียนไว้แล้ว จะมี tooltip ใกล้ caret แสดงรูปแบบการเรียก เช่น `SUBSTRING(expression, start, length)` โดยทำ **ตัวหนา** ที่ parameter ตรงกับตำแหน่ง caret
  และเปลี่ยนตัวหนาเองเมื่อพิมพ์ `,` หรือเลื่อน caret (ไม่ต้องพิมพ์ใหม่) ใช้ tooltip ของ editor เดิม ไม่แย่ง focus ปรับตำแหน่งตามพื้นที่ editor ตามธีม Light/Dark และ DPI
  - นับเฉพาะ comma ที่แบ่ง argument ของ call นั้นเอง: comma ใน string (รวม `N'O''Brien, Smith'`), comment, วงเล็บซ้อน และ call ชั้นในไม่ถูกนับ
    ฟังก์ชันซ้อนแสดงของ call ชั้นในสุดที่ caret อยู่ พอ caret ออกจาก call ชั้นใน (เช่นอยู่ที่ `N','` ใน `REPLACE(SUBSTRING(…), N',', N' ')`) จะกลับไปแสดง `REPLACE` โดยเน้น argument ที่สอง
  - วงเล็บของ `IN (...)`, `VALUES (...)`, `OVER (...)` และวงเล็บจัดกลุ่ม expression ไม่ถูกมองเป็น function call (มองทะลุไปยัง call ที่ล้อมอยู่ ถ้ามี) วงเล็บของ subquery / derived table เป็นกำแพง caret อยู่ข้างในจะไม่แสดง hint ของ call ที่อยู่นอก subquery
    ชื่อตารางหรือ procedure ที่ตามด้วยวงเล็บ (`INSERT INTO t (a, b)`, `CREATE FUNCTION f (@a int)`) ไม่ใช่ call
  - ฟังก์ชัน built-in: `SUBSTRING`, `REPLACE`, `CHARINDEX`, `PATINDEX`, `LEFT`, `RIGHT`, `LEN`, `UPPER`, `LOWER`, `LTRIM`, `RTRIM`, `REVERSE`, `REPLICATE`, `STUFF`, `CONCAT`, `CONCAT_WS`, `FORMAT`, `ISNULL`, `COALESCE`, `NULLIF`, `IIF`, `CHOOSE`,
    `DATEADD`, `DATEDIFF`, `DATEDIFF_BIG`, `DATEPART`, `DATENAME`, `YEAR`, `MONTH`, `DAY`, `EOMONTH`, `DATEFROMPARTS`, `ROUND`, `ABS`, `CEILING`, `FLOOR`, `POWER`, `ISNUMERIC`, `ISDATE`, `CONVERT`, `TRY_CONVERT`
    ชื่อและลำดับ parameter ตาม syntax ใน T-SQL reference รองรับ optional (`[, start_location]`) และ variadic (`COALESCE(expression [, ...n])`, `CONCAT(…, argument2 [, argumentN]...)`) ฟังก์ชันที่ไม่อยู่ในตารางจะไม่แสดง hint (ไม่เดา signature) และไม่เลือก overload ตามชนิดของ argument
  - ฟังก์ชันที่ syntax ไม่ใช่รายการคั่นด้วย comma (`CAST(x AS type)`, `TRY_CAST`, `TRIM(chars FROM s)`, `PARSE`, `STRING_AGG` ฯลฯ) ข้ามอย่างชัดเจน ไม่ใช้กติกา comma ทั่วไป
  - scalar / table-valued function ของผู้ใช้ที่ระบุ schema (`dbo.fnTax(`, `[dbo].[fnTax](`) ใช้ชื่อ parameter และชนิดข้อมูลจาก schema ที่โหลดไว้แล้ว (ไม่รวม return value; แสดง MS_Description และชนิดที่ return) ไม่ query ฐานข้อมูลเพิ่มทุกครั้งที่พิมพ์หรือขยับ caret (ใช้ schema cache เดิม: โหลดตามปกติครั้งเดียวต่อ database ทุก 10 นาทีเหมือนรายการแนะนำ ชื่อที่มี schema นำหน้าก่อนวงเล็บ เช่น `FROM dbo.Orders (` ก็อาจเป็นตัวเริ่มโหลดครั้งแรกนั้น)
    ถ้า schema ยังไม่พร้อมหรือไม่พบ function จะไม่แสดง hint รุ่นนี้ไม่รวม `EXEC` stored procedure
  - SQL ที่ยังพิมพ์ไม่ครบ (วงเล็บยังไม่ปิด, argument ว่าง) แสดงเฉพาะที่ระบุได้แน่นอน วงเล็บที่ค้างไว้ไม่ลามไปยัง statement ถัดไป (`;`, `GO`, หรือคำสั่ง `SELECT` / `INSERT` / `UPDATE` ... ที่โผล่ในวงเล็บของ function)
  - กด **Esc** เพื่อปิด (ไม่แก้ข้อความ ไม่เปลี่ยน selection ไม่เพิ่ม Undo) จะไม่เปิดซ้ำจนกว่า caret จะออกจาก call นั้นแล้วเข้าไปใหม่ ไม่ดักปุ่ม Tab / Enter / comma และไม่แตะ snippet, INSERT / UPDATE / EXEC expansion, ไฮไลต์คอลัมน์ INSERT
    hint ซ่อนเองเมื่อรายการแนะนำ (completion) หรือ tooltip ตอนชี้เมาส์เปิดอยู่ หรือเมื่อ tooltip "Press Tab to …" ของการขยายกำลังแสดง แล้วกลับมาเองเมื่อปิด
  - อ่านเฉพาะข้อความก่อน caret (ไม่เกิน 8,000 ตัวอักษร) ไม่ parse เอกสารทั้งฉบับ ตรวจใหม่หลัง caret หรือข้อความนิ่ง ~0.3 วินาที ถอด event / timer และยกเลิกงานที่ค้างเมื่อปิด editor
  - เปิด/ปิดได้ที่ **Tools › SqlAssist › Options… › Editor** (`<Editor ShowParameterHints="true|false"/>` ใน `settings.xml`, เปิดเป็นค่าเริ่มต้น)

## [1.5.4] - 2026-11-05

### เปลี่ยน
- เอาฟังก์ชัน **CSV to SQL** ออก (เมนู **Tools › SqlAssist › CSV to SQL...** และโค้ดที่เกี่ยวข้องทั้งหมด) Generate Script As จาก results grid ยังใช้ได้เหมือนเดิม
- รายการแนะนำแบบต้นไม้ตาราง (`SELECT |`): ไม่แสดงบรรทัดคำอธิบายใต้รายการแล้ว (ชนิดข้อมูลแสดงอยู่ในแถวอยู่แล้ว) รายการแนะนำแบบปกติยังแสดงเหมือนเดิม

### แก้ไข
- รายการแนะนำแบบต้นไม้ตาราง (`SELECT |`): คลิก checkbox หรือปุ่มขยายของแถวที่ยังไม่ได้เลือก ต้องคลิก 2 ครั้งถึงจะติก ตอนนี้คลิกครั้งเดียวก็ติก

## [1.5.3] - 2026-11-05

### เพิ่ม

- **`GROUP BY` + Tab:** พิมพ์ `GROUP BY ` (caret ต่อท้าย `BY` หรือช่องว่าง และยังไม่มี expression ตามหลัง) แล้วกด Tab จะเติมรายการ grouping expression จาก `SELECT` ของ query block เดียวกันทันที ไม่มี preview หรือกล่องยืนยัน
  มี hint "Press Tab to insert N grouping expressions" ใต้ `GROUP BY` เฉพาะเมื่อ Tab ขยายได้จริง (hint กับ Tab เรียกการตรวจเดียวกันใน Core (`GroupByExpander.Find`) และ Tab ตรวจข้อความปัจจุบันใหม่ทุกครั้ง)
  - เรียงตามลำดับใน SELECT ตัด alias ออกทุกแบบ (`expr AS a`, `expr a`, `a = expr`) ได้ expression ต้นฉบับ เช่น `YEAR(o.OrderDate) AS OrderYear` → `YEAR(o.OrderDate)`
    รองรับคอลัมน์, scalar function, `CASE`, `CONVERT` / `CAST` / `DATEADD` ฯลฯ ข้ามค่าคงที่ ตัวแปร และ aggregate (`COUNT(*)`, `SUM(x)`, `COALESCE(SUM(x), 0)`) SELECT ที่ไม่มี aggregate ก็ใช้ได้
  - รายการที่ซ้ำกันทุก token (เทียบตัวอักษรตรงตัว ไม่ตัดช่องว่างหรือเปลี่ยนตัวพิมพ์ทั้งก้อน) จะเติมครั้งเดียว
  - อ่านเฉพาะ SELECT ที่เป็นเจ้าของ `GROUP BY` นั้น (comma ใน string / function / วงเล็บซ้อน / comment ไม่ถูกนับเป็นตัวแบ่ง; SELECT ใน CTE, derived table, subquery ไม่ถูกหยิบมา)
  - เติมเฉพาะรายการหลัง `GROUP BY` (edit ครั้งเดียว Ctrl+Z ครั้งเดียวย้อนทั้งชุด) caret อยู่ท้าย expression สุดท้าย `HAVING` / `ORDER BY` / `OPTION` / semicolon ที่มีอยู่เดิมไม่ถูกแตะและไม่เติม semicolon เพิ่ม
    จัดบรรทัดตาม indent และตำแหน่ง comma ที่ตั้งไว้ใน Format SQL (ค่าตั้งต้นคือ comma นำหน้า)
  - ไม่ขยาย (Tab ทำงานตามปกติ ไม่มี hint): มี expression หลัง `GROUP BY` แล้ว, `SELECT *` / `alias.*`, window function (`OVER`), `UNION` / `EXCEPT` / `INTERSECT`, `ROLLUP` / `CUBE` / `GROUPING SETS` / `GROUPING()`,
    subquery ใน select item, SQL ที่ยังไม่ครบ, ฟังก์ชันที่ไม่รู้ว่าเป็น scalar หรือ aggregate (รวมฟังก์ชันของผู้ใช้ `dbo.f(...)`), คอลัมน์ปนกับ aggregate (`o.Price * SUM(o.Qty)`) และเมื่อไม่มีรายการเหลือให้เติม
  - ไม่ต่อฐานข้อมูลและไม่ต้องรอ schema ถ้ารายการแนะนำคอลัมน์เปิดอยู่ Tab ครั้งแรกจะเลือกรายการตามเดิม (กด Esc ก่อนถ้าต้องการขยาย)

- **ไฮไลต์คอลัมน์และรายการที่ตรงกันใน `INSERT … SELECT`:** ต่อยอดจากไฮไลต์ของ `INSERT … VALUES` ใช้สีและกรอบชุดเดิม (ปรับตามธีม Light/Dark เหมือนเดิม) ทำงานเองเมื่อคลิกหรือเลื่อน caret ไม่ต้องกดคำสั่ง
  - caret ที่ชื่อคอลัมน์ในรายการ `INSERT INTO t (a, b, …)` → กรอบสีฟ้ารอบ select item ที่อยู่ตำแหน่งเดียวกัน (ทั้งก้อน เช่น `CONCAT(…)`)
    caret อยู่ที่ใดก็ได้ภายใน select item → แรเงาคอลัมน์ปลายทางที่ตรงกัน
  - select item ที่มี alias (`expr AS a`, `a = expr`, `expr a`) ไฮไลต์ทั้งก้อนรวม alias
  - จับคู่ตามลำดับตำแหน่ง ไม่ได้ดูจากชื่อหรือข้อความที่เหมือนกัน และไม่ต่อฐานข้อมูล
  - อ่านเฉพาะรายการ SELECT ชั้นนอกที่ป้อนข้อมูลเข้า INSERT (รองรับ `DISTINCT`, `ALL`, `TOP n` / `TOP (n) [PERCENT] [WITH TIES]`, ชื่อ `[...]` และ schema, comment, หลายบรรทัด):
    comma ใน string, ฟังก์ชัน, `CASE`, วงเล็บซ้อนและ subquery ไม่ถูกนับเป็นตัวแบ่ง และ SELECT ภายใน CTE, derived table หรือ subquery ไม่ถูกสับสนกับ SELECT ของ INSERT
  - รุ่นนี้ไม่ขยาย `*` / `alias.*`: จับคู่เฉพาะรายการที่อยู่ก่อน `*` ตัวแรก (ตัว `*` และรายการหลังจากนั้นไม่ไฮไลต์เพราะไม่รู้ตำแหน่งแน่นอน)
    ไม่รองรับ `UNION` / `EXCEPT` / `INTERSECT`, `INSERT … EXEC`, `OUTPUT` ก่อน SELECT และ `(SELECT …)` ที่ครอบด้วยวงเล็บ — กรณีเหล่านี้ล้างไฮไลต์แทนการเดา
  - ถ้า SQL ยังพิมพ์ไม่ครบหรือจำนวนคอลัมน์กับ select item ไม่เท่ากัน จะแสดงเฉพาะคู่ที่ยืนยันตำแหน่งได้
  - เปิด/ปิดด้วยตัวเลือกเดียวกับ `INSERT … VALUES` ที่ Tools › SqlAssist › Options… › Editor

### เปลี่ยน

- ภายใน: ตัวอ่านรายการ SELECT (`SelectListReader`) ถูกแยกออกมาใช้ร่วมกันระหว่างไฮไลต์ `INSERT … SELECT` กับ `GROUP BY` + Tab ไม่เปลี่ยนพฤติกรรมของไฮไลต์เดิม

## [1.5.2] - 2026-10-30

### เพิ่ม

- **ข้อความแนะนำ Tab ใต้ชื่อตารางหลัง `INSERT INTO t` และ `UPDATE t`:** เหมือนที่มีอยู่แล้วใต้ `*` และ `EXEC p` จะขึ้น
  "Press Tab to insert columns and VALUES" / "Press Tab to insert SET and WHERE" เฉพาะเมื่อ Tab ขยายได้จริง (ตารางมีในฐานข้อมูล
  มีคอลัมน์ให้เติม และยังไม่ได้เขียนรายชื่อคอลัมน์ / `VALUES` / `SET` ต่อท้าย)
- **ข้อความแนะนำ Tab หลัง `ALTER PROCEDURE p` / `ALTER FUNCTION f` / `ALTER VIEW v`:** ขึ้น "Press Tab to insert the current definition"
  เมื่อ SqlAssist รู้จักอ็อบเจกต์นั้น (ตรวจจาก schema ที่โหลดไว้ ไม่ได้ต่อ server เพิ่มทุกครั้งที่ขยับ caret) `ALTER TRIGGER` ยังไม่มีข้อความนี้
  และ procedure ที่เข้ารหัสไว้ซึ่งอ่านนิยามไม่ได้ จะยังเห็นข้อความแต่ Tab ไม่ทำอะไร

## [1.5.1] - 2026-10-30

### เพิ่ม

- **ไฮไลต์คอลัมน์และค่าที่ตรงกันใน `INSERT … VALUES`:** วาง caret (คลิกหรือใช้ลูกศร) ที่ค่าใน `VALUES (…)` แล้วชื่อคอลัมน์ในรายการ `INSERT INTO t (a, b, …)` ที่อยู่ตำแหน่งเดียวกันจะถูกแรเงา
  และค่านั้นมีกรอบสีฟ้า (ใช้ได้กับ VALUES หลายแถว อ้างอิงแถวที่ caret อยู่) ถ้า caret อยู่ที่ชื่อคอลัมน์ จะแรเงาค่าตำแหน่งเดียวกันในทุกแถว
  - จับคู่ตามลำดับตำแหน่ง ไม่ได้ค้นหาจากข้อความ และไม่ต่อฐานข้อมูล: comma ใน string (`N'O''Brien, X'`), ในฟังก์ชัน (`CONVERT(datetime, '…', 121)`), ในวงเล็บซ้อน/subquery และใน comment ไม่ถูกนับเป็นตัวแบ่ง
  - เฉพาะ INSERT ที่ caret อยู่ (ในสคริปต์ที่มีหลาย INSERT) และเฉพาะรูปแบบที่ระบุรายชื่อคอลัมน์ + `VALUES` (ไม่รวม `INSERT … SELECT` / `EXEC` / `DEFAULT VALUES`)
  - ถ้า SQL ยังพิมพ์ไม่ครบ หรือจำนวนคอลัมน์กับค่าไม่เท่ากัน จะแสดงเฉพาะคู่ที่ชัดเจน ไม่เดา
  - ไม่แก้ข้อความ ไม่แตะ selection และไม่เพิ่มรายการ Undo ปิดได้ที่ Tools › SqlAssist › Options… › Editor

## [1.5.0] - 2026-10-30

### เปลี่ยน

- **Tab หลัง `INSERT INTO ชื่อตาราง` และ `UPDATE ชื่อตาราง` เขียนใหม่ให้อ่านง่ายขึ้น:**
  - `INSERT` ขยายเป็นรายชื่อคอลัมน์ `( … )` ตามด้วย `VALUES ( … )` ลูกน้ำอยู่ท้ายบรรทัด ค่าตั้งต้นเรียงตรงกับคอลัมน์ (ตัวเลข `0`, ข้อความ `N''`, วันที่ `GETDATE()` ฯลฯ)
    ข้ามคอลัมน์ IDENTITY, computed และ rowversion เคอร์เซอร์อยู่ที่ค่าแรก เอาคอมเมนต์ชนิดข้อมูลท้ายบรรทัดออกแล้ว
  - `UPDATE` ขยายเป็น `SET` (ไม่ใส่ Primary Key) และ `WHERE` ตาม Primary Key ที่ใช้ `@parameter` ชื่อเดียวกับคอลัมน์ ถ้าเป็น Composite Key เชื่อมด้วย `AND`
    ระบบใส่ `DECLARE @Id <ชนิดจริง> = NULL;` ไว้บนสุดให้กรอกค่า (ถ้า batch มี `@Id` ประกาศไว้แล้วจะไม่ประกาศซ้ำ)
    ตารางที่ไม่มี Primary Key จะได้ `WHERE /* TODO: ... */ ;` ซึ่งรันไม่ได้จนกว่าจะเขียนเงื่อนไขเอง ไม่มี UPDATE ที่ไม่มี WHERE
  - ไม่ขยายซ้ำเมื่อบรรทัดถัดไปมี `(รายชื่อคอลัมน์)`, `VALUES`, `SELECT`, `DEFAULT VALUES` (ของ INSERT) หรือ `SET` (ของ UPDATE) เขียนไว้แล้ว
  - ถ้าอ่าน schema ไม่สำเร็จ จะบันทึกลง log แล้ว Tab ทำงานตามปกติ ไม่แก้ข้อความใน editor

- ย้าย repo ไปอยู่ใน organization `kriangdet-chuaboonjun` แล้ว การเช็กและดาวน์โหลดอัปเดตจึงไปที่
  `github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon` ใครใช้เวอร์ชัน 1.4.3 หรือเก่ากว่า ระบบยังแจ้งเตือนเวอร์ชันใหม่ได้
  แต่จะดาวน์โหลดและติดตั้งให้อัตโนมัติไม่ได้ ต้องโหลด `SqlAssistSetup-*.exe` จากหน้า Releases มาติดตั้งเองหนึ่งครั้ง

## [1.4.3] - 2026-10-26

### แก้ไข
- **function ในตัวของ SQL Server ขึ้นในรายการ completion แล้ว:** พิมพ์ `rep` จะเห็น `REPLACE` (รวมถึง `SUBSTRING`, `CHARINDEX`, `LEN`, `DATEPART`, `FORMAT`, `STRING_AGG`, `IIF` ฯลฯ) เดิมรายการมีแค่บางตัว เช่น `COUNT`, `CAST`, `GETDATE`

## [1.4.2] - 2026-10-25

### เพิ่ม
- **คอลัมน์ของ table-valued function:** `FROM dbo.fnOrders(1) f` แล้วพิมพ์ `f.` จะขึ้นคอลัมน์ที่ function คืนมา `SELECT *` + Tab ขยายคอลัมน์ได้ และชี้เมาส์ที่ชื่อ function จะเห็นคอลัมน์ทั้งหมดพร้อมข้อความ "returns TABLE" (ใช้กับ `CROSS APPLY` / `OUTER APPLY` ได้ด้วย)
- **ตามคำสั่ง `USE`:** ถ้าในสคริปต์มี `USE ชื่อdb` อยู่เหนือ caret รายการ completion, `SELECT *` + Tab, การขยาย statement ด้วย Tab และการชี้เมาส์ จะใช้ตารางของ database นั้น ไม่ต้องรันสคริปต์หรือเปลี่ยน dropdown ก่อน
- **ชี้เมาส์ที่คอลัมน์และ procedure / function:** ชี้ที่ `wr.ReqId` หรือ `ReqId` จะเห็นชนิดข้อมูล, primary key / foreign key และคำอธิบาย (MS_Description) ชี้ที่ชื่อหลัง `EXEC` หรือ function ที่เรียกอยู่ (`dbo.fnTax(...)`) จะเห็น parameter ทุกตัว (เดิมชี้ได้แค่ชื่อตารางกับ alias)
- **F12 Go to Definition:** วาง caret ที่ชื่อ object แล้วกด F12 (หรือคลิกขวา › Go To Definition) ตารางจะเปิดใน Object Explorer ส่วน view / procedure / function จะเปิด script ในหน้าต่าง query ใหม่ที่ขึ้นต้นด้วย `USE [database]` (ไม่รันให้) กดแล้วไม่ต้องรอ server: อ่าน script เบื้องหลังและแจ้งผลที่ status bar
- **Search หา index, constraint และ user-defined type:** เมนูชนิด object มี Index, Constraint (primary key, unique, foreign key, check, default) และ User-defined type (alias type, table type) เพิ่มขึ้นมา ค้นได้จากชื่อ preview เป็น script แบบย่อ และกดไปที่ Object Explorer ได้ (ถ้าหา node ของตัวมันไม่เจอจะไปที่ตารางเจ้าของ) คอลัมน์ Type บอกชนิดย่อยด้วย เช่น "Constraint (Check)"

## [1.4.1] - 2026-10-25

### แก้ไข
- รายการ completion: คลิกเมาส์ที่รายการเพื่อเลือกได้แล้ว (เดิมเลือกได้แค่ด้วยลูกศร) ดับเบิลคลิกเพื่อใส่รายการนั้นลงในสคริปต์
- SSMS ค้างหรือดับทั้งโปรแกรมได้ ถ้าโหลด schema ของ database ไม่สำเร็จ (เช่น server เข้าไม่ได้ login ไม่ผ่าน หรือ query อ่านโครงสร้างล้มเหลว) แล้ววาง caret ไว้ที่ `SELECT *` หรือ `EXEC ชื่อ_procedure` เกินเสี้ยววินาที ตัวที่แสดงข้อความ "Press Tab to expand..." วนตรวจ schema ซ้ำไม่รู้จบจน SSMS หยุดทำงาน ตอนนี้ถ้า schema ยังไม่มีจะไม่แสดงข้อความนั้น แล้วลองใหม่เมื่อ caret ขยับหรือพิมพ์เพิ่ม (โหลดสำเร็จเมื่อไรก็แสดงตามปกติ)
- `snippets.xml` ไม่หายง่ายอีก
  - เขียนไฟล์ผ่านไฟล์ชั่วคราวแล้วค่อยสลับเข้าแทน ถ้า disk เต็มหรือ SSMS ถูกปิดกลางคัน ไฟล์เดิมยังอยู่ ไม่ถูกตัดเหลือว่างหรือขาดครึ่ง (รวมถึงตอนที่ SqlAssist เพิ่ม snippet เริ่มต้นตัวใหม่ให้เอง)
  - ถ้าเปิด Snippets Manager แล้วอ่าน `snippets.xml` ไม่ได้ (เช่นพิมพ์ XML ผิดตอนแก้ด้วยมือ) หน้าจะว่าง และการบันทึกจะแทนที่ไฟล์ด้วยรายการในหน้า ตอนนี้เก็บสำเนาไฟล์เดิมไว้เป็น `snippets.xml.unreadable` และบอกที่อยู่ในกล่องข้อความ
- พิมพ์ตัวอักษรแล้วเกิดข้อผิดพลาดตอนเปิดรายการ completion ไม่ทำให้ตัวอักษรนั้นถูกพิมพ์ซ้ำสองตัวในสคริปต์อีก (ข้อผิดพลาดบันทึกลง `SqlAssist.log` เหมือนเดิม)
- กด Ctrl+J บน results grid แล้วคลิกกลับไปที่ query editor ตัวอักษรถัดไปที่พิมพ์ (`I` `T` `W` `O` `U`) ไม่ถูกกลืนหรือสร้าง script ใส่ clipboard ผิดที่อีก
- หน้าต่าง Search: ถ้าสลับ server ระหว่างที่กำลังโหลดรายชื่อ database ของ server เดิม เมนู Databases และการค้นหา "All databases" ของ server ใหม่ไม่เอารายชื่อของ server เดิมมาใช้แล้ว
- ถ้าเกิดข้อผิดพลาดตอนกรองรายการ completion ขณะพิมพ์ รายการจะว่างและบันทึกลง `SqlAssist.log` แทนที่จะปล่อยให้ข้อผิดพลาดหลุดเข้า SSMS
- Generate Script As จาก results grid: ถ้า SSMS รุ่นใหม่เปลี่ยนวิธีอ่านเซลล์ที่เลือกอยู่ จะขอให้เลือกเซลล์ แทนที่จะสร้าง script จากแถวแรกของ grid โดยไม่บอก
- ตัวติดตั้ง: ข้อความใน `SqlAssist.log` ตอนพบว่ามีตัวรอติดตั้งอยู่แล้ว (`/installonexit`) ไม่ระบุเวอร์ชันผิดตัวอีก
- **Install when SSMS closes:** ถ้าหน้าต่าง "กำลังอัปเดต SqlAssist..." ถูกปิดโดยไม่ใช่ผู้ใช้กดปิดเอง (เช่น End task ใน Task Manager) ระหว่างติดตั้ง การติดตั้งไม่ถูกตัดกลางทางอีก ตัวติดตั้งรอให้ติดตั้งจบและเขียนผลลัพธ์ให้ SSMS รอบถัดไปแจ้งตามปกติ (เดิมโปรแกรมจบทั้งที่ยังไม่เสร็จ และบอกว่าสำเร็จ ถ้าถูกตัดตรงช่วงถอดตัวเก่ากับลงตัวใหม่ SqlAssist จะหายไปจาก SSMS เงียบ ๆ) ส่วนตอนปิดเครื่อง Windows ยังบังคับปิดโปรแกรมได้เองเหมือนเดิม

## [1.4.0] - 2026-10-23

### เปลี่ยน
- **โลโก้และไอคอนใหม่ทั้งหมด:** ใช้รูปฐานข้อมูลคู่กับเครื่องหมาย `<>` และสายฟ้าแทนของเดิม ในทุกที่ที่ SqlAssist แสดงโลโก้
  - ไอคอนของ extension ในหน้า Extensions ของ SSMS
  - ปุ่ม **Search** บน toolbar ของ SSMS (มีให้ครบ 16 / 24 / 32 / 48 px ตามสเกลหน้าจอ)
  - ไอคอนของไฟล์ติดตั้ง `SqlAssistSetup-*.exe` ไอคอนหน้าต่างติดตั้ง และไอคอนในรายการ Programs and Features
  - โลโก้มุมซ้ายบนของหน้าต่างติดตั้ง
- ตัวติดตั้งล้างแคชรูปภาพของ SSMS ให้เองตอนติดตั้ง ไอคอนบน toolbar จึงเปลี่ยนทันทีเมื่อเปิด SSMS ครั้งถัดไป (ถ้ายังเป็นรูปเดิม ให้รัน `clear-ssms-cache.ps1`)

## [1.3.9] - 2026-10-21

### เปลี่ยน
- กด **Install now** หรือ **Install when SSMS closes** ที่แถบอัปเดตแล้วไม่เด้งกล่องข้อความยืนยันอีก **Install now** เปิดหน้าต่าง setup ซึ่งบอกให้ปิด SSMS อยู่แล้ว ส่วน **Install when SSMS closes** แสดงที่ status bar ว่า "will be installed when SSMS closes" แล้วมีหน้าต่างความคืบหน้าขึ้นเองหลังปิด SSMS ครบ (ถ้าโหลดไม่สำเร็จยังแจ้งเป็นกล่องข้อความเหมือนเดิม)

## [1.3.8] - 2026-10-21

### เพิ่ม
- **Hint ของ EXEC:** พิมพ์ `EXEC ชื่อ_procedure` แล้วหยุด caret ไว้ติดท้ายชื่อ จะขึ้นข้อความ "Press **Tab** to insert list of parameters" ใต้ชื่อ (เหมือน "Press Tab to expand N columns" ของ `*`) กด Tab ใส่รายการ parameter ให้ตามเดิม
  - ขึ้นเฉพาะตอนที่ Tab ทำได้จริง: procedure ต้องอยู่ใน database ที่ต่ออยู่ และมี parameter อย่างน้อย 1 ตัว (ไม่ขึ้นกับ function, procedure ที่ไม่มี parameter หรือชื่อที่ยังหาไม่เจอ)
  - ถ้ามีเว้นวรรคต่อท้ายชื่อ หรือ caret ไม่ได้อยู่ท้ายชื่อ จะไม่ขึ้น และ Tab เป็น Tab ปกติเหมือนเดิม

### แก้ไข
- **Install when SSMS closes** เงียบเกินไป: หลังปิด SSMS ครบ ตัวติดตั้งที่รออยู่เบื้องหลังไม่แสดงอะไรและไม่เขียน log เลยจนกว่าจะเปิด SSMS รอบถัดไป ดูเหมือนว่าไม่ได้อัปเดต (ทั้งที่ติดตั้งอยู่ และถ้าเปิด SSMS กลับมากลางคันก็ขัดการติดตั้งได้)
  - เมื่อปิด SSMS ครบ จะมีหน้าต่างเล็ก "กำลังอัปเดต SqlAssist..." บอกให้อย่าเปิด SSMS จนกว่าจะเสร็จ แล้วเปลี่ยนเป็น "อัปเดตเรียบร้อยแล้ว" (มีปุ่ม เปิด SSMS) หรือบอกสาเหตุและวิธีติดตั้งใหม่ถ้าไม่สำเร็จ
  - ตัวที่รออยู่เขียนลง `SqlAssist.log` ด้วย: เริ่มรอ, SSMS ปิดแล้วเริ่มติดตั้ง, ติดตั้งเสร็จหรือล้มเหลวพร้อมเวลาที่ใช้ ช่วงระหว่างปิดกับเปิด SSMS จึงไม่ว่างอีก

## [1.3.6] - 2026-10-21

### เพิ่ม
- **Generate Script As** จาก results grid: เลือก cell ในผลลัพธ์ แล้วคลิกขวา › **Generate Script As** › เลือกชนิด statement › ส่งไปที่ **New Query Window**, **Clipboard** หรือ **File...**
  - **INSERT**: `INSERT INTO [Table] (...) VALUES (...), (...)` (แบ่งทุก 1,000 แถว)
  - **INSERT #tmpTable**: `CREATE TABLE #tmpTable` ตามชนิดข้อมูลของคอลัมน์ + INSERT + `SELECT * FROM #tmpTable` + `DROP TABLE`
  - **WHERE IN()**: `WHERE col IN(...)` (ค่าไม่ซ้ำ, ค่า NULL กลายเป็น `IS NULL`)
  - **WHERE AND OR**: `WHERE (a = 1 AND b = N'x') OR (...)` หนึ่งกลุ่มต่อแถว
  - **UPDATE** (และ Ctrl+J, U): เปิดหน้าต่างให้กำหนดเอง ไม่เดาจากผลลัพธ์: พิมพ์ชื่อตารางปลายทาง (ระบุ schema ได้ ระบบตรวจและ quote ชื่อให้), ติ๊กคอลัมน์ **SET** และคอลัมน์ **Key** (หลายคอลัมน์ต่อด้วย `AND`, Key ที่เป็น NULL เขียนเป็น `IS NULL`) แล้วดูตัวอย่าง script ก่อนส่งไป New Query Window / Clipboard / File
    - ช่องชื่อตารางเริ่มว่าง Key เริ่มต้นไม่เลือกอะไร ถ้ายังไม่ได้เลือก Key หรือไม่มีคอลัมน์ SET จะสร้าง script ไม่ได้
    - ติ๊กคอลัมน์เป็น Key แล้วระบบเอาออกจาก SET ให้ก่อน (ติ๊ก SET กลับเองได้)
    - ถ้าแถวที่เลือกมี Key ซ้ำกัน จะเตือนว่าหลายแถวใช้เงื่อนไข `WHERE` เดียวกัน และแจ้งว่า Key ที่เลือกไม่รับประกันว่า unique ในฐานข้อมูลจริง
- ปุ่มลัดในผลลัพธ์ (copy ลง clipboard): **Ctrl+J** แล้วตามด้วย **I** (INSERT), **T** (INSERT #tmpTable), **W** (WHERE IN), **O** (WHERE AND OR), **U** (UPDATE) ใช้ได้เฉพาะตอนที่ results grid ถูกเลือกอยู่ ส่วน Ctrl+J ใน editor ยังเป็น List Members เหมือนเดิม **ยังไม่ได้ยืนยันว่าปุ่มลัดนี้ทำงานใน SSMS 22** (ตอนทดสอบกดแล้วไม่ตอบสนอง ยังไม่ทราบสาเหตุ) ถ้าไม่ตอบสนองให้ใช้เมนูคลิกขวา › Generate Script As แทน
- **Format SQL** (**Ctrl+K, Ctrl+Shift+F**, Tools › SqlAssist › Format SQL หรือ **คลิกขวาใน query editor › Format SQL (SqlAssist)**): จัดรูปแบบ SQL ที่เลือก หรือทั้งหน้า query ถ้าไม่ได้เลือก
  - จัด SELECT / JOIN / WHERE / GROUP BY / ORDER BY / UNION / CASE / CTE / subquery / INSERT / UPDATE / DELETE / MERGE และย่อหน้าของ IF / ELSE / WHILE / BEGIN…END / TRY…CATCH ส่วน statement อื่นจัดช่องว่างและตัวพิมพ์ keyword โดยคงการขึ้นบรรทัดเดิม
  - รักษา comment, ข้อความใน `'...'`, ชื่อใน `[...]`, hint และ `GO` เปลี่ยนเฉพาะช่องว่าง ตัวพิมพ์ของ keyword (คำสงวนและชื่อฟังก์ชัน/ชนิดข้อมูลที่ตำแหน่งชัดเจน ไม่แตะชื่อตาราง/คอลัมน์/alias) และตำแหน่งเครื่องหมาย `,`
  - Undo ครั้งเดียวย้อนทั้งชุด เคอร์เซอร์/selection อยู่กับข้อความเดิม จัดซ้ำได้ผลเดิม
  - ตรวจ token และ comment ทุกตัวหลังจัดเสร็จ ถ้า SQL ไม่สมบูรณ์ (string / comment / วงเล็บไม่ปิด) หรือผลไม่ตรงกับต้นฉบับ จะแจ้งสาเหตุและไม่แก้ข้อความ
- **Tools › SqlAssist › Check SQL Risks...**: ตรวจ `UPDATE` / `DELETE` ที่ไม่มี `WHERE`, `TRUNCATE TABLE` และ `DROP TABLE` ในส่วนที่เลือก หรือทั้งหน้า query แล้วแสดงในหน้าต่างผลตรวจ (กฎ, บรรทัด/คอลัมน์, คำสั่ง, เหตุผล) คลิกแถวเพื่อกระโดดไปที่คำสั่งนั้น
  - อ่านเป็นคำสั่ง ไม่ค้นคำ: `WHERE` ใน subquery / CTE / ข้อความ / comment ไม่ถูกนับ รองรับ `UPDATE … FROM`, `DELETE … FROM` และ alias
  - ไม่แก้ SQL และไม่เติม `WHERE` ให้ ระบุชัดว่าการมี `WHERE` ไม่ได้รับประกันว่าปลอดภัย และตรวจ SQL ที่ประกอบเป็นข้อความ (`EXEC`, `sp_executesql`) ไม่ได้
  - เปิด/ปิดทีละกฎ และเลือกข้ามตาราง temp / table variable ได้ที่ Options
  - **เตือนก่อนกด Execute (ทดลอง ปิดอยู่เป็นค่าเริ่มต้น):** แสดง Server, Database และคำสั่งเสี่ยง ปุ่มเริ่มต้นคือยกเลิก ดักที่ระดับคำสั่ง `Query.Execute` ของ SSMS ลองแล้วใน SSMS 22 กับ F5, Ctrl+E, ปุ่ม Execute, เมนู Query › Execute และการเลือกบางส่วน แต่ **ไม่ใช่การรับประกัน** ว่าจะไม่มีคำสั่งเสี่ยงหลุดไปรัน (ช่องทางอื่นที่รัน query ที่ยังไม่ได้ลองอาจไม่ถูกดัก) ไม่แตะ Cancel Query
- **Tools › SqlAssist › Options...**: ตั้งค่าตัวพิมพ์ keyword (ใหญ่ / เล็ก / ตามที่พิมพ์), ย่อหน้า 2–8 ช่องว่าง และเครื่องหมาย `,` นำหน้าหรือท้ายบรรทัด พร้อมตัวอย่างสด และเลือกกฎของ Check SQL Risks เก็บที่ `%APPDATA%\SqlAssist\settings.xml` มีปุ่ม **เปิดไฟล์ Log** (ย้ายมาจากเมนู Open Log)
- **Tools › SqlAssist › CSV to SQL...**: วางข้อมูลที่คัดลอกจาก Excel หรือข้อความ CSV แล้วเลือกผลลัพธ์เป็น `IN (...)`, `VALUES (...), (...)` หรือ `CREATE TABLE #Temp` + `INSERT` พร้อมตัวอย่างที่แก้ได้ก่อนส่งไป Clipboard หรือ New Query Window (ไม่รัน SQL ให้ และไม่บันทึกเนื้อหา Clipboard ลง log)
  - เลือก **ตัวคั่น cell** ได้: Tab (ค่าเริ่มต้น สำหรับข้อมูลที่คัดลอกจาก Excel), คอมมา `,`, เซมิโคลอน `;` หรือไปป์ `|` ระบบไม่เดาตัวคั่นให้เอง เพราะคอลัมน์เดียวที่มี `,` ในข้อความจะถูกแยกผิด
  - รองรับ cell ที่ครอบด้วย `"` (มีตัวคั่น, ขึ้นบรรทัดใหม่ หรือ `""` อยู่ข้างใน) เลือกได้ว่าแถวแรกเป็นชื่อคอลัมน์หรือข้อมูล และตัดค่าซ้ำของ IN ได้
  - ชนิดข้อมูลของแต่ละคอลัมน์เลือกได้ ค่าเริ่มต้นเป็นข้อความ (nvarchar) เพื่อไม่ให้ `00123` กลายเป็น `123`
  - กติกา NULL แยกชัดเจน: cell ว่าง / ข้อความ `NULL` (ตัวพิมพ์ใหญ่ตรงตัวเท่านั้น) เลือกได้ว่าเป็น SQL NULL หรือข้อความ
  - ข้อมูลที่ไม่เข้ากับชนิดที่เลือกจะแจ้ง **แถวที่ N คอลัมน์ M** พร้อมค่าที่ผิด และไม่สร้าง SQL ตัวเลขและวันที่อ่านแบบเข้มงวดโดยไม่ขึ้นกับ regional settings ของเครื่อง
  - INSERT แบ่งก้อนละ 1,000 แถวด้วยตัวเขียนเดียวกับ Generate Script As จำกัดข้อมูลไม่เกิน 2,000,000 ตัวอักษร / 100,000 แถว / 256 คอลัมน์

### เปลี่ยน
- เมนู **Tools › SqlAssist › Open Log** ถูกเอาออก ไปเปิดไฟล์ log ได้จากปุ่ม **เปิดไฟล์ Log** ในหน้าต่าง Options (เปิดด้วยโปรแกรมที่ผูกกับไฟล์ `.log` ของ Windows)

## [1.3.3] - 2026-10-20

### เพิ่ม
- อัปเดตแบบ **Install when SSMS closes**: ดาวน์โหลดและตรวจ SHA-256 ไว้ก่อน แล้วรออยู่เบื้องหลัง พอปิด SSMS ครบทุกหน้าต่างจะติดตั้งให้เอง
  - เปิด SSMS ครั้งถัดไปจะแจ้งผล: สำเร็จจะขึ้นที่แถบสถานะ ไม่สำเร็จจะมีข้อความบอกสาเหตุ
  - ถ้า restart เครื่องก่อนได้ปิด SSMS ระบบจะตั้งรอใหม่ให้เอง
- ตัวติดตั้งรองรับคำสั่ง `SqlAssistSetup.exe /installonexit`

### เปลี่ยน
- ปุ่มในแถบแจ้งเตือนอัปเดต: **Install when SSMS closes** (ใหม่) และ **Install now** (เดิมชื่อ Download and install)
- การเช็กอัตโนมัติจะไม่เตือนซ้ำเรื่องเวอร์ชันที่ดาวน์โหลดไว้และรอติดตั้งอยู่

## [1.3.2] - 2026-10-20

### เพิ่ม
- หลัง `EXEC ` / `EXEC dbo.` / `EXEC @rc = ` รายการจะแนะนำ stored procedure และ function แทนตาราง
- ข้อมูล parameter ข้างรายการ: เช่น `dbo.ufnGetProductDealerPrice (Function) returns MONEY` พร้อม parameter ทุกตัว (ชนิดข้อมูล, INPUT / OUTPUT / READONLY) และคำอธิบาย (MS_Description)
- scalar function แนะนำในรายการทั่วไป ส่วน table-valued function แนะนำหลัง `FROM` / `JOIN`

## [1.3.1] - 2026-10-20

### เพิ่ม
- กด **Tab** หลังชื่อ object ที่ท้ายบรรทัดเพื่อขยายเป็น statement เต็ม:
  - `INSERT INTO t`: รายชื่อคอลัมน์และ `VALUES` พร้อมค่าตั้งต้นตามชนิดข้อมูล (ข้าม IDENTITY / computed / rowversion)
  - `UPDATE t`: `SET` ทุกคอลัมน์ และ `WHERE` ตาม primary key (ถ้าไม่มี PK จะเว้น `WHERE` ว่างไว้ให้กรอก)
  - `EXEC p`: `@param = ค่า` ครบทุกตัวเรียงตรงกัน ส่วน OUTPUT จะ `DECLARE` ตัวแปรให้ด้านบน
  - `ALTER PROCEDURE / FUNCTION / VIEW / TRIGGER p`: ดึงโค้ดปัจจุบันจากฐานข้อมูลมาเป็น `ALTER ...`

## [1.3.0] - 2026-10-20

### เพิ่ม
- เช็กอัปเดตออนไลน์จาก GitHub Releases วันละครั้งตอนเปิด SSMS และสั่งเองได้ที่ Tools › SqlAssist › Check for Updates
- ดาวน์โหลดตัวติดตั้ง ตรวจ SHA-256 แล้วเปิดหน้าต่างติดตั้ง

## [1.2.24] - 2026-10-20

### เพิ่ม
- Column picker: ใน `SELECT | FROM t x JOIN ...` ติ๊กคอลัมน์จากกิ่งของแต่ละตาราง แล้ว `x.Column` จะถูกเขียนลง select list ทันที

### แก้ไข
- พิมพ์ `@` ตอนที่ยังไม่มีรายการเปิดอยู่ จะเปิดรายการตัวแปรได้แล้ว

## [1.2.23] - 2026-10-20

### เพิ่ม
- พิมพ์ `@` จะแนะนำตัวแปรที่ `DECLARE` ไว้ด้านบน และ parameter ของ procedure / function พร้อมชนิดข้อมูลและ INPUT / OUTPUT

## [1.2.22] - 2026-10-20

### แก้ไข
- Snippets Manager แสดง snippet เริ่มต้นตัวใหม่ แม้จะเปิด Manager ก่อนพิมพ์ใน query

## [1.2.21] - 2026-10-20

### เปลี่ยน
- shortcut ของ snippet CASE WHEN เปลี่ยนจาก `cw` เป็น `case`

### แก้ไข
- snippet เริ่มต้นตัวใหม่จะถูกเพิ่มเข้าไปใน `snippets.xml` ที่มีอยู่แล้วให้อัตโนมัติ (ตัวที่ผู้ใช้ลบทิ้งจะไม่กลับมา)

## [1.2.20] - 2026-10-19

### เพิ่ม
- snippet เริ่มต้น CASE WHEN ... THEN ... ELSE ... END

## [1.2.19] - 2026-10-19

### เพิ่ม
- ถอนการติดตั้งได้จาก Programs and Features

## [1.2.18] - 2026-10-19

### เพิ่ม
- คอลัมน์ของ CTE และ derived table ถูกแนะนำเหมือนตารางจริง
- `SELECT |` ที่ยังไม่มี FROM: เลือกคอลัมน์จากกิ่งของตาราง แล้วสร้าง `SELECT ... FROM ... JOIN ...` ให้ทั้งก้อน
- หน้าต่างตัวติดตั้งแบบใหม่

## [1.2.15] - 2026-10-17

### เพิ่ม
- Snippets Manager, ค่าแทนที่ใน snippet (`$DATE$`, `$USER$`, `$DB$` ...) และเมนู Tools › SqlAssist

## [1.2.13] - 2026-10-17

### เพิ่ม
- panel โครงสร้างตารางข้างรายการแนะนำ และตอนชี้เมาส์ที่ชื่อตาราง

## [1.2.9] - 2026-10-17

### เพิ่ม
- หน้าต่างค้นหา object, แนะนำเงื่อนไข `JOIN ... ON` และ toolbar

## [1.1.2] - 2026-10-16

### เพิ่ม
- หน้าต่างติดตั้งแบบ GUI และไอคอนโปรแกรม
