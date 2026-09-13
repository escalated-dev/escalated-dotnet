using Escalated.Controllers;
using Escalated.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Moq;

namespace Escalated.Tests.Controllers;

public class AttachmentControllerTests
{
    private static AttachmentController NewController(out Data.EscalatedDbContext db)
    {
        db = TestHelpers.CreateInMemoryDb();
        return new AttachmentController(db, TestHelpers.DefaultOptions(), StaffAuthorization());
    }

    /// <summary>
    /// These tests are about storage paths, so the caller is staff, who may download
    /// any attachment. Who may download what is covered in Hosting/AuthorizationTests.
    /// </summary>
    private static IAuthorizationService StaffAuthorization()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        return authorization.Object;
    }

    [Fact]
    public async Task Download_RemoteDiskRedirectsToHttpUrl()
    {
        var controller = NewController(out var db);
        db.Attachments.Add(new Attachment
        {
            Filename = "report.pdf",
            Disk = "s3",
            Path = "https://cdn.example.com/report.pdf",
        });
        await db.SaveChangesAsync();

        var result = await controller.Download(1);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://cdn.example.com/report.pdf", redirect.Url);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,unsafe")]
    [InlineData("//cdn.example.com/report.pdf")]
    [InlineData("/support/attachments/2/download")]
    public async Task Download_RemoteDiskRejectsUnsafeRedirectUrl(string path)
    {
        var controller = NewController(out var db);
        db.Attachments.Add(new Attachment
        {
            Filename = "report.pdf",
            Disk = "s3",
            Path = path,
        });
        await db.SaveChangesAsync();

        var result = await controller.Download(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
