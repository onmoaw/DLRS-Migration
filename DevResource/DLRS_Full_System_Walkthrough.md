# DLRS Data Migration System — Complete Technical Walkthrough

> **Document Version:** 1.0 | **Last Updated:** 2026-09-30
> **Project:** LandPress Data Migration Desktop App
> **Stack:** C# WPF (.NET), SQL Server 2016+, T-SQL Stored Procedures
> **Repo Root:** `e:\DLRS_DATA-MIGRATION\01. WPF\01. Main\`

---

## Table of Contents

1. Purpose & Background
2. Project Structure
3. Architecture Diagram
4. Database Roles
5. Source Table Schema (Legacy)
6. Target Table Schema (DLRSLSINF_4103PRSDB)
7. Stored Procedure Catalogue
8. C# Layer — DBAccess Class Library
9. Phase 0 — DB Preparation Wizard (NEW)
10. Phase 1 — Connection & DB Selection
11. Phase 2 — Data Fetch Into Memory
12. Phase 3 — Font Conversion (In-Memory)
13. Phase 4 — Write Unicode Data Back to Source DB
14. Phase 5 — SP_LIS_DATA_MIGRATION Deep Dive
15. convsts Row Status Lifecycle
16. unitcod Transformation
17. addr1 Intelligence Parsing
18. mismchlog — Audit Trail
19. Complete End-to-End Flow Diagram
20. Key Design Decisions & Rationale
21. Known Stubs & Future Work

---

## 1. Purpose & Background

Bangladesh's Department of Land Records and Surveys (DLRS) maintains land ownership data in
legacy databases using an older Bangla font encoding called **Adarshalipi** — a proprietary
8-bit mapping that predates Unicode. These databases are one per district or upazila.

**Goals of this project:**

1. **Convert** all Bangla text fields from Adarshalipi encoding to proper **Unicode (UTF-16)**.
2. **Normalize** the data from three loosely-linked legacy tables into a structured, relational
   schema in a single unified target database (`DLRSLSINF_4103PRSDB`).
3. **Validate** data integrity by detecting mismatches, orphaned records, and duplicates,
   logging them with full JSON snapshots for later inspection.
4. Provide a **fully GUI-driven workflow** so operators can run the entire pipeline step-by-step
   without writing any SQL.

---

## 2. Project Structure

```
e:\DLRS_DATA-MIGRATION\01. WPF\01. Main\
|
|-- LandPressDataMigrationDesktopApp\          WPF UI Project
|   |-- MainWindow.xaml / .cs                  Main application window
|   |-- DbPrepWizardWindow.xaml / .cs          NEW: 6-step DB Prep Wizard
|   |-- App.xaml / .cs
|
|-- DBAccess\                                  Data access class library
|   |-- DataAccessParams.cs                    SP parameter wrapper model
|   |-- DataMigrationEntityView00.cs           C# models: tblLinfo01 / tblLinfo02
|   |-- DataMigrationViewman00.cs              SP call builders (factory pattern)
|   |-- Geninfo.cs                             HTTP client / JWT token holder
|   |-- UtilityFunction.cs                     ListToJsonString, ConvertDataTable, etc.
|   |-- WebProcAccess.cs                       Web API access layer
|
|-- FontConverter\                             Bangla font conversion library
|   |-- Converter.cs                           ConvertToAdarshalipi()
|
|-- DevResource\                               SQL scripts & reference docs
    |-- CheckandChangeDbCompatibilityLevelsqlNew.sql   Source for DB Prep Wizard
    |-- SP_LIS_DATA_MIGRATION.txt                      Main migration stored procedure
    |-- DLRS_Full_System_Walkthrough.md                THIS DOCUMENT
```

---

## 3. Architecture Diagram

```
+------------------------------------------------------------------------+
|              WPF Desktop Application                                   |
|                                                                        |
|  MainWindow.xaml                DbPrepWizardWindow.xaml (NEW)         |
|  - Connect to SQL Server        Step 1: Check Compat Levels           |
|  - List all Databases           Step 2: Change Compat Level           |
|  - Show Tables + Row Counts     Step 3: Add RowId (tblLinfo02)        |
|  - Checkbox Table Select        Step 4: KRemark to NVARCHAR(MAX)      |
|  - Fetch Data (streaming)       Step 5: Expand addr columns           |
|  - Font Convert (in-memory)     Step 6: Add convsts column            |
|  - Batch Write-back (JSON)                                            |
|                                                                        |
|  DBAccess Class Library                                               |
|  DataAccessParams   DataMigrationEntityView00   DataMigrationViewman00|
|  UtilityFunction    Geninfo                      WebProcAccess        |
|                                                                        |
|  FontConverter: ConvertToAdarshalipi() -- Adarshalipi to Unicode      |
+------------------------------------------------------------------------+
                         |
              SQL Server  |
