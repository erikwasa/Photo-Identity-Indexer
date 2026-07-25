# PhotoIdentity.Imaging.OpenCv

OpenCV-backed image decoding and transformation adapter.

## Public boundary

The module implements neutral `PhotoIdentity.Core` contracts and returns application-owned `ImageFrame` values. OpenCV `Mat` objects never cross the module boundary.

## Decoder behaviour

- Accepts JPEG and PNG content identified by file signature.
- Applies EXIF orientation during decoding.
- Converts decoded content to packed 8-bit BGR pixels.
- Optionally downsizes to fit `DecodeOptions.MaximumSize` without upscaling.
- Throws `ImageDecodingException` with `UnsupportedFormat` for other signatures.
- Throws `ImageDecodingException` with `CorruptMedia` when JPEG or PNG content cannot be decoded.
- Honours cancellation while reading the encoded stream.

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

HEIC and other formats remain future adapters rather than being hidden behind platform-specific behaviour in this module.

## Native runtime

The library references only the managed `OpenCvSharp4` package. A host or test project must select the correct native runtime package for its operating system. The current Windows test project uses `OpenCvSharp4.runtime.win`.
