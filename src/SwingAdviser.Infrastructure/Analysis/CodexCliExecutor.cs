using System.Diagnostics;
using SwingAdviser.Application.Analysis;

namespace SwingAdviser.Infrastructure.Analysis;

/// <summary>Starts Codex without a shell. Each configured argument is passed through ProcessStartInfo.ArgumentList.</summary>
public sealed class CodexCliExecutor : IAiCliExecutor
{
    public async Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startInfo = new ProcessStartInfo { FileName = request.ExecutablePath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) startInfo.WorkingDirectory = request.WorkingDirectory;
        // Codex exec accepts the prompt as one argument; ArgumentList preserves it as data rather than shell syntax.
        startInfo.ArgumentList.Add("exec");
        foreach (var argument in request.AdditionalArguments) startInfo.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(request.Model)) { startInfo.ArgumentList.Add("--model"); startInfo.ArgumentList.Add(request.Model); }
        startInfo.ArgumentList.Add(request.Prompt);
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
                return new AiCliResponse(await stdout, await stderr, process.HasExited ? process.ExitCode : null, timeout.IsCancellationRequested ? AiCliCompletion.TimedOut : AiCliCompletion.Cancelled);
            }
            await Task.WhenAll(stdout, stderr);
            return new AiCliResponse(await stdout, await stderr, process.ExitCode, AiCliCompletion.Completed);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new AiCliResponse(string.Empty, exception.Message, null, AiCliCompletion.FailedToStart);
        }
    }
}
