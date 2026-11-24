using BookManagerApi.Data;
using BookManagerApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BookManagerApi.Services
{
    public interface IAuditLogService
    {
        Task LogAsync(AuditLog log);
        Task<List<AuditLog>> GetLatestAsync(int take = 100);
        Task<List<AuditLog>> GetByEntityAsync(string entityName, int take = 100);
    }

    public class AuditLogService : IAuditLogService
    {
        private readonly ApplicationDbContext _db;

        public AuditLogService(ApplicationDbContext db)
        {
            _db = db;
        }

        
        // CREATE LOG ENTRY
        
        public async Task LogAsync(AuditLog log)
        {
            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }

        
        // GET RECENT LOGS (Swagger / Admin UI)
        public async Task<List<AuditLog>> GetLatestAsync(int take = 100)
        {
            return await _db.AuditLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(take)
                .ToListAsync();
        }

        
        // FILTER BY ENTITY (Book, User, etc.)
        public async Task<List<AuditLog>> GetByEntityAsync(string entityName, int take = 100)
        {
            return await _db.AuditLogs
                .AsNoTracking()
                .Where(x => x.EntityName == entityName)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(take)
                .ToListAsync();
        }
    }
}