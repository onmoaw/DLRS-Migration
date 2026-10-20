using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Manages the permanent intermediate staging database [DLRS_Migration_Intrm].
    /// Handles database creation, deep schema validation, data staging from source databases,
    /// and completion audit logging in tblUnitMigrationLog.
    /// </summary>
    public static class IntermediateDbService
    {
        public static string DbName => AppConfig.IntermediateDbName;

        /// <summary>
        /// Checks if [DLRS_Migration_Intrm] exists on the connected server instance and returns its compatibility level.
        /// </summary>
        public static async Task<(bool Exists, int CompatLevel)> CheckDatabaseExistsAsync(string masterConnectionString)
        {
            using var conn = new SqlConnection(masterConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT 
                    CASE WHEN DB_ID(@db) IS NOT NULL THEN 1 ELSE 0 END AS ExistsFlag,
                    ISNULL((SELECT compatibility_level FROM sys.databases WHERE name = @db), 0) AS CompatLevel;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@db", DbName);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                bool exists = reader.GetInt32(0) == 1;
                int compat = reader.GetByte(1);
                return (exists, compat);
            }

            return (false, 0);
        }

        /// <summary>
        /// Performs deep schema validation on [DLRS_Migration_Intrm].
        /// Verifies compatibility level (>= 130), required tables (tblHalsabek, tblLinfo01, tblLinfo02, tblUnitMigrationLog),
        /// and required Unicode data types (NVARCHAR(MAX), NVARCHAR(150), RowId, convsts).
        /// </summary>
        public static async Task<(bool IsValid, List<string> Errors)> ValidateSchemaAsync(string masterConnectionString)
        {
            var errors = new List<string>();

            try
            {
                using var conn = new SqlConnection(masterConnectionString);
                await conn.OpenAsync();

                // 1. Check DB exists
                using (var cmd = new SqlCommand("SELECT DB_ID(@db);", conn))
                {
                    cmd.Parameters.AddWithValue("@db", DbName);
                    var dbId = await cmd.ExecuteScalarAsync();
                    if (dbId == null || dbId == DBNull.Value)
                    {
                        errors.Add($"Database [{DbName}] does not exist on this server.");
                        return (false, errors);
                    }
                }

                // 2. Check Compatibility Level >= 130
                using (var cmd = new SqlCommand("SELECT compatibility_level FROM sys.databases WHERE name = @db;", conn))
                {
                    cmd.Parameters.AddWithValue("@db", DbName);
                    int compat = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                    if (compat < 130)
                    {
                        errors.Add($"Compatibility level is {compat} (minimum 130 required for JSON and modern Unicode processing).");
                    }
                }

                // 3. Validate tblHalsabek
                using (var cmd = new SqlCommand($@"
                    SELECT 
                        CASE WHEN OBJECT_ID('{DbName}.dbo.tblHalsabek') IS NOT NULL THEN 1 ELSE 0 END AS TableExists,
                        CASE WHEN COL_LENGTH('{DbName}.dbo.tblHalsabek', 'convsts') IS NOT NULL THEN 1 ELSE 0 END AS HasConvsts;", conn))
                {
                    using var dr = await cmd.ExecuteReaderAsync();
                    if (await dr.ReadAsync())
                    {
                        if (dr.GetInt32(0) == 0) errors.Add("Missing table dbo.tblHalsabek.");
                        else if (dr.GetInt32(1) == 0) errors.Add("dbo.tblHalsabek is missing tracking column 'convsts'.");
                    }
                }

                // 4. Validate tblLinfo01
                using (var cmd = new SqlCommand($@"
                    SELECT 
                        CASE WHEN OBJECT_ID('{DbName}.dbo.tblLinfo01') IS NOT NULL THEN 1 ELSE 0 END AS TableExists,
                        ISNULL((SELECT system_type_id FROM [{DbName}].sys.columns WHERE object_id = OBJECT_ID('[{DbName}].dbo.tblLinfo01') AND name = 'KRemark'), 0) AS KRemarkType,
                        CASE WHEN COL_LENGTH('{DbName}.dbo.tblLinfo01', 'convsts') IS NOT NULL THEN 1 ELSE 0 END AS HasConvsts,
                        CASE WHEN COL_LENGTH('{DbName}.dbo.tblLinfo01', 'ROWID') IS NOT NULL THEN 1 ELSE 0 END AS HasRowId;", conn))
                {
                    using var dr = await cmd.ExecuteReaderAsync();
                    if (await dr.ReadAsync())
                    {
                        if (dr.GetInt32(0) == 0)
                        {
                            errors.Add("Missing table dbo.tblLinfo01.");
                        }
                        else
                        {
                            int kremarkType = Convert.ToInt32(dr["KRemarkType"]);
                            if (kremarkType != 231) // 231 = nvarchar
                                errors.Add("dbo.tblLinfo01.KRemark must be NVARCHAR (Unicode).");
                            if (Convert.ToInt32(dr["HasConvsts"]) == 0)
                                errors.Add("dbo.tblLinfo01 is missing tracking column 'convsts'.");
                            if (Convert.ToInt32(dr["HasRowId"]) == 0)
                                errors.Add("dbo.tblLinfo01 is missing column 'ROWID'.");
                        }
                    }
                }

                // 5. Validate tblLinfo02
                using (var cmd = new SqlCommand($@"
                    SELECT 
                        CASE WHEN OBJECT_ID('{DbName}.dbo.tblLinfo02') IS NOT NULL THEN 1 ELSE 0 END AS TableExists,
                        ISNULL((SELECT max_length FROM [{DbName}].sys.columns WHERE object_id = OBJECT_ID('[{DbName}].dbo.tblLinfo02') AND name = 'OwnName'), 0) AS OwnNameLen,
                        CASE WHEN COL_LENGTH('{DbName}.dbo.tblLinfo02', 'RowId') IS NOT NULL THEN 1 ELSE 0 END AS HasRowId,
                        CASE WHEN COL_LENGTH('{DbName}.dbo.tblLinfo02', 'convsts') IS NOT NULL THEN 1 ELSE 0 END AS HasConvsts;", conn))
                {
                    using var dr = await cmd.ExecuteReaderAsync();
                    if (await dr.ReadAsync())
                    {
                        if (dr.GetInt32(0) == 0)
                        {
                            errors.Add("Missing table dbo.tblLinfo02.");
                        }
                        else
                        {
                            int ownNameLen = Convert.ToInt32(dr["OwnNameLen"]);
                            if (ownNameLen < 300) // 300 bytes = 150 nvarchar characters
                                errors.Add("dbo.tblLinfo02.OwnName must be NVARCHAR(150) or larger.");
                            if (Convert.ToInt32(dr["HasRowId"]) == 0)
                                errors.Add("dbo.tblLinfo02 is missing column 'RowId'.");
                            if (Convert.ToInt32(dr["HasConvsts"]) == 0)
                                errors.Add("dbo.tblLinfo02 is missing tracking column 'convsts'.");
                        }
                    }
                }

                // 6. Validate tblUnitMigrationLog
                using (var cmd = new SqlCommand($@"
                    SELECT CASE WHEN OBJECT_ID('{DbName}.dbo.tblUnitMigrationLog') IS NOT NULL THEN 1 ELSE 0 END;", conn))
                {
                    int logTableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                    if (logTableCount == 0)
                    {
                        errors.Add("Missing unit audit tracking table dbo.tblUnitMigrationLog.");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Connection or validation check failed: {ex.Message}");
            }

            return (errors.Count == 0, errors);
        }

        /// <summary>
        /// Executes the complete DDL creation and repair script to ensure [DLRS_Migration_Intrm]
        /// exists with all pre-prepared tables and columns.
        /// </summary>
        public static async Task CreateOrRepairDatabaseAsync(string masterConnectionString)
        {
            using var conn = new SqlConnection(masterConnectionString);
            await conn.OpenAsync();

            // 1. Create database if missing
            string createDbSql = $@"
                IF DB_ID('{DbName}') IS NULL
                BEGIN
                    CREATE DATABASE [{DbName}];
                END;";
            using (var cmd = new SqlCommand(createDbSql, conn))
            {
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Set compatibility level (try 150 first, fallback to 130)
            try
            {
                using var cmd = new SqlCommand($"ALTER DATABASE [{DbName}] SET COMPATIBILITY_LEVEL = 150;", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                using var cmd = new SqlCommand($"ALTER DATABASE [{DbName}] SET COMPATIBILITY_LEVEL = 130;", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // 3. Create or repair tables in [DLRS_Migration_Intrm]
            var intrmBuilder = new SqlConnectionStringBuilder(masterConnectionString)
            {
                InitialCatalog = DbName
            };
            using var intrmConn = new SqlConnection(intrmBuilder.ConnectionString);
            await intrmConn.OpenAsync();

            string ddlTables = @"
                SET ANSI_NULLS ON;
                SET QUOTED_IDENTIFIER ON;

                -- 3A. tblHalsabek
                IF OBJECT_ID('dbo.tblHalsabek') IS NULL
                BEGIN
                    CREATE TABLE dbo.tblHalsabek (
                        UnitCod   CHAR(9) NULL,
                        KhtNum    NVARCHAR(9) NULL,
                        PltNum    NVARCHAR(13) NULL,
                        tacrdes   NUMERIC(10,4) NULL,
                        sacrdes   NUMERIC(10,4) NULL,
                        HSPltNum  NVARCHAR(13) NULL,
                        hsbdec    NUMERIC(10,4) NULL,
                        entdt     DATETIME NULL,
                        enttm     CHAR(8) NULL,
                        kpaco     CHAR(40) NULL,
                        actdata   NUMERIC(1,0) NULL,
                        UsrId     NVARCHAR(10) NULL,
                        TermID    NVARCHAR(20) NOT NULL DEFAULT N'',
                        convsts   NVARCHAR(20) NOT NULL DEFAULT N''
                    );
                END
                ELSE
                BEGIN
                    IF COL_LENGTH('dbo.tblHalsabek', 'convsts') IS NULL
                        ALTER TABLE dbo.tblHalsabek ADD convsts NVARCHAR(20) NOT NULL DEFAULT N'';
                END;

                -- 3B. tblLinfo01
                IF OBJECT_ID('dbo.tblLinfo01') IS NULL
                BEGIN
                    CREATE TABLE dbo.tblLinfo01 (
                        UnitCod   CHAR(9) NOT NULL,
                        KhtNum    NVARCHAR(9) NOT NULL,
                        PltNum    NVARCHAR(13) NOT NULL,
                        LndTyp    INT NOT NULL,
                        Tax       NUMERIC(8,2) NULL,
                        KriUse    INT NULL,
                        NKriUse   INT NULL,
                        TPltArea  NUMERIC(10,4) NULL,
                        KhtShare  NUMERIC(7,4) NULL,
                        kshare    NUMERIC(1,0) NULL,
                        kshare1   NUMERIC(2,0) NULL,
                        kshare2   NUMERIC(2,0) NULL,
                        kshare3   NUMERIC(1,0) NULL,
                        kshare4   NUMERIC(1,0) NULL,
                        kshare5   NUMERIC(2,0) NULL,
                        KPltArea  NUMERIC(10,4) NULL,
                        KRemark   NVARCHAR(MAX) NULL,
                        hsbplot   NUMERIC(6,0) NULL,
                        hsbbata   NUMERIC(6,0) NULL,
                        hsbdec    NUMERIC(10,4) NULL,
                        Easmnt    NUMERIC(1,0) NULL,
                        EasArea   NUMERIC(10,4) NULL,
                        eascom1   NVARCHAR(60) NULL,
                        eascom2   NVARCHAR(60) NULL,
                        eascom3   NVARCHAR(60) NULL,
                        eascom4   NVARCHAR(60) NULL,
                        eascom5   NVARCHAR(60) NULL,
                        eascom6   NVARCHAR(60) NULL,
                        tacrdesx  NUMERIC(10,4) NULL,
                        sacrdesx  NUMERIC(10,4) NULL,
                        pltsharex NUMERIC(7,4) NULL,
                        EntDate   DATETIME NULL,
                        EntTime   SMALLDATETIME NULL,
                        LUpDate   SMALLDATETIME NULL,
                        LUpTime   SMALLDATETIME NULL,
                        LUpNumb   NUMERIC(3,0) NULL,
                        kpaco     NVARCHAR(40) NULL,
                        ROWID     INT NOT NULL,
                        UsrId     NVARCHAR(10) NULL,
                        TermID    NVARCHAR(20) NOT NULL DEFAULT N'',
                        convsts   NVARCHAR(20) NOT NULL DEFAULT N''
                    );
                END
                ELSE
                BEGIN
                    IF COL_LENGTH('dbo.tblLinfo01', 'convsts') IS NULL
                        ALTER TABLE dbo.tblLinfo01 ADD convsts NVARCHAR(20) NOT NULL DEFAULT N'';
                    IF COL_LENGTH('dbo.tblLinfo01', 'ROWID') IS NULL
                        ALTER TABLE dbo.tblLinfo01 ADD ROWID INT NOT NULL DEFAULT 0;
                    ALTER TABLE dbo.tblLinfo01 ALTER COLUMN KRemark NVARCHAR(MAX) NULL;
                END;

                -- 3C. tblLinfo02
                IF OBJECT_ID('dbo.tblLinfo02') IS NULL
                BEGIN
                    CREATE TABLE dbo.tblLinfo02 (
                        UnitCod   CHAR(9) NOT NULL,
                        KhtNum    NVARCHAR(9) NOT NULL,
                        OwnrGrp   NUMERIC(2,0) NULL,
                        OwnrNum   NUMERIC(3,0) NOT NULL,
                        OwnName   NVARCHAR(150) NULL,
                        addr1     NVARCHAR(150) NULL,
                        addr2     NVARCHAR(150) NULL,
                        addr3     NVARCHAR(150) NULL,
                        addr4     NVARCHAR(150) NULL,
                        addr5     NVARCHAR(150) NULL,
                        addr6     NVARCHAR(150) NULL,
                        addr7     NVARCHAR(150) NULL,
                        addr8     NVARCHAR(150) NULL,
                        addr9     NVARCHAR(150) NULL,
                        addr10    NVARCHAR(150) NULL,
                        OwnrArea  NUMERIC(8,4) NULL,
                        hdec      NUMERIC(10,4) NULL,
                        lshare    NUMERIC(1,0) NULL,
                        lshare1   NUMERIC(2,0) NULL,
                        lshare2   NUMERIC(2,0) NULL,
                        lshare3   NUMERIC(1,0) NULL,
                        lshare4   NUMERIC(1,0) NULL,
                        lshare5   NUMERIC(2,0) NULL,
                        hdecx     NUMERIC(10,4) NULL,
                        losdecx   NUMERIC(8,4) NULL,
                        oinf      NVARCHAR(50) NULL,
                        EntDate   DATETIME NULL,
                        EntTime   SMALLDATETIME NULL,
                        LUpDate   SMALLDATETIME NULL,
                        LUpTime   SMALLDATETIME NULL,
                        LUpNumb   NUMERIC(3,0) NULL,
                        lpaco     NVARCHAR(50) NULL,
                        UsrId     NVARCHAR(10) NULL,
                        TermID    NVARCHAR(20) NOT NULL DEFAULT N'',
                        case_no   NVARCHAR(150) NOT NULL DEFAULT N'',
                        RowId     INT NULL,
                        convsts   NVARCHAR(20) NOT NULL DEFAULT N''
                    );
                END
                ELSE
                BEGIN
                    IF COL_LENGTH('dbo.tblLinfo02', 'convsts') IS NULL
                        ALTER TABLE dbo.tblLinfo02 ADD convsts NVARCHAR(20) NOT NULL DEFAULT N'';
                    IF COL_LENGTH('dbo.tblLinfo02', 'RowId') IS NULL
                        ALTER TABLE dbo.tblLinfo02 ADD RowId INT NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN OwnName NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr1 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr2 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr3 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr4 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr5 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr6 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr7 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr8 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr9 NVARCHAR(150) NULL;
                    ALTER TABLE dbo.tblLinfo02 ALTER COLUMN addr10 NVARCHAR(150) NULL;
                END;

                -- 3D. tblUnitMigrationLog (Audit & Tracking Table)
                IF OBJECT_ID('dbo.tblUnitMigrationLog') IS NULL
                BEGIN
                    CREATE TABLE dbo.tblUnitMigrationLog (
                        LogId             BIGINT IDENTITY(1,1) PRIMARY KEY,
                        UnitCod           VARCHAR(20) NOT NULL,
                        SourceDbName      NVARCHAR(128) NOT NULL,
                        HalsabekRowCount  INT NOT NULL DEFAULT 0,
                        Linfo01RowCount   INT NOT NULL DEFAULT 0,
                        Linfo02RowCount   INT NOT NULL DEFAULT 0,
                        TotalRecords      INT NOT NULL DEFAULT 0,
                        Status            NVARCHAR(50) NOT NULL DEFAULT N'COMPLETED',
                        CompletedDate     DATETIME NOT NULL DEFAULT GETDATE(),
                        ExecutedBy        NVARCHAR(50) NULL,
                        MachineName       NVARCHAR(50) NULL,
                        Remarks           NVARCHAR(500) NULL
                    );

                    CREATE NONCLUSTERED INDEX IX_tblUnitMigrationLog_UnitCod ON dbo.tblUnitMigrationLog (UnitCod, SourceDbName);
                END;";

            using (var cmd = new SqlCommand(ddlTables, intrmConn))
            {
                cmd.CommandTimeout = 180;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Stages (copies) records from the legacy source database into [DLRS_Migration_Intrm].
        /// Applies NVARCHAR conversions, sets initial convsts, and computes sequential RowId for tblLinfo02.
        /// </summary>
        public static async Task<(int HalsabekCount, int Linfo01Count, int Linfo02Count)> StageDataFromSourceAsync(
            string masterConnectionString, 
            string sourceDbName, 
            string? unitCod = null)
        {
            using var conn = new SqlConnection(masterConnectionString);
            await conn.OpenAsync();

            int hsbCount = 0, l01Count = 0, l02Count = 0;

            // 1. Copy tblHalsabek
            string sqlHsb = $@"
                TRUNCATE TABLE [{DbName}].dbo.tblHalsabek;

                INSERT INTO [{DbName}].dbo.tblHalsabek (
                    UnitCod, KhtNum, PltNum, tacrdes, sacrdes, HSPltNum, hsbdec, entdt, enttm, kpaco, actdata, UsrId, TermID, convsts
                )
                SELECT 
                    UnitCod, KhtNum, PltNum, tacrdes, sacrdes, HSPltNum, hsbdec, entdt, enttm, kpaco, actdata, UsrId, 
                    ISNULL(TermID, N''), 
                    " + (await ColumnExistsAsync(conn, sourceDbName, "tblHalsabek", "convsts") ? "ISNULL(convsts, N'')" : "N''") + $@"
                FROM [{sourceDbName}].dbo.tblHalsabek
                WHERE (@u IS NULL OR LTRIM(RTRIM(UnitCod)) = @u);

                SELECT COUNT(*) FROM [{DbName}].dbo.tblHalsabek;";

            using (var cmd = new SqlCommand(sqlHsb, conn))
            {
                cmd.CommandTimeout = 300;
                cmd.Parameters.AddWithValue("@u", string.IsNullOrWhiteSpace(unitCod) ? (object)DBNull.Value : unitCod.Trim());
                hsbCount = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }

            // 2. Copy tblLinfo01 (with KRemark converted to NVARCHAR(MAX))
            bool hasSrcRowId = await ColumnExistsAsync(conn, sourceDbName, "tblLinfo01", "ROWID");
            bool hasSrcConvsts01 = await ColumnExistsAsync(conn, sourceDbName, "tblLinfo01", "convsts");

            string sql01 = $@"
                TRUNCATE TABLE [{DbName}].dbo.tblLinfo01;

                INSERT INTO [{DbName}].dbo.tblLinfo01 (
                    UnitCod, KhtNum, PltNum, LndTyp, Tax, KriUse, NKriUse, TPltArea, KhtShare, 
                    kshare, kshare1, kshare2, kshare3, kshare4, kshare5, KPltArea, KRemark, 
                    hsbplot, hsbbata, hsbdec, Easmnt, EasArea, eascom1, eascom2, eascom3, eascom4, eascom5, eascom6, 
                    tacrdesx, sacrdesx, pltsharex, EntDate, EntTime, LUpDate, LUpTime, LUpNumb, kpaco, 
                    ROWID, UsrId, TermID, convsts
                )
                SELECT 
                    UnitCod, KhtNum, PltNum, LndTyp, Tax, KriUse, NKriUse, TPltArea, KhtShare, 
                    kshare, kshare1, kshare2, kshare3, kshare4, kshare5, KPltArea, 
                    CAST(KRemark AS NVARCHAR(MAX)), 
                    hsbplot, hsbbata, hsbdec, Easmnt, EasArea, eascom1, eascom2, eascom3, eascom4, eascom5, eascom6, 
                    tacrdesx, sacrdesx, pltsharex, EntDate, EntTime, LUpDate, LUpTime, LUpNumb, kpaco, 
                    " + (hasSrcRowId ? "ISNULL(NULLIF(ROWID, 0), ROW_NUMBER() OVER (ORDER BY UnitCod, KhtNum, PltNum))" : "ROW_NUMBER() OVER (ORDER BY UnitCod, KhtNum, PltNum)") + $@",
                    UsrId, 
                    ISNULL(TermID, N''), 
                    " + (hasSrcConvsts01 ? "ISNULL(convsts, N'')" : "N''") + $@"
                FROM [{sourceDbName}].dbo.tblLinfo01
                WHERE (@u IS NULL OR LTRIM(RTRIM(UnitCod)) = @u);

                SELECT COUNT(*) FROM [{DbName}].dbo.tblLinfo01;";

            using (var cmd = new SqlCommand(sql01, conn))
            {
                cmd.CommandTimeout = 300;
                cmd.Parameters.AddWithValue("@u", string.IsNullOrWhiteSpace(unitCod) ? (object)DBNull.Value : unitCod.Trim());
                l01Count = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }

            // 3. Copy tblLinfo02 (expanding fields & generating sequential RowId)
            bool hasCaseNo = await ColumnExistsAsync(conn, sourceDbName, "tblLinfo02", "case_no");
            bool hasSrcConvsts02 = await ColumnExistsAsync(conn, sourceDbName, "tblLinfo02", "convsts");

            string sql02 = $@"
                TRUNCATE TABLE [{DbName}].dbo.tblLinfo02;

                WITH RankedSource AS (
                    SELECT 
                        UnitCod, KhtNum, OwnrGrp, OwnrNum, 
                        CAST(OwnName AS NVARCHAR(150)) AS OwnName, 
                        CAST(addr1 AS NVARCHAR(150)) AS addr1, 
                        CAST(addr2 AS NVARCHAR(150)) AS addr2, 
                        CAST(addr3 AS NVARCHAR(150)) AS addr3, 
                        CAST(addr4 AS NVARCHAR(150)) AS addr4, 
                        CAST(addr5 AS NVARCHAR(150)) AS addr5, 
                        CAST(addr6 AS NVARCHAR(150)) AS addr6, 
                        CAST(addr7 AS NVARCHAR(150)) AS addr7, 
                        CAST(addr8 AS NVARCHAR(150)) AS addr8, 
                        CAST(addr9 AS NVARCHAR(150)) AS addr9, 
                        CAST(addr10 AS NVARCHAR(150)) AS addr10, 
                        OwnrArea, hdec, lshare, lshare1, lshare2, lshare3, lshare4, lshare5, 
                        hdecx, losdecx, oinf, EntDate, EntTime, LUpDate, LUpTime, LUpNumb, lpaco, UsrId, 
                        ISNULL(TermID, N'') AS TermID, 
                        " + (hasCaseNo ? "ISNULL(case_no, N'') AS case_no" : "N'' AS case_no") + $@",
                        ROW_NUMBER() OVER (ORDER BY UnitCod, KhtNum, OwnrGrp, OwnrNum) AS CalculatedRowId,
                        " + (hasSrcConvsts02 ? "ISNULL(convsts, N'') AS convsts" : "N'' AS convsts") + $@"
                    FROM [{sourceDbName}].dbo.tblLinfo02
                    WHERE (@u IS NULL OR LTRIM(RTRIM(UnitCod)) = @u)
                )
                INSERT INTO [{DbName}].dbo.tblLinfo02 (
                    UnitCod, KhtNum, OwnrGrp, OwnrNum, OwnName, 
                    addr1, addr2, addr3, addr4, addr5, addr6, addr7, addr8, addr9, addr10, 
                    OwnrArea, hdec, lshare, lshare1, lshare2, lshare3, lshare4, lshare5, 
                    hdecx, losdecx, oinf, EntDate, EntTime, LUpDate, LUpTime, LUpNumb, lpaco, UsrId, 
                    TermID, case_no, RowId, convsts
                )
                SELECT 
                    UnitCod, KhtNum, OwnrGrp, OwnrNum, OwnName, 
                    addr1, addr2, addr3, addr4, addr5, addr6, addr7, addr8, addr9, addr10, 
                    OwnrArea, hdec, lshare, lshare1, lshare2, lshare3, lshare4, lshare5, 
                    hdecx, losdecx, oinf, EntDate, EntTime, LUpDate, LUpTime, LUpNumb, lpaco, UsrId, 
                    TermID, case_no, CalculatedRowId, convsts
                FROM RankedSource;

                SELECT COUNT(*) FROM [{DbName}].dbo.tblLinfo02;";

            using (var cmd = new SqlCommand(sql02, conn))
            {
                cmd.CommandTimeout = 300;
                cmd.Parameters.AddWithValue("@u", string.IsNullOrWhiteSpace(unitCod) ? (object)DBNull.Value : unitCod.Trim());
                l02Count = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }

            return (hsbCount, l01Count, l02Count);
        }

        /// <summary>
        /// Records a completed migration run for each migrated unit code into [DLRS_Migration_Intrm].dbo.tblUnitMigrationLog.
        /// </summary>
        public static async Task<int> LogCompletedUnitsAsync(
            string masterConnectionString, 
            string sourceDbName, 
            string status = "COMPLETED", 
            string? remarks = null)
        {
            try
            {
                using var conn = new SqlConnection(masterConnectionString);
                await conn.OpenAsync();

                string userName = UserSession.CurrentUsername;
                string machineName = Environment.MachineName;

                // Determine scan database: check if sourceDbName has tblHalsabek or tblLinfo01
                string scanDb = DbName;
                if (!string.IsNullOrWhiteSpace(sourceDbName))
                {
                    bool srcHasHsb = await TableExistsAsync(conn, sourceDbName, "tblHalsabek");
                    bool srcHas01 = await TableExistsAsync(conn, sourceDbName, "tblLinfo01");
                    if (srcHasHsb || srcHas01)
                    {
                        scanDb = sourceDbName;
                    }
                }

                string sql = $@"
                    INSERT INTO [{DbName}].dbo.tblUnitMigrationLog (
                        UnitCod, SourceDbName, HalsabekRowCount, Linfo01RowCount, Linfo02RowCount, TotalRecords, Status, CompletedDate, ExecutedBy, MachineName, Remarks
                    )
                    SELECT 
                        COALESCE(h.UnitCod, p.UnitCod, o.UnitCod) AS UnitCod,
                        @sourceDb,
                        ISNULL(h.HsbCount, 0),
                        ISNULL(p.P01Count, 0),
                        ISNULL(o.O02Count, 0),
                        ISNULL(h.HsbCount, 0) + ISNULL(p.P01Count, 0) + ISNULL(o.O02Count, 0),
                        @status,
                        GETDATE(),
                        @userName,
                        @machineName,
                        @remarks
                    FROM 
                    (
                        SELECT LTRIM(RTRIM(UnitCod)) AS UnitCod, COUNT(*) AS HsbCount 
                        FROM [{scanDb}].dbo.tblHalsabek 
                        WHERE UnitCod IS NOT NULL AND LEN(LTRIM(RTRIM(UnitCod))) > 0
                        GROUP BY LTRIM(RTRIM(UnitCod))
                    ) h
                    FULL OUTER JOIN
                    (
                        SELECT LTRIM(RTRIM(UnitCod)) AS UnitCod, COUNT(*) AS P01Count 
                        FROM [{scanDb}].dbo.tblLinfo01 
                        WHERE UnitCod IS NOT NULL AND LEN(LTRIM(RTRIM(UnitCod))) > 0
                        GROUP BY LTRIM(RTRIM(UnitCod))
                    ) p ON h.UnitCod = p.UnitCod
                    FULL OUTER JOIN
                    (
                        SELECT LTRIM(RTRIM(unitcod)) AS UnitCod, COUNT(*) AS O02Count 
                        FROM [{scanDb}].dbo.tblLinfo02 
                        WHERE unitcod IS NOT NULL AND LEN(LTRIM(RTRIM(unitcod))) > 0
                        GROUP BY LTRIM(RTRIM(unitcod))
                    ) o ON COALESCE(h.UnitCod, p.UnitCod) = o.UnitCod;";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@sourceDb", sourceDbName);
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@userName", (object?)userName ?? Environment.UserName);
                cmd.Parameters.AddWithValue("@machineName", machineName);
                cmd.Parameters.AddWithValue("@remarks", (object?)remarks ?? $"Migrated from {sourceDbName} into {DbName}");

                return await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Reads all completed migration log entries from [DLRS_Migration_Intrm].dbo.tblUnitMigrationLog.
        /// </summary>
        public static async Task<DataTable> GetMigrationLogsAsync(string masterConnectionString)
        {
            var dt = new DataTable();
            using var conn = new SqlConnection(masterConnectionString);
            await conn.OpenAsync();

            string sql = $@"
                SELECT 
                    LogId,
                    UnitCod AS [Unit Code],
                    SourceDbName AS [Source DB],
                    HalsabekRowCount AS [Halsabek Rows],
                    Linfo01RowCount AS [Linfo01 Rows],
                    Linfo02RowCount AS [Linfo02 Rows],
                    TotalRecords AS [Total Records],
                    Status,
                    FORMAT(CompletedDate, 'yyyy-MM-dd HH:mm:ss') AS [Completed At],
                    ExecutedBy AS [User],
                    MachineName AS [Machine]
                FROM [{DbName}].dbo.tblUnitMigrationLog
                ORDER BY LogId DESC;";

            using var cmd = new SqlCommand(sql, conn);
            using var adapter = new SqlDataAdapter(cmd);
            adapter.Fill(dt);

            return dt;
        }

        private static async Task<bool> ColumnExistsAsync(SqlConnection conn, string db, string table, string column)
        {
            string sql = $"SELECT CASE WHEN COL_LENGTH('[{db}].dbo.[{table}]', '{column}') IS NOT NULL THEN 1 ELSE 0 END;";
            using var cmd = new SqlCommand(sql, conn);
            var res = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(res ?? 0) == 1;
        }

        private static async Task<bool> TableExistsAsync(SqlConnection conn, string db, string table)
        {
            try
            {
                string sql = $"SELECT CASE WHEN OBJECT_ID('[{db}].dbo.[{table}]') IS NOT NULL THEN 1 ELSE 0 END;";
                using var cmd = new SqlCommand(sql, conn);
                var res = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(res ?? 0) == 1;
            }
            catch
            {
                return false;
            }
        }
    }
}
