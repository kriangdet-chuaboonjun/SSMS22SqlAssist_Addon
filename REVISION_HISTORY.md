# Revision History

ประวัติการออกเวอร์ชันของ **SqlAssist** (extension สำหรับ SSMS 22) แบบสรุปสั้น ๆ ต่อเวอร์ชัน
รายละเอียดเต็มของแต่ละเวอร์ชันอยู่ใน [CHANGELOG.md](CHANGELOG.md) และไฟล์ติดตั้งอยู่ที่หน้า
[GitHub Releases](https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases)

ของที่แก้แล้วแต่ยังไม่ได้ออกเวอร์ชัน ให้สรุปไว้ใต้ `## [Unreleased]` ก่อน แบ่งเป็น `### เพิ่ม` / `### เปลี่ยน` / `### แก้ไข`
เหมือน CHANGELOG.md พอออกเวอร์ชันใหม่ก็เปลี่ยนหัวข้อเป็น `## [x.y.z] - YYYY-MM-DD` เพิ่มแถวในตาราง
แล้วเปิด `## [Unreleased]` อันใหม่ไว้ด้านบน (`publish-release.ps1` จะไม่ publish ถ้าไฟล์นี้ยังไม่มีเวอร์ชันนั้น)

| เวอร์ชัน | วันที่ | สรุป |
|---|---|---|
| [1.6.1](#161---2026-11-27) | 2026-11-27 | เมนูเลือก Database ไม่ค้าง + ปุ่มล้าง, ตัวติดตั้งกู้คืนเวอร์ชันเดิมได้, Search Index ประหยัดหน่วยความจำ |
| [1.6.0](#160---2026-11-19) | 2026-11-19 | AI Assistant, IntelliSense สำหรับ `#temp` / `@table`, หน้าตา Options และ Search ใหม่ |
| [1.5.7](#157---2026-11-13) | 2026-11-13 | INSERT รู้จักคอลัมน์ IDENTITY, รายการแนะนำเรียนรู้จากที่ใช้บ่อย |
| [1.5.6](#156---2026-11-10) | 2026-11-10 | Find Usages, Generate CRUD procedures, Function Parameter Hints |
| [1.5.4](#154---2026-11-05) | 2026-11-05 | เอา CSV to SQL ออก, แก้ checkbox ต้องคลิก 2 ครั้ง |
| [1.5.3](#153---2026-11-05) | 2026-11-05 | `GROUP BY` + Tab, ไฮไลต์คอลัมน์ใน `INSERT … SELECT` |
| [1.5.2](#152---2026-10-30) | 2026-10-30 | ข้อความ "Press Tab" หลัง `INSERT INTO` / `UPDATE` / `ALTER` |
| [1.5.1](#151---2026-10-30) | 2026-10-30 | ไฮไลต์คอลัมน์กับค่าที่ตรงกันใน `INSERT … VALUES` |
| [1.5.0](#150---2026-10-30) | 2026-10-30 | Tab หลัง `INSERT INTO` / `UPDATE` เขียนใหม่, ย้าย repo ไป organization |

> เวอร์ชัน 1.5.5 ไม่ได้ออก (ข้ามจาก 1.5.4 ไป 1.5.6) · release ของ 1.5.x ถูกลบออกจากหน้า GitHub Releases แล้ว
> (2026-11-20) ไฟล์ติดตั้งที่โหลดได้เริ่มที่ 1.6.0

## [Unreleased]

## [1.6.1] - 2026-11-27

### เพิ่ม

- **หน้าต่าง Search:** ปุ่มล้าง Database ที่เลือกทั้งหมด (รวม All databases) ในเมนูเลือก Database

### เปลี่ยน

- **ตัวติดตั้ง:** เตรียมไฟล์กู้คืนของเวอร์ชันเดิมก่อนอัปเดต ถ้าลงเวอร์ชันใหม่ไม่สำเร็จจะลงเวอร์ชันเดิมกลับให้
- **หน้าต่าง Search:** Search Index ของ database ที่ใหญ่เกินจะค้นทีละส่วนจากฐานข้อมูลแทนการเก็บทั้งหมดในหน่วยความจำ
- **IntelliSense ของ `#temp` / `@table` และปุ่ม Tab:** วิเคราะห์ข้อความในเบื้องหลัง ไม่ต้องรอระหว่างโหลด

### แก้ไข

- **หน้าต่าง Search:** เปิดเมนูเลือก Database ครั้งแรกแล้วไม่ค้างอีก (โหลดรายชื่อไว้ล่วงหน้า)
- **Format SQL:** วงเล็บหรือ `CASE` ซ้อนเกิน 128 ชั้นจะแจ้งเหตุผลและไม่แก้ข้อความ แทนที่จะเสี่ยงทำให้ SSMS หยุดทำงาน

## [1.6.0] - 2026-11-19

### เพิ่ม

- **AI Assistant** (Tools › SqlAssist › AI Assistant…): แชตด้านข้าง editor ถามเรื่อง SQL กับ OpenRouter หรือ Ollama
  ส่งเฉพาะข้อความที่พิมพ์และ SQL ที่แนบเอง, มี Explain / Review / Suggest Optimization, ปุ่ม Copy และ Insert into Editor
  (SqlAssist ไม่รัน SQL ให้) key ของ OpenRouter เก็บใน Windows Credential Manager
- **IntelliSense สำหรับ `#temp` และ `@table`** ที่ประกาศใน Query Editor: เติมชื่อคอลัมน์และขยาย `*` ด้วย Tab

### เปลี่ยน

- หน้าต่าง **Options** ใหม่ (ตั้งค่าซ้าย ตัวอย่าง SQL ขวา สีตามธีม Light/Dark)
- หน้าต่าง **Search** ใช้ dropdown และปุ่มหน้าตาเดียวกับ Options
- ภายใน: การเชื่อมต่อฐานข้อมูลทุกจุดผ่านทางเดียวกัน เตรียมรองรับ Entra ID

## [1.5.7] - 2026-11-13

### เพิ่ม

- **Generate Script As › INSERT รู้จักคอลัมน์ IDENTITY:** ครอบด้วย `SET IDENTITY_INSERT … ON/OFF` หรือตัดคอลัมน์ออก
  (เลือกได้ที่ Options › Results)
- **รายการแนะนำเรียนรู้จากที่ใช้บ่อย:** รายการที่เลือกบ่อยและเพิ่งใช้ขึ้นก่อนรายการที่ตรงพอ ๆ กัน
  (เก็บในเครื่องที่ `%LOCALAPPDATA%\SqlAssist\usage.txt` ปิด/ล้างได้ที่ Options › Editor)

### เปลี่ยน

- **Generate Script As › UPDATE:** คอลัมน์ IDENTITY เริ่มเป็น Key แทน SET
- พิมพ์ต่อจาก `FROM ตาราง alias` แล้ว `WHERE` ขึ้นก่อน `WHEN` / `WITH` / `WHILE`

## [1.5.6] - 2026-11-10

### เพิ่ม

- **Find Usages** (หน้าต่าง Search, คลิกขวาหรือ Shift+F12): ดูว่า object ไหนอ้างถึงตาราง / view / procedure / function /
  คอลัมน์ / user-defined type นั้น
- **Generate CRUD procedures** (หน้าต่าง Search, คลิกขวาที่ตาราง): สร้าง Select / Insert / Update / Delete procedure
  ตั้งรูปแบบชื่อได้ ดู script ก่อนส่งไปหน้าต่าง query / clipboard / ไฟล์ (ไม่รันให้)
- **Function Parameter Hints:** tooltip แสดงรูปแบบการเรียกฟังก์ชัน built-in และฟังก์ชันของผู้ใช้
  พร้อมทำตัวหนาที่ parameter ตำแหน่งปัจจุบัน (ปิดได้ที่ Options › Editor)

## [1.5.4] - 2026-11-05

### เปลี่ยน

- เอาฟังก์ชัน **CSV to SQL** ออก
- รายการแนะนำแบบต้นไม้ตาราง (`SELECT |`) ไม่แสดงบรรทัดคำอธิบายใต้รายการแล้ว

### แก้ไข

- รายการแนะนำแบบต้นไม้ตาราง: checkbox ต้องคลิก 2 ครั้งถึงจะติก ตอนนี้คลิกครั้งเดียว

## [1.5.3] - 2026-11-05

### เพิ่ม

- **`GROUP BY` + Tab:** เติม grouping expression จาก `SELECT` ของ query เดียวกัน (ตัด alias และ aggregate ออก)
- **ไฮไลต์คอลัมน์และรายการที่ตรงกันใน `INSERT … SELECT`:** จับคู่คอลัมน์ปลายทางกับ select item ตามตำแหน่ง

### เปลี่ยน

- ภายใน: แยกตัวอ่านรายการ SELECT มาใช้ร่วมกันระหว่างสองฟีเจอร์ข้างบน

## [1.5.2] - 2026-10-30

### เพิ่ม

- ข้อความ **"Press Tab to …"** ใต้ชื่อตารางหลัง `INSERT INTO t` และ `UPDATE t` เมื่อ Tab ขยายได้จริง
- ข้อความ **"Press Tab to insert the current definition"** หลัง `ALTER PROCEDURE` / `ALTER FUNCTION` / `ALTER VIEW`

## [1.5.1] - 2026-10-30

### เพิ่ม

- **ไฮไลต์คอลัมน์และค่าที่ตรงกันใน `INSERT … VALUES`:** วาง caret ที่ค่าหรือชื่อคอลัมน์ แล้วอีกฝั่งที่ตำแหน่งเดียวกันถูกแรเงา
  (ปิดได้ที่ Options › Editor)

## [1.5.0] - 2026-10-30

### เปลี่ยน

- **Tab หลัง `INSERT INTO ตาราง`:** ขยายเป็นรายชื่อคอลัมน์ + `VALUES` พร้อมค่าตั้งต้นตามชนิดข้อมูล
  (ข้าม IDENTITY / computed / rowversion)
- **Tab หลัง `UPDATE ตาราง`:** ขยายเป็น `SET` + `WHERE` ตาม Primary Key พร้อม `DECLARE @parameter`
  ตารางที่ไม่มี Primary Key ได้ `WHERE /* TODO */` ที่รันไม่ได้จนกว่าจะเขียนเงื่อนไขเอง
- ย้าย repo ไปที่ organization `kriangdet-chuaboonjun` ผู้ใช้ 1.4.3 หรือเก่ากว่าต้องโหลด `SqlAssistSetup-*.exe`
  จากหน้า Releases มาติดตั้งเองหนึ่งครั้ง
