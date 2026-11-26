namespace WaveStudio.Audio.Engine;

internal readonly record struct WasapiAcousticMatch(int Frame, double Correlation, double CompetingCorrelation, double MatchedRms);

internal static class WasapiAcousticSignal
{
    public const float Peak = .04f;
    public const int Count = 3;
    private const int Stride = 4;

    public static float[] Create(int sampleRate, int marker)
    {
        var (start, end) = marker switch
        {
            0 => (600d, 3600d),
            1 => (3500d, 850d),
            2 => (750d, 3000d),
            _ => throw new ArgumentOutOfRangeException(nameof(marker))
        };
        var result = new float[checked((int)Math.Round(sampleRate * .018))];
        double duration = (result.Length - 1) / (double)sampleRate;
        for (int i = 1; i < result.Length - 1; i++)
        {
            double time = i / (double)sampleRate;
            double phase = 2 * Math.PI * time * (start + (end - start) * time / (2 * duration));
            double ramp = Math.Min(1, Math.Min(time, duration - time) / .002);
            double envelope = .5 - .5 * Math.Cos(Math.PI * ramp);
            result[i] = (float)(Peak * envelope * Math.Sin(phase));
        }
        return result;
    }

    public static WasapiAcousticMatch? Find(ReadOnlySpan<float> probe, ReadOnlySpan<float> samples,
        int first, int last, int rate)
    {
        if (first < 0 || last < first || last > samples.Length - probe.Length || probe.Length < 2)
            return null;

        // Centra a referência para excluir DC. O stride reduz o custo, mas avalia cada deslocamento.
        Span<double> reference = stackalloc double[(probe.Length + Stride - 1) / Stride];
        double sum = 0;
        for (int i = 0; i < reference.Length; i++)
            sum += reference[i] = probe[i * Stride];
        double average = sum / reference.Length;
        double energy = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            reference[i] -= average;
            energy += reference[i] * reference[i];
        }
        if (energy == 0) return null;
        double matchedRmsDenominator = Math.Sqrt(energy * reference.Length);

        var scores = new double[last - first + 1];
        double best = 0, bestRms = 0;
        int bestFrame = first;
        for (int frame = first; frame <= last; frame++)
        {
            double dot = 0, sumInput = 0, energyInput = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                double sample = samples[frame + i * Stride];
                dot += reference[i] * sample;
                sumInput += sample;
                energyInput += sample * sample;
            }
            double variance = energyInput - sumInput * sumInput / reference.Length;
            double matchedRms = Math.Abs(dot) / matchedRmsDenominator;
            if (variance <= 0 || matchedRms < .00004) continue;
            double score = Math.Min(1, Math.Abs(dot) / Math.Sqrt(energy * variance));
            scores[frame - first] = score;
            if (score <= best) continue;
            best = score;
            bestRms = matchedRms;
            bestFrame = frame;
        }

        double competitor = 0;
        int exclude = rate / 500; // Agrupa os picos vizinhos da mesma resposta, até 2 ms.
        for (int frame = first; frame <= last; frame++)
            if (Math.Abs(frame - bestFrame) > exclude)
                competitor = Math.Max(competitor, scores[frame - first]);
        return best >= .25 && best - competitor >= .06
            ? new(bestFrame, best, competitor, bestRms) : null;
    }
}

internal readonly record struct WasapiAcousticSubmission(long Before100ns, long After100ns);
internal readonly record struct WasapiAcousticPacket(int FirstFrame, int Frames, long Timestamp100ns,
    long LastEmptyBefore100ns, long ReadAfter100ns);
internal readonly record struct WasapiAcousticTimingResult(double FrameTimestampMs, double FrameTimestampLowerMs,
    double FrameTimestampUpperMs, double ReadObservedMs, double AvailabilityLowerMs, double AvailabilityUpperMs);

internal static class WasapiAcousticTiming
{
    public static WasapiAcousticTimingResult? Measure(int frame, int rate, WasapiAcousticSubmission submit, WasapiAcousticPacket packet)
    {
        int relative = frame - packet.FirstFrame;
        if (relative < 0 || relative >= packet.Frames || rate <= 0 || submit.Before100ns > submit.After100ns ||
            packet.LastEmptyBefore100ns > packet.ReadAfter100ns)
            return null;

        double frameDelta = packet.Timestamp100ns - submit.Before100ns + relative * (10_000_000d / rate);
        double readDelta = packet.ReadAfter100ns - submit.Before100ns;
        double submitWidth = submit.After100ns - submit.Before100ns;
        if (frameDelta < submitWidth || frameDelta > readDelta)
            return null;

        // O limite de disponibilidade usa apenas observações do aplicativo no QPC.
        double availableLower = Math.Max(submitWidth, packet.LastEmptyBefore100ns - submit.Before100ns);
        return new((frameDelta - submitWidth / 2) / 10_000, (frameDelta - submitWidth) / 10_000, frameDelta / 10_000,
            (readDelta - submitWidth / 2) / 10_000, (availableLower - submitWidth) / 10_000, readDelta / 10_000);
    }
}
