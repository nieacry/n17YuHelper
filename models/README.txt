Models for n17 YuHelper. They are NOT copied into publish\ or versions\.
The app finds this folder automatically (next to the exe or up to 3 levels above).

Download everything with:  tools\setup-models.cmd

ocr\paddleocr-vl-1.6\        text recognition (v0.2):
    PaddleOCR-VL-1.6-GGUF.gguf           ~936 MB
    PaddleOCR-VL-1.6-GGUF-mmproj.gguf    ~882 MB
    chat_template.jinja
    source: https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.6-GGUF

llm\gemma-4-12b-qat\         translation and grammar (v0.3):
    gemma-4-12b-it-qat-q4_0.gguf         ~7 GB
    source: https://huggingface.co/google/gemma-4-12B-it-qat-q4_0-gguf
llm\qwen3.5-9b\              second model for comparison (v0.6)

Tip: if this folder is on a network drive, loading is slow. Put the models on a
local SSD and set "ModelsPath" in settings.json to that folder.
