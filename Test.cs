using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Test {
    public static async Task Main() {
        try {
            using (var client = new HttpClient()) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AmbilightControllerUpdater");
                string content = await client.GetStringAsync("https://raw.githubusercontent.com/KayraKocak/Ambilight-Controller/main/version.txt");
                Console.WriteLine("Success: " + content.Substring(0, 10));
            }
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
