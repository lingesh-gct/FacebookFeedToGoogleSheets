using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource;
using System.Net;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using System.Net.Http;

namespace MillionMileRunningStats
{
    // Updates the spreadsheet with running stats from facebook group posts.
    class Program
    {
        static void Main(string[] args)
        {
            // Spreadsheet is the source of truth. Get the time of last stat recorded from spreadsheet.
            DateTime waterMark = SpreadSheet.GetTimeOfLastRecord();

            List<Post> posts = FacebookOperations.GetPosts(waterMark);

            foreach (Post post in posts)
            {
                Stats stats = null;
                Parser.GetStatsIfPresent(post.message, out stats);

                if (stats != null)
                {
                    string[] valuesToAddToSpreadsheet = new string[] { post.from.name, post.updated_time.ToString(), stats.distance.ToString(), stats.time };
                    SpreadSheet.AddDataToSpreadSheet(valuesToAddToSpreadsheet);
                }
                else
                {
                    // FacebookOperations.PostUnableToParseComment(post);
                }
            }
        }
    }

    class Stats
    {
        public double distance;
        public string time;
    };

    class Parser
    {
        static bool IsDistance(string token1, string token2, out double distance)
        {
            string[] unitNames = new string[] { "mi", "miles", "mile", "mil", "km", "kms", "kilometers" };
            distance = 0;

            foreach (string unitName in unitNames)
            {
                string numberPortion = null;
                if (token1.ToLower().Contains(unitName.ToLower()))
                {
                    int index = token1.LastIndexOf(unitName, StringComparison.OrdinalIgnoreCase);
                    numberPortion = token1.Substring(0, index + 1);
                }
                else if (token2.ToLower().Contains(unitName.ToLower()))
                {
                    numberPortion = token1;
                }

                if (numberPortion != null &&
                    double.TryParse(numberPortion, out distance))
                {
                    if (unitName.StartsWith("k", StringComparison.OrdinalIgnoreCase))
                    {
                        // convert km to mile
                        distance = distance / 1.7;
                        return true;
                    }
                    return true;
                }
            }

            return false;
        }

        static bool IsTime(string timePortion, out string time)
        {
            time = null;
            if (timePortion.Contains(":"))
            {
                bool isTime = true;
                foreach (char c in timePortion)
                {
                    if (char.IsNumber(c) || c == ':')
                    {
                        continue;
                    }
                    else
                    {
                        isTime = false;
                        break;
                    }
                }

                if (isTime)
                {
                    time = timePortion;
                    return true;
                }
            }
            else
            {
                int timeInInt;
                if (int.TryParse(timePortion, out timeInInt))
                {
                    time = timePortion;
                    return true;
                }
            }
            return false;
        }

        static bool IsTime(string token1, string token2, out string time)
        {
            if (IsTime(token1, out time))
            {
                return true;
            }

            string[] unitNames = new string[] { "minutes", "mts" };

            foreach (string unitName in unitNames)
            {
                string timePortion = null;
                if (token1.ToLower().Contains(unitName.ToLower()))
                {
                    int index = token1.LastIndexOf(unitName, StringComparison.OrdinalIgnoreCase);
                    timePortion = token1.Substring(0, index + 1);
                }
                else if (token2.ToLower().Contains(unitName.ToLower()))
                {
                    timePortion = token1;
                }

                if (timePortion != null &&
                    IsTime(timePortion, out time))
                {
                    return true;
                }

            }

            return false;
        }

        public static bool GetStatsIfPresent(string text, out Stats stats)
        {
            stats = null;

            if (text == null)
            {
                return false;
            }

            char[] seperators = new char[] { ' ', '\n' };
            string[] tokens = text.Split(seperators, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Count() > 50)
            {
                return false;
            }

            double distance = 0;
            string time = null;
            bool timeFound = false;
            bool distanceFound = false;
            for (int i = 0; i < tokens.Length - 1; ++i)
            {
                if (timeFound == true && distanceFound == true)
                {
                    break;
                }
                if (timeFound == false)
                {
                    if (IsTime(tokens[i].Trim(), tokens[i + 1].Trim(), out time))
                    {
                        timeFound = true;
                    }
                }

                if (distanceFound == false)
                {
                    if (IsDistance(tokens[i].Trim(), tokens[i + 1].Trim(), out distance))
                    {
                        distanceFound = true;
                    }
                }
            }

            if (distanceFound)
            {
                stats = new Stats();
                stats.time = time;
                stats.distance = distance;
                System.Console.WriteLine(time + "  " + distance);
                return true;
            }

            return false;
        }
    }

    class SpreadSheet
    {
        // If modifying these scopes, delete your previously saved credentials
        // at ~/.credentials/sheets.googleapis.com-dotnet-quickstart.json
        static string[] Scopes = { SheetsService.Scope.Spreadsheets };
        static string ApplicationName = "Google Sheets API .NET Quickstart";

