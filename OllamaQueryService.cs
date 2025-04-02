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
        Console.Write($"Ollama model '");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write($"{modelName}");
        Console.ResetColor(); // Reset to default color
        Console.WriteLine($"'");

        var requestData = new
        {
            name = modelName,
            stream = false
        };

        var content = new StringContent(
            JsonConvert.SerializeObject(requestData),
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

    public Task WarmUp(string modelName) => QueryModelAsync(modelName, "Hello");

    public async Task<string> QueryModelAsync(string modelName, string question)
    {
        var requestData = new
        {
            model = modelName,
            prompt = question,
            stream = false
        };

        var content = new StringContent(
            JsonConvert.SerializeObject(requestData),
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

    public async Task<OllamaResponse> QueryModelAndParseAsync(string modelName, string question)
    {
        string responseJson = await QueryModelAsync(modelName, question);
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

    public List<(string Lang, string Content)> ExtractCodeBlocks(ICollection<string> langs)
    {
        var results = new List<(string, string)>();

        foreach (var lang in langs)
        {
            // Define the regex pattern to match text between ```lang...```
            string pattern = $@"```{lang}(.*?)```";
            var matches = Regex.Matches(Response, pattern, RegexOptions.Singleline);

            // Extract all matched groups
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Groups.Count > 1)
                {
                    results.Add((lang, matches[i].Groups[1].Value));
                }
            }
        }

        return results;
    }
}