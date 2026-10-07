using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

[Authorize(Roles = Roles.Admin)]
public class UsersController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _me;

    public UsersController(AppDbContext db, IAuditService audit, ICurrentUser me)
    {
        _db = db;
        _audit = audit;
        _me = me;
    }

    public async Task<IActionResult> Index() => View(await _db.Users.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());

    public IActionResult Create() => View(new UserFormVm());

    [HttpPost]
    public async Task<IActionResult> Create(UserFormVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Password)) ModelState.AddModelError(nameof(vm.Password), "Set an initial password.");
        if (!Roles.All.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), "Choose a valid role.");
        if (await _db.Users.AnyAsync(u => u.Username == vm.Username)) ModelState.AddModelError(nameof(vm.Username), "This username is taken.");
        if (!ModelState.IsValid) return View(vm);
        var user = new AppUser
        {
            Username = vm.Username.Trim(), FullName = vm.FullName.Trim(), Email = vm.Email, Role = vm.Role,
            IsActive = vm.IsActive, PasswordHash = PasswordHasher.Hash(vm.Password!)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "User", user.Username, $"Role {user.Role}");
        Success($"User {user.Username} created.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null) return NotFound();
        return View(new UserFormVm { Id = u.Id, Username = u.Username, FullName = u.FullName, Email = u.Email, Role = u.Role, IsActive = u.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, UserFormVm vm)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null) return NotFound();
        ModelState.Remove(nameof(vm.Password));
        if (!Roles.All.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), "Choose a valid role.");
        if (await _db.Users.AnyAsync(x => x.Username == vm.Username && x.Id != id)) ModelState.AddModelError(nameof(vm.Username), "This username is taken.");
        if (id == _me.Id && (!vm.IsActive || vm.Role != Roles.Admin))
            ModelState.AddModelError(string.Empty, "You cannot deactivate yourself or remove your own administrator role.");
        if (!ModelState.IsValid) { vm.Id = id; return View(vm); }
        var changes = $"Role {u.Role}→{vm.Role}, active {u.IsActive}→{vm.IsActive}";
        u.Username = vm.Username.Trim(); u.FullName = vm.FullName.Trim(); u.Email = vm.Email; u.Role = vm.Role; u.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "User", u.Username, changes);
        Success($"User {u.Username} updated.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ResetPassword(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null) return NotFound();
        return View(new ResetPasswordVm { Id = u.Id, Username = u.Username });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var u = await _db.Users.FindAsync(vm.Id);
        if (u == null) return NotFound();
        u.PasswordHash = PasswordHasher.Hash(vm.NewPassword);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ResetPassword", "User", u.Username);
        Success($"Password for {u.Username} reset.");
        return RedirectToAction(nameof(Index));
    }
}
