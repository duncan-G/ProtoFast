using System.Diagnostics;

namespace ProtoFast.AppHost;

/// <summary>
/// AwsSignIn starts an SSO session for the launch profile's AWS_PROFILE when there is none, so
/// the processes can read protofast/dev at startup. Under `aspire run` only.
/// </summary>
internal static class AwsSignIn
{
    public static void EnsureSession()
    {
        var profile = Environment.GetEnvironmentVariable("AWS_PROFILE");
        if (string.IsNullOrWhiteSpace(profile) || Aws("sts", "get-caller-identity", "--profile", profile) == 0)
        {
            return;
        }

        Console.WriteLine($"No AWS session for profile '{profile}'. Running aws sso login…");
        if (Aws("sso", "login", "--profile", profile) != 0)
        {
            throw new InvalidOperationException(
                $"aws sso login --profile {profile} failed. Make the profile once with: aws configure sso --profile {profile}");
        }
    }

    private static int Aws(params string[] args)
    {
        var quiet = args[0] == "sts";
        var start = new ProcessStartInfo("aws", args) { RedirectStandardOutput = quiet, RedirectStandardError = quiet };
        try
        {
            using var process = Process.Start(start)!;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException("The AWS CLI v2 is required to read protofast/dev.", e);
        }
    }
}
