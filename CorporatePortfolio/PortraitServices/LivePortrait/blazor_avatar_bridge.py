import os
import cv2
import json
import base64
import asyncio
import torch
import numpy as np
from fastapi import FastAPI, UploadFile, File
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from starlette.websockets import WebSocket, WebSocketDisconnect

# Import the clean official configuration models
from src.config.inference_config import InferenceConfig
from src.config.crop_config import CropConfig
from src.live_portrait_pipeline import LivePortraitPipeline

print("[AVATAR-API] Booting production FastAPI gateway environment...")

# Initialize standard engine configurations routing directly to your NVIDIA GPU
inference_cfg = InferenceConfig()
crop_cfg = CropConfig()
inference_cfg.device = "cuda" if torch.cuda.is_available() else "cpu"

pipeline = LivePortraitPipeline(inference_cfg=inference_cfg, crop_cfg=crop_cfg)
wrapper = pipeline.live_portrait_wrapper

app = FastAPI()

# Enable cross-origin resource sharing so your Blazor app (port 7116) can communicate cleanly
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Global memory caches to maintain user facial context layers across HTTP and WebSocket channels
global_context = {
    "x_s_info": None,
    "f_s": None,
    "x_s": None,
    "x_c_s": None,
    "R_s": None,
}

@app.post("/set-avatar-context")
async def set_avatar_context(file: UploadFile = File(...)):
    """
    🎯 GATE 1: Receives the initial portrait file via HTTP FormData upload.
    Crops, processes, and pre-caches the facial landmarks into VRAM.
    """
    print(f"[AVATAR-API] Received context sync request file: {file.filename}")
    try:
        contents = await file.read()
        nparr = np.frombuffer(contents, np.uint8)
        img_bgr = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        
        # Convert to RGB to align with the AI network graph constraints
        img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
        
        # Crop the headshot cleanly using the official repository's internal tools
        crop_info = pipeline.cropper.crop_source_image(img_rgb, crop_cfg)
        if crop_info is None:
            print("[AVATAR-API-WARN] Face detection failed for uploaded image buffer.")
            return JSONResponse(status_code=400, content={"status": "error", "message": "No face detected in image"})
            
        img_crop_256x256 = crop_info['img_crop_256x256']
        I_s = wrapper.prepare_source(img_crop_256x256)
        
        # Extract and cache pristine identity matrices into GPU VRAM
        global_context["x_s_info"] = wrapper.get_kp_info(I_s)
        global_context["f_s"] = wrapper.extract_feature_3d(I_s)
        global_context["x_s"] = wrapper.transform_keypoint(global_context["x_s_info"])
        global_context["x_c_s"] = global_context["x_s_info"]['kp']
        global_context["R_s"] = global_context["x_s_info"]['R'] if 'R' in global_context["x_s_info"].keys() else global_context["x_s_info"].get('R_d', torch.eye(3).unsqueeze(0).cuda())
        
        print("[AVATAR-API] Dynamic portrait metrics successfully loaded and cached in VRAM!")
        return {"status": "ready", "message": "Context synchronized successfully."}
        
    except Exception as e:
        print(f"[AVATAR-API-ERR] Critical HTTP failure during context sync: {e}")
        return JSONResponse(status_code=500, content={"status": "error", "message": str(e)})

