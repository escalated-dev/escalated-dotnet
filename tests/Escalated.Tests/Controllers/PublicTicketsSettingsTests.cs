using Escalated.Controllers.Admin;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Escalated.Tests.Controllers;

/// <summary>
/// Tests for the two public-tickets settings endpoints on
/// <see cref="AdminSettingsController"/>. The controller delegates all
/// persistence to <see cref="SettingsService"/>; these tests exercise
/// the round-trip (write via PUT → read via GET) end-to-end with an
/// in-memory DB to prove validation, mode-switch cleanup, and default
/// values work together.
/// </summary>
public class PublicTicketsSettingsTests
{
    private static AdminSettingsController NewController(out SettingsService settings)
    {
        var db = TestHelpers.CreateInMemoryDb();
        settings = new SettingsService(db);
        var audit = new AuditLogService(db);
        return new AdminSettingsController(db, audit, settings);
    }

    [Fact]
    public async Task Get_DefaultsToUnassigned_WhenNoSettingsWritten()
    {
        var controller = NewController(out _);

        var result = await controller.GetPublicTicketsSettings();

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        AssertPolicy(value, "unassigned", null, "");
    }

    [Fact]
    public async Task Put_PersistsMode_GuestUser_AndClearsOtherFields()
    {
        var controller = NewController(out _);

        var result = await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "guest_user",
            GuestPolicyUserId: 42,
            GuestPolicySignupUrlTemplate: "https://example.com/signup"));

        var ok = Assert.IsType<OkObjectResult>(result);
        AssertPolicy(ok.Value!, "guest_user", 42, "");
    }

    [Fact]
    public async Task Put_PersistsMode_PromptSignup_AndClearsGuestUserId()
    {
        var controller = NewController(out _);

        var result = await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "prompt_signup",
            GuestPolicyUserId: 99,
            GuestPolicySignupUrlTemplate: "https://example.com/join?t={{token}}"));

        var ok = Assert.IsType<OkObjectResult>(result);
        AssertPolicy(ok.Value!, "prompt_signup", null, "https://example.com/join?t={{token}}");
    }

    [Fact]
    public async Task Put_SwitchFromGuestUserToUnassigned_ClearsUserId()
    {
        var controller = NewController(out _);

        await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "guest_user", GuestPolicyUserId: 42, GuestPolicySignupUrlTemplate: null));

        var result = await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "unassigned", GuestPolicyUserId: null, GuestPolicySignupUrlTemplate: null));

        var ok = Assert.IsType<OkObjectResult>(result);
        AssertPolicy(ok.Value!, "unassigned", null, "");
    }

    [Fact]
    public async Task Put_CoercesUnknownMode_ToUnassigned()
    {
        var controller = NewController(out _);

        var result = await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "bogus-value",
            GuestPolicyUserId: 1,
            GuestPolicySignupUrlTemplate: "ignored"));

        var ok = Assert.IsType<OkObjectResult>(result);
        AssertPolicy(ok.Value!, "unassigned", null, "");
    }

    [Fact]
    public async Task Put_TruncatesLongSignupTemplate_At500Chars()
    {
        var controller = NewController(out _);
        var longTemplate = new string('x', 1000);

        var result = await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "prompt_signup",
            GuestPolicyUserId: null,
            GuestPolicySignupUrlTemplate: longTemplate));

        var ok = Assert.IsType<OkObjectResult>(result);
        var template = GetProp<string>(ok.Value!, "guest_policy_signup_url_template");
        Assert.Equal(500, template.Length);
    }

    [Fact]
    public async Task Put_GuestUserMode_IgnoresZeroUserId_AsEmpty()
    {
        var controller = NewController(out _);

        var result = await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "guest_user",
            GuestPolicyUserId: 0,
            GuestPolicySignupUrlTemplate: null));

        var ok = Assert.IsType<OkObjectResult>(result);
        // guest_policy_user_id is stored as empty string, surfaces as null in the GET payload.
        AssertPolicy(ok.Value!, "guest_user", null, "");
    }

    [Fact]
    public async Task Get_ReflectsLatestWrite_AfterMultipleUpdates()
    {
        var controller = NewController(out _);

        await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "guest_user", GuestPolicyUserId: 7, GuestPolicySignupUrlTemplate: null));

        await controller.UpdatePublicTicketsSettings(new PublicTicketsSettingsRequest(
            GuestPolicyMode: "guest_user", GuestPolicyUserId: 15, GuestPolicySignupUrlTemplate: null));

        var result = await controller.GetPublicTicketsSettings();
        var ok = Assert.IsType<OkObjectResult>(result);
        AssertPolicy(ok.Value!, "guest_user", 15, "");
    }

    private static void AssertPolicy(object payload, string expectedMode, int? expectedUserId, string expectedTemplate)
    {
        Assert.Equal(expectedMode, GetProp<string>(payload, "guest_policy_mode"));
        Assert.Equal(expectedUserId, GetProp<int?>(payload, "guest_policy_user_id"));
        Assert.Equal(expectedTemplate, GetProp<string>(payload, "guest_policy_signup_url_template"));
    }

    private static T GetProp<T>(object source, string name)
    {
        var prop = source.GetType().GetProperty(name)
            ?? throw new Xunit.Sdk.XunitException($"Anonymous payload missing property '{name}'");
        return (T)prop.GetValue(source)!;
    }
}
