using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using static DBAccess.DataMigrationEntityView00;

namespace DBAccess
{
    public class Geninfo
    {
        public static HttpClient McApiClient = new HttpClient();
        public static JwtTokenInfo ApiToken;
        public static List<DatabaseErrorInfo> DatabaseErrorInfoList;
        public class JwtTokenUser
        {
            public string UserId { get; set; } = "NOUSER";
            public string Password { get; set; } = "#$^&%##%&**^%$*&";
            public int Expirhour { get; set; } = 2;
        }

        //[Serializable]
        public class JwtTokenInfo
        {
            public bool istokenstr { get; set; } = false;
            public string tokenstr { get; set; } = "Nothing";
            public DateTime createtime { get; set; } = DateTime.UtcNow;
            public DateTime expirytime { get; set; } = DateTime.UtcNow;
            public string tokenmsg { get; set; } = "Failed to create token";

        }
        public static void ConnectApi()
        {
            //live
            // McApiClient.BaseAddress = new Uri("http://103.159.73.222:8033/api/");

            //McApiClient.BaseAddress = new Uri("http://localhost:8088/api/");


            McApiClient.BaseAddress = new Uri("http://localhost:8099/api/");


            //McApiClient.BaseAddress = new Uri("http://103.159.73.219:8011/api/");
            // McApiClient.BaseAddress = new Uri("http://202.125.69.22:8095/api/");


            // oly only
            //McApiClient.BaseAddress = new Uri("http://192.168.0.112:8099/api/");

            McApiClient.DefaultRequestHeaders.Accept.Clear();
            McApiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public static async Task GetApiToken()
        {

            var jwtoken1 = new JwtTokenUser() { UserId = "user001", Password = "12345678", Expirhour = 2 };
            var result = await Geninfo.McApiClient.PostAsJsonAsync("Jwt", jwtoken1);
            result.EnsureSuccessStatusCode();
            if (result.IsSuccessStatusCode)
            {
                try
                {
                    string tokenjson1 = await result.Content.ReadAsStringAsync();
                    Geninfo.ApiToken = JsonConvert.DeserializeObject<JwtTokenInfo>(tokenjson1);
                }
                catch (Exception ex)
                {

                }
            }

        }
    }
}
