namespace DBAccess
{
    [Serializable]
    public class DataAccessParams
    {
        public string ComCod { get; set; }
        public string ProcName { get; set; }
        public string ProcID { get; set; }
        public string parmJson1 { get; set; }
        public string parmJson2 { get; set; }
        public byte[] parmBin01 { get; set; }
        public string parm01 { get; set; }
        public string parm02 { get; set; }
        public string parm03 { get; set; }
        public string parm04 { get; set; }
        public string parm05 { get; set; }
        public string parm06 { get; set; }
        public string parm07 { get; set; }
        public string parm08 { get; set; }
        public string parm09 { get; set; }
        public string parm10 { get; set; }
        public string parm11 { get; set; }
        public string parm12 { get; set; }
        public string parm13 { get; set; }
        public string parm14 { get; set; }
        public string parm15 { get; set; }
        public string parm16 { get; set; }
        public string parm17 { get; set; }
        public string parm18 { get; set; }
        public string parm19 { get; set; }
        public string parm20 { get; set; }


        public DataAccessParams() { }

        public DataAccessParams(string _ComCod = "XXXX", string _ProcName = "XXXXXX", string _ProcID = "XXXXXX", string _parmJson1 = "", string _parmJson2 = "",
                byte[] _parmBin01 = null, string _parm01 = "", string _parm02 = "", string _parm03 = "", string _parm04 = "", string _parm05 = "",
                string _parm06 = "", string _parm07 = "", string _parm08 = "", string _parm09 = "", string _parm10 = "",
                string _parm11 = "", string _parm12 = "", string _parm13 = "", string _parm14 = "", string _parm15 = "", string _parm16 = "",
                string _parm17 = "", string _parm18 = "", string _parm19 = "", string _parm20 = "")
        {
            this.ComCod = _ComCod;
            this.ProcName = _ProcName;
            this.ProcID = _ProcID;
            this.parmJson1 = _parmJson1;
            this.parmJson2 = _parmJson2;
            this.parmBin01 = _parmBin01;
            this.parm01 = _parm01;
            this.parm02 = _parm02;
            this.parm03 = _parm03;
            this.parm04 = _parm04;
            this.parm05 = _parm05;
            this.parm06 = _parm06;
            this.parm07 = _parm07;
            this.parm08 = _parm08;
            this.parm09 = _parm09;
            this.parm10 = _parm10;
            this.parm11 = _parm11;
            this.parm12 = _parm12;
            this.parm13 = _parm13;
            this.parm14 = _parm14;
            this.parm15 = _parm15;
            this.parm16 = _parm16;
            this.parm17 = _parm17;
            this.parm18 = _parm18;
            this.parm19 = _parm19;
            this.parm20 = _parm20;
        }
    }
}
