using System.Diagnostics;
using System.Text;

namespace ProtoFast.AppHost.Aws;

/// <summary>
/// Ensures the local AppHost is logged into AWS SSO profile <c>developer</c>
/// so child services can resolve that profile (Secrets Manager, etc.).
/// Call only outside publish mode: production uses the instance role.
/// </summary>
internal static class AwsDeveloperSso
{
    public const string ProfileName = "developer";

    public static void EnsureAuthenticated()
    {
        Environment.SetEnvironmentVariable("AWS_PROFILE", ProfileName);
        Environment.SetEnvironmentVariable("AWS_SDK_LOAD_CONFIG", "true");

        EnsureAwsCli();
        EnsureProfile();

        if (IsAuthenticated())
        {
            return;
        }

        Console.WriteLine(
            $"AWS SSO session for profile '{ProfileName}' is missing or expired. Starting login…");
        Login();

        if (!IsAuthenticated())
        {
            throw new InvalidOperationException(
                $"AWS SSO login for profile '{ProfileName}' did not produce usable credentials.");
        }
    }

    public static IResourceBuilder<T> WithSsoProfile<T>(this IResourceBuilder<T> resource)
        where T : IResourceWithEnvironment
    {
        if (resource.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return resource;
        }

        return resource
            .WithEnvironment("AWS_PROFILE", ProfileName)
            .WithEnvironment("AWS_SDK_LOAD_CONFIG", "true");
    }

    private static void EnsureAwsCli()
    {
        var result = RunAws(["--version"], capture: true);
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "AWS CLI v2 is required for local AWS access. Install it, then run: aws configure sso --profile developer");
    }

    private static void EnsureProfile()
    {
        if (ProfileExists())
        {
            return;
        }

        throw new InvalidOperationException(
            $"""
            AWS SSO profile '{ProfileName}' is not configured.
            Run once:
              aws configure sso --profile {ProfileName}
            Choose the Developer permission set, then restart aspire.
            """);
    }

    private static bool ProfileExists()
    {
        var listed = RunAws(["configure", "list-profiles"], capture: true);
        if (listed.ExitCode == 0)
        {
            return listed.StdOut
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim().Equals(ProfileName, StringComparison.Ordinal));
        }

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aws", "config");
        if (!File.Exists(configPath))
        {
            return false;
        }

        var text = File.ReadAllText(configPath);
        return text.Contains($"[profile {ProfileName}]", StringComparison.Ordinal)
               || text.Contains($"[{ProfileName}]", StringComparison.Ordinal);
    }

    private static bool IsAuthenticated()
    {
        var result = RunAws(
            ["sts", "get-caller-identity", "--profile", ProfileName, "--output", "text"],
            capture: true);
        return result.ExitCode == 0;
    }

    private static void Login()
    {
        var result = RunAws(["sso", "login", "--profile", ProfileName], capture: false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"aws sso login --profile {ProfileName} failed (exit {result.ExitCode}). {result.StdErr.Trim()}");
        }
    }

    private static AwsCliResult RunAws(IReadOnlyList<string> args, bool capture)
    {
        var psi = new ProcessStartInfo("aws")
        {
            UseShellExecute = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
            StandardOutputEncoding = capture ? Encoding.UTF8 : null,
            StandardErrorEncoding = capture ? Encoding.UTF8 : null,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start aws.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "AWS CLI v2 is required for local AWS access. Install it, then run: aws configure sso --profile developer",
                ex);
        }

        using (process)
        {
            var stdout = "";
            var stderr = "";
            if (capture)
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                stdout = stdoutTask.GetAwaiter().GetResult();
                stderr = stderrTask.GetAwaiter().GetResult();
            }
            else
            {
                process.WaitForExit();
            }

            return new AwsCliResult(process.ExitCode, stdout, stderr);
        }
    }

    private readonly record struct AwsCliResult(int ExitCode, string StdOut, string StdErr);
}
