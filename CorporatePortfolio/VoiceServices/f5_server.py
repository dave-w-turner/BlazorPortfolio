# f5_server.py - High Fidelity Local Voice Cloning Endpoint (Final Production)
import os
from dotenv import load_dotenv

load_dotenv()

# Set absolute base configuration variables first
os.environ["KMP_DUPLICATE_LIB_OK"] = "TRUE"

import io
import torch
import soundfile as sf
import librosa
import torchaudio

# ==============================================================================
# MONKEY-PATCH: Explicitly force torchaudio.load to use soundfile decoding layout.
def soundfile_load_fallback(filepath, frame_offset=0, num_frames=-1, normalize=True, channels_first=True):
    data, sample_rate = sf.read(filepath, dtype='float32')
    tensor_data = torch.FloatTensor(data)
    if len(tensor_data.shape) == 1:
        tensor_data = tensor_data.unsqueeze(0)
    else:
        tensor_data = tensor_data.t()
    return tensor_data, sample_rate

torchaudio.load = soundfile_load_fallback
print("[PATCH] torchaudio.load successfully bound to pure Python Soundfile wrapper.")
# ==============================================================================

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from f5_tts.api import F5TTS

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

print("Initializing F5-TTS 384M Parameter Flow-Matching Network...")
# Explicitly loads onto your high-speed Nvidia graphics card cores!
f5_pipeline = F5TTS(device="cuda" if torch.cuda.is_available() else "cpu") 

class TTSRequest(BaseModel):
    text: str

@app.post("/api/tts")
async def generate_voice_clone(request: TTSRequest):
    try:
        ref_audio_file = "david_voice_sample.wav"
        
        # Isolated configuration parameters
        local_config = {
            "ref_text": "Welcome to my digital innovation workspace.",
            "gen_text": str(request.text).strip()
        }
        
        if not os.path.exists(ref_audio_file):
            raise FileNotFoundError(f"Could not find reference clip file at: {os.path.abspath(ref_audio_file)}")
            
        print(f"\n[F5-ENGINE] Ingesting audio asset: {ref_audio_file}")
        audio_data, sample_rate = sf.read(ref_audio_file)
        
        if len(audio_data.shape) > 1:
            audio_data = audio_data.mean(axis=1)
            
        if sample_rate != 24000:
            print(f"[F5-ENGINE] Resampling audio track dynamically from {sample_rate}Hz to 24000Hz...")
            audio_data = librosa.resample(audio_data, orig_sr=sample_rate, target_sr=24000)
            
        max_samples = int(24000 * 2.5)  # 2.5 seconds at 24kHz
        audio_data = audio_data[:max_samples]
        
        normalized_audio_path = "normalized_reference.wav"
        sf.write(normalized_audio_path, audio_data, 24000)

        print(f"[F5-ENGINE] Processing flow-matching inference for prompt: '{local_config['gen_text']}'")
        
        # LOCKED PACK: Positional variables + verified keyword properties
        wav, sr, spect = f5_pipeline.infer(
            normalized_audio_path,
            local_config["ref_text"],
            local_config["gen_text"],
            speed=0.95,               # <-- Your perfect natural cadence multiplier!
            nfe_step=16              # Controls resolution steps smoothly for GPU
        )
        
        # Stream standard 16-bit playable wave bytes back to the Blazor frontend
        buffer = io.BytesIO()
        sf.write(buffer, wav, sr, format='WAV', subtype='PCM_16')
        buffer.seek(0)
        
        from fastapi.responses import Response
        return Response(content=buffer.read(), media_type="audio/wav")

    except Exception as e:
        import traceback
        print("\n" + "!"*60)
        print("[CRITICAL EXCEPTION DETECTED INSIDE THE INTERPOLATION CORE]:")
        traceback.print_exc()
        print("!"*60 + "\n")
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=5000)
