using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

public class OllamaQueryService
{
    private static HttpClient _httpClient = new();
    private const string BaseUrl = "http://localhost:11434";

    // Pulls a model if not already downloaded
    public async Task PullModelAsync(string modelName)
    {
        Console.WriteLine($"Ollama pulling model '{modelName}'");

        var requestData = new
        {
            name = modelName,
            stream = false
        };

        var content = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(requestData),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync($"{BaseUrl}/api/pull", content);

        if (!response.IsSuccessStatusCode)
        {
            string responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error pulling model '{modelName}': {responseContent}");
        }
        response.EnsureSuccessStatusCode();
    }

    // Queries the model with file context
    public async Task<string> QueryModelAsync(string modelName, string filePath, string question)
    {
        string fileContent = await File.ReadAllTextAsync(filePath);
        var requestData = new
        {
            model = modelName,
            prompt = $"Context:\n{fileContent}\n\nQuestion: {question}",
            stream = false
        };

        var content = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(requestData),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync($"{BaseUrl}/api/generate", content);

        if (!response.IsSuccessStatusCode)
        {
            string responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error querying model '{modelName}': {responseContent}");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<OllamaResponse> QueryModelAndParseAsync(string modelName, string filePath, string question)
    {
        string responseJson = await QueryModelAsync(modelName, filePath, question);
        return JsonConvert.DeserializeObject<OllamaResponse>(responseJson);
    }
}

public class OllamaResponse
{
    public string Model { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Response { get; set; }
    public bool Done { get; set; }
    public string DoneReason { get; set; }
    public List<int> Context { get; set; }
    public long TotalDuration { get; set; }
    public long LoadDuration { get; set; }
    public int PromptEvalCount { get; set; }
    public long PromptEvalDuration { get; set; }
    public int EvalCount { get; set; }
    public long EvalDuration { get; set; }

    public string[] ExtractCsharpBlocks()
    {
        // Define the regex pattern to match text between ```csharp...```
        string pattern = @"```csharp(.*?)```";
        var matches = Regex.Matches(Response, pattern, RegexOptions.Singleline);

        // Extract all matched groups
        string[] results = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Groups.Count > 1)
            {
                results[i] = matches[i].Groups[1].Value;
            }
        }

        return results;
    }
}