@app.websocket("/stream-avatar")
async def stream_avatar_endpoint(websocket: WebSocket):
    """
    ⚡ GATE 2: Handles the high-speed real-time binary audio stream.
    Warps the cached face in VRAM and pushes raw image bytes back down the pipe.
    """
    await websocket.accept()
    print("[AVATAR-API] High-speed audio WebSocket pipeline established successfully.")
    
    # Verify we have a portrait loaded before processing voice data
    if global_context["x_s_info"] is None:
        print("[AVATAR-API-WARN] WebSocket pipe opened, but no portrait context has been synced yet over HTTP!")

    try:
        while True:
            # Await raw audio binary array buffers arriving directly from the Blazor client microphone
            message = await websocket.receive_bytes()
            
            if global_context["x_s_info"] is None:
                continue

            # 1. 🚀 ADAPTIVE SAFE F5-TTS AUDIO AMPLITUDE PARSER
            mouth_intensity = 0.0
            if len(message) > 0:
                try:
                    audio_array = np.frombuffer(message, dtype=np.int16)
                    
                    if audio_array.max() > 100 or audio_array.min() < -100:
                        abs_array = np.abs(audio_array).astype(np.float32)
                        mean_amplitude = np.mean(abs_array)
                        
                        if mean_amplitude < 50.0:
                            mouth_intensity = 0.0
                        else:
                            # 🎯 THE FIX: Changed divisor from 1500.0 to 35000.0
                            # This scales down the integer volume to a safe 0.01 - 0.05 range!
                            mouth_intensity = float(mean_amplitude / 35000.0)
                    else:
                        audio_array_f32 = np.frombuffer(message, dtype=np.float32)
                        audio_array_f32 = audio_array_f32[np.isfinite(audio_array_f32)]
                        if len(audio_array_f32) > 0:
                            abs_array = np.abs(audio_array_f32)
                            mean_amplitude = np.mean(abs_array)
                            
                            if mean_amplitude < 0.001:
                                mouth_intensity = 0.0
                            else:
                                # 🎯 THE FIX: Changed multiplier from 3.5 to 0.15
                                # This scales down the float volume to a safe 0.01 - 0.05 range!
                                mouth_intensity = float(mean_amplitude * 0.15)
                            
                except Exception as audio_err:
                    mouth_intensity = 0.0

                # 🎯 THE STRIC LIMIT FIX: Lowered the maximum safety cap from 0.35 to 0.06
                # This guarantees the math can NEVER overload the model and melt the face!
                mouth_intensity = max(0.0, min(mouth_intensity, 0.06))

            # Cache reference shortcuts to keep code tight and optimize speed
            x_s_info = global_context["x_s_info"]
            f_s = global_context["f_s"]
            x_s = global_context["x_s"]
            x_c_s = global_context["x_c_s"]
            
            # Extract the raw baseline rotation and tracking scale vectors fresh every single pass
            R_s = x_s_info['R'] if 'R' in x_s_info.keys() else x_s_info.get('R_d', torch.eye(3).unsqueeze(0).cuda())
            scale_locked = x_s_info['scale']
            t_locked = x_s_info['t']

            # ==============================================================================
            # 🎯 THE PERMANENT ISOLATION MATRIX HOTFIX (ZERO FILTERS)
            # ==============================================================================
            # Create a fresh, empty NumPy array on the CPU for every audio frame.
            # This completely guarantees that your face matrix will NEVER accumulate or melt!
            numpy_exp = np.zeros((1, 21, 3), dtype=np.float32)

            if mouth_intensity > 0.0:
                # Explicitly target only the vertical movement axis (index 1) for mouth tracking
                # 6 = mouth opening, 12/17 = lips, 14 = lower jaw drop
                numpy_exp.itemset((0, 6, 1), mouth_intensity)
                numpy_exp.itemset((0, 12, 1), mouth_intensity)
                numpy_exp.itemset((0, 14, 1), mouth_intensity)
                numpy_exp.itemset((0, 17, 1), mouth_intensity)
                numpy_exp.itemset((0, 19, 1), mouth_intensity)
                numpy_exp.itemset((0, 20, 1), mouth_intensity)

            # Convert our clean, isolated array into a brand new PyTorch tensor on the GPU
            delta_new = torch.from_numpy(numpy_exp).cuda().float()

            with torch.no_grad():
                x_d_i_new = scale_locked * (x_c_s @ R_s + delta_new) + t_locked
                x_d_i_new.select(2, 2).fill_(0)  # Enforce zero tz axis matching rules
         
                out = wrapper.warp_decode(f_s, x_s, x_d_i_new)
                raw_frame = wrapper.parse_output(out['out'])[0]
                
                # Convert the float array to an 8-bit unsigned integer canvas (0 to 255)
                # and push it onto your standard CPU memory tracking channels
                if isinstance(raw_frame, torch.Tensor):
                    raw_frame = raw_frame.cpu().numpy()
                    
                # If the values are floats between 0 and 1, upscale them to full pixel range
                if raw_frame.max() <= 1.0:
                    output_bgr = (raw_frame * 255.0).astype(np.uint8)
                else:
                    output_bgr = raw_frame.astype(np.uint8)

            # Convert RGB tensor mapping seamlessly to standard web browser BGR formatting
            output_bgr = cv2.cvtColor(output_bgr, cv2.COLOR_RGB2BGR)
            
            # Encode frame straight to a lightweight JPEG string layout completely in memory
            success, encoded_image = cv2.imencode('.jpg', output_bgr, [int(cv2.IMWRITE_JPEG_QUALITY), 85])
            
            if success:
                # 🎯 MATCH JAVASCRIPT EXACT EXPECTATIONS: Stream out raw binary bytes (Do not wrap in a JSON string)
                await websocket.send_bytes(encoded_image.tobytes())
                
    except WebSocketDisconnect:
        print("[AVATAR-API] Audio stream socket disconnected cleanly by client.")
    except Exception as e:
        print(f"[AVATAR-API-ERR] Core stream loop failure: {e}")

if __name__ == "__main__":
    import uvicorn
    # Launch the API gateway to listen exactly on localhost port 8080
    uvicorn.run(app, host="127.0.0.1", port=8080)