+-----------------------------------+  +------------------------------+
| SOURCE DBs (1 per district)       |  | TARGET DB                    |
| chachra, barishal_sbd22122025,    |  | DLRSLSINF_4103PRSDB          |
| softbd_tangail, Dhaka_Pritthi_U   |  |                              |
|                                   |  | dbo_lis.linfo00              |
| tblHalsabek   (plot list)         |  | dbo_lis.linfo01              |
| tblLinfo01    (khatian info)      |  | dbo_lis.linfo02              |
| tblLinfo02    (owner info)        |  | dbo_lis.mapinf01             |
|                                   |  | dbo.mismchlog                |
| SP_LIS_DATA_MIGRATION_REPORT_     |  |                              |
|   CODEBOOK (fetch/update via JSON)|  | SP_LIS_DATA_MIGRATION        |
+-----------------------------------+  +------------------------------+
```

---

## 4. Database Roles

| Role | Example Names | Purpose |
|------|--------------|---------|
| **Source** | chachra, barishal_sbd22122025, softbd_tangail_07012026 | Legacy per-district databases. One per land settlement area. Contains raw Adarshalipi-encoded Bangla text. |
| **Target** | DLRSLSINF_4103PRSDB | Unified DLRS Press database. Receives cleaned, Unicode-encoded, normalized data. |
| **System** | DLRSLSINF_0000SYSDB | Reference database for area codes (SYS_AREAINF). Used by AREAINF migration (currently stubbed). |

**Connection string** is built dynamically in `MainWindow.BuildConnectionString()`:
- Windows auth: `Server=X;Database=Y;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;`
- SQL auth: `Server=X;Database=Y;User Id=U;Password=P;Encrypt=True;TrustServerCertificate=True;`

---

## 5. Source Table Schema (Legacy)

Three tables exist in every source database. Linked by **unitcod + khtnum**.

### tblHalsabek — Plot List per Khatian

| Column | Type | Description |
|--------|------|-------------|
| unitcod | char(9) | Mouza unit code. Leading digit is '0' in source. |
| khtnum | nvarchar(20) | Khatian (land ownership folio) number |
| pltnum | nvarchar(15) | Plot number |
| hspltnum | nvarchar | Historical/original plot number |
| entdt | datetime | Entry date |
| convsts | nvarchar(20) | **Added by Wizard Step 6.** Migration status tracker. |

> tblHalsabek is the "bridge" table: links khatians to their plots. MTO_LINFO01 reads from here.

### tblLinfo01 — Khatian Header / Plot Info

| Column | Type | Description |
|--------|------|-------------|
| unitcod | char(9) | Mouza unit code |
| khtnum | nvarchar(20) | Khatian number |
| pltnum | nvarchar(15) | Plot number |
| KRemark | TEXT -> NVARCHAR(MAX) | Khatian remarks in Adarshalipi Bangla. **Wizard Step 4 changes the type.** |
| LndTyp | nvarchar | Land type code (numeric, prefixed with '0' in target) |
| TPltArea | decimal | Total plot area |
| eascom1-eascom6 | nvarchar | Easement comments |
| UsrId | nvarchar | User who entered the record |
| Entdate | datetime | Entry date |
| convsts | nvarchar(20) | **Added by Wizard Step 6.** |

### tblLinfo02 — Owner Details per Khatian

| Column | Type | Description |
|--------|------|-------------|
| unitcod | char(9) | Mouza unit code |
| khtnum | nvarchar(20) | Khatian number |
| ownrgrp | int | Owner group number |
| ownrnum | int | Owner sequence number within group |
| OwnName | nvarchar(150) | Owner name in Adarshalipi Bangla. **Wizard Step 5 expands to 150.** |
| addr1 | nvarchar(150) | Address line 1. Often encodes relationship (father/spouse prefix). |
| addr2-addr10 | nvarchar(150) each | Remaining address lines. All expanded to 150 by Wizard Step 5. |
| ownrarea | decimal(18,6) | Owner's land share |
| entdate | datetime | Entry date |
| convsts | nvarchar(20) | **Added by Wizard Step 6.** |
| RowId | INT | **Added by Wizard Step 3.** Synthetic unique key for batch updates. |

---

## 6. Target Table Schema (DLRSLSINF_4103PRSDB)

### dbo_lis.linfo00 — Khatian Header (normalized)

| Column | Source / Value | Notes |
|--------|---------------|-------|
| comcod | '4103' | Company/district code |
| rsnum | @Desc02 (e.g. '201901') | Record Settlement number |
| unitcod | '7' + right(src.unitcod,8) | DLRS national unitcod format |
| khtnum | src.khtnum | Khatian number |
| ekhtnum | src.khtnum | Equivalent khatian number |
| pkhtnum | '' | Parent khatian (empty on initial migration) |
| shetnum | '000' | Sheet number |
| ltcode | '0' + src.LndTyp | Land type code prefixed |
| kremark | src.KRemark | **Unicode-converted** khatian remarks |
| khtstatus | 'Final Publication Level' (Bangla) | Fixed status string |
| rowtime | src.Entdate | Original entry timestamp |

### dbo_lis.linfo01 — Plot Records (normalized)

| Column | Source / Value | Notes |
|--------|---------------|-------|
| comcod | '4103' | |
| rsnum | @Desc02 | |
| unitcod | '7' + right(src.unitcod,8) | |
| khtnum | src.khtnum | |
| pltnum | src.hspltnum | Historical plot number from tblHalsabek |
| slnum | ROW_NUMBER() within khatian | Sequential plot slot number |
| lucode | '' | Land use code (not populated yet) |
| tpltarea | src.TPltArea | From tblLinfo01 join |
| khtshare | src.khtshare | From tblLinfo01 join |
| kpltarea | src.kpltarea | From tblLinfo01 join |
| kpltrmrk | src.KRemark | Unicode remarks from tblLinfo01 |
| spltnum | STRING_AGG(hspltnum) | All plot numbers aggregated per khatian |
| rowtime | src.entdt | |

### dbo_lis.linfo02 — Owner Records (normalized)

| Column | Source / Value | Notes |
|--------|---------------|-------|
| comcod | '4103' | |
| rsnum | @Desc02 | |
| unitcod | '7' + right(src.unitcod,8) | |
| khtnum | src.khtnum | |
| ownrgrp | src.ownrgrp | |
| ownrnum | src.ownrnum | |
| ownname | src.OwnName | **Unicode-converted** owner name |
| gender | Derived from addr1 | 'Female' if addr1 starts with spouse prefix |
| nidnum | '' | Not available in legacy data |
| bregnum | '' | Not available |
| fathernam | Derived from addr1 | If addr1 starts with father prefix |
| mothernam | '' | Not available |
| spousenam | Derived from addr1 | If addr1 starts with spouse prefix |
| phone01 | '' | Not available |
| email01 | '' | Not available |
| address01 | addr2+addr3+...+addr10 | Concatenated, max 350 chars. Unicode-converted. |
| ownrarea | src.ownrarea | Owner's land share |
| rowtime | src.entdate | |

### dbo_lis.mapinf01 — Map Unit Table

| Column | Value |
|--------|-------|
| comcod | '4103' |
| rsnum | '201901' |
| unitcod | Distinct unitcod from tblHalsabek |
| shetnum | '000' |

### dbo.mismchlog — Mismatch/Duplicate Audit Log

| Column | Type | Description |
|--------|------|-------------|
| comcod | nchar(4) | Company code, always '4103' |
| unitcod | nchar(9) | Source unitcod (with leading '0', NOT transformed to '7') |
| khtnum | nvarchar(20) | Khatian number |
| rowjson | nvarchar(max) | Full row as JSON (FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) |
| rowsts | nvarchar(20) | 'MISMATCH' or 'DUPLICATE' |
| tblnam | nvarchar(20) | 'tblLinfo01', 'tblLinfo02', or 'tblHalsabek' |

> Rows only inserted if exact match (comcod+unitcod+khtnum+tblnam+rowjson) does not already exist.


---

## 7. Stored Procedure Catalogue

### In SOURCE databases

| SP Name | ProcID | Purpose |
|---------|--------|---------|
| SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK | GETROWCOUNTS | Returns row counts of tblLinfo01 and tblLinfo02 |
| SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK | SLCTLINFO01 | Selects UnitCod, KhtNum, PltNum, KRemark, eascom1-6, rowid from tblLinfo01 |
| SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK | SLCTLINFO02 | Selects OwnName, addr1-addr10, rowid from tblLinfo02 |
| SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK | UPDTTBLINF01 | Receives JSON batch, parses with OPENJSON, UPDATEs tblLinfo01 by rowid |
| SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK | UPDTTBLINF02 | Receives JSON batch, parses with OPENJSON, UPDATEs tblLinfo02 by rowid |

### In TARGET database (DLRSLSINF_4103PRSDB)

| SP Name | ProcID | Purpose |
|---------|--------|---------|
| SP_LIS_DATA_MIGRATION | MTO_AREAINF | Migrate area info (STUB — all code commented out) |
| SP_LIS_DATA_MIGRATION | MANAGE_MISSMATCH | Pre-migration data integrity audit |
| SP_LIS_DATA_MIGRATION | MTO_LINFO00 | Migrate khatian headers to linfo00 |
| SP_LIS_DATA_MIGRATION | MTO_LINFO01 | Migrate plot records to linfo01 |
| SP_LIS_DATA_MIGRATION | MTO_LINFO02 | Migrate owner records to linfo02 |
| SP_LIS_DATA_MIGRATION | UPDTMAPINF01 | Insert distinct unitcods into mapinf01 |
| SP_LIS_DATA_MIGRATION | MTO_PLOTINF01 | Migrate plot info (STUB — returns immediately) |

### SP_LIS_DATA_MIGRATION Full Signature

```sql
CREATE PROCEDURE [dbo].[SP_LIS_DATA_MIGRATION]
    @ComCod  nvarchar(2500) = '',    -- Company code e.g. '4103'
    @ProcID  nvarchar(2500) = '',    -- Dispatch key e.g. 'MTO_LINFO00'
    @Dxml01  xml            = null,
    @Dxml02  xml            = null,
    @Dbin01  varbinary(max) = null,
    @Desc01  nvarchar(2500) = '',    -- Source DB name e.g. 'chachra'
    @Desc02  nvarchar(2500) = '',    -- RS number e.g. '201901'
    @Desc03  nvarchar(2500) = '',
    -- ... @Desc04-@Desc10 nvarchar(2500)
    @Desc11  nvarchar(max) = '',
    -- ... @Desc12-@Desc20 nvarchar(max)
```

Internal settings:
```sql
SET NOCOUNT ON;
SET XACT_ABORT ON;         -- uncommittable transaction on constraint violation
SET QUERY_GOVERNOR_COST_LIMIT 0;  -- no cost limit on heavy queries
```

---

## 8. C# Layer — DBAccess Class Library

### DataAccessParams.cs — Universal SP Parameter Wrapper

```csharp
[Serializable]
public class DataAccessParams
{
    public string ComCod    { get; set; }   // Company code ("0000" for utility calls)
    public string ProcName  { get; set; }   // SP name e.g. "dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK"
    public string ProcID    { get; set; }   // Dispatch key e.g. "SLCTLINFO01"
    public string parmJson1 { get; set; }   // JSON payload 1 (used for batch updates)
    public string parmJson2 { get; set; }   // JSON payload 2
    public byte[] parmBin01 { get; set; }   // Binary payload
    public string parm01    { get; set; }   // maps to @Desc01 (usually source DB name)
    public string parm02    { get; set; }   // maps to @Desc02 (usually RS number)
    // ... parm03-parm20
}
```

---

### DataMigrationEntityView00.cs — C# Entity Models

```csharp
// Maps to columns fetched by SLCTLINFO01
public class tblLinfo01
{
    public string UnitCod  { get; set; } = string.Empty;   // unitcod
    public string KhtNum   { get; set; } = string.Empty;   // khtnum
    public string PltNum   { get; set; } = string.Empty;   // pltnum
    public string KRemark  { get; set; } = string.Empty;   // KRemark (converted in Phase 3)
    public string eascom1  { get; set; } = string.Empty;   // easement comments 1-6
    public string eascom2  { get; set; } = string.Empty;
    public string eascom3  { get; set; } = string.Empty;
    public string eascom4  { get; set; } = string.Empty;
    public string eascom5  { get; set; } = string.Empty;
    public string eascom6  { get; set; } = string.Empty;
    public int    rowid    { get; set; } = 0;              // synthetic row ID
}

