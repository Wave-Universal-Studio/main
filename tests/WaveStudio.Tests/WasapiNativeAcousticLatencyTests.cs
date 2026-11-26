using System.Text.Json;
using NAudio.CoreAudioApi;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiNativeAcousticLatencyTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativeAcousticLatency")]
    public async Task AuthorizedHeadsetAndMicrophoneMeasureAcousticPath()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_ACOUSTIC_START_AUTHORIZED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_ACOUSTIC_SETUP_CONFIRMED"));
        Assert.True(Environment.UserInteractive && !Console.IsInputRedirected,
            "A prova exige console interativo e acesso a Esc.");
        WasapiNativeAudibleProofTests.AssertNoOtherTestApphost();
        var token = TestContext.Current.CancellationToken;
        var route = await WasapiNativeSilentTests.Preflight("WAVESTUDIO_ACOUSTIC_CAPTURE_NAME", "WAVESTUDIO_ACOUSTIC_HEADSET_NAME", token);
        await CheckIsolationAsync(route, token);

        Console.WriteLine("Retire o headset dos ouvidos e encoste uma concha a 2–5 cm do FIFINE desmutado.");
        Console.WriteLine("Mantenha o volume baixo. Feche chamadas, reprodução e monitoração do microfone.");
        Console.WriteLine("Esc testa a parada. Enter inicia três sinais de 18 ms em menos de três segundos. Esc cancela.");
        await WasapiNativeAudibleProofTests.ConfirmStart(token, TimeSpan.FromSeconds(60));
        WasapiNativeSilentTests.AssertNoPreviewProcess();
        WasapiNativeAudibleProofTests.AssertNoOtherTestApphost();
        await CheckIsolationAsync(route, token);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var consent = WasapiAudibleConsent.Confirm(route, headsetConfirmed: true, lowLevelConfirmed: true, stop.Token);
        var isolationReady = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var isolation = WatchIsolationAsync(route, stop, isolationReady);
        var watcher = WatchStopAsync(stop);
        WasapiAcousticReport result;
        (int Checks, string? Error) isolationDuring;
        try
        {
            Assert.Null(await isolationReady.Task.WaitAsync(TimeSpan.FromSeconds(2), token));
            result = await WasapiAcousticSession.RunAsync(route, consent, token);
        }
        finally
        {
            await stop.CancelAsync();
            await watcher;
            try { isolationDuring = await isolation.WaitAsync(TimeSpan.FromSeconds(1), token); }
            catch (TimeoutException) { isolationDuring = (0, "IsolationWatchTimeout"); }
        }

        string? isolationAfterError = null;
        try { await CheckIsolationAsync(route, token); }
        catch (Exception error) when (error is Xunit.Sdk.XunitException || AudioError.IsDeviceAccessError(error))
        {
            isolationAfterError = error.GetType().Name;
        }

        // Os nomes, identificadores, timestamps absolutos e PCM ficam fora do arquivo.
        string report = JsonSerializer.Serialize(new
        {
            CaptureFormat = route.Capture.Mix,
            RenderFormat = route.Playback.Mix,
            CapturePeriodMs = route.Capture.DefaultPeriod100ns / 10_000d,
            RenderPeriodMs = route.Playback.DefaultPeriod100ns / 10_000d,
            StimulusPeakDbfs = 20 * Math.Log10(WasapiAcousticSignal.Peak),
            IsolationChecks = isolationDuring.Checks,
            IsolationDuringError = isolationDuring.Error,
            IsolationAfterError = isolationAfterError,
            Result = result
        }, new JsonSerializerOptions { WriteIndented = true });
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/wasapi-acoustic-latency"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"native-acoustic-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, report);
        Console.WriteLine($"Relatório numérico: {file}");
        output.WriteLine(report);

        Assert.Null(isolationDuring.Error);
        Assert.True(isolationDuring.Checks >= 2, "Falta uma amostra de isolamento durante a janela.");
        Assert.Null(isolationAfterError);
        Assert.True(result.Measured, $"Medição indisponível. Estágio: {result.Failure}; evidência: {file}");
    }

    private static Task CheckIsolationAsync(TransportRoute route, CancellationToken token) =>
        Task.Run(() =>
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var info in new[] { route.Capture, route.Playback })
            {
                token.ThrowIfCancellationRequested();
                using var device = enumerator.GetDevice(info.Endpoint.Id);
                RequireEndpoint(device, info.Endpoint);
                var adapter = NativeSingleCableIdentity.Read(enumerator, device).RequireRole(expectsCable: false);
                Assert.Equal("USB", NativeSingleCableIdentity.ReadEnumerator(adapter), ignoreCase: true);
                RequireQuiet(device);
            }
        }, token);

    private static Task<(int Checks, string? Error)> WatchIsolationAsync(TransportRoute route, CancellationTokenSource stop,
        TaskCompletionSource<string?> ready) => Task.Run(() =>
    {
        int checks = 0;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var capture = enumerator.GetDevice(route.Capture.Endpoint.Id);
            using var render = enumerator.GetDevice(route.Playback.Endpoint.Id);
            var stopSignal = stop.Token.WaitHandle;
            while (!stop.IsCancellationRequested)
            {
                RequireEndpoint(capture, route.Capture.Endpoint);
                RequireEndpoint(render, route.Playback.Endpoint);
                // As consultas COM e a enumeração de sessões ficam na thread auxiliar, fora do transporte de PCM.
                RequireQuiet(capture, (uint)Environment.ProcessId);
                RequireQuiet(render, (uint)Environment.ProcessId);
                checks++;
                ready.TrySetResult(null);
                stopSignal.WaitOne(250);
            }
            return (checks, (string?)null);
        }
        catch (Exception error) when (error is Xunit.Sdk.XunitException || AudioError.IsDeviceAccessError(error))
        {
            ready.TrySetResult(error.GetType().Name);
            stop.Cancel();
            return (checks, error.GetType().Name);
        }
        finally { ready.TrySetResult("IsolationWatchStopped"); }
    }, CancellationToken.None);

    private static void RequireEndpoint(MMDevice device, TransportEndpoint endpoint)
    {
        Assert.Equal(endpoint.Id, device.ID);
        Assert.Equal(DeviceState.Active, device.State);
        Assert.Equal(endpoint.Direction == EndpointDirection.Capture ? DataFlow.Capture : DataFlow.Render, device.DataFlow);
    }

    private static void RequireQuiet(MMDevice device, uint? ownerPid = null)
    {
        Assert.False(device.AudioEndpointVolume.Mute, "Os endpoints da prova precisam estar desmutados.");
        var manager = device.AudioSessionManager;
        if (ownerPid.HasValue) manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (int i = 0, count = sessions.Count; i < count; i++)
        {
            using var session = sessions[i];
            var state = (SessionActivity)(int)session.State;
            if (state is SessionActivity.Inactive or SessionActivity.Expired) continue;
            Assert.True(state == SessionActivity.Active && ownerPid.HasValue && !session.IsSystemSoundsSession &&
                session.GetProcessID == ownerPid.Value, "Sessão concorrente detectada no endpoint da prova.");
        }
    }

    private static async Task WatchStopAsync(CancellationTokenSource stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                {
                    await stop.CancelAsync();
                    return;
                }
                await Task.Delay(15, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            await stop.CancelAsync();
        }
    }
}
