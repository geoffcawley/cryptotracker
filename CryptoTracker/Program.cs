using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

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

        public static float GetPriceFromCoinMarketCap(string coin)
        {
            string URL = "https://coinmarketcap.com/currencies/" + coin + "/index.html";
            string htmlstring = UnicodeToUTF8(CallUrl(URL).Result);
            string searchstring = "<div class=\"priceValue \"><span>$";
            int priceindexstart = htmlstring.IndexOf(searchstring) + searchstring.Length;
            int priceindexend = htmlstring.IndexOf("</span>", priceindexstart);
            string pricestring = htmlstring.Substring(priceindexstart, priceindexend - priceindexstart);
            return float.Parse(pricestring);
        }

        public class TokenRecord
        {
            public string Name { get; set; }
            public string Ticker { get; set; }
            public float Quantity { get; set; }
            public float ValuePerToken { get; set; }
            public float TotalValue { get { return Quantity * ValuePerToken; } }
            public string ToString()
            {
                return Quantity.ToString() + " " + Name + "(" + Ticker + ") @" + ValuePerToken.ToString() + " = $" + TotalValue.ToString();
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
            public List<TokenRecord> Holdings { get; set; }
        }

        public static void WritePortfolio(Portfolio portfolio, string filename) => File.WriteAllText(filename, JsonConvert.SerializeObject(portfolio));

        public static Portfolio ReadPortfolio(string filename)
        {
            return JsonConvert.DeserializeObject<Portfolio>(File.ReadAllText(filename));
        }

        public static void Main(string[] args)
        {
            try
            {
                Portfolio portfolio = new Portfolio();
                portfolio = ReadPortfolio("portfolio.txt");
                float total = 0;
                foreach (var token in portfolio.Holdings)
                {
                    token.ValuePerToken = GetPriceFromCoinMarketCap(token.Name);
                    total += token.TotalValue;
                    Console.WriteLine(token.ToString());
                }
                Console.WriteLine("Total: $" + total);
                WritePortfolio(portfolio, "portfolio.txt");
                Console.ReadKey();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
