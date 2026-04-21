using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using IoFile = System.IO.File;

var repoRoot = FindRepoRoot();
var artifactsRoot = Path.Combine(repoRoot, "tests", "TerraformPlugin.E2E", "artifacts", Guid.NewGuid().ToString("n"));

Directory.CreateDirectory(artifactsRoot);

await RunFileScenarioAsync(repoRoot, artifactsRoot);
await RunAzureStorageScenarioAsync(repoRoot, artifactsRoot);

Console.WriteLine("Terraform tfprotov6 end-to-end test passed.");

return;

static async Task RunFileScenarioAsync(string repoRoot, string artifactsRoot)
{
    var scenarioRoot = Path.Combine(artifactsRoot, "file");
    var providerOutput = Path.Combine(scenarioRoot, "provider");
    var terraformWorkdir = Path.Combine(scenarioRoot, "terraform");
    var dataDirectory = Path.Combine(scenarioRoot, "data");
    var terraformRcPath = Path.Combine(scenarioRoot, "terraform.rc");
    var providerProjectPath = Path.Combine(repoRoot, "samples", "File", "File.csproj");

    Directory.CreateDirectory(providerOutput);
    Directory.CreateDirectory(terraformWorkdir);
    Directory.CreateDirectory(dataDirectory);

    await RunAsync("dotnet", ["publish", providerProjectPath, "-c", "Release", "-o", providerOutput], repoRoot);

    var providerExecutable = Path.Combine(providerOutput, GetProviderExecutableName("terraform-provider-file"));

    if (!IoFile.Exists(providerExecutable))
    {
        throw new InvalidOperationException($"Expected published provider executable at '{providerExecutable}'.");
    }

    IoFile.WriteAllText(
        terraformRcPath,
        $@"provider_installation {{
  dev_overrides {{
    ""registry.terraform.io/example/file"" = ""{ToHclStringLiteral(providerOutput)}""
  }}
  direct {{}}
}}
");

    IoFile.WriteAllText(
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

    var environment = CreateTerraformEnvironment(terraformRcPath);

    await RunAsync("terraform", ["providers", "schema", "-json"], terraformWorkdir, environment);
    await RunAsync("terraform", ["validate", "-no-color"], terraformWorkdir, environment);
    await RunAsync("terraform", ["apply", "-auto-approve", "-no-color"], terraformWorkdir, environment);

    var expectedFilePath = Path.Combine(dataDirectory, "hello.txt");

    if (!IoFile.Exists(expectedFilePath))
    {
        throw new InvalidOperationException($"Expected Terraform to create '{expectedFilePath}'.");
    }

    var fileContents = await IoFile.ReadAllTextAsync(expectedFilePath);

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

    if (IoFile.Exists(expectedFilePath))
    {
        throw new InvalidOperationException($"Expected Terraform to delete '{expectedFilePath}'.");
    }
}

static async Task RunAzureStorageScenarioAsync(string repoRoot, string artifactsRoot)
{
    var scenarioRoot = Path.Combine(artifactsRoot, "az_storage");
    var providerOutput = Path.Combine(scenarioRoot, "provider");
    var terraformWorkdir = Path.Combine(scenarioRoot, "terraform");
    var azuriteDataDirectory = Path.Combine(scenarioRoot, "azurite");
    var terraformRcPath = Path.Combine(scenarioRoot, "terraform.rc");
    var providerProjectPath = Path.Combine(repoRoot, "samples", "Azure", "Azure.csproj");

    Directory.CreateDirectory(providerOutput);
    Directory.CreateDirectory(terraformWorkdir);
    Directory.CreateDirectory(azuriteDataDirectory);

    await RunAsync("dotnet", ["publish", providerProjectPath, "-c", "Release", "-o", providerOutput], repoRoot);

    var providerExecutable = Path.Combine(providerOutput, GetProviderExecutableName("terraform-provider-az"));

    if (!IoFile.Exists(providerExecutable))
    {
        throw new InvalidOperationException($"Expected published provider executable at '{providerExecutable}'.");
    }

    var blobPort = GetFreeTcpPort();
    var accountName = "sdktest";
    var accountKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    var accountImportId = $"/providers/Microsoft.Storage/storageAccounts/{accountName}";
    var importId = $"/providers/Microsoft.Storage/storageAccounts/{accountName}/blobServices/default/containers/sdk-container/blobs/hello.txt";
    var connectionString =
        $"DefaultEndpointsProtocol=http;AccountName={accountName};AccountKey={accountKey};BlobEndpoint=http://127.0.0.1:{blobPort}/{accountName};";

    using var azuriteProcess = StartBackgroundProcess(
        "npx",
        [
            "--yes",
            "azurite",
            "--silent",
            "--skipApiVersionCheck",
            "--location", azuriteDataDirectory,
            "--blobHost", "127.0.0.1",
            "--blobPort", blobPort.ToString(CultureInfo.InvariantCulture),
        ],
        repoRoot,
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AZURITE_ACCOUNTS"] = $"{accountName}:{accountKey}",
        });

    try
    {
        await WaitForTcpPortAsync(IPAddress.Loopback, blobPort);

        IoFile.WriteAllText(
            terraformRcPath,
            $@"provider_installation {{
  dev_overrides {{
    ""registry.terraform.io/example/az"" = ""{ToHclStringLiteral(providerOutput)}""
  }}
  direct {{}}
}}
");

        IoFile.WriteAllText(
            Path.Combine(terraformWorkdir, "main.tf"),
            $@"terraform {{
  required_providers {{
    az = {{
      source = ""example/az""
    }}
  }}
}}

