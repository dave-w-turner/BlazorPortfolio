let dotNetComponentRef = null;
let currentAudioElement = null;

/**
 * Initializes the connection link to your background Python service
 * @param {object} dotNetRef - Blazor instance context passed via DotNetObjectReference
 */
export async function initializeNeuralTts(dotNetRef) {
    dotNetComponentRef = dotNetRef;
    console.log("[F5-BRIDGE] Local service bridge successfully configured.");

    // Instantly unlock the Blazor MudButton since the local Python server handles everything
    if (dotNetComponentRef) {
        await dotNetComponentRef.invokeMethodAsync('OnEngineReady');
    }
}

/**
 * Sends text prompt to Python and streams back high-fidelity cloned audio
 * @param {string} incomingText - The text string you want your clone to speak
 */
export async function synthesizeClonedVoice(incomingText) {
    // Check if Blazor is passing the timeout options structural array as the first variable layout
    let targetText = incomingText;
    if (typeof incomingText === 'object') {
        // If Blazor routes the options block down as argument 0, grab the literal string text out of argument 1 instead
        targetText = arguments[1];
    }

    if (stringIsBlank(targetText)) return;

    try {
        console.log("[F5-BRIDGE] Dispatching text sequence to local F5-TTS service port...");

        const response = await fetch('http://localhost:5000/api/tts', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'audio/wav'
            },
            body: JSON.stringify({ text: targetText })
        });

        if (!response.ok) {
            throw new Error(`Python server returned an execution error: ${response.status}`);
        }

        const audioBlob = await response.blob();
        const dynamicAudioUrl = URL.createObjectURL(audioBlob);

        if (currentAudioElement) {
            currentAudioElement.pause();
            currentAudioElement.removeEventListener('ended', handleAudioPlaybackEnded);
        }

        currentAudioElement = new Audio(dynamicAudioUrl);
        currentAudioElement.addEventListener('ended', handleAudioPlaybackEnded);
        await currentAudioElement.play();

    } catch (error) {
        console.error("[F5-BRIDGE] Pipeline generation failure: ", error);
        if (dotNetComponentRef) {
            await dotNetComponentRef.invokeMethodAsync('OnSpeechFinished');
        }
    }
}

async function handleAudioPlaybackEnded() {
    console.log("[F5-BRIDGE] Playback finished. Resetting UI state handles...");
    if (dotNetComponentRef) {
        await dotNetComponentRef.invokeMethodAsync('OnSpeechFinished');
    }
}

function stringIsBlank(str) {
    return (!str || /^\s*$/.test(str));
}
