using System.Security.Claims;
using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[ApiController]
public abstract class ApiBaseController(AppDbContext db) : ControllerBase
{
    protected readonly AppDbContext Db = db;

    protected int? CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    protected async Task<User?> GetCurrentActiveUserAsync()
    {
        if (CurrentUserId is not int id) return null;
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return null;
        if (user.IsSuspended)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }
        return user;
    }

    protected void AddAuditLog(User actor, string action, string targetType, int? targetId, string details)
    {
        Db.AuditLogs.Add(new AuditLog
        {
            ActorId = actor.Id,
            ActorName = actor.DisplayName,
            ActorRole = actor.Role,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details.Length > 600 ? details[..600] : details,
            CreatedAt = DateTime.UtcNow
        });
    }

    protected static object MapUserSummary(User u) => new
    {
        u.Id,
        u.DisplayName,
        u.Email,
        u.Role,
        u.IsSuspended,
        u.SuspendReason,
        u.SellerStatus,
        u.ShopName,
        u.Phone,
        u.BankAccount,
        u.SellerBio,
        u.SellerRejectReason,
        u.SellerAppliedAt,
        u.CreatedAt
    };
}
