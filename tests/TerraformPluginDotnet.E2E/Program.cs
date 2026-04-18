using System.Diagnostics;
using System.Text;

var repoRoot = FindRepoRoot();
var artifactsRoot = Path.Combine(repoRoot, "tests", "TerraformPluginDotnet.E2E", "artifacts", Guid.NewGuid().ToString("n"));
var providerOutput = Path.Combine(artifactsRoot, "provider");
var terraformWorkdir = Path.Combine(artifactsRoot, "terraform");
var dataDirectory = Path.Combine(artifactsRoot, "data");
var terraformRcPath = Path.Combine(artifactsRoot, "terraform.rc");
var providerProjectPath = Path.Combine(repoRoot, "samples", "TerraformProviderFile", "TerraformProviderFile.csproj");

Directory.CreateDirectory(providerOutput);
Directory.CreateDirectory(terraformWorkdir);
Directory.CreateDirectory(dataDirectory);

await RunAsync(
    "dotnet",
    ["publish", providerProjectPath, "-c", "Release", "-o", providerOutput],
    repoRoot);

var providerExecutable = Path.Combine(providerOutput, GetProviderExecutableName());

if (!File.Exists(providerExecutable))
{
    throw new InvalidOperationException($"Expected published provider executable at '{providerExecutable}'.");
}

File.WriteAllText(
    terraformRcPath,
    $@"provider_installation {{
  dev_overrides {{
    ""registry.terraform.io/example/file"" = ""{ToHclStringLiteral(providerOutput)}""
  }}
  direct {{}}
}}
");

File.WriteAllText(
    Path.Combine(terraformWorkdir, "main.tf"),
    $@"terraform {{
  required_providers {{
    file = {{
      source = ""example/file""
    }}
  }}
}}

provider ""file"" {{
  base_directory = ""{ToHclStringLiteral(dataDirectory)}""
}}

resource ""file_managed"" ""sample"" {{
  path    = ""hello.txt""
  content = ""hello from dotnet""
}}

data ""file_read"" ""sample"" {{
  path = file_managed.sample.path
}}

output ""content"" {{
  value = data.file_read.sample.content
}}
");

var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
{
    ["TF_CLI_CONFIG_FILE"] = terraformRcPath,
    ["TF_IN_AUTOMATION"] = "1",
    ["CHECKPOINT_DISABLE"] = "1",
};

await RunAsync("terraform", ["apply", "-auto-approve", "-no-color"], terraformWorkdir, environment);

var expectedFilePath = Path.Combine(dataDirectory, "hello.txt");

if (!File.Exists(expectedFilePath))
{
    throw new InvalidOperationException($"Expected Terraform to create '{expectedFilePath}'.");
}

var fileContents = await File.ReadAllTextAsync(expectedFilePath);

if (!string.Equals(fileContents, "hello from dotnet", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"Unexpected file contents: '{fileContents}'.");
}

var outputResult = await RunAsync("terraform", ["output", "-raw", "content"], terraformWorkdir, environment);

if (!string.Equals(outputResult.StandardOutput.Trim(), "hello from dotnet", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"Unexpected terraform output value: '{outputResult.StandardOutput.Trim()}'.");
}

var planResult = await RunAsync(
    "terraform",
    ["plan", "-detailed-exitcode", "-no-color"],
    terraformWorkdir,
    environment,
    acceptedExitCodes: [0, 2]);

if (planResult.ExitCode != 0)
{
    throw new InvalidOperationException(
        $"Expected converged plan exit code 0, saw {planResult.ExitCode}.{Environment.NewLine}{planResult.CombinedOutput}");
}

await RunAsync("terraform", ["destroy", "-auto-approve", "-no-color"], terraformWorkdir, environment);

if (File.Exists(expectedFilePath))
{
    throw new InvalidOperationException($"Expected Terraform to delete '{expectedFilePath}'.");
}

Console.WriteLine("Terraform tfprotov6 end-to-end test passed.");

return;

static async Task<CommandResult> RunAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string?>? additionalEnvironment = null,
    IReadOnlyCollection<int>? acceptedExitCodes = null)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    if (additionalEnvironment is not null)
    {
        foreach (var pair in additionalEnvironment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
    }

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
    var standardErrorTask = process.StandardError.ReadToEndAsync();

    await process.WaitForExitAsync();

    var standardOutput = await standardOutputTask;
    var standardError = await standardErrorTask;
    var result = new CommandResult(fileName, arguments, process.ExitCode, standardOutput, standardError);

    var exitCodes = acceptedExitCodes ?? [0];

    if (!exitCodes.Contains(result.ExitCode))
    {
        throw new InvalidOperationException(
            $"Command failed ({result.ExitCode}): {result.CommandLine}{Environment.NewLine}{result.CombinedOutput}");
    }

    return result;
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "TerraformPluginDotnet.slnx")) &&
            Directory.Exists(Path.Combine(current.FullName, "src")) &&
            Directory.Exists(Path.Combine(current.FullName, "samples")) &&
            Directory.Exists(Path.Combine(current.FullName, "tests")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
}

static string GetProviderExecutableName() =>
    OperatingSystem.IsWindows()
        ? "terraform-provider-file.exe"
        : "terraform-provider-file";

static string ToHclStringLiteral(string value)
{
    var builder = new StringBuilder(value.Length);

    foreach (var character in value)
    {
        builder.Append(character switch
        {
            '\\' => "\\\\",
            '"' => "\\\"",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => character,
        });
    }

    return builder.ToString();
}

internal sealed record CommandResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CommandLine => string.Join(" ", [FileName, ..Arguments]);

    public string CombinedOutput
    {
        get
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(StandardOutput))
            {
                builder.AppendLine("stdout:");
                builder.AppendLine(StandardOutput.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(StandardError))
            {
                builder.AppendLine("stderr:");
                builder.AppendLine(StandardError.TrimEnd());
            }

            return builder.ToString().TrimEnd();
        }
    }
}
