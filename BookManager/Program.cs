using BookManagerApi.Data;
using BookManagerApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using static BookManagerApi.Services.WishlistService;

var builder = WebApplication.CreateBuilder(args);

// ======================================================
// CONFIG (ENV vs Development)
// ======================================================
var connectionString = "";
var jwtKey = "";

if (builder.Environment.IsDevelopment())
{
    connectionString = builder.Configuration.GetConnectionString("ConnectionString1");
    jwtKey = builder.Configuration["Jwt:Key"];
}
else
{
    connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
    jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
}

// ======================================================
// SERVICES
// ======================================================

// Register HttpContextAccessor (needed for audit interceptor)
builder.Services.AddHttpContextAccessor();

// Register interceptor
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

// ======================================================
// DB CONTEXT (IMPORTANT PART)
// ======================================================
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();

    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention()
           .AddInterceptors(interceptor);

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});

// ======================================================
// APP SERVICES
// ======================================================
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<WishlistService>();
builder.Services.AddTransient<IUserService, UserService>();
builder.Services.AddTransient<IEmailService, EmailService>();
builder.Services.AddScoped<BorrowingService>();
builder.Services.AddHostedService<BorrowingOverdueWorker>();
builder.Services.AddScoped<ICopiesService, InventoryService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<ICenterService, CenterService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();

// ======================================================
// AUTH
// ======================================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey)
        )
    };
});

// ======================================================
// CORS
// ======================================================
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ======================================================
// STORAGE PATH
// ======================================================
string? coverStoragePath;

if (builder.Environment.IsDevelopment())
{
    coverStoragePath =
        builder.Configuration["PhotoStorage:RootPath"] ??
        builder.Configuration["CoverStoragePath"] ??
        Environment.GetEnvironmentVariable("APP_STORAGE_PATH");
}
else
{
    coverStoragePath =
        Environment.GetEnvironmentVariable("APP_STORAGE_PATH") ??
        "/mnt/app_storage/inventoryphotos";
}

// ======================================================
// BUILD APP
// ======================================================
var app = builder.Build();

// ======================================================
// DB INITIALIZATION 
// ======================================================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS book_availability_notifications (
            id SERIAL PRIMARY KEY,
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            book_id INTEGER NOT NULL REFERENCES books(id) ON DELETE CASCADE,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at_utc TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
            notified_at_utc TIMESTAMP WITHOUT TIME ZONE NULL
        );
    ");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE INDEX IF NOT EXISTS ix_book_availability_notifications_user_id_book_id_is_active
        ON book_availability_notifications(user_id, book_id, is_active);
    ");

    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE users ADD COLUMN IF NOT EXISTS preferred_language TEXT NOT NULL DEFAULT 'sl';");
    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE book_copies ADD COLUMN IF NOT EXISTS center_id INTEGER NULL;");
    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE book_copies ADD COLUMN IF NOT EXISTS created_at TIMESTAMP WITH TIME ZONE NULL;");
    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE wishlist_requests ADD COLUMN IF NOT EXISTS center_id INTEGER NULL;");
    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE wishlist_requests ADD COLUMN IF NOT EXISTS issn TEXT NULL;");
    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE wishlist_requests ADD COLUMN IF NOT EXISTS issn_volume TEXT NULL;");
    await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE wishlist_requests ADD COLUMN IF NOT EXISTS issn_number TEXT NULL;");

    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE book_copies
        ALTER COLUMN created_at SET DEFAULT (NOW() AT TIME ZONE 'UTC');
    ");

    await db.Database.ExecuteSqlRawAsync(@"
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'fk_book_copies_center_center_id'
            ) THEN
                ALTER TABLE book_copies
                ADD CONSTRAINT fk_book_copies_center_center_id
                FOREIGN KEY (center_id)
                REFERENCES center(id)
                ON DELETE SET NULL;
            END IF;
        END
        $$;
    ");

    await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ix_book_copies_center_id ON book_copies(center_id);");

    await db.Database.ExecuteSqlRawAsync(@"
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'fk_wishlist_requests_center_center_id'
            ) THEN
                ALTER TABLE wishlist_requests
                ADD CONSTRAINT fk_wishlist_requests_center_center_id
                FOREIGN KEY (center_id)
                REFERENCES center(id)
                ON DELETE SET NULL;
            END IF;
        END
        $$;
    ");

    await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ix_wishlist_requests_center_id ON wishlist_requests(center_id);");

    await db.Database.ExecuteSqlRawAsync(@"
        INSERT INTO categories (code, name, level, parent_code, is_active)
        VALUES ('10.00.00', 'Serijske publikacije', 1, NULL, TRUE)
        ON CONFLICT (code) DO NOTHING;
    ");
}

// ======================================================
// PIPELINE
// ======================================================
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

if (!string.IsNullOrWhiteSpace(coverStoragePath))
{
    Directory.CreateDirectory(coverStoragePath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(coverStoragePath),
        RequestPath = "/covers",
        OnPrepareResponse = context =>
        {
            var headers = context.Context.Response.Headers;
            headers[HeaderNames.CacheControl] = "public,max-age=31536000,immutable";
        }
    });
}

app.UseCors(x => x.AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowAnyOrigin());

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
