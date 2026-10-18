using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using static DBAccess.DataMigrationEntityView00;

namespace DBAccess
{
    public static class WebProcAccess
    {
        public static async Task<string> GetMcAppJsonData(DataAccessParams pap1)
        {

            try
            {
                Geninfo.DatabaseErrorInfoList = null;
                string JsonDs1 = "";

                //pap1 = await DataAccess.GetMultiBdAccParms(pap1);
                string sJSONResponse = JsonConvert.SerializeObject(pap1, Newtonsoft.Json.Formatting.Indented);
                var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(sJSONResponse);
                var data1 = System.Convert.ToBase64String(plainTextBytes);
                if (Geninfo.ApiToken == null)
                {
                    Geninfo.ConnectApi();
                    await Geninfo.GetApiToken();
                }
                else if (Geninfo.ApiToken.expirytime <= DateTime.UtcNow)
                {
                    await Geninfo.GetApiToken();
                }

                Geninfo.McApiClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Geninfo.ApiToken.tokenstr);
                var result = await Geninfo.McApiClient.PostAsJsonAsync("dbAsyncRequest", data1);
                result.EnsureSuccessStatusCode();
                if (result.IsSuccessStatusCode)
                {
                    JsonDs1 = await result.Content.ReadAsStringAsync();
                    JsonDs1 = JsonDs1.TrimStart('"').TrimEnd('"');
                    JsonDs1 = UtilityFunction.StrDecompress(JsonDs1.ToString());
                }

                if (JsonDs1.Trim().Length <= 20)
                {
                    Geninfo.DatabaseErrorInfoList = new List<DatabaseErrorInfo>();
                    Geninfo.DatabaseErrorInfoList.Add(new DatabaseErrorInfo
                    {
                        errornumber = 0,
                        errorseverity = 0,
                        errorstate = 0,
                        process_id = "",
                        errorline = 0,
                        errormessage = "Unknown Error Occurred",
                        errorprocedure = ""
                    });
                    return "";
                }
                if (JsonDs1.Substring(0, 20).Contains("\"ErrorTable\": ["))
                {
                    Geninfo.DatabaseErrorInfoList = UtilityFunction.JsonStringToList<DatabaseErrorInfo>(JsonDs1, "ErrorTable");
                    return null;
                }
                return JsonDs1;
            }
            catch (Exception exp)
            {
                //Xceed.Wpf.Toolkit.MessageBox.Show("WpfProAcc-01: " + exp.Message.ToString(), McAppGenInfo.AppTitle, MessageBoxButton.OK,
                //    MessageBoxImage.Stop, MessageBoxResult.OK);
                //Application.Current.Shutdown();
                return "";
            }
        }
    }



}