// Maps to columns fetched by SLCTLINFO02
public class tblLinfo02
{
    public string OwnName  { get; set; } = string.Empty;  // Owner name (converted in Phase 3)
    public string addr1    { get; set; } = string.Empty;  // may encode relationship
    public string addr2    { get; set; } = string.Empty;  // address lines 2-10
    public string addr3    { get; set; } = string.Empty;
    public string addr4    { get; set; } = string.Empty;
    public string addr5    { get; set; } = string.Empty;
    public string addr6    { get; set; } = string.Empty;
    public string addr7    { get; set; } = string.Empty;
    public string addr8    { get; set; } = string.Empty;
    public string addr9    { get; set; } = string.Empty;
    public string addr10   { get; set; } = string.Empty;
    public int    rowid    { get; set; } = 0;             // synthetic row ID
}
```

---

### DataMigrationViewman00.cs — SP Call Factory

```csharp
// Pre-configured DataAccessParams builders for each operation:

// Fetch tblLinfo01 records from source DB
GetParametersForLoadingtblLinfo01Data(string dbName)
// -> ProcID="SLCTLINFO01", parm01=dbName

// Fetch tblLinfo02 records from source DB
GetParametersForLoadingtblLinfo02Data(string dbName)
// -> ProcID="SLCTLINFO02", parm01=dbName

// Batch update tblLinfo01 with Unicode-converted data
GetParametersForUpdateingtblLinfo01(string json, string dbName)
// -> ProcID="UPDTTBLINF01", parm01=dbName, parmJson1=json

// Batch update tblLinfo02 with Unicode-converted data
GetParametersForUpdateingtblLinfo02(string json, string dbName)
// -> ProcID="UPDTTBLINF02", parm01=dbName, parmJson1=json
```

---

### UtilityFunction.cs — Key Utility Methods

```csharp
// Converts a List<T> to a JSON string with a named wrapper key.
// Used to build @Djson1 payload for UPDTTBLINF01 / UPDTTBLINF02
ListToJsonString(List<object> dataList, string key) -> string

// Converts a DataTable to a typed List<T>
ConvertDataTable<T>(DataTable dt) -> List<T>

// Determines image type from base64 encoded data
GetImageTypeFromBase64(string base64Image) -> string

// Custom batch extension method for chunking large lists
IEnumerable<List<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
```

---

### Geninfo.cs — HTTP Client & JWT

```csharp
public class Geninfo
{
    public static HttpClient McApiClient = new HttpClient();
    public static JwtTokenInfo ApiToken;

    // Base address options (localhost dev / live servers):
    // McApiClient.BaseAddress = new Uri("http://localhost:8099/api/");
    // McApiClient.BaseAddress = new Uri("http://103.159.73.222:8033/api/");

    public static async Task GetApiToken()
    // POST to /api/Jwt with UserId + Password -> stores JWT in ApiToken
}
```

> NOTE: The API/JWT system is partially built for future server-side integration.
> The current migration pipeline uses DIRECT SQL connections, not the HTTP API.

---

## 9. Phase 0 — DB Preparation Wizard (NEW)

**Files:** `DbPrepWizardWindow.xaml` / `DbPrepWizardWindow.xaml.cs`
**Constructor:** `DbPrepWizardWindow(string connectionString, string dbName)`
**Triggered by:** "Prepare Database" button in MainWindow (visible after selecting a DB)
**Source SQL logic from:** `DevResource\CheckandChangeDbCompatibilityLevelsqlNew.sql`

### Wizard UI Design
- Dark-themed window: 900x680 pixels, background `#1A1A2E`
- Header: gradient from `#0F3460` to `#16213E` with DB name shown
- Step indicator strip: numbered dots (blue=active, green=complete, grey=future)
- Scrollable step content area with card-style step panels
- Footer: Previous / Next / Close buttons

### Step 1 — Check Compatibility Levels

**Connects to:** master database (using `MasterConnectionString()` helper)
**Button:** "Check Compatibility Levels"

```sql
SELECT
    name AS DatabaseName,
    compatibility_level AS CompatLevel,
    CASE compatibility_level
        WHEN 80  THEN 'SQL Server 2000'
        WHEN 90  THEN 'SQL Server 2005'
        WHEN 100 THEN 'SQL Server 2008 / R2'
        WHEN 110 THEN 'SQL Server 2012'
        WHEN 120 THEN 'SQL Server 2014'
        WHEN 130 THEN 'SQL Server 2016'
        WHEN 140 THEN 'SQL Server 2017'
        WHEN 150 THEN 'SQL Server 2019'
        WHEN 160 THEN 'SQL Server 2022'
        ELSE 'Unknown'
    END AS SqlVersion
FROM sys.databases
ORDER BY name;
```

**Why:** SP_LIS_DATA_MIGRATION uses FOR JSON PATH, OPENJSON, STRING_AGG — all require compat >= 130.

---

### Step 2 — Change Compatibility Level

**Connects to:** master database
**Button:** "Apply Compatibility Level" (with confirmation dialog)
**ComboBox options:** 130 (SQL 2016, default), 140, 150, 160

```sql
ALTER DATABASE [source_db_name] SET COMPATIBILITY_LEVEL = 160;
```

**Level selection logic in C#:**
```csharp
string content = selectedItem.Content?.ToString() ?? "130";
int level = int.Parse(content.Split(' ')[0]);  // parses "130 -- SQL Server 2016 (OPENJSON)"
```

---

### Step 3 — Add RowId Column to tblLinfo02

**Connects to:** source database directly
**Button:** "Add RowId Column"
**Idempotent:** Checks INFORMATION_SCHEMA.COLUMNS before adding

```sql
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'tblLinfo02' AND COLUMN_NAME = 'RowId'
)
BEGIN
    ALTER TABLE dbo.tblLinfo02 ADD RowId INT NULL;
END;

WITH OrderedData AS
(
    SELECT *,
           ROW_NUMBER() OVER (
               ORDER BY UnitCod, KhtNum, OwnrGrp, OwnrNum
           ) AS rn
    FROM dbo.tblLinfo02
)
UPDATE OrderedData SET RowId = rn;
```

**Timeout:** 300 seconds (heavy operation on large tables)
**Why:** tblLinfo02 has no single reliable unique key. RowId enables the OPENJSON batch UPDATE JOIN.

---

### Step 4 — Change KRemark Column Type

**Button:** "Alter KRemark to NVARCHAR(MAX)"

```sql
ALTER TABLE dbo.tblLinfo01
ALTER COLUMN KRemark NVARCHAR(MAX) NULL;
```

**Timeout:** 300 seconds
**Why:** Legacy column type is TEXT (deprecated). OPENJSON and string functions cannot process TEXT.

---

### Step 5 — Expand Address Column Lengths

**Button:** "Expand Address Columns to NVARCHAR(150)"

```sql
DECLARE @sql NVARCHAR(MAX) = N'';
DECLARE @columns TABLE (colname NVARCHAR(50));

INSERT INTO @columns VALUES
    ('OwnName'),('addr1'),('addr2'),('addr3'),('addr4'),('addr5'),
    ('addr6'),('addr7'),('addr8'),('addr9'),('addr10');

SELECT @sql = @sql + N'ALTER TABLE dbo.tblLinfo02 ALTER COLUMN '
              + colname + N' NVARCHAR(150) NULL;' + CHAR(13)
FROM @columns;

EXEC sp_executesql @sql;
```

**Timeout:** 300 seconds
**Why:** Unicode characters may require more bytes than Adarshalipi-encoded text. Without expansion, write-back will truncate data.

---

### Step 6 — Add convsts Column

