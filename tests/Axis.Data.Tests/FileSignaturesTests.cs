using System.Text;
using Axis.Data.Files;

namespace Axis.Data.Tests;

public sealed class FileSignaturesTests
{
    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01 })]
    [InlineData(new byte[] { 0xFE, 0xED, 0xFA, 0xCE })]
    [InlineData(new byte[] { 0xFE, 0xED, 0xFA, 0xCF })]
    [InlineData(new byte[] { 0xCE, 0xFA, 0xED, 0xFE })]
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE })]
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE })]
    public void An_executable_start_is_found(byte[] head)
    {
        Assert.True(FileSignatures.IsExecutable(head));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x4D })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]
    [InlineData(new byte[] { 0x00, 0x4D, 0x5A })]
    public void Other_bytes_are_no_executable_start(byte[] head)
    {
        Assert.False(FileSignatures.IsExecutable(head));
    }

    [Theory]
    [InlineData("application/pdf", "%PDF-1.7")]
    [InlineData("APPLICATION/PDF", "%PDF-1.7")]
    [InlineData("image/gif", "GIF87a....")]
    [InlineData("image/gif", "GIF89a....")]
    [InlineData("image/webp", "RIFF\0\0\0\0WEBPVP8 ")]
    [InlineData("application/zip", "PK\u0003\u0004rest")]
    [InlineData("application/zip", "PK\u0005\u0006rest")]
    [InlineData("application/zip", "PK\u0007\u0008rest")]
    public void Bytes_that_start_as_the_declared_type_match(string mediaType, string head)
    {
        Assert.True(FileSignatures.Matches(mediaType, Encoding.Latin1.GetBytes(head)));
    }

    [Fact]
    public void A_png_and_a_jpeg_match_their_starts()
    {
        Assert.True(FileSignatures.Matches("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }));
        Assert.True(FileSignatures.Matches("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
    }

    [Theory]
    [InlineData("application/pdf", "PDF-1.7")]
    [InlineData("application/pdf", "")]
    [InlineData("image/gif", "GIF88a....")]
    [InlineData("image/webp", "RIFF\0\0\0\0WAVEfmt ")]
    [InlineData("image/webp", "RIFF\0\0\0")]
    [InlineData("application/zip", "PK\u0001\u0002rest")]
    public void Bytes_that_do_not_start_as_the_declared_type_do_not_match(string mediaType, string head)
    {
        Assert.False(FileSignatures.Matches(mediaType, Encoding.Latin1.GetBytes(head)));
    }

    [Fact]
    public void Other_bytes_do_not_match_a_png_or_a_jpeg()
    {
        Assert.False(FileSignatures.Matches("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        Assert.False(FileSignatures.Matches("image/jpeg", new byte[] { 0xFF, 0xD8, 0x00 }));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("application/vnd.ms-excel")]
    public void A_type_without_a_known_start_matches_any_bytes(string mediaType)
    {
        Assert.True(FileSignatures.Matches(mediaType, "anything"u8));
        Assert.True(FileSignatures.Matches(mediaType, []));
    }
}
