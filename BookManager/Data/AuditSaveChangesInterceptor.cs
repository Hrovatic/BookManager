using BookManagerApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace BookManagerApi.Data
{
    public class AuditSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        // IMPORTANT: per-interceptor-instance guard (not AsyncLocal)
        private bool _isHandling = false;

        public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context == null)
                return await base.SavingChangesAsync(eventData, result, cancellationToken);

            // HARD GUARD: prevents double execution
            if (_isHandling)
                return await base.SavingChangesAsync(eventData, result, cancellationToken);

            // CRITICAL: don't audit audit logs
            var auditEntries = context.ChangeTracker.Entries<AuditLog>();
            if (auditEntries.Any())
                return await base.SavingChangesAsync(eventData, result, cancellationToken);

            _isHandling = true;

            try
            {
                var http = _httpContextAccessor.HttpContext;

                var userIdRaw = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int? userId = int.TryParse(userIdRaw, out var parsed) ? parsed : null;

                var username = http?.User?.Identity?.Name;
                var userRole = http?.User?.Claims?
                    .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

                var ip = http?.Connection.RemoteIpAddress?.ToString();
                var userAgent = http?.Request.Headers["User-Agent"].ToString();
                var requestPath = http?.Request.Path.Value;
                var httpMethod = http?.Request.Method;

                var entries = context.ChangeTracker.Entries()
                    .Where(e =>
                        e.Entity is not AuditLog &&
                        (e.State == EntityState.Added ||
                         e.State == EntityState.Modified ||
                         e.State == EntityState.Deleted))
                    .ToList();

                foreach (var entry in entries)
                {
                    var excludedProps = new HashSet<string>
                    {
                        "PasswordHash",
                        "Password",
                        "RefreshToken",
                        "Token",
                        "SecurityStamp"
                    };

                    var entityName = entry.Entity.GetType().Name;

                    var action = entry.State switch
                    {
                        EntityState.Added => "CREATE",
                        EntityState.Modified => "UPDATE",
                        EntityState.Deleted => "DELETE",
                        _ => "UNKNOWN"
                    };

                    var pk = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());

                    string? entityId = null;

                    if (pk is { IsTemporary: false })
                    {
                        var value = pk.CurrentValue;

                        if (value is int i && i == int.MinValue)
                            entityId = null;
                        else
                            entityId = value?.ToString();
                    }

                    string? oldValues = null;
                    string? newValues = null;

                    // Skip auditing preferred language changes
                    if (http?.Request.Path.StartsWithSegments("/Users/preferred-language") == true)
                    {
                        return await base.SavingChangesAsync(eventData, result, cancellationToken);
                    }
                    if (entry.State == EntityState.Added)
                    {
                        var dict = entry.Properties
                            .Where(p => !p.IsTemporary && !excludedProps.Contains(p.Metadata.Name))
                            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

                        newValues = JsonSerializer.Serialize(dict);
                    }

                    if (entry.State == EntityState.Deleted)
                    {
                        var dict = entry.Properties
                            .Where(p => !excludedProps.Contains(p.Metadata.Name))
                            .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);

                        oldValues = JsonSerializer.Serialize(dict);
                    }

                    if (entry.State == EntityState.Modified)
                    {
                        var oldDict = new Dictionary<string, object?>();
                        var newDict = new Dictionary<string, object?>();

                        foreach (var prop in entry.Properties)
                        {
                            if (excludedProps.Contains(prop.Metadata.Name)) continue;
                            if (prop.IsTemporary) continue;
                            if (prop.Metadata.IsPrimaryKey()) continue;

                            oldDict[prop.Metadata.Name] = prop.OriginalValue;
                            newDict[prop.Metadata.Name] = prop.CurrentValue;
                        }

                        oldValues = JsonSerializer.Serialize(oldDict);
                        newValues = JsonSerializer.Serialize(newDict);
                    }

                    var audit = new AuditLog
                    {
                        Action = action,
                        EntityName = entityName,
                        EntityId = entityId,

                        ControllerName = http?.GetEndpoint()?.DisplayName,
                        ActionName = http?.Request.RouteValues["action"]?.ToString(),

                        HttpMethod = httpMethod,
                        RequestPath = requestPath,

                        StatusCode = http?.Response?.StatusCode ?? 0,
                        Succeeded = true,

                        UserId = userId,
                        Username = username,
                        UserRole = userRole,

                        IpAddress = ip,
                        UserAgent = userAgent,

                        OldValues = oldValues,
                        NewValues = newValues,

                        CreatedAtUtc = DateTime.UtcNow
                    };

                    context.Set<AuditLog>().Add(audit);
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
            finally
            {
                _isHandling = false;
            }
        }
    }
}