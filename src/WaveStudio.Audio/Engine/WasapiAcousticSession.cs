using System.Runtime.InteropServices;

namespace WaveStudio.Audio.Engine;

internal sealed record WasapiAcousticTrial(int Marker, int CaptureChannel, int QueuedRenderFrames, double Correlation,
    double CompetingCorrelation, double MatchedRms, WasapiAcousticTimingResult Timing);

internal sealed record WasapiAcousticReport(bool Completed, int Submitted, int Packets, int CapturedFrames,
    bool CaptureStopConfirmed, bool RenderStopConfirmed, bool ResourcesReleased, string? Failure, int? HResult,
    IReadOnlyList<WasapiAcousticTrial> Trials)
{
    public bool Measured => Completed && Failure is null && CaptureStopConfirmed && RenderStopConfirmed && ResourcesReleased &&
        Trials.Count == WasapiAcousticSignal.Count &&
        Trials.Max(t => t.Timing.FrameTimestampMs) - Trials.Min(t => t.Timing.FrameTimestampMs) <= 25;
}

internal static class WasapiAcousticSession
{
    private const long Startup100ns = 1_500_000;
    // Os deslocamentos diferentes dentro de um período de 10 ms amostram fases distintas do motor.
    private static readonly long[] MarkerOffsets100ns = [4_500_000, 11_570_000, 18_760_000];
    private const long Search100ns = 4_000_000;
    private const long Duration100ns = 24_000_000;
    private const uint Silent = 2;
    private const int BufferEmpty = 0x08890001; // AUDCLNT_S_BUFFER_EMPTY

