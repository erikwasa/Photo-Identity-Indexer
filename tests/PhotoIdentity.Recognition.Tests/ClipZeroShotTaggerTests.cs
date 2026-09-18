using PhotoIdentity.Recognition.Onnx.Semantic;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class ClipZeroShotTaggerTests
{
    [Fact]
    public void Tokenizer_applies_byte_bpe_lowercasing_and_special_tokens()
    {
        ClipBpeTokenizer tokenizer = ClipBpeTokenizer.CreateForTests(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["<|startoftext|>"] = 100,
                ["<|endoftext|>"] = 101,
                ["cat</w>"] = 7,
            },
            [
                ("c", "a"),
                ("ca", "t</w>"),
            ]);

        int[] encoded = tokenizer.Encode("  CAT  ", maximumLength: 77);

        Assert.Equal([100, 7, 101], encoded);
    }

    [Fact]
    public void Tokenizer_truncates_content_but_always_preserves_end_token()
    {
        ClipBpeTokenizer tokenizer = ClipBpeTokenizer.CreateForTests(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["<|startoftext|>"] = 100,
                ["<|endoftext|>"] = 101,
                ["a</w>"] = 1,
            },
            [
                ("x", "y"),
            ]);

        int[] encoded = tokenizer.Encode("a a a a a", maximumLength: 4);

        Assert.Equal([100, 1, 1, 101], encoded);
    }

    [Fact]
    public void Tokenizer_decodes_html_before_controlled_prompt_tokenization()
    {
        ClipBpeTokenizer tokenizer = ClipBpeTokenizer.CreateForTests(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["<|startoftext|>"] = 100,
                ["<|endoftext|>"] = 101,
                ["a</w>"] = 1,
                ["&</w>"] = 2,
                ["b</w>"] = 3,
            },
            [
                ("x", "y"),
            ]);

        int[] encoded = tokenizer.Encode("A &amp; B", maximumLength: 77);

        Assert.Equal([100, 1, 2, 3, 101], encoded);
    }
}
