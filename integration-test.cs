using System; 
using System.Threading.Tasks; 
using RuriLib.Services; 
 
class IntegrationTest 
{ 
    static async Task Main() 
    { 
        try 
        { 
            Console.WriteLine("Testing C# real browser service..."); 
            var service = new PuppeteerRealBrowserService(); 
