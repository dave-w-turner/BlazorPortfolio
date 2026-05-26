# f5_server.py - High Fidelity Local Voice Cloning Endpoint (Final Production with Streaming support)
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
import numpy as np

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

from fastapi import FastAPI, HTTPException, Depends, Security
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from f5_tts.api import F5TTS
from starlette.status import HTTP_403_FORBIDDEN

# --- NEW AUTH CONFIGURATION ---
# Create a secure password token. Change this to whatever you want!
SHARED_SECRET_TOKEN = os.environ.get("F5_SHARED_SECRET_TOKEN", "TOKEN_NOT_SET_IN_ENV_FILE") 

if SHARED_SECRET_TOKEN == "TOKEN_NOT_SET_IN_ENV_FILE":
    print("[WARNING] F5_SHARED_SECRET_TOKEN env variable is missing! Check your local .env file.")

security_bearer = HTTPBearer(auto_error=False) 

async def verify_azure_token(credentials: HTTPAuthorizationCredentials = Depends(security_bearer)):
    if credentials and credentials.credentials == SHARED_SECRET_TOKEN:
        return credentials.credentials
    raise HTTPException(
        status_code=HTTP_403_FORBIDDEN, 
        detail="Access Denied: Invalid or missing Authorization Bearer Token."
    )
# ------------------------------

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Swap with your actual frontend domain when pushing live!
    allow_methods=["*"],
    allow_headers=["*"],
)

print("Initializing F5-TTS 384M Parameter Flow-Matching Network...")
# Explicitly loads onto your high-speed Nvidia graphics card cores!
f5_pipeline = F5TTS(device="cuda" if torch.cuda.is_available() else "cpu") 

class TTSRequest(BaseModel):
    text: str

@app.post("/api/tts")
async def generate_voice_clone(request: TTSRequest, token: str = Depends(verify_azure_token)):
    try:
        ref_audio_file = "david_voice_sample.wav"
        
        processed_text = str(request.text).strip()

        # FIX STRAY SPACE: Look for periods touching a letter, but EXPLICITLY ignore it 
        # if the following letters spell out "net" or "NET"!
        processed_text = re.sub(r'\.(?=[A-Za-z0-9])(?!(?:net|NET)\b)', '. ', processed_text)

        # 2. HARD HYBRID OVERRIDES FOR TECHNICAL SYMBOLS (Runs cleanly below)
        processed_text = re.sub(r'\bC#\b|\bC#', "C sharp", processed_text, flags=re.IGNORECASE)
        processed_text = re.sub(r'\.NET\b', "dot net", processed_text, flags=re.IGNORECASE)
        processed_text = re.sub(r'\b\.net\b', "dot net", processed_text, flags=re.IGNORECASE)
        processed_text = processed_text.replace(".NET", "dot net")
        processed_text = processed_text.replace(".net", "dot net")

        # 3. CANADIAN ACCENT DICTIONARY MAPPINGS (Pure letters only)
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
            "decal": "dee kal",
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
            "daemon": "daymon"
        }

        # Run the standard case-insensitive regex loop for the pure letter keys
        for word, phonetic in CANADIAN_DICTIONARY.items():
            pattern = re.compile(rf'\b{re.escape(word)}\b', re.IGNORECASE)
            
            def match_case(match):
                text = match.group()
                if text.isupper():
                    return phonetic.upper()
                if text.istitle():
                    return phonetic.capitalize()
                return phonetic

            processed_text = pattern.sub(match_case, processed_text)

        # 4. FIX COMMA CRACKLES SAFELY WITHOUT SWALLOWING WORD SPACES
        # Adds space after a comma if missing, then pads the front of the comma with a clean space token
        processed_text = re.sub(r',(?=[A-Za-z0-9])', ', ', processed_text)
        processed_text = re.sub(r'\s*,\s*', ' , ', processed_text) # Handles spacing on both sides safely

        # 5. STRIP STRUCTURAL HYPHENS
        processed_text = processed_text.replace("-", " ")

        # 6. COMPRESS CONSECUTIVE SPACES INTO A SINGLE UNIFIED BLANK SPACE
        # This replaces your old buggy block and guarantees 'Over 10' is preserved!
        processed_text = ' '.join(processed_text.split())

        # 7. APPEND TERMINAL PUNCTUATION GUARD
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
            
        max_samples = int(24000 * 7.0)  
        audio_data = audio_data[:max_samples]
        
        normalized_audio_path = "normalized_reference.wav"
        sf.write(normalized_audio_path, audio_data, 24000)

        # ==============================================================================
        # STREAMING GENERATOR CORE
        # ==============================================================================
        def chunk_audio_stream():
            # Only split on final periods (.) followed by spaces to prevent fragmentation
            raw_sentences = re.split(r'(?<=\.)\s+', local_config["gen_text"].strip())
            sentences = [s.strip() for s in raw_sentences if s.strip()]
            
            for sentence in sentences:
                # Add silence buffers so text components wind down smoothly inside the DiT matrix
                padded_sentence = " " + sentence + " . . ."
                
                print(f"[F5-ENGINE] Processing sub-block chunk: '{padded_sentence}'")
                wav_chunk, sr, _ = f5_pipeline.infer(
                    normalized_audio_path,
                    local_config["ref_text"],
                    padded_sentence,
                    speed=1.35,
                    nfe_step=24
                )
                
                # ACOUSTIC LIMITER MATRIX: Scale max floating peak safely down to -1dB (0.89)
                max_peak = np.max(np.abs(wav_chunk))
                if max_peak > 0:
                    wav_chunk = (wav_chunk / max_peak) * 0.89
                
                # Chunk and yield standard uncompressed mono 16-bit PCM bytes (4096-frame buffer)
                chunk_size = 4096
                for i in range(0, len(wav_chunk), chunk_size):
                    chunk = wav_chunk[i:i + chunk_size]
                    pcm_data = (chunk * 32767).astype(np.int16).tobytes()
                    yield pcm_data

            # Final line terminal cushion
            silence_pad = np.zeros(9600, dtype=np.int16).tobytes()
            yield silence_pad

        return StreamingResponse(chunk_audio_stream(), media_type="audio/pcm")

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
