# Baseline models

## Detector: YuNet

YuNet is the first face detector because it is lightweight, ONNX-compatible, provides five-point landmarks and has permissive model licensing.

## Embedder: SFace

SFace is the first face embedder because it is lightweight, ONNX-compatible, works with five-point alignment and can run on CPU during the local vertical slice.

## Runtime

Use ONNX Runtime from C#. CPU inference is the default; GPU execution is an optional deployment choice using the same model contract.

The baseline is a starting point, not a permanent selection. A model becomes production-selected only after reproducible evaluation on the private dataset, licence review, throughput measurement and reprocessing proof.
