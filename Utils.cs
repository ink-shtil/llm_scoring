using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

public static class Utils
{
    public static void CopyDirectory(string sourceDir, string destDir)
    {
        // Create the destination directory if it doesn't exist
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Get all files in the source directory
        string[] files = Directory.GetFiles(sourceDir);

        // Copy each file to the destination directory
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true); // true to overwrite existing files
        }

        // Get all subdirectories in the source directory
        string[] subDirs = Directory.GetDirectories(sourceDir);

        // Recursively copy each subdirectory to the destination directory
        foreach (string subDir in subDirs)
        {
            string subDirName = Path.GetFileName(subDir);
            string destSubDir = Path.Combine(destDir, subDirName);
            CopyDirectory(subDir, destSubDir);
        }
    }

    public static void TotalLine()
    {
        Console.WriteLine("".PadLeft(44, '='));
    }

    public static string GenerateRandomString(int length) => Guid.NewGuid().ToString("n")[..length];

    public static void TestResults(Stat stat)
    {
        double categoryPercent = (double)stat.TotalPoints / stat.MaxPoints * 100d;

        // Save original console color
        ConsoleColor originalColor = Console.ForegroundColor;

        // Print category name in cyan
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{stat.Category,20}");
        Console.ForegroundColor = originalColor;

        Console.Write($" {stat.TotalPoints,2}/{stat.MaxPoints,2}, ");

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

    public static string Interpret(string text, string directory)
    {
        string pattern = @"\{([^}]+)\}";

        string result = Regex.Replace(text, pattern, match =>
        {
            string fileName = match.Groups[1].Value;
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) throw new Exception($"file is not found {path}");

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.Append("```");
            sb.Append(MarkdownLangByFileName(fileName));
            sb.Append(' ');
            sb.AppendLine(fileName);
            sb.AppendLine(File.ReadAllText(path));
            sb.AppendLine("```");
            return sb.ToString();
        });

        return result;
    }

    public static string MarkdownLangByFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".py" => "python",
            ".java" => "java",
            ".cpp" => "cpp",
            ".h" => "cpp",
            ".hpp" => "cpp",
            ".html" => "html",
            ".css" => "css",
            ".json" => "json",
            ".xml" => "xml",
            ".md" => "markdown",
            ".sql" => "sql",
            ".sh" => "bash",
            ".rb" => "ruby",
            ".php" => "php",
            ".go" => "go",
            ".ts" => "typescript",
            ".swift" => "swift",
            ".kt" => "kotlin",
            ".rs" => "rust",
            ".m" => "objective-c",
            ".scala" => "scala",
            ".groovy" => "groovy",
            ".lua" => "lua",
            ".r" => "r",
            ".pl" => "perl",
            ".dart" => "dart",
            ".vb" => "vbnet",
            ".fsharp" => "fsharp",
            ".erl" => "erlang",
            ".ex" => "elixir",
            ".hs" => "haskell",
            ".jl" => "julia",
            ".clj" => "clojure",
            ".fs" => "fsharp",
            ".coffee" => "coffeescript",
            ".less" => "less",
            ".sass" => "sass",
            ".scss" => "scss",
            ".styl" => "stylus",
            ".pug" => "pug",
            ".haml" => "haml",
            ".twig" => "twig",
            ".ejs" => "ejs",
            ".jsx" => "jsx",
            ".tsx" => "tsx",
            ".vue" => "vue",
            ".svelte" => "svelte",
            ".graphql" => "graphql",
            ".yaml" => "yaml",
            ".toml" => "toml",
            ".ini" => "ini",
            ".conf" => "conf",
            ".dockerfile" => "dockerfile",
            ".makefile" => "makefile",
            ".cmake" => "cmake",
            ".batch" => "batch",
            ".ps1" => "powershell",
            ".bat" => "batch",
            ".cmd" => "batch",
            ".zsh" => "zsh",
            ".fish" => "fish",
            ".awk" => "awk",
            ".sed" => "sed",
            ".tcl" => "tcl",
            ".nim" => "nim",
            ".vhdl" => "vhdl",
            ".verilog" => "verilog",
            ".systemverilog" => "systemverilog",
            ".v" => "verilog",
            ".sv" => "systemverilog",
            ".vhd" => "vhdl",
            _ => "unknown"
        };
    }
}

public class TestsConfig
    {
        [JsonProperty("models")]
        public string[] Models { get; set; }

        [JsonProperty("tests")]
        public Test[] Tests { get; set; }

        public static void Check(TestsConfig? config)
        {
            var emptyModels = !(config?.Models?.Any() ?? false);
            var emptyTests = !(config?.Tests?.Any() ?? false);
            if (emptyModels || emptyTests) throw new Exception("Tests is not configured properly. Check 'tests.json' file");
        }
    }

public class Test
{
    [JsonProperty("dir")]
    public string Dir { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("prompt")]
    public string Prompt { get; set; }

    [JsonProperty("results")]
    public List<QueryResult> Results { get; set; }

    [JsonProperty("scoring")]
    public List<Scoring> Scoring { get; set; }
}

public class QueryResult
{
    [JsonProperty("lang")]
    public string Lang { get; set; }

    [JsonProperty("file")]
    public string File { get; set; }
}

public class Scoring
{
    [JsonProperty("output")]
    public string Output { get; set; }

    [JsonProperty("type")]
    public OutputType Type { get; set; }

    [JsonProperty("score")]
    public int Score { get; set; }
}

public enum OutputType
{
    Exact = 0,
    Contains = 1
}

public record struct Stat(string Category, int TotalPoints, int MaxPoints, TimeSpan Duration)
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

    public Stat WithName(string name) => new(name, TotalPoints, MaxPoints, Duration);
}