**Button:** "Add convsts Column to All Three Tables"
**Idempotent:** Uses IF NOT EXISTS check on each table

```sql
-- Runs for: tblHalsabek, tblLinfo01, tblLinfo02
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'convsts'
)
BEGIN
    ALTER TABLE dbo.{table}
    ADD convsts NVARCHAR(20) NOT NULL DEFAULT N'';
END
```

**Timeout:** 120 seconds per table
**Why:** SP_LIS_DATA_MIGRATION writes 'SUCCESS', 'MISMATCH', 'DUPLICATE' to this column.
It MUST exist before Phase 5 SPs are run.

---

## 10. Phase 1 — Connection & DB Selection

**File:** `MainWindow.xaml.cs`
**Key fields:**
```csharp
private string _ServerName;
private string _DbName;
private string _baseConnectionString;  // points to master
private const string _BaseDbName = "DLRSLSINF_4103PRSDB";  // fixed target
```

### Connect_Click Flow

```
User types server name in txtServer
User selects auth mode (cmbAuth): Windows or SQL
  If SQL: txtUser and txtPassword become visible
User clicks "Connect"
  |
  v
Validates server name not empty
BuildConnectionString("master")
Open SqlConnection(cs)  -- test connection to master
LoadDatabases(conn):
  SELECT name FROM sys.databases WHERE state_desc='ONLINE' ORDER BY name
  -> Populates lstDatabases ListBox
  -> Shows connection success UI
```

### Databases_SelectionChanged Flow

```
User clicks a DB in lstDatabases
  |
  v
_DbName = selectedItem.ToString()
_baseConnectionString = BuildConnectionString(_DbName)
btnPrepareDb.Visibility = Visible   <- "Prepare Database" button appears

Opens connection to selected DB
Queries INFORMATION_SCHEMA.TABLES for BASE TABLE list
Stores tables in lstTables (as TableItem objects with IsSelected=false)

GetTotalRowCount():
  Connects to DLRSLSINF_4103PRSDB (always the target DB)
  Calls SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK @ProcID='GETROWCOUNTS', @Desc01=_DbName
  Returns: tblLinfo01 row count + tblLinfo02 row count

Populates lstTables:
  - Skips first table (tblHalsabek -- isFirst flag)
  - Displays: "dbo.tblLInfo01 (123456 rows)"
  - Displays: "dbo.tblLInfo02 (456789 rows)"
```

---

## 11. Phase 2 — Data Fetch Into Memory

**Method:** `ShowSelectedTables_Click` -> "Fetch Data for Selected Tables"

### Fetch tblLinfo01 (ProcID: SLCTLINFO01)

```csharp
await Task.Run(() => {
    // Connects to TARGET DB where the utility SP lives
    using var conn = new SqlConnection(BuildConnectionString(_BaseDbName));
    conn.Open();
    using var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn);
    cmd.CommandType = CommandType.StoredProcedure;
    cmd.CommandTimeout = 300;
    cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO01";
    cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value  = _DbName;

    // SequentialAccess streams data column-by-column without buffering rows
    using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

    // Pre-resolve column ordinals ONCE before the loop (performance optimization)
    int unitCodOrd = reader.GetOrdinal("UnitCod");
    int khtNumOrd  = reader.GetOrdinal("KhtNum");
    int pltNumOrd  = reader.GetOrdinal("PltNum");
    int kRemarkOrd = reader.GetOrdinal("KRemark");
    int eascom1Ord = reader.GetOrdinal("eascom1");
    // ... all ordinals
    int rowidOrd   = reader.GetOrdinal("rowid");

    while (reader.Read())
    {
        tblLinfo01List.Add(new tblLinfo01 {
            UnitCod = reader.IsDBNull(unitCodOrd) ? "" : reader.GetString(unitCodOrd),
            KhtNum  = reader.IsDBNull(khtNumOrd)  ? "" : reader.GetString(khtNumOrd),
            PltNum  = reader.IsDBNull(pltNumOrd)  ? "" : reader.GetString(pltNumOrd),
            KRemark = reader.IsDBNull(kRemarkOrd) ? "" : reader.GetString(kRemarkOrd),
            eascom1 = reader.IsDBNull(eascom1Ord) ? "" : reader.GetString(eascom1Ord),
            // ...
            rowid   = reader.IsDBNull(rowidOrd)   ? 0  : reader.GetInt32(rowidOrd),
        });
    }
});
```

### Fetch tblLinfo02 (ProcID: SLCTLINFO02)

Same pattern. Streams: OwnName, addr1-addr10, rowid into `tblLinfo02List`.

### After Both Fetched

```
fontConversionPermission.Visibility = Visible
fontConversionPermission.Text = "All set! You can now start the font conversion."
fontConverterBTN.IsEnabled = true
```

> **Design Note:** `CommandBehavior.SequentialAccess` is mandatory for large datasets.
> Without it, SqlDataReader buffers entire rows in memory. For a table with 500k rows
> each having a NVARCHAR(MAX) KRemark field, this would exhaust system RAM.

---

## 12. Phase 3 — Font Conversion (In-Memory)

**Method:** `fontConverterBTN_On_Click`
**Engine:** `FontConverter.Converter.ConvertToAdarshalipi(string input)`
**Batch size:** 50,000 rows

### tblLinfo01 Conversion

```csharp
int batchNum = 0;
foreach (var batch in tblLinfo01List.Batch(50000))
{
    batchNum++;
    int rowInBatch = 0;
    foreach (var item in batch)
    {
        rowInBatch++;
        if (!string.IsNullOrEmpty(item.KRemark))
            item.KRemark = ConvertToAdarshalipi(item.KRemark);
        // NOTE: eascom1-6 are NOT converted (codes, not Bangla text)

        // UI update every row: "Converting tblLinfo01: Batch X of Y -- Row Z of W"
    }
}
// UI: "tblLInfo01 font conversion completed!"
```

### tblLinfo02 Conversion

```csharp
foreach (var batch in tblLinfo02List.Batch(50000))
{
    foreach (var item in batch)
    {
        item.OwnName = ConvertToAdarshalipi(item.OwnName);
        item.addr1   = ConvertToAdarshalipi(item.addr1);
        item.addr2   = ConvertToAdarshalipi(item.addr2);
        item.addr3   = ConvertToAdarshalipi(item.addr3);
        item.addr4   = ConvertToAdarshalipi(item.addr4);
        item.addr5   = ConvertToAdarshalipi(item.addr5);
        item.addr6   = ConvertToAdarshalipi(item.addr6);
        item.addr7   = ConvertToAdarshalipi(item.addr7);
        item.addr8   = ConvertToAdarshalipi(item.addr8);
        item.addr9   = ConvertToAdarshalipi(item.addr9);
        item.addr10  = ConvertToAdarshalipi(item.addr10);
    }
}
// UI: "tblLInfo02 font conversion completed!"
```

### After All Conversion

```
FontConversionSts.Text = "All done! Font conversion finished!"
FontConversionSts.Visibility = Visible
updateDatabaseBTN.IsEnabled = true
```

> **What ConvertToAdarshalipi does:** The Adarshalipi font system maps Bangla characters
> to code points in the ASCII range using a proprietary scheme. The method applies a
> character-by-character lookup table to translate each input byte/codepoint to its correct
> Unicode Bengali glyph. For example, 0xAB in Adarshalipi may map to U+09B2 (Bengali Letter La).

---

## 13. Phase 4 — Write Unicode Data Back to Source DB

**Method:** `updateDatabaseBTN_On_Click` -> calls `UpdateTblLinfo01InBatchesAsync` and `UpdateTblLinfo02InBatchesAsync`

### UpdateTblLinfo01InBatchesAsync

```csharp
var fixedCS = BuildConnectionString(_BaseDbName);  // connects to DLRSLSINF_4103PRSDB
int totalBatches = (int)Math.Ceiling((double)tblLinfo01List.Count / 50000);
int batchNum = 0;

foreach (var batch in tblLinfo01List.Batch(50000))
{
    batchNum++;
    // Serialize entire batch as JSON with named key
    string jsonstring = UtilityFunction.ListToJsonString(
        new List<object> { batch }, "tbl_tblLinfo01");
    // JSON looks like: {"tbl_tblLinfo01": [{rowid:1, KRemark:"...", ...}, ...]}

    await Task.Run(() => {
        using var conn = new SqlConnection(fixedCS);
        conn.Open();
        using var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@ProcID",  NVarChar, 2500).Value = "UPDTTBLINF01";
        cmd.Parameters.Add("@Desc01",  NVarChar,  200).Value = _DbName;
        cmd.Parameters.Add("@Djson1",  NVarChar,   -1).Value = jsonstring;
        cmd.ExecuteScalar();
    });
    // UI: "Updating tblLinfo01: batch X of Y..."
}
// UI: "tblLinfo01 updated successfully."
```

