using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class AccountController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _user;

    public AccountController(AppDbContext db, IAuditService audit, ICurrentUser user)
    {
        _db = db;
        _audit = audit;
        _user = user;
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == vm.Username.Trim());
        if (user == null || !user.IsActive || !PasswordHasher.Verify(vm.Password, user.PasswordHash))
        {
            await _audit.LogAsync("LoginFailed", "User", vm.Username, "Wrong credentials or inactive account");
            ModelState.AddModelError(string.Empty, "Incorrect username or password, or the account is inactive.");
            return View(vm);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("FullName", user.FullName)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = vm.RememberMe });

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Login", "User", user.Username, $"Signed in as {user.Role}");

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Logout", "User", _user.UserName);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    public async Task<IActionResult> Profile()
    {
        var user = await _db.Users.FirstAsync(u => u.Id == _user.Id);
        ViewBag.Created = await _db.Vouchers.CountAsync(v => v.CreatedById == user.Id);
        ViewBag.Approved = await _db.Vouchers.CountAsync(v => v.ApprovedById == user.Id && v.ApprovedById != v.CreatedById);
        ViewBag.Activity = await _db.AuditLogs.Where(a => a.UserName == user.Username).OrderByDescending(a => a.Id).Take(15).ToListAsync();
        return View(user);
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await _db.Users.FirstAsync(u => u.Id == _user.Id);
        if (!PasswordHasher.Verify(vm.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(vm.CurrentPassword), "The current password is incorrect.");
            return View(vm);
        }
        user.PasswordHash = PasswordHasher.Hash(vm.NewPassword);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ChangePassword", "User", user.Username);
        Success("Password changed.");
        return RedirectToAction(nameof(Profile));
    }
}
