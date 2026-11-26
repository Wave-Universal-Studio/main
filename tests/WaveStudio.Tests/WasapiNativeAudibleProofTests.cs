using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiNativeAudibleProofTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativeBoundedAudibleProof")]
    public async Task AuthorizedHeadsetRunsBoundedAudibleNominalProof()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_AUDIBLE_PROOF_START_AUTHORIZED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_AUDIBLE_PROOF_PREFLIGHT_CONFIRMED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_AUDIBLE_PROOF_HEADSET_CONFIRMED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_AUDIBLE_PROOF_LOW_LEVEL_CONFIRMED"));
        Assert.True(Environment.UserInteractive && !Console.IsInputRedirected,
            "A prova requer console interativo em primeiro plano e parada ao alcance do operador.");

        AssertNoOtherTestApphost();
        var token = TestContext.Current.CancellationToken;
        var route = await WasapiNativeSilentTests.Preflight("WAVESTUDIO_AUDIBLE_PROOF_CAPTURE_NAME", "WAVESTUDIO_AUDIBLE_PROOF_HEADSET_NAME", token);
        Console.WriteLine("Com o headset colocado e em volume baixo, pressione Esc para verificar a parada. Depois, Enter confirma uma tentativa audível de até 5 s. Esc cancela.");
        await ConfirmStart(token);
        WasapiNativeSilentTests.AssertNoPreviewProcess();
        AssertNoOtherTestApphost();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var consent = WasapiAudibleConsent.Confirm(route, headsetConfirmed: true, lowLevelConfirmed: true, stop.Token);
        await using var session = WasapiAudibleProofSession.CreateNative(route, consent);
        var watcher = WatchStop(stop);
        WasapiAudibleReport result;
        try { result = await session.RunAsync(WasapiNominalSession.MaximumDuration, token); }
        finally
        {
            await stop.CancelAsync();
            await watcher;
        }

        // Somente formatos negociados e números referentes aos buffers confirmados.
        string report = JsonSerializer.Serialize(new { CaptureFormat = route.Capture.Mix, RenderFormat = route.Playback.Mix, Result = result },
            new JsonSerializerOptions { WriteIndented = true });
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/wasapi-audible-proof"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"native-audible-{Guid.NewGuid():N}.json"), report);
        output.WriteLine(report);

        Assert.True(result.Transport.NominalObserved, "O transporte nominal ou o encerramento não foi confirmado.");
        Assert.False(result.Transport.DriftPrecisionAvailable);
        Assert.False(result.Transport.Cleanup.DriftControlEnabled);
        var actual = Assert.IsType<WasapiAudibleOutput>(result.Output);
        Assert.Equal(result.Transport.Cleanup.Playback.FramesReleased, actual.SilentFrames + actual.PcmFrames);
        Assert.InRange(actual.NonZeroFrames, 1, actual.PcmFrames);
        Assert.InRange(actual.PeakGain, float.Epsilon, WasapiAudibleProofSession.ProofGain);
        // O arredondamento PCM16 pode acrescentar até meio passo ao pico quantizado.
        Assert.InRange(actual.PeakAmplitude, float.Epsilon, WasapiAudibleProofSession.ProofGain + .5f / 32768);
    }

    internal static void AssertNoOtherTestApphost()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("WaveStudio.Tests");
        try { Assert.DoesNotContain(processes, process => process.Id != Environment.ProcessId); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static async Task ConfirmStart(CancellationToken token, TimeSpan? confirmationWindow = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(confirmationWindow ?? TimeSpan.FromSeconds(15));
        bool stopVerified = false;
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Enter && stopVerified) return;
                if (key == ConsoleKey.Escape)
                {
                    if (stopVerified) throw new OperationCanceledException("Prova cancelada antes de Start.");
                    stopVerified = true;
                    Console.WriteLine("Parada confirmada. Enter para iniciar ou Esc para cancelar.");
                }
            }
            await Task.Delay(15, deadline.Token);
        }
    }

    private static async Task WatchStop(CancellationTokenSource stop)
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
