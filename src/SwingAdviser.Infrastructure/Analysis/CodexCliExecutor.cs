using System.Diagnostics;
using SwingAdviser.Application.Analysis;

namespace SwingAdviser.Infrastructure.Analysis;

/// <summary>Starts an isolated Codex research session without a shell. Each configured argument is passed through ProcessStartInfo.ArgumentList.</summary>
public sealed class CodexCliExecutor : IAiCliExecutor
{
    public async Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var finalMessagePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-codex-{Guid.NewGuid():N}.txt");
        var startInfo = CreateStartInfo(
            request,
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            finalMessagePath);
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return new AiCliResponse(string.Empty, "The CLI process did not start.", null, AiCliCompletion.FailedToStart);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(request.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await Task.WhenAll(stdout, stderr);
                return new AiCliResponse(ReadFinalMessageOrFallback(finalMessagePath, await stdout), await stderr, process.HasExited ? process.ExitCode : null, timeout.IsCancellationRequested ? AiCliCompletion.TimedOut : AiCliCompletion.Cancelled);
            }
            await Task.WhenAll(stdout, stderr);
            return new AiCliResponse(ReadFinalMessageOrFallback(finalMessagePath, await stdout), await stderr, process.ExitCode, AiCliCompletion.Completed);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new AiCliResponse(string.Empty, exception.Message, null, AiCliCompletion.FailedToStart);
        }
        finally
        {
            try { if (File.Exists(finalMessagePath)) File.Delete(finalMessagePath); } catch (IOException) { }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(AiCliRequest request, string? homeDirectory, string? userProfileDirectory, string? finalMessagePath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startInfo = new ProcessStartInfo { FileName = request.ExecutablePath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) startInfo.WorkingDirectory = request.WorkingDirectory;
        // The desktop host may not inherit HOME on Windows even when USERPROFILE exists. Codex CLI
        // uses HOME to locate its authenticated configuration, so copy an explicit HOME or supply
        // the standard profile only for this child process.
        if (!string.IsNullOrWhiteSpace(homeDirectory)) startInfo.Environment["HOME"] = homeDirectory;
        else if (!string.IsNullOrWhiteSpace(userProfileDirectory)) startInfo.Environment["HOME"] = userProfileDirectory;
        // AI research must not load the user's interactive Codex configuration: it can start unrelated
        // MCP servers (for example language servers) for every candidate. Codex retains authentication
        // while ignoring that configuration, and ArgumentList keeps the prompt as data rather than shell syntax.
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--ignore-user-config");
        foreach (var argument in request.AdditionalArguments) startInfo.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(request.Model)) { startInfo.ArgumentList.Add("--model"); startInfo.ArgumentList.Add(request.Model); }
        if (!string.IsNullOrWhiteSpace(finalMessagePath)) { startInfo.ArgumentList.Add("--output-last-message"); startInfo.ArgumentList.Add(finalMessagePath); }
        startInfo.ArgumentList.Add(request.Prompt);
        return startInfo;
    }

    private static string ReadFinalMessageOrFallback(string path, string fallback)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : fallback; }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
    }
}
