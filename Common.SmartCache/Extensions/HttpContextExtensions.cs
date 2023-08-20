#nullable enable

using System;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Common.SmartCache;

public static class HttpContextExtensions
{
    public static string GetAccessToken(this IHttpContextAccessor httpContextAccessor)
    {
        HttpContext httpContext = httpContextAccessor.HttpContext ?? throw new InvalidOperationException($"{nameof(HttpContext)} is null.");
        HttpRequest request = httpContext.Request;

        string? authenticationHeaderRaw = request.Headers["Authorization"].LastOrDefault();
        if (AuthenticationHeaderValue.TryParse(authenticationHeaderRaw, out AuthenticationHeaderValue? authenticationHeader) &&
            authenticationHeader.Scheme == "Bearer")
        {
            return authenticationHeader.Parameter!;
        }

        return request.Headers["accesstoken"].LastOrDefault() ?? string.Empty;
    }
}
