// AIAvatar.razor.js - Thread-Safe Stable Production Parallel Audio Core
var dotNetReference = typeof dotNetReference !== 'undefined' ? dotNetReference : null;
var globalAudioCtx = typeof globalAudioCtx !== 'undefined' ? globalAudioCtx : null;
var nextPlayTime = typeof nextPlayTime !== 'undefined' ? nextPlayTime : 0;
var lastBufferEndSamples = typeof lastBufferEndSamples !== 'undefined' ? lastBufferEndSamples : null;

var jsActiveTargetUrl = typeof jsActiveTargetUrl !== 'undefined' ? jsActiveTargetUrl : "";
var jsTextAccumulator = typeof jsTextAccumulator !== 'undefined' ? jsTextAccumulator : "";
var isJsFetchWorkerRunning = typeof isJsFetchWorkerRunning !== 'undefined' ? isJsFetchWorkerRunning : false;
var isJsPlaybackWorkerRunning = typeof isJsPlaybackWorkerRunning !== 'undefined' ? isJsPlaybackWorkerRunning : false;

var pendingSentencesToFetchQueue = typeof pendingSentencesToFetchQueue !== 'undefined' ? pendingSentencesToFetchQueue : [];
var downloadedAudioPayloadBufferQueue = typeof downloadedAudioPayloadBufferQueue !== 'undefined' ? downloadedAudioPayloadBufferQueue : [];

window.Blazor = window.Blazor || {};
window.Blazor.registerChatDialogRef = function (dotNetRef) {
    window.chatDialogRef = dotNetRef;
};

export function initializeNeuralTts(dotNetRef) {
    dotNetReference = dotNetRef;
    console.log("[JS-MODULE] Studio Parallel Core Connected Safely.");

    window.accumulateAndStreamVoiceTokens = accumulateAndStreamVoiceTokens;
    window.initializeVoiceStreamSession = initializeVoiceStreamSession;
    window.finalizeVoiceStreamSession = finalizeVoiceStreamSession;

    if (dotNetReference) {
        dotNetReference.invokeMethodAsync('OnEngineReady');
    }
}

// Warm up the target URL context right when the LLM begins streaming tokens
export function initializeVoiceStreamSession(targetUrl) {
    jsActiveTargetUrl = targetUrl;
    window.isWaitingForAudioHandshake = true;
    jsTextAccumulator = "";
    pendingSentencesToFetchQueue = [];
    downloadedAudioPayloadBufferQueue = [];
    isJsFetchWorkerRunning = false;
    isJsPlaybackWorkerRunning = false;

    if (!globalAudioCtx) {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        globalAudioCtx = new AudioContext({ sampleRate: 24000 });
        nextPlayTime = globalAudioCtx.currentTime;
    }
}

