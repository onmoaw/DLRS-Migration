using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Represents an administrative geographic unit from AREAINF.
    /// Strictly adheres to the 9-digit hierarchical breakdown:
    ///   - বিভাগ (Division):        XX0000000 (XX != "00", rest are 0000000)
    ///   - জেলা (District):        XXXX00000 (XX == Div, YY != "00", rest are 00000) -> NEVER XX0000000
    ///   - উপজেলা/থানা (Upazila):   XXXXXX000 (XXXX == Dist, ZZ != "00", rest are 000) -> NEVER XXXX00000
    ///   - মৌজা (Mouza):           XXXXXXXXX (XXXXXX == Upazila, MMM != "000") -> NEVER XXXXXX000
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

        public string DistrictPart => UnitCod.Length >= 4 ? UnitCod.Substring(2, 2) : string.Empty;
        public string UpazilaPart  => UnitCod.Length >= 6 ? UnitCod.Substring(4, 2) : string.Empty;
        public string MouzaPart    => UnitCod.Length >= 9 ? UnitCod.Substring(6, 3) : string.Empty;

        // বিভাগ: XX0000000 (XX != "00", rest are "0000000")
        public bool IsDivision => UnitCod.Length == 9 
                               && DivisionCode != "00" 
                               && UnitCod.Substring(2) == "0000000";

        // জেলা: XXXX00000 (DistrictPart != "00", rest are "00000") -> strictly excludes XX0000000
        public bool IsDistrict => UnitCod.Length == 9 
                               && DivisionCode != "00" 
                               && DistrictPart != "00" 
                               && UnitCod.Substring(4) == "00000";

        // উপজেলা/থানা: XXXXXX000 (UpazilaPart != "00", rest are "000") -> strictly excludes XXXX00000
        public bool IsUpazila  => UnitCod.Length == 9 
                               && DistrictPart != "00" 
                               && UpazilaPart != "00" 
                               && UnitCod.Substring(6) == "000";

        // মৌজা: XXXXXXXXX (MouzaPart != "000") -> strictly excludes XXXXXX000
        public bool IsMouza    => UnitCod.Length == 9 
                               && UpazilaPart != "00" 
                               && MouzaPart != "000";

        // Dropdown value displays ONLY the name without code, per user instruction:
        public string DisplayName => !string.IsNullOrWhiteSpace(UnitNam) ? UnitNam : UnitCod;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Service to manage AREAINF hierarchical queries and execute the dedicated
    /// land records retrieval stored procedure [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS].
    /// NOTE: Auto-deployment has been completely removed as the SP is maintained in the database properly.
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
        /// Directly executes the dedicated stored procedure [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS]
        /// against the core database and returns a DataSet containing 3 tables:
        ///   - Table[0]: Khatians (LINFO00 + LANDTYPE + AREAINF + Plot Aggregates)
        ///   - Table[1]: Owners (LINFO02)
        ///   - Table[2]: Plots (LINFO01 + 3-part Land Classification: 4-Ka Agri, 4-Kha Non-Agri, 4-Ga Local Name)
        /// </summary>
        public static async Task<DataSet> GetMigratedLandRecordsAsync(
            string coreConnectionString,
            string comCod,
            string rsNum,
            string unitCod,
            string? currentKhatian = null,
            string? ownerName = null)
        {
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
    }
}
