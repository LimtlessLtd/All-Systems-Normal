window.overseerAudio = (() => {
    let context = null;
    // Owner idea #72: sound and music are on by default. Browsers require a
    // user gesture before an AudioContext can actually run, so these flags
    // record the intent immediately; armAutoStart() below resumes playback
    // silently on the page's first click/keypress without the player having
    // to find and press an "enable sound" button first.
    let enabled = true;
    let musicEnabled = true;
    let musicTimer = null;
    let musicStep = 0;
    let musicBus = null;

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

    const ensureMusicBus = ctx => {
        if (!musicBus) {
            const filter = ctx.createBiquadFilter();
            filter.type = "lowpass";
            filter.frequency.value = 1350;
            filter.Q.value = 0.35;

            const gain = ctx.createGain();
            gain.gain.value = 0.72;
            filter.connect(gain);
            gain.connect(ctx.destination);

            musicBus = { filter, gain };
        }

        return musicBus;
    };

    const padTone = (ctx, frequency, delay, duration, gainAmount, type = "sine") => {
        const bus = ensureMusicBus(ctx);
        const start = ctx.currentTime + delay;
        const oscillator = ctx.createOscillator();
        const gain = ctx.createGain();

        oscillator.type = type;
        oscillator.frequency.setValueAtTime(frequency, start);

        gain.gain.setValueAtTime(0.0001, start);
        gain.gain.exponentialRampToValueAtTime(gainAmount, start + 1.35);
        gain.gain.setValueAtTime(gainAmount, start + Math.max(1.5, duration - 2.1));
        gain.gain.exponentialRampToValueAtTime(0.0001, start + duration);

        oscillator.connect(gain);
        gain.connect(bus.filter);
        oscillator.start(start);
        oscillator.stop(start + duration + 0.05);
    };

    const playAmbientChord = (ctx, frequencies, step) => {
        const duration = 7.8;
        frequencies.forEach((frequency, index) => {
            padTone(
                ctx,
                frequency,
                index * 0.08,
                duration,
                index === 0 ? 0.010 : 0.0065,
                index === 0 ? "sine" : "triangle");
        });

        // A very quiet upper note every other chord keeps the loop musical
        // without turning the station ambience into a foreground soundtrack.
        if (step % 2 === 0) {
            padTone(ctx, frequencies[2] * 2, 1.2, 4.8, 0.0026, "sine");
        }
    };

    const scheduleMusicPhrase = () => {
        if (!musicEnabled) return;

        const ctx = ensureContext();
        if (!ctx || ctx.state !== "running") return;

        const progression = [
            [146.83, 220.00, 293.66], // Dm/A
            [116.54, 174.61, 261.63], // Bb/F
            [130.81, 196.00, 261.63], // F/C
            [130.81, 196.00, 293.66]  // C/G/D suspension
        ];

        const chord = progression[musicStep % progression.length];
        playAmbientChord(ctx, chord, musicStep);
        musicStep = (musicStep + 1) % progression.length;

        musicTimer = window.setTimeout(scheduleMusicPhrase, 6200);
    };

    const stopMusic = () => {
        if (musicTimer !== null) {
            window.clearTimeout(musicTimer);
            musicTimer = null;
        }

        if (musicBus && context) {
            const now = context.currentTime;
            musicBus.gain.gain.cancelScheduledValues(now);
            musicBus.gain.gain.setValueAtTime(
                Math.max(0.0001, musicBus.gain.gain.value),
                now);
            musicBus.gain.gain.exponentialRampToValueAtTime(0.0001, now + 1.0);
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

    const setMusicEnabled = async value => {
        const next = !!value;
        musicEnabled = next;

        if (!next) {
            stopMusic();
            return true;
        }

        const ctx = ensureContext();
        if (!ctx) return false;

        if (ctx.state === "suspended") {
            try {
                await ctx.resume();
            } catch {
                return false;
            }
        }

        if (ctx.state !== "running") return false;

        const bus = ensureMusicBus(ctx);
        const now = ctx.currentTime;
        bus.gain.gain.cancelScheduledValues(now);
        bus.gain.gain.setValueAtTime(
            Math.max(0.0001, bus.gain.gain.value),
            now);
        bus.gain.gain.exponentialRampToValueAtTime(0.72, now + 0.8);

        if (musicTimer === null) {
            scheduleMusicPhrase();
        }

        return true;
    };

    // Sound/music default to on (owner idea #72), but every major browser
    // keeps a fresh AudioContext suspended until a user gesture. Arm a
    // one-time listener for the page's very first click/keypress/touch to
    // silently resume it and, if music is still intended on, start the loop
    // - the same effect as pressing the toggle buttons, without requiring it.
    let autoStartArmed = false;
    const armAutoStart = () => {
        if (autoStartArmed) return;
        autoStartArmed = true;

        const tryAutoStart = async () => {
            const ctx = ensureContext();
            if (!ctx) return;

            if (ctx.state === "suspended") {
                try {
                    await ctx.resume();
                } catch {
                    return;
                }
            }

            if (ctx.state !== "running") return;

            if (musicEnabled && musicTimer === null) {
                scheduleMusicPhrase();
            }

            document.removeEventListener("pointerdown", tryAutoStart);
            document.removeEventListener("keydown", tryAutoStart);
        };

        document.addEventListener("pointerdown", tryAutoStart);
        document.addEventListener("keydown", tryAutoStart);
    };

    armAutoStart();

    return { play, setEnabled, setMusicEnabled };
})();