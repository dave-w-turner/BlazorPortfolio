import asyncio
import base64
import cv2
import numpy as np
import ctypes 
import os
import sys
import shutil
import re
import torch
import wave
from fastapi import FastAPI, WebSocket, WebSocketDisconnect, UploadFile, File, APIRouter, File, UploadFile, HTTPException, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware
from omegaconf import OmegaConf
import scipy.signal
import inspect
import copy

try:
    import torch
    if torch.cuda.is_available():
        torch_bin_dir = os.path.join(os.path.dirname(torch.__file__), "lib")
        if os.path.exists(torch_bin_dir):
            os.environ["PATH"] = torch_bin_dir + os.path.pathsep + os.environ["PATH"]
            if hasattr(os, "add_dll_directory"):
                os.add_dll_directory(torch_bin_dir)
            print(f"[AVATAR-API] Successfully linked ONNX onto PyTorch's CUDA Runtime: {torch_bin_dir}")
            
        trt_bin_dir = r"C:\TensorRT-11.2.1.2\lib" 
        if os.path.exists(trt_bin_dir):
            os.environ["PATH"] = trt_bin_dir + os.path.pathsep + os.environ["PATH"]
            if hasattr(os, "add_dll_directory"):
                os.add_dll_directory(trt_bin_dir)
            print(f"[AVATAR-API] Successfully linked TensorRT Library Paths: {trt_bin_dir}")
        else:
            print(f"[AVATAR-API-WARN] TensorRT path not found at '{trt_bin_dir}'. Ensure your ZIP is unzipped here!")
except Exception as e:
    print(f"[AVATAR-API-WARN] Hardware acceleration path checks bypassed: {e}")


from src.pipelines.faster_live_portrait_pipeline import FasterLivePortraitPipeline

# 1. INITIALIZE GLOBAL FASTAPI APP INSTANCE
app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

active_connections = []

global_session_state = {
    "is_prepared": False,
    "is_processing_voice": False
}

# ==============================================================================
# FORCE-INJECT CTYPES, TENSORRT, & PATH PATCH INTO PREDICTOR SCOPE 🛠️
# ==============================================================================
import ctypes
try:
    import tensorrt as trt
    import src.models.predictor as trt_predictor_module
    
    # Inject missing core global names directly into the module scope
    trt_predictor_module.ctypes = ctypes
    trt_predictor_module.trt = trt  # 🚀 INJECT MISSING TENSORRT ENGINE UTILITY
    print("[AVATAR-API] Programmatically injected 'ctypes' and 'trt' directly into predictor.py scope!")
    
    # Keep our working absolute path conversion active
    plugin_relative_path = "./checkpoints/liveportrait_onnx/grid_sample_3d_plugin.dll"
    plugin_absolute_path = os.path.abspath(plugin_relative_path)
    
    original_cdll = ctypes.CDLL
    class PatchedCDLL(original_cdll):
        def __init__(self, name, *args, **kwargs):
            if "grid_sample_3d_plugin.dll" in name:
                name = plugin_absolute_path
                if "winmode" in kwargs:
                    kwargs.pop("winmode")
            super().__init__(name, *args, **kwargs)
            
    ctypes.CDLL = PatchedCDLL
    print(f"[AVATAR-API] Programmatically patched plugin loading sequence to: {plugin_absolute_path}")
except Exception as injection_err:
    print(f"[AVATAR-API-WARN] Global scope injection breakdown: {injection_err}")

# 2. SPIN UP THE ACCELERATED PIPELINE ON CUDA
print("[AVATAR-API] Booting hardware runtime framework on CUDA...")
cfg = OmegaConf.load("configs/onnx_infer.yaml")
avatar_pipeline = FasterLivePortraitPipeline(cfg, use_tensorrt=False)

