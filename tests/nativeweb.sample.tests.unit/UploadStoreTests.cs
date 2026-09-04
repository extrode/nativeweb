using System.Reflection;
using System.Text;

using Microsoft.AspNetCore.Http;

using NativeWeb.Forms;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// <see cref="UploadStore"/> against a real directory, spec 027 R11 and R13.
/// </summary>
/// <remarks>
/// The integration tier drives the store through the endpoint, which is where the
/// happy path and the traversal fact belong. What is here is what an endpoint cannot
/// reach: a store that is full, a configured directory written with a trailing
/// separator, and a content type carrying a parameter.
/// </remarks>
public sealed class UploadStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "nativeweb-upload-store-" + Guid.NewGuid().ToString("n"));

    public UploadStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// A part may declare <c>image/png; charset=binary</c>, which is well formed. The
    /// binder strips the parameter before checking the accepted set, so a part like
    /// that is admitted; the store has to strip it too or the lookup misses and the
    /// throw that documents itself as unreachable becomes a 500 on a valid upload.
    /// </summary>
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/png; charset=binary")]
    [InlineData("IMAGE/PNG")]
    public async Task A_declared_content_type_carrying_a_parameter_still_finds_its_extension(string contentType)
    {
        var store = new UploadStore(_directory);

        string? stored = await store.StoreAsync(File("holiday.png", contentType, "bytes"), CancellationToken.None);

        Assert.NotNull(stored);
        Assert.EndsWith(".png", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_store_refuses_rather_than_growing()
    {
        for (int i = 0; i < UploadStore.MaxStoredFiles; i++)
        {
            await System.IO.File.WriteAllTextAsync(Path.Combine(_directory, $"filler-{i}.png"), "x");
        }

        var store = new UploadStore(_directory);

        Assert.Null(await store.StoreAsync(File("holiday.png", "image/png", "bytes"), CancellationToken.None));
        Assert.Equal(UploadStore.MaxStoredFiles, Directory.EnumerateFiles(_directory).Count());
    }

    /// <summary>
    /// <c>Path.GetDirectoryName</c> never returns a trailing separator and
    /// <c>Path.GetFullPath</c> keeps the one the configuration was written with, so
    /// comparing them raw makes every stored picture a 404 on a host whose configured
    /// directory happens to end in a slash.
    /// </summary>
    [Fact]
    public async Task A_directory_configured_with_a_trailing_separator_still_serves_what_it_stored()
    {
        var writer = new UploadStore(_directory);
        string? stored = await writer.StoreAsync(File("holiday.png", "image/png", "bytes"), CancellationToken.None);

        var reader = new UploadStore(_directory + Path.DirectorySeparatorChar);

        Assert.NotNull(reader.Resolve(stored, out string contentType));
        Assert.Equal("image/png", contentType);
    }

    [Fact]
    public void An_id_the_store_never_wrote_reports_no_content_type()
    {
        var store = new UploadStore(_directory);

        Assert.Null(store.Resolve(new string('a', 32) + ".png", out string contentType));
        Assert.Equal("", contentType);
    }

    /// <summary>
    /// <c>FileMode.CreateNew</c> has created the file before the copy can fail, and a
    /// client hanging up mid-upload is the ordinary way it fails. Left behind, the
    /// truncated picture is one the serve route would hand out.
    /// </summary>
    [Fact]
    public async Task A_copy_that_fails_leaves_nothing_behind()
    {
        var store = new UploadStore(_directory);

        await Assert.ThrowsAsync<IOException>(
            () => store.StoreAsync(Unreadable("holiday.png", "image/png"), CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    /// <summary>
    /// <c>UploadForm</c> repeats the accepted set as attribute literals, because an
    /// attribute argument has to be a constant and <see cref="UploadStore.Accepted"/>
    /// is not one. Nothing but this compares the two, and they have to agree: a type
    /// the attribute admits and the table has no extension for is a 500 on a valid
    /// upload, and one the table has and the attribute refuses is unreachable.
    /// </summary>
    [Fact]
    public void The_declared_accept_set_and_the_extension_table_name_the_same_types()
    {
        string[] declared = typeof(UploadForm)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<FormFileAttribute>())
            .Single(attribute => attribute is not null)!
            .Accept!;

        Assert.Equal(
            UploadStore.Accepted.OrderBy(type => type, StringComparer.Ordinal),
            declared.OrderBy(type => type, StringComparer.Ordinal));
    }

    private static UploadedFile File(string fileName, string contentType, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);

        return Bind(new FormFile(new MemoryStream(bytes), 0, bytes.Length, "picture", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        });
    }

    private static UploadedFile Unreadable(string fileName, string contentType) =>
        Bind(new FormFile(new FailingStream(), 0, 4, "picture", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        });

    // The only public route to an UploadedFile: its constructor is internal to
    // NativeWeb.Forms, and the binder is what a real request goes through anyway.
    private static UploadedFile Bind(FormFile file) =>
        FormBinding.RequiredFile(
            new FormCollection(null, new FormFileCollection { file }), "picture", null, 4096L);

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        // Seekable, because FormFile.OpenReadStream sets Position before handing the
        // stream over. The failure this fixture stages is the read, not the seek.
        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => 4;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("the client hung up");

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
