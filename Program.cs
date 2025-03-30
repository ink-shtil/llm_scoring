using System.Diagnostics;
using Newtonsoft.Json;

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

    var allStat = new List<Stat>();

    foreach (var test in testsConfig.Tests.GroupBy(_ => _.Category).SelectMany(_ => _))
    {
        maxPoints += test.Scoring.Values.Max();
        var sw = Stopwatch.StartNew();
        int points = await RunTest(test, model, rndName);
        sw.Stop();
        totalPoints += points;

        var newStat = new Stat(test.Category, points, test.Scoring.Values.Max(), sw.Elapsed);
        allStat.Add(newStat);
        TestResults(newStat.WithName(test.Name));
    }

    TotalLine();

    var total = new Stat().WithName("Total");
    foreach (var gr in allStat.GroupBy(_ => _.Category))
    {
        var categorySum = gr.Aggregate(new Stat(gr.Key, 0, 0, TimeSpan.Zero), (acc, stat) => acc + stat);
        TestResults(categorySum);
        total += categorySum.WithName("Total");
    }
    TestResults(total);
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
    string sourceFilePath = test.SourceFile;
    string projectName = test.Name.Replace(" ", "");

    string currentDirectory = Directory.GetCurrentDirectory();
    string testDirectory = Path.Combine(currentDirectory, "generated", testName, model.Replace(':', '_')!, projectName);

    Directory.CreateDirectory(testDirectory);

    string projectFilePath = Path.Combine(testDirectory, $"{projectName}.csproj");

    CreateDotNetProject(testDirectory);

    AddFileToProject(projectFilePath, sourceFilePath);

    var sw = Stopwatch.StartNew();
    await CopyAndEvaluateSourceFile(test, model, testDirectory);
    sw.Stop();
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

static void TestResults(Stat stat)
{
    double categoryPercent = (double)stat.TotalPoints / stat.MaxPoints * 100d;
    
    // Save original console color
    ConsoleColor originalColor = Console.ForegroundColor;
    
    // Print category name in cyan
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"{stat.Category, 20}");
    Console.ForegroundColor = originalColor;

    Console.Write($" {stat.TotalPoints, 2}/{stat.MaxPoints}, ");
    
    Console.ForegroundColor = categoryPercent switch
    {
        < 40 => ConsoleColor.Red,
        < 80 => ConsoleColor.Yellow,
        _ => ConsoleColor.Green
    };
    
    Console.Write($"{categoryPercent,4:F0}%");
    Console.ForegroundColor = originalColor;
    Console.WriteLine($" {stat.Duration:mm\\:ss\\.ff}");
}

static void TotalLine()
{
    Console.WriteLine("".PadLeft(44, '='));
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
    [JsonProperty("category")]
    public string Category { get; set; }
    [JsonProperty("description")]
    public string Description { get; set; }
    [JsonProperty("scoring")]
    public Dictionary<string, int> Scoring { get; set; }
}

internal record struct Stat(string Category, int TotalPoints, int MaxPoints, TimeSpan Duration)
{
    public static Stat operator +(Stat a, Stat b)
    {
        return new Stat(
            a.Category,
            a.TotalPoints + b.TotalPoints,
            a.MaxPoints + b.MaxPoints,
            a.Duration + b.Duration
        );
    }

    public Stat WithName(string name) => new (name, TotalPoints, MaxPoints, Duration);
}