### What the SP (UPDTTBLINF01) does internally

```sql
-- Parses the JSON payload and does a set-based UPDATE in one statement
UPDATE src
SET src.KRemark = j.KRemark,
    src.eascom1 = j.eascom1,
    -- ... all converted columns
FROM [source_db].dbo.tblLinfo01 src
INNER JOIN OPENJSON(@Djson1, '$.tbl_tblLinfo01')
    WITH (
        rowid    INT,
        KRemark  NVARCHAR(MAX),
        eascom1  NVARCHAR(MAX),
        -- ...
    ) AS j ON src.rowid = j.rowid
```

### UpdateTblLinfo02InBatchesAsync

Same pattern. SP (UPDTTBLINF02) updates OwnName + addr1-addr10 in tblLinfo02.

### Final UI State

```
dbUpdateSts.Text = "All records updated — database is ready for migration!"
dbUpdateSts.Visibility = Visible
```


---

## 14. Phase 5 — SP_LIS_DATA_MIGRATION Deep Dive

**File:** `DevResource\SP_LIS_DATA_MIGRATION.txt`
**Lives in:** DLRSLSINF_4103PRSDB (target DB)
**Called:** Manually from SSMS (future: from WPF UI)

### Dispatch Pattern (GOTO-based routing)

```sql
BEGIN TRY
    if @ProcID = 'MTO_AREAINF'      goto MIGRATE_LANDINFO_TO_AREAINF;
    if @ProcID = 'MANAGE_MISSMATCH' goto MANAGE_THE_MISSMATCH_KHATIANS;
    if @ProcID = 'MTO_LINFO00'      goto MIGRATE_LANDINFO_TO_LINFO00;
    if @ProcID = 'MTO_LINFO01'      goto MIGRATE_LANDINFO_TO_LINFO01;
    if @ProcID = 'MTO_LINFO02'      goto MIGRATE_LANDINFO_TO_LINFO02;
    if @ProcID = 'UPDTMAPINF01'     goto UPDATE_MAPINF01_WITH_UNITCOD;
    if @ProcID = 'MTO_PLOTINF01'    goto MIGRATE_LANDINFO_TO_PLOTINF01;

    -- No match:
    set @ProcID = 'Process ID "' + @ProcID + '" not found to execute';
    raiserror(@ProcID, 16, 1);
END TRY
BEGIN CATCH
    execute dbo.sp_lis_common_utility_info 'XXX', 'GETERRORINFO01', @Desc01=@ProcID;
END CATCH;
```

---

### A. MTO_LINFO00 — Migrate Khatian Headers

**Call example:**
```sql
EXEC [dbo].[SP_LIS_DATA_MIGRATION]
    @ComCod = '4103',
    @ProcID = 'MTO_LINFO00',
    @Desc01 = 'chachra',
    @Desc02 = '201901';
```

**Source:** source_db.dbo.tblLinfo01
**Target:** DLRSLSINF_4103PRSDB.dbo_lis.linfo00

**Step 1 — Create staging temp table:**
```sql
CREATE TABLE #templf00 (
    comcod nchar(4), rsnum nchar(6), unitcod nchar(9), khtnum nvarchar(20),
    ekhtnum nvarchar(20), pkhtnum nvarchar(20), shetnum nvarchar(5),
    ltcode nchar(2), kremark nvarchar(1000), khtnote nvarchar(100),
    khtdat1 xml, khtdat2 xml, khtdat3 xml, khtdat4 xml, khtdat5 xml, khtdat6 xml,
    khtstatus nvarchar(250), usrid nchar(12), sealid nchar(3),
    sealdesc nvarchar(250), usrses nvarchar(10), termid nvarchar(50),
    rowtime datetime
);
```

**Step 2 — Dynamic SQL insert (cross-database):**
```sql
-- Only khatians that exist in ALL THREE source tables:
insert into #templf00
select
    @ComCod as comcod,
    @Desc02 as rsnum,
    '7' + right(ltrim(rtrim(a.unitcod)), 8) as unitcod,  -- unitcod transformation
    ltrim(rtrim(a.khtnum)) as khtnum,
    ltrim(rtrim(a.KhtNum)) as ekhtnum,
    N'' as pkhtnum,
    N'000' AS shetnum,
    N'0' + ltrim(rtrim(a.LndTyp)) as ltcode,             -- land type prefix
    ltrim(rtrim(a.KRemark)) as kremark,                  -- Unicode text (already converted)
    N'' as khtnote,
    NULL AS khtdat1, NULL as khtdat2, NULL as khtdat3,
    NULL as khtdat4, NULL as khtdat5, NULL as khtdat6,
    N'Final Publication Level (Bangla)' as khtstatus,
    ltrim(rtrim(a.UsrId)) as usrid,
    N'000' AS sealid, N'' as sealdesc, N'' as usrses, N'' as termid,
    ltrim(rtrim(a.Entdate)) as ROWTIME
from [chachra].dbo.tbllinfo01 a
where exists (
    select 1 from [chachra].dbo.tblHalsabek h
    where ltrim(rtrim(h.unitcod)) = ltrim(rtrim(a.unitcod))
      and ltrim(rtrim(h.KhtNum))  = ltrim(rtrim(a.KhtNum))
)
  and exists (
    select 1 from [chachra].dbo.tblLinfo02 l2
    where ltrim(rtrim(l2.unitcod)) = ltrim(rtrim(a.unitcod))
      and ltrim(rtrim(l2.KhtNum))  = ltrim(rtrim(a.KhtNum))
);
```

**Step 3 — Deduplicate + insert to target:**
```sql
with temp001 as (
    select *,
           ROW_NUMBER() over (
               partition by comcod, rsnum, unitcod, khtnum
               order by rowtime desc    -- keep latest entry per khatian
           ) as newrownum
    from #templf00
)
insert into DLRSLSINF_4103PRSDB.dbo_lis.linfo00 (...)
select ... from temp001 where newrownum = 1;
```

**Step 4 — Mark source rows SUCCESS:**
```sql
UPDATE [chachra].dbo.tblLinfo01 SET convsts = 'SUCCESS'
FROM [chachra].dbo.tblLinfo01 AS a
INNER JOIN (
    SELECT STUFF(unitcod, 1, 1, '0') AS unitcod, khtnum
    FROM #templf00 GROUP BY STUFF(unitcod, 1, 1, '0'), khtnum
) AS b ON a.unitcod = b.unitcod AND a.khtnum = b.khtnum;
-- NOTE: STUFF(unitcod,1,1,'0') reverses '7...' back to '0...' to match source format
```

**Step 5 — Mismatch detection + logging:**
```sql
-- Find tblLinfo02 rows with no match in tblLinfo01
insert into #tempmissmatch00 (unitcod, KhtNum, RowJson, tblnam)
select distinct ltrim(rtrim(l2.unitcod)), ltrim(rtrim(l2.KhtNum)),
    (select top 1 * from [chachra].[dbo].[tblLinfo02] x
     where ltrim(rtrim(x.unitcod)) = ltrim(rtrim(l2.unitcod))
       and ltrim(rtrim(x.KhtNum))  = ltrim(rtrim(l2.KhtNum))
     FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),  -- full row as JSON
    'tblLinfo02'
from [chachra].[dbo].[tblLinfo02] l2
left join [chachra].[dbo].[tblLinfo01] l1
    on ltrim(rtrim(l2.unitcod)) = ltrim(rtrim(l1.unitcod))
   and ltrim(rtrim(l2.KhtNum))  = ltrim(rtrim(l1.KhtNum))
where ltrim(rtrim(l1.unitcod)) is null or ltrim(rtrim(l1.KhtNum)) is null
-- UNION: also finds tblHalsabek orphans
;

-- Insert to audit log (idempotent -- no duplicate entries)
INSERT INTO dlrslsinf_4103prsdb.[dbo].[mismchlog]
    (comcod, unitcod, khtnum, rowjson, rowsts, tblnam)
SELECT DISTINCT t.comcod, t.unitcod, t.khtnum, t.rowjson, 'MISMATCH', t.tblnam
FROM #tempmissmatch00 t
WHERE NOT EXISTS (
    SELECT 1 FROM dlrslsinf_4103prsdb.[dbo].[mismchlog] d
    WHERE d.comcod = t.comcod AND d.unitcod = t.unitcod
      AND d.khtnum = t.khtnum AND d.tblnam  = t.tblnam
      AND d.rowjson = t.rowjson
);

-- Update convsts in tblLinfo02 and tblHalsabek for mismatch rows
UPDATE [chachra].dbo.tbllinfo02 SET convsts = 'MISMATCH' WHERE ... AND convsts = '';
UPDATE [chachra].dbo.tblhalsabek SET convsts = 'MISMATCH' WHERE ... AND convsts = '';
```

