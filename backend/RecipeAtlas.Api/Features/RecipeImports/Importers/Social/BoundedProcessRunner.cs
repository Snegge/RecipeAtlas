using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed record MetadataProcessResult(int ExitCode, string Output, string Error);
public interface IMetadataProcessRunner
{
    Task<MetadataProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

// The SDK's line reader has no byte bound. Read streams in bounded chunks instead.
public sealed class BoundedProcessRunner : IMetadataProcessRunner
{
    private readonly SemaphoreSlim capacity = new(2, 2);
    private readonly TimeSpan executionTimeout;
    public BoundedProcessRunner(TimeSpan? executionTimeout = null)
    {
        this.executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(30);
        if (this.executionTimeout <= TimeSpan.Zero || this.executionTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(executionTimeout));
    }
    public async Task<MetadataProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!await capacity.WaitAsync(0, cancellationToken))
            throw new RecipeImportException("Two imports are already running. Try again shortly.", 429, "import_busy", true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(executionTimeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        var started = false;
        // Do not pass the application's owner/model credentials to the child or JS engine.
        foreach (var key in process.StartInfo.Environment.Keys.ToArray())
            if (key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) || key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) || key is "GEMINI_API_KEY" or "GOOGLE_API_KEY")
                process.StartInfo.Environment.Remove(key);
        try
        {
            try { started = process.Start(); }
            catch (Win32Exception)
            {
                throw new RecipeImportException("yt-dlp is not installed or its configured executable cannot start. Install the documented version or paste the description.",
                    503, "metadata_unavailable", true);
            }
            using var stop = timeout.Token.Register(() => Kill(process));
            var output = ReadAsync(process.StandardOutput.BaseStream, 2 * 1024 * 1024, timeout.Token);
            var error = ReadAsync(process.StandardError.BaseStream, 64 * 1024, timeout.Token);
            // An output-limit failure must kill immediately, even while the other pipe is blocked.
            var exited = process.WaitForExitAsync(timeout.Token);
            var remaining = new List<Task> { output, error, exited };
            while (remaining.Count > 0)
            {
                var finished = await Task.WhenAny(remaining);
                await finished;
                remaining.Remove(finished);
            }
            return new(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RecipeImportException("Caption retrieval timed out. Try again or paste the description.", 504, "metadata_timeout", true);
        }
        finally
        {
            try
            {
                Kill(process);
                timeout.Cancel();
                if (started)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(cleanup.Token);
                }
            }
            finally { capacity.Release(); }
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception)
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }
    private static async Task<string> ReadAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (output.Length + count > limit)
                throw new RecipeImportException("Platform metadata exceeded the safe output limit. Paste the description instead.",
                    422, "metadata_too_large", true);
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
