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
    [InlineData("image/bmp", "BM....")]
    [InlineData("image/tiff", "II*\0....")]
    [InlineData("image/tiff", "MM\0*....")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.oasis.opendocument.text", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.oasis.opendocument.presentation", "PK\u0003\u0004rest")]
    [InlineData("application/msword", "\u00D0\u00CF\u0011\u00E0\u00A1\u00B1\u001A\u00E1rest")]
    [InlineData("application/vnd.ms-excel", "\u00D0\u00CF\u0011\u00E0\u00A1\u00B1\u001A\u00E1rest")]
    [InlineData("application/vnd.ms-powerpoint", "\u00D0\u00CF\u0011\u00E0\u00A1\u00B1\u001A\u00E1rest")]
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
    [InlineData("image/bmp", "BX....")]
    [InlineData("image/tiff", "II\0*....")]
    [InlineData("image/tiff", "MM*\0....")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "%PDF-1.7")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "%PDF-1.7")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", "%PDF-1.7")]
    [InlineData("application/vnd.oasis.opendocument.text", "%PDF-1.7")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", "%PDF-1.7")]
    [InlineData("application/vnd.oasis.opendocument.presentation", "%PDF-1.7")]
    [InlineData("application/msword", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.ms-excel", "PK\u0003\u0004rest")]
    [InlineData("application/vnd.ms-powerpoint", "PK\u0003\u0004rest")]
    [InlineData("text/plain", "a\0b")]
    [InlineData("text/csv", "a,b\u0001")]
    [InlineData("application/json", "{\"a\":\u001B}")]
    [InlineData("application/xml", "<a/>\0")]
    [InlineData("text/xml", "<a/>\0")]
    [InlineData("text/html", "<p>\u0002</p>")]
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
    [InlineData("text/plain", "a note\r\n\tline\f")]
    [InlineData("text/csv", "name,note\n")]
    [InlineData("application/json", "{\"a\": 1}")]
    [InlineData("application/xml", "<a>text</a>")]
    [InlineData("text/xml", "<a>text</a>")]
    [InlineData("text/html", "<p>hi</p>")]
    public void Text_without_control_bytes_matches_in_any_encoding_of_one_byte_characters(string mediaType, string head)
    {
        Assert.True(FileSignatures.Matches(mediaType, Encoding.Latin1.GetBytes(head)));
        Assert.True(FileSignatures.Matches(mediaType, Encoding.UTF8.GetBytes(head + " Tiếng Việt")));
        Assert.True(FileSignatures.Matches(mediaType, Encoding.Latin1.GetBytes(head + " caf\u00E9")));
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    [InlineData("application/x-unknown")]
    public void A_type_that_is_not_checked_matches_no_bytes_and_has_no_extension(string mediaType)
    {
        Assert.False(FileSignatures.IsChecked(mediaType));
        Assert.False(FileSignatures.Matches(mediaType, "anything"u8));
        Assert.False(FileSignatures.Matches(mediaType, []));
        Assert.False(FileSignatures.HasExtension(mediaType, "file.svg"));
        Assert.False(FileSignatures.HasExtension(mediaType, "file.bin"));
    }

    [Fact]
    public void Every_type_in_the_table_has_lower_case_extensions_that_start_with_a_dot()
    {
        Assert.NotEmpty(FileSignatures.Types);
        Assert.All(FileSignatures.Types, type =>
        {
            Assert.Equal(type.Key.ToLowerInvariant(), type.Key);
            Assert.NotEmpty(type.Value.Extensions);
            Assert.All(type.Value.Extensions, extension =>
            {
                Assert.StartsWith(".", extension, StringComparison.Ordinal);
                Assert.Equal(extension.ToLowerInvariant(), extension);
                Assert.True(extension.Length > 1);
            });
        });
    }

    [Fact]
    public void Every_type_accepts_each_of_its_extensions_in_any_letter_case()
    {
        foreach (var (mediaType, type) in FileSignatures.Types)
        {
            Assert.True(FileSignatures.IsChecked(mediaType.ToUpperInvariant()));
            foreach (var extension in type.Extensions)
            {
                Assert.True(FileSignatures.HasExtension(mediaType, $"invoice{extension}"), $"{mediaType} {extension}");
                Assert.True(FileSignatures.HasExtension(mediaType.ToUpperInvariant(), $"INVOICE{extension.ToUpperInvariant()}"), $"{mediaType} {extension}");
            }
        }
    }

    [Fact]
    public void Every_type_refuses_the_extension_of_another_type_and_a_name_without_an_extension()
    {
        foreach (var (mediaType, type) in FileSignatures.Types)
        {
            var other = FileSignatures.Types.Values.SelectMany(candidate => candidate.Extensions).First(extension => !type.Extensions.Contains(extension));
            Assert.False(FileSignatures.HasExtension(mediaType, $"invoice{other}"), $"{mediaType} {other}");
            Assert.False(FileSignatures.HasExtension(mediaType, "invoice"), mediaType);
            Assert.False(FileSignatures.HasExtension(mediaType, "invoice."), mediaType);
            Assert.False(FileSignatures.HasExtension(mediaType, type.Extensions[0].TrimStart('.')), mediaType);
        }
    }

    [Fact]
    public void A_png_name_is_refused_for_a_pdf()
    {
        Assert.False(FileSignatures.HasExtension("application/pdf", "invoice.png"));
        Assert.False(FileSignatures.HasExtension("application/pdf", "invoice.pdf.png"));
        Assert.True(FileSignatures.HasExtension("application/pdf", "invoice.png.pdf"));
    }
}