provider ""az"" {{
  connection_string = ""{ToHclStringLiteral(connectionString)}""
}}

resource ""az_storage_account"" ""account"" {{
  account_name = ""{accountName}""
}}

resource ""az_storage_blob"" ""sample"" {{
  container_name = ""sdk-container""
  blob_name      = ""hello.txt""
  content        = ""hello from azure dotnet""
}}

data ""az_storage_blob"" ""sample"" {{
  container_name = az_storage_blob.sample.container_name
  blob_name      = az_storage_blob.sample.blob_name
}}

data ""az_storage_account"" ""current"" {{
  account_name = az_storage_account.account.account_name
}}

output ""content"" {{
  value = data.az_storage_blob.sample.content
}}

output ""blob_endpoint"" {{
  value = data.az_storage_account.current.blob_endpoint
}}
");

        IoFile.WriteAllText(
            Path.Combine(terraformWorkdir, "query.tfquery.hcl"),
            $@"list ""az_storage_account"" ""accounts"" {{
  provider = az
  include_resource = true
  limit = 10
  config {{}}
}}
");

        var environment = CreateTerraformEnvironment(terraformRcPath);

        var schemaResult = await RunAsync("terraform", ["providers", "schema", "-json"], terraformWorkdir, environment);

        if (!schemaResult.StandardOutput.Contains("az_storage_account", StringComparison.Ordinal) ||
            !schemaResult.StandardOutput.Contains("az_storage_blob", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Terraform provider schema did not contain the expected Azure resource types.");
        }

        await RunAsync("terraform", ["validate", "-no-color"], terraformWorkdir, environment);
        await RunAsync("terraform", ["apply", "-auto-approve", "-no-color"], terraformWorkdir, environment);

        var outputResult = await RunAsync("terraform", ["output", "-raw", "content"], terraformWorkdir, environment);

        if (!string.Equals(outputResult.StandardOutput.Trim(), "hello from azure dotnet", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected az output value: '{outputResult.StandardOutput.Trim()}'.");
        }

        var endpointResult = await RunAsync("terraform", ["output", "-raw", "blob_endpoint"], terraformWorkdir, environment);

        if (!string.Equals(endpointResult.StandardOutput.Trim(), $"http://127.0.0.1:{blobPort}/{accountName}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected az blob_endpoint value: '{endpointResult.StandardOutput.Trim()}'.");
        }

        var queryResult = await RunAsync("terraform", ["query", "-json", "-no-color"], terraformWorkdir, environment);

        if (!queryResult.StandardOutput.Contains(accountImportId, StringComparison.Ordinal) ||
            !queryResult.StandardOutput.Contains("az_storage_account", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Terraform query did not return the expected account identity.{Environment.NewLine}{queryResult.CombinedOutput}");
        }

        await RunAsync("terraform", ["state", "rm", "az_storage_account.account"], terraformWorkdir, environment);
        await RunAsync("terraform", ["import", "az_storage_account.account", accountImportId], terraformWorkdir, environment);
        await RunAsync("terraform", ["state", "rm", "az_storage_blob.sample"], terraformWorkdir, environment);
        await RunAsync("terraform", ["import", "az_storage_blob.sample", importId], terraformWorkdir, environment);

        var planResult = await RunAsync(
            "terraform",
            ["plan", "-detailed-exitcode", "-no-color"],
            terraformWorkdir,
            environment,
            acceptedExitCodes: [0, 2]);

        if (planResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Expected converged az plan exit code 0, saw {planResult.ExitCode}.{Environment.NewLine}{planResult.CombinedOutput}");
        }

        await RunAsync("terraform", ["destroy", "-auto-approve", "-no-color"], terraformWorkdir, environment);
    }
    finally
    {
        TryStopProcess(azuriteProcess);
    }
}

static IReadOnlyDictionary<string, string?> CreateTerraformEnvironment(string terraformRcPath) =>
    new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["TF_CLI_CONFIG_FILE"] = terraformRcPath,
        ["TF_IN_AUTOMATION"] = "1",
        ["CHECKPOINT_DISABLE"] = "1",
    };

static Process StartBackgroundProcess(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string?>? additionalEnvironment = null)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
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

    var process = new Process { StartInfo = startInfo };
    process.Start();
    return process;
}

static async Task WaitForTcpPortAsync(IPAddress address, int port)
{
    var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);

    while (DateTimeOffset.UtcNow < timeoutAt)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(address, port);
            return;
        }
        catch (SocketException)
        {
            await Task.Delay(200);
        }
    }

    throw new TimeoutException($"Timed out waiting for TCP {address}:{port}.");
}

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

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null)
    {
        if (IoFile.Exists(Path.Combine(current.FullName, "TerraformPlugin.slnx")) &&
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

static string GetProviderExecutableName(string baseName) =>
    OperatingSystem.IsWindows()
        ? $"{baseName}.exe"
        : baseName;

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

static void TryStopProcess(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }
    catch (InvalidOperationException)
    {
    }
}

internal sealed record CommandResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CommandLine => string.Join(" ", [FileName, .. Arguments]);

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
