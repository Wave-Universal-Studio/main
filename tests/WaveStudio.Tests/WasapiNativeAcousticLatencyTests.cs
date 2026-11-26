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
        await WasapiNativeAudibleProofTests.ConfirmStart(token);
        WasapiNativeSilentTests.AssertNoPreviewProcess();
        WasapiNativeAudibleProofTests.AssertNoOtherTestApphost();
        await CheckIsolationAsync(route, token);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var consent = WasapiAudibleConsent.Confirm(route, headsetConfirmed: true, lowLevelConfirmed: true, stop.Token);
        var watcher = WatchStopAsync(stop);
        WasapiAcousticReport result;
        try { result = await WasapiAcousticSession.RunAsync(route, consent, token); }
        finally
        {
            await stop.CancelAsync();
            await watcher;
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
            IsolationAfterError = isolationAfterError,
            Result = result
        }, new JsonSerializerOptions { WriteIndented = true });
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/wasapi-acoustic-latency"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"native-acoustic-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, report);
        Console.WriteLine($"Relatório numérico: {file}");
        output.WriteLine(report);

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
                Assert.Equal(DeviceState.Active, device.State);
                var adapter = NativeSingleCableIdentity.Read(enumerator, device).RequireRole(expectsCable: false);
                Assert.Equal("USB", NativeSingleCableIdentity.ReadEnumerator(adapter), ignoreCase: true);
                using var volume = device.AudioEndpointVolume;
                Assert.False(volume.Mute, "Os endpoints da prova precisam estar desmutados.");
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    Assert.Contains((SessionActivity)(int)session.State, new[] { SessionActivity.Inactive, SessionActivity.Expired });
                }
            }
        }, token);

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
