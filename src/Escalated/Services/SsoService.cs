using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Escalated.Services;

/// <summary>Why a sign-in was refused: for the log, never for the user.</summary>
public sealed class SsoException : Exception
{
    public SsoException(string message) : base(message)
    {
    }
}

public sealed record SsoIdentity(string Email, string Name, string Role);

/// <summary>
/// Single sign-on through the provider an admin picks in Admin &gt; Settings &gt; SSO (ADR
/// 2026-10-29-configurable-access-features): SAML 2 (signatures through
/// <c>System.Security.Cryptography.Xml</c>), OAuth 2 / OIDC authorization code with PKCE
/// (Duende.IdentityModel) and JWT (Microsoft.IdentityModel.JsonWebTokens). Each validate method
/// returns the same <see cref="SsoIdentity"/>, or throws <see cref="SsoException"/> with the reason.
/// </summary>
public class SsoService
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["sso_provider"] = "none",
        ["sso_entity_id"] = string.Empty,
        ["sso_url"] = string.Empty,
        ["sso_login_url"] = string.Empty,
        ["sso_logout_url"] = string.Empty,
        ["sso_metadata_url"] = string.Empty,
        ["sso_certificate"] = string.Empty,
        ["sso_attr_email"] = "email",
        ["sso_attr_name"] = "name",
        ["sso_attr_role"] = "role",
        ["sso_jwt_secret"] = string.Empty,
        ["sso_jwt_algorithm"] = "HS256",
        ["sso_oauth_authorize_url"] = string.Empty,
        ["sso_oauth_token_url"] = string.Empty,
        ["sso_oauth_userinfo_url"] = string.Empty,
        ["sso_oauth_client_id"] = string.Empty,
        ["sso_oauth_client_secret"] = string.Empty,
        ["sso_oauth_scopes"] = "openid profile email",
    };

    public static readonly string[] SecretKeys = { "sso_jwt_secret", "sso_oauth_client_secret" };
    public static readonly string[] JwtAlgorithms = { "HS256", "HS384", "HS512", "RS256", "RS384", "RS512" };
    public static readonly string[] Providers = { "none", "saml", "oauth", "jwt" };

    /// <summary>How long an AuthnRequest ID stays answerable, in seconds.</summary>
    public const long SamlRequestTtl = 600;

    private const string Saml = "urn:oasis:names:tc:SAML:2.0:assertion";
    private const string Samlp = "urn:oasis:names:tc:SAML:2.0:protocol";
    private const string Dsig = "http://www.w3.org/2000/09/xmldsig#";
    private const string Bearer = "urn:oasis:names:tc:SAML:2.0:cm:bearer";
    private static readonly Regex Email = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedTransforms = new()
    {
        SignedXml.XmlDsigEnvelopedSignatureTransformUrl,
        SignedXml.XmlDsigExcC14NTransformUrl,
        SignedXml.XmlDsigExcC14NWithCommentsTransformUrl,
        SignedXml.XmlDsigC14NTransformUrl,
    };

    private readonly EscalatedDbContext _db;
    private readonly SettingsService _settings;
    private readonly AccessFeatures _features;
    private readonly EscalatedOptions _options;
    private readonly IHttpClientFactory _http;

    public SsoService(EscalatedDbContext db, SettingsService settings, AccessFeatures features,
        IOptions<EscalatedOptions> options, IHttpClientFactory http)
    {
        _db = db;
        _settings = settings;
        _features = features;
        _options = options.Value;
        _http = http;
    }

    public bool Enabled => _features.Sso;

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');

    public async Task<Dictionary<string, string>> ConfigAsync(CancellationToken ct = default)
    {
        var config = new Dictionary<string, string>();
        foreach (var (key, fallback) in Defaults)
        {
            config[key] = await _settings.GetAsync(key, null, ct) ?? fallback;
        }

        if (config["sso_login_url"].Length == 0 && config["sso_url"].Trim().Length > 0)
        {
            config["sso_login_url"] = config["sso_url"].Trim();
        }

        return config;
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        foreach (var (key, value) in values)
        {
            if (Defaults.ContainsKey(key))
            {
                await _settings.SetAsync(key, value ?? string.Empty, ct);
            }
        }
    }

    public async Task<string> ProviderAsync(CancellationToken ct = default)
    {
        var provider = await _settings.GetAsync("sso_provider", "none", ct) ?? "none";
        return Providers.Contains(provider) ? provider : "none";
    }

    // --- Install-level rules ---

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static bool Flag(bool? configured, string env) => configured ?? AccessFeatures.Truthy(Env(env));

    public long ClockSkew
    {
        get
        {
            var raw = _options.Sso.ClockSkew?.ToString() ?? Env("ESCALATED_SSO_CLOCK_SKEW");
            return long.TryParse(raw, out var skew) && skew >= 0 ? Math.Min(skew, 300) : 120;
        }
    }

    public bool AllowIdpInitiated => Flag(_options.Sso.AllowIdpInitiated, "ESCALATED_SSO_ALLOW_IDP_INITIATED");
    public bool CreateUsers => Flag(_options.Sso.CreateUsers, "ESCALATED_SSO_CREATE_USERS");
    public bool SyncRoles => Flag(_options.Sso.SyncRoles, "ESCALATED_SSO_SYNC_ROLES");
    public string RedirectTo => _options.Sso.RedirectTo ?? Env("ESCALATED_SSO_REDIRECT_TO") ?? Prefix + "/agent";

    public string BaseUrl(HttpRequest request) =>
        (_options.Sso.BaseUrl ?? Env("ESCALATED_SSO_BASE_URL") ?? $"{request.Scheme}://{request.Host}{request.PathBase}").TrimEnd('/');

    public string Url(HttpRequest request, string path) => BaseUrl(request) + Prefix + "/sso" + path;

    public string SpEntityId(HttpRequest request) =>
        _options.Sso.SpEntityId ?? Env("ESCALATED_SSO_SP_ENTITY_ID") ?? Url(request, "/saml/metadata");

    public string JwtAudience(HttpRequest request) => _options.Sso.JwtAudience ?? Env("ESCALATED_SSO_JWT_AUDIENCE") ?? BaseUrl(request);

    /// <summary>The URLs an admin gives the identity provider.</summary>
    public Dictionary<string, string> Endpoints(HttpRequest request) => new()
    {
        ["login"] = Url(request, "/login"),
        ["sp_entity_id"] = SpEntityId(request),
        ["metadata"] = Url(request, "/saml/metadata"),
        ["acs"] = Url(request, "/saml/acs"),
        ["oauth_redirect"] = Url(request, "/oauth/callback"),
        ["jwt_callback"] = Url(request, "/jwt/callback"),
    };

    // --- Message IDs ---

    private async Task PurgeAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SsoMessageIds.Where(m => m.ExpiresAt < now).ExecuteDeleteAsync(ct);
    }

    private async Task RememberAsync(string kind, string id, long expiresAt, CancellationToken ct)
    {
        await PurgeAsync(ct);
        _db.SsoMessageIds.Add(new SsoMessageId { Kind = kind, MessageId = id, ExpiresAt = expiresAt });
        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> ConsumeAsync(string kind, string id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await _db.SsoMessageIds.Where(m => m.Kind == kind && m.MessageId == id && m.ExpiresAt >= now).ExecuteDeleteAsync(ct) == 1;
    }

    /// <summary>Claims an ID seen for the first time: false when it was already claimed (a replay).</summary>
    private async Task<bool> ClaimAsync(string kind, string id, long expiresAt, CancellationToken ct)
    {
        if (id.Length is 0 or > 191)
        {
            return false;
        }

        await PurgeAsync(ct);
        if (await _db.SsoMessageIds.AnyAsync(m => m.Kind == kind && m.MessageId == id, ct))
        {
            return false;
        }

        var row = new SsoMessageId { Kind = kind, MessageId = id, ExpiresAt = expiresAt };
        _db.SsoMessageIds.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    // --- SAML ---

    /// <summary>The IdP sign-in URL for a new AuthnRequest (HTTP-Redirect binding); its ID is stored server side.</summary>
    public async Task<string> SamlLoginUrlAsync(HttpRequest request, CancellationToken ct = default)
    {
        var config = await ConfigAsync(ct);
        IdpCertificate(config["sso_certificate"]);
        var idpUrl = config["sso_login_url"].Trim();
        if (idpUrl.Length == 0)
        {
            throw new SsoException("SAML refused: no IdP sign-in URL is configured.");
        }

        var id = "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        await RememberAsync("saml_request", id, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + SamlRequestTtl, ct);
        var xml = $"<samlp:AuthnRequest xmlns:samlp=\"{Samlp}\" xmlns:saml=\"{Saml}\" ID=\"{id}\" Version=\"2.0\""
            + $" IssueInstant=\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\" Destination=\"{Escape(idpUrl)}\""
            + " ProtocolBinding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\""
            + $" AssertionConsumerServiceURL=\"{Escape(Url(request, "/saml/acs"))}\">"
            + $"<saml:Issuer>{Escape(SpEntityId(request))}</saml:Issuer>"
            + "<samlp:NameIDPolicy Format=\"urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified\" AllowCreate=\"true\"/>"
            + "</samlp:AuthnRequest>";
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Encoding.UTF8.GetBytes(xml));
        }

        return idpUrl + (idpUrl.Contains('?') ? "&" : "?") + "SAMLRequest=" + Uri.EscapeDataString(Convert.ToBase64String(output.ToArray()));
    }

    public string SamlMetadata(HttpRequest request) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + $"<md:EntityDescriptor xmlns:md=\"urn:oasis:names:tc:SAML:2.0:metadata\" entityID=\"{Escape(SpEntityId(request))}\">"
        + $"<md:SPSSODescriptor AuthnRequestsSigned=\"false\" WantAssertionsSigned=\"true\" protocolSupportEnumeration=\"{Samlp}\">"
        + "<md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified</md:NameIDFormat>"
        + "<md:AssertionConsumerService Binding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\""
        + $" Location=\"{Escape(Url(request, "/saml/acs"))}\" index=\"1\"/>"
        + "</md:SPSSODescriptor></md:EntityDescriptor>";

    /// <summary>
    /// Validates a base64 SAMLResponse posted to the ACS. Accepted only when the IdP certificate is
    /// configured; the single assertion carries a signature by it over the whole assertion (a key in
    /// KeyInfo is never trusted); Issuer is <c>sso_entity_id</c>; Audience names our SP entity ID;
    /// Destination and the bearer Recipient are our ACS URL; the validity windows hold; InResponseTo
    /// names a pending request (or IdP-initiated sign-in is on); and the assertion ID is new.
    /// </summary>
    public async Task<SsoIdentity> ValidateSamlResponseAsync(HttpRequest request, string? samlResponse, CancellationToken ct = default)
    {
        var config = await ConfigAsync(ct);
        var certificate = IdpCertificate(config["sso_certificate"]);
        var entityId = config["sso_entity_id"].Trim();
        if (entityId.Length == 0)
        {
            throw new SsoException("SAML refused: no IdP entity ID is configured.");
        }

        var spEntityId = SpEntityId(request);
        var acs = Url(request, "/saml/acs");
        var skew = ClockSkew;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String((samlResponse ?? string.Empty).Trim());
        }
        catch (FormatException)
        {
            throw new SsoException("SAML refused: the response is not base64.");
        }

        var document = Parse(bytes);
        if (document.GetElementsByTagName("EncryptedAssertion", Saml).Count > 0)
        {
            throw new SsoException("SAML refused: encrypted assertions are not supported.");
        }

        var assertions = document.GetElementsByTagName("Assertion", Saml);
        if (assertions.Count != 1)
        {
            throw new SsoException("SAML refused: the response must carry exactly one assertion.");
        }

        var root = document.DocumentElement!;
        var assertion = (XmlElement)assertions[0]!;
        VerifySignature(document, assertion, certificate);

        var issuer = Text(First(assertion, Saml, "Issuer"));
        if (issuer != entityId)
        {
            throw new SsoException($"SAML refused: Issuer [{issuer}] is not [{entityId}].");
        }

        var conditions = First(assertion, Saml, "Conditions") ?? throw new SsoException("SAML refused: the assertion has no Conditions.");
        var conditionsUntil = Window(conditions, now, skew, "assertion");
        Audience(conditions, spEntityId);

        var isResponse = root.NamespaceURI == Samlp && root.LocalName == "Response";
        if (isResponse && root.GetAttribute("Destination") != acs)
        {
            throw new SsoException($"SAML refused: Destination [{root.GetAttribute("Destination")}] is not [{acs}].");
        }

        var (inResponseTo, bearerUntil) = BearerConfirmation(assertion, acs, now, skew);
        var outer = isResponse && root.GetAttribute("InResponseTo").Length > 0 ? root.GetAttribute("InResponseTo") : null;
        if (outer is not null && inResponseTo is not null && outer != inResponseTo)
        {
            throw new SsoException("SAML refused: the Response and assertion answer different requests.");
        }

        inResponseTo ??= outer;
        if (inResponseTo is not null)
        {
            if (!await ConsumeAsync("saml_request", inResponseTo, ct))
            {
                throw new SsoException($"SAML refused: InResponseTo [{inResponseTo}] is not a pending request.");
            }
        }
        else if (!AllowIdpInitiated)
        {
            throw new SsoException("SAML refused: unsolicited response (no InResponseTo) and IdP-initiated sign-in is off.");
        }

        var assertionId = assertion.GetAttribute("ID");
        var until = Math.Max(conditionsUntil ?? 0, bearerUntil) + skew;
        if (assertionId.Length == 0 || !await ClaimAsync("saml_assertion", assertionId, until, ct))
        {
            throw new SsoException($"SAML refused: assertion [{assertionId}] was already used.");
        }

        var attributes = new Dictionary<string, string>();
        foreach (XmlElement attribute in assertion.GetElementsByTagName("Attribute", Saml))
        {
            var value = attribute.GetElementsByTagName("AttributeValue", Saml);
            if (attribute.GetAttribute("Name").Length > 0 && value.Count > 0)
            {
                attributes[attribute.GetAttribute("Name")] = value[0]!.InnerText.Trim();
            }
        }

        var email = attributes.GetValueOrDefault(Or(config["sso_attr_email"], "email"), string.Empty);
        if (email.Length == 0)
        {
            var nameId = assertion.GetElementsByTagName("NameID", Saml);
            var candidate = nameId.Count > 0 ? nameId[0]!.InnerText.Trim() : string.Empty;
            if (Email.IsMatch(candidate))
            {
                email = candidate;
            }
        }

        return Identity(email, attributes.GetValueOrDefault(Or(config["sso_attr_name"], "name"), string.Empty),
            attributes.GetValueOrDefault(Or(config["sso_attr_role"], "role"), string.Empty));
    }

    private static void VerifySignature(XmlDocument document, XmlElement assertion, X509Certificate2 certificate)
    {
        var signatures = Children(assertion, Dsig, "Signature");
        if (signatures.Count == 0)
        {
            throw new SsoException("SAML refused: the assertion is not signed.");
        }

        var id = assertion.GetAttribute("ID");
        if (id.Length == 0 || document.SelectNodes($"//*[@ID='{id.Replace("'", string.Empty)}' or @Id='{id.Replace("'", string.Empty)}' or @id='{id.Replace("'", string.Empty)}']")!.Count != 1)
        {
            throw new SsoException("SAML refused: the assertion ID is missing or not unique.");
        }

        foreach (var element in signatures)
        {
            try
            {
                var signed = new SignedXml(assertion);
                signed.LoadXml(element);
                if (signed.SignedInfo!.References.Count != 1
                    || signed.SignedInfo.References[0] is not Reference reference
                    || reference.Uri != "#" + id
                    || Enumerable.Range(0, reference.TransformChain.Count).Any(i => !AllowedTransforms.Contains(reference.TransformChain[i].Algorithm)))
                {
                    continue;
                }

                if (signed.CheckSignature(certificate, verifySignatureOnly: true))
                {
                    return;
                }
            }
            catch (CryptographicException)
            {
                // Try the next signature; none verifying is refused below.
            }
        }

        throw new SsoException("SAML refused: the assertion signature does not verify against the IdP certificate.");
    }

    private static XmlDocument Parse(byte[] xml)
    {
        try
        {
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using var reader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            document.Load(reader);
            if (document.DocumentElement is null)
            {
                throw new SsoException("SAML refused: malformed XML.");
            }

            return document;
        }
        catch (XmlException)
        {
            throw new SsoException("SAML refused: malformed XML.");
        }
    }

    private static X509Certificate2 IdpCertificate(string? certificate)
    {
        var raw = (certificate ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            throw new SsoException("SAML refused: no IdP certificate is configured.");
        }

        if (raw.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new SsoException("SAML refused: the IdP certificate must not be a private key.");
        }

        var body = Regex.Replace(raw.Replace("-----BEGIN CERTIFICATE-----", string.Empty).Replace("-----END CERTIFICATE-----", string.Empty), @"\s+", string.Empty);
        try
        {
#pragma warning disable SYSLIB0057 // net8 has no X509CertificateLoader.
            return new X509Certificate2(Convert.FromBase64String(body));
#pragma warning restore SYSLIB0057
        }
        catch (Exception)
        {
            throw new SsoException("SAML refused: the IdP certificate could not be read.");
        }
    }

    private static List<XmlElement> Children(XmlElement parent, string ns, string localName) =>
        parent.ChildNodes.OfType<XmlElement>().Where(e => e.NamespaceURI == ns && e.LocalName == localName).ToList();

    private static XmlElement? First(XmlElement parent, string ns, string localName) => Children(parent, ns, localName).FirstOrDefault();

    private static string Text(XmlElement? element) => element?.InnerText.Trim() ?? string.Empty;

    private static long Time(string value, string attribute) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUnixTimeSeconds()
            : throw new SsoException($"SAML refused: {attribute} is not a time.");

    private static long? Window(XmlElement element, long now, long skew, string what)
    {
        var notBefore = element.GetAttribute("NotBefore");
        if (notBefore.Length > 0 && Time(notBefore, "NotBefore") > now + skew)
        {
            throw new SsoException($"SAML refused: the {what} is not yet valid.");
        }

        var notOnOrAfter = element.GetAttribute("NotOnOrAfter");
        if (notOnOrAfter.Length == 0)
        {
            return null;
        }

        var until = Time(notOnOrAfter, "NotOnOrAfter");
        if (now >= until + skew)
        {
            throw new SsoException($"SAML refused: the {what} has expired.");
        }

        return until;
    }

    private static void Audience(XmlElement conditions, string spEntityId)
    {
        var restrictions = Children(conditions, Saml, "AudienceRestriction");
        if (restrictions.Count == 0)
        {
            throw new SsoException("SAML refused: the assertion names no Audience.");
        }

        if (restrictions.Any(r => Children(r, Saml, "Audience").All(a => Text(a) != spEntityId)))
        {
            throw new SsoException($"SAML refused: [{spEntityId}] is not an Audience of the assertion.");
        }
    }

    private static (string? InResponseTo, long NotOnOrAfter) BearerConfirmation(XmlElement assertion, string acs, long now, long skew)
    {
        var subject = First(assertion, Saml, "Subject");
        var bearers = subject is null
            ? new List<XmlElement>()
            : Children(subject, Saml, "SubjectConfirmation").Where(c => c.GetAttribute("Method") == Bearer).ToList();
        if (bearers.Count == 0)
        {
            throw new SsoException("SAML refused: no bearer SubjectConfirmation.");
        }

        SsoException? first = null;
        foreach (var bearer in bearers)
        {
            try
            {
                var data = First(bearer, Saml, "SubjectConfirmationData") ?? throw new SsoException("SAML refused: no SubjectConfirmationData.");
                if (data.GetAttribute("Recipient") != acs)
                {
                    throw new SsoException($"SAML refused: Recipient [{data.GetAttribute("Recipient")}] is not [{acs}].");
                }

                var until = Window(data, now, skew, "subject confirmation")
                    ?? throw new SsoException("SAML refused: SubjectConfirmationData has no NotOnOrAfter.");
                var inResponseTo = data.GetAttribute("InResponseTo");
                return (inResponseTo.Length == 0 ? null : inResponseTo, until);
            }
            catch (SsoException refused)
            {
                first ??= refused;
            }
        }

        throw first!;
    }

    // --- OAuth 2 / OIDC ---

    public sealed record OAuthStart(string Url, string State, string Verifier);

    /// <summary>The provider's authorization URL with a fresh <c>state</c> and a PKCE (S256) challenge.</summary>
    public async Task<OAuthStart> OAuthStartAsync(HttpRequest request, CancellationToken ct = default)
    {
        var config = await ConfigAsync(ct);
        if (config["sso_oauth_authorize_url"].Length == 0 || config["sso_oauth_token_url"].Length == 0 || config["sso_oauth_client_id"].Length == 0)
        {
            throw new SsoException("OAuth refused: the provider is not fully configured.");
        }

        var state = CryptoRandom.CreateUniqueId(32);
        var verifier = CryptoRandom.CreateUniqueId(48);
        var challenge = Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = new RequestUrl(config["sso_oauth_authorize_url"]).CreateAuthorizeUrl(
            clientId: config["sso_oauth_client_id"],
            responseType: OidcConstants.ResponseTypes.Code,
            scope: string.Join(' ', config["sso_oauth_scopes"].Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)),
            redirectUri: Url(request, "/oauth/callback"),
            state: state,
            codeChallenge: challenge,
            codeChallengeMethod: OidcConstants.CodeChallengeMethods.Sha256);
        return new OAuthStart(url, state, verifier);
    }

    /// <summary>Exchanges the code (with the PKCE verifier) server to server, then reads the userinfo profile.</summary>
    public async Task<SsoIdentity> OAuthIdentityAsync(HttpRequest request, string code, string verifier, CancellationToken ct = default)
    {
        var config = await ConfigAsync(ct);
        var client = _http.CreateClient("escalated-sso");
        var token = await client.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
        {
            Address = config["sso_oauth_token_url"],
            ClientId = config["sso_oauth_client_id"],
            ClientSecret = config["sso_oauth_client_secret"],
            ClientCredentialStyle = ClientCredentialStyle.PostBody,
            Code = code,
            RedirectUri = Url(request, "/oauth/callback"),
            CodeVerifier = verifier,
        }, ct);
        if (token.IsError || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new SsoException($"OAuth refused: the token exchange failed ({token.Error}).");
        }

        using var userinfo = new HttpRequestMessage(HttpMethod.Get, config["sso_oauth_userinfo_url"]);
        userinfo.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        userinfo.Headers.Accept.ParseAdd("application/json");
        using var response = await client.SendAsync(userinfo, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new SsoException($"OAuth refused: userinfo answered {(int)response.StatusCode}.");
        }

        JsonElement profile;
        try
        {
            profile = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new SsoException("OAuth refused: userinfo is not JSON.");
        }

        if (profile.TryGetProperty("email_verified", out var verified)
            && (verified.ValueKind == JsonValueKind.False || verified.ToString() is "false" or "0"))
        {
            throw new SsoException("OAuth refused: the provider says the email is not verified.");
        }

        string Claim(string name, string? fallback) =>
            profile.TryGetProperty(name, out var value) || (fallback is not null && profile.TryGetProperty(fallback, out value))
                ? (value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()).Trim()
                : string.Empty;

        return Identity(Claim(Or(config["sso_attr_email"], "email"), "email"), Claim(Or(config["sso_attr_name"], "name"), "name"),
            Claim(Or(config["sso_attr_role"], "role"), null));
    }

    // --- JWT ---

    /// <summary>Where JWT sign-in starts: the configured login URL, told where to come back.</summary>
    public async Task<string> JwtLoginUrlAsync(HttpRequest request, CancellationToken ct = default)
    {
        var config = await ConfigAsync(ct);
        var url = (config["sso_login_url"].Length > 0 ? config["sso_login_url"] : config["sso_url"]).Trim();
        if (url.Length == 0)
        {
            throw new SsoException("JWT sign-in has no login URL configured.");
        }

        return url + (url.Contains('?') ? "&" : "?") + "return_to=" + Uri.EscapeDataString(Url(request, "/jwt/callback"));
    }

    /// <summary>
    /// Validates a JWT handed back by the identity provider: <c>alg</c> must be the configured
    /// algorithm (so <c>none</c> and algorithm switching are refused); <c>exp</c> is required;
    /// <c>nbf</c>/<c>iat</c> hold within the skew; <c>aud</c> names this install; <c>jti</c> is
    /// present and used once.
    /// </summary>
    public async Task<SsoIdentity> ValidateJwtAsync(HttpRequest request, string? token, CancellationToken ct = default)
    {
        var config = await ConfigAsync(ct);
        var algorithm = Or(config["sso_jwt_algorithm"], "HS256");
        var secret = config["sso_jwt_secret"];
        if (!JwtAlgorithms.Contains(algorithm))
        {
            throw new SsoException($"JWT refused: unsupported algorithm {algorithm}.");
        }

        if (secret.Length == 0)
        {
            throw new SsoException("JWT refused: no secret or public key is configured.");
        }

        SecurityKey key;
        try
        {
            key = algorithm.StartsWith("HS", StringComparison.Ordinal) ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) : RsaKey(secret);
        }
        catch (Exception)
        {
            throw new SsoException("JWT refused: the configured key could not be read.");
        }

        var audience = JwtAudience(request);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token ?? string.Empty, new TokenValidationParameters
        {
            ValidAlgorithms = new[] { algorithm },
            IssuerSigningKey = key,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = false,
            ValidAudience = audience,
            ValidateAudience = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(ClockSkew),
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            throw new SsoException("JWT refused: " + (result.Exception?.Message ?? "the token is not valid."));
        }

        if (jwt.Alg != algorithm)
        {
            throw new SsoException($"JWT refused: alg {jwt.Alg} is not {algorithm}.");
        }

        if (jwt.IssuedAt != DateTime.MinValue && jwt.IssuedAt > DateTime.UtcNow.AddSeconds(ClockSkew))
        {
            throw new SsoException("JWT refused: the token was issued in the future.");
        }

        var jti = jwt.Id;
        if (string.IsNullOrEmpty(jti))
        {
            throw new SsoException("JWT refused: no jti claim.");
        }

        var exp = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds();
        if (!await ClaimAsync("jwt", jti, exp + ClockSkew, ct))
        {
            throw new SsoException($"JWT refused: jti [{jti}] was already used.");
        }

        string Claim(string name, string? fallback) =>
            jwt.TryGetPayloadValue<object>(name, out var value) || (fallback is not null && jwt.TryGetPayloadValue(fallback, out value))
                ? value?.ToString()?.Trim() ?? string.Empty
                : string.Empty;

        return Identity(Claim(Or(config["sso_attr_email"], "email"), "email"), Claim(Or(config["sso_attr_name"], "name"), "name"),
            Claim(Or(config["sso_attr_role"], "role"), null));
    }

    private static RsaSecurityKey RsaKey(string pem)
    {
        if (pem.Contains("CERTIFICATE", StringComparison.Ordinal))
        {
            return new RsaSecurityKey(IdpCertificate(pem).GetRSAPublicKey()!);
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }

    // --- Shared ---

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static SsoIdentity Identity(string email, string name, string role) =>
        Email.IsMatch(email) ? new SsoIdentity(email, name, role) : throw new SsoException("SSO refused: no valid email in the identity.");

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