    public static async Task<WasapiAcousticReport> RunAsync(TransportRoute route, WasapiAudibleConsent consent,
        CancellationToken token)
    {
        route.Validate();
        if (route.Capture.Evidence != FormatEvidence.ExactCapability || route.Playback.Evidence != FormatEvidence.ExactCapability)
            throw new InvalidOperationException("A prova exige endpoints nativos com formato exato.");
        token.ThrowIfCancellationRequested();
        consent.Consume(route);
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, consent.StopToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var completed = new TaskCompletionSource<WasapiAcousticReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Thread(() =>
        {
            bool park = false;
            try
            {
                using (deadline)
                    completed.TrySetResult(Run(route, deadline.Token, out park));
            }
            catch (Exception error) { completed.TrySetException(error); }
            // Preserva a propriedade do COM retido e o apartamento inicializado, sem voltar a tocar nos streams.
            if (park) Thread.Sleep(Timeout.Infinite);
        }) { IsBackground = true, Name = "WaveStudio WASAPI acoustic owner" };
        try
        {
            owner.SetApartmentState(ApartmentState.MTA);
            owner.Start();
        }
        catch
        {
            deadline.Dispose();
            throw;
        }
        try { return await completed.Task.WaitAsync(TimeSpan.FromSeconds(7)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // Chamadas síncronas do driver não são interrompíveis. O owner preserva o COM e o guard.
            return new(false, 0, 0, 0, false, false, false, "OwnerQuiescenceTimeout", null, []);
        }
    }

    private static WasapiAcousticReport Run(TransportRoute route, CancellationToken token, out bool park)
    {
        park = false;
        if (token.IsCancellationRequested)
            return new(false, 0, 0, 0, false, false, true, "Canceled", null, []);
        IWasapiStreamClient? capture = null, render = null;
        bool captureStart = false, renderStart = false, captureStop = false, renderStop = false;
        bool captureInFlight = false, renderInFlight = false, completed = false;
        string? failure = null;
        int? hr = null;
        int sent = 0;
        var submitted = new WasapiAcousticSubmission[WasapiAcousticSignal.Count];
        var queue = new int[submitted.Length];
        var trials = new List<WasapiAcousticTrial>(submitted.Length);
        var input = route.Capture.Mix!;
        var output = route.Playback.Mix!;
        using var captured = new CaptureTimeline(input);
        var resources = new WasapiResourceScope(TransportActivityGuard.Shared.TryAcquire(AudioActivity.IsolatedTransport)
            ?? throw new InvalidOperationException("Outra operação de áudio está ativa."));
        try
        {
            var stimuli = Enumerable.Range(0, submitted.Length).Select(i => WasapiAcousticSignal.Create(output.SampleRate, i)).ToArray();
            capture = Open(route.Capture, resources, token, out uint captureSize);
            render = Open(route.Playback, resources, token, out uint renderSize);
            if (renderSize < stimuli[0].Length)
                throw new InvalidOperationException("MarkerExceedsRenderCapacity");
            var read = resources.Add(capture.OpenCapture());
            var write = resources.Add(render.OpenRender());
            token.ThrowIfCancellationRequested();
            _ = write.GetBuffer(renderSize);
            renderInFlight = true;
            WasapiCallException.RequireSuccess(write.ReleaseBuffer(renderSize, Silent), "Render.ReleaseBuffer/Prime");
            renderInFlight = false;

            token.ThrowIfCancellationRequested();
            captureStart = true;
            WasapiCallException.RequireSuccess(capture.Start(), "Capture.Start");
            token.ThrowIfCancellationRequested();
            renderStart = true;
            WasapiCallException.RequireSuccess(render.Start(), "Render.Start");

            long origin = WasapiTime.Now100ns();
            long lastEmpty = origin;
            while (WasapiTime.Now100ns() - origin < Duration100ns)
            {
                token.ThrowIfCancellationRequested();
                for (int i = 0; i < 16; i++)
                {
                    long before = WasapiTime.Now100ns();
                    uint next = read.NextPacketSize();
                    if (next == 0)
                    {
                        lastEmpty = before;
                        break;
                    }
                    if (next > captureSize) throw new InvalidOperationException("CapturePacketTooLarge");
                    var packet = read.GetBuffer();
                    long after = WasapiTime.Now100ns();
                    if (packet.HResult == BufferEmpty)
                    {
                        if (packet.Frames != 0) throw new InvalidOperationException("CaptureEmptyBufferHasFrames");
                        lastEmpty = before;
                        break;
                    }
                    WasapiCallException.RequireSuccess(packet.HResult, "Capture.GetBuffer");
                    captureInFlight = true;
                    uint accepted = 0;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (packet.Frames != next) throw new InvalidOperationException("CapturePacketSizeChanged");
                        if (after - origin >= Startup100ns) captured.Add(packet, lastEmpty, after);
                        accepted = packet.Frames;
                    }
                    finally
                    {
                        WasapiCallException.RequireSuccess(read.ReleaseBuffer(accepted), "Capture.ReleaseBuffer");
                        captureInFlight = false;
                    }
                }

                if (sent < submitted.Length && WasapiTime.Now100ns() - origin >= MarkerOffsets100ns[sent])
                {
                    uint padding = render.ReadPadding();
                    if (padding > renderSize) throw new InvalidOperationException("InvalidRenderPadding");
                    var marker = stimuli[sent];
                    if (renderSize - padding >= marker.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        nint data = write.GetBuffer((uint)marker.Length);
                        renderInFlight = true;
                        bool written = false;
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            EncodeMarker(marker, output, data);
                            token.ThrowIfCancellationRequested();
                            written = true;
                        }
                        finally
                        {
                            long before = WasapiTime.Now100ns();
                            written &= !token.IsCancellationRequested;
                            WasapiCallException.RequireSuccess(write.ReleaseBuffer(written ? (uint)marker.Length : 0, 0), "Render.ReleaseBuffer/Marker");
                            long after = WasapiTime.Now100ns();
                            renderInFlight = false;
                            if (written)
                            {
                                submitted[sent] = new(before, after);
                                queue[sent++] = (int)padding;
                            }
                        }
                    }
                }
                token.WaitHandle.WaitOne(1);
            }
            token.ThrowIfCancellationRequested();
            completed = sent == submitted.Length;
            if (!completed) failure = "MarkersNotSubmitted";
        }
        catch (OperationCanceledException) { failure = "Canceled"; }
        catch (WasapiCallException error) { failure = error.Stage; hr = error.Result; }
        catch (InvalidOperationException error) { failure = error.Message; }
        catch (COMException error) { failure = nameof(COMException); hr = error.HResult; }
        catch (NotSupportedException) { failure = "UnsupportedFormat"; }
        finally
        {
            renderStop = !renderStart || TryStop(render);
            captureStop = !captureStart || TryStop(capture);
            try
            {
                if (captureInFlight || renderInFlight || !captureStop || !renderStop)
                {
                    failure ??= captureInFlight || renderInFlight ? "BufferReleaseUnconfirmed" : "StopUnconfirmed";
                    WasapiResourceScope.Retain(resources);
                }
                else
                    resources.Dispose();
            }
            finally { park = !resources.Released; }
        }

