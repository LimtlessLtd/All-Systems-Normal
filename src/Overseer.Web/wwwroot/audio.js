window.overseerAudio = (() => {
    let context = null;
    let enabled = false;

    const ensureContext = () => {
        if (!context) {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) return null;
            context = new AudioContextType();
        }
        return context;
    };

    const hash = value => {
        let result = 2166136261;
        const text = value || "";
        for (let i = 0; i < text.length; i++) {
            result ^= text.charCodeAt(i);
            result = Math.imul(result, 16777619);
        }
        return Math.abs(result >>> 0);
    };

    const tone = (ctx, frequency, delay, duration, gain, type = "sine", endFrequency = null) => {
        const now = ctx.currentTime + delay;
        const oscillator = ctx.createOscillator();
        const volume = ctx.createGain();

        oscillator.type = type;
        oscillator.frequency.setValueAtTime(frequency, now);
        if (endFrequency !== null) {
            oscillator.frequency.exponentialRampToValueAtTime(
                Math.max(40, endFrequency),
                now + duration);
        }

        volume.gain.setValueAtTime(0.0001, now);
        volume.gain.exponentialRampToValueAtTime(gain, now + 0.012);
        volume.gain.exponentialRampToValueAtTime(0.0001, now + duration);

        oscillator.connect(volume);
        volume.connect(ctx.destination);
        oscillator.start(now);
        oscillator.stop(now + duration + 0.02);
    };

    const noise = (ctx, delay, duration, gain) => {
        const length = Math.max(1, Math.floor(ctx.sampleRate * duration));
        const buffer = ctx.createBuffer(1, length, ctx.sampleRate);
        const data = buffer.getChannelData(0);
        for (let i = 0; i < length; i++) {
            data[i] = (Math.random() * 2 - 1) * (1 - i / length);
        }

        const source = ctx.createBufferSource();
        const volume = ctx.createGain();
        const filter = ctx.createBiquadFilter();
        filter.type = "bandpass";
        filter.frequency.value = 1100;

        const now = ctx.currentTime + delay;
        volume.gain.setValueAtTime(gain, now);
        volume.gain.exponentialRampToValueAtTime(0.0001, now + duration);

        source.buffer = buffer;
        source.connect(filter);
        filter.connect(volume);
        volume.connect(ctx.destination);
        source.start(now);
    };

    const play = (kind, sourceId) => {
        if (!enabled) return;
        const ctx = ensureContext();
        if (!ctx || ctx.state !== "running") return;

        const variant = hash(sourceId) % 7;

        switch ((kind || "").toLowerCase()) {
            case "speech": {
                const base = 440 + variant * 26;
                tone(ctx, base, 0, .055, .035, "triangle", base * 1.08);
                tone(ctx, base * .92, .09, .05, .027, "triangle", base * 1.03);
                break;
            }
            case "thought":
                tone(ctx, 520 + variant * 18, 0, .08, .018, "sine", 610 + variant * 12);
                break;
            case "suspicion":
                tone(ctx, 360, 0, .11, .045, "triangle", 520);
                tone(ctx, 520, .14, .13, .04, "triangle", 690);
                break;
            case "warning":
                tone(ctx, 690, 0, .09, .05, "square", 610);
                tone(ctx, 610, .13, .09, .045, "square", 540);
                break;
            case "hostile":
                tone(ctx, 240, 0, .16, .065, "sawtooth", 180);
                tone(ctx, 240, .22, .16, .065, "sawtooth", 180);
                tone(ctx, 240, .44, .16, .065, "sawtooth", 180);
                break;
            case "critical":
                tone(ctx, 170, 0, .28, .075, "sawtooth", 95);
                noise(ctx, .03, .19, .025);
                break;
            case "failure":
                tone(ctx, 220, 0, .5, .08, "sawtooth", 82);
                tone(ctx, 164, .23, .58, .07, "square", 65);
                break;
            case "important":
                tone(ctx, 440, 0, .12, .045, "sine", 660);
                tone(ctx, 660, .14, .16, .045, "sine", 880);
                break;
            case "system":
            default:
                tone(ctx, 760, 0, .045, .02, "triangle", 900);
                break;
        }
    };

    const setEnabled = async value => {
        enabled = !!value;
        if (!enabled) return true;

        const ctx = ensureContext();
        if (!ctx) return false;

        if (ctx.state === "suspended") {
            try {
                await ctx.resume();
            } catch {
                return false;
            }
        }

        if (ctx.state === "running") {
            tone(ctx, 520, 0, .06, .024, "sine", 660);
            tone(ctx, 660, .08, .08, .022, "sine", 780);
            return true;
        }

        return false;
    };

    return { play, setEnabled };
})();