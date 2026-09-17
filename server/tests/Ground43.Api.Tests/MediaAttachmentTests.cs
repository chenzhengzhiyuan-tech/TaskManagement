using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ground43.Api.Tests;

public class MediaAttachmentTests
{
    private static async Task Login(HttpClient client, string account = "admin")
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account, password = "demo123" });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
    }
    private static byte[] Bytes(string extension) => extension switch
    {
        "mp4" => [0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 109, 112, 52, 50],
        "webm" => [0x1A, 0x45, 0xDF, 0xA3, 0x87, 0x42, 0x82, 0x84, 0x77, 0x65, 0x62, 0x6D],
        _ => "GIF89a000000"u8.ToArray()
    };
    private static async Task<HttpResponseMessage> Upload(HttpClient client, string ext, string type, byte[] bytes, bool comment = false)
    {
        var created = await client.PostAsJsonAsync("/api/requirements/REQ-0048/attachments/uploads", new CreateUploadRequest("sample." + ext, type, bytes.Length, null, comment));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<CreateUploadResponse>())!;
        (await client.PutAsync($"/api/attachments/uploads/{session.UploadId}/chunks/0", new ByteArrayContent(bytes))).EnsureSuccessStatusCode();
        return await client.PostAsync($"/api/attachments/uploads/{session.UploadId}/complete", null);
    }
    [Theory]
    [InlineData("mp4", "video/mp4")]
    [InlineData("webm", "video/webm")]
    [InlineData("gif", "image/gif")]
    public async Task Upload_RangeDownload_Permissions_AndDelete(string ext, string type)
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await Login(client);
        var bytes = Bytes(ext);
        var response = await Upload(client, ext, type, bytes); response.EnsureSuccessStatusCode();
        var attachment = (await response.Content.ReadFromJsonAsync<CompleteUploadResponse>())!.Attachment;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{attachment.Id}");
        request.Headers.Range = new RangeHeaderValue(0, 5);
        var partial = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(type, partial.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes[..6], await partial.Content.ReadAsByteArrayAsync());
        await Login(client, "member-a");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/attachments/{attachment.Id}")).StatusCode);
        await Login(client);
        (await client.DeleteAsync($"/api/attachments/{attachment.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attachments/{attachment.Id}")).StatusCode);
    }
    [Theory]
    [InlineData("mp4", "video/mp4", 100)]
    [InlineData("webm", "video/webm", 100)]
    [InlineData("gif", "image/gif", 20)]
    public async Task LimitsAndSpoofedFilesAreRejected(string ext, string type, int mb)
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await Login(client);
        var exact = new CreateUploadRequest("sample." + ext, type, mb * 1024L * 1024, null);
        (await client.PostAsJsonAsync("/api/requirements/REQ-0048/attachments/uploads", exact)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/requirements/REQ-0048/attachments/uploads", exact with { TotalSize = exact.TotalSize + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/requirements/REQ-0048/attachments/uploads", exact with { FileName = "sample.avi" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, ext, type, "this is not media"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, ext, type, [0, 1])).StatusCode);
    }
    [Fact]
    public async Task CommentVideoIsDeletedWithCommentOnlyByAuthorOrAdmin()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await Login(client, "member-a");
        var upload = await Upload(client, "mp4", "video/mp4", Bytes("mp4"), true); upload.EnsureSuccessStatusCode();
        var attachment = (await upload.Content.ReadFromJsonAsync<CompleteUploadResponse>())!.Attachment;
        var response = await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", new CreateCommentRequest("", Guid.NewGuid(), AttachmentIds: [Guid.Parse(attachment.Id)]));
        response.EnsureSuccessStatusCode(); var comment = (await response.Content.ReadFromJsonAsync<CommentDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/attachments/{attachment.Id}")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var path = await db.Attachments.Where(x => x.Id == Guid.Parse(attachment.Id)).Select(x => x.RelativePath).SingleAsync();
        await Login(client, "member-b");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/requirements/REQ-0048/comments/{comment.Id}")).StatusCode);
        await Login(client, "member-a");
        (await client.DeleteAsync($"/api/requirements/REQ-0048/comments/{comment.Id}")).EnsureSuccessStatusCode();
        Assert.False(File.Exists(Path.Combine(factory.StorageRoot, path)));
        Assert.False(await db.Comments.AnyAsync(x => x.Id == Guid.Parse(comment.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attachments/{attachment.Id}")).StatusCode);
    }
}
