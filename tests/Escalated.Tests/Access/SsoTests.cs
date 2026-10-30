using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using Escalated.Authorization;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Access;

/// <summary>
/// Single sign-on (ADR 2026-10-29-configurable-access-features, port test checklist), through the
/// real pipeline, with a test SAML IdP (a generated certificate), a test OAuth provider and HS256 JWTs.
/// </summary>
public class SsoTests
{
    private const string Base = "http://localhost";
    private const string Acs = Base + "/support/sso/saml/acs";
    private const string Sp = Base + "/support/sso/saml/metadata";
    private const string Issuer = "https://idp.example.com/saml";
    private const string JwtSecret = "a-test-jwt-secret-that-is-long-enough-for-hs256!";

    private static readonly X509Certificate2 IdpCertificate = Certificate("idp");
    private static readonly X509Certificate2 OtherCertificate = Certificate("other");

    private static X509Certificate2 Certificate(string name)
    {
        var request = new CertificateRequest($"CN=escalated-test-{name}", RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
    }

    private static string Pem(X509Certificate2 certificate) =>
        "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(certificate.RawData, Base64FormattingOptions.InsertLineBreaks) + "\n-----END CERTIFICATE-----";

    /// <summary>A host whose users are 'known@example.com' (id 10) and anyone created; sign-ins are recorded.</summary>
    private sealed class Host
    {
        public required EscalatedTestHost Server { get; init; }
        public List<string> SignedIn { get; } = new();
        public Dictionary<string, string> Users { get; } = new(StringComparer.OrdinalIgnoreCase) { ["known@example.com"] = "10" };
        public Action<Escalated.Configuration.EscalatedOptions> Options { get; set; } = _ => { };

        public async Task SettingsAsync(string provider, Dictionary<string, string> values)
        {
            await Server.SeedAsync(async db =>
            {
                foreach (var (key, value) in values.Append(new("sso_provider", provider)))
                {
                    db.Settings.RemoveRange(db.Settings.Where(s => s.Key == key));
                    db.Settings.Add(new EscalatedSettings { Key = key, Value = value });
                }

                await db.SaveChangesAsync();
            });
        }
    }

    private static async Task<Host> StartAsync(bool on = true, bool createUsers = false, bool roles = false, bool syncRoles = false,
        bool idpInitiated = false, HttpMessageHandler? oauth = null)
    {
        Host? host = null;
        var server = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.Access.Sso = on;
                options.Access.RolesPermissions = roles;
                options.Sso.CreateUsers = createUsers;
                options.Sso.SyncRoles = syncRoles;
                options.Sso.AllowIdpInitiated = idpInitiated;
                options.Sso.FindUserByEmail = (email, _) => Task.FromResult(host!.Users.GetValueOrDefault(email));
                options.Sso.CreateUser = (email, _, _) =>
                {
                    host!.Users[email] = "new-" + email;
                    return Task.FromResult<string?>(host.Users[email]);
                };
                options.Sso.SignIn = (_, userId) =>
                {
                    host!.SignedIn.Add(userId);
                    return Task.CompletedTask;
                };
            },
            services =>
            {
                if (oauth is not null)
                {
                    services.AddHttpClient("escalated-sso").ConfigurePrimaryHttpMessageHandler(() => oauth);
                }
            });
        host = new Host { Server = server };
        return host;
    }

    // --- SAML ---

    private sealed class SamlResponse
    {
        public string Issuer = SsoTests.Issuer;
        public string Audience = Sp;
        public string Destination = Acs;
        public string Recipient = Acs;
        public string? InResponseTo;
        public string Email = "known@example.com";
        public string AssertionId = "_a" + Guid.NewGuid().ToString("N");
        public DateTime NotBefore = DateTime.UtcNow.AddMinutes(-1);
        public DateTime NotOnOrAfter = DateTime.UtcNow.AddMinutes(5);
        public X509Certificate2? SignWith = IdpCertificate;
        public Func<string, string> AfterSigning = xml => xml;

        public string Encoded()
        {
            var irt = InResponseTo is null ? string.Empty : $" InResponseTo=\"{InResponseTo}\"";
            var time = (DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var xml = "<samlp:Response xmlns:samlp=\"urn:oasis:names:tc:SAML:2.0:protocol\" xmlns:saml=\"urn:oasis:names:tc:SAML:2.0:assertion\""
                + $" ID=\"_r{AssertionId}\" Version=\"2.0\" IssueInstant=\"{time(DateTime.UtcNow)}\" Destination=\"{Destination}\"{irt}>"
                + $"<saml:Issuer>{Issuer}</saml:Issuer>"
                + $"<saml:Assertion ID=\"{AssertionId}\" Version=\"2.0\" IssueInstant=\"{time(DateTime.UtcNow)}\">"
                + $"<saml:Issuer>{Issuer}</saml:Issuer>"
                + $"<saml:Subject><saml:NameID>{Email}</saml:NameID><saml:SubjectConfirmation Method=\"urn:oasis:names:tc:SAML:2.0:cm:bearer\">"
                + $"<saml:SubjectConfirmationData Recipient=\"{Recipient}\" NotOnOrAfter=\"{time(NotOnOrAfter)}\"{irt}/></saml:SubjectConfirmation></saml:Subject>"
                + $"<saml:Conditions NotBefore=\"{time(NotBefore)}\" NotOnOrAfter=\"{time(NotOnOrAfter)}\">"
                + $"<saml:AudienceRestriction><saml:Audience>{Audience}</saml:Audience></saml:AudienceRestriction></saml:Conditions>"
                + "<saml:AttributeStatement>"
                + $"<saml:Attribute Name=\"email\"><saml:AttributeValue>{Email}</saml:AttributeValue></saml:Attribute>"
                + "<saml:Attribute Name=\"name\"><saml:AttributeValue>SSO User</saml:AttributeValue></saml:Attribute>"
                + "</saml:AttributeStatement></saml:Assertion></samlp:Response>";
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(xml);
            if (SignWith is not null)
            {
                var assertion = (XmlElement)document.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0]!;
                var signed = new SignedXml(assertion) { SigningKey = SignWith.GetRSAPrivateKey() };
                signed.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
                signed.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
                var reference = new Reference("#" + AssertionId) { DigestMethod = SignedXml.XmlDsigSHA256Url };
                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                reference.AddTransform(new XmlDsigExcC14NTransform());
                signed.AddReference(reference);
                signed.ComputeSignature();
                assertion.InsertAfter(document.ImportNode(signed.GetXml(), true), assertion.FirstChild);
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(AfterSigning(document.OuterXml)));
        }
    }

    private static async Task SamlAsync(Host host, X509Certificate2? certificate = null) =>
        await host.SettingsAsync("saml", new()
        {
            ["sso_entity_id"] = Issuer,
            ["sso_login_url"] = "https://idp.example.com/sso",
            ["sso_certificate"] = certificate is null ? string.Empty : Pem(certificate),
        });

    private static async Task<SamlResponse> PendingAsync(Host host)
    {
        var client = host.Server.Client;
        using var login = await client.GetAsync("/support/sso/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var location = login.Headers.Location!.ToString();
        Assert.StartsWith("https://idp.example.com/sso?SAMLRequest=", location);
        var deflated = Convert.FromBase64String(Uri.UnescapeDataString(Regex.Match(location, "SAMLRequest=([^&]+)").Groups[1].Value));
        using var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(deflated), System.IO.Compression.CompressionMode.Decompress);
        var xml = new StreamReader(inflate).ReadToEnd();
        return new SamlResponse { InResponseTo = Regex.Match(xml, "ID=\"([^\"]+)\"").Groups[1].Value };
    }

    private static async Task<HttpStatusCode> PostAcsAsync(Host host, string encoded)
    {
        using var response = await host.Server.Client.PostAsync("/support/sso/saml/acs",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["SAMLResponse"] = encoded }));
        return response.StatusCode;
    }

    [Fact]
    public async Task Saml_ASignedResponseForThePendingRequestSignsTheUserIn()
    {
        var host = await StartAsync();
        await using var _ = host.Server;
        await SamlAsync(host, IdpCertificate);
        var response = await PendingAsync(host);
        using var acs = await host.Server.Client.PostAsync("/support/sso/saml/acs",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["SAMLResponse"] = response.Encoded() }));
        Assert.Equal(HttpStatusCode.Redirect, acs.StatusCode);
        Assert.Equal("/support/agent", acs.Headers.Location!.ToString());
        Assert.Equal(new[] { "10" }, host.SignedIn);

        var metadata = await host.Server.Client.GetStringAsync("/support/sso/saml/metadata");
        Assert.Contains($"entityID=\"{Sp}\"", metadata);
        Assert.Contains($"Location=\"{Acs}\"", metadata);
    }

    [Fact]
    public async Task Saml_EveryFlawIsRefused()
    {
        var host = await StartAsync();
        await using var _ = host.Server;
        await SamlAsync(host, IdpCertificate);

        async Task Refused(Action<SamlResponse> change)
        {
            var response = await PendingAsync(host);
            change(response);
            Assert.Equal(HttpStatusCode.Forbidden, await PostAcsAsync(host, response.Encoded()));
        }

        await Refused(r => r.SignWith = null);
        await Refused(r => r.SignWith = OtherCertificate);
        await Refused(r => r.AfterSigning = xml => xml.Replace("SSO User", "Someone Else"));
        await Refused(r => r.Issuer = "https://evil.example.com");
        await Refused(r => r.Audience = "https://other-sp.example.com");
        await Refused(r => r.Destination = "https://other-sp.example.com/acs");
        await Refused(r => r.Recipient = "https://other-sp.example.com/acs");
        await Refused(r => { r.NotBefore = DateTime.UtcNow.AddHours(-2); r.NotOnOrAfter = DateTime.UtcNow.AddHours(-1); });
        await Refused(r => r.InResponseTo = "_unknown");
        await Refused(r => r.InResponseTo = null);
        Assert.Empty(host.SignedIn);

        var once = await PendingAsync(host);
        var encoded = once.Encoded();
        Assert.Equal(HttpStatusCode.Redirect, await PostAcsAsync(host, encoded));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAcsAsync(host, encoded));
    }

    [Fact]
    public async Task Saml_TheSameAssertionTwiceIsRefusedEvenWhenUnsolicitedIsAllowed()
    {
        var host = await StartAsync(idpInitiated: true);
        await using var _ = host.Server;
        await SamlAsync(host, IdpCertificate);
        var encoded = new SamlResponse { InResponseTo = null }.Encoded();
        Assert.Equal(HttpStatusCode.Redirect, await PostAcsAsync(host, encoded));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAcsAsync(host, encoded));
    }

    [Fact]
    public async Task Saml_WithoutACertificateEveryResponseIsRefused()
    {
        var host = await StartAsync(idpInitiated: true);
        await using var _ = host.Server;
        await SamlAsync(host, certificate: null);
        Assert.Equal(HttpStatusCode.Forbidden, await PostAcsAsync(host, new SamlResponse().Encoded()));
        Assert.Empty(host.SignedIn);
    }

    // --- OAuth ---

    private sealed class FakeProvider : HttpMessageHandler
    {
        public string? SentVerifier;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));
                SentVerifier = form["code_verifier"];
                return form["code"] == "good-code"
                    ? Json(HttpStatusCode.OK, "{\"access_token\":\"at-1\",\"token_type\":\"Bearer\"}")
                    : Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
            }

            return request.Headers.Authorization?.Parameter == "at-1"
                ? Json(HttpStatusCode.OK, "{\"sub\":\"1\",\"email\":\"known@example.com\",\"name\":\"SSO\"}")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string Query(string url, string name) =>
        Uri.UnescapeDataString(Regex.Match(url, $"[?&]{name}=([^&]+)").Groups[1].Value);

    private static string? Cookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("escalated_sso_oauth=", StringComparison.Ordinal))
            : null;

    [Fact]
    public async Task OAuth_AMatchingStateSignsInAndSendsThePkceVerifier()
    {
        var provider = new FakeProvider();
        var host = await StartAsync(oauth: provider);
        await using var _ = host.Server;
        await host.SettingsAsync("oauth", new()
        {
            ["sso_oauth_authorize_url"] = "https://idp.example.com/authorize",
            ["sso_oauth_token_url"] = "https://idp.example.com/token",
            ["sso_oauth_userinfo_url"] = "https://idp.example.com/userinfo",
            ["sso_oauth_client_id"] = "escalated",
            ["sso_oauth_client_secret"] = "shh",
        });

        async Task<(string State, string Challenge, string Cookie)> StartLoginAsync()
        {
            using var login = await host.Server.Client.GetAsync("/support/sso/login");
            var location = login.Headers.Location!.ToString();
            Assert.StartsWith("https://idp.example.com/authorize?", location);
            Assert.Equal("S256", Query(location, "code_challenge_method"));
            return (Query(location, "state"), Query(location, "code_challenge"), Cookie(login)!);
        }

        async Task<HttpStatusCode> CallbackAsync(string? cookie, string query)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/support/sso/oauth/callback?" + query);
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            using var response = await host.Server.Client.SendAsync(request);
            return response.StatusCode;
        }

        var (state, challenge, cookie) = await StartLoginAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await CallbackAsync(cookie, "state=forged&code=good-code"));
        Assert.Equal(HttpStatusCode.Forbidden, await CallbackAsync(null, $"state={state}&code=good-code"));
        Assert.Equal(HttpStatusCode.Forbidden, await CallbackAsync(cookie, "error=access_denied&state=" + state));
        Assert.Empty(host.SignedIn);

        Assert.Equal(HttpStatusCode.Redirect, await CallbackAsync(cookie, $"state={Uri.EscapeDataString(state)}&code=good-code"));
        Assert.Equal(new[] { "10" }, host.SignedIn);
        var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(provider.SentVerifier!)));
        Assert.Equal(challenge, expected);
    }

    // --- JWT ---

    private static async Task JwtAsync(Host host) =>
        await host.SettingsAsync("jwt", new()
        {
            ["sso_url"] = "https://idp.example.com/jwt",
            ["sso_jwt_secret"] = JwtSecret,
            ["sso_jwt_algorithm"] = "HS256",
        });

    private static string Token(string email = "known@example.com", string algorithm = SecurityAlgorithms.HmacSha256,
        string secret = JwtSecret, DateTime? expires = null, string? audience = Base, bool jti = true, bool exp = true,
        Dictionary<string, object>? extra = null)
    {
        var claims = new Dictionary<string, object> { ["email"] = email, ["name"] = "SSO" };
        if (jti) claims["jti"] = Guid.NewGuid().ToString();
        foreach (var (key, value) in extra ?? new()) claims[key] = value;
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = exp ? expires ?? DateTime.UtcNow.AddMinutes(5) : null,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), algorithm),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = exp }.CreateToken(descriptor);
    }

    private static async Task<HttpStatusCode> PostJwtAsync(Host host, string token)
    {
        using var response = await host.Server.Client.PostAsync("/support/sso/jwt/callback",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["jwt"] = token }));
        return response.StatusCode;
    }

    [Fact]
    public async Task Jwt_AValidTokenSignsInAndEveryFlawIsRefused()
    {
        var host = await StartAsync();
        await using var _ = host.Server;
        await JwtAsync(host);
        var valid = Token();
        Assert.Equal(HttpStatusCode.Redirect, await PostJwtAsync(host, valid));
        using (var get = await host.Server.Client.GetAsync("/support/sso/jwt/callback?jwt=" + Uri.EscapeDataString(Token())))
        {
            Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        }

        using (var login = await host.Server.Client.GetAsync("/support/sso/login"))
        {
            Assert.StartsWith("https://idp.example.com/jwt?return_to=", login.Headers.Location!.ToString());
        }

        Assert.Equal(new[] { "10", "10" }, host.SignedIn);
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = valid.Split('.')[1];
        foreach (var token in new[]
                 {
                     Token(secret: JwtSecret + "-wrong"),
                     header + "." + payload + ".",
                     Token(algorithm: SecurityAlgorithms.HmacSha384),
                     Token(exp: false),
                     Token(expires: DateTime.UtcNow.AddHours(-1)),
                     Token(audience: "https://other.example.com"),
                     Token(jti: false),
                     valid,
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await PostJwtAsync(host, token));
        }

        Assert.Equal(2, host.SignedIn.Count);
    }

    // --- Users and roles ---

    [Fact]
    public async Task AnUnknownEmailIsRefusedUnlessCreateUsersIsOn()
    {
        var off = await StartAsync();
        await using (off.Server)
        {
            await JwtAsync(off);
            Assert.Equal(HttpStatusCode.Forbidden, await PostJwtAsync(off, Token("stranger@example.com")));
            Assert.False(off.Users.ContainsKey("stranger@example.com"));
        }

        var on = await StartAsync(createUsers: true);
        await using (on.Server)
        {
            await JwtAsync(on);
            Assert.Equal(HttpStatusCode.Redirect, await PostJwtAsync(on, Token("stranger@example.com")));
            Assert.Equal(new[] { "new-stranger@example.com" }, on.SignedIn);
        }
    }

    [Fact]
    public async Task TheRoleIsMappedOnlyWithRolesOnAndSyncRolesOn()
    {
        async Task<List<string>> RolesAfterAsync(bool roles, bool sync, string role)
        {
            var host = await StartAsync(roles: roles, syncRoles: sync);
            await using var _ = host.Server;
            using (var scope = host.Server.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
            }

            await JwtAsync(host);
            await PostJwtAsync(host, Token(extra: new() { ["role"] = role }));
            return await host.Server.QueryAsync(db => db.RoleUsers.Where(ru => ru.UserId == "10").Select(ru => ru.Role!.Slug).ToListAsync());
        }

        Assert.Empty(await RolesAfterAsync(roles: false, sync: true, "light_agent"));
        Assert.Empty(await RolesAfterAsync(roles: true, sync: false, "light_agent"));
        Assert.Empty(await RolesAfterAsync(roles: true, sync: true, "no-such-role"));
        Assert.Empty(await RolesAfterAsync(roles: true, sync: true, "escalated-admin"));
        Assert.Equal(new[] { "light_agent" }, await RolesAfterAsync(roles: true, sync: true, "light_agent"));
    }

    // --- Switch, provider and settings ---

    [Fact]
    public async Task ProviderNoneMakesLogin404AndAnotherProvidersRoutes404()
    {
        var host = await StartAsync();
        await using var _ = host.Server;
        await host.SettingsAsync("none", new());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Server.Client.GetAsync("/support/sso/login")).StatusCode);
        await JwtAsync(host);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Server.Client.GetAsync("/support/sso/saml/metadata")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await PostAcsAsync(host, "x"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Server.Client.GetAsync("/support/sso/oauth/callback")).StatusCode);
    }

    [Fact]
    public async Task TheSettingsPageMasksSecretsListsEndpointsAndKeepsAMaskedSecret()
    {
        var host = await StartAsync();
        await using var _ = host.Server;
        await host.Server.GrantRoleAsync("1", "escalated-admin");
        await JwtAsync(host);
        var admin = host.Server.ClientFor("1");
        var page = await PageAsync(admin, "/support/admin/settings/sso");
        Assert.Equal("Escalated/Admin/Settings/SsoSettings", page.GetProperty("component").GetString());
        var props = page.GetProperty("props");
        Assert.Equal("********", props.GetProperty("settings").GetProperty("sso_jwt_secret").GetString());
        Assert.DoesNotContain(JwtSecret, page.GetRawText());
        Assert.Equal(Acs, props.GetProperty("endpoints").GetProperty("acs").GetString());
        Assert.Equal(Sp, props.GetProperty("endpoints").GetProperty("sp_entity_id").GetString());
        Assert.Equal(Base + "/support/sso/jwt/callback", props.GetProperty("endpoints").GetProperty("jwt_callback").GetString());
        Assert.True(props.GetProperty("escalated").GetProperty("features").GetProperty("sso").GetBoolean());

        using var saved = await admin.SendAsync(Visit(HttpMethod.Post, "/support/admin/settings/sso", new
        {
            sso_provider = "jwt",
            sso_url = "https://idp.example.com/jwt2",
            sso_jwt_secret = "********",
            sso_jwt_algorithm = "HS256",
        }));
        Assert.Equal(HttpStatusCode.SeeOther, saved.StatusCode);
        Assert.Equal(JwtSecret, await host.Server.QueryAsync(db => db.Settings.Where(s => s.Key == "sso_jwt_secret").Select(s => s.Value).SingleAsync()));
        Assert.Equal("https://idp.example.com/jwt2", await host.Server.QueryAsync(db => db.Settings.Where(s => s.Key == "sso_url").Select(s => s.Value).SingleAsync()));
    }

    [Fact]
    public async Task WithTheSwitchOffEveryRouteAnswers404()
    {
        var host = await StartAsync(on: false);
        await using var _ = host.Server;
        await JwtAsync(host);
        await host.Server.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "sso", Value = "1" });
            await db.SaveChangesAsync();
        });
        await host.Server.GrantRoleAsync("1", "escalated-admin");
        var client = host.Server.Client;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/support/sso/login")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/support/sso/saml/metadata")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await PostAcsAsync(host, "x"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/support/sso/oauth/callback")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await PostJwtAsync(host, Token()));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Server.ClientFor("1").SendAsync(Visit(HttpMethod.Get, "/support/admin/settings/sso"))).StatusCode);
        Assert.Empty(host.SignedIn);
    }
}
