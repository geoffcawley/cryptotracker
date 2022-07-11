using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using HtmlAgilityPack;

namespace CryptoTracker
{
    class Program
    {
        private static async Task<string> CallUrl(string fullUrl)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("accept", "*/*");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "PostmanRuntime/7.28.4");
            var response = await httpClient.GetStringAsync(fullUrl);
            return response;
        }

        private static string UnicodeToUTF8(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            return new string(bytes.Select(b => (char)b).ToArray());
        }

        public class TokenRecord
        {
            public string Name { get; set; }
            public string Ticker { get; set; }
            public string CMChref { get; set; }
            public float Quantity { get; set; }
            public float ValuePerToken { get; set; }
            public float TotalValue { get { return Quantity * ValuePerToken; } }
            public float DailyChange { get; set; }
            public override string ToString()
            {
                string leftSide = Quantity.ToString("F5") + " " + Name + " (" + Ticker + ") @" + ValuePerToken.ToString();
                int leftSideLen = leftSide.Length;

                string rightSide = "\t\t= $" + TotalValue.ToString("F2") + "\t";
                int rightSideLen = rightSide.Length;

                string spacer = new string(' ', 35 - leftSideLen);
                string s = leftSide + spacer + rightSide;
                //string s = Quantity.ToString("F5") + " " + Name + " (" + Ticker + ") @" + ValuePerToken.ToString() 
                //    + "\t\t= $" + TotalValue.ToString("F2") + "\t";
                if (DailyChange > 0) s += "+";
                s += DailyChange.ToString("F2") + "%";
                return s;
            }
        }

        public class Portfolio
        {
            public float TotalValue
            {
                get
                {
                    float total = 0.0f;
                    foreach (var token in Holdings)
                    {
                        total += token.TotalValue;
                    }
                    return total;
                }
            }
            public float DailyChange { get; set; }
            public List<TokenRecord> Holdings { get; set; }
            public override string ToString()
            {
                string s = "";
                foreach (var token in Holdings)
                {
                    s += (token.ToString() + "\n");
                }
                s += "Total: $" + TotalValue.ToString("F2") + " ";
                if (DailyChange > 0) s += "+";
                s += DailyChange.ToString("F2") + "%";
                return s;
            }

            public bool AddToken(TokenRecord token)
            {
                var existing = Holdings.Where(t => t.Name == token.Name).FirstOrDefault();
                if (existing != null)
                {
                    existing.Quantity += token.Quantity;
                    return true;
                }
                Holdings.Add(token);
                return true;
            }

            public void RemoveToken(string tokenName)
            {
                Holdings.Remove(Holdings.Find(t => t.Name == tokenName));
            }
        }

        public static void WritePortfolio(Portfolio portfolio, string filename) => File.WriteAllText(filename, JsonConvert.SerializeObject(portfolio));

        public static Portfolio ReadPortfolio(string filename)
        {
            return JsonConvert.DeserializeObject<Portfolio>(File.ReadAllText(filename));
        }

        public static void RefreshPortfolio(Portfolio portfolio)
        {
            float total = 0;
            foreach (var tokenRecord in portfolio.Holdings)
            {
                TokenRecord t = GetTokenInformationFromCoinMarketCap(tokenRecord.CMChref);
                tokenRecord.Name = t.Name;
                tokenRecord.ValuePerToken = t.ValuePerToken;
                tokenRecord.DailyChange = t.DailyChange;
                tokenRecord.Ticker = t.Ticker;
                total += tokenRecord.TotalValue;
            }
            //calculate daily change
            portfolio.DailyChange = 0;
            foreach (var tokenRecord in portfolio.Holdings)
            {
                portfolio.DailyChange += tokenRecord.DailyChange * (tokenRecord.TotalValue / portfolio.TotalValue);
            }
        }

        public static void ShowPortfolio(Portfolio portfolio)
        {
            Console.WriteLine(portfolio.ToString());
        }

        public static void Buy(Portfolio portfolio, string tokenName, float quantity)
        {
            var record = portfolio.Holdings.Where(r => r.Name.ToLower() == tokenName.ToLower()).FirstOrDefault();
            if (record == null)
            {
                record = portfolio.Holdings.Where(r => r.Ticker.ToLower() == tokenName.ToLower()).FirstOrDefault();
            }
            record.Quantity += quantity;
        }

        public static void Sell(Portfolio portfolio, string tokenName, float quantity)
        {
            var record = portfolio.Holdings.Where(r => r.Name.ToLower() == tokenName.ToLower()).FirstOrDefault();
            if (record == null)
            {
                record = portfolio.Holdings.Where(r => r.Ticker.ToLower() == tokenName.ToLower()).FirstOrDefault();
            }
            record.Quantity -= quantity;
        }

        public static TokenRecord GetNameFromCoinMarketCap(string searchString)
        {
            string URL = "https://coinmarketcap.com/all/views/all/";
            var web = new HtmlWeb();
            HtmlAgilityPack.HtmlWeb.PreRequestHandler handler = delegate (HttpWebRequest request)
            {
                request.Headers[HttpRequestHeader.AcceptEncoding] = "gzip, deflate";
                request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
                request.CookieContainer = new System.Net.CookieContainer();
                return true;
            };
            web.PreRequest += handler;
            var html = web.Load(URL);

            var tokenListings = html.DocumentNode.SelectNodes("//tr[contains(@class, \"cmc-table-row\")]");
            List<TokenRecord> tokenMap = new List<TokenRecord>();
            foreach (var token in tokenListings)
            {
                var tokenNameNode = token.SelectSingleNode(".//a[contains(@class, \"cmc-table__column-name--name\")]");
                if (tokenNameNode == null) continue;
                string tokenName = tokenNameNode?.InnerText;
                string tokenHrefFull = tokenNameNode?.Attributes["href"].Value;
                string[] hrefStrings = tokenHrefFull?.Split('/');
                string tokenHref = hrefStrings[2];
                var tokenTickerNode = token.SelectSingleNode(".//td[contains(@class, \"cmc-table__cell--sort-by__symbol\")]");
                if (tokenTickerNode == null) continue;
                string tokenTicker = tokenTickerNode?.InnerText;
                var priceNode = token?.SelectSingleNode(".//div[contains(@class, \"sc-131di3y-0\")]/a/span");
                if (priceNode == null) continue;
                float tokenPrice = float.Parse(new string(priceNode.InnerHtml.Where(c => char.IsDigit(c) || c == '.').ToArray()));
                var dailyChangeNode = token.SelectSingleNode(".//td[contains(@class, \"sort-by__percent-change-24-h\")]/div");
                if (dailyChangeNode == null) continue;
                float tokenDailyChange = float.Parse(new string(dailyChangeNode.InnerHtml.Where(c => char.IsDigit(c) || c == '.').ToArray()));
                tokenMap.Add(new TokenRecord()
                {
                    Name = tokenName,
                    Ticker = tokenTicker,
                    CMChref = tokenHref,
                    ValuePerToken = tokenPrice,
                    DailyChange = tokenDailyChange
                });
            }

            TokenRecord result = tokenMap.Where(t => t.Name.ToLower() == searchString.ToLower() || t.Ticker.ToLower() == searchString.ToLower()).FirstOrDefault();
            return result;
        }

        public static TokenRecord GetTokenInformationFromCoinMarketCap(string tokenHref)
        {
            try
            {
                string URL = "https://coinmarketcap.com/currencies/" + tokenHref + "/";
                string rawHtml = CallUrl(URL).Result;
                float tokenPrice = 0;
                string tokenName;
                string tokenTicker = string.Empty;
                float tokenDailyChange = 0;
                TokenRecord tokenRecord = new TokenRecord();
                var web = new HtmlWeb();
                HtmlAgilityPack.HtmlWeb.PreRequestHandler handler = delegate (HttpWebRequest request)
                {
                    request.Headers[HttpRequestHeader.AcceptEncoding] = "gzip, deflate";
                    request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
                    request.CookieContainer = new System.Net.CookieContainer();
                    return true;
                };
                web.PreRequest += handler;
                var html = web.Load(URL);
                var nameNode = html.DocumentNode.SelectSingleNode("//span[contains(@class,\"sc-1eb5slv-0\")]");
                tokenName = nameNode.InnerText;
                var priceNode = html.DocumentNode.SelectSingleNode("//div[contains(@class,\"priceValue\")]/span");
                tokenPrice = float.Parse(new string(priceNode.InnerHtml.Where(c => char.IsDigit(c) || c == '.').ToArray()));
                var tickerNode = html.DocumentNode.SelectSingleNode("//small[contains(@class, \"nameSymbol\")]");
                tokenTicker = tickerNode.InnerHtml;
                var dailyChangeNode = html.DocumentNode.SelectSingleNode("//span[contains(@class, \"sc-15yy2pl-0\")]");
                tokenDailyChange = float.Parse(dailyChangeNode.InnerHtml.Where(c => char.IsDigit(c) || c == '.').ToArray());
                if (dailyChangeNode.InnerHtml.Contains("down")) tokenDailyChange = -tokenDailyChange;
                //}
                tokenRecord.ValuePerToken = tokenPrice;
                tokenRecord.Name = tokenName;
                tokenRecord.CMChref = tokenHref;
                tokenRecord.Ticker = tokenTicker;
                tokenRecord.DailyChange = tokenDailyChange;
                return tokenRecord;
            }
            catch (Exception e)
            {
                Console.WriteLine("Could find add a token named " + tokenHref);
                return null;
            }
        }

        public static void AddToken(Portfolio portfolio, string tokenName, float quantity)
        {
            try
            {
                string href = GetNameFromCoinMarketCap(tokenName).CMChref;
                TokenRecord tokenRecord = GetTokenInformationFromCoinMarketCap(tokenName);
                portfolio.AddToken(tokenRecord);
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not add a token named " + tokenName);
            }
        }

        public static void RemoveToken(Portfolio portfolio, string tokenName) => portfolio.RemoveToken(tokenName);

        public static void Main(string[] args)
        {
            try
            {
                Portfolio portfolio = new Portfolio();
                string filename = "portfolio.txt";
                Console.WriteLine("Reading from " + Directory.GetCurrentDirectory() + "\\" + filename);
                portfolio = ReadPortfolio(filename);
                RefreshPortfolio(portfolio);
                ShowPortfolio(portfolio);

                string s = "";
                while (s.ToLower() != "quit" && s.ToLower() != "exit")
                {
                    s = Console.ReadLine();
                    var command = s.Split(" ");

                    switch (command[0].ToLower())
                    {
                        case "buy":
                            Buy(portfolio, command[1], float.Parse(command[2]));
                            break;
                        case "sell":
                            Sell(portfolio, command[1], float.Parse(command[2]));
                            break;
                        case "add":
                            AddToken(portfolio, command[1], float.Parse(command[2]));
                            break;
                        case "remove":
                            RemoveToken(portfolio, command[1]);
                            break;
                        case "show":
                            ShowPortfolio(portfolio);
                            break;
                        case "refresh":
                            RefreshPortfolio(portfolio);
                            break;
                        case "get":
                            TokenRecord tokenRecord = GetNameFromCoinMarketCap(command[1]);
                            Console.WriteLine(tokenRecord);
                            break;
                        case "save":
                            WritePortfolio(portfolio, "portfolio.txt");
                            Console.WriteLine("Saved to " + Directory.GetCurrentDirectory() + "\\" + filename);
                            break;

                        case "test":
                            if (command.Length == 1) GetNameFromCoinMarketCap("eth");
                            else GetNameFromCoinMarketCap(command[1]);
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
