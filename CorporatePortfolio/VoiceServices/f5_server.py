# f5_server.py - High Fidelity Local Voice Cloning Endpoint (Final Production)
import os
from dotenv import load_dotenv

current_folder = os.path.dirname(os.path.abspath(__file__))
env_file_path = os.path.join(current_folder, ".env")
load_dotenv(dotenv_path=env_file_path)

# Set absolute base configuration variables first
os.environ["KMP_DUPLICATE_LIB_OK"] = "TRUE"

import io
import torch
import soundfile as sf
import librosa
import torchaudio
import re

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
        
        processed_text = str(request.text).strip()

        # Strip hyphens completely to join the words natively
        processed_text = processed_text.replace("-", " ")

        # Fix pronounciations
        CANADIAN_DICTIONARY = {
            "routing": "rauwting",
            "processes": "prosesses",
            "processing": "prosessessing",
            "processed": "prosessessed",
            "sorry": "soary",
            "niche": "neesh",
            "foyer": "foyyay",
            "pasta": "paassta",
            "cache": "kash",
            "asphalt": "ashphalt",
            "lever": "leever",
            "decal": "dee-kal",
            "project": "proeject",
            "projects": "proejects",
            "drama": "dramma",
            "shone": "shawn",
            "adult": "addult",
            "tomorrow": "toomorerow",
            "borrow": "borerow",
            "sql": "Sequool",
            "saas": "Sass",
            "iaas": "Infrastructure as a service",
            "paas": "Pass",
            "latency": "laytency",
            "kubernetes": "koobernetties",
            "variable": "vairyable",
            "daemon": "daymon",
            ".net": "dot net",
            "dotnot": "dot net",
            "C#": "C-sharp"
        }

        processed_text = str(request.text).strip()
        processed_text = processed_text.replace("-", " ")

        for word, phonetic in CANADIAN_DICTIONARY.items():
            pattern = re.compile(rf'\b{re.escape(word)}\b', re.IGNORECASE)
            
            def match_case(match):
                text = match.group()
                # If the original matched word was fully uppercase (like SQL or SAAS)
                if text.isupper():
                    return phonetic.upper()
                # If the original matched word was capitalized (like Process or Routing)
                if text[0].isupper():
                    return phonetic.capitalize()
                # Fall back to standard lowercase replacement
                return phonetic

            processed_text = pattern.sub(match_case, processed_text)

        if not processed_text.endswith(('.', '!', '?')):
            processed_text += '.'

        local_config = {
            "ref_text": "Our synchronized infrastructure seamlessly processes complex data arrays.",
            "gen_text": " " + processed_text
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
            
        # FIX: Raised boundary to 7 seconds so your 5.4s track is never truncated!
        max_samples = int(24000 * 7.0)  
        audio_data = audio_data[:max_samples]
        
        normalized_audio_path = "normalized_reference.wav"
        sf.write(normalized_audio_path, audio_data, 24000)

        print(f"[F5-ENGINE] Processing flow-matching inference for prompt: '{local_config['gen_text']}'")
        
        wav, sr, spect = f5_pipeline.infer(
            normalized_audio_path,
            local_config["ref_text"],
            local_config["gen_text"],
            speed=1.3,               
            nfe_step=16              
        )
        
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