# ==============================================================================
# GLOBAL INDESTRUCTIBLE ONNX SESSION RUNTIME INTERCEPTOR (ALL MODELS) 🔥 🚀
# ==============================================================================
try:
    if hasattr(avatar_pipeline, "model_dict") and isinstance(avatar_pipeline.model_dict, dict):
        import torch
        import torch.nn.functional as F
        
        print("[AVATAR-API] Scanning pipeline model inventory for low-level session gates...")
        
        # Define the global low-level interceptor function
        def absolute_low_level_session_run_patch_factory(original_run_func):
            def execution_wrapper(output_names, input_feed, run_options=None):
                """
                Intercepts runtime execution graphs at the lowest possible gate.
                Processes 5-D volumetric data on PyTorch CUDA, completely bypassing ONNX limitations.
                """
                try:
                    # Look for input layers matching volumetric spatial maps
                    feature_map = input_feed.get("feature_map")
                    flow_grid = input_feed.get("flow_grid")

                    if feature_map is not None and flow_grid is not None:
                        # 1. Instantly promote input matrices to PyTorch CUDA spaces
                        if isinstance(feature_map, np.ndarray):
                            feature_map = torch.from_numpy(feature_map).cuda().float()
                        elif isinstance(feature_map, torch.Tensor):
                            feature_map = feature_map.cuda().float()
                            
                        if isinstance(flow_grid, np.ndarray):
                            flow_grid = torch.from_numpy(flow_grid).cuda().float()
                        elif isinstance(flow_grid, torch.Tensor):
                            flow_grid = flow_grid.cuda().float()

                        # 2. Intercept and match volumetric array dimensions dynamically
                        if feature_map.dim() == 5:
                            if flow_grid.dim() == 3:
                                B, N, C = flow_grid.shape
                                D = feature_map.size(2)  # 16
                                H = feature_map.size(3)  # 64
                                W = feature_map.size(4)  # 64
                                
                                # Project the tracking keys perfectly across the 5-D grid topology
                                if N == 21:
                                    flow_grid = flow_grid.view(B, 1, N, C)
                                    flow_grid = F.interpolate(flow_grid.permute(0, 3, 1, 2), size=(H, W), mode='bilinear', align_corners=True)
                                    flow_grid = flow_grid.permute(0, 2, 3, 1).unsqueeze(1).expand(B, D, H, W, C)
                                else:
                                    flow_grid = flow_grid.view(B, 1, 1, N, C).expand(B, D, H, W, C)

                            with torch.no_grad():
                                output_tensor = F.grid_sample(
                                    feature_map, 
                                    flow_grid, 
                                    mode='bilinear', 
                                    padding_mode='zeros', 
                                    align_corners=True
                                )
                                # Return standard list wrap to satisfy ONNX Runtime output signature
                                return [output_tensor.cpu().numpy()]

                except Exception as raw_session_err:
                    print(f"[AVATAR-API-DEBUG] Low-level interceptor bypass warning: {raw_session_err}")

                # Fallback to standard ONNX execution providers if conditions are flat (4-D maps)
                return original_run_func(output_names, input_feed, run_options)
            return execution_wrapper

        # Loop through every loaded model in the pipeline and patch its session
        for model_key, model_instance in avatar_pipeline.model_dict.items():
            if hasattr(model_instance, "session") and model_instance.session is not None:
                sess = model_instance.session
                if hasattr(sess, "run") and not hasattr(sess, "_original_run"):
                    sess._original_run = sess.run
                    sess.run = absolute_low_level_session_run_patch_factory(sess._original_run)
                    print(f"[AVATAR-API] Ultimate session hook successfully bound to model entry: '{model_key}' 🔥")
                    
except Exception as global_hook_err:
    print(f"[AVATAR-API-WARN] Failed to engage low-level session hooks: {global_hook_err}")

# ==============================================================================
# THE INDESTRUCTIBLE SIZE-COMPLIANT LIVE STREAMING GRID-SAMPLE HOOKS 🚀
# ==============================================================================

# Initialize global fallback variable to prevent UnboundLocalErrors
native_warping_spade_predict = None

fallback_path = "temp_portrait_input.png"
audio_temp_path = "temp_voice_chunk.wav"

