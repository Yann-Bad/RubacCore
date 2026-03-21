using System.Security.Claims;

namespace RubacCore.Middleware;

/// <summary>
/// Reads the <c>X-Centre-ID</c> header from each authenticated request and
/// injects an <c>ActiveCentreId</c> claim into the current principal.
///
/// This allows users to switch management centres mid-session without
/// re-authenticating.
///
/// Priority chain:
///   1. <c>X-Centre-ID</c> request header (user switched centre in the UI)
///   2. JWT <c>centre_primary</c> claim is NOT used here — the DB lookup 
///      happens in the controller.  This middleware only stores the numeric 
///      override from the header.
///
/// Registration: must come AFTER <c>UseAuthentication()</c> and 
/// <c>UseAuthorization()</c> in the middleware pipeline.
/// </summary>
public class ActiveCentreMiddleware(
    RequestDelegate next,
    ILogger<ActiveCentreMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // Read the numeric centre id from the custom header
            if (context.Request.Headers.TryGetValue("X-Centre-ID", out var headerValue))
            {
                var raw = headerValue.ToString().Trim();

                // Validate it's a valid integer to prevent injection
                if (int.TryParse(raw, out var centreId) && centreId > 0)
                {
                    var identity = context.User.Identity as ClaimsIdentity;
                    identity?.AddClaim(new Claim("ActiveCentreId", centreId.ToString()));

                    logger.LogDebug(
                        "ActiveCentreMiddleware: user={User}, activeCentreId={CentreId}",
                        context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                        centreId);
                }
                else
                {
                    logger.LogWarning(
                        "ActiveCentreMiddleware: invalid X-Centre-ID header '{Val}' from user {User}",
                        raw,
                        context.User.FindFirstValue(ClaimTypes.NameIdentifier));
                }
            }
        }

        await next(context);
    }
}
