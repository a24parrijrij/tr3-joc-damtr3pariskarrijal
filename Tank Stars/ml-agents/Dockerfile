FROM python:3.10.12-slim

# torch>=2.1.1 required by mlagents==1.1.0
# torch<2.5   uses the old ONNX export path (no onnxscript needed)
# pip will install 2.4.1 which is the sweet spot
RUN pip install --no-cache-dir \
    "torch>=2.1.1,<2.5" \
    mlagents==1.1.0 \
    protobuf==3.20.3

WORKDIR /workspace

VOLUME ["/workspace/results"]

EXPOSE 5004

CMD ["bash"]