# 3. BACKGROUND IDLE TICKER ENGINE (Fixed Non-Blocking Event-Yield)
async def send_idle_frames(websocket: WebSocket, state_container: dict):
    try:
        while True:
            if state_container.get("is_prepared") and not state_container.get("is_processing_voice"):
                if os.path.exists(fallback_path):
                    idle_frame = cv2.imread(fallback_path)
                    if idle_frame is not None and idle_frame.dtype == np.uint8:
                        _, jpeg_buffer = cv2.imencode('.jpg', idle_frame)
                        await websocket.send_bytes(jpeg_buffer.tobytes())
            
            # Universal yield allows async endpoints to receive concurrent data safely
            await asyncio.sleep(0.04)
    except Exception:
        await asyncio.sleep(0.04)

@app.post("/set-avatar-context")
async def set_avatar_context(file: UploadFile = File(...)):
    """
    Ingests the true user portrait, extracts expressions instantly without 
    triggering cold-start engine compilation penalties.
    """
    print(f"[AVATAR-API] Syncing live portrait asset context: {file.filename}")
    try:
        # Save inbound file to RAM or temporary storage space safely
        temp_portrait_path = "temp_portrait_input.png"
        with open(temp_portrait_path, "wb") as buffer:
            shutil.copyfileobj(file.file, buffer)
            
        # Extract operational matrices for the specific user asset
        init_success = avatar_pipeline.prepare_source(temp_portrait_path)
        if not init_success:
            raise HTTPException(status_code=400, detail="Failed to resolve facial landmarks.")
            
        # Cache references directly in RAM memory state
        global_session_state["cached_images"] = copy.deepcopy(getattr(avatar_pipeline, 'src_imgs', []))
        global_session_state["cached_landmarks"] = copy.deepcopy(getattr(avatar_pipeline, 'src_infos', []))
        global_session_state["is_prepared"] = True
        
        return {
            "status": "ready",
            "message": "User portrait mapped onto pre-warmed GPU channels successfully!"
        }
    except Exception as e:
        print(f"[AVATAR-API-CRIT] Context initialization breakdown: {e}")
        return {"status": "error", "message": str(e)}

    # If it is a fresh boot, force flag to false until the background worker completes the compilation pass
    global_session_state["is_prepared"] = False
    prin
    t(f"[AVATAR-API] Cold-start detected. Fast-tracking asset over RAM stream: {file.filename}")
    
    try:
        ram_temp_dir = tempfile.gettempdir()
        ram_fallback_path = os.path.join(ram_temp_dir, "avatar_portrait_vram_buffer.jpg")

        if os.path.exists(ram_fallback_path):
            try: os.remove(ram_fallback_path)
            except Exception: pass

        # Stream upload bytes quickly into virtual system RAM space
        with open(ram_fallback_path, "wb") as buffer:
            shutil.copyfileobj(file.file, buffer)

        # 🚀 2. THE ASYNC OFF-LOAD TRICK 🚀
        # We define a fast background function to process the warmup off the main web thread
        def run_background_warmup(path):
            print("[AVATAR-API] Background task started: Running facial extraction rules...")
            init_success = avatar_pipeline.prepare_source(path)
            if not init_success:
                print("[AVATAR-API-ERR] Background warmup failed to resolve landmarks.")
                return

            src_imgs_list = getattr(avatar_pipeline, 'src_imgs', [])
            src_infos_list = getattr(avatar_pipeline, 'src_infos', [])
            
            clean_pack = src_infos_list
            while isinstance(clean_pack, list) and len(clean_pack) == 1 and len(clean_pack) not in list((7, 10)): 
                clean_pack = clean_pack[0]
                
            if isinstance(clean_pack, list) and len(clean_pack) > 0 and isinstance(clean_pack[0], list):
                clean_pack = clean_pack[0]

            global_session_state["cached_landmarks"] = copy.deepcopy(clean_pack)
            global_session_state["cached_images"] = copy.deepcopy(src_imgs_list)

            # 🚀 OBLITERATE SECONDARY 31-SECOND FREEZE 🚀
            # We intercept the compiled RAM image tensor and save it straight over the repository 
            # template block. When the WebSocket opens, it reads this pre-optimized asset instantly!
            try:
                if len(src_imgs_list) > 0:
                    target_frame = src_imgs_list[0]
                    # Convert float tensor format down to standard uint8 image space if needed
                    if hasattr(target_frame, 'cpu'):
                        target_frame = target_frame.cpu().numpy()
                    if isinstance(target_frame, np.ndarray):
                        if target_frame.ndim == 4:
                            target_frame = np.squeeze(target_frame, axis=0)
                        if target_frame.ndim == 3 and target_frame.shape[0] == 3:
                            target_frame = np.transpose(target_frame, (1, 2, 0))
                        if target_frame.max() <= 1.0:
                            target_frame = (target_frame * 255.0).astype(np.uint8)
                        
                        # Overwrite the disk template to bypass secondary initialization loops
                        cv2.imwrite("temp_portrait_input.png", cv2.cvtColor(target_frame, cv2.COLOR_RGB2BGR))
                        print("[AVATAR-API] Seamlessly injected pre-optimized template to hard drive!")
            except Exception as template_ex:
                print(f"[AVATAR-API-WARN] Failed caching idle template descriptor: {template_ex}")
            
            # The exact millisecond this finishes, the flag turns True and unblocks the socket worker!
            global_session_state["is_prepared"] = True
            print("[AVATAR-API] Background warmup complete! Prime for non-blocking stream.")
            try: os.remove(path)
            except Exception: pass

        # Fire the processing operation onto an independent async background task queue thread
        background_tasks.add_task(run_background_warmup, ram_fallback_path)

        # Return a processing JSON payload to the Blazor frontend IMMEDIATELY (0 milliseconds delay)
        return {
            "status": "processing",
            "message": "Avatar upload cached. GPU processing offloaded to non-blocking background lane."
        }

    except Exception as e:
        print(f"[AVATAR-API-CRIT] Initialization breakdown crash: {e}")
        return {"status": "error", "message": str(e)}

