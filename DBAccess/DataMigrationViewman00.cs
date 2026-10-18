using System;
using System.Collections.Generic;
using System.Text;

namespace DBAccess
{
    public class DataMigrationViewman00
    {
        public static DataAccessParams GetParametersForLoadingtblLinfo01Data(string dbName)
        {
            return new DataAccessParams(
                _ComCod: "0000",
                _ProcName: "dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK",
                _ProcID: "SLCTLINFO01",
                _parm01: dbName
            );
        }



        public static DataAccessParams GetParametersForLoadingtblLinfo02Data(string dbName)
        {
            return new DataAccessParams(
                _ComCod: "0000",
                _ProcName: "dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK",
                _ProcID: "SLCTLINFO02",
                _parm01: dbName

            );
        }


        public static DataAccessParams GetParametersForUpdateingtblLinfo01(string json, string dbName)
        {
            return new DataAccessParams(
                _ComCod: "0000",
                _ProcName: "dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK",
                _ProcID: "UPDTTBLINF01",
                _parm01: dbName,
                _parmJson1: json
            );
        }


        public static DataAccessParams GetParametersForUpdateingtblLinfo02(string json, string dbName)
        {
            return new DataAccessParams(
                _ComCod: "0000",
                _ProcName: "dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK",
                _ProcID: "UPDTTBLINF02",
                _parm01: dbName,
                _parmJson1: json
            );
        }


    }

}
