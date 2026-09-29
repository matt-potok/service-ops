using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceOps.Api.Identity;

namespace ServiceOps.Api.Features.Auth;

[ApiController]
[Route("api/v1/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<CsrfResponse> Csrf() =>
        new CsrfResponse(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return Problem(statusCode: 400, title: "Invalid request token", detail: "Refresh the page and try again.");
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
            return Problem(statusCode: 401, title: "Sign-in failed", detail: "Check your email and password, or try again later.");

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Problem(statusCode: 401, title: "Sign-in failed", detail: "Check your email and password, or try again later.");

        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SessionResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            await signIn.SignOutAsync();
            return Unauthorized();
        }
        return new SessionResponse(user.Id, user.DisplayName, user.Email!, (await users.GetRolesAsync(user)).ToArray());
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return Problem(statusCode: 400, title: "Invalid request token", detail: "Refresh the page and try again.");
        await signIn.SignOutAsync();
        return NoContent();
    }
}

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(256)] string Password);
public sealed record CsrfResponse(string Token);
public sealed record SessionResponse(Guid Id, string DisplayName, string Email, string[] Roles);
