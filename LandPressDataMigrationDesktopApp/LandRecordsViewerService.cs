using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Represents an administrative geographic unit from AREAINF.
    /// Supports 9-digit hierarchical breakdown:
    ///   - Division:    XX0000000
    ///   - District:    XXXX00000
    ///   - Upazila:     XXXXXX000
    ///   - Mouza:       XXXXXXXXX
    /// </summary>
    public class AreaHierarchyItem
    {
        public string UnitCod { get; set; } = string.Empty;
        public string UnitNam { get; set; } = string.Empty;
        public string UnitType { get; set; } = string.Empty;
        public string ResaNum { get; set; } = string.Empty;

        public string DivisionCode => UnitCod.Length >= 2 ? UnitCod.Substring(0, 2) : string.Empty;
        public string DistrictCode => UnitCod.Length >= 4 ? UnitCod.Substring(0, 4) : string.Empty;
        public string UpazilaCode  => UnitCod.Length >= 6 ? UnitCod.Substring(0, 6) : string.Empty;

        public bool IsDivision => UnitCod.EndsWith("0000000") && UnitCod != "000000000";
        public bool IsDistrict => UnitCod.EndsWith("00000") && !IsDivision;
        public bool IsUpazila  => UnitCod.EndsWith("000") && !IsDistrict && !IsDivision;
        public bool IsMouza    => !UnitCod.EndsWith("000");

        public string DisplayName => !string.IsNullOrWhiteSpace(UnitNam) ? $"{UnitNam} ({UnitCod})" : UnitCod;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Service to manage AREAINF hierarchical queries and execute the dedicated
    /// land records retrieval stored procedure [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS].
    /// </summary>
    public static class LandRecordsViewerService
    {
        private const string SpFullName = "dbo_lis.SP_LIS_GET_MIGRATED_RECORDS";

        /// <summary>
        /// Retrieves all AREAINF records for the given company/district code (default '4103')
        /// to power in-memory cascading dropdowns.
        /// </summary>
        public static async Task<List<AreaHierarchyItem>> LoadAreaHierarchyAsync(string coreConnectionString, string comCod = "4103")
        {
            var list = new List<AreaHierarchyItem>();

            using var conn = new SqlConnection(coreConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT 
                    LTRIM(RTRIM(UNITCOD)) AS UNITCOD,
                    LTRIM(RTRIM(ISNULL(UNITNAM, ''))) AS UNITNAM,
                    LTRIM(RTRIM(ISNULL(UNITTYPE, ''))) AS UNITTYPE,
                    LTRIM(RTRIM(ISNULL(RESANUM, ''))) AS RESANUM
                FROM dbo.AREAINF
                WHERE COMCOD = @comCod OR COMCOD = '0000'
                ORDER BY UNITCOD;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@comCod", comCod);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new AreaHierarchyItem
                {
                    UnitCod  = reader.GetString(0),
                    UnitNam  = reader.GetString(1),
                    UnitType = reader.GetString(2),
                    ResaNum  = reader.GetString(3)
                });
            }

            return list;
        }

        /// <summary>
        /// Ensures that the dedicated stored procedure [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS]
        /// exists in the target core database, deploying it if missing.
        /// </summary>
        public static async Task EnsureSearchProcedureDeployedAsync(string coreConnectionString)
        {
            using var conn = new SqlConnection(coreConnectionString);
            await conn.OpenAsync();

            using var checkCmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo_lis.SP_LIS_GET_MIGRATED_RECORDS', 'P') IS NOT NULL THEN 1 ELSE 0 END", conn);
            int exists = (int)(await checkCmd.ExecuteScalarAsync() ?? 0);
            if (exists == 1) return;

            // Deploy procedure from DevResource file if found, otherwise from embedded script text
            string sqlScript = string.Empty;
            string devResourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "DevResource", "SP_LIS_GET_MIGRATED_RECORDS.sql");
            if (File.Exists(devResourcePath))
            {
                sqlScript = await File.ReadAllTextAsync(devResourcePath);
            }
            else
            {
                string localDevPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DevResource", "SP_LIS_GET_MIGRATED_RECORDS.sql");
                if (File.Exists(localDevPath))
                {
                    sqlScript = await File.ReadAllTextAsync(localDevPath);
                }
            }

            if (string.IsNullOrWhiteSpace(sqlScript))
            {
                sqlScript = GetEmbeddedSearchProcedureScript();
            }

            var batches = Regex.Split(sqlScript, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            foreach (var batch in batches)
            {
                string trimmed = batch.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("USE ", StringComparison.OrdinalIgnoreCase)) continue;

                using var cmd = new SqlCommand(trimmed, conn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Executes the dedicated stored procedure and returns a DataSet containing 3 tables:
        ///   - Table[0]: Khatians (LINFO00 + LANDTYPE + AREAINF + Plot Count & Total Area)
        ///   - Table[1]: Owners (LINFO02)
        ///   - Table[2]: Plots (LINFO01 + dynamic 4-Ka Agri, 4-Kha Non-Agri, 4-Ga Local Name)
        /// </summary>
        public static async Task<DataSet> GetMigratedLandRecordsAsync(
            string coreConnectionString,
            string comCod,
            string rsNum,
            string unitCod,
            string? currentKhatian = null,
            string? ownerName = null)
        {
            await EnsureSearchProcedureDeployedAsync(coreConnectionString);

            var ds = new DataSet();
            using var conn = new SqlConnection(coreConnectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(SpFullName, conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 300
            };

            cmd.Parameters.Add(new SqlParameter("@ComCod", SqlDbType.NVarChar, 10) { Value = string.IsNullOrWhiteSpace(comCod) ? "4103" : comCod });
            cmd.Parameters.Add(new SqlParameter("@RsNum", SqlDbType.NVarChar, 20) { Value = string.IsNullOrWhiteSpace(rsNum) ? "201901" : rsNum });
            cmd.Parameters.Add(new SqlParameter("@UnitCod", SqlDbType.NVarChar, 20) { Value = unitCod ?? string.Empty });
            cmd.Parameters.Add(new SqlParameter("@CurrentKhatian", SqlDbType.NVarChar, 50) { Value = (object?)currentKhatian ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@OwnerName", SqlDbType.NVarChar, 200) { Value = (object?)ownerName ?? DBNull.Value });

            using var adapter = new SqlDataAdapter(cmd);
            adapter.Fill(ds);

            if (ds.Tables.Count > 0) ds.Tables[0].TableName = "Khatians";
            if (ds.Tables.Count > 1) ds.Tables[1].TableName = "Owners";
            if (ds.Tables.Count > 2) ds.Tables[2].TableName = "Plots";

            return ds;
        }

        private static string GetEmbeddedSearchProcedureScript()
        {
            return @"
CREATE OR ALTER PROCEDURE [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS]
    @ComCod         NVARCHAR(10)  = '4103',
    @RsNum          NVARCHAR(20)  = '201901',
    @UnitCod        NVARCHAR(20),
    @CurrentKhatian NVARCHAR(50)  = NULL,
    @OwnerName      NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @ComCod = ISNULL(NULLIF(LTRIM(RTRIM(@ComCod)), ''), '4103');
    SET @RsNum = ISNULL(NULLIF(LTRIM(RTRIM(@RsNum)), ''), '201901');
    SET @UnitCod = LTRIM(RTRIM(ISNULL(@UnitCod, '')));
    SET @CurrentKhatian = NULLIF(LTRIM(RTRIM(@CurrentKhatian)), '');
    SET @OwnerName = NULLIF(LTRIM(RTRIM(@OwnerName)), '');

    SELECT 
        lf00.COMCOD,
        lf00.RSNUM,
        lf00.UNITCOD,
        ISNULL(a.UNITNAM, lf00.UNITCOD) AS MouzaName,
        CASE WHEN lf00.RSNUM = '201901' THEN N'বিডিএস (BDS)' ELSE lf00.RSNUM END AS SurveyName,
        lf00.KHTNUM AS CurrentKhatian,
        ISNULL(lf00.PKHTNUM, '') AS FormerKhatian,
        ISNULL(lf00.EKHTNUM, '') AS EntryKhatian,
        ISNULL(lf00.SHETNUM, '') AS SheetNum,
        lf00.LTCODE AS LtCode,
        ISNULL(lt.LTNAME, '') AS LandTypeName,
        ISNULL(lt.PTITLE, '') AS PorchaTitle,
        ISNULL(lf00.KREMARK, '') AS KhatianRemarks,
        ISNULL(lf00.KHTNOTE, '') AS KhatianNote,
        ISNULL(lf00.KHTSTATUS, '') AS KhatianStatus,
        lf00.USRID,
        lf00.SEALID,
        ISNULL(lf00.SEALDESC, '') AS SealDesc,
        lf00.TERMID,
        FORMAT(lf00.ROWTIME, 'dd-MMM-yyyy hh:mm tt') AS EntryTime
    INTO #tblKhatian
    FROM dbo_lis.LINFO00 lf00
    LEFT JOIN dbo.AREAINF a ON lf00.COMCOD = a.COMCOD AND lf00.UNITCOD = a.UNITCOD
    LEFT JOIN dbo.LANDTYPE lt ON lf00.LTCODE = lt.LTCODE
    WHERE lf00.COMCOD = @ComCod
      AND (@RsNum = '%' OR lf00.RSNUM = @RsNum)
      AND (lf00.UNITCOD = @UnitCod OR lf00.UNITCOD LIKE @UnitCod + '%')
      AND (@CurrentKhatian IS NULL OR lf00.KHTNUM LIKE '%' + @CurrentKhatian + '%');

    SELECT 
        lf02.COMCOD,
        lf02.RSNUM,
        lf02.UNITCOD,
        lf02.KHTNUM,
        lf02.OWNRGRP AS OwnerGroup,
        lf02.OWNRNUM AS OwnerSerial,
        ISNULL(lf02.OWNNAME, '') AS OwnerName,
        ISNULL(lf02.FATHERNAM, '') AS FatherName,
        ISNULL(lf02.MOTHERNAM, '') AS MotherName,
        ISNULL(lf02.SPOUSENAM, '') AS SpouseName,
        ISNULL(lf02.GENDER, '') AS Gender,
        ISNULL(lf02.NIDNUM, '') AS Nid,
        ISNULL(lf02.BREGNUM, '') AS BirthReg,
        ISNULL(lf02.PHONE01, '') AS Phone,
        ISNULL(lf02.EMAIL01, '') AS Email,
        ISNULL(lf02.ADDRESS01, '') AS Address,
        ISNULL(lf02.OWNRAREA, 0) AS OwnerShareArea
    INTO #tblOwner
    FROM dbo_lis.LINFO02 lf02
    INNER JOIN #tblKhatian k ON lf02.COMCOD = k.COMCOD 
                            AND lf02.RSNUM = k.RSNUM 
                            AND lf02.UNITCOD = k.UNITCOD 
                            AND lf02.KHTNUM = k.CurrentKhatian
    WHERE (@OwnerName IS NULL OR lf02.OWNNAME LIKE '%' + @OwnerName + '%');

    SELECT 
        lf01.COMCOD,
        lf01.RSNUM,
        lf01.UNITCOD,
        lf01.KHTNUM,
        lf01.PLTNUM AS CurrentPlot,
        ISNULL(lf01.SPLTNUM, '') AS FormerPlot,
        ISNULL(lf01.SKHTNUM, '') AS SabekKhatian,
        ISNULL(lf01.CPLTNUM, '') AS CsPlot,
        lf01.SLNUM AS PlotSerial,
        lf01.LUCODE AS LuCode,
        CASE WHEN specLu.LUTYPE = N'কৃষি' THEN ISNULL(specLu.LUNAME, '') ELSE N'' END AS AgriLandClass_4Ka,
        CASE WHEN specLu.LUTYPE = N'অকৃষি' THEN ISNULL(specLu.LUNAME, '') ELSE N'' END AS NonAgriLandClass_4Kha,
        ISNULL(parentLu.LUNAME, '') AS LocalName_4Ga,
        ISNULL(lf01.TPLTAREA, 0) AS TotalPlotArea,
        ISNULL(lf01.KHTSHARE, 0) AS KhatianShare,
        ISNULL(lf01.KPLTAREA, 0) AS KhatianPlotArea,
        ISNULL(lf01.KPLTRMRK, '') AS PlotRemarks
    INTO #tblPlot
    FROM dbo_lis.LINFO01 lf01
    INNER JOIN #tblKhatian k ON lf01.COMCOD = k.COMCOD 
                            AND lf01.RSNUM = k.RSNUM 
                            AND lf01.UNITCOD = k.UNITCOD 
                            AND lf01.KHTNUM = k.CurrentKhatian
    LEFT JOIN dbo.LANDUSE parentLu ON (LEFT(lf01.LUCODE, 2) + '000') = parentLu.LUCODE
    LEFT JOIN dbo.LANDUSE specLu   ON lf01.LUCODE = specLu.LUCODE;

    IF @OwnerName IS NOT NULL
    BEGIN
        DELETE k FROM #tblKhatian k WHERE NOT EXISTS (SELECT 1 FROM #tblOwner o WHERE o.KHTNUM = k.CurrentKhatian);
        DELETE p FROM #tblPlot p WHERE NOT EXISTS (SELECT 1 FROM #tblKhatian k WHERE k.CurrentKhatian = p.KHTNUM);
    END

    SELECT 
        KHTNUM,
        COUNT(DISTINCT CurrentPlot) AS TotalPlots,
        SUM(KhatianPlotArea) AS TotalArea
    INTO #tblPlotAgg
    FROM #tblPlot
    GROUP BY KHTNUM;

    SELECT 
        KHTNUM,
        STRING_AGG(OwnerName, N', ') AS AllOwnerNames
    INTO #tblOwnerSummary
    FROM (
        SELECT DISTINCT KHTNUM, OwnerName 
        FROM #tblOwner 
        WHERE LEN(OwnerName) > 0
    ) uo
    GROUP BY KHTNUM;

    SELECT 
        ROW_NUMBER() OVER (ORDER BY k.CurrentKhatian) AS SlNo,
        k.COMCOD AS ComCod,
        k.RSNUM AS RsNum,
        k.UNITCOD AS UnitCod,
        k.MouzaName,
        k.SurveyName,
        k.CurrentKhatian,
        k.FormerKhatian,
        k.EntryKhatian,
        k.SheetNum,
        k.LtCode,
        k.LandTypeName,
        k.PorchaTitle,
        ISNULL(agg.TotalPlots, 0) AS TotalPlots,
        ISNULL(agg.TotalArea, 0.0000) AS TotalArea,
        ISNULL(os.AllOwnerNames, N'') AS AllOwnerNames,
        k.KhatianStatus,
        k.KhatianRemarks,
        k.KhatianNote,
        k.USRID AS EntryUserId,
        k.SEALID AS SealId,
        k.SealDesc,
        k.TERMID AS TerminalId,
        k.EntryTime
    FROM #tblKhatian k
    LEFT JOIN #tblPlotAgg agg ON k.CurrentKhatian = agg.KHTNUM
    LEFT JOIN #tblOwnerSummary os ON k.CurrentKhatian = os.KHTNUM
    ORDER BY k.CurrentKhatian;

    SELECT 
        ROW_NUMBER() OVER (PARTITION BY o.KHTNUM ORDER BY o.OwnerGroup, o.OwnerSerial) AS SlNo,
        o.COMCOD AS ComCod,
        o.RSNUM AS RsNum,
        o.UNITCOD AS UnitCod,
        o.KHTNUM AS CurrentKhatian,
        o.OwnerGroup,
        o.OwnerSerial,
        o.OwnerName,
        o.FatherName,
        o.MotherName,
        o.SpouseName,
        o.Gender,
        o.Nid,
        o.BirthReg,
        o.Phone,
        o.Email,
        o.Address,
        o.OwnerShareArea
    FROM #tblOwner o
    ORDER BY o.KHTNUM, o.OwnerGroup, o.OwnerSerial;

    SELECT 
        ROW_NUMBER() OVER (PARTITION BY p.KHTNUM ORDER BY p.PlotSerial, p.CurrentPlot) AS SlNo,
        p.COMCOD AS ComCod,
        p.RSNUM AS RsNum,
        p.UNITCOD AS UnitCod,
        p.KHTNUM AS CurrentKhatian,
        p.CurrentPlot,
        p.FormerPlot,
        p.SabekKhatian,
        p.CsPlot,
        p.PlotSerial,
        p.LuCode,
        p.AgriLandClass_4Ka,
        p.NonAgriLandClass_4Kha,
        p.LocalName_4Ga,
        p.TotalPlotArea,
        p.KhatianShare,
        p.KhatianPlotArea,
        p.PlotRemarks
    FROM #tblPlot p
    ORDER BY p.KHTNUM, p.PlotSerial, p.CurrentPlot;

    DROP TABLE IF EXISTS #tblKhatian;
    DROP TABLE IF EXISTS #tblOwner;
    DROP TABLE IF EXISTS #tblPlot;
    DROP TABLE IF EXISTS #tblPlotAgg;
    DROP TABLE IF EXISTS #tblOwnerSummary;
END;
";
        }
    }
}
