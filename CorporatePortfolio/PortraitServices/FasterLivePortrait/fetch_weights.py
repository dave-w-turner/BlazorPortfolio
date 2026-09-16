import os
import subprocess

# 1. Ensure the official Hugging Face Downloader library is installed
try:
    import huggingface_hub
except ImportError:
    print("[AVATAR-API] Installing huggingface_hub client core module package library...")
    subprocess.check_call(["pip", "install", "huggingface_hub"])
    from huggingface_hub import snapshot_download

from huggingface_hub import snapshot_download

# 2. Build the exact target destination tree on your Alienware's local disk
local_dir = os.path.abspath("checkpoints")
os.makedirs(local_dir, exist_ok=True)

print(f"[AVATAR-API] Initializing official API snapshot stream download...")
print(f"[AVATAR-API] Target local path directory: {local_dir}")

try:
    # 3. Request the complete model snapshot bundle using the verified repository signature ID
    # This automatically tracks files like warping_spade, extraction layers, etc.
    snapshot_download(
        repo_id="warmshao/FasterLivePortrait",
        local_dir=local_dir,
        allow_patterns=["*liveportrait_onnx/*", "*LivePortrait_onnx/*", "*.onnx"],
        ignore_patterns=["*.trt", "*.engine"] # Skip massive TensorRT binaries you don't need for ONNX
    )
    print("[AVATAR-API] Success! All required weight profiles synchronized perfectly.")
    
except Exception as e:
    print(f"[AVATAR-API-CRITICAL-ERR] Snapshot stream deployment failed: {str(e)}")
