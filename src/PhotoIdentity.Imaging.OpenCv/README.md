# PhotoIdentity.Imaging.OpenCv

OpenCV-backed image decoding and transformation adapter.

## Public boundary

The module implements `IImageDecoder` from `PhotoIdentity.Core` and returns neutral `ImageFrame` values. OpenCV `Mat` objects never cross the module boundary.

## Current decoder behaviour

- Accepts JPEG and PNG content identified by file signature.
- Applies EXIF orientation during decoding.
- Converts decoded content to packed 8-bit BGR pixels.
- Optionally downsizes to fit `DecodeOptions.MaximumSize` without upscaling.
- Throws `ImageDecodingException` with `UnsupportedFormat` for other signatures.
- Throws `ImageDecodingException` with `CorruptMedia` when JPEG or PNG content cannot be decoded.
- Honours cancellation while reading the encoded stream.

HEIC and other formats remain future adapters rather than being hidden behind platform-specific behaviour in this module.

## Native runtime

The library references only the managed `OpenCvSharp4` package. A host or test project must select the correct native runtime package for its operating system. The current Windows test project uses `OpenCvSharp4.runtime.win`.