        // O processamento do PCM acontece somente em memória. O relatório contém apenas métricas e estágios.
        if (token.IsCancellationRequested) failure ??= "Canceled";
        if (completed && resources.Released && failure is null)
        {
            for (int i = 0; i < sent && !token.IsCancellationRequested; i++)
            {
                var reference = WasapiAcousticSignal.Create(input.SampleRate, i);
                var trial = captured.Find(i, reference, submitted[i], queue[i]);
                if (trial is not null) trials.Add(trial);
            }
            if (token.IsCancellationRequested) failure = "Canceled";
            else if (trials.Count != sent) failure = "MarkerAbsentOrAmbiguous";
            else if (trials.Max(t => t.Timing.FrameTimestampMs) - trials.Min(t => t.Timing.FrameTimestampMs) > 25)
                failure = "MarkerLatencyNotRepeatable";
        }
        return new(completed, sent, captured.PacketCount, captured.FrameCount, captureStop, renderStop, resources.Released,
            failure ?? resources.CleanupError, hr, trials.AsReadOnly());
    }

    private static IWasapiStreamClient Open(WasapiEndpointInfo info, WasapiResourceScope resources,
        CancellationToken token, out uint capacity)
    {
        token.ThrowIfCancellationRequested();
        var device = resources.Add(NativeWasapiPreparationDevice.OpenNonPersistent(info.Endpoint));
        var client = resources.Add((IWasapiStreamClient)device.Activate());
        if (client.ReadMixFormat() != info.Mix) throw new InvalidOperationException("MixFormatChanged");
        client.RequireExactSupport();
        token.ThrowIfCancellationRequested();
        client.Initialize();
        (capacity, _) = client.ReadBufferInfo();
        if (capacity is 0 or > 4096) throw new InvalidOperationException("BufferCapacityOutOfBounds");
        client.RevalidateFormat(info.Mix!);
        device.ValidateIdentity(info.Endpoint);
        return client;
    }

    private static bool TryStop(IWasapiStreamClient? client)
    {
        try { return client?.Stop() is 0 or 1; }
        catch (Exception) { return false; }
    }

    private static unsafe void EncodeMarker(ReadOnlySpan<float> marker, WasapiPcm format, nint address)
    {
        if (address == 0) throw new InvalidOperationException("RenderBufferMissing");
        if (format.ContainerBits == 32)
        {
            var samples = new Span<float>((void*)address, marker.Length * format.Channels);
            for (int i = 0; i < marker.Length; i++)
                samples.Slice(i * format.Channels, format.Channels).Fill(marker[i]);
        }
        else
        {
            var samples = new Span<short>((void*)address, marker.Length * format.Channels);
            int limit = (int)(WasapiAcousticSignal.Peak * 32768);
            for (int i = 0; i < marker.Length; i++)
            {
                int value = Math.Clamp((int)MathF.Round(marker[i] * 32768), -limit, limit);
                samples.Slice(i * format.Channels, format.Channels).Fill((short)value);
            }
        }
    }

    private sealed class CaptureTimeline(WasapiPcm format) : IDisposable
    {
        private readonly float[][] pcm = Enumerable.Range(0, format.Channels).Select(_ => new float[3 * format.SampleRate]).ToArray();
        private readonly List<WasapiAcousticPacket> packets = new(1024);
        private ulong expectedPosition;
        public int FrameCount { get; private set; }
        public int PacketCount => packets.Count;

        public unsafe void Add(WasapiCaptureBuffer packet, long lastEmpty, long after)
        {
            if ((packet.Flags & ~Silent) != 0 || packet.Qpc100ns == 0 || packet.Qpc100ns > (ulong)after ||
                after - (long)packet.Qpc100ns > 1_000_000 || packet.Position > ulong.MaxValue - packet.Frames)
                throw new InvalidOperationException("InvalidCaptureTimestampOrFlags");
            int frames = (int)packet.Frames;
            if (frames > pcm[0].Length - FrameCount || packets.Count == 1024)
                throw new InvalidOperationException("CaptureCapacityExceeded");
            long timestamp = (long)packet.Qpc100ns;
            if (packets.Count != 0)
            {
                var previous = packets[^1];
                double framePeriod = previous.Frames * (10_000_000d / format.SampleRate);
                long advance = timestamp - previous.Timestamp100ns;
                if (packet.Position != expectedPosition || advance <= 0 || Math.Abs(advance - framePeriod) > 30_000)
                    throw new InvalidOperationException("CaptureDiscontinuity");
            }

            if ((packet.Flags & Silent) == 0)
            {
                if (packet.Data == 0) throw new InvalidOperationException("CaptureBufferMissing");
                for (int channel = 0; channel < format.Channels; channel++)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        int sample = i * format.Channels + channel;
                        float value = format.ContainerBits == 32 ? ((float*)packet.Data)[sample] :
                            ((short*)packet.Data)[sample] / 32768f;
                        if (!float.IsFinite(value) || MathF.Abs(value) >= .999f)
                            throw new InvalidOperationException("CaptureClippedOrNonFinite");
                        pcm[channel][FrameCount + i] = value;
                    }
                }
            }
            packets.Add(new(FrameCount, frames, timestamp, lastEmpty, after));
            expectedPosition = packet.Position + packet.Frames;
            FrameCount += frames;
        }

        public WasapiAcousticTrial? Find(int marker, float[] reference, WasapiAcousticSubmission submit, int queued)
        {
            double ticksPerFrame = 10_000_000d / format.SampleRate;
            int first = packets.FindIndex(p => p.Timestamp100ns + (p.Frames - 1) * ticksPerFrame >= submit.After100ns);
            long searchEnd = submit.After100ns + Search100ns;
            int last = packets.FindLastIndex(p => p.Timestamp100ns <= searchEnd);
            if (first < 0 || last < first) return null;
            var startPacket = packets[first];
            var finalPacket = packets[last];
            int firstFrame = startPacket.FirstFrame + Math.Max(0,
                (int)Math.Ceiling((submit.After100ns - startPacket.Timestamp100ns) / ticksPerFrame));
            int lastFrame = finalPacket.FirstFrame + Math.Min(finalPacket.Frames - 1,
                (int)Math.Floor((searchEnd - finalPacket.Timestamp100ns) / ticksPerFrame));
            WasapiAcousticTrial? best = null;
            for (int channel = 0; channel < pcm.Length; channel++)
            {
                var match = WasapiAcousticSignal.Find(reference, pcm[channel].AsSpan(0, FrameCount), firstFrame,
                    Math.Min(FrameCount - reference.Length, lastFrame), format.SampleRate);
                if (match is not { } signal || best is not null && signal.Correlation <= best.Correlation) continue;
                var packet = packets.First(p => signal.Frame < p.FirstFrame + p.Frames);
                var measured = WasapiAcousticTiming.Measure(signal.Frame, format.SampleRate, submit, packet);
                if (measured is not { FrameTimestampMs: <= 400 } timing) continue;
                best = new(marker + 1, channel + 1, queued, signal.Correlation, signal.CompetingCorrelation,
                    signal.MatchedRms, timing);
            }
            return best;
        }

        public void Dispose()
        {
            foreach (float[] channel in pcm) Array.Clear(channel);
        }
    }
}
