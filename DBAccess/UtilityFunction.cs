using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace DBAccess
{
    public static class UtilityFunction
    {
        public static string GetImageTypeFromBase64(string base64Image)
        {
            // Remove any data URI prefix (e.g., "data:image/png;base64,")
            base64Image = base64Image.Trim();
            if (base64Image.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var separatorIndex = base64Image.IndexOf(",");
                if (separatorIndex != -1)
                {
                    base64Image = base64Image.Substring(separatorIndex + 1);
                }
            }

            // Convert the base64 string to bytes
            byte[] imageBytes = Convert.FromBase64String(base64Image);

            // Determine the image type based on the magic bytes
            if (imageBytes.Length < 2)
            {
                return null; // Not enough data to determine the image type
            }

            // Create a hex string from the first two bytes of the image data
            string hex = BitConverter.ToString(imageBytes.Take(2).ToArray());

            // Check the hex string to identify the image type
            switch (hex)
            {
                case "FF-D8":
                    return "image/jpeg";
                case "89-50":
                    return "image/png";
                case "47-49":
                    return "image/gif";
                default:
                    return null; // Unknown image type
            }
        }
        public static string FilterString(string SourceString)
        {
            SourceString = SourceString.Trim();
            SourceString = SourceString.Replace("'", "''");
            return SourceString;
        }

        public static List<T> ConvertDataTable<T>(DataTable dt)
        {
            List<T> data = new List<T>();
            foreach (DataRow row in dt.Rows)
            {
                T item = GetItem<T>(row);
                data.Add(item);
            }
            return data;
        }
        public static T GetItem<T>(DataRow dr)
        {
            Type temp = typeof(T);
            T obj = Activator.CreateInstance<T>();

            foreach (DataColumn column in dr.Table.Columns)
            {
                foreach (PropertyInfo pro in temp.GetProperties())
                {
                    if (pro.Name == column.ColumnName)
                        pro.SetValue(obj, dr[column.ColumnName], null);
                    else
                        continue;
                }
            }
            return obj;
        }

        public static string Left(string host, int index)
        {
            return host.Substring(0, index);

        }
        public static string Right(string host, int index)
        {
            return host.Substring(host.Length - index);
        }
        public static string ExprToValue(string cExpr)
        {
            string mExpr1 = cExpr.Trim().Replace(",", "");
            mExpr1 = mExpr1.Replace("/", " div ");
            XmlDocument xmlDoc = new XmlDocument();
            XPathNavigator xPathNavigator = xmlDoc.CreateNavigator();
            mExpr1 = xPathNavigator.Evaluate(mExpr1).ToString();
            return mExpr1;
        }

        public static string Text2Value(string InputValue)
        {
            // Calculator
            string OutputValue = "0.00";
            #region javascript calculat

            Type scriptType = Type.GetTypeFromCLSID(Guid.Parse("0E59F1D5-1FBE-11D0-8FF2-00A0D10038BC"));
            dynamic obj = Activator.CreateInstance(scriptType, false);
            obj.Language = "Javascript";
            string str = null;
            try
            {
                dynamic res = obj.Eval(InputValue);
                str = Convert.ToString(res);
                //this.txtbFResult.Text = this.txtResult.Text + "=" + str;
                OutputValue = str;
            }
            catch (Exception)
            {
                return OutputValue;
                //throw;
            }
            #endregion
            return OutputValue;
        }

        public static string Text2IntValue(string InputValue)
        {
            string OutputValue = "0.00";
            string[] num = Regex.Split(InputValue, @"\-|\+|\*|\/").Where(s => !String.IsNullOrEmpty(s)).ToArray(); // get Array for numbers
            string[] op = Regex.Split(InputValue, @"\d{1,3}").Where(s => !String.IsNullOrEmpty(s)).ToArray(); // get Array for mathematical operators +,-,/,*
            int numCtr = 0, lastVal = 0; // number counter and last Value accumulator
            string lastOp = ""; // last Operator
            foreach (string n in num)
            {
                numCtr++;
                if (numCtr == 1)
                {
                    lastVal = int.Parse(n); // if first loop lastVal will have the first numeric value
                }
                else
                {
                    if (!String.IsNullOrEmpty(lastOp)) // if last Operator not empty
                    {
                        // Do the mathematical computation and accumulation
                        switch (lastOp)
                        {
                            case "+":
                                lastVal = lastVal + int.Parse(n);
                                break;
                            case "-":
                                lastVal = lastVal - int.Parse(n);
                                break;
                            case "*":
                                lastVal = lastVal * int.Parse(n);
                                break;
                            case "/":
                                lastVal = lastVal / int.Parse(n);
                                break;

                        }
                    }
                }
                int opCtr = 0;
                foreach (string o in op)
                {
                    opCtr++;
                    if (opCtr == numCtr) //will make sure it will get the next operator
                    {
                        lastOp = o;  // get the last operator
                        break;
                    }
                }
                OutputValue = lastVal.ToString();
            }
            return OutputValue;
        }

        public static byte[] GetBytes(object obj1)
        {
            try
            {
                return (byte[])obj1;
            }
            catch
            {
                //string msg1 = exp.Message;
                return null;
            }
        }

        public static string Trans(double XX1, int Index)
        {
            Index = (Index == 0 ? 1 : Index);
            string[] X1 = new string[101];
            string[] Y1 = new string[6];
            string[] Z1 = new string[3];

            X1[0] = "Zero ";
            X1[1] = "One ";
            X1[2] = "Two ";
            X1[3] = "Three ";
            X1[4] = "Four ";
            X1[5] = "Five ";
            X1[6] = "Six ";
            X1[7] = "Seven ";
            X1[8] = "Eight ";
            X1[9] = "Nine ";
            X1[10] = "Ten ";
            X1[11] = "Eleven ";
            X1[12] = "Twelve ";
            X1[13] = "Thirteen ";
            X1[14] = "Fourteen ";
            X1[15] = "Fifteen ";
            X1[16] = "Sixteen ";
            X1[17] = "Seventeen ";
            X1[18] = "Eighteen ";
            X1[19] = "Nineteen ";
            X1[20] = "Twenty ";
            X1[30] = "Thirty ";
            X1[40] = "Forty ";
            X1[50] = "Fifty ";
            X1[60] = "Sixty ";
            X1[70] = "Seventy ";
            X1[80] = "Eighty ";
            X1[90] = "Ninety ";

            for (int J1 = 20; J1 <= 90; J1 = J1 + 10)
                for (int I1 = 1; I1 <= 9; I1++)
                    X1[J1 + I1] = X1[J1] + X1[I1];

            Y1[1] = "Hundred ";
            Y1[2] = "Thousand ";
            Y1[3] = (Index >= 3 ? "Million " : "Lac ");
            Y1[4] = (Index >= 3 ? "Billion " : "Crore ");
            Y1[5] = "Trillion ";
            Z1[1] = "Minus ";
            Z1[2] = "Zero ";
            long N_1 = System.Convert.ToInt64(Math.Floor(XX1));
            string N_2 = XX1.ToString();
            while (!(N_2.Length == 0))
            {
                if (N_2.Substring(0, 1) == ".")
                    break;
                N_2 = N_2.Substring(1);
            }
            N_2 = (N_2.Length == 0 ? " " : N_2);
            switch (Index)
            {
                case 1:
                case 3:
                    N_2 = ((N_2.Substring(0, 1) == ".") ? ((string)(N_2.Substring(1) + "00000")).Substring(0, 5) : "00000");
                    break;
                case 2:
                case 4:
                    N_2 = ((N_2.Substring(0, 1) == ".") ? ((string)(N_2.Substring(1) + "00000")).Substring(0, 2) : "00");
                    break;
            }
            string S_GN = (Math.Sign(N_1) == -1 ? Z1[1] : "");
            string Z1_ER = (N_1 == 0 ? Z1[2] : "");
            string N_O = Right("00000000000000000" + Math.Abs(N_1).ToString(), 17);
            string[] L = new string[100];
            switch (Index)
            {
                case 1:
                case 2:
                    L[0] = "";
                    L[1] = ((Convert.ToInt32(N_O.Substring(0, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(0, 1))] + Y1[1]);
                    L[2] = ((Convert.ToInt32(N_O.Substring(1, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(1, 2))] + Y1[4]);
                    L[3] = ((Convert.ToInt32(N_O.Substring(3, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(3, 2))] + Y1[3]);
                    L[4] = ((Convert.ToInt32(N_O.Substring(5, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(5, 2))] + Y1[2]);
                    L[5] = ((Convert.ToInt32(N_O.Substring(7, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(7, 1))] + Y1[1]);
                    L[6] = ((Convert.ToInt32(N_O.Substring(8, 2)) == 0) ? ((Convert.ToInt32(N_O.Substring(0, 10))) == 0 ? "" : Y1[4]) : X1[Int32.Parse(N_O.Substring(8, 2))] + Y1[4]);
                    L[7] = ((Convert.ToInt32(N_O.Substring(10, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(10, 2))] + Y1[3]);
                    L[8] = ((Convert.ToInt32(N_O.Substring(12, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(12, 2))] + Y1[2]);
                    L[9] = ((Convert.ToInt32(N_O.Substring(14, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(14, 1))] + Y1[1]);
                    L[10] = (Convert.ToInt32(N_O.Substring(15, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(15, 2))];
                    break;
                case 3:
                case 4:
                    L[0] = ((Convert.ToInt32(N_O.Substring(0, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(0, 2))] + Y1[2]);
                    L[1] = ((Convert.ToInt32(N_O.Substring(2, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(2, 1))] + Y1[1]);
                    L[2] = ((Convert.ToInt32(N_O.Substring(3, 2)) == 0) ? ((Convert.ToInt32(N_O.Substring(2, 1)) == 0) ? "" : Y1[5]) : X1[Int32.Parse(N_O.Substring(3, 2))] + Y1[5]);
                    L[3] = ((Convert.ToInt32(N_O.Substring(5, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(5, 1))] + Y1[1]);
                    L[4] = ((Convert.ToInt32(N_O.Substring(6, 2)) == 0) ? ((Convert.ToInt32(N_O.Substring(5, 1)) == 0) ? "" : Y1[4]) : X1[Int32.Parse(N_O.Substring(6, 2))] + Y1[4]);
                    L[5] = ((Convert.ToInt32(N_O.Substring(8, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(8, 1))] + Y1[1]);
                    L[6] = ((Convert.ToInt32(N_O.Substring(9, 2)) == 0) ? ((Convert.ToInt32(N_O.Substring(8, 1)) == 0) ? "" : Y1[3]) : X1[Int32.Parse(N_O.Substring(9, 2))] + Y1[3]);
                    L[7] = ((Convert.ToInt32(N_O.Substring(11, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(11, 1))] + Y1[1]);
                    L[8] = ((Convert.ToInt32(N_O.Substring(12, 2)) == 0) ? ((Convert.ToInt32(N_O.Substring(11, 1)) == 0) ? "" : Y1[2]) : X1[Int32.Parse(N_O.Substring(12, 2))] + Y1[2]);
                    L[9] = ((Convert.ToInt32(N_O.Substring(14, 1)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(14, 1))] + Y1[1]);
                    L[10] = (Convert.ToInt32(N_O.Substring(15, 2)) == 0) ? "" : X1[Int32.Parse(N_O.Substring(15, 2))];
                    break;
            }
            string O = S_GN + Z1_ER + L[0] + L[1] + L[2] + L[3] + L[4] + L[5] + L[6] + L[7] + L[8] + L[9] + L[10];
            string[] M = new string[100];
            string Q_ = "";
            string P = "";

            switch (Index)
            {
                case 1:
                case 3:
                    M[1] = ((Convert.ToInt32(N_2) >= 1) ? X1[Int32.Parse(N_2.Substring(0, 1))] : "");
                    M[2] = ((Convert.ToInt32(N_2) >= 1 & Convert.ToInt32(N_2.Substring(1)) >= 1) ? X1[Int32.Parse(N_2.Substring(1, 1))] : "");
                    M[3] = ((Convert.ToInt32(N_2) >= 1 & Convert.ToInt32(N_2.Substring(2)) >= 1) ? X1[Int32.Parse(N_2.Substring(2, 1))] : "");
                    M[4] = ((Convert.ToInt32(N_2) >= 1 & Convert.ToInt32(N_2.Substring(3 - 1)) >= 1) ? X1[Int32.Parse(N_2.Substring(3, 1))] : "");
                    M[5] = ((Convert.ToInt32(N_2) >= 1 & Convert.ToInt32(N_2.Substring(4)) >= 1) ? X1[Convert.ToInt32(N_2.Substring(4, 1))] : "");
                    M[6] = ((Convert.ToInt32(N_2) > 0) ? "Point " : "");
                    P = M[6] + M[1] + M[2] + M[3] + M[4] + M[5];
                    Q_ = O + P;
                    break;
                case 2:
                    M[1] = ((Convert.ToInt32(N_2) >= 1) ? X1[Int32.Parse(N_2)] : "");
                    M[6] = ((Convert.ToInt32(N_2) > 0) ? "And Paisa " : "");
                    P = M[6] + M[1];
                    Q_ = "( Taka " + O + P + "Only )";
                    break;
                case 4:
                    M[1] = ((Convert.ToInt32(N_2) >= 1) ? X1[Int32.Parse(N_2)] : "");
                    M[6] = ((Convert.ToInt32(N_2) > 0) ? "And Cent " : "");
                    P = M[6] + M[1];
                    Q_ = "( Dollar " + O + P + "Only )";
                    break;
            }
            return Q_;
        }

        //--------------------------------------------------------------------------------------------------------
        public static string DefComa(double AA) // Bangla Coma
        {
            string[] A = new string[21];
            A[1] = ((Math.Sign(AA) >= 0) ? "" : "-");
            A[2] = Math.Abs(AA).ToString("###0.00");
            A[3] = Math.Abs(AA).ToString("###0.000");
            A[3] = ((double.Parse(A[3]) - (double.Parse(A[2])))).ToString();
            A[2] = A[2] + ((Double.Parse(A[3]) >= 0.005) ? 0.01 : 0);
            A[2] = Left(A[2], A[2].Length - 1);
            A[4] = ((string)(string.Empty.PadLeft(24) + A[2])).Substring(((string)(string.Empty.PadLeft(24) + A[2])).Length - 24);
            A[5] = A[4].Substring(0, 2);
            A[6] = A[4].Substring(2, 2);
            A[7] = A[4].Substring(4, 3);
            A[8] = A[4].Substring(7, 2);
            A[9] = A[4].Substring(9, 2);
            A[10] = A[4].Substring(11, 3);
            A[11] = A[4].Substring(14, 2);
            A[12] = A[4].Substring(16, 2);
            A[13] = A[4].Substring(18, 3);
            A[14] = A[5] + "," + A[6] + "," + A[7] + "," + A[8] + "," + A[9] + "," + A[10] + "," + A[11] + "," + A[12] + "," + A[13];
            A[14] = A[14].Trim();

            while (A[14].Substring(0, 1) == ",")
            {
                A[14] = A[14].Substring(1, A[14].Length - 1);
                A[14] = A[14].Trim();
            }
            A[15] = A[14] + A[4].Substring(21, 3);
            A[16] = ((string)(string.Empty.PadLeft(24) + A[15])).Substring(((string)(string.Empty.PadLeft(24) + A[15])).Length - 24) + " ";
            A[17] = ((A[1] != "") ? "(" : "") + A[16].Trim() + ((A[1] != "") ? ")" : "");
            return A[17];
        }
        //-------------------------------------------------------------------------------------------------------       
        public static string Concat(string compname, string username, string printdate)
        {
            string concat = "";
            concat = concat + "Printed from Computer Address:" + compname + ", User:" + username + ", Time:" + printdate;
            return concat;
        }

        public static string Cominformation()
        {
            return "Software By: www.idealinfo.com, E-Mail: info@idealinfo.com";
        }

        public static string ToRoman(int N)
        {
            const string Digits = "IVXLCDM";
            int I = 0;
            int Digit = 0;
            string Temp = null;
            string Temp1 = null;
            int N1 = 0;
            Temp1 = "";
            if (N >= 1000)
            {
                String s = "MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM";
                Temp1 = s.Substring(0, N1);
                N1 = N / (1000);
                N = N - N1 * 1000;
            }
            I = 1;
            Temp = "";
            while (N > 0)
            {
                Digit = N % 10;
                N = N / 10;
                switch (Digit)
                {
                    case 1:
                        Temp = Digits.Substring(I - 1, 1) + Temp;
                        break;
                    case 2:
                        Temp = Digits.Substring(I - 1, 1) + Digits.Substring(I - 1, 1) + Temp;
                        break;
                    case 3:
                        Temp = Digits.Substring(I - 1, 1) + Digits.Substring(I - 1, 1) + Digits.Substring(I - 1, 1) + Temp;
                        break;
                    case 4:
                        Temp = Digits.Substring(I - 1, 2) + Temp;
                        break;
                    case 5:
                        Temp = Digits.Substring(I, 1) + Temp;
                        break;
                    case 6:
                        Temp = Digits.Substring(I, 1) + Digits.Substring(I - 1, 1) + Temp;
                        break;
                    case 7:
                        Temp = Digits.Substring(I, 1) + Digits.Substring(I - 1, 1) + Digits.Substring(I - 1, 1) + Temp;
                        break;
                    case 8:
                        Temp = Digits.Substring(I, 1) + Digits.Substring(I - 1, 1) + Digits.Substring(I - 1, 1) + Digits.Substring(I - 1, 1) + Temp;
                        break;
                    case 9:
                        Temp = Digits.Substring(I - 1, 1) + Digits.Substring(I + 2 - 1, 1) + Temp;
                        break;
                }
                I = I + 2;
            }
            return Temp1 + Temp;


        }

        //public static string StrDecompress(string s)
        //{
        //    var bytes = Convert.FromBase64String(s);
        //    using (var msi = new MemoryStream(bytes))
        //    using (var mso = new MemoryStream())
        //    {
        //        using (var gs = new GZipStream(msi, CompressionMode.Decompress))
        //        {
        //            gs.CopyTo(mso);
        //        }
        //        return Encoding.Unicode.GetString(mso.ToArray());
        //    }
        //}

        public static string EncMD5(string password)
        {
            MD5 md5 = new MD5CryptoServiceProvider();
            UTF8Encoding encoder = new UTF8Encoding();
            Byte[] originalBytes = encoder.GetBytes(password);
            Byte[] encodedBytes = md5.ComputeHash(originalBytes);
            password = BitConverter.ToString(encodedBytes).Replace("-", "");
            var result = password.ToLower();
            return result;
        }

        public static string NumberOnly(string value)
        {
            Regex _isNumber = new Regex(@"^\d+$");
            string result = "0";
            string[] data = Regex.Split(value, "([0-9]|[.])");
            List<string> list = new List<string>();
            for (int i = 0; i < data.Length; i++)
            {
                Match m = _isNumber.Match(data[i]);
                if (m.Success)
                {
                    list.Add(data[i]);
                }
                if (data[i] == ".")
                {
                    list.Add(data[i]);
                }
            }
            string ans = "";
            for (int j = 0; j < list.Count; j++)
            {
                ans += list[j];
            }

            int count = 0;
            for (int i = 0; i < ans.Length; i++)
                if ('.' == ans[i])
                    count++;

            for (int k = count; count > 1; count--)
            {
                if (ans.Contains('.'))
                {
                    int i = ans.IndexOf('.');
                    ans = ans.Remove(i, 1);
                }
            }
            int index = 0;
            if (value.StartsWith("-") || (value.StartsWith("(") && value.EndsWith(")")))
                index = 1;
            else if (value == "")
                index = 2;

            result = (index == 0 ? ans : (index == 1 ? "-" + ans : result));
            return result;
        }
        public static string EncodePassword(string originalPassword)
        {
            Byte[] originalBytes;
            Byte[] encodedBytes;
            string hashString;

            //MD5 md5;
            //md5 = new MD5CryptoServiceProvider();
            //originalBytes = ASCIIEncoding.Default.GetBytes(originalPassword);
            //encodedBytes = md5.ComputeHash(originalBytes);
            //hashString = BitConverter.ToString(encodedBytes).Replace("-", "");

            using (SHA256 sha256 = SHA256.Create()) // Use SHA256 instead of MD5
            {
                originalBytes = Encoding.UTF8.GetBytes(originalPassword);
                encodedBytes = sha256.ComputeHash(originalBytes);
                hashString = BitConverter.ToString(encodedBytes).Replace("-", "");
            }
            return hashString;
        }

        public static string XmlSerialize(object dataToSerialize)
        {
            if (dataToSerialize == null) return null;

            using (StringWriter stringwriter = new System.IO.StringWriter())
            {
                var serializer = new XmlSerializer(dataToSerialize.GetType());
                serializer.Serialize(stringwriter, dataToSerialize);
                return stringwriter.ToString();
            }
        }

        public static T XmlDeserialize<T>(string xmlText)
        {
            if (String.IsNullOrWhiteSpace(xmlText)) return default(T);

            using (StringReader stringReader = new System.IO.StringReader(xmlText))
            {
                var serializer = new XmlSerializer(typeof(T));
                return (T)serializer.Deserialize(stringReader);
            }
        }

        public static string UppercaseWords(string value)
        {
            char[] array = value.ToCharArray();
            // Handle the first letter in the string.
            if (array.Length >= 1)
            {
                if (char.IsLower(array[0]))
                {
                    array[0] = char.ToUpper(array[0]);
                }
            }
            // Scan through the letters, checking for spaces.
            // ... Uppercase the lowercase letters following spaces.
            for (int i = 1; i < array.Length; i++)
            {
                if (array[i - 1] == ' ')
                {
                    if (char.IsLower(array[i]))
                    {
                        array[i] = char.ToUpper(array[i]);
                    }
                }
            }
            return new string(array);
        }

        public static List<T> DataTableToList<T>(this DataTable table) where T : class, new()
        {
            try
            {
                List<T> list = new List<T>();

                foreach (var row in table.AsEnumerable())
                {
                    T obj = new T();

                    foreach (var prop in obj.GetType().GetProperties())
                    {
                        try
                        {
                            PropertyInfo propertyInfo = obj.GetType().GetProperty(prop.Name);
                            propertyInfo.SetValue(obj, Convert.ChangeType(row[prop.Name], propertyInfo.PropertyType), null);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    list.Add(obj);
                }

                return list;
            }
            catch
            {
                return null;
            }
        }

        public static DataTable ListToDataTable<T>(List<T> items)
        {
            DataTable dataTable = new DataTable(typeof(T).Name);
            //Get all the properties
            PropertyInfo[] Props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in Props)
            {
                //Setting column names as Property names
                dataTable.Columns.Add(prop.Name);
            }
            foreach (T item in items)
            {
                var values = new object[Props.Length];
                for (int i = 0; i < Props.Length; i++)
                {
                    //inserting property values to datatable rows
                    values[i] = Props[i].GetValue(item, null);
                }
                dataTable.Rows.Add(values);
            }
            //put a breakpoint here and check datatable
            return dataTable;
        }
        public static string ConvertDtToXmlString(DataTable table)
        {
            TextWriter writer = new StringWriter();
            table.WriteXml(writer);
            string xml = writer.ToString();
            return xml;
        }

        public static string UnicodeNumEng2Ban(string input1 = "")
        {
            //char[] ar1 = {'0','1','2','3','4','5','6','7','8','9' };
            char[] ar2 = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
            char[] ar3 = input1.ToCharArray();
            string output1 = "";
            foreach (char item in ar3)
            {
                int i1 = -1;
                bool isInt = int.TryParse(item.ToString(), out i1);
                output1 += (isInt && i1 >= 0 ? ar2[i1].ToString() : item.ToString());
            }

            return output1;
        }
        public static string UnicodeNumBan2Eng(string input1 = "")
        {
            char[] ar3 = input1.ToCharArray();
            string output1 = "";
            foreach (char item in ar3)
            {
                string item1 = item.ToString();
                switch (item1)
                {
                    case "০": output1 += "0"; break;
                    case "১": output1 += "1"; break;
                    case "২": output1 += "2"; break;
                    case "৩": output1 += "3"; break;
                    case "৪": output1 += "4"; break;
                    case "৫": output1 += "5"; break;
                    case "৬": output1 += "6"; break;
                    case "৭": output1 += "7"; break;
                    case "৮": output1 += "8"; break;
                    case "৯": output1 += "9"; break;
                    default: output1 += item1; break;
                }
            }
            return output1;
        }

        public static string Eng2BanMonthsDays()
        {
            string[] EngMonthse = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            string[] EndMonthsb = { "জানুয়ারী", "ফেব্রুয়ারী", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই ", "আগষ্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর" };

            string[] EngSMonthse = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            string[] EndSMonthsb = { "জানুঃ", "ফেব্রু", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই ", "আগষ্ট", "সেপ্টেঃ", "অক্টোঃ", "নভেঃ", "ডিসেঃ" };

            string[] BanMonthse = { "Boishakh", "Joishtho", "Asharh", "Srabon", "Bhadro", "Ashshin", "Kartik", "Ogrohaeon", "Poush", "Magh", "Falgun", "Choitro" };
            string[] BanMonthsb = { "বৈশাখ", "জৈষ্ঠ্য", "আষাঢ়", "শ্রাবণ", "ভাদ্র", "আশ্বিন", "কার্তিক", "অগ্রাহায়ন", "পৌষ", "মাঘ", "ফাল্গুন" };

            string[] EngDays = { "Saturday", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
            string[] BanDays = { "শনিবার", "রবিবার", "সোমবার", "মঙ্গলবার", "বুধবার", "বৃহস্পতিবার", "শুক্রবার" };

            string[] EngsDays = { "Sat", "Sun", "Mon", "Tue", "Wed", "Thu", "Fri" };
            string[] BansDays = { "শনি", "রবি", "সোম", "মঙ্গল", "বুধ", "বৃহস্পতি", "শুক্র" };

            // January       February       March   April   May     June    July    August  September   October     November        December
            // জানুয়ারী        ফেব্রুয়ারী       মার্চ     এপ্রিল        মে       জুন      জুলাই     আগষ্ট        সেপ্টেম্বর      অক্টোবর       নভেম্বর       ডিসেম্বর

            // বৈশাখ  জৈষ্ঠ্য      আষাঢ়     শ্রাবণ        ভাদ্র     আশ্বিন        কার্তিক        অগ্রাহায়ন      পৌষ      মাঘ      ফাল্গুন

            //Saturday      Sunday      Monday      Tuesday     Wednesday       Thursday, Firday
            // শনিবার         রবিবার         সোমবার   মঙ্গলবার  বুধবার    বৃহস্পতিবার শুক্রবার
            // Boishakh            Joishtho            Asharh Srabon    Bhadro  Ashshin Kartik  Ogrohaeon   Poush   Magh    Falgun  Choitro
            // PM = 12-2= দুপুর, 3-4 = অপরাহ্ন, 5 = বিকেল, 6-7, সন্ধ্যা, 8-11: রাত, AM = পূর্বাহ্ণ
            return "";
        }
        public static List<string> EngBanCalculator(string input1 = "0")
        {
            input1 = input1.Trim();
            string BanNum1 = "০১২৩৪৫৬৭৮৯";
            bool isBan1 = false;
            char[] arr1 = input1.ToCharArray();
            foreach (var item in arr1)
            {
                if (BanNum1.Contains(item.ToString()))
                {
                    isBan1 = true;
                    break;
                }
            }
            string EngNum1 = (isBan1 ? UtilityFunction.UnicodeNumBan2Eng(input1) : input1);
            string output1 = UtilityFunction.Text2Value(EngNum1);
            if (isBan1)
            {
                input1 = UtilityFunction.UnicodeNumEng2Ban(input1);
                output1 = UtilityFunction.UnicodeNumEng2Ban(output1);
            }

            var lst1 = new List<string>();
            lst1.Add(input1);
            lst1.Add(output1);
            return lst1;
        }

        public static void ObjectToObject(object source, object destination)
        {
            // Purpose : Use reflection to set property values of objects that share the same property names.
            Type s = source.GetType();
            Type d = destination.GetType();

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            var objSourceProperties = s.GetProperties(flags);
            var objDestinationProperties = d.GetProperties(flags);

            var propertyNames = objSourceProperties
            .Select(c => c.Name)
            .ToList();

            foreach (var properties in objDestinationProperties.Where(properties => propertyNames.Contains(properties.Name)))
            {
                try
                {
                    PropertyInfo piSource = source.GetType().GetProperty(properties.Name);

                    properties.SetValue(destination, piSource.GetValue(source, null), null);
                }
                catch
                {
                    throw;
                }

            }
        }
        public static List<T> CopyList<T>(this List<T> lst)
        {
            List<T> lstCopy = new List<T>();

            foreach (var item in lst)
            {
                var instanceOfT = Activator.CreateInstance<T>();
                ObjectToObject(item, instanceOfT);
                lstCopy.Add(instanceOfT);
            }
            return lstCopy;
        }

        public static List<T> JsonStringToList<T>(this string JsonDs1, string partName1) //where T : class, new()
        {
            // Here obj is a JToken like "JObject, JArray" etc.
            dynamic obj = JsonConvert.DeserializeObject(JsonDs1);
            /* Here, we are Serializing the object again and then Deserialize it
             * This is happening because, obj is of dynamic type, so we can't 
             * Desirialize obj into model. And Newtonsoft expects a JSON string as input when using DeserializeObject<T>().
             * Not a JToken, JArray or anything only JSON string.
             * which is why we need to Serialize obj[partName1] before desirializing it to get the data.  */
            List<T> lstCopy = JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(obj[partName1]));
            return lstCopy;
        }

        public static String ListToJsonString<T>(this List<T> list1, string partName1)
        {
            string JsonStr1 = JsonConvert.SerializeObject(list1);
            JsonStr1 = "{\"" + partName1 + "\":" + JsonStr1 + "}";
            return JsonStr1;


            //DataTable tbl1 = UtilityFuns.ListToDataTable<T>(list1);
            //tbl1.TableName = partName1;
            //DataSet ds1 = new DataSet();
            //ds1.Tables.Add(tbl1);
            //string JsonStr2 = JsonConvert.SerializeObject(ds1);
            //return JsonStr2;
        }

        public static string StrCompress(string s)
        {
            var bytes = Encoding.Unicode.GetBytes(s);
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    msi.CopyTo(gs);
                }
                return Convert.ToBase64String(mso.ToArray());
            }
        }

        public static string StrDecompress(string s)
        {
            var bytes = Convert.FromBase64String(s);
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }
                return Encoding.Unicode.GetString(mso.ToArray());
            }
        }

        //List<MyTable1> User = JsonConvert.DeserializeObject<List<MyTable1>>(JsonConvert.SerializeObject(obj["Table"]));

        /*
         * DataTable dtTable = GetEmployeeDataTable();
         * List<Employee> employeeList = dtTable.DataTableToList<Employee>();
         */
        // MH
        public static string TakaEnToBanWithCommaSeparator(decimal enTaka)
        {


            string enNumInp = Comma_SeparatorTaka(Math.Round(Convert.ToDecimal(enTaka), 2));


            //    enNumInp = this.txtboxInput.Text.Trim();  //12-February-2018
            char[] bnNum = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
            string banNumOutput = "";

            foreach (var c in enNumInp.ToArray())
            {
                char c1 = c;
                for (int i = 0; i <= 9; i++)
                {
                    if (c1.ToString() == i.ToString())
                    {
                        c1 = bnNum[i];
                        break;
                    }
                }
                banNumOutput += c1.ToString();  // 
            }

            return banNumOutput;
        }
        public static String Comma_SeparatorTaka(decimal amount)
        {
            string result = "";
            string amt = "";
            string amt_paisa = "";

            amt = amount.ToString();
            int aaa = amount.ToString().IndexOf(".", 0);
            amt_paisa = amount.ToString().Substring(aaa + 1);

            if (amt == amt_paisa)
            {
                amt_paisa = "";
            }
            else
            {
                amt = amount.ToString().Substring(0, amount.ToString().IndexOf(".", 0));
                amt = (amt.Replace(",", "")).ToString();
            }
            switch (amt.Length)
            {
                case 12:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 2) + "," + amt.Substring(6, 3);
                    }
                    else
                    {
                        //result = amt.Substring(0, 1) + "," + amt.Substring(1, 3) + "," + amt.Substring(4, 2) + "," +
                        //        amt.Substring(6, 2) + "," + amt.Substring(8, 3) + "." +
                        //        amt_paisa;
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 3) + "," + amt.Substring(5, 2) + "," +
                                 amt.Substring(7, 2) + "," + amt.Substring(9, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                case 11:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 2) + "," + amt.Substring(6, 3);
                    }
                    else
                    {
                        //result = amt.Substring(0, 3) + "," + amt.Substring(3, 2) + "," +
                        //         amt.Substring(5, 2) + "," + amt.Substring(7, 3) + "." +
                        //         amt_paisa;
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 3) + "," + amt.Substring(4, 2) + "," +
                                 amt.Substring(6, 2) + "," + amt.Substring(8, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                case 10:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 2) + "," + amt.Substring(6, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 3) + "," + amt.Substring(3, 2) + "," +
                                 amt.Substring(5, 2) + "," + amt.Substring(7, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                case 9:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 2) + "," + amt.Substring(6, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 2) + "," + amt.Substring(6, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                case 8:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 2) + "," +
                                 amt.Substring(3, 2) + "," + amt.Substring(5, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 2) + "," +
                                 amt.Substring(3, 2) + "," + amt.Substring(5, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                case 7:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 2) + "," +
                                 amt.Substring(4, 3) + "." + amt_paisa;
                    }
                    break;
                case 6:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 2) + "," +
                                 amt.Substring(3, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 2) + "," +
                                 amt.Substring(3, 3) + "." + amt_paisa;
                    }
                    break;
                case 5:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 2) + "," + amt.Substring(2, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                case 4:
                    if (amt_paisa == "")
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 3);
                    }
                    else
                    {
                        result = amt.Substring(0, 1) + "," + amt.Substring(1, 3) + "." +
                                 amt_paisa;
                    }
                    break;
                default:
                    if (amt_paisa == "")
                    {
                        result = amt;
                    }
                    else
                    {
                        result = amt + "." + amt_paisa;
                    }
                    break;
            }
            return result;
        }

        #region customFunctions > dateToBangla, DateBanglatoenglish, NumEntoBan
        public static string DateToBangla(string enNumInp, string type = "")
        {
            //    enNumInp = this.txtboxInput.Text.Trim();  //12-February-2018
            char[] bnNum = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
            string[] bn = { "জানু", "ফেব্রু", "মার্চ", "এপ্রি", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টে", "অক্টো", "নভে", "ডিসে" };
            string[] bnFull = { "জানুয়ারি", "ফেব্রুয়ারি", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর" };
            string[] enFull = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            //   string[] en = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            string banNumOutput = "";

            foreach (var c in enNumInp.ToArray())
            {
                char c1 = c;
                for (int i = 0; i <= 9; i++)
                {
                    if (c1.ToString() == i.ToString())
                    {
                        c1 = bnNum[i];
                        break;
                    }
                }
                banNumOutput += c1.ToString();  // 
            }
            for (int i = 0; i < 12; i++)
            {
                if (banNumOutput.Contains(enFull[i]))
                {
                    banNumOutput = banNumOutput.Replace(enFull[i], bnFull[i].ToString());
                    break;
                }
                else if (banNumOutput.Contains(enFull[i].Substring(0, 3)))
                {
                    if (type == "FULLNAME")
                        banNumOutput = banNumOutput.Replace(enFull[i].Substring(0, 3), bnFull[i]);
                    else
                        banNumOutput = banNumOutput.Replace(enFull[i].Substring(0, 3), bn[i]);
                    break;
                }
            }
            //   this.lblShow.Content = banNumOutput;


            return banNumOutput;
        }

        public static string DateToBanglaMin(string enNumInp, string type = "")
        {
            //    enNumInp = this.txtboxInput.Text.Trim();  //12-February-2018
            char[] bnNum = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
            string[] bn = { "জানু", "ফেব্রু", "মার্চ", "এপ্রি", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টে", "অক্টো", "নভে", "ডিসে" };
            string[] bnFull = { "জানুয়ারি", "ফেব্রুয়ারি", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর" };
            string[] enFull = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            //   string[] en = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            string banNumOutput = "";

            foreach (var c in enNumInp.ToArray())
            {
                char c1 = c;
                for (int i = 0; i <= 9; i++)
                {
                    if (c1.ToString() == i.ToString())
                    {
                        c1 = bnNum[i];
                        break;
                    }
                }
                banNumOutput += c1.ToString();  // 
            }


            for (int i = 0; i < 12; i++)
            {
                if (banNumOutput.Contains(enFull[i]))
                {
                    banNumOutput = banNumOutput.Replace(enFull[i], bnFull[i].ToString());
                    break;
                }
                else if (banNumOutput.Contains(enFull[i].Substring(0, 3)))
                {
                    if (type == "FULLNAME")
                        banNumOutput = banNumOutput.Replace(enFull[i].Substring(0, 3), bnFull[i]);
                    else
                        banNumOutput = banNumOutput.Replace(enFull[i].Substring(0, 3), bn[i]);
                    break;
                }
            }
            //   this.lblShow.Content = banNumOutput;


            return banNumOutput;
        }

        public static string DateBanglaToEnglish(string bnNumInp)
        {
            // ২৪-মে-২০২১	
            //    enNumInp = this.txtboxInput.Text.Trim();  //12-February-2018
            char[] bnNum = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
            char[] enNum = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
            string[] bn = { "জানু", "ফেব্রু", "মার্চ", "এপ্রি", "মে", "জুন", "জুলা", "আগ", "সেপ্টে", "অক্টো", "নভে", "ডিসে" };
            string[] bnFull = { "জানুয়ারি", "ফেব্রুয়ারি", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর" };
            string[] enFull = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            string[] en = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            string banNumOutput = "";

            foreach (var c in bnNumInp.ToArray())
            {
                char c1 = c;
                for (int i = 0; i <= 9; i++)
                {
                    if (c1.ToString() == bnNum[i].ToString())
                    {
                        c1 = bnNum[i];
                        break;
                    }
                }
                banNumOutput += c1.ToString();  // 
            }
            for (int i = 0; i < 12; i++)
            {
                if (banNumOutput.Contains(bnFull[i]))
                {
                    banNumOutput = banNumOutput.Replace(bnFull[i], enFull[i].ToString());
                    break;
                }
                else if (banNumOutput.Contains(bn[i]))
                {
                    banNumOutput = banNumOutput.Replace(bnFull[i].Substring(0, 3), en[i]);
                    break;
                }
            }
            //   this.lblShow.Content = banNumOutput;


            return banNumOutput;
        }

        public static string NumEnToBan(string enNumInp)
        {
            //    enNumInp = this.txtboxInput.Text.Trim();  //12-February-2018
            char[] bnNum = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
            string banNumOutput = "";

            foreach (var c in enNumInp.ToArray())
            {
                char c1 = c;
                for (int i = 0; i <= 9; i++)
                {
                    if (c1.ToString() == i.ToString())
                    {
                        c1 = bnNum[i];
                        break;
                    }
                }
                banNumOutput += c1.ToString();  // 
            }

            return banNumOutput;
        }

        public static string EnglishToBangla(object value)
        {
            if (value == null) return "";

            string input = value.ToString();

            char[] eng = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
            char[] ban = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };

            for (int i = 0; i < eng.Length; i++)
            {
                input = input.Replace(eng[i], ban[i]);
            }

            return input;
        }

        #endregion

        static readonly string[] SizeSuffixes =
                   { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        public static string GetFileSizeSuffix(Int64 value, int decimalPlaces = 1)
        {
            if (decimalPlaces < 0) { throw new ArgumentOutOfRangeException("decimalPlaces"); }
            if (value < 0) { return "-" + GetFileSizeSuffix(-value, decimalPlaces); }
            if (value == 0) { return string.Format("{0:n" + decimalPlaces + "} bytes", 0); }

            // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
            int mag = (int)Math.Log(value, 1024);

            // 1L << (mag * 10) == 2 ^ (10 * mag) 
            // [i.e. the number of bytes in the unit corresponding to mag]
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            // make adjustment when the value is large enough that
            // it would round up to 1000 or more
            if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
            {
                mag += 1;
                adjustedSize /= 1024;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}",
                adjustedSize,
                SizeSuffixes[mag]);
        }


        public static Dictionary<string, string> GetMimeTypes() // its called and working
        {
            return new Dictionary<string, string>
            {
                {".txt", "text/plain" },
                {".pdf", "application/pdf" },
                {".doc", "application/vnd.ms-word" },
                {".docx", "application/vnd.ms-word" },
                {".ppt", "application/vnd.ms-powerpoint" },
                {".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
                {".xls", "application/vnd.ms-excel" },
                {".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                {".png", "image/png" },
                {".jpg", "image/jpeg" },
                {".jpeg", "image/jpeg" },
                {".gif", "image/gif" },
                {".csv", "text/csv" },
                {".sql", "application/octet-stream" },
                {".apk", "application/vnd.android.package-archive" },
            };
        }


        public static string EncryptString(string plainText, string key)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(plainText);
                    }

                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }


        public static string DecryptString(string cipherText, string key)
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            using (var aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);

                byte[] iv = new byte[aes.IV.Length];
                byte[] cipher = new byte[fullCipher.Length - iv.Length];

                Array.Copy(fullCipher, iv, iv.Length);
                Array.Copy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream(cipher))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }

    }
}
