using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TextChunker.Api.Tests;

/// <summary>
/// Поднимает реальный процесс ASP.NET Core (Kestrel) на свободном порту и даёт
/// HttpClient для интеграционных тестов. Без Microsoft.AspNetCore.Mvc.Testing.
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;

    public HttpClient Client { get; }

    public string BaseUrl { get; }

    private ApiServer(Process process, int port)
    {
        _process = process;
        BaseUrl = $"http://127.0.0.1:{port}";
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        _stdoutPump = PumpAsync(process.StandardOutput, _stdout);
        _stderrPump = PumpAsync(process.StandardError, _stderr);
    }

    public static async Task<ApiServer> StartAsync(IDictionary<string, string>? environment = null)
    {
        int port = GetFreePort();
        string dll = FindApiDll();

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll)!
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--urls");
        psi.ArgumentList.Add($"http://127.0.0.1:{port}");
        psi.ArgumentList.Add("--environment");
        psi.ArgumentList.Add("Testing");

        if (environment is not null)
        {
            foreach (KeyValuePair<string, string> pair in environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить TextChunker.Api.dll");

        var server = new ApiServer(process, port);
        await server.WaitUntilReadyAsync();
        return server;
    }

    public async Task WaitUntilReadyAsync()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await Client.GetAsync("/api/v1/health");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            if (_process.HasExited)
            {
                break;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            $"API не запустился.{Environment.NewLine}STDOUT:{Environment.NewLine}{_stdout}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{_stderr}");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        try
        {
            await _process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
        }

        await Task.WhenAll(_stdoutPump, _stderrPump);
        _process.Dispose();
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder sink)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null)
            {
                return;
            }
            lock (sink)
            {
                sink.AppendLine(line);
            }
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string FindApiDll()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "TextChunker.Api.dll"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "TextChunker.Api", "bin", "Debug", "net8.0", "TextChunker.Api.dll"))
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "TextChunker.Api.dll не найден. Проверьте ProjectReference и сборку.",
            candidates[0]);
    }
}