export function accumulateAndStreamVoiceTokens(textChunk) {
    jsTextAccumulator += textChunk;

    // 1. Repair word-smashing boundary anomalies safely
    jsTextAccumulator = jsTextAccumulator.replace(/\b([A-Za-z]+)([0-9]+)\b/g, "$1 $2");

    // ==============================================================================
    // 2. UNIFIED STALL VALVE
    // Stall the matching engine if the text ends with "C# ." so we wait for "NET"
    // ==============================================================================
    if (/C#\s*\.\s*$/i.test(jsTextAccumulator) || /C#\s*\.\s*\s+$/i.test(jsTextAccumulator)) {
        return;
    }

    // ==============================================================================
    // 3. SECURE PUNCTUATION BOUNDARY ENGINE
    // Matches the FIRST valid sentence block from the start of the buffer (^)
    // ==============================================================================
    let match = jsTextAccumulator.match(/^[^.!?]+[.!?](?!(?:\s*NET)\b)(?=\s+|\s*$)/i);

    if (match) {
        // Extract the raw text chunk exactly as matched by the regex pattern
        let completedSentence = match[0];

        // Advance the master buffer tracker safely past the precise match slice point
        jsTextAccumulator = jsTextAccumulator.substring(match.index + completedSentence.length);

        // Clean up the text sentence string natively
        completedSentence = completedSentence.trim();

        // ==============================================================================
        // 4. FRAMEWORK LAYOUT RECOVERY
        // Forces a clean formatting space between language and framework before queueing
        // ==============================================================================
        completedSentence = completedSentence.replace(/\bC#\s*\.?NET\b/gi, "C# .NET");
        completedSentence = completedSentence.replace(/\bC\s*\+\s*\+/g, "C++");

        if (completedSentence.length > 3) {
            console.log("[JS-TEXT-LOOP] Intercepted Sentence:", completedSentence);
            pendingSentencesToFetchQueue.push(completedSentence);

            if (!isJsFetchWorkerRunning) {
                isJsFetchWorkerRunning = true;
                processBackgroundFetchLoop();
            }
        }
    }
}

// Flush whatever trailing sentence fragment is left when the LLM closes
export function finalizeVoiceStreamSession() {
    // 3. APPLY MATCHING CORRECTIONS TO THE FLUSH VALVE BEFORE TRIMMING
    // This catches instances where text was split across boundaries during streaming
    let finalSentence = jsTextAccumulator;

    finalSentence = finalSentence.replace(/\bC\s*#\s*\.\s*NET/gi, "C# .NET");
    finalSentence = finalSentence.replace(/\bC\s*\+\s*\+/g, "C++");

    finalSentence = finalSentence.trim();

    if (finalSentence.length > 0) {
        console.log("[JS-TEXT-LOOP] Final Sentence Flush:", finalSentence);
        pendingSentencesToFetchQueue.push(finalSentence);
        jsTextAccumulator = "";

        if (!isJsFetchWorkerRunning) {
            isJsFetchWorkerRunning = true;
            processBackgroundFetchLoop();
        }
    } else {
        checkSystemIdleState();
    }
}

// ==============================================================================
// PIPELINE 1: BACKGROUND NETWORK FETCH WORKER (Runs at max hardware speed)
// ==============================================================================
async function processBackgroundFetchLoop() {
    if (pendingSentencesToFetchQueue.length === 0) {
        isJsFetchWorkerRunning = false;
        checkSystemIdleState();
        return;
    }

    let textToGenerate = pendingSentencesToFetchQueue.shift();
    console.log("[GPU-FETCHER] Sending segment to Python endpoint instantly:", textToGenerate);

    try {
        const response = await fetch(jsActiveTargetUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: textToGenerate })
        });

        if (!response.ok) throw new Error("FastAPI generation request rejected.");

        const reader = response.body.getReader();
        let phraseBuffers = [];

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const int16Array = new Int16Array(value.buffer, value.byteOffset, value.byteLength / 2);
            if (int16Array.length === 0) continue;

            const float32Array = new Float32Array(int16Array.length);
            for (let i = 0; i < int16Array.length; i++) {
                float32Array[i] = int16Array[i] / 32768.0;
            }
            phraseBuffers.push(float32Array);
        }

        if (phraseBuffers.length > 0) {
            let totalLength = phraseBuffers.reduce((acc, b) => acc + b.length, 0);
            let mergedFloats = new Float32Array(totalLength);
            let offset = 0;
            for (let buffer of phraseBuffers) {
                mergedFloats.set(buffer, offset);
                offset += buffer.length;
            }

            downloadedAudioPayloadBufferQueue.push({ audioData: mergedFloats, text: textToGenerate });
            console.log("[GPU-FETCHER] Generation complete, cached in memory. Queue size:", downloadedAudioPayloadBufferQueue.length);

            if (!isJsPlaybackWorkerRunning) {
                isJsPlaybackWorkerRunning = true;
                processHardwarePlaybackLoop();
            }
        }
    } catch (err) {
        console.error("[GPU-FETCHER-FAULT]: Recovery triggered.", err);
        isJsFetchWorkerRunning = false;
        checkSystemIdleState();
    }

    setTimeout(processBackgroundFetchLoop, 0);
}

