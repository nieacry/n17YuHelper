# Third-party components

n17 YuHelper is MIT-licensed. It uses or downloads the following third-party components,
each under its own license. Models and llama.cpp are **not** bundled: the app downloads them
from their original locations on first run.

| Component | Used for | License | Source |
|---|---|---|---|
| llama.cpp (llama-server) | Running local models | MIT | https://github.com/ggml-org/llama.cpp |
| NVIDIA CUDA runtime (cudart, cuBLAS) | GPU acceleration (NVIDIA only, shipped by llama.cpp releases) | NVIDIA CUDA EULA | https://docs.nvidia.com/cuda/eula/ |
| Gemma 4 models | Translation, grammar, voice | Apache 2.0 | https://huggingface.co/google |
| PaddleOCR-VL 1.6 | Text recognition | Apache 2.0 | https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.6-GGUF |
| Qwen3.5 (optional) | Alternative translation model | Apache 2.0 | https://huggingface.co/Qwen |
| ZXing.Net | QR / barcode reading | Apache 2.0 | https://github.com/micjahn/ZXing.Net |
| PdfPig | PDF text extraction | Apache 2.0 | https://github.com/UglyToad/PdfPig |
