using Escalated.Configuration;
using Escalated.Controllers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Controllers;

public class ApiAuthControllerTests
{
    private static ApiAuthController Build(ApiAuthOptions auth, string? bearer = null)
    {
        var options = Options.Create(new EscalatedOptions { ApiAuth = auth });
        var ctrl = new ApiAuthController(options);
        var http = new DefaultHttpContext();
        if (bearer is not null)
        {
            http.Request.Headers.Authorization = $"Bearer {bearer}";
        }

        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    [Fact]
    public async Task Login_Returns501_WhenUnconfigured()
    {
        var ctrl = Build(new ApiAuthOptions());

        var result = await ctrl.Login(new Dictionary<string, object?>());

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(501, obj.StatusCode);
    }

    [Fact]
    public async Task Login_DelegatesToAuthenticator()
    {
        var auth = new ApiAuthOptions
        {
            Authenticate = body => Task.FromResult<Dictionary<string, object?>?>(
                new Dictionary<string, object?> { ["token"] = "abc", ["email"] = body["email"] }),
        };
        var ctrl = Build(auth);

        var result = await ctrl.Login(new Dictionary<string, object?> { ["email"] = "a@b.com" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task Login_Returns401_WhenCallbackReturnsNull()
    {
        var auth = new ApiAuthOptions
        {
            Authenticate = _ => Task.FromResult<Dictionary<string, object?>?>(null),
        };
        var ctrl = Build(auth);

        var result = await ctrl.Login(new Dictionary<string, object?>());

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Me_ForwardsBearerToken()
    {
        string? seen = null;
        var auth = new ApiAuthOptions
        {
            Validate = token =>
            {
                seen = token;
                return Task.FromResult<Dictionary<string, object?>?>(new Dictionary<string, object?> { ["id"] = 7 });
            },
        };
        var ctrl = Build(auth, bearer: "tok123");

        var result = await ctrl.Me();

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("tok123", seen);
    }

    [Fact]
    public async Task Logout_AlwaysSucceeds()
    {
        var ctrl = Build(new ApiAuthOptions(), bearer: "x");

        var result = await ctrl.Logout();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task Validate_RequiresToken()
    {
        var ctrl = Build(new ApiAuthOptions
        {
            Validate = _ => Task.FromResult<Dictionary<string, object?>?>(new Dictionary<string, object?>()),
        });

        var result = await ctrl.Validate(new Dictionary<string, object?>());

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
