using System.Diagnostics;
using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class NativeSingleCableProofTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativeBoundedSingleCable")]
    public async Task AuthorizedOperatorRunsBothPhasesWithQuietHandoff()
    {
        var token = TestContext.Current.CancellationToken;
        foreach (var flag in new[] { "AUTHORIZED", "ISOLATION_CONFIRMED", "HEADSET_LOW_CONFIRMED", "DRIVER_CHECKED" })
            Assert.Equal("1", Read(flag));
        Assert.True(Environment.UserInteractive && !Console.IsInputRedirected);
        CheckOtherRunners();
        // O proprietário grava o snapshot antes de qualquer mudança de aplicativo.
        using var file = File.OpenRead(Read("SNAPSHOT_PATH"));
        var saved = JsonSerializer.Deserialize<PrivateSnapshot>(file) ?? throw new InvalidOperationException("Snapshot vazio.");
        using var browser = Process.GetProcessById(saved.Browser.Pid);
        using var obs = Process.GetProcessById(saved.Obs.Pid);
        Assert.Contains(browser.ProcessName.ToLowerInvariant(), new[] { "msedge", "chrome", "firefox" });
        Assert.Equal("obs64", obs.ProcessName, ignoreCase: true);
        var ids = new SingleCableEndpointIds(Read("RENDER"), Read("CAPTURE"), Read("HEADSET"), Read("MIC"));
        var before = new ExternalSelections(Original(saved.Browser, browser, ids.Render),
            Original(saved.Obs, obs, ids.Capture, allowDisabled: true));
        var topology = await NativeExternalRoute.ProbeSingleAsync(ids, 1, token);
        var isolation = await VerifyIsolation(topology, before, token);
        var combined = new ExternalSelections(before.Browser.SelectEndpoint(ids.Render),
            before.Consumer.SelectEndpoint(ids.Capture));
        var reports = new List<PhaseEvidence>(2);
        CableQuietReport? quiet = null;
        ExternalRestoreReport? restoration = null;
        string files = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/f3-single-cable"));
        Directory.CreateDirectory(files);
        string evidence = Path.Combine(files, $"single-cable-{Guid.NewGuid():N}.json");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        await using var batch = new SingleCableAcceptance(topology, before, stop.Token);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = NativeExternalRouteProofTests.WatchTopology(stop, ready);
        Task? producers = null;
        int allowOwnRender = 0;
        long lastProducerCheck = 0;
        try
        {
            await ready.Task.WaitAsync(stop.Token);
            Console.WriteLine("Navegador no cabo, OBS na seleção original. Confira as duas seleções.");
            var browserSettings = await Observe(stop.Token);
            Console.WriteLine("Pressione Esc para verificar a parada e Enter para confirmar a fase do navegador.");
            await WasapiNativeAudibleProofTests.ConfirmStart(stop.Token);
            await RunAsync(ExternalFlow.Browser, batch.BrowserTopology, browserSettings, () => true);
            Assert.Equal(SingleCableState.BrowserRestore, batch.State);
            var levels = reports[0].Result.Levels!.Value;
            Assert.InRange(levels.EffectPeak / levels.BypassPeak, .20f, .30f);

            Console.WriteLine("Pare o tom, restaure o navegador e mantenha o OBS original. Confira as seleções e o isolamento.");
            restoration = await RestoreAsync(stop.Token);
            Assert.True(restoration.Value.Complete, "Restauração do navegador pendente.");
            var quietTopology = await Reprobe(batch.QuietTopology, stop.Token);
            var producerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            producers = WatchProducerAsync();
            await producerReady.Task.WaitAsync(stop.Token);
            Console.WriteLine("Enter confirma captura de quietude por 3 s, sem reprodução. Esc encerra.");
            if (await NativeExternalRouteProofTests.ReadKey(stop.Token, ConsoleKey.Enter, ConsoleKey.Escape) == ConsoleKey.Escape)
                throw new OperationCanceledException();
            using (var quietDone = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                var key = WatchQuietStopAsync(browser, obs, quietDone.Token, stop);
                try
                {
                    quiet = await batch.CheckQuietAsync(quietTopology, before, true, () => isolation.HasNoForeignProducer());
                }
                finally { await quietDone.CancelAsync(); await key; }
            }
            Save();
            Assert.True(quiet.Passed, "Quietude não confirmada: " + quiet.Failure);

            Console.WriteLine("Navegador restaurado. Selecione só a captura do cabo na fonte isolada do OBS e confira.");
            if (before.Consumer.Disabled)
                Console.WriteLine("No OBS, abra Configurações > Áudio. Em Mic/Auxiliar, selecione CABLE Output e aplique.");
            var micSettings = await Observe(stop.Token);
            Console.WriteLine("Enter confirma a fase de microfone. Esc encerra.");
            if (await NativeExternalRouteProofTests.ReadKey(stop.Token, ConsoleKey.Enter, ConsoleKey.Escape) == ConsoleKey.Escape)
                throw new OperationCanceledException();
            await RunAsync(ExternalFlow.Microphone, batch.MicrophoneTopology, micSettings,
                () => isolation.HasNoForeignProducer() && WasapiTime.Now100ns() - Volatile.Read(ref lastProducerCheck) <= 1_000_000);
            Assert.Equal(SingleCableState.FinalRestore, batch.State);

            Task WatchProducerAsync() => Task.Run(async () =>
            {
                try
                {
                    while (!watchStop.IsCancellationRequested)
                    {
                        if (browser.HasExited || obs.HasExited ||
                            !isolation.HasNoForeignProducer(Volatile.Read(ref allowOwnRender) == 1 ? Environment.ProcessId : null, ids.Render))
                        {
                            await stop.CancelAsync();
                            return;
                        }
                        Volatile.Write(ref lastProducerCheck, WasapiTime.Now100ns());
                        producerReady.TrySetResult();
                        await Task.Delay(20, watchStop.Token);
                    }
                }
                catch (OperationCanceledException) when (watchStop.IsCancellationRequested) { }
                catch { await stop.CancelAsync(); }
            });
        }
        finally
        {
            await watchStop.CancelAsync();
            if (producers is not null) await producers;
            Save();
            try
            {
                if (!batch.ResourcesRetained && !token.IsCancellationRequested)
                {
                    restoration = await RestoreAsync(token);
                    Save();
                }
            }
            finally { await stop.CancelAsync(); await watch; }
        }
        Assert.Equal(SingleCableState.Complete, batch.State);
        Assert.True(restoration is { Complete: true });
        Assert.Equal(2, reports.Count);

        async Task RunAsync(ExternalFlow flow, SingleCableTopology expected, ExternalSelections observed, Func<bool> noProducer)
        {
            var current = await Reprobe(expected, stop.Token);
            bool producerClear = await Task.Run(noProducer, stop.Token);
            var session = batch.Begin(flow, current, observed, true, () => producerClear);
            using var phase = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            try
            {
                if (flow == ExternalFlow.Microphone) Volatile.Write(ref allowOwnRender, 1);
                Console.WriteLine(flow == ExternalFlow.Browser ? "Reproduza o tom" : "Confira o medidor do OBS e fale em nível normal");
                Console.WriteLine("B alterna a atenuação. Esc encerra o lote.");
                var keys = NativeExternalRouteProofTests.WatchKeys(session, browser, obs, phase, stop);
                try
                {
                    using var self = Process.GetCurrentProcess();
                    var time = Stopwatch.StartNew();
                    var cpu = self.TotalProcessorTime;
                    long rss = self.WorkingSet64;
                    var result = await session.RunAsync(WasapiExternalRouteSession.MaximumDuration, phase.Token);
                    self.Refresh();
                    reports.Add(new(current.Route(flow).Capture.Mix!, current.Route(flow).Playback.Mix!, result,
                        time.Elapsed.TotalMilliseconds, (self.TotalProcessorTime - cpu).TotalMilliseconds, rss, self.WorkingSet64));
                }
                finally { await phase.CancelAsync(); await keys; }
            }
            finally { await batch.FinishFlowAsync(); Save(); }
        }

        async Task<SingleCableTopology> Reprobe(SingleCableTopology expected, CancellationToken ct)
        {
            Assert.False(browser.HasExited || obs.HasExited);
            CheckOtherRunners();
            var fresh = await NativeExternalRoute.ProbeSingleAsync(ids, expected.Generation, ct);
            Assert.True(fresh == expected, "Topologia ou formato mudou.");
            var current = await VerifyIsolation(fresh, before, ct);
            Assert.Equal(isolation.EndpointIds, current.EndpointIds);
            return fresh;
        }

        Task<ExternalSelections> Observe(CancellationToken ct) =>
            NativeExternalRouteProofTests.ObserveSettings(before, combined, browser, obs, ct);

        async Task<ExternalRestoreReport> RestoreAsync(CancellationToken ct)
        {
            var observed = await Observe(ct);
            var original = await NativeExternalRouteProofTests.OriginalAvailability(before, ct);
            var plan = batch.Restoration(observed, original.Browser, original.Consumer);
            Console.WriteLine($"Navegador: {plan.Browser}. OBS: {plan.Consumer}.");
            if (plan.Browser == ExternalRestoreState.Restore || plan.Consumer == ExternalRestoreState.Restore)
            {
                Console.WriteLine("Restaure somente as seleções marcadas Restore e confira mais uma vez.");
                if (plan.Consumer == ExternalRestoreState.Restore && before.Consumer.Disabled)
                    Console.WriteLine("No OBS, volte a Configurações > Áudio, deixe Mic/Auxiliar em Desativado e aplique.");
                observed = await Observe(ct);
                original = await NativeExternalRouteProofTests.OriginalAvailability(before, ct);
            }
            return batch.FinishRestoration(observed, original.Browser, original.Consumer);
        }

        void Save()
        {
            // Não serializa a topologia nem o snapshot particular.
            var json = JsonSerializer.Serialize(new { State = batch.State.ToString(), batch.ResourcesRetained, Quiet = quiet,
                Trials = reports, Restored = restoration }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(evidence + ".tmp", json);
            File.Move(evidence + ".tmp", evidence, overwrite: true);
        }
    }

    private static string Read(string option)
    {
        var value = Environment.GetEnvironmentVariable("WAVESTUDIO_SINGLE_CABLE_" + option);
        Assert.False(string.IsNullOrWhiteSpace(value), "Pré-condição ausente: " + option);
        return value;
    }

    private static void CheckOtherRunners()
    {
        WasapiNativeSilentTests.AssertNoPreviewProcess();
        WasapiNativeAudibleProofTests.AssertNoOtherTestApphost();
    }

    private static ExternalSelection Original(PrivateSelection saved, Process process, string temporary, bool allowDisabled = false)
    {
        Assert.Equal(saved.StartUtcTicks, process.StartTime.ToUniversalTime().Ticks);
        Assert.Equal(temporary, saved.Temporary);
        Assert.False(string.IsNullOrWhiteSpace(saved.Context) || string.IsNullOrWhiteSpace(saved.Before));
        return ParseOriginalSelection(NativeExternalRouteProofTests.ProcessIdentity(process, saved.Context), saved.Before, allowDisabled);
    }

    internal static ExternalSelection ParseOriginalSelection(string clientId, string value, bool allowDisabled) => value switch
    {
        "DEFAULT" => new(clientId, null, true, true),
        "DISABLED" when allowDisabled => new(clientId, null, true, true, Disabled: true),
        "DISABLED" => throw new InvalidOperationException("A seleção desativada só é válida para a fonte do OBS."),
        _ => new(clientId, value, true, true)
    };

    private static async Task<NativeCableIsolation> VerifyIsolation(SingleCableTopology topology, ExternalSelections original, CancellationToken token)
    {
        var result = await Task.Run(() => NativeCableIsolation.Verify(topology), token);
        Assert.DoesNotContain(result.EndpointIds, id => id == original.Browser.EndpointId || id == original.Consumer.EndpointId);
        var current = await NativeExternalRouteProofTests.OriginalAvailability(original, token);
        Assert.True(current.Browser && current.Consumer);
        return result;
    }

    private static async Task WatchQuietStopAsync(Process browser, Process obs, CancellationToken token, CancellationTokenSource stop)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (browser.HasExited || obs.HasExited ||
                    (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape))
                {
                    await stop.CancelAsync();
                    return;
                }
                await Task.Delay(15, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException) { await stop.CancelAsync(); }
    }

    private sealed record PrivateSelection(int Pid, long StartUtcTicks, string Context, string Before, string Temporary);
    private sealed record PrivateSnapshot(PrivateSelection Browser, PrivateSelection Obs);
    private sealed record PhaseEvidence(WasapiPcm CaptureFormat, WasapiPcm RenderFormat, ExternalRouteReport Result,
        double WallMilliseconds, double CpuMilliseconds, long WorkingSetBefore, long WorkingSetAfter);
}
