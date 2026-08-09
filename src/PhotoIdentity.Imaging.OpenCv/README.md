# PhotoIdentity.Imaging.OpenCv

Image decoding and OpenCV-backed transformation adapter.

## Public boundary

The module implements neutral `PhotoIdentity.Core` contracts and returns application-owned `ImageFrame` values. OpenCV `Mat` and ImageMagick types never cross the module boundary.

## Decoder behaviour

- Accepts JPEG, PNG and HEIC/HEIF content identified by file signature/container brand.
- Keeps the established OpenCV JPEG/PNG decode path.
- Uses the bundled Magick.NET HEIF delegate for HEIC/HEIF, applies orientation, strips metadata from the intermediate render and returns the same packed BGR contract as other formats.
- Converts decoded content to packed 8-bit BGR pixels.
- Optionally downsizes to fit `DecodeOptions.MaximumSize` without upscaling.
- Throws `ImageDecodingException` with `UnsupportedFormat` for other signatures.
- Throws `ImageDecodingException` with `CorruptMedia` when recognized image content cannot be decoded.
- Honours cancellation while reading the encoded stream.

RAW formats are deliberately not accepted merely by extension. WI-0053 adds a RAW variant only after that format is found in the real archive and representative private input has established its rendering, orientation and resource-use policy.

## Review proxies

`OpenCvReviewProxyRenderer` uses the same `OpenCvImageDecoder` path before producing the configured metadata-free JPEG derivative. HEIC therefore has one rendered-pixel interpretation for archive analysis and normal review-proxy generation rather than separate format-specific implementations.

## Face crops

`OpenCvFaceCropper` creates review crops from normalised detected boxes. Padding is applied relative to the detected width and height, rounded outwards and clamped to the decoded image bounds. Results contain packed pixels, the exact source rectangle and a canonical SHA-256 content digest that is independent of source-row padding.

## Five-point alignment

`OpenCvFaceAligner` implements `IFaceAligner` for `sface-five-point-v1`:

- output is always 112×112;
- the OpenCV SFace five-point reference template is used;
- anatomical left/right landmarks from the core contract are reordered explicitly for the SFace template;
- a deterministic orientation-preserving similarity transform is applied with bilinear interpolation;
- degenerate landmarks and unsupported protocol IDs fail explicitly;
- pixels outside the source image use constant zero boundary fill.

## Native runtime

The library references managed OpenCvSharp plus the x64 Magick.NET package that carries its native ImageMagick runtime. A host or test project must still select the correct OpenCvSharp native runtime package for its operating system. The current application and CI target Windows x64; the current Windows test project uses `OpenCvSharp4.runtime.win`.