---

### B. MTO_LINFO01 — Migrate Plot Records

**Call example:**
```sql
EXEC [dbo].[SP_LIS_DATA_MIGRATION]
    @ComCod = '4103',
    @ProcID = 'MTO_LINFO01',
    @Desc01 = 'chachra',
    @Desc02 = '201901';
```

**Source:** source_db.dbo.tblHalsabek
**Target:** DLRSLSINF_4103PRSDB.dbo_lis.linfo01

**Key temp table (spltnum expanded to nvarchar(max) vs original 500):**
```sql
create table #templf01 (
    comcod nchar(4), rsnum nchar(6), unitcod nchar(9), khtnum nvarchar(20),
    pltnum nvarchar(15), slnum int, lucode nchar(5),
    tpltarea decimal(18,6), khtshare decimal(18,6), kpltarea decimal(18,6),
    kpltrmrk nvarchar(1500), skhtnum nvarchar(250),
    spltnum nvarchar(max),   -- NOTE: expanded from original nvarchar(500)
    cpltnum nvarchar(250), rowtime datetime
);
```

**CTE-based insert with STRING_AGG:**
```sql
with cte as (
    select @comcod as comcod, @desc02 as rsnum,
           '7' + right(ltrim(rtrim(a.unitcod)), 8) as unitcod,
           ltrim(rtrim(a.khtnum)) as khtnum,
           ltrim(rtrim(a.pltnum)) as pltnum,
           -- slnum: increments if khatian already has rows in #templf01
           case when exists (select 1 from #templf01 t
                             where t.unitcod = '7'+right(ltrim(rtrim(a.unitcod)),8)
                               and t.khtnum = ltrim(rtrim(a.khtnum)))
                then (select max(slnum)+1 from #templf01 t
                      where t.unitcod = '7'+right(ltrim(rtrim(a.unitcod)),8)
                        and t.khtnum = ltrim(rtrim(a.khtnum)))
                else 1 end as slnum,
           '' as lucode, 0.0 as tpltarea, 0.0 as khtshare, 0.0 as kpltarea,
           '' as kpltrmrk, '' as skhtnum,
           ltrim(rtrim(a.hspltnum)) as spltnum,  -- historical plot number
           '' as cpltnum,
           a.entdt as rowtime
    from [chachra].dbo.tblHalsabek a
    where exists (-- tblLinfo01 match)
      and exists (-- tblLinfo02 match)
)
insert into #templf01 (...)
select comcod, rsnum, unitcod, khtnum, pltnum,
       ROW_NUMBER() over (partition by @comcod, @desc02,
                          '7'+right(ltrim(rtrim(unitcod)),8), ltrim(rtrim(khtnum))
                          order by pltnum) as slnum,
       max(lucode) as lucode, max(tpltarea) as tpltarea,
       max(khtshare) as khtshare, max(kpltarea) as kpltarea,
       max(kpltrmrk) as kpltrmrk, max(skhtnum) as skhtnum,
       STRING_AGG(CAST(spltnum AS nvarchar(max)), ',') AS spltnum,  -- aggregates plot numbers
       max(cpltnum) as cpltnum, max(rowtime) as rowtime
from CTE group by comcod, rsnum, unitcod, khtnum, pltnum;
```

**Enrich with area data from tblLinfo01:**
```sql
update a set
    a.tpltarea = isnull(b.tpltarea, 0.0),
    a.khtshare = isnull(b.khtshare, 0.0),
    a.kpltarea = isnull(b.kpltarea, 0.0),
    a.kpltrmrk = isnull(b.kremark, '')
from #templf01 a
inner join [chachra].dbo.tbllinfo01 b
    on a.unitcod = '7'+right(ltrim(rtrim(b.unitcod)),8)
   and a.khtnum = b.khtnum and a.pltnum = b.pltnum;
```

**Duplicate detection (checks against existing target records):**
```sql
SELECT l.unitcod, l.khtnum, l.pltnum
INTO #tempchecklf01
FROM dlrslsinf_4103prsdb.dbo_lis.linfo01 l
WHERE EXISTS (SELECT 1 FROM #templf01 t WHERE t.unitcod = l.unitcod);

IF EXISTS (SELECT 1 FROM #tempchecklf01)
BEGIN
    -- Mark source as DUPLICATE
    UPDATE [chachra].dbo.tblHalsabek SET convsts = 'DUPLICATE'
    WHERE ... AND len(ltrim(rtrim(convsts))) = 0;

    -- Log to mismchlog with full JSON snapshot
    INSERT INTO dlrslsinf_4103prsdb.[dbo].[mismchlog] (...)
    SELECT DISTINCT t.comcod, STUFF(t.unitcod,1,1,'0') as unitcod, t.khtnum,
           (SELECT t.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS rowjson,
           'DUPLICATE', 'tblHalsabek'
    FROM #templf01 t INNER JOIN #tempchecklf01 c ON ...
    WHERE NOT EXISTS (-- dedup check on mismchlog);

    -- Delete duplicates from staging BEFORE insert
    DELETE t FROM #templf01 t INNER JOIN #tempchecklf01 c ON ...;
END

-- Insert clean records
INSERT INTO DLRSLSINF_4103PRSDB.dbo_lis.LINFO01 (...) SELECT ... FROM #templf01;

-- Mark source SUCCESS
UPDATE [chachra].dbo.tblHalsabek SET convsts = 'SUCCESS' WHERE ...;

-- Mismatch detection (tblLinfo01 orphans, tblLinfo02 orphans not in tblHalsabek)
-- -> mismchlog insertion + convsts update in tblLinfo01 and tblLinfo02
```

---

### C. MTO_LINFO02 — Migrate Owner Records

**Call example:**
```sql
EXEC [dbo].[SP_LIS_DATA_MIGRATION]
    @ComCod = '4103',
    @ProcID = 'MTO_LINFO02',
    @Desc01 = 'chachra',
    @Desc02 = '201901';
```

**Source:** source_db.dbo.tblLinfo02
**Target:** DLRSLSINF_4103PRSDB.dbo_lis.linfo02

**addr1 Relationship Parsing (key transformation):**
```sql
-- gender: female if addr1 starts with spouse prefix
CASE WHEN ltrim(rtrim(a.addr1)) LIKE N'JNG_PREFIX%' THEN N'Female' ELSE N'' END as gender,

-- fathernam: if addr1 starts with father prefix
CASE WHEN ltrim(rtrim(a.addr1)) LIKE N'FATHER_PREFIX%'
     THEN ltrim(rtrim(a.addr1)) ELSE N'' END as fathernam,

-- spousenam: if addr1 starts with spouse prefix
CASE WHEN ltrim(rtrim(a.addr1)) LIKE N'SPOUSE_PREFIX%'
     THEN ltrim(rtrim(a.addr1)) ELSE N'' END as spousenam,

-- address01: addr2 through addr10 concatenated (max 350 chars)
left(
    ltrim(rtrim(isnull(a.addr2, ''))) + ' ' + ltrim(rtrim(isnull(a.addr3, ''))) + ' '
    + ltrim(rtrim(isnull(a.addr4, ''))) + ' ' + ltrim(rtrim(isnull(a.addr5, ''))) + ' '
    + ltrim(rtrim(isnull(a.addr6, ''))) + ' ' + ltrim(rtrim(isnull(a.addr7, ''))) + ' '
    + ltrim(rtrim(isnull(a.addr8, ''))) + ' ' + ltrim(rtrim(isnull(a.addr9, ''))) + ' '
    + ltrim(rtrim(isnull(a.addr10, ''))),
350) as address01
```