# 1. ADD A WORKER QUEUE WITHIN YOUR WEBSOCKET ENDPOINT
@app.websocket("/stream-avatar")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    current_socket_id = id(websocket)
    audio_queue = asyncio.Queue()  # FIFO queue to store chunks securely
    
    if websocket not in active_connections:
        active_connections.append(websocket)
        
    print("[AVATAR-API] WebSocket connected. Waiting for background GPU context warmup...")
    
    # 🚀 1. NON-BLOCKING ASYNC GUARD RAIL 🚀
    # Prevents duplicate 30-second compilation passes by waiting for the RAM cache upload task
    while not global_session_state.get("is_prepared", False):
        await asyncio.sleep(0.1)

    print("[AVATAR-API] GPU pipeline ready! Binding directly to pre-allocated VRAM matrices...")
    
    try:
        # Pull properties directly from RAM cache to bypass secondary disk parsing
        avatar_pipeline.src_imgs = global_session_state.get("cached_images")
        avatar_pipeline.src_infos = global_session_state.get("cached_landmarks")
        avatar_pipeline.cfg.infer_params.flag_pasteback = False
        avatar_pipeline.cfg.infer_params.flag_stitching = False
    except Exception as e:
        print(f"[AVATAR-API-WARN] Static image pre-warm mapping issue: {e}")

    idle_task = asyncio.create_task(send_idle_frames(websocket, global_session_state))

    # 2. SEPARATE WORKER TO PROCESS QUEUED AUDIO ITEMS
    # DIRECT HIGH-SPEED ENGINE EXECUTION WORKER LOOP 🚀
    async def audio_processing_worker():
        print("[API-DEBUG] Audio processor online and listening for incoming Blazor bytes...")
        while True:
            actual_bytes = await audio_queue.get()
            print(f"[API-DEBUG] Snatched payload from queue! Length: {len(actual_bytes)} bytes.")
            try:
                if not global_session_state.get("is_prepared", False):
                    print("[API-DEBUG] Audio skipped - global_session_state is not prepared yet.")
                    continue

                # 1. PARSE F5-TTS AUDIO VECTORS NATIVELY
                raw_float_samples = np.frombuffer(actual_bytes, dtype=np.float32).copy()
                raw_float_samples = np.nan_to_num(raw_float_samples, nan=0.0, posinf=1.0, neginf=-1.0)

                # NOISE & SILENCE GATE 🛑
                # If the audio chunk is too short or its maximum volume is near total silence,
                # instantly flush the queue so it doesn't cause accumulation lag.
                if len(raw_float_samples) < 256 or np.max(np.abs(raw_float_samples)) < 0.01:
                    while not audio_queue.empty():
                        try:
                            audio_queue.get_nowait()
                            audio_queue.task_done()
                        except asyncio.QueueEmpty:
                            break
                    continue

                duration = len(raw_float_samples) / 24000

                num_target_samples = int(duration * 16000)
                if num_target_samples <= 0:
                    continue

                # Perfect math alignment resampling channel
                src_indices = np.linspace(0, len(raw_float_samples) - 1, num_target_samples)
                resampled_float = np.interp(src_indices, np.arange(len(raw_float_samples)), raw_float_samples)
                incoming_chunk_samples = np.clip(resampled_float * 32767.0, -32768, 32767).astype(np.int16)

                with wave.open(audio_temp_path, 'wb') as wav_file:
                    wav_file.setnchannels(1)
                    wav_file.setsampwidth(2) 
                    wav_file.setframerate(16000) # Save clean standard format for the model dict
                    wav_file.writeframes(incoming_chunk_samples.tobytes())

                global_session_state["is_processing_voice"] = True

                # 2. READ THE CACHED BACKING MATRICES FROM MEMORY
                clean_src_info = global_session_state.get("cached_landmarks")
                src_imgs_list = global_session_state.get("cached_images")

                if clean_src_info is None or src_imgs_list is None:
                    continue

                # Ensure image background matrix remains a clean 4-D numpy array object
                if isinstance(src_imgs_list, list):
                    numpy_array_target = np.array(src_imgs_list, dtype=np.float32)
                else:
                    numpy_array_target = src_imgs_list.astype(np.float32)
                    
                if numpy_array_target.ndim == 3:
                    numpy_array_target = np.expand_dims(numpy_array_target, axis=0)

                # 1. COMPUTE THE DYNAMIC VOLUME INTENSITY FROM THE F5-TTS GENERATED PAYLOAD FIRST 🚀
                # Calculate the overall root-mean-square energy level of the resampled float audio bytes
                audio_volume_power = np.sqrt(np.mean(resampled_float**2)) if len(resampled_float) > 0 else 0.0
            
                # Lower the gain scalar so the expression values stay within realistic human boundaries
                mouth_opening_amplitude = np.clip(audio_volume_power * 25.0, 0.0, 1.0)

                numpy_motion_dict = {
                    "pitch": np.zeros((1, 1), dtype=np.float32),
                    "yaw": np.zeros((1, 1), dtype=np.float32),
                    "roll": np.zeros((1, 1), dtype=np.float32),
                    "t": np.zeros((1, 3), dtype=np.float32),
                    "exp": np.zeros((1, 21, 3), dtype=np.float32), 
                    "scale": np.ones((1, 1), dtype=np.float32),
                    "kp": np.zeros((1, 21, 3), dtype=np.float32),  
                    "R": np.eye(3, dtype=np.float32)[None, ...], 
                    "R_d": np.eye(3, dtype=np.float32)[None, ...] 
                }

                # TARGET THE VERTICAL TRANSLATION CHANNELS PRECISELY WITH BRACKET INDEXING 🎯
                # Syntax map: [Batch 0, Landmark Slot Index, Vertical Y-Axis Channel 1]
            
                # 1. DRIVE THE MAIN LOWER JAW AND MOUTH LAYOUT DOWNWARD
                numpy_motion_dict["exp"][0, 19, 1] = mouth_opening_amplitude * 0.30  # Pulls lower chin down
                numpy_motion_dict["exp"][0, 20, 1] = mouth_opening_amplitude * 0.20  # Separates lower lip down
                numpy_motion_dict["exp"][0, 13, 1] = -mouth_opening_amplitude * 0.10 # Pulls upper lip slightly up
            
                # 2. BIND THE SAME VALUES TO THE TEMPLATE KEYPOINTS TO FORCE RELATIVE MOTION CALCULATIONS
                numpy_motion_dict["kp"][0, 19, 1] = mouth_opening_amplitude * 0.30
                numpy_motion_dict["kp"][0, 20, 1] = mouth_opening_amplitude * 0.20
                numpy_motion_dict["kp"][0, 13, 1] = -mouth_opening_amplitude * 0.10
            
                # Calculate the exact number of frames required to span this audio block
                num_frames_needed = int(duration * 25)
                num_frames_needed = max(5, num_frames_needed)
            
                # CLEAR THE INDEX OUT OF RANGE EXCEPTION CRASH 🚀
                # Create a completely isolated structural frame track array.
                # This satisfies FasterLivePortrait's internal template loop counters!
                packed_motion_info = []
                for frame_idx in range(num_frames_needed):
                    # Run a deep copy so each frame possesses its own independent memory allocation channel
                    frame_data_structure = copy.deepcopy(numpy_motion_dict)
                
                    # If warmshao's template engine checks for frame mappings, we bind them safely
                    frame_data_structure["frame_id"] = frame_idx
                    frame_data_structure["id"] = frame_idx
                
                    packed_motion_info.append(frame_data_structure)

                # ==============================================================================
                # 4. DIRECT CORE PIPELINE ROUTE WITH HIGH-LEVEL PREDICT MAPPING 🎯
                # ==============================================================================
                try:
                    # A. Deep-detect and normalize structural nesting of landmark frames
                    # FasterLivePortrait expects: [ [item1, item2, ..., item10] ]
                    normalized_src_info = clean_src_info
            
                    if isinstance(normalized_src_info, list):
                        # Case A: If it's a flat 10-item tuple/list, wrap it once
                        if len(normalized_src_info) == 10:
                            normalized_src_info = [normalized_src_info]
                        # Case B: If it's double-nested [[ [10 items] ]], drill down one level
                        elif len(normalized_src_info) == 1 and isinstance(normalized_src_info[0], list):
                            if len(normalized_src_info[0]) == 1 and isinstance(normalized_src_info[0][0], (list, tuple)):
                                normalized_src_info = normalized_src_info[0]

                    # B. Execute frame generation using the high-level predict gate
                    # This automatically handles dimensional flattening to prevent 5-D crashes!
                    if hasattr(avatar_pipeline, "predict"):
                        outputs = avatar_pipeline.predict(
                            dri_motion_info=packed_motion_info,
                            img_src=numpy_array_target,
                            src_info=normalized_src_info
                        )
                    else:
                        # Direct safe fallback to the native engine array parser
                        outputs = avatar_pipeline.run_with_pkl(
                            dri_motion_info=packed_motion_info,
                            img_src=numpy_array_target,
                            src_info=normalized_src_info
                        )
                
                except Exception as direct_ex:
                    print(f"[AVATAR-API-ERR] Pipeline execution failed: {direct_ex}")
                    continue

                # 5. STREAM GENERATED PIXELS TO THE WEBSOCKET CHANNEL 🚀
                if outputs is not None:
                    # FasterLivePortrait returns a tuple: (list_of_frames, motion_data)
                    # We grab just the frame sequences list out of slot 0!
                    frame_sequence = outputs[0] if isinstance(outputs, (tuple, list)) else outputs
                    if not isinstance(frame_sequence, (list, tuple)):
                        frame_sequence = [frame_sequence]
                    
                    for frame_package in frame_sequence:
                        frame_element = frame_package
                        
                        # Handle dictionary unpacking cleanly if present
                        if isinstance(frame_package, dict):
                            frame_element = frame_package.get("frame") or frame_package.get("output")
                            if frame_element is None:
                                # Safe target unpacking of the first values array item
                                dict_values = list(frame_package.values())
                                frame_element = dict_values[0] if len(dict_values) > 0 else None

                        if frame_element is None:
                            continue

                        # Extract array data out of PyTorch tensors cleanly
                        if hasattr(frame_element, 'detach'):
                            frame_element = frame_element.detach().cpu().numpy()
                        elif hasattr(frame_element, 'cpu'):
                            frame_element = frame_element.cpu().numpy()

                        # Validate type requirements before advancing to array processing
                        if not isinstance(frame_element, np.ndarray):
                            continue

                        if len(frame_element.shape) == 4:
                            frame_element = np.squeeze(frame_element, axis=0)
                        if len(frame_element.shape) == 3 and frame_element.shape[0] == 3:
                            frame_element = np.transpose(frame_element, (1, 2, 0))

                        if frame_element.dtype != np.uint8:
                            if frame_element.max() <= 1.0:
                                frame_element = (frame_element * 255.0)
                            frame_element = np.clip(frame_element, 0, 255).astype(np.uint8)

                        if len(frame_element.shape) == 3:
                            final_render = cv2.cvtColor(frame_element, cv2.COLOR_RGB2BGR)
                            _, jpeg_buffer = cv2.imencode('.jpg', final_render)
                    
                            # Stream the generated frame across the open websocket
                            await websocket.send_bytes(jpeg_buffer.tobytes())
                    
                            # PACED PLAYBACK LOCK: Sleep for exactly 40ms (25 FPS) ⏱️
                            await asyncio.sleep(0.04)

                global_session_state["is_processing_voice"] = False

            except Exception as inner_ex:
                print(f"[AVATAR-API-ERR] Worker batch error: {inner_ex}")
                global_session_state["is_processing_voice"] = False
            finally:
                audio_queue.task_done()


    # Start the background processing loop
    worker_task = asyncio.create_task(audio_processing_worker())

    # 3. WEBSOCKET LISTENER COROUTINE 
    try:
        while True:
            raw_message = await websocket.receive()
            if raw_message.get("type") == "websocket.disconnect":
                break

            bytes_data = raw_message.get("bytes")
            if bytes_data or (raw_message.get("type") == "websocket.receive" and "bytes" in raw_message):
                actual_bytes = bytes_data if bytes_data is not None else raw_message["bytes"]
                
                # Push the raw audio payload into the queue instantly
                await audio_queue.put(actual_bytes)

    except WebSocketDisconnect:
        print(f"[AVATAR-UI] Pipe detached cleanly.")
    except Exception as e:
        print(f"[AVATAR-UI-ERR] Pipeline broken: {e}")
    finally:
        idle_task.cancel()
        worker_task.cancel()
        if websocket in active_connections:
            active_connections.remove(websocket)

