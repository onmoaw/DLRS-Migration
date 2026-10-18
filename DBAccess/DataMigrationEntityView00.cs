using System;
using System.Collections.Generic;
using System.Text;

namespace DBAccess
{
    public class DataMigrationEntityView00
    {
        public class JwtTokenUser
        {
            public string UserId { get; set; } = "NOUSER";
            public string Password { get; set; } = "#$^&%##%&**^%$*&";
            public int Expirhour { get; set; } = 2;
        }

        public class DatabaseErrorInfo
        {
            public int errornumber { get; set; }
            public int errorseverity { get; set; }
            public int errorstate { get; set; }
            public string errorprocedure { get; set; }
            public string process_id { get; set; }
            public int errorline { get; set; }
            public string errormessage { get; set; }

            // select error_number() as errornumber, error_severity() as errorseverity, error_state() as errorstate, 
            // error_procedure() as errorprocedure, @Desc01 as process_id, error_line() as errorline, error_message() as errormessage;
        }

        public class tblLinfo01 
        {
            public string UnitCod { get; set; } = string.Empty;
            public string KhtNum { get; set; } = string.Empty;
            public string PltNum { get; set; } = string.Empty;

            public string KRemark { get; set; } = string.Empty;

            public string eascom1 { get; set; } = string.Empty;
            public string eascom2 { get; set; } = string.Empty;
            public string eascom3 { get; set; } = string.Empty;
            public string eascom4 { get; set; } = string.Empty;
            public string eascom5 { get; set; } = string.Empty;
            public string eascom6 { get; set; } = string.Empty;
            public int rowid { get; set; } = 0;

        }



        public class tblLinfo02 
        {
            public string OwnName { get; set; } = string.Empty;
            public string addr1 { get; set; } = string.Empty;
            public string addr2 { get; set; } = string.Empty;
            public string addr3 { get; set; } = string.Empty;
            public string addr4 { get; set; } = string.Empty;
            public string addr5 { get; set; } = string.Empty;
            public string addr6 { get; set; } = string.Empty;
            public string addr7 { get; set; } = string.Empty;
            public string addr8 { get; set; } = string.Empty;
            public string addr9 { get; set; } = string.Empty;
            public string addr10 { get; set; } = string.Empty;
            public int rowid { get; set; } = 0;

        }

    }
}
