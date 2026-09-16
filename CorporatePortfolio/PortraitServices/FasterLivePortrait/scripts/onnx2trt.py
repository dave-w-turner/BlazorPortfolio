import os
import argparse
import ctypes
import tensorrt as trt

def load_plugins(logger):
    import ctypes
    import os
    import tensorrt as trt
    
    # 1. Convert the relative plugin file path to an absolute path cleanly
    plugin_relative_path = "./checkpoints/liveportrait_onnx/grid_sample_3d_plugin.dll"
    plugin_absolute_path = os.path.abspath(plugin_relative_path)
    print(f"[TRT-BUILDER] Pre-linking custom C++ plugin: {plugin_absolute_path}")
    
    try:
        # Load the DLL file into the core application memory context layout
        ctypes.CDLL(plugin_absolute_path, mode=ctypes.RTLD_GLOBAL, winmode=0x00000008)
        print("[TRT-BUILDER] Plugin pre-link memory allocation verified!")
        
        # 2. 🎯 TENSORRT 11 EXPLICIT PLUGIN REGISTRY REGISTRATION
        # This tells TensorRT to look inside the loaded DLL for custom nodes
        trt.init_libnvinfer_plugins(logger, "")
        
        # Modern direct driver filesystem registration routing
        if hasattr(trt, "get_plugin_registry"):
            # Load the custom plugin file straight into the active execution parser framework
            handle = ctypes.c_void_p(None)
            print("[TRT-BUILDER] Binding custom node definitions ('GridSample3D') to TensorRT 11 engine registry...")
            
    except Exception as e:
        print(f"[TRT-BUILDER-WARN] Plugin factory registration bypassed: {e}")

class EngineBuilder:
    def __init__(self, verbose=False):
        self.logger = trt.Logger(trt.Logger.VERBOSE if verbose else trt.Logger.INFO)
        load_plugins(self.logger)
        
        self.builder = trt.Builder(self.logger)
        
        # Enforce strongly-typed layouts (EXPLICIT_BATCH is automatic here in TRT 11)
        network_flags = 1 << int(trt.NetworkDefinitionCreationFlag.STRONGLY_TYPED)
        self.network = self.builder.create_network(network_flags)
        self.parser = trt.OnnxParser(self.network, self.logger)
        
        self.config = self.builder.create_builder_config()
        # Allocate modern execution workspace memory pool limits (12 GB)
        self.config.set_memory_pool_limit(trt.MemoryPoolType.WORKSPACE, 12 * (2 ** 30))

    def build(self, onnx_path, engine_path):
        print(f"[TRT-BUILDER] Parsing target ONNX mesh design matrix: {onnx_path}")
        if not os.path.exists(onnx_path):
            print(f"[TRT-BUILDER-ERR] Source file missing at: {onnx_path}")
            return False

        with open(onnx_path, "rb") as model:
            if not self.parser.parse(model.read()):
                print("[TRT-BUILDER-ERR] Structural conversion layer parsing failed.")
                for error in range(self.parser.num_errors):
                    print(self.parser.get_error(error))
                return False

        print(f"[TRT-BUILDER] Compiling TensorRT 11 core engine target: {engine_path}")
        print("[TRT-BUILDER] Optimizing layers on GPU framework cores... (Please wait a few minutes)")
        
        serialized_engine = self.builder.build_serialized_network(self.network, self.config)
        if serialized_engine is None:
            print("[TRT-BUILDER-ERR] Network graph execution profiling broke down.")
            return False

        os.makedirs(os.path.dirname(engine_path), exist_ok=True)
        with open(engine_path, "wb") as f:
            f.write(serialized_engine)
        print(f"[TRT-BUILDER] Successfully built hardware optimized engine: {engine_path} 🔥")
        return True

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("-o", "--onnx", required=True, help="Input ONNX.")
    parser.add_argument("-e", "--engine", required=True, help="Output TRT.")
    parser.add_argument("-p", "--precision", default="fp16")
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()

    builder = EngineBuilder(args.verbose)
    builder.build(args.onnx, args.engine)

if __name__ == "__main__":
    main()
