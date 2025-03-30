using System.Diagnostics;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

string jsonFilePath = "tests.json";
string jsonString = File.ReadAllText(jsonFilePath);
var testsConfig = JsonConvert.DeserializeObject<TestsConfig>(jsonString);

var rndName = $"{DateTime.Now:HH_mm_ss}_{GenerateRandomString(4)}";
foreach (var model in testsConfig.Models)
{
    int totalPoints = 0;
    int maxPoints = 0;
    var ollama = new OllamaQueryService();
    await ollama.PullModelAsync(model);

    var categoryResults = new Dictionary<string, (int TotalPoints, int MaxPoints)>();

    foreach (var test in testsConfig.Tests)
    {
        if (!categoryResults.ContainsKey(test.Category))
        {
            categoryResults[test.Category] = (0, 0);
        }

        maxPoints += test.Scoring.Values.Max();
        int points = await RunTest(test, model, rndName);
        totalPoints += points;

        var (currentTotalPoints, currentMaxPoints) = categoryResults[test.Category];
        categoryResults[test.Category] = (currentTotalPoints + points, currentMaxPoints + test.Scoring.Values.Max());
    }

    double percent = (double)totalPoints / maxPoints * 100;
    Console.WriteLine($"{model}: {totalPoints} / {maxPoints}, {percent:F2}%");

    foreach (var category in categoryResults)
    {
        CategoryResults(category);
    }

    var stat = (categoryResults.Values.Sum(c => c.TotalPoints), categoryResults.Values.Sum(c => c.MaxPoints));
    var total = new KeyValuePair<string, (int, int)>("Total", stat);

    CategoryResults(total);
}

static async Task<int> RunTest(Test test, string model, string testRndName)
{
    string output = await CompileAndRun(test, testRndName, model);

    foreach (var scoring in test.Scoring)
    {
        if (output.Trim().Equals(scoring.Key, StringComparison.InvariantCultureIgnoreCase))
        {
            return scoring.Value;
        }
    }

    return 0;
}

static async Task<string> CompileAndRun(Test test, string testName, string model)
{
    Console.WriteLine($"test={test.Name}");
    string sourceFilePath = test.SourceFile;
    string methodName = test.Method;

    string currentDirectory = Directory.GetCurrentDirectory();
    string testDirectory = Path.Combine(currentDirectory, "generated", testName, model.Replace(':', '_')!, methodName);

    Directory.CreateDirectory(testDirectory);

    string projectFilePath = Path.Combine(testDirectory, $"{methodName}.csproj");

    CreateDotNetProject(testDirectory);

    AddFileToProject(projectFilePath, sourceFilePath);

    var sw = Stopwatch.StartNew();
    await CopyAndEvaluateSourceFile(test, model, testDirectory);
    sw.Stop();
    Console.WriteLine($"Ollama call elapsed: {sw}");
    var wd = Path.GetDirectoryName(projectFilePath);

    var runBuild = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build",
            WorkingDirectory = wd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    runBuild.Start();
    string outputBUild = runBuild.StandardOutput.ReadToEnd();
    runBuild.WaitForExit();

    var runProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --no-restore",
            WorkingDirectory = wd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    runProcess.Start();
    string output = runProcess.StandardOutput.ReadToEnd();
    runProcess.WaitForExit();

    return output.TrimEnd('\n');
}

static void CreateDotNetProject(string projectDirectory)
{
    var createProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"new console -o {projectDirectory}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    createProcess.Start();
    string output = createProcess.StandardOutput.ReadToEnd();
    createProcess.WaitForExit();
}

static void AddFileToProject(string projectFilePath, string sourceFilePath)
{
    var addProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"add {projectFilePath} reference {sourceFilePath}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    addProcess.Start();
    addProcess.WaitForExit();
}

static async Task CopyAndEvaluateSourceFile(Test test, string model, string destinationDirectory)
{
    try
    {
        string destinationFilePath = Path.Combine(destinationDirectory, "Program.cs");
        File.Copy(test.SourceFile, destinationFilePath, overwrite: true);

        var ollamaService = new OllamaQueryService();

        var response = await ollamaService.QueryModelAsync(
            modelName: model,
            filePath: destinationFilePath,
            question: test.Description
        );
        File.WriteAllText(Path.Combine(destinationDirectory, "ollama.json"), response);
        var ollamaResponse = JsonConvert.DeserializeObject<OllamaResponse>(response);
        var csharpBlocks = ollamaResponse.ExtractCsharpBlocks();
        var output = csharpBlocks.Any() ? csharpBlocks[0] : response;
        File.WriteAllText(destinationFilePath, output);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error evaluation file: {ex.Message}");
        throw;
    }
}

static string GenerateRandomString(int length) => Guid.NewGuid().ToString("n")[..length];

static void CategoryResults(KeyValuePair<string, (int TotalPoints, int MaxPoints)> category)
{
    double categoryPercent = (double)category.Value.TotalPoints / category.Value.MaxPoints * 100;
    
    // Save original console color
    ConsoleColor originalColor = Console.ForegroundColor;
    
    // Print category name in cyan
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"{category.Key,20}");
    Console.ForegroundColor = originalColor;
    
    Console.Write(" Points: ");
    Console.Write($"{category.Value.TotalPoints}/{category.Value.MaxPoints}, ");
    
    // Set percentage color using switch expression
    Console.ForegroundColor = categoryPercent switch
    {
        < 40 => ConsoleColor.Red,
        < 80 => ConsoleColor.Yellow,
        _ => ConsoleColor.Green
    };
    
    Console.Write($"{categoryPercent,6:F2}%");
    Console.ForegroundColor = originalColor;
    Console.WriteLine();
}

public class TestsConfig
{
    [JsonProperty("models")]
    public List<string> Models { get; set; }

    [JsonProperty("tests")]
    public List<Test> Tests { get; set; }
}

public class Test
{
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("source_file")]
    public string SourceFile { get; set; }
    [JsonProperty("method")]
    public string Method { get; set; }
    [JsonProperty("category")]
    public string Category { get; set; }
    [JsonProperty("description")]
    public string Description { get; set; }
    [JsonProperty("scoring")]
    public Dictionary<string, int> Scoring { get; set; }
}