// ==============================================================================
// PIPELINE 2: HARDWARE PLAYBACK SCHEDULER (Runs smoothly on the sound card clock)
// ==============================================================================
function processHardwarePlaybackLoop() {
    if (downloadedAudioPayloadBufferQueue.length === 0) {
        isJsPlaybackWorkerRunning = false;
        checkSystemIdleState(); // Calls OnSpeechFinished inside Blazor thread-safely
        return;
    }

    try {
        let payload = downloadedAudioPayloadBufferQueue.shift();
        let mergedFloats = payload.audioData;
        let sentenceText = payload.text;

        if (dotNetReference && window.isWaitingForAudioHandshake) {
            window.isWaitingForAudioHandshake = false;
            let physicalStartDelayMs = Math.max(0, (nextPlayTime - globalAudioCtx.currentTime) * 1000);
            setTimeout(() => {
                console.log("[JS-HARDWARE] Audio waves are physically playing now. Releasing Blazor bubbles.");
                if (window.chatDialogRef) {
                    window.chatDialogRef.invokeMethodAsync('OnPhysicalAudioStarted');
                }
            }, physicalStartDelayMs);
        }

        if (nextPlayTime < globalAudioCtx.currentTime) {
            nextPlayTime = globalAudioCtx.currentTime + 0.01;
        }

        if (lastBufferEndSamples && mergedFloats.length > 4) {
            for (let i = 0; i < 4; i++) {
                mergedFloats[i] = (mergedFloats[i] + lastBufferEndSamples[i]) / 2.0;
            }
        }
        lastBufferEndSamples = mergedFloats.slice(-4);

        const audioBuffer = globalAudioCtx.createBuffer(1, mergedFloats.length, 24000);
        audioBuffer.getChannelData(0).set(mergedFloats);

        const source = globalAudioCtx.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(globalAudioCtx.destination);

        source.start(nextPlayTime);

        if (dotNetReference && sentenceText) {
            // Strip out markdown link tags before splitting into individual words for the UI valve
            let sanitizedText = sentenceText.replace(/[\[\]]/g, "").replace(/\([^)]*\)/g, "");

            // 1. Repair broken framework spaces before splitting into word tokens
            sanitizedText = sanitizedText.replace(/\bC\s*#\s*\.?\s*NET\b/gi, "C# .NET");
            sanitizedText = sanitizedText.replace(/\bC\s*\+\+\b/gi, "C++");
            sanitizedText = sanitizedText.replace(/\bCI\s*\/\s*CD\b/gi, "CI/CD");

            // 2. Now safely split the repaired text into individual words
            const words = sanitizedText.trim().split(/(?<!\b(?:C#|F#|C\+\+|CI))\s+(?!(?:\.?NET|CD)\b)/gi);

            if (words.length > 0) {
                const wordDisplayInterval = (audioBuffer.duration / words.length) * 1000;
                words.forEach((word, index) => {
                    setTimeout(() => {
                        dotNetReference.invokeMethodAsync('OnWordAudioTriggered', word);
                    }, (nextPlayTime - globalAudioCtx.currentTime) * 1000 + (index * wordDisplayInterval));
                });
            }
        }

        let duration = audioBuffer.duration;
        nextPlayTime += duration;

        setTimeout(processHardwarePlaybackLoop, duration * 1000 - 15);

    } catch (playbackError) {
        console.error("[PLAYBACK-CRITICAL-FAULT]: Bypassing to preserve UI state locks.", playbackError);
        // SAFETY FALLBACK: If an audio array handles improperly, release the worker states
        isJsPlaybackWorkerRunning = false;
        checkSystemIdleState();
    }
}

// ==============================================================================
// PIPELINE 3: RECOVERY SYSTEM CONTROL VALVE MONITOR
// ==============================================================================
function checkSystemIdleState() {
    if (!isJsFetchWorkerRunning &&
        !isJsPlaybackWorkerRunning &&
        pendingSentencesToFetchQueue.length === 0 &&
        downloadedAudioPayloadBufferQueue.length === 0) {

        console.log("[JS-SYSTEM] Audio pipelines empty and idle. Re-enabling Blazor UI input nodes.");

        if (dotNetReference) {
            dotNetReference.invokeMethodAsync('OnSpeechFinished');
        }
    }
}

export function resetAudioEngineState() {
    console.log("[JS-SYSTEM] Purging dirty pipeline buffers for new session...");

    // 1. Wipe text accumulators completely
    jsTextAccumulator = "";
    pendingSentencesToFetchQueue = [];
    isJsFetchWorkerRunning = false;

    // 2. Reset hardware timeline clocks to the current audio context timeline space
    if (globalAudioCtx) {
        nextPlayTime = globalAudioCtx.currentTime + 0.1;
    } else {
        nextPlayTime = 0;
    }
}

export function forceStopAndResetAudioContext() {
    console.log("[JS-AUDIO-SYSTEM] Component disposed. Purging active timeline audio tracks...");

    // 1. Clear out all pending sentence and audio array queues immediately
    jsTextAccumulator = "";
    pendingSentencesToFetchQueue = [];
    downloadedAudioPayloadBufferQueue = [];
    isJsFetchWorkerRunning = false;
    isJsPlaybackWorkerRunning = false;

    // 2. Shut down the browser's hardware sound context channel completely
    if (globalAudioCtx) {
        try {
            // Closes the sound channel and frees up your device's audio hardware
            globalAudioCtx.close();
        } catch (e) {
            console.error("Error closing audio context:", e);
        }
        globalAudioCtx = null;
        nextPlayTime = 0;
        lastBufferEndSamples = null;
    }
}

window.forceStopAndResetAudioContext = forceStopAndResetAudioContext;
window.resetAudioEngineState = resetAudioEngineState;
window.Blazor = window.Blazor || {};
window.Blazor.registerChatDialogRef = function (dotNetRef) {
    window.chatDialogRef = dotNetRef;
};