var dotNetReference = null;
var globalAudioCtx = null;
var nextPlayTime = 0;
var lastBufferEndSamples = null;
var jsActiveTargetUrl = "";
var jsTextAccumulator = "";
var isJsFetchWorkerRunning = false;
var isJsPlaybackWorkerRunning = false;
var pendingSentencesToFetchQueue = [];
var downloadedAudioPayloadBufferQueue = [];
var activeWordTimeoutIdsPool = [];
let jsBearerToken = "";

window.Blazor = window.Blazor || {};
window.Blazor.registerChatDialogRef = function (dotNetRef) {
    window.chatDialogRef = dotNetRef;
};

export function initializeNeuralTts(dotNetRef) {
    dotNetReference = dotNetRef;
    console.log("[JS-MODULE] Studio Parallel Core Connected Safely.");
    if (dotNetReference) {
        dotNetReference.invokeMethodAsync('OnEngineReady');
    }
}

// Warm up the target URL context right when the LLM begins streaming tokens
export function initializeVoiceStreamSession(targetUrl, liveDotNetRef, bearerToken) {
    jsActiveTargetUrl = targetUrl;
    window.isWaitingForAudioHandshake = true;
    jsBearerToken = bearerToken;

    if (liveDotNetRef) {
        dotNetReference = liveDotNetRef;
    }
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
    jsTextAccumulator = jsTextAccumulator.replace(/\b([A-Za-z]+)([0-9]+)\b/g, "$1 $2");

    // Existing safety check for C# text streaming chunks
    if (/C#\s*\.\s*$/i.test(jsTextAccumulator) || /C#\s*\.\s*\s+$/i.test(jsTextAccumulator)) {
        return;
    }

    if (/\b[A-Za-z]+\s*\.\s*$/i.test(jsTextAccumulator)) {
        return;
    }

    let match = jsTextAccumulator.match(/^[^.!?]+[.!?](?!(?:[\s\r\n]*[Nn][Ee][Tt])\b)(?=\s+|\s*$)/i);
    if (match) {
        let completedSentence = match[0];
        jsTextAccumulator = jsTextAccumulator.substring(match.index + completedSentence.length);
        completedSentence = completedSentence.trim();
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

async function processBackgroundFetchLoop() {
    if (!globalAudioCtx || pendingSentencesToFetchQueue.length === 0) {
        isJsFetchWorkerRunning = false;
        checkSystemIdleState();
        return;
    }
    let textToGenerate = pendingSentencesToFetchQueue.shift();
    try {
        const response = await fetch(jsActiveTargetUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${jsBearerToken}` },
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
    if (isJsFetchWorkerRunning) {
        setTimeout(processBackgroundFetchLoop, 0);
    }
}

function processHardwarePlaybackLoop() {
    if (!globalAudioCtx || downloadedAudioPayloadBufferQueue.length === 0) {
        isJsPlaybackWorkerRunning = false;
        checkSystemIdleState();
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
                if (dotNetReference) dotNetReference.invokeMethodAsync('OnAudioStarted');
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
            let sanitizedText = sentenceText.replace(/[\[\]]/g, "").replace(/\([^)]*\)/g, "");
            sanitizedText = sanitizedText.replace(/\bC\s*#\s*.\s*NET\b/gi, "C# .NET");
            sanitizedText = sanitizedText.replace(/\bC\s*\+\+\b/gi, "C++");
            const words = sanitizedText.trim().split(/(?<!\b(?:C#|F#|C\+\+|CI))\s+(?!(?:\.?NET|CD)\b)/gi);
            if (words.length > 0) {
                const wordDisplayInterval = (audioBuffer.duration / words.length) * 1000;
                const chunkScheduleStartTime = nextPlayTime;
                words.forEach((word, index) => {
                    let wordDelayMs = Math.max(0, ((chunkScheduleStartTime - globalAudioCtx.currentTime) * 1000) + (index * wordDisplayInterval));
                    let timeoutId = setTimeout(() => {
                        if (dotNetReference) dotNetReference.invokeMethodAsync('OnWordAudioTriggered', word);
                    }, wordDelayMs);
                    activeWordTimeoutIdsPool.push(timeoutId);
                });
            }
        }

        let duration = audioBuffer.duration;
        nextPlayTime += duration;
        setTimeout(processHardwarePlaybackLoop, duration * 1000 - 15);
    } catch (playbackError) {
        console.error("[PLAYBACK-CRITICAL-FAULT]: Preserving state.", playbackError);
        isJsPlaybackWorkerRunning = false;
        checkSystemIdleState();
    }
}

function checkSystemIdleState() {
    if (!isJsFetchWorkerRunning && !isJsPlaybackWorkerRunning && pendingSentencesToFetchQueue.length === 0 && downloadedAudioPayloadBufferQueue.length === 0) {
        if (dotNetReference) {
            dotNetReference.invokeMethodAsync('OnSpeechFinished');
        }
    }
}

export function resetAudioEngineState() {
    console.log("[JS-SYSTEM] Purging dirty pipeline buffers for new session...");

    jsTextAccumulator = "";
    pendingSentencesToFetchQueue = [];
    isJsFetchWorkerRunning = false;

    if (globalAudioCtx) {
        nextPlayTime = globalAudioCtx.currentTime + 0.1;
    } else {
        nextPlayTime = 0;
    }
}

export function forceStopAndResetAudioContext() {
    console.log("[JS-AUDIO-SYSTEM] Purging timeline audio tracks...");
    activeWordTimeoutIdsPool.forEach(id => clearTimeout(id));
    activeWordTimeoutIdsPool = [];
    jsTextAccumulator = "";
    pendingSentencesToFetchQueue = [];
    downloadedAudioPayloadBufferQueue = [];
    isJsFetchWorkerRunning = false;
    isJsPlaybackWorkerRunning = false;
    if (globalAudioCtx) {
        try {
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