using System.Diagnostics;
using Newtonsoft.Json;

string jsonFilePath = "tests.json";
string jsonString = File.ReadAllText(jsonFilePath);
var testsConfig = JsonConvert.DeserializeObject<TestsConfig>(jsonString);

TestsConfig.Check(testsConfig);

var rndName = $"{DateTime.Now:HH_mm_ss}_{Utils.GenerateRandomString(4)}";
foreach (var model in testsConfig!.Models)
{
    Console.Write("model '");
    Console.ForegroundColor = ConsoleColor.DarkMagenta;
    Console.Write(model);
    Console.WriteLine("'");
    Console.ResetColor();

    int totalPoints = 0;
    int maxPoints = 0;
    var ollama = new OllamaQueryService();
    await ollama.WarmUp(model);

    var allStat = new List<Stat>();

    foreach (var test in testsConfig.Tests.GroupBy(_ => _.Category).SelectMany(_ => _))
    {
        var thisTestMax = test.Scoring.Select(_ => _.Score).Max();
        maxPoints += thisTestMax;
        var sw = Stopwatch.StartNew();
        int points = await RunTest(test, model, rndName);
        sw.Stop();
        totalPoints += points;

        var newStat = new Stat(test.Category, points, thisTestMax, sw.Elapsed);
        allStat.Add(newStat);
        Utils.TestResults(newStat.WithName(test.Dir));
    }

    Utils.TotalLine();

    var total = new Stat().WithName("Total");
    foreach (var gr in allStat.GroupBy(_ => _.Category))
    {
        var categorySum = gr.Aggregate(new Stat(gr.Key, 0, 0, TimeSpan.Zero), (acc, stat) => acc + stat);
        Utils.TestResults(categorySum);
        total += categorySum.WithName("Total");
    }
    Utils.TestResults(total);
}

static async Task<int> RunTest(Test test, string model, string testRndName)
{
    string output = await CompileAndRun(test, testRndName, model);

    foreach (var scoring in test.Scoring.OrderByDescending(_ => _.Score))
    {
        if (scoring.Type == OutputType.Exact && output.Equals(scoring.Output))
        {
            return scoring.Score;
        }
        if (scoring.Type == OutputType.Contains && output.ToLower().Contains(scoring.Output.ToLower()))
        {
            return scoring.Score;
        }
    }

    return 0;
}

static async Task<string> CompileAndRun(Test test, string testName, string model)
{
    string currentDirectory = Directory.GetCurrentDirectory();
    string testDirectory = Path.Combine(currentDirectory, "generated", testName, model.Replace(':', '_')!, test.Dir);

    Utils.CopyDirectory(Path.Combine(currentDirectory, "tests", test.Dir), testDirectory);
    Directory.CreateDirectory(Path.Combine(testDirectory, "logs"));

    var sw = Stopwatch.StartNew();
    await QueryModel(test, model, testDirectory);
    sw.Stop();

    var runBuild = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build",
            WorkingDirectory = testDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    runBuild.Start();
    string outputBuild = runBuild.StandardOutput.ReadToEnd();
    runBuild.WaitForExit();
    File.WriteAllText(Path.Combine(testDirectory, "logs", "build.log"), outputBuild);

    var runProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --no-restore",
            WorkingDirectory = testDirectory,
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

static async Task QueryModel(Test test, string model, string destinationDirectory)
{
    try
    {
        var ollamaService = new OllamaQueryService();
        var interpretedPrompt = Utils.Interpret(test.Prompt, destinationDirectory);

        File.WriteAllText(Path.Combine(destinationDirectory, "logs", "prompt.log"), interpretedPrompt);

        var response = await ollamaService.QueryModelAsync(
            modelName: model,
            question: interpretedPrompt
        );
        File.WriteAllText(Path.Combine(destinationDirectory, "logs", "ollama.json"), response);
        var ollamaResponse = JsonConvert.DeserializeObject<OllamaResponse>(response);

        var resByLang = test.Results.GroupBy(_ => _.Lang);
        var responses = ollamaResponse!.ExtractCodeBlocks([.. resByLang.Select(_ => _.Key)])
            .GroupBy(_ => _.Lang)
            .ToDictionary(_ => _.Key, _ => _.Select(_ => _.Content).ToList());

        foreach (var results in resByLang)
        {
            var lang = results.Key;
            if (responses.TryGetValue(lang, out List<string>? value))
            {
                var files = value.Zip(results, (s, i) => new { FileName = i.File, Content = s });
                foreach (var f in files)
                {
                    File.WriteAllText(Path.Combine(destinationDirectory, f.FileName), f.Content);
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error evaluation file: {ex.Message}");
        throw;
    }
}