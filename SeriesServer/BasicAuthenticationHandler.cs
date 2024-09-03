// Macrobond Financial AB 2020-2024

using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SeriesServer
{
    /// <summary>
    /// Very basic authentication handler that has one user called "Test" and password "123".
    /// </summary>
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {

        /// <inheritdoc />
        public BasicAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            try
            {
                if (!Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues authHeader))
                    return Task.FromResult(AuthenticateResult.Fail("Missing authorization header!"));

                string? authHeaderString = authHeader;

                if (authHeaderString is null)
                    return Task.FromResult(AuthenticateResult.Fail("Empty authorization header!"));

                AuthenticationHeaderValue headerValue = AuthenticationHeaderValue.Parse(authHeaderString);
                if (headerValue.Scheme != "Basic" || headerValue.Parameter is null)
                    return Task.FromResult(AuthenticateResult.Fail("Invalid authorization header!"));

                byte[] loginString = Convert.FromBase64String(headerValue.Parameter);
                string[] credentials = Encoding.ASCII.GetString(loginString).Split(':');
                string userName = credentials[0];
                string password = credentials[1];

                if (userName == m_userName && password == m_password)
                    return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "545"), new Claim(ClaimTypes.Name, m_userName),], Scheme.Name)), Scheme.Name)));

                return Task.FromResult(AuthenticateResult.Fail("Authorization failed! Wrong username or password!"));
            }
            catch
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid authorization header!"));
            }

        }
        readonly string m_userName = "Test";
        readonly string m_password = "123";
    }
}