        public static void AddDataToSpreadSheet(string[] dataToBeInput)
        {
            UserCredential credential;

            using (var stream =
                new FileStream("client_secret.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.Personal);
                credPath = Path.Combine(credPath, ".credentials/sheets.googleapis.com-dotnet-quickstart.json");

                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("Credential file saved to: " + credPath);
            }

            // Create Google Sheets API service.
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // Define request parameters.
            String spreadsheetId = "1KRLFbL5-i1TYkL_Q5geGbN64UXN8-VG5PcdurdO0III";
            String range = "Sheet1!A2:C";

            SpreadsheetsResource.ValuesResource.GetRequest request =
                    service.Spreadsheets.Values.Get(spreadsheetId, range);

            ValueRange range1 = new ValueRange();
            List<object> valuesToAppend = new List<object>();
            foreach (string data in dataToBeInput)
            {
                valuesToAppend.Add(data);
            }

            List<IList<object>> toAppend = new List<IList<object>>();
            toAppend.Add(valuesToAppend);
            range1.Values = toAppend;
            AppendRequest request1 = service.Spreadsheets.Values.Append(range1, spreadsheetId, range);
            request1.ValueInputOption = AppendRequest.ValueInputOptionEnum.USERENTERED;
            AppendValuesResponse response1 = request1.Execute();
        }

        public static DateTime GetTimeOfLastRecord()
        {
            UserCredential credential;
            DateTime dateTime = DateTime.MinValue;

            using (var stream =
                new FileStream("client_secret.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.Personal);
                credPath = Path.Combine(credPath, ".credentials/sheets.googleapis.com-dotnet-quickstart.json");

                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("Credential file saved to: " + credPath);
            }

            // Create Google Sheets API service.
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // Define request parameters.
            String spreadsheetId = "1KRLFbL5-i1TYkL_Q5geGbN64UXN8-VG5PcdurdO0III";
            String range = "Sheet1!A2:C";

            SpreadsheetsResource.ValuesResource.GetRequest request =
                    service.Spreadsheets.Values.Get(spreadsheetId, range);

            ValueRange response = request.Execute();
            IList<IList<Object>> values = response.Values;
            if (values != null && values.Count > 0)
            {
                IList<Object> lastRow = values.Last();
                dateTime = DateTime.Parse(lastRow[1] as string);
            }
            return dateTime;
        }
    }

    // Facebook Request/Response JSON representation.
    [DataContract]
    class FacebookMeResponse
    {
        [DataMember]
        public string id;
    }

    [DataContract]
    class FacebookFromData
    {
        [DataMember]
        public string name;

        [DataMember]
        public string id;
    }

    [DataContract]
    class Post
    {
        [DataMember]
        public FacebookFromData from;

        [DataMember]
        public string message;

        [DataMember]
        public string id;

        [DataMember]
        public DateTime updated_time;
    }

    [DataContract]
    class Paging
    {
        [DataMember]
        public string previous;

        [DataMember]
        public string next;
    }

    [DataContract]
    class FacebookGroupFeedResponse
    {
        [DataMember]
        public Post[] data;

        [DataMember]
        public Paging paging;
    }

    class FacebookOperations
    {
        // The access tokens are blanked out. If you need them please email lingesh.
        public const string AccessToken = "x";
        public const string ShortLivedAccessToken = "x";
        public const string MillionMilesGroupId = "1663123014002741";
        public const string TestGroupId = "380448308976825";
        public const string client_secret = "d493302e6661888f5845a520a208cc45";
        public const string client_id = "166208533864106";

        // Exchanges access token for a long lived token.
        public static string ExchangeTokens()
        {
            const string tokenExchangeUriTemplate = "https://graph.facebook.com/v2.8/oauth/access_token?grant_type=fb_exchange_token&client_id={0}&client_secret={1}&fb_exchange_token={2}";
            string tokenExchangeUri = string.Format(tokenExchangeUriTemplate, client_id, client_secret, ShortLivedAccessToken);
            WebClient client = new WebClient();

            byte[] response = client.DownloadData(tokenExchangeUri);

            string stringResponse = System.Text.Encoding.Default.GetString(response);
            Console.WriteLine(stringResponse);

            return stringResponse;
        }
        public static List<Post> GetPosts(DateTime waterMark)
        {
            WebClient client = new WebClient();
            List<Post> posts = new List<Post>();
            const string GetFeedUriTemplate = "https://graph.facebook.com/v2.8/{0}/feed?fields=from%2Cmessage%2Cupdated_time&access_token={1}";

            string getFeedUri = string.Format(GetFeedUriTemplate, MillionMilesGroupId, AccessToken);

            Uri nextUri = new Uri(getFeedUri);
            while (true)
            {
                byte[] response = client.DownloadData(nextUri);

                if (response == null || response.Count() == 0)
                    break;

                string stringResponse = System.Text.Encoding.Default.GetString(response);

                if (string.IsNullOrEmpty(stringResponse))
                    break;

                FacebookGroupFeedResponse facebookResponse = JsonConvert.DeserializeObject<FacebookGroupFeedResponse>(stringResponse);
                if (facebookResponse == null)
                    break;

                // Add only the posts that are above the watermark.
                foreach (Post post in facebookResponse.data)
                {
                    DateTime dateTime = post.updated_time;
                    if (dateTime > waterMark)
                    {
                        posts.Add(post);
                    }
                }

                if (facebookResponse.paging == null || facebookResponse.paging.next == null)
                    break;

                nextUri = new Uri(facebookResponse.paging.next);
            }

            posts.Reverse();
            return posts;
        }

        public static void PostUnableToParseComment(Post post)
        {
            const string CommentPostUrl = "https://graph.facebook.com/v2.8/{0}/comments";
            HttpClient client = new HttpClient();
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                {
                    "message", "This message is from the stats parsing service. The service was unable to parse the message for running stats. If you have posted stats here please CREATE A NEW POST with the distance in the text(eg: 4 km, 4.2 miles 4 miles)"
                },
                {
                    "access_token", AccessToken
                }
            };

            FormUrlEncodedContent content = new FormUrlEncodedContent(values);

            string url = string.Format(CommentPostUrl, post.id);
            HttpResponseMessage response = client.PostAsync(url, content).Result;

            string responseString = response.Content.ReadAsStringAsync().Result;
        }
    }
}