def perform_hardware_warmup():
    """
    Executes a dummy inference pass on a real reference face image
    to ensure full VRAM cache compilation before the server accepts traffic.
    """
    print("[AVATAR-API] Triggering global GPU VRAM pre-compilation passes...")
    
    # 🔴 CHANGE THIS to the actual path of one of your reference/default faces
    warmup_path = "../../wwwroot/images/headshots/headshot_primary.png" 
    
    try:
        if not os.path.exists(warmup_path):
            print(f"[AVATAR-API-WARN] Warmup image missing at '{warmup_path}'.")
            print("[AVATAR-API-WARN] Please place a valid face image here to enable proper warmup.")
            return

        print(f"[AVATAR-API] Warming up extraction graphs using asset: {warmup_path}")
        
        # This will now detect a face and compile 100% of the TensorRT/ONNX VRAM cache
        avatar_pipeline.prepare_source(warmup_path)
        
        print("[AVATAR-API] Global hardware acceleration engine fully warmed up and HOT! 🔥")
    except Exception as warmup_err:
        print(f"[AVATAR-API-WARN] Warmup routine bypassed or failed: {warmup_err}")

if __name__ == "__main__":
    import uvicorn

    perform_hardware_warmup()
    
    print("[AVATAR-API] Starting server web workers...")
    uvicorn.run(app, host="127.0.0.1", port=8080)