Duplicate check, mismatch logging, and convsts updates follow the same pattern as LINFO01.

---

### D. UPDTMAPINF01 — Update Map Unit Table

**Call example:**
```sql
EXEC [dbo].[SP_LIS_DATA_MIGRATION]
    @ProcID = 'UPDTMAPINF01',
    @Desc01 = 'chachra';
```

```sql
INSERT INTO [dlrslsinf_4103prsdb].[dbo_lis].[mapinf01]
    (comcod, rsnum, unitcod, shetnum)
SELECT '4103' as comcod, '201901' as rsnum, h.unitcod, '000' as shetnum
FROM (SELECT DISTINCT unitcod FROM [chachra].dbo.tblhalsabek) h
WHERE NOT EXISTS (
    SELECT 1 FROM [dlrslsinf_4103prsdb].[dbo_lis].[mapinf01] m
    WHERE m.comcod  = '4103'
      AND m.rsnum   = '201901'
      AND m.unitcod = h.unitcod
      AND m.shetnum = '000'
);
```

Ensures every mouza unit has a map record. Required for the GIS/spatial layer of DLRS.

---

### E. MANAGE_MISSMATCH — Pre-flight Data Integrity Audit

**Call example:**
```sql
EXEC [dbo].[SP_LIS_DATA_MIGRATION]
    @ComCod = '4103',
    @ProcID = 'MANAGE_MISSMATCH',
    @Desc01 = 'chachra';
```

Runs 6 cross-join checks via UNION (all in dynamic SQL with QUOTENAME protection):

```sql
-- (1) khatians in tblLinfo01 NOT in tblHalsabek
select distinct ltrim(rtrim(l1.unitcod)), ltrim(rtrim(l1.KhtNum))
from [chachra].[dbo].[tblLinfo01] l1
left join [chachra].[dbo].[tblHalsabek] h
    on ltrim(rtrim(l1.unitcod)) = ltrim(rtrim(h.unitcod))
   and ltrim(rtrim(l1.KhtNum)) = ltrim(rtrim(h.KhtNum))
where ltrim(rtrim(h.unitcod)) is null or ltrim(rtrim(h.KhtNum)) is null

UNION
-- (2) khatians in tblLinfo01 NOT in tblLinfo02 ...
UNION
-- (3) khatians in tblLinfo02 NOT in tblHalsabek ...
UNION
-- (4) khatians in tblLinfo02 NOT in tblLinfo01 ...
UNION
-- (5) khatians in tblHalsabek NOT in tblLinfo01 ...
UNION
-- (6) khatians in tblHalsabek NOT in tblLinfo02 ...
```

Returns: result set of (unitcod, khtnum) pairs that are incomplete/orphaned.
Must be investigated and resolved BEFORE running MTO_LINFO00/01/02.

---

## 15. convsts Row Status Lifecycle

Added by Wizard Step 6 to: tblHalsabek, tblLinfo01, tblLinfo02.
Written by SP_LIS_DATA_MIGRATION.

```
After Wizard Step 6:         convsts = ''   (empty — not yet processed)
                                 |
          +----------------------+------------------------+
          |                      |                       |
          v                      v                       v
    'MISMATCH'             'DUPLICATE'              'SUCCESS'
    ----------             -----------              ---------
    Khatian is             Row already              Migrated to
    inconsistent           exists in target         target DB
    across 3 tables        linfo table              successfully
          |                      |
          v                      v
    Logged in              Logged in
    dbo.mismchlog          dbo.mismchlog
    with rowjson           with rowjson
    rowsts='MISMATCH'      rowsts='DUPLICATE'
```

**Idempotency rule:** All updates use `WHERE len(ltrim(rtrim(convsts))) = 0`
So rows tagged SUCCESS/MISMATCH/DUPLICATE are NEVER re-tagged on SP re-run.

---

## 16. unitcod Transformation

The old system uses a 9-char area code starting with an admin prefix ('0').
The DLRS national system standardizes it to start with '7'.

```
SOURCE:  '010109104'  (leading '0' = local/district code)
TARGET:  '710109104'  (leading '7' = DLRS national land records prefix)

Formula: '7' + RIGHT(LTRIM(RTRIM(unitcod)), 8)

The 8 meaningful digits after stripping the prefix:
  Digit 1:     Division code
  Digits 2-3:  District code
  Digits 4-5:  Upazila code
  Digits 6-8:  Mouza (village) code
```

**Reverse when updating source tables:**
```sql
STUFF(unitcod, 1, 1, '0')
-- Replaces first character '7' with '0' to match source format
```

Used in MTO_LINFO00, MTO_LINFO01, MTO_LINFO02 when writing convsts back to source.

---

## 17. addr1 Intelligence Parsing

In the legacy tblLinfo02, `addr1` was dual-purpose:

| addr1 Pattern | Extracted To | gender Set To | Bangla Meaning |
|--------------|-------------|--------------|----------------|
| Starts with spouse prefix (জং) | spousenam | 'মহিলা' (Female) | Wife/Husband of [name] |
| Starts with father prefix (পিং) | fathernam | '' (empty) | Father of [name] |
| Anything else | Not extracted | '' | Plain address content |

The remaining lines `addr2` through `addr10` are concatenated into `address01` (max 350 chars).
Empty segments are handled with ISNULL and trimmed before concatenation.

---

## 18. mismchlog — Audit Trail

Every row that fails migration is fully preserved as JSON for investigation.

**Insert pattern (idempotent):**
```sql
INSERT INTO dlrslsinf_4103prsdb.[dbo].[mismchlog]
    (comcod, unitcod, khtnum, rowjson, rowsts, tblnam)
SELECT DISTINCT
    t.comcod, t.unitcod, t.khtnum,
    (SELECT TOP 1 * FROM [chachra].[dbo].[tblLinfo02] x
     WHERE ltrim(rtrim(x.unitcod)) = ltrim(rtrim(t.unitcod))
       AND ltrim(rtrim(x.KhtNum))  = ltrim(rtrim(t.khtnum))
     FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS rowjson,
    'MISMATCH' AS rowsts,
    'tblLinfo02' AS tblnam
FROM #tempmissmatch00 t
WHERE NOT EXISTS (
    -- Exact match check prevents duplicate audit entries
    SELECT 1 FROM dlrslsinf_4103prsdb.[dbo].[mismchlog] d
    WHERE d.comcod  = t.comcod  AND d.unitcod = t.unitcod
      AND d.khtnum  = t.khtnum  AND d.tblnam  = t.tblnam
      AND d.rowjson = t.rowjson
);
```

---

## 19. Complete End-to-End Flow Diagram

