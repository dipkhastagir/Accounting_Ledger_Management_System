using System.Security.Claims;

namespace TroyeeLedger.Services;

public interface ICurrentUser
{
    int Id { get; }
    string UserName { get; }
    string Role { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool CanEdit { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public CurrentUser(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int Id => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string UserName => IsAuthenticated ? Principal!.Identity!.Name ?? "unknown" : "system";

    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? "";

    public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public bool CanEdit => IsInRole("Admin") || IsInRole("Accountant");
}
