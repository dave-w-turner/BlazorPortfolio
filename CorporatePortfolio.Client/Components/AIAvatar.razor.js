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

export function initializeNeuralTts(dotNetRef) {
    dotNetReference = dotNetRef;
    console.log("[JS-MODULE] Studio Parallel Core Connected Safely.");
    if (dotNetReference) {
        dotNetReference.invokeMethodAsync('OnEngineReady');
    }
}

// Warm up the target URL context right when the LLM begins streaming tokens
export function initializeVoiceStreamSession(targetUrl) {
    jsActiveTargetUrl = targetUrl;
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

    // Fix word-smashing safely at clear word-boundary anchors
    jsTextAccumulator = jsTextAccumulator.replace(/\b([A-Za-z]+)([0-9]+)\b/g, "$1 $2");

    let match = jsTextAccumulator.match(/[^.!?]+[.!?](?=\s+[A-Z]|\s*$)/);

    if (match) {
        // CORRECTED FIX: Pull index 0 out of the regex result array block to run string trim mechanics safely!
        let completedSentence = match[0].trim();

        // Advance the text pointer cleanly by the entire length of the sentence
        jsTextAccumulator = jsTextAccumulator.substring(match.index + match[0].length);

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
    let finalSentence = jsTextAccumulator.trim();
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
            const words = sanitizedText.trim().split(/\s+/);

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
