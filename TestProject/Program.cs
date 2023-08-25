// See https://aka.ms/new-console-template for more information
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

Console.WriteLine("Hello, World!");
string TenantId = "687f51c3-0c5d-4905-84f8-97c683a5b9d1";
string ClientId = "a62b9538-c2af-46a9-8d4c-674735211bbe";
 string ClientSecret = "YAl8Q~~tKLeyDQdExGvLxumw6UEP5gpOnaUxZc-F";
 string Resource = "https://management.azure.com/"; // Replace with your ADT instance URL

 HttpClient httpClient = new HttpClient();


    var requestParameters = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", ClientId },
                { "client_secret", ClientSecret },
                { "resource", Resource }
            };

    var content = new FormUrlEncodedContent(requestParameters);

    var response = await httpClient.PostAsync($"https://login.microsoftonline.com/{TenantId}/oauth2/token", content);
    var responseContent = await response.Content.ReadAsStringAsync();
string accessToken;
    if (response.IsSuccessStatusCode)
    {
        var json = JObject.Parse(responseContent);
    accessToken= json["access_token"].ToString();
    }
    else
    {
        throw new Exception($"Failed to obtain access token: {responseContent}");
    }

 string ApiEndpoint = "https://oil-drill-twins-di.api.sea.digitaltwins.azure.net";


 HttpClient _httpClient = new HttpClient();

var query = $"SELECT * FROM DIGITALTWINS T where T.$dtId in ['kb1.001.depth','kb1.001.gasdetection','kb1.001.pressure','kb1.001.flowin']";

_httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

var queryUri = $"{ApiEndpoint}/query?api-version=2020-10-31";
var requestBody = new
{
    query
};
var jsonContent = JsonConvert.SerializeObject(requestBody);
var content1 = new StringContent(jsonContent, Encoding.UTF8, "application/json");

var response1 = await _httpClient.PostAsync(queryUri, content1);
string resp;
if (response.IsSuccessStatusCode)
{
     resp=await response1.Content.ReadAsStringAsync();
}
else
{
    throw new Exception($"Failed to query data from Azure Digital Twins: {response.StatusCode}");
}


Console.WriteLine(accessToken);


