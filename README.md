# SqlAssist — IntelliSense + Snippets สำหรับ SSMS 22

add-on ขนาดเล็กสำหรับ SQL Server Management Studio 22 ทำแค่ 5 อย่างที่ใช้จริงจาก dbForge SQL Complete และมีหน้าต่างค้นหา object แบบ dbForge Search (ดู [ค้นหา object](#ค้นหา-object))

| ฟีเจอร์ | ใช้ยังไง |
|---|---|
| ขยาย `*` เป็นรายชื่อคอลัมน์ | `SELECT *` แล้วกด **Tab** จะได้ `c.CustomerID, c.Name, ...` (หรือ `c.*` + Tab เพื่อขยายเฉพาะตาราง `c`) |
| alias อัตโนมัติ | เลือกตารางหลัง `FROM`/`JOIN` จะได้ `Customers c`, `OrderDetails od` และถ้าซ้ำจะได้ `c2` |
| JOIN ตาม Foreign Key | พิมพ์ `JOIN ` แล้วรายการจะเริ่มด้วย `Orders o ON o.CustomerID = c.CustomerID` |
| ดูโครงสร้างตาราง | เลื่อนเลือกตาราง/วิวในรายการแนะนำค้างไว้ 1.5 วินาที จะมีกล่องข้าง ๆ แสดงคอลัมน์ ชนิดข้อมูล PK/FK, `IDENTITY` / `NOT NULL` และจำนวนแถวโดยประมาณ (Est. row count) |
| ชี้เมาส์ดูตาราง | เอาเมาส์ชี้ชื่อตารางหรือ alias ใน query (เช่น `Work_InitialApproval`, `wia`, หรือ `wr` ใน `wr.ReqId`) จะขึ้นกล่องโครงสร้างตารางแบบเดียวกัน ชี้ที่ชื่อ procedure / function (หลัง `EXEC` หรือหน้า `(`) จะเห็น parameter และชี้ที่คอลัมน์ (`wr.ReqId` หรือ `ReqId`) จะเห็นชนิดข้อมูล key และคำอธิบาย ถ้าคอลัมน์ไม่ได้ระบุตารางและมีหลายตารางที่มีชื่อนี้ จะไม่แสดงอะไร |
| CTE / subquery | คอลัมน์ของ `WITH Sales_CTE AS (...)` และ `FROM (SELECT ...) d` ถูกแนะนำเหมือนตารางจริง ใช้กับ `*` + Tab, JOIN, การชี้เมาส์ได้ ถ้ายังไม่มี FROM (`SELECT |`) จะแนะนำคอลัมน์ของ CTE ให้เลย |
| เลือกคอลัมน์แบบกิ่ง | ใน `SELECT |` ที่ยังไม่มี FROM รายการจะเป็นกิ่ง: CTE และทุกตาราง กด ▸ (หรือ → / ←) เพื่อขยายดูคอลัมน์ ติ๊ก ☐ (หรือ Shift+Space) ได้หลายตัว แล้ว Enter / Tab จะ gen `SELECT ... FROM ... JOIN ...` ให้ทั้งก้อน (JOIN ตาม FK หรือคอลัมน์ `...Id` ที่ชื่อตรงกัน) ไม่ติ๊กอะไร = ได้ทั้งตารางหรือคอลัมน์ที่เลือก |
| ขยาย INSERT / UPDATE / EXEC / ALTER | พิมพ์ชื่อ object จนจบบรรทัดแล้วกด **Tab**: `INSERT INTO t` → รายชื่อคอลัมน์ + `VALUES` พร้อมค่าตั้งต้นตามชนิดข้อมูล (ข้าม IDENTITY / computed / rowversion) · `UPDATE t` → `SET` ทุกคอลัมน์ + `WHERE` ตาม PK (ไม่มี PK = เว้น `WHERE` ว่างไว้ให้กรอก) · `EXEC p` → `@param = ค่า` ครบทุกตัว เรียงตรงกัน (OUTPUT จะ `DECLARE` ตัวแปรให้ด้านบน) · `ALTER PROCEDURE / FUNCTION / VIEW / TRIGGER p` → ดึงโค้ดปัจจุบันจากฐานข้อมูลมาเป็น `ALTER ...` ทั้งก้อน ถ้ารายการแนะนำเปิดอยู่ Tab ครั้งแรกจะเลือกรายการก่อน กด Tab อีกครั้งเพื่อขยาย |
| Column picker | ใน select list ที่มี FROM แล้ว (`SELECT | FROM Person.PersonPhone pp JOIN Person.Person p ...`) กด Space หลัง `SELECT` / `,` หรือ Ctrl+Space จะได้กิ่งของแต่ละตารางตาม alias (กางไว้แล้ว) ติ๊ก ☐ (หรือ Shift+Space) แล้ว `p.FirstName` ถูกเขียนลง select list ทันที (comma นำหน้าบรรทัดละคอลัมน์) เอาติ๊กออก = ลบออก พิมพ์เพื่อกรองชื่อคอลัมน์ได้ Enter / Tab / Esc ปิดรายการ |
| Procedure / function | หลัง `EXEC ` (หรือ `EXEC dbo.`, `EXEC @rc = `) รายการจะเป็น stored procedure และ function (ไม่ใช่ตาราง) เลือกรายการแล้วจะเห็นข้อมูลด้านข้าง: `dbo.ufnGetProductDealerPrice (Function) returns MONEY` และ parameter ทุกตัวพร้อมชนิดข้อมูล INPUT / OUTPUT / READONLY และคำอธิบาย (MS_Description) · scalar function แนะนำในรายการทั่วไป (ใช้ใน expression) · table-valued function แนะนำหลัง `FROM` / `JOIN` |
| ตัวแปร / parameter | พิมพ์ `@` จะแนะนำตัวแปรที่ `DECLARE` ไว้ด้านบน และ parameter ของ `CREATE/ALTER PROCEDURE / FUNCTION` พร้อมชนิดข้อมูล และ INPUT / OUTPUT (นับเฉพาะใน batch เดียวกัน คือหลัง `GO` ตัวล่าสุด) |
| ข้อมูลคอลัมน์ | เลือกคอลัมน์ในรายการแนะนำ จะเห็นชนิดข้อมูล, PK / IDENTITY / NOT NULL, คำอธิบายจากฐานข้อมูล (MS_Description) และ Foreign key ไปตารางไหน |
| เงื่อนไข ON อัตโนมัติ | พิมพ์ `JOIN Work_InitialApproval wia ON ` แล้วจะได้ `wr.ReqId = wia.ReqId` เรียงจาก Foreign Key ก่อน แล้วตามด้วยคอลัมน์ที่ชื่อตรงกัน (คอลัมน์ `...Id` ขึ้นก่อน) ใช้หลัง `AND` ได้ด้วย |
| ค้นชื่อไม่ต้องตรงตัว | `custid` → `CustomerID`, `od` → `OrderDetails` |
| Format SQL | **Tools › SqlAssist › Format SQL**, คลิกขวาใน query editor › **Format SQL (SqlAssist)** หรือ **Ctrl+K, Ctrl+Shift+F** จัดรูปแบบ SQL ที่เลือก (หรือทั้งหน้า query ถ้าไม่ได้เลือก) โดยรักษา comment, string, hint และ `GO` ปรับค่าได้ที่ **Tools › SqlAssist › Options...** ดูหัวข้อ [Format SQL](#format-sql) |
| ตรวจจุดเสี่ยงใน SQL | **Tools › SqlAssist › Check SQL Risks...** ตรวจ `UPDATE` / `DELETE` ที่ไม่มี `WHERE`, `TRUNCATE TABLE`, `DROP TABLE` ในส่วนที่เลือก (หรือทั้งหน้า query) คลิกรายการเพื่อกระโดดไปที่คำสั่ง ไม่แก้และไม่รัน SQL ให้ ดูหัวข้อ [ตรวจจุดเสี่ยงใน SQL](#ตรวจจุดเสี่ยงใน-sql) |
| CSV to SQL | **Tools › SqlAssist › CSV to SQL...** เปลี่ยนข้อมูลที่คัดลอกจาก Excel หรือข้อความ CSV เป็น `IN (...)`, `VALUES (...)` หรือ `CREATE TABLE #Temp` + `INSERT` ดูหัวข้อ [CSV to SQL](#csv-to-sql) |
| Snippets | `ssf` + **Tab** → `SELECT * FROM ` แล้วรายชื่อตารางจะเด้งขึ้นมาให้เลือกต่อ |

## ก่อนติดตั้ง (ทำครั้งเดียว)

1. **ปิด IntelliSense ของ SSMS**: Tools › Options › Text Editor › Transact-SQL › IntelliSense › เอาติ๊ก *Enable IntelliSense* ออก
2. **Uninstall หรือปิด dbForge SQL Complete** เพราะมันดักปุ่ม Tab และ IntelliSense เหมือนกัน

## ติดตั้ง

ดับเบิลคลิก **`dist\SqlAssistSetup-<version>.exe`** จะเปิดหน้าต่างติดตั้งขึ้นมา

- **ยังไม่เคยติดตั้ง:** กดปุ่ม **ติดตั้ง**
- **ติดตั้งไว้แล้ว:** เลือกได้ว่าจะ **อัปเดต** (หรือติดตั้งซ้ำถ้าเป็นเวอร์ชันเดียวกัน) หรือ **ถอนการติดตั้ง**
- ถอนการติดตั้งจาก **Control Panel › Programs and Features** (หรือ Settings › Apps) ได้ด้วย: เลือก **SqlAssist for SSMS 22** แล้วกด Uninstall
- **ถ้า SSMS เปิดอยู่:** จะขึ้นแถบเตือนสีเหลืองและกดปุ่มไม่ได้ พอปิด SSMS ครบทุกหน้าต่าง ปุ่มจะกดได้เองภายใน 1 วินาที

### ตรวจสอบเวอร์ชัน
- ตัวติดตั้งจะแสดงเวอร์ชันที่ติดตั้งอยู่เทียบกับเวอร์ชันในตัวติดตั้ง และบอกว่ามีเวอร์ชันใหม่กว่าหรือเป็นล่าสุดแล้ว
- ใน SSMS: **Tools › SqlAssist › About...**
- ใน log (Tools › SqlAssist › Options... › เปิดไฟล์ Log) ทุกครั้งที่เปิดหน้า query จะมีบรรทัด `SqlAssist x.y.z attached`
- ก่อนออกเวอร์ชันใหม่ ให้แก้เลขเวอร์ชันที่ `Directory.Build.props` ที่เดียว แล้วรัน `.\build-setup.ps1`

### อัปเดตออนไลน์ (ตั้งแต่ 1.3.0)
- ตอนเปิด SSMS ระบบจะเช็ก [GitHub Releases](https://github.com/kriangdet/SSMS22SqlAssist_Addon/releases) วันละครั้ง ถ้ามีเวอร์ชันใหม่จะขึ้นแถบแจ้งเตือนด้านบนหน้าต่าง SSMS
- เช็กเองได้ที่ **Tools › SqlAssist › Check for Updates...**
- ในแถบแจ้งเตือนมีปุ่มให้เลือก:
  - **Install when SSMS closes** (ตั้งแต่ 1.3.3) — ดาวน์โหลดและตรวจ SHA-256 ไว้ก่อน แล้วรอเงียบ ๆ อยู่เบื้องหลัง พอปิด SSMS ครบทุกหน้าต่างจะติดตั้งให้เอง เปิด SSMS ครั้งถัดไปก็ได้เวอร์ชันใหม่ (แถบสถานะขึ้น "SqlAssist updated to ...") ถ้าติดตั้งไม่สำเร็จจะแจ้งตอนเปิด SSMS ถ้า restart เครื่องก่อนได้ปิด SSMS ระบบจะตั้งรอใหม่ให้ตอนเปิด SSMS
  - **Install now** — ดาวน์โหลดตัวติดตั้งไว้ที่ `%LOCALAPPDATA%\SqlAssist\updates\` แล้วตรวจ SHA-256 กับค่าที่ GitHub ให้มา จากนั้นจะเปิดหน้าต่างติดตั้งขึ้นมา ให้ **ปิด SSMS แล้วกดอัปเดต**
  - **What's new** — เปิดหน้า release เพื่อดูว่ามีอะไรใหม่
  - **Skip this version** — ไม่ต้องเตือนเวอร์ชันนี้อีก
  - **Don't check automatically** — ปิดการเช็กอัตโนมัติ (เมนู Check for Updates ยังใช้ได้ตามปกติ) ถ้าอยากเปิดกลับ ให้ลบไฟล์ `%LOCALAPPDATA%\SqlAssist\update.txt`
- ดูว่าแต่ละเวอร์ชันเพิ่มหรือแก้อะไรได้ที่ [CHANGELOG.md](CHANGELOG.md) หรือหน้า [Releases](https://github.com/kriangdet/SSMS22SqlAssist_Addon/releases) (ลิงก์ **What's new** ในแถบแจ้งเตือน)
- ออกเวอร์ชันใหม่ (สำหรับนักพัฒนา): เขียนสิ่งที่เปลี่ยนลง `CHANGELOG.md` → แก้เวอร์ชัน → `.\build-setup.ps1` → commit แล้ว push → ตั้งค่า `$env:GITHUB_TOKEN` → `.\publish-release.ps1` (ข้อความใน release มาจาก CHANGELOG ของเวอร์ชันนั้น)

- ไฟล์ exe ไฟล์เดียวมี add-on อยู่ข้างในแล้ว จะก๊อปไปเครื่องอื่นที่มี SSMS 22 ก็ใช้ได้เลย ไม่ต้องลงโปรแกรมเพิ่ม
- ติดตั้งแบบ per-user ไม่ต้องใช้สิทธิ์ admin ถ้า SSMS ยังเปิดอยู่จะขอให้ปิดก่อน
- ติดตั้งซ้ำทับได้ (ใช้ตอนอัปเดตเวอร์ชัน)
- ใช้ผ่าน command line โดยไม่เปิดหน้าต่างได้: `SqlAssistSetup-<version>.exe /install` หรือ `/uninstall` (exit code: 0 สำเร็จ, 1 ล้มเหลว, 2 SSMS เปิดอยู่, 3 ไม่พบ SSMS 22)

พอเปิด SSMS ใหม่จะเห็นเมนู **Tools › SqlAssist: ...**

สร้าง exe ใหม่หลังแก้โค้ดด้วย `.\build-setup.ps1` (รัน test → build → ได้ไฟล์ใน `dist\`)
ถ้าไม่อยากใช้ exe ก็ใช้สคริปต์ `.\install.ps1` / `.\uninstall.ps1` แทนได้

## ปุ่มลัด

| ปุ่ม | ทำอะไร |
|---|---|
| Tab | ขยาย snippet → ถ้ามีรายการแนะนำเปิดอยู่จะเลือกรายการนั้น → ขยาย `*` → ถ้าไม่เข้าข้อไหนเลยจะเป็น Tab ปกติ |
| Enter | เลือกรายการแนะนำ ถ้าพิมพ์คำครบแล้ว (เช่น `from`) จะเลือกแล้วขึ้นบรรทัดใหม่ให้ด้วย |
| Space, `,`, `(` ฯลฯ | ปิดรายการแนะนำโดย **ไม่** เลือกอะไร |
| Ctrl+Space | เปิดรายการแนะนำเอง |
| Ctrl+Shift+R | โหลด schema ใหม่ (หรือใช้ Tools › SqlAssist › Refresh Schema Cache) |
| F12 | ไปที่ definition ของชื่อที่ caret อยู่ (หรือคลิกขวา › Go To Definition): ตารางจะเปิดใน Object Explorer ส่วน view / procedure / function / trigger จะเปิด script (`CREATE ...` ตามที่ SQL Server เก็บไว้) ในหน้าต่าง query ใหม่ ไม่รันให้ ถ้าไม่ใช่ชื่อ object (keyword, ตัวแปร, คอลัมน์, CTE) จะไม่ทำอะไร |
| Ctrl+K, Ctrl+Shift+F | จัดรูปแบบ SQL ที่เลือก (หรือทั้งหน้า query) ดูหัวข้อ Format SQL |

| Ctrl+J แล้ว I / T / W / O / U | (ใน results grid) copy script ของ cell ที่เลือกเป็น INSERT / INSERT #tmpTable / WHERE IN() / WHERE AND OR / UPDATE (UPDATE เปิดหน้าต่างเลือกตารางและ Key ก่อน) ดูหัวข้อ Generate Script As ด้านล่าง |

schema จะถูกโหลดครั้งแรกที่ใช้งานในแต่ละ database แล้ว cache ไว้ 10 นาที
ถ้าใน query มีชื่อตารางที่ cache ยังไม่รู้จัก (เช่นเพิ่งสร้าง) ระบบจะโหลดใหม่เองโดยห่างกันอย่างน้อย 30 วินาที

## Generate Script As จาก results grid

เลือก cell ในผลลัพธ์ของ query (ลากคลุมหลายแถว/หลายคอลัมน์ หรือกด Ctrl ค้างเพื่อเลือกหลายช่วงก็ได้) แล้วคลิกขวา ›
**Generate Script As** › ชนิด statement › **New Query Window** (เปิดหน้าต่าง query ใหม่ที่ต่อ server เดียวกัน),
**Clipboard** หรือ **File...** (บันทึกเป็น `.sql`)

| ชนิด | ได้อะไร |
|---|---|
| INSERT | `INSERT INTO [Table] (คอลัมน์ที่เลือก) VALUES (...), (...)` แบ่ง statement ทุก 1,000 แถว |
| INSERT #tmpTable | `CREATE TABLE #tmpTable` ตามชนิดข้อมูลของคอลัมน์ แล้ว INSERT, `SELECT * FROM #tmpTable` และ `DROP TABLE` |
| WHERE IN() | `WHERE col IN(...)` ค่าไม่ซ้ำกัน ถ้าเลือกหลายคอลัมน์จะต่อกันด้วย `AND` |
| WHERE AND OR | `WHERE (a = ... AND b = ...) OR (...)` หนึ่งวงเล็บต่อแถว |
| UPDATE | เปิดหน้าต่างให้ระบุตารางปลายทางและเลือกคอลัมน์ SET / Key แล้วได้ `UPDATE ตาราง SET ... WHERE <Key> = ...` ทีละแถวตามด้วย `GO` (ดูหัวข้อ [UPDATE](#update-เลือกตารางและ-key) ด้านล่าง) |

- ชื่อตารางของ INSERT เป็น `[Table]` ให้แก้เป็นชื่อจริงเอง (ผลลัพธ์ของ SSMS ไม่ได้บอกว่าคอลัมน์มาจากตารางไหน)
- ค่า NULL เขียนเป็น `NULL` / `IS NULL`, ข้อความเป็น `N'...'` (หรือ `'...'` สำหรับ varchar), วันที่เป็น `'yyyy-MM-dd HH:mm:ss.fff'` (script ขึ้นต้นด้วย `SET DATEFORMAT ymd`), binary เป็น `0x...`
- ทำได้ครั้งละไม่เกิน 100,000 แถว
- ปุ่มลัด (copy ลง clipboard ทันที): คลิกใน grid แล้วกด **Ctrl+J** ตามด้วย **I** / **T** / **W** / **O** / **U** (ส่วน **U** จะเปิดหน้าต่าง UPDATE ให้เลือกตารางและ Key ก่อน โดยตั้งปลายทางเป็น Clipboard)

### UPDATE: เลือกตารางและ Key

เมนู **UPDATE** (และ Ctrl+J แล้ว U) จะเปิดหน้าต่างให้กำหนดเอง SqlAssist **ไม่เดา**ชื่อตารางหรือ key จากผลลัพธ์:

- **ชื่อตารางปลายทาง:** ช่องเริ่มต้นว่าง พิมพ์เช่น `dbo.Orders` หรือ `[Sales].[Order Line]` (ใส่ database นำหน้าได้ 3 ส่วน) ระบบตรวจชื่อและใส่ `[ ]` ให้เองเมื่อจำเป็น ชื่อที่มีช่องว่างหรืออักขระพิเศษโดยไม่ครอบด้วย `[ ]` จะถูกปฏิเสธ
- **SET / Key:** แต่ละคอลัมน์ของผลลัพธ์ติ๊กได้ว่าเป็น SET (คอลัมน์ที่จะเปลี่ยนค่า) หรือ Key (คอลัมน์ที่ใช้หาแถวใน `WHERE`) คอลัมน์ที่เลือกใน grid เริ่มต้นเป็น SET ส่วน Key เริ่มต้นไม่มีเลย เมื่อติ๊กคอลัมน์เป็น Key ระบบจะเอาออกจาก SET ให้ก่อน แต่ติ๊ก SET กลับเองได้
- **หลาย Key:** ต่อด้วย `AND` ถ้าค่าของ Key เป็น NULL จะเขียนเป็น `IS NULL`
- **ตัวอย่าง:** แสดง 10 แถวแรกของ script ก่อนส่ง เลือกปลายทางได้ที่ New Query Window / Clipboard / File
- **สร้างไม่ได้ถ้าข้อมูลไม่ครบ:** ต้องมีชื่อตารางที่ถูกต้อง อย่างน้อย 1 คอลัมน์ SET และอย่างน้อย 1 Key
- **เตือน Key ซ้ำ:** ถ้าแถวที่เลือกมีค่า Key ซ้ำกัน จะแจ้งว่าหลายแถวใช้เงื่อนไข `WHERE` เดียวกัน (เทียบตามค่าที่จะเขียนใน script)
- **Key ที่เลือกไม่รับประกันว่า unique ในฐานข้อมูลจริง:** SqlAssist ไม่ได้ตรวจกับตารางจริง ถ้า Key ซ้ำในตาราง UPDATE จะกระทบหลายแถว ตรวจ PRIMARY KEY / UNIQUE ก่อนรัน script

## Format SQL

**Tools › SqlAssist › Format SQL** หรือ **Ctrl+K, Ctrl+Shift+F** ในหน้า query (คำสั่งเป็นสีเทาถ้าหน้าที่ใช้อยู่ไม่ใช่ T-SQL)

- **ส่วนที่จัด:** มีข้อความที่เลือก → จัดเฉพาะส่วนนั้น (บรรทัดต่อ ๆ ไปย่อหน้าตามบรรทัดที่เลือกเริ่ม) ไม่ได้เลือกอะไร → จัดทั้งเอกสาร (ไม่รองรับการเลือกแบบ box)
- **Undo:** กด **Ctrl+Z** ครั้งเดียวย้อนการจัดทั้งชุด เคอร์เซอร์หรือส่วนที่เลือกอยู่กับข้อความเดิม และเปลี่ยนเฉพาะช่วงที่ต่างกันจริง (bookmark/breakpoint ที่อื่นไม่หลุด)
- **ที่จัดให้เต็มรูปแบบ:** SELECT (DISTINCT / TOP / INTO), JOIN ทุกชนิด และ CROSS / OUTER APPLY, WHERE / HAVING (AND / OR ขึ้นบรรทัดใหม่, `BETWEEN … AND` ไม่ถูกแยก), GROUP BY / ORDER BY / OFFSET … FETCH, UNION / EXCEPT / INTERSECT, CASE (ตั้งแต่ 2 WHEN ขึ้นไปแยกบรรทัด), subquery และ derived table, CTE (`WITH … AS (…)`), INSERT / UPDATE / DELETE / MERGE (รวม OUTPUT, OPTION) และ `IF / ELSE / WHILE / BEGIN … END / TRY … CATCH` ที่กำหนดย่อหน้าของ statement ข้างใน
- **statement อื่น** (DECLARE, SET, EXEC, CREATE TABLE / PROCEDURE …): จัดช่องว่าง ย่อหน้า และตัวพิมพ์ของ keyword แต่คง **การขึ้นบรรทัดเดิม** ของคุณไว้
- **รักษาไว้ไม่ให้เปลี่ยน:** comment (`--` และ `/* */`), ข้อความใน `'...'`, ชื่อใน `[...]` / `"..."`, hint (`WITH (NOLOCK)`, `OPTION (...)`), `GO` (บรรทัดละคำสั่ง) ตัวแปร `@x`, temp table `#t`, ตัวเลข / เงิน / hex จะมีแค่ **ช่องว่าง ตัวพิมพ์ของ keyword และเครื่องหมาย `,` ที่ย้ายไปอีกฝั่งของบรรทัด** เท่านั้นที่เปลี่ยน
- **ตัวพิมพ์ของ keyword:** เปลี่ยนเฉพาะคำสงวนของ T-SQL และชื่อฟังก์ชัน/ชนิดข้อมูลที่ตำแหน่งบอกชัดว่าไม่ใช่ชื่อ (เช่น `COUNT(`, `CAST(x AS INT)`, `@a INT`, `OFFSET … ROWS`) ชื่อตาราง คอลัมน์ และ alias ไม่ถูกเปลี่ยนเด็ดขาด (database ที่แยกตัวพิมพ์ใหญ่เล็กจึงไม่พัง) คำหลังจุด เช่น `t.Key` ก็ไม่เปลี่ยน
- **ตั้งค่า** ที่ **Tools › SqlAssist › Options...** (มีตัวอย่างที่เปลี่ยนตามตัวเลือก): ตัวพิมพ์ keyword (ใหญ่ / เล็ก / ตามที่พิมพ์), ย่อหน้า 2–8 ช่องว่าง, เครื่องหมาย `,` นำหน้าหรือท้ายบรรทัด ค่าเก็บที่ `%APPDATA%\SqlAssist\settings.xml`
- **จัดซ้ำได้ผลเดิม:** รันกี่ครั้งก็ได้ข้อความเดียวกัน
- **ถ้าจัดไม่ได้:** ถ้า SQL ไม่สมบูรณ์ (string `'`, `[`, `/*` หรือวงเล็บที่เปิดไว้แล้วไม่ปิด) จะแจ้งบรรทัดและคอลัมน์ที่เจอปัญหา และ **ไม่แก้ข้อความ** ทุกครั้งที่จัดเสร็จ ระบบตรวจซ้ำว่า token และ comment ทุกตัวยังอยู่ครบตามลำดับ (และการแบ่ง batch ด้วย `GO` เหมือนเดิม) ถ้าไม่ตรงจะไม่แก้ข้อความเช่นกัน
- **ปุ่มลัด:** เลือก Ctrl+K, Ctrl+Shift+F เพราะตรวจแล้วไม่ชนกับ: ปุ่มลัดมาตรฐานของ Visual Studio (Ctrl+K, Ctrl+F / Ctrl+D ใช้ Format ของ Visual Studio), โปรไฟล์ของ SSMS 22 (Format SQL ในตัวของ SSMS คือ Ctrl+K, Ctrl+Q ซึ่งยังใช้ได้ตามเดิม, Copilot ใช้ Ctrl+Alt+E/R/D/F, Presenter Mode ใช้ Ctrl+Shift+F5), ไฟล์ตั้งค่าปุ่มลัดของเครื่องนี้ และปุ่มของ SqlAssist เอง (Tab, Ctrl+Space, Ctrl+Shift+R, Ctrl+J แล้ว I/T/W/O/U) ถ้าคุณมีส่วนเสริมอื่นที่ใช้ปุ่มนี้ เปลี่ยนได้ที่ Tools › Options › Environment › Keyboard (ค้น `SqlAssist.FormatSql`)

## ตรวจจุดเสี่ยงใน SQL

**Tools › SqlAssist › Check SQL Risks...** (ในหน้า query T-SQL) ตรวจส่วนที่เลือก หรือทั้งหน้า query ถ้าไม่ได้เลือกอะไร แล้วแสดงรายการใน **หน้าต่างผลตรวจ** ที่ไม่ปิดบัง editor: กฎที่เจอ ตำแหน่ง (บรรทัด, คอลัมน์) คำสั่ง และเหตุผล **คลิกแถว** (หรือกด Enter) เพื่อเลือกคำสั่งนั้นใน editor ตำแหน่งจะตามไปเองแม้แก้ข้อความหลังตรวจ

| กฎ | เมื่อไหร่ |
|---|---|
| UPDATE ไม่มี WHERE | `UPDATE` ที่ไม่มี `WHERE` ของตัวเอง (รวมรูปแบบ `UPDATE a SET … FROM t a JOIN …`) |
| DELETE ไม่มี WHERE | `DELETE` ที่ไม่มี `WHERE` ของตัวเอง (รวม `DELETE a FROM t a JOIN …` และ `DELETE TOP (n)`) |
| TRUNCATE TABLE | ทุกคำสั่ง `TRUNCATE TABLE` |
| DROP TABLE | ทุกคำสั่ง `DROP TABLE` (รวม `IF EXISTS` และหลายตาราง) ไม่รวม `DROP VIEW / INDEX / PROCEDURE` และ `ALTER TABLE … DROP COLUMN` |

- **อ่านเป็นคำสั่ง ไม่ใช่ค้นคำ:** `WHERE` ใน subquery, CTE, ข้อความ `'...'`, comment หรือชื่อ `[where]` ไม่ถูกนับเป็น `WHERE` ของคำสั่งด้านนอก คำสั่งใน `IF`, `BEGIN … END`, `TRY … CATCH` ถูกตรวจด้วย `GO` แบ่ง batch
- **ไม่แก้ SQL และไม่เติม WHERE ให้** และไม่รันอะไร
- **การมี WHERE ไม่ได้รับประกันว่าคำสั่งปลอดภัย** (WHERE อาจกว้างเกินไปหรือผิดเงื่อนไข) และการไม่พบรายการก็ไม่ได้แปลว่าปลอดภัย ข้อความนี้แสดงในหน้าต่างผลตรวจเสมอ
- **ตรวจไม่ได้:** SQL ที่ประกอบเป็นข้อความใน `EXEC (...)` / `sp_executesql`, stored procedure ที่ถูกเรียก, trigger, view, และ `MERGE` ถ้า SQL ไม่สมบูรณ์ (string, comment, วงเล็บไม่ปิด) จะตรวจเท่าที่อ่านออกและมีหมายเหตุบอกจุดที่อ่านไม่ได้
- **ตั้งค่า** ที่ **Tools › SqlAssist › Options... › ตรวจจุดเสี่ยง:** เปิด/ปิดได้ทีละกฎ และเลือกให้ข้ามตาราง temp (`#t`, `##t`) กับ table variable (`@t`) (ค่าเริ่มต้นตรวจทุกอย่าง)

### เตือนก่อนกด Execute (ทดลอง ปิดอยู่เป็นค่าเริ่มต้น)

เปิดได้ที่ **Options › ตรวจจุดเสี่ยง › เตือนก่อนกด Execute** เมื่อเปิดแล้ว ทุกครั้งที่สั่ง Execute และ script ที่จะรัน (ส่วนที่เลือก หรือทั้งหน้า query) มีรายการตามกฎ จะขึ้นหน้าต่างแสดง **Server, Database** และคำสั่งเสี่ยง พร้อมปุ่ม **ยกเลิก** (ค่าเริ่มต้น: Enter, Esc และปิดหน้าต่างล้วนยกเลิก) กับ **รันต่อ** ถ้าไม่มีรายการจะรันตามปกติโดยไม่ถามอะไร และ **Cancel Query ไม่ถูกแตะต้อง**

- **กลไก:** ลงทะเบียนเป็น priority command target ของ SSMS ซึ่งได้รับทุกคำสั่งก่อนปลายทางจริง ชื่อคำสั่ง Execute ถูกค้นหาตอนเปิดใช้ (ลองชื่อ `Query.Execute`, `Query.ExecuteQuery`, `Query.ExecuteSelection`) และบันทึกลง log (Tools › SqlAssist › Options... › เปิดไฟล์ Log บรรทัด `Execute guard hooked: …`; ใน SSMS 22 ที่ลองพบ `Query.Execute` อย่างเดียว) ถ้าหาไม่พบจะบอกที่หน้า Options ว่ายังไม่ได้ดัก
- **ข้อจำกัดที่ต้องรู้:** ยังเป็นของทดลอง ลองแล้วใน SSMS 22 (เครื่องทดสอบ, server SQL Express, ใช้ temp table เท่านั้น) กับ F5, Ctrl+E, ปุ่ม Execute บน toolbar, เมนู Query › Execute และการรันเฉพาะ selection ทุกช่องทางขึ้นหน้าต่างเตือน ปุ่มยกเลิก/Enter/Esc ไม่รันอะไร ปุ่ม รันต่อ รันครั้งเดียว และ Cancel Query ยังใช้ได้ปกติ แต่ **ยังไม่ได้ลองทุกช่องทางที่รัน query ใน SSMS** และไม่ใช่การรับประกันว่าจะไม่มีคำสั่งเสี่ยงหลุดไปรัน ถ้าต้องการความแน่ใจให้ใช้ Check SQL Risks ตรวจก่อนรันเอง ไม่แก้ปุ่มลัดของ SSMS เลย และถ้าปิดตัวเลือกนี้จะไม่มีอะไรถูกลงทะเบียน (ไม่มีผลต่อความเร็ว)
- **ลองยืนยันเอง (โดยไม่แตะข้อมูลจริง):** เปิดตัวเลือก แล้วในหน้า query ที่ต่อ server ทดสอบ พิมพ์ `CREATE TABLE #t (a INT); DELETE FROM #t; DROP TABLE #t;` แล้วสั่ง Execute ด้วยทุกช่องทาง — **F5**, **Ctrl+E**, ปุ่ม **Execute** บน toolbar, เมนู **Query › Execute** — ทั้งแบบไม่เลือกอะไรและแบบเลือกเฉพาะ `DELETE FROM #t` ทุกครั้งต้องขึ้นหน้าต่างเตือน และกด **ยกเลิก** แล้วต้องไม่มีอะไรรัน (ตารางนี้เป็น temp table ของ session เท่านั้น)

## CSV to SQL

เปิดจาก **Tools › SqlAssist › CSV to SQL...** เพื่อเปลี่ยนข้อมูลที่คัดลอกจาก Excel หรือข้อความ CSV ให้เป็น SQL

- **ช่องข้อความ:** ตอนเปิดหน้าต่างจะวางข้อความจาก Clipboard ให้ (ปุ่ม **วางจาก Clipboard** อ่านซ้ำได้) แก้ไขในช่องนี้ได้ แล้วตัวอย่างจะอัปเดตเอง รองรับ cell ที่ครอบด้วย `"` ตามแบบ Excel/CSV (มีตัวคั่น, ขึ้นบรรทัดใหม่ หรือ `""` อยู่ข้างใน)
- **ตัวคั่น cell:** ค่าเริ่มต้นเป็น **Tab** (สิ่งที่ Excel ใส่ใน Clipboard) ถ้าเป็นข้อความ CSV เลือก **คอมมา `,`**, **เซมิโคลอน `;`** หรือ **ไปป์ `|`** เอง ระบบไม่เดาตัวคั่นจากข้อความ เพราะคอลัมน์เดียวที่มี `,` ในข้อความ (เช่น `Smith, John`) จะถูกแยกผิด ตัวเลขที่ใช้ `,` เป็นทศนิยม (CSV แบบยุโรป) อ่านไม่ได้ตามกติกาด้านล่างและจะแจ้งเป็น error
- **แถวแรกเป็นชื่อคอลัมน์:** ติ๊กเองได้ ถ้าไม่ติ๊กทุกแถวเป็นข้อมูลและคอลัมน์ชื่อ `Column1`, `Column2`, ...
- **ผลลัพธ์ 3 แบบ**

  | แบบ | ได้อะไร |
  |---|---|
  | `IN (...)` | `IN (N'00123', N'00456')` ของคอลัมน์เดียวที่เลือก ติ๊ก **ตัดค่าซ้ำ** ได้ ค่า NULL จะถูกข้าม (IN จับ NULL ไม่ได้) และแจ้งจำนวนที่ข้าม ถ้ามีเกิน 10 ค่าจะขึ้นบรรทัดใหม่ทุก 10 ค่า |
  | `VALUES (...), (...)` | `VALUES` ตามด้วยแถวละบรรทัด แบ่งเป็นก้อนละไม่เกิน 1,000 แถวตามข้อจำกัดของ SQL Server |
  | `CREATE TABLE #Temp + INSERT` | สร้าง `#Temp` (ตั้งชื่อ `#...` / `##...` เองได้) ตามชนิดข้อมูลที่เลือก ขนาด `nvarchar(n)` / `decimal(p,s)` คำนวณจากข้อมูลจริง แล้ว `INSERT` แบ่งทุก 1,000 แถว (ใช้ตัวเขียน INSERT เดียวกับ Generate Script As) |

- **ชนิดข้อมูลของแต่ละคอลัมน์:** ค่าเริ่มต้นเป็น *ข้อความ (nvarchar)* ทุกคอลัมน์ เพื่อให้รหัสอย่าง `00123` ไม่กลายเป็น `123` เลือก varchar, int, bigint, decimal, float, bit, date, datetime, datetime2, uniqueidentifier ได้ ถ้าข้อมูลไม่เข้ากับชนิดที่เลือกจะแจ้ง **แถวที่ N คอลัมน์ M** พร้อมค่าที่ผิด (แถวนับรวมแถวหัวตาราง) และจะไม่สร้าง SQL จนกว่าจะแก้ ไม่มีการแปลงผิดแบบเงียบ ๆ
- **กติกา NULL (แยกเป็น 3 อย่างชัดเจน):**
  - *cell ว่าง* (ไม่มีอะไรระหว่างตัวคั่น) → เลือกได้ว่าเป็น `NULL` (ค่าเริ่มต้น) หรือสตริงว่าง `''`
  - *ข้อความ NULL* (ตัวพิมพ์ใหญ่ `NULL` ตรงตัว เช่น ที่ SSMS ใช้แสดงค่า NULL ตอน copy จาก grid) → เลือกได้ว่าเป็น `NULL` (ค่าเริ่มต้น) หรือข้อความ `'NULL'` ส่วน `Null`, `null`, ` NULL` ถือเป็นข้อความธรรมดาเสมอ (กันชื่อคนอย่าง Null)
  - ถ้าเลือกให้เก็บเป็นสตริงว่าง/ข้อความ `NULL` แต่คอลัมน์เป็นตัวเลขหรือวันที่ ซึ่งเก็บไม่ได้ จะแจ้งเป็น error
- **ตัวเลขและวันที่ไม่ขึ้นกับ regional settings ของเครื่อง:** ตัวเลขต้องเป็นเลขอารบิกและใช้จุด `.` เป็นทศนิยม (ไม่มี `,` คั่นหลัก, ไม่มี `1e5` ใน decimal) วันที่ต้องตรงรูปแบบที่เลือก (`yyyy-MM-dd`, `dd/MM/yyyy` หรือ `MM/dd/yyyy`) และ `date` ต้องไม่มีเวลา, `datetime` ละเอียดไม่เกิน 3 หลัก (เกินให้ใช้ `datetime2`) ปีที่ดูเหมือนปี พ.ศ. (2400-2700) จะมีคำเตือน วันที่ในผลลัพธ์เขียนเป็น ISO (`'2024-01-31T13:05:09.000'`) จึงไม่ต้องใช้ `SET DATEFORMAT`
- **ตัวอย่างและการส่งออก:** ตัวอย่างแสดง 50 แถวแรก (แต่ตรวจข้อมูลครบทุกแถวแล้ว) ส่งไปที่ **Clipboard** หรือ **New Query Window** ได้ SqlAssist ไม่รัน SQL ให้
- **ขีดจำกัด (กัน SSMS ค้าง):** ข้อความไม่เกิน 2,000,000 ตัวอักษร, 100,000 แถว, 256 คอลัมน์ และทุกแถวต้องมีจำนวนคอลัมน์เท่าแถวแรก (ไม่เติมช่องว่างให้เอง)
- **ความเป็นส่วนตัว:** เนื้อหาใน Clipboard ไม่ถูกบันทึกลง log

## ค้นหา object

เปิดจาก **Tools › SqlAssist › Search Objects...** หรือปุ่ม **SqlAssist** (โลโก้ SqlAssist) บน toolbar **SqlAssist** (ถ้าไม่เห็น toolbar ให้คลิกขวาที่แถบ toolbar แล้วติ๊ก SqlAssist หรือไปที่ View › Toolbars) หน้าต่างจะ dock เป็นแท็บข้าง Output และลากไปวางที่อื่นได้

- **ค้นขณะพิมพ์:** พิมพ์อย่างน้อย 2 ตัวอักษรแล้วผลจะขึ้นเอง (กด Enter เพื่อค้นทันที หรือกด ↓ เพื่อไปที่ตารางผลลัพธ์)
- **ค้นจากอะไร:** ชื่อ object และข้อความใน definition ของ view / procedure / function / trigger ส่วนชื่อตารางและคอลัมน์จะค้นจากชื่ออย่างเดียว
- **ชนิด object (dropdown "All object types"):** ติ๊กเลือก Schema, Table, View, Procedure, Function, Trigger, Column
- **ตัวเลือกการค้น (ปุ่ม ⚙):** Match case, Match whole word และ Use wildcards (`*` = อักษรกี่ตัวก็ได้ในบรรทัดเดียวกัน, `?` = อักษร 1 ตัว) ถ้าไม่ติ๊กอะไรเลยจะค้นแบบ "มีคำนี้อยู่" และไม่สนตัวพิมพ์เล็กใหญ่
- **Database (dropdown):** ค่าเริ่มต้นคือ database ปัจจุบัน เลือกได้หลายตัวหรือเลือก **All databases** (ไม่รวม system database) ระบบจะโหลดทีละ database และแสดงผลทันทีที่เจอ ส่วน database ที่ offline หรือไม่มีสิทธิ์จะถูกข้ามและบันทึกไว้ใน log
- **Server (dropdown):** ค่าเริ่มต้นคือ "Active query window" (ใช้ server ของหน้าต่าง query ที่ active อยู่) หรือเลือก server อื่นที่เคยต่อในรอบนี้ก็ได้ ถ้าหน้าต่าง query ที่ active เปลี่ยนไปต่อ server อื่น ระบบจะกลับไปใช้ server ของหน้าต่างนั้นเอง
- **กระโดดไปที่ object:** ดับเบิลคลิกหรือกด **Enter** ที่ผลลัพธ์ แล้ว Object Explorer จะกาง tree ไปที่ object นั้นและเลือกไว้ให้ (ถ้า Object Explorer ยังไม่ได้ต่อ server นั้น ระบบจะต่อให้เอง) ส่วน **Ctrl+Enter** จะเปิด query window ใหม่ที่มี `USE [db]` ตามด้วย definition และเลือกคำที่เจอไว้ คลิกขวาที่ผลลัพธ์จะมีเมนู Locate in Object Explorer, Open in new query window และ Copy name (Ctrl+C)
- **ผลลัพธ์:** ตารางคอลัมน์ Name, Schema, Type, Parent, Database, Matched in คลิกที่หัวคอลัมน์เพื่อเรียงได้ แสดงสูงสุด 5,000 รายการ
- **Preview:** แสดง definition และ highlight คำที่เจอ ถ้าเป็นตารางจะแสดง `CREATE TABLE` แบบย่อ (คอลัมน์, type, NULL/NOT NULL, primary key) ส่วนคอลัมน์จะแสดงของตาราง/view ที่คอลัมน์นั้นอยู่

ข้อมูลที่ใช้ค้นจะถูกโหลดมาเก็บไว้ใน memory ทีละ database แล้ว cache ไว้ 10 นาที (เกิน 10 นาทีจะโหลดใหม่เบื้องหลัง) ถ้าต้องการโหลดใหม่ทันทีให้กดปุ่ม **Refresh** ในหน้าต่าง หรือกด Ctrl+Shift+R / **Tools › SqlAssist › Refresh Schema Cache**

ทุกครั้งที่เปิด SSMS ใหม่ หน้าต่าง Search จะเริ่มจากค่าเริ่มต้นเสมอ (ทุกชนิด object, ไม่ติ๊กตัวเลือกการค้น, database ปัจจุบัน) ค่าที่เลือกไว้จะใช้ได้จนกว่าจะปิด SSMS

## แก้ไข Snippets

ไปที่ **Tools › SqlAssist › Edit Snippets...** จะเปิดหน้าต่าง **Snippets Manager**

- **รายการด้านซ้าย:** มีช่องค้นหา และเลือกเรียงตาม Shortcut หรือตามคำอธิบายได้
- **ด้านขวา:** แก้ **Shortcut** (คำที่พิมพ์แล้วกด Tab), **คำอธิบาย** และ **ข้อความ** ของ snippet
- **Literals:** ดับเบิลคลิกเพื่อแทรกลงในข้อความ ค่าเหล่านี้จะถูกแทนตอนขยาย snippet

  | Literal | ได้อะไร |
  |---|---|
  | `$CARET$` | ตำแหน่งเคอร์เซอร์หลังขยาย snippet |
  | `$DATE$` / `$TIME$` | วันที่ / เวลาปัจจุบัน |
  | `$USER$` | ชื่อผู้ใช้ Windows |
  | `$DB$` / `$SERVER$` | database / server ของหน้า query |

- **ปุ่มด้านบน:** Snippet ใหม่, ลบ, บันทึก (Ctrl+S), ยกเลิกการแก้ไข (โหลดจากไฟล์ใหม่) และเปิดโฟลเดอร์
- **การตรวจตอนบันทึก:** Shortcut ห้ามว่าง ห้ามซ้ำ และห้ามมีช่องว่าง ส่วนข้อความห้ามว่าง
- **มีผลทันที:** บันทึกแล้วใช้ได้เลย ไม่ต้องเปิด SSMS ใหม่
- **ถ้าปิดหน้าต่างโดยยังไม่ได้บันทึก:** จะถามก่อนว่าจะบันทึกไหม

snippet เก็บอยู่ที่ไฟล์ `%APPDATA%\SqlAssist\snippets.xml` จะแก้ไฟล์นี้ด้วยมือก็ได้ ไฟล์เก่าที่ใช้ `$end$` ยังใช้ได้เหมือนเดิม (มีความหมายเดียวกับ `$CARET$`) snippet แบบหลายบรรทัดจะเยื้องตามบรรทัดที่พิมพ์คำย่อ

## ข้อจำกัด (v1)

- รองรับแค่ Windows auth กับ SQL auth ยังไม่รองรับ Entra ID / Azure AD
- database ที่ใช้มาจาก connection ของหน้าต่าง query (`AdvancedOptions["DATABASE"]` ของ SSMS) ถ้าในสคริปต์มี `USE ชื่อdb` อยู่เหนือ caret จะใช้ database นั้นแทน (ตัวล่าสุดที่อยู่เหนือ caret) แต่มองเห็นแค่ `USE` ที่อยู่ไม่เกินประมาณ 20,000 ตัวอักษรเหนือ caret ถ้าอยู่ไกลกว่านั้นจะกลับไปใช้ database ของหน้าต่าง
- แบ่ง statement ด้วย `;`, `GO` หรือบรรทัดว่าง ถ้าเขียนหลาย query ติดกันโดยไม่มีตัวคั่น ตารางของ query อื่นอาจโผล่มาในรายการด้วย
- คอลัมน์ของ table-valued function มาจาก function ใน database เท่านั้น function ในตัวของ SQL Server (`STRING_SPLIT`, `OPENJSON` ฯลฯ) ยังไม่รู้จัก และ TVF ที่เพิ่งสร้างใหม่ต้องกด **Ctrl+Shift+R** ก่อน
- คอลัมน์ของ CTE ที่คำนวณจาก expression (เช่น `YEAR(OrderDate) AS SalesYear`) จะไม่มีชนิดข้อมูล รู้ชนิดเฉพาะคอลัมน์ที่ดึงมาจากตารางตรง ๆ
- Search: ยังไม่ค้นข้อมูลในตาราง (data search), index, constraint และ user-defined type และ preview หรือ script ของตารางเป็น `CREATE TABLE` แบบย่อ ไม่ใช่ script เต็มแบบของ SSMS ส่วน definition ที่เปิดใน query window เป็นข้อความ `CREATE` ตามที่ SQL Server เก็บไว้ ถ้าจะแก้ต้องเปลี่ยนเป็น `ALTER` เอง
- Search: ถ้าไม่มีหน้าต่าง query ที่ active อยู่ จะใช้ connection ล่าสุดที่เคยเห็น

## เมื่อมีปัญหา

- **Tools › SqlAssist › Options... › เปิดไฟล์ Log** จะเปิด `%LOCALAPPDATA%\SqlAssist\SqlAssist.log` ซึ่งเก็บ error ทุกตัวไว้ (ใน SSMS ต้องกลืน error ทั้งหมดไม่ให้ editor ล่ม)
- ถ้าไม่เห็นเมนู SqlAssist ให้ปิด SSMS แล้วรัน `.\clear-ssms-cache.ps1`

## สำหรับนักพัฒนา

```
src/SqlAssist.Core     logic ล้วน (netstandard2.0) ไม่พึ่ง VS: tokenizer, context, statement scanner, SQL formatter, clipboard → SQL, fuzzy, alias, JOIN, *, snippets, search matcher, CREATE TABLE preview, settings, update policy
src/SqlAssist.Vsix     ส่วนที่เชื่อมกับ SSMS (net48): completion source, item/commit manager, Tab filter, schema cache, Search tool window + index cache, update check
tests/                 xUnit ของ Core
```

- `dotnet test tests\SqlAssist.Core.Tests`
- `dotnet build src\SqlAssist.Vsix -c Release` จะได้ `src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix`
- Debug: ตั้ง start program เป็น `Ssms.exe` ไว้แล้ว (SSMS 22 ไม่มี experimental instance จึงต้องติดตั้งก่อนแล้วค่อย attach หรือ F5)
- ตอน build ไม่ต้องมี SSMS เพราะ connection ของ SSMS อ่านผ่าน reflection เข้า `SQLEditors.dll` ตอน runtime เท่านั้น จึงไม่มี DLL ของ SSMS ถูกอ้างอิงตอน build
- เชื่อมต่อฐานข้อมูลด้วย `System.Data.SqlClient` ที่มากับ .NET Framework จึงไม่ต้องแนบ DLL เพิ่มใน VSIX