```
OPERATOR STEP                    SYSTEM ACTION
=========================================================================

Connect to SQL Server            BuildConnectionString("master")
                                 SqlConnection.Open()
                                 SELECT from sys.databases -> lstDatabases

Click a database                 _DbName = selected
                                 "Prepare Database" button appears
                                 Load tables + row counts -> lstTables

=========================================================================
PHASE 0 — DB PREP WIZARD (run once per source DB)
=========================================================================

Click "Prepare Database"         Opens DbPrepWizardWindow(connectionString, dbName)

Step 1: Click "Check Compat"    SELECT compat_level FROM sys.databases -> DataGrid
Step 2: Select level + Apply    ALTER DATABASE [db] SET COMPATIBILITY_LEVEL = N
Step 3: Click "Add RowId"       ALTER TABLE tblLinfo02 ADD RowId INT NULL
                                 UPDATE tblLinfo02 SET RowId = ROW_NUMBER()
Step 4: Click "Alter KRemark"   ALTER TABLE tblLinfo01 ALTER COLUMN KRemark NVARCHAR(MAX)
Step 5: Click "Expand Addrs"    ALTER TABLE tblLinfo02 ALTER COLUMN OwnName/addr1-10 NVARCHAR(150)
Step 6: Click "Add convsts"     ALTER TABLE tblHalsabek/tblLinfo01/tblLinfo02 ADD convsts NVARCHAR(20)

=========================================================================
PHASE 1-2 — DATA FETCH
=========================================================================

Check tblLInfo01 + tblLInfo02   TableItem.IsSelected = true
Click "Fetch Data"               SP SLCTLINFO01 -> SequentialAccess reader
                                 -> tblLinfo01List (List<tblLinfo01>) in RAM
                                 SP SLCTLINFO02 -> SequentialAccess reader
                                 -> tblLinfo02List (List<tblLinfo02>) in RAM
                                 fontConverterBTN.IsEnabled = true

=========================================================================
PHASE 3 — FONT CONVERSION (in-memory)
=========================================================================

Click "Convert to Unicode"       foreach batch (50,000 rows) in tblLinfo01List:
                                   item.KRemark = ConvertToAdarshalipi(item.KRemark)
                                 foreach batch (50,000 rows) in tblLinfo02List:
                                   item.OwnName + addr1-addr10 = ConvertToAdarshalipi(...)
                                 updateDatabaseBTN.IsEnabled = true

=========================================================================
PHASE 4 — WRITE-BACK TO SOURCE DB
=========================================================================

Click "Update Database"          foreach batch in tblLinfo01List:
                                   JSON = ListToJsonString(batch, "tbl_tblLinfo01")
                                   SP UPDTTBLINF01:
                                     OPENJSON(@json) JOIN by rowid
                                     UPDATE source_db.tblLinfo01.KRemark
                                 foreach batch in tblLinfo02List:
                                   JSON = ListToJsonString(batch, "tbl_tblLinfo02")
                                   SP UPDTTBLINF02:
                                     UPDATE source_db.tblLinfo02 OwnName+addr1-10

=========================================================================
PHASE 5 — SERVER-SIDE MIGRATION (run manually from SSMS)
=========================================================================

OPTIONAL: Run MANAGE_MISSMATCH   Audits cross-table integrity. Returns (unitcod, khtnum)
          @Desc01='chachra'      pairs that are orphaned across the 3 source tables.
                                 Review and fix before proceeding.

Run MTO_LINFO00                  Source: tblLinfo01 (where match in tblHalsabek + tblLinfo02)
  @Desc01='chachra'              Transform: unitcod '0...' -> '7...', ltcode, khtstatus
  @Desc02='201901'               Dedup: ROW_NUMBER() partition by khatian
                                 Target: dbo_lis.linfo00
                                 Mark: tblLinfo01.convsts='SUCCESS'
                                 Mismatch: dbo.mismchlog + tblLinfo02/tblHalsabek.convsts='MISMATCH'

Run MTO_LINFO01                  Source: tblHalsabek (where match in tblLinfo01 + tblLinfo02)
  @Desc01='chachra'              Transform: STRING_AGG plots, slnum, enrich areas from tblLinfo01
  @Desc02='201901'               Dup check: vs existing dbo_lis.linfo01 records
                                 Target: dbo_lis.linfo01
                                 Mark: tblHalsabek.convsts='SUCCESS'/'DUPLICATE'
                                 Mismatch: dbo.mismchlog + tblLinfo01/tblLinfo02.convsts='MISMATCH'

Run MTO_LINFO02                  Source: tblLinfo02 (where match in tblLinfo01 + tblHalsabek)
  @Desc01='chachra'              Transform: addr1 -> gender/fathernam/spousenam
  @Desc02='201901'                         addr2-10 -> address01 (350 char max)
                                 Dup check: vs existing dbo_lis.linfo02 records
                                 Target: dbo_lis.linfo02
                                 Mark: tblLinfo02.convsts='SUCCESS'/'DUPLICATE'
                                 Mismatch: dbo.mismchlog + tblLinfo01/tblHalsabek.convsts='MISMATCH'

Run UPDTMAPINF01                 Source: DISTINCT unitcods from tblHalsabek
  @Desc01='chachra'              Target: dbo_lis.mapinf01 (WHERE NOT EXISTS)
                                 Ensures every mouza has a map record entry

=========================================================================
DONE: Repeat Phases 1-5 for each source district/upazila database
=========================================================================
```

---

## 20. Key Design Decisions & Rationale

| Decision | Implementation | Rationale |
|----------|---------------|-----------|
| **Batch size = 50,000** | `const int batchSize = 50_000` | Balances JSON payload size (~5-10MB), SQL temp table performance, and memory footprint |
| **SequentialAccess streaming** | `CommandBehavior.SequentialAccess` | Enables streaming of million-row tables with NVARCHAR(MAX) columns without exhausting RAM |
| **OPENJSON for batch updates** | SP parses `@Djson1` via OPENJSON | Set-based UPDATE of 50k rows per statement vs 50k individual updates. Requires compat >= 130. |
| **FOR JSON PATH in mismchlog** | `SELECT * FOR JSON PATH, WITHOUT_ARRAY_WRAPPER` | Serializes entire row to JSON without application code. Exact JSON used for idempotent dedup check. |
| **convsts column** | `WHERE len(ltrim(rtrim(convsts))) = 0` | All SPs are safely re-runnable. Previously processed rows (SUCCESS/MISMATCH/DUPLICATE) are never re-tagged. |
| **mismchlog JSON dedup** | `AND d.rowjson = t.rowjson` | Prevents duplicate audit entries on SP re-runs. Exact JSON string comparison ensures truly identical rows are not logged twice. |
| **RowId on tblLinfo02** | Wizard Step 3 adds + populates RowId | tblLinfo02 composite key (unitcod+khtnum+ownrgrp+ownrnum) is complex to pass in JSON. Integer RowId is simpler and faster for OPENJSON JOIN. |
| **STUFF(unitcod,1,1,'0')** | Used in all convsts UPDATE statements | After transforming unitcod '0...' -> '7...' for target, must reverse to '0...' to match source table format when writing convsts. |
| **Dynamic SQL + QUOTENAME** | `QUOTENAME(@Desc01)` wraps source DB name | Prevents SQL injection and handles DB names with spaces or special characters in cross-database queries. |
| **Pre-ordinal resolution** | `int kRemarkOrd = reader.GetOrdinal("KRemark")` before loop | Avoids calling GetOrdinal() millions of times inside the read loop. One-time resolution per result set. |
| **tblHalsabek skipped in UI** | `isFirst = true` flag in SelectionChanged | tblHalsabek is read server-side by the migration SPs. It is not fetched into WPF RAM and needs no checkbox selection. |
| **comcod = '4103'** | Hardcoded in all SPs and UI constants | '4103' is the DLRS Press system company/district code. All migrated records are tagged with this code. |
| **_BaseDbName constant** | `const string _BaseDbName = "DLRSLSINF_4103PRSDB"` | The target database name is fixed. Only the source DB changes per district migration run. |

---

## 21. Known Stubs & Future Work

| Area | Current Status | Detail |
|------|---------------|--------|
| **MTO_AREAINF** | STUB — all code commented out | Was intended to migrate area hierarchy (Division/District/Upazila/Mouza) from DLRSLSINF_0000SYSDB.SYS_AREAINF using a cursor approach per unitcod. Complex unitcod decomposition logic included. |
| **MTO_PLOTINF01** | STUB — returns immediately | Template code present (commented). Intended to populate dbo_lis.plotinf01 with distinct plot data. Template: SELECT DISTINCT from tblLinfo01 WHERE NOT EXISTS in plotinf01. |
| **Phase 5 in WPF** | Not yet integrated | SP_LIS_DATA_MIGRATION is run manually from SSMS. Future "Run Migration" step in WPF would call it with ProcID dropdown and show progress. |
| **API layer** | Partially built | Geninfo.cs + WebProcAccess.cs contain HttpClient/JWT infrastructure suggesting future API integration. Currently the migration pipeline uses direct SqlConnection only. |
| **MTO_LINFO01 (original)** | Commented out (lines 273-341 in SP file) | Original version had spltnum as nvarchar(500). Rewritten version (active, lines 349-488) expands to nvarchar(max) and uses STRING_AGG with explicit CAST. |
| **tblHalsabek from WPF** | Not fetched or converted | tblHalsabek contains no Bangla text columns (only plot numbers and dates), so it needs no font conversion. It is processed entirely server-side in Phase 5. |
| **MANAGE_MISSMATCH return** | Only returns result set | Could be enhanced to auto-generate an Excel/CSV report of mismatched khatians for field officers to investigate. |
| **Mother name (mothernam)** | Always empty string | Not present in legacy tblLinfo02. Would require additional data collection from field offices. |
| **Phone/Email (phone01, email01)** | Always empty string | Not present in legacy data. Modern DLRS field data collection could populate these post-migration. |
