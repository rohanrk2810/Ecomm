using System.ComponentModel.DataAnnotations;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace Commerce.Identity.Api.Pages.Account;

[EnableRateLimiting("registration")]
public sealed class MfaModel(MfaEnrollment enrollment) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [BindProperty, Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    [BindProperty, StringLength(32)] public string? Code { get; set; }
    public string? Key { get; private set; }
    public string[]? RecoveryCodes { get; private set; }
    public string? Error { get; private set; }
    public void OnGet() { }
    public Task<IActionResult> OnPostBeginAsync(CancellationToken ct) => Submit(false, ct);
    public Task<IActionResult> OnPostConfirmAsync(CancellationToken ct) => Submit(true, ct);
    private async Task<IActionResult> Submit(bool confirm, CancellationToken ct)
    {
        var result = ModelState.IsValid ? await enrollment.ExecuteAsync(Email, Password, Code, confirm, HttpContext.TraceIdentifier, ct) : null;
        Password = string.Empty; Code = null; ModelState.Remove(nameof(Password)); ModelState.Remove(nameof(Code));
        if (result is null) { Error = "Unable to continue. Check your credentials, code and account availability, or restart setup if it expired."; Response.StatusCode = 400; }
        else
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
            Key = result.Key; RecoveryCodes = result.RecoveryCodes;
        }
        return Page();
    